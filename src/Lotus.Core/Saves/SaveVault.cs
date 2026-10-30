using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lotus.Core.Saves;

// Local, verified snapshots of a game's save folder, kept apart from the rest of Lotus's data:
//   <vault>\<game folder>\<snapshot id>\manifest.json + files\...
// A snapshot is written to "<id>.partial", every copied file is hashed (SHA-256) against the source, then re-read and
// checked, and only then renamed to its final name: a snapshot either exists complete and valid, or not at all.
// Links (symlinks, junctions) are never followed; cloud-only placeholders are never downloaded; relative paths are
// validated on the way in and on the way out (no path traversal); a valid version is never deleted before the new one
// is confirmed. Restores keep a copy of the current state first and roll back on any failure.
public sealed record SaveFile(string Path, long Size, DateTime LastWriteUtc, string Sha256);
public sealed record SnapshotManifest(
    int Format, string GameKey, string GameName, string Id, DateTimeOffset Created, string Origin, string SourceRoot,
    long TotalBytes, IReadOnlyList<SaveFile> Files, string Validation, int SkippedLinks = 0, int SkippedCloudOnly = 0)
{
    public const int CurrentFormat = 1;
    public const string Auto = "auto", Manual = "manual", BeforeRestore = "before-restore";
    public bool IsValid => Validation == "valid";
}

public enum SnapshotStatus { Created, Unchanged, Failed }
public enum SaveFailure { None, SourceMissing, Empty, NoSpace, Locked, Changing, Unreadable, VerifyFailed, Cancelled, GameRunning, InvalidSnapshot, UnsafePath, RolledBack }
public sealed record SnapshotResult(SnapshotStatus Status, SnapshotManifest? Manifest, SaveFailure Failure = SaveFailure.None, string? Detail = null);
public sealed record RestorePlan(SnapshotManifest Snapshot, string Destination, IReadOnlyList<string> Replace, IReadOnlyList<string> Create, IReadOnlyList<string> KeepUntouched);
public sealed record RestoreResult(bool Restored, SaveFailure Failure = SaveFailure.None, string? SafetySnapshotId = null, string? Detail = null);
public sealed record ScanResult(IReadOnlyList<(string Rel, FileInfo File)> Files, long TotalBytes, int SkippedLinks, int SkippedCloudOnly);

public sealed class SaveVault
{
    private readonly string root;
    public string Root => root;
    // Test seams (and the app's free-space check): never used to change behavior in production.
    public Func<string, long?> FreeSpace { get; set; } = DefaultFreeSpace;
    public Action<string>? BeforeCopyFile { get; set; }
    public Action<string>? BeforeReplaceFile { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(2);
    public const long SpaceMargin = 100L * 1024 * 1024;

    public SaveVault(string root) => this.root = root;

    public static string FolderFor(string gameKey)
    {
        var safe = string.Concat(gameKey.Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_'));
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(gameKey.ToLowerInvariant())))[..8];
        return (safe.Length > 40 ? safe[..40] : safe) + "-" + hash.ToLowerInvariant(); // two games with similar keys never share a folder
    }
    private string GameDir(string gameKey) => Path.Combine(root, FolderFor(gameKey));

    // ---------- Paths ----------
    // A relative path stored in a manifest: forward slashes, no root, no "." or "..", no device names, no ADS.
    public static bool IsSafeRelative(string rel)
    {
        if (string.IsNullOrWhiteSpace(rel) || rel.Length > 400 || Path.IsPathRooted(rel) || rel.Contains('\\') || rel.Contains(':') || rel.StartsWith('/')) return false;
        foreach (var part in rel.Split('/'))
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.')) return false;
            if (part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            var stem = part.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3]))) return false;
        }
        return true;
    }
    // Joins a safe relative path to a folder and confirms the result stays inside it.
    public static string? Inside(string folder, string rel)
    {
        if (!IsSafeRelative(rel)) return null;
        var baseFull = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(baseFull, rel.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    // ---------- Scan ----------
    private const FileAttributes RecallOnOpen = (FileAttributes)0x40000, RecallOnDataAccess = (FileAttributes)0x400000;
    public static bool IsLink(FileSystemInfo info) => info.LinkTarget is not null;
    public static bool IsCloudOnly(FileSystemInfo info) => (info.Attributes & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0;

    // Every regular file under the root; links are not followed and cloud-only placeholders are not opened.
    public static ScanResult Scan(string sourceRoot)
    {
        var files = new List<(string, FileInfo)>(); long total = 0; int links = 0, cloud = 0;
        var pending = new Stack<(DirectoryInfo Dir, string Rel)>();
        pending.Push((new DirectoryInfo(sourceRoot), ""));
        while (pending.Count > 0)
        {
            var (dir, relDir) = pending.Pop();
            IEnumerable<FileSystemInfo> entries;
            try { entries = dir.EnumerateFileSystemInfos().ToList(); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { continue; } // an unreadable folder is skipped, never guessed
            foreach (var e in entries)
            {
                if (IsLink(e)) { links++; continue; }
                var rel = relDir.Length == 0 ? e.Name : relDir + "/" + e.Name;
                if (e is DirectoryInfo d) { pending.Push((d, rel)); continue; }
                if (e is not FileInfo f) continue;
                if (IsCloudOnly(f)) { cloud++; continue; }
                if (!IsSafeRelative(rel)) continue;
                files.Add((rel, f)); total += f.Length;
            }
        }
        files.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        return new(files, total, links, cloud);
    }

    // ---------- Snapshots ----------
    public IReadOnlyList<SnapshotManifest> List(string gameKey)
    {
        var dir = GameDir(gameKey);
        if (!Directory.Exists(dir)) return [];
        var list = new List<SnapshotManifest>();
        foreach (var d in Directory.EnumerateDirectories(dir))
        {
            var name = Path.GetFileName(d);
            if (name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)) { TryDeleteStale(d); continue; } // an interrupted snapshot is never shown
            if (ReadManifest(d) is { } m && string.Equals(m.GameKey, gameKey, StringComparison.OrdinalIgnoreCase)) list.Add(m);
        }
        return [.. list.OrderByDescending(m => m.Created)];
    }
    public long SizeOf(string gameKey) => List(gameKey).Sum(m => m.TotalBytes);

    private static SnapshotManifest? ReadManifest(string snapshotDir)
    {
        try
        {
            var file = Path.Combine(snapshotDir, "manifest.json");
            if (!File.Exists(file)) return null;
            var m = JsonSerializer.Deserialize(File.ReadAllText(file), VaultJson.Default.SnapshotManifest);
            if (m is null || m.Format > SnapshotManifest.CurrentFormat || m.Files is null) return null;
            if (m.Files.Any(f => !IsSafeRelative(f.Path))) return m with { Validation = "unsafe" }; // a tampered manifest is never restored
            return m;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public async Task<SnapshotResult> CreateAsync(string gameKey, string gameName, string sourceRoot, string origin, int keep, CancellationToken token = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            var result = await Task.Run(() => CreateOnce(gameKey, gameName, sourceRoot, origin, keep, token), token);
            if (result.Failure is not (SaveFailure.Changing or SaveFailure.Locked) || attempt >= MaxAttempts) return result;
            await Task.Delay(RetryDelay, token); // files were being written: wait and try again
        }
    }

    private SnapshotResult CreateOnce(string gameKey, string gameName, string sourceRoot, string origin, int keep, CancellationToken token)
    {
        if (!Directory.Exists(sourceRoot)) return new(SnapshotStatus.Failed, null, SaveFailure.SourceMissing);
        var scan = Scan(sourceRoot);
        if (scan.Files.Count == 0) return new(SnapshotStatus.Failed, null, SaveFailure.Empty);

        // 1) Read and hash the source (shared read: the game is closed, but a launcher may still hold a handle).
        var before = new List<SaveFile>(scan.Files.Count);
        foreach (var (rel, f) in scan.Files)
        {
            token.ThrowIfCancellationRequested();
            try { f.Refresh(); before.Add(new(rel, f.Length, f.LastWriteTimeUtc, HashFile(f.FullName))); }
            catch (FileNotFoundException) { return new(SnapshotStatus.Failed, null, SaveFailure.Changing, rel); }
            catch (IOException) { return new(SnapshotStatus.Failed, null, SaveFailure.Locked, rel); }
            catch (UnauthorizedAccessException) { return new(SnapshotStatus.Failed, null, SaveFailure.Unreadable, rel); }
        }

        // 2) Nothing changed since the latest valid snapshot of this origin family: no duplicate.
        var latest = List(gameKey).FirstOrDefault(m => m.IsValid && m.Origin != SnapshotManifest.BeforeRestore);
        if (latest is not null && SameContent(latest.Files, before) && origin != SnapshotManifest.BeforeRestore)
            return new(SnapshotStatus.Unchanged, latest);

        // 3) Room for it (with a margin), on the vault's drive.
        var gameDir = GameDir(gameKey);
        Directory.CreateDirectory(gameDir);
        if (FreeSpace(gameDir) is { } free && free < scan.TotalBytes + scan.TotalBytes / 10 + SpaceMargin)
            return new(SnapshotStatus.Failed, null, SaveFailure.NoSpace);

        var id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..6] + "-" + origin; // unique even within the same millisecond
        var partial = Path.Combine(gameDir, id + ".partial");
        try
        {
            Directory.CreateDirectory(Path.Combine(partial, "files"));
            // 4) Copy with hashing; the source must still be what was hashed in step 1.
            foreach (var expected in before)
            {
                token.ThrowIfCancellationRequested();
                BeforeCopyFile?.Invoke(expected.Path);
                var src = Path.Combine(sourceRoot, expected.Path.Replace('/', Path.DirectorySeparatorChar));
                var dst = Inside(Path.Combine(partial, "files"), expected.Path) ?? throw new InvalidDataException("unsafe path");
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                string copied;
                try { copied = CopyHashing(src, dst); }
                catch (FileNotFoundException) { return Fail(partial, SaveFailure.Changing, expected.Path); }
                catch (IOException ex) when (!IsDiskFull(ex)) { return Fail(partial, SaveFailure.Locked, expected.Path); }
                var now = new FileInfo(src);
                if (copied != expected.Sha256 || !now.Exists || now.Length != expected.Size || now.LastWriteTimeUtc != expected.LastWriteUtc)
                    return Fail(partial, SaveFailure.Changing, expected.Path);
                File.SetLastWriteTimeUtc(dst, expected.LastWriteUtc);
            }
            // 5) Re-read what was written.
            foreach (var expected in before)
                if (HashFile(Inside(Path.Combine(partial, "files"), expected.Path)!) != expected.Sha256) return Fail(partial, SaveFailure.VerifyFailed, expected.Path);
            // 6) Manifest, then the atomic rename that makes the snapshot real.
            var manifest = new SnapshotManifest(SnapshotManifest.CurrentFormat, gameKey, gameName, id, DateTimeOffset.Now, origin, sourceRoot,
                before.Sum(f => f.Size), before, "valid", scan.SkippedLinks, scan.SkippedCloudOnly);
            var tmp = Path.Combine(partial, "manifest.json.tmp");
            File.WriteAllText(tmp, JsonSerializer.Serialize(manifest, VaultJson.Default.SnapshotManifest));
            File.Move(tmp, Path.Combine(partial, "manifest.json"));
            var final = Path.Combine(gameDir, id);
            Directory.Move(partial, final);
            // 7) Only now, older versions beyond the limit go (never the one just made).
            Prune(gameKey, origin, keep, except: id);
            return new(SnapshotStatus.Created, manifest);
        }
        catch (OperationCanceledException) { TryDelete(partial); return new(SnapshotStatus.Failed, null, SaveFailure.Cancelled); }
        catch (IOException ex) when (IsDiskFull(ex)) { TryDelete(partial); return new(SnapshotStatus.Failed, null, SaveFailure.NoSpace); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { TryDelete(partial); return new(SnapshotStatus.Failed, null, SaveFailure.Unreadable, ex.GetType().Name); }
    }

    private static SnapshotResult Fail(string partial, SaveFailure failure, string? detail) { TryDelete(partial); return new(SnapshotStatus.Failed, null, failure, detail); }
    private static bool SameContent(IReadOnlyList<SaveFile> a, IReadOnlyList<SaveFile> b) =>
        a.Count == b.Count && a.OrderBy(f => f.Path, StringComparer.Ordinal).Zip(b.OrderBy(f => f.Path, StringComparer.Ordinal)).All(p => p.First.Path == p.Second.Path && p.First.Sha256 == p.Second.Sha256);

    // Keeps the newest `keep` valid snapshots of this origin; invalid ones of the same origin go first.
    public void Prune(string gameKey, string origin, int keep, string? except = null)
    {
        var mine = List(gameKey).Where(m => m.Origin == origin && m.Id != except).ToList();
        var keepCount = Math.Max(1, keep) - (except is null ? 0 : 1);
        foreach (var old in mine.Where(m => !m.IsValid).Concat(mine.Where(m => m.IsValid).Skip(Math.Max(0, keepCount))))
            TryDelete(Path.Combine(GameDir(gameKey), old.Id));
    }

    public bool Delete(string gameKey, string id)
    {
        if (!IsSafeRelative(id) || id.Contains('/')) return false;
        var dir = Path.Combine(GameDir(gameKey), id);
        if (!Directory.Exists(dir)) return false;
        TryDelete(dir); return !Directory.Exists(dir);
    }

    // Re-reads every file of a snapshot against its manifest.
    public bool Verify(string gameKey, string id)
    {
        var dir = Path.Combine(GameDir(gameKey), id);
        if (ReadManifest(dir) is not { IsValid: true } m) return false;
        foreach (var f in m.Files)
        {
            var path = Inside(Path.Combine(dir, "files"), f.Path);
            if (path is null || !File.Exists(path)) return false;
            try { if (new FileInfo(path).Length != f.Size || HashFile(path) != f.Sha256) return false; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        }
        return true;
    }

    // ---------- Restore ----------
    public RestorePlan? Plan(string gameKey, string id, string destination)
    {
        if (ReadManifest(Path.Combine(GameDir(gameKey), id)) is not { IsValid: true } m) return null;
        var replace = new List<string>(); var create = new List<string>();
        foreach (var f in m.Files)
        {
            var target = Inside(destination, f.Path);
            if (target is null) return null;
            (File.Exists(target) ? replace : create).Add(f.Path);
        }
        var inSnapshot = m.Files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keep = Directory.Exists(destination) ? Scan(destination).Files.Select(f => f.Rel).Where(r => !inSnapshot.Contains(r)).ToList() : [];
        return new(m, destination, replace, create, keep);
    }

    private string JournalFile(string gameKey) => Path.Combine(GameDir(gameKey), "restore-in-progress.json");
    public bool RestoreInterrupted(string gameKey) => File.Exists(JournalFile(gameKey));
    public RestoreJournal? Interrupted(string gameKey)
    {
        try { return File.Exists(JournalFile(gameKey)) ? JsonSerializer.Deserialize(File.ReadAllText(JournalFile(gameKey)), VaultJson.Default.RestoreJournal) : null; }
        catch (Exception ex) when (ex is IOException or JsonException) { return null; }
    }
    // Finishes an interrupted restore by putting the state from before it back.
    public bool RecoverInterrupted(string gameKey)
    {
        if (Interrupted(gameKey) is not { } j) return false;
        if (ReadManifest(Path.Combine(GameDir(gameKey), j.SnapshotId)) is { } m) CleanTemps(j.Destination, m.Files.Select(f => f.Path));
        return RollBack(gameKey, j.SafetyId, j.Destination, j.Created);
    }
    private static void CleanTemps(string destination, IEnumerable<string> rels)
    {
        foreach (var rel in rels) if (Inside(destination, rel) is { } path) TryDeleteFile(path + ".lotus-restore");
    }

    // The game must be closed. A copy of the current state is made first; any failure puts it back.
    public async Task<RestoreResult> RestoreAsync(string gameKey, string gameName, string id, string destination, Func<bool> gameRunning, int keepSafety, CancellationToken token = default)
    {
        if (!Verify(gameKey, id)) return new(false, SaveFailure.InvalidSnapshot);
        if (Plan(gameKey, id, destination) is not { } plan) return new(false, SaveFailure.UnsafePath);
        if (gameRunning()) return new(false, SaveFailure.GameRunning);
        token.ThrowIfCancellationRequested(); // the last moment to cancel: nothing has been touched yet

        string? safetyId = null;
        if (Directory.Exists(destination) && Scan(destination).Files.Count > 0)
        {
            var safety = await CreateAsync(gameKey, gameName, destination, SnapshotManifest.BeforeRestore, keepSafety, CancellationToken.None);
            if (safety.Status == SnapshotStatus.Failed) return new(false, safety.Failure, null, "safety copy");
            safetyId = safety.Manifest!.Id;
        }
        return await Task.Run(() =>
        {
            File.WriteAllText(JournalFile(gameKey), JsonSerializer.Serialize(new RestoreJournal(id, safetyId, DateTimeOffset.Now, destination, plan.Create), VaultJson.Default.RestoreJournal));
            try
            {
                if (gameRunning()) throw new InvalidOperationException("game started");
                Apply(gameKey, plan.Snapshot, destination);
                foreach (var f in plan.Snapshot.Files)
                    if (HashFile(Inside(destination, f.Path)!) != f.Sha256) throw new InvalidDataException("verify");
                File.Delete(JournalFile(gameKey));
                return new RestoreResult(true, SaveFailure.None, safetyId);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
            {
                CleanTemps(destination, plan.Snapshot.Files.Select(f => f.Path));
                var back = RollBack(gameKey, safetyId, destination, plan.Create);
                return new RestoreResult(false, back ? SaveFailure.RolledBack : SaveFailure.Unreadable, safetyId, ex.GetType().Name);
            }
        }, CancellationToken.None);
    }

    // Puts the "before restore" copy back (after a failure, or later from an interrupted restore).
    public bool RollBack(string gameKey, string? safetyId, string destination, IEnumerable<string>? created = null)
    {
        try
        {
            // Files the restore created (they did not exist before) go away; half-written temporary files too.
            foreach (var rel in created ?? [])
                if (Inside(destination, rel) is { } path) { TryDeleteFile(path); TryDeleteFile(path + ".lotus-restore"); }
            if (safetyId is not null)
            {
                if (ReadManifest(Path.Combine(GameDir(gameKey), safetyId)) is not { IsValid: true } safety) return false;
                Apply(gameKey, safety, destination, hook: false);
            }
            File.Delete(JournalFile(gameKey));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { return false; }
    }

    // Each file goes to "<name>.lotus-restore" first, is checked, then replaces the target in one move.
    private void Apply(string gameKey, SnapshotManifest m, string destination, bool hook = true)
    {
        var dir = Path.Combine(GameDir(gameKey), m.Id, "files");
        foreach (var f in m.Files)
        {
            var src = Inside(dir, f.Path) ?? throw new InvalidDataException("unsafe");
            var dst = Inside(destination, f.Path) ?? throw new InvalidDataException("unsafe");
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            var tmp = dst + ".lotus-restore";
            if (CopyHashing(src, tmp) != f.Sha256) { TryDeleteFile(tmp); throw new InvalidDataException("copy"); }
            if (hook) BeforeReplaceFile?.Invoke(f.Path);
            File.Move(tmp, dst, overwrite: true);
            File.SetLastWriteTimeUtc(dst, f.LastWriteUtc);
        }
    }

    // ---------- Helpers ----------
    public static string HashFile(string path)
    {
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(s));
    }
    private static string CopyHashing(string src, string dst)
    {
        using var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
        using var output = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 16]; int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0) { sha.AppendData(buffer, 0, read); output.Write(buffer, 0, read); }
        output.Flush(true);
        return Convert.ToHexString(sha.GetHashAndReset());
    }
    private static bool IsDiskFull(IOException ex) => (ex.HResult & 0xFFFF) is 0x70 or 0x27; // ERROR_DISK_FULL / ERROR_HANDLE_DISK_FULL
    private static long? DefaultFreeSpace(string path)
    {
        try { var root = Path.GetPathRoot(Path.GetFullPath(path)); return root is null ? null : new DriveInfo(root).AvailableFreeSpace; }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { return null; }
    }
    private static void TryDelete(string dir) { try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
    private static void TryDeleteFile(string file) { try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
    private static void TryDeleteStale(string dir) { try { if (Directory.GetLastWriteTimeUtc(dir) < DateTime.UtcNow.AddHours(-1)) TryDelete(dir); } catch (IOException) { } }
}

public sealed record RestoreJournal(string SnapshotId, string? SafetyId, DateTimeOffset Started, string Destination, IReadOnlyList<string>? Created = null);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SnapshotManifest))]
[JsonSerializable(typeof(RestoreJournal))]
internal sealed partial class VaultJson : JsonSerializerContext { }
