using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lotus.Core.Backup;

namespace Lotus.Infrastructure.Backup;

// The player's data in one zip (.lotusbackup) with a manifest. Only known player files go in or come out: caches,
// logs, tools and the Windows-changes journal (it describes this PC) are never part of a backup.
public sealed class LotusBackupArchive(string? root = null, string appVersion = "") : IBackupArchive
{
    // resume-notes.json, save-protection.json (settings only, never the save files) and game-prefs.json are new in 0.6;
    // older backups simply do not have them and still restore.
    private static readonly string[] Files = ["preferences.json", "player.json", "diary.json", "achievements.json", "games-hidden.json", "score-history.json", "benchmarks.db", "resume-notes.json", "save-protection.json", "game-prefs.json"];
    private static readonly string[] Folders = ["Profiles", "Library", "Overlays", "Profile", "Benchmarks"];
    private const string ManifestName = "manifest.json", AvatarPrefix = "external/avatar";
    // Save protection: the newest valid copy of each game's saves travels in the backup (SaveBackups/<game>/<id>/...).
    // Restoring puts it back among the game's copies; the game's own save folder is only changed from 'Backup de saves'.
    private const string SavesFolder = "SaveBackups";
    private readonly string root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus");

    public string FileExtension => ".lotusbackup";
    public string BackupFolder => Path.Combine(root, "Backups");
    private string PendingFile => Path.Combine(root, "pending-restore.lotusbackup");
    public bool HasPending => File.Exists(PendingFile);

    // Relative paths (with '/') of the player's files that exist now.
    private List<string> CurrentFiles()
    {
        var list = Files.Where(f => File.Exists(Path.Combine(root, f))).ToList();
        foreach (var folder in Folders)
        {
            var dir = Path.Combine(root, folder);
            if (!Directory.Exists(dir)) continue;
            list.AddRange(Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')));
        }
        return list;
    }

    // The newest verified copy of each protected game (by its manifest date); unfinished or damaged copies never go in.
    private List<string> LatestSaves()
    {
        var list = new List<string>();
        var saves = Path.Combine(root, SavesFolder);
        if (!Directory.Exists(saves)) return list;
        foreach (var game in Directory.EnumerateDirectories(saves))
        {
            string? best = null; var bestDate = DateTimeOffset.MinValue;
            foreach (var snap in Directory.EnumerateDirectories(game).Where(d => !d.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var node = JsonNode.Parse(File.ReadAllText(Path.Combine(snap, ManifestName)));
                    if (node?["Validation"]?.GetValue<string>() != "valid" || node["Created"]?.GetValue<DateTimeOffset>() is not { } created) continue;
                    if (created > bestDate) { bestDate = created; best = snap; }
                }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or FormatException or UnauthorizedAccessException) { }
            }
            if (best is null) continue;
            list.AddRange(Directory.EnumerateFiles(best, "*", SearchOption.AllDirectories)
                .Where(f => new FileInfo(f).LinkTarget is null)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                .Where(Allowed));
        }
        return list;
    }

    // Save copies from a backup join the game's copies; a copy that is already here is left as it is.
    private void RestoreSaves(ZipArchive zip)
    {
        foreach (var group in zip.Entries.Where(e => !e.FullName.EndsWith('/') && Allowed(e.FullName) && e.FullName.StartsWith(SavesFolder + "/", StringComparison.OrdinalIgnoreCase))
                     .GroupBy(e => string.Join('/', e.FullName.Split('/').Take(3)), StringComparer.OrdinalIgnoreCase))
        {
            var final = Path.GetFullPath(Path.Combine(root, group.Key.Replace('/', Path.DirectorySeparatorChar)));
            if (!final.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Directory.Exists(final)) continue;
            var partial = final + ".partial";
            try
            {
                if (Directory.Exists(partial)) Directory.Delete(partial, true);
                foreach (var entry in group)
                {
                    var rel = entry.FullName[(group.Key.Length + 1)..];
                    var target = Path.GetFullPath(Path.Combine(partial, rel.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(partial + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                }
                if (File.Exists(Path.Combine(partial, ManifestName))) Directory.Move(partial, final); // complete copies only
            }
            catch (IOException) { }
            finally { try { if (Directory.Exists(partial)) Directory.Delete(partial, true); } catch (IOException) { } }
        }
    }

    // Only paths a backup may contain; anything else (or "..") is refused.
    public static bool Allowed(string path)
    {
        if (path.Contains("..") || path.Contains(':') || path.StartsWith('/') || path.Contains('\\')) return false;
        if (Files.Contains(path, StringComparer.OrdinalIgnoreCase)) return true;
        if (path.StartsWith(AvatarPrefix, StringComparison.OrdinalIgnoreCase) && !path[AvatarPrefix.Length..].Contains('/')) return true;
        var parts = path.Split('/');
        if (string.Equals(parts[0], SavesFolder, StringComparison.OrdinalIgnoreCase))
            return parts.Length >= 4 && parts.All(p => p.Length > 0 && p != ".") && !parts[2].EndsWith(".partial", StringComparison.OrdinalIgnoreCase)
                && (parts.Length == 4 && parts[3] == ManifestName || parts.Length > 4 && parts[3] == "files");
        return path.Contains('/') && Folders.Contains(parts[0], StringComparer.OrdinalIgnoreCase);
    }

    public BackupManifest Write(Stream output) => Write(output, includeSaves: true);

    // Safety and automatic copies stay small: the save copies are already on this PC.
    private BackupManifest Write(Stream output, bool includeSaves)
    {
        var files = CurrentFiles();
        if (includeSaves) files.AddRange(LatestSaves());
        var avatar = AvatarOutsideRoot();
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var stored = new List<string>();
        foreach (var rel in files)
        {
            try { zip.CreateEntryFromFile(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)), rel, CompressionLevel.Optimal); stored.Add(rel); }
            catch (IOException) { } // a file in use (rare): the rest of the backup is still useful
        }
        string? avatarEntry = null;
        if (avatar is not null)
        {
            avatarEntry = AvatarPrefix + Path.GetExtension(avatar).ToLowerInvariant();
            try { zip.CreateEntryFromFile(avatar, avatarEntry); stored.Add(avatarEntry); } catch (IOException) { avatarEntry = null; }
        }
        var manifest = new BackupManifest(BackupManifest.CurrentFormat, appVersion, DateTimeOffset.Now, Environment.MachineName, stored, Summarize(rel => File.Exists(Path.Combine(root, rel)) ? File.ReadAllText(Path.Combine(root, rel)) : null, stored), avatarEntry is null ? null : avatar);
        using (var w = new StreamWriter(zip.CreateEntry(ManifestName).Open()))
            w.Write(JsonSerializer.Serialize(manifest, BackupJson.Default.BackupManifest));
        return manifest;
    }

    public BackupManifest Inspect(Stream input)
    {
        using var zip = Open(input);
        return Read(zip);
    }

    public void Stage(Stream input)
    {
        var tmp = PendingFile + ".tmp";
        Directory.CreateDirectory(root);
        using (var file = File.Create(tmp)) input.CopyTo(file);
        using (var check = File.OpenRead(tmp)) Inspect(check); // refuse anything that is not a valid backup
        File.Move(tmp, PendingFile, overwrite: true);
    }

    public BackupManifest? ApplyPending()
    {
        if (!HasPending) return null;
        try
        {
            using var input = File.OpenRead(PendingFile);
            using var zip = Open(input);
            var manifest = Read(zip);
            SafetyCopy("before-restore");
            foreach (var entry in zip.Entries.Where(e => e.FullName != ManifestName && e.Length >= 0 && !e.FullName.EndsWith('/')))
            {
                if (!Allowed(entry.FullName) || entry.FullName.StartsWith(AvatarPrefix, StringComparison.OrdinalIgnoreCase) || entry.FullName.StartsWith(SavesFolder + "/", StringComparison.OrdinalIgnoreCase)) continue;
                var target = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var tmp = target + ".tmp";
                entry.ExtractToFile(tmp, overwrite: true);
                File.Move(tmp, target, overwrite: true);
            }
            RestoreAvatar(zip, manifest);
            RestoreSaves(zip);
            return manifest;
        }
        finally
        {
            try { File.Delete(PendingFile); } catch (IOException) { }
        }
    }

    // "Restore Lotus" (start from zero): asked in Settings, applied at the next start before anything loads.
    // A safety copy is made first, then the player's data moves to Reset\<date> (nothing is deleted). The history of
    // Windows changes (Optimizer), backups, logs and caches stay, so changes made earlier can still be undone.
    private string ResetFile => Path.Combine(root, "pending-reset");
    public bool HasPendingReset => File.Exists(ResetFile);
    public void StageReset() { Directory.CreateDirectory(root); File.WriteAllText(ResetFile, DateTimeOffset.Now.ToString("o")); }
    public string? ApplyPendingReset()
    {
        if (!HasPendingReset) return null;
        try
        {
            if (CurrentFiles().Count > 0) SafetyCopy("before-reset");
            var dest = Path.Combine(root, "Reset", DateTime.Now.ToString("yyyy-MM-dd-HHmmss"));
            Directory.CreateDirectory(dest);
            foreach (var name in Files.SelectMany(f => new[] { f, f + ".bak" }))
                if (File.Exists(Path.Combine(root, name))) File.Move(Path.Combine(root, name), Path.Combine(dest, name));
            foreach (var name in Folders)
                if (Directory.Exists(Path.Combine(root, name))) Directory.Move(Path.Combine(root, name), Path.Combine(dest, name));
            return dest;
        }
        finally
        {
            try { File.Delete(ResetFile); } catch (IOException) { }
        }
    }

    // A dated copy in Backups when the last one is older than `every`; keeps the newest `keep` copies.
    public string? AutoBackup(TimeSpan every, int keep)
    {
        if (CurrentFiles().Count == 0) return null;
        Directory.CreateDirectory(BackupFolder);
        var autos = Directory.GetFiles(BackupFolder, "auto-*" + FileExtension).OrderByDescending(File.GetLastWriteTimeUtc).ToList();
        if (autos.Count > 0 && DateTime.UtcNow - File.GetLastWriteTimeUtc(autos[0]) < every) return null;
        var path = SafetyCopy("auto");
        foreach (var old in Directory.GetFiles(BackupFolder, "auto-*" + FileExtension).OrderByDescending(File.GetLastWriteTimeUtc).Skip(keep))
            try { File.Delete(old); } catch (IOException) { }
        return path;
    }

    private string SafetyCopy(string kind)
    {
        Directory.CreateDirectory(BackupFolder);
        var path = Path.Combine(BackupFolder, $"{kind}-{DateTime.Now:yyyy-MM-dd-HHmmss}{FileExtension}");
        using (var file = File.Create(path)) Write(file, includeSaves: false);
        foreach (var old in Directory.GetFiles(BackupFolder, "before-restore-*" + FileExtension).OrderByDescending(File.GetLastWriteTimeUtc).Skip(5))
            try { File.Delete(old); } catch (IOException) { }
        return path;
    }

    private static ZipArchive Open(Stream input)
    {
        try { return new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true); }
        catch (InvalidDataException) { throw new BackupFormatException("not a zip"); }
    }

    private static BackupManifest Read(ZipArchive zip)
    {
        var entry = zip.GetEntry(ManifestName) ?? throw new BackupFormatException("no manifest");
        using var reader = new StreamReader(entry.Open());
        BackupManifest? manifest;
        try { manifest = JsonSerializer.Deserialize(reader.ReadToEnd(), BackupJson.Default.BackupManifest); }
        catch (JsonException) { throw new BackupFormatException("bad manifest"); }
        if (manifest is null || manifest.Format < 1) throw new BackupFormatException("bad manifest");
        if (manifest.Format > BackupManifest.CurrentFormat) throw new BackupFormatException("newer format");
        return manifest;
    }

    // A picture outside Lotus (e.g. Steam's avatar cache) travels inside the backup; on restore it goes back where it
    // was, or into Lotus\Profile when that place no longer exists, and the preference is pointed at it.
    private string? AvatarOutsideRoot()
    {
        try
        {
            var prefs = Path.Combine(root, "preferences.json");
            if (!File.Exists(prefs) || JsonNode.Parse(File.ReadAllText(prefs))?["AvatarPath"]?.GetValue<string>() is not { Length: > 0 } a) return null;
            var full = Path.GetFullPath(a);
            return File.Exists(full) && !full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException or ArgumentException or NotSupportedException) { return null; }
    }

    private void RestoreAvatar(ZipArchive zip, BackupManifest manifest)
    {
        var entry = zip.Entries.FirstOrDefault(e => e.FullName.StartsWith(AvatarPrefix, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return;
        if (manifest.AvatarOriginal is { } original && File.Exists(original)) return; // still there: nothing to do
        var target = Path.Combine(root, "Profile", "avatar" + Path.GetExtension(entry.FullName));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        entry.ExtractToFile(target, overwrite: true);
        var prefs = Path.Combine(root, "preferences.json");
        if (!File.Exists(prefs)) return;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(prefs)) is JsonObject node)
            {
                node["AvatarPath"] = target;
                File.WriteAllText(prefs, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch (JsonException) { }
    }

    // Numbers for the restore preview, read from the stored files.
    private static BackupSummary Summarize(Func<string, string?> read, IReadOnlyList<string> files)
    {
        static JsonNode? Parse(string? s) { try { return s is null ? null : JsonNode.Parse(s); } catch (JsonException) { return null; } }
        var prefs = Parse(read("preferences.json"));
        var diary = Parse(read("diary.json"));
        var ach = Parse(read("achievements.json"));
        var player = Parse(read("player.json"));
        return new(prefs?["DisplayName"]?.GetValue<string>() ?? "",
            (diary?["Entries"] as JsonArray)?.Count ?? 0,
            (ach?["Earned"] as JsonObject)?.Count ?? 0,
            (player?["Favorites"] as JsonArray)?.Count ?? 0,
            files.Count(f => f.StartsWith("Benchmarks/", StringComparison.OrdinalIgnoreCase) && f.EndsWith("result.json", StringComparison.OrdinalIgnoreCase)),
            files.Where(f => f.StartsWith(SavesFolder + "/", StringComparison.OrdinalIgnoreCase)).Select(f => f.Split('/')[1]).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(BackupManifest))]
internal sealed partial class BackupJson : System.Text.Json.Serialization.JsonSerializerContext { }
