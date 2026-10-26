using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lotus.Core.Backup;

namespace Lotus.Infrastructure.Backup;

// The player's data in one zip (.lotusbackup) with a manifest. Only known player files go in or come out: caches,
// logs, tools and the Windows-changes journal (it describes this PC) are never part of a backup.
public sealed class LotusBackupArchive(string? root = null, string appVersion = "") : IBackupArchive
{
    private static readonly string[] Files = ["preferences.json", "player.json", "diary.json", "achievements.json", "games-hidden.json", "score-history.json", "benchmarks.db"];
    private static readonly string[] Folders = ["Profiles", "Library", "Overlays", "Profile", "Benchmarks"];
    private const string ManifestName = "manifest.json", AvatarPrefix = "external/avatar";
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

    // Only paths a backup may contain; anything else (or "..") is refused.
    public static bool Allowed(string path)
    {
        if (path.Contains("..") || path.Contains(':') || path.StartsWith('/') || path.Contains('\\')) return false;
        if (Files.Contains(path, StringComparer.OrdinalIgnoreCase)) return true;
        if (path.StartsWith(AvatarPrefix, StringComparison.OrdinalIgnoreCase) && !path[AvatarPrefix.Length..].Contains('/')) return true;
        var first = path.Split('/')[0];
        return path.Contains('/') && Folders.Contains(first, StringComparer.OrdinalIgnoreCase);
    }

    public BackupManifest Write(Stream output)
    {
        var files = CurrentFiles();
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
                if (!Allowed(entry.FullName) || entry.FullName.StartsWith(AvatarPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                var target = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var tmp = target + ".tmp";
                entry.ExtractToFile(tmp, overwrite: true);
                File.Move(tmp, target, overwrite: true);
            }
            RestoreAvatar(zip, manifest);
            return manifest;
        }
        finally
        {
            try { File.Delete(PendingFile); } catch (IOException) { }
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
        using (var file = File.Create(path)) Write(file);
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
            files.Count(f => f.StartsWith("Benchmarks/", StringComparison.OrdinalIgnoreCase) && f.EndsWith("result.json", StringComparison.OrdinalIgnoreCase)));
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(BackupManifest))]
internal sealed partial class BackupJson : System.Text.Json.Serialization.JsonSerializerContext { }
