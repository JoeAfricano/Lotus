using System.Collections.ObjectModel;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Maintenance;

// ---- Arquivos e Armazenamento: find what takes space; nothing is removed by Lotus on its own ----

public sealed record StorageFile(string Path, long Bytes, DateTime LastWriteUtc);
public sealed record StorageFolder(string Path, long Bytes, int Files);
public sealed record DuplicateGroup(string Hash, long BytesEach, IReadOnlyList<StorageFile> Files)
{
    public long Wasted => BytesEach * (Files.Count - 1);
}
public sealed record StorageReport(string Root, IReadOnlyList<StorageFile> LargeFiles, IReadOnlyList<StorageFolder> Folders, IReadOnlyList<DuplicateGroup> Duplicates,
    IReadOnlyList<StorageFile> OldDownloads, long ScannedBytes, int ScannedFiles, int Skipped);
public sealed record DriveSpace(string Name, string Label, long Free, long Total)
{
    public double UsedPercent => Total > 0 ? (Total - Free) * 100.0 / Total : 0;
}

public interface IFileRecycler { bool Recycle(string path); }       // to the Recycle Bin; true only when the file left its folder
public interface IFileRevealer { void Reveal(string path); }        // opens Explorer with the item selected

public sealed class StorageScanner(Func<string, string?> folders, Func<DateTime>? clock = null)
{
    public long LargeFileMin { get; init; } = 100L << 20;
    public long DuplicateMin { get; init; } = 1L << 20;
    public TimeSpan OldDownloadAge { get; init; } = TimeSpan.FromDays(90);
    private DateTime Now => (clock ?? (() => DateTime.UtcNow))();
    private static readonly EnumerationOptions Options = new() { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };

    // Windows, program folders and app data are only shown, never offered for removal from here.
    public bool IsSystemPath(string path)
    {
        foreach (var token in new[] { "WINDIR", "PROGRAMFILES", "PROGRAMFILESX86", "PROGRAMDATA", "LOCALAPPDATA", "APPDATA" })
            if (folders(token) is { Length: > 0 } f && path.StartsWith(f.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        var name = System.IO.Path.GetFileName(path);
        return name.Equals("pagefile.sys", StringComparison.OrdinalIgnoreCase) || name.Equals("hiberfil.sys", StringComparison.OrdinalIgnoreCase) || name.Equals("swapfile.sys", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\$Recycle.Bin\", StringComparison.OrdinalIgnoreCase) || path.Contains(@"\System Volume Information\", StringComparison.OrdinalIgnoreCase);
    }

    public Task<StorageReport> ScanAsync(string root, IProgress<double>? progress = null, CancellationToken token = default) => Task.Run(() =>
    {
        var rootDir = new DirectoryInfo(root);
        if (!rootDir.Exists || rootDir.LinkTarget is not null) return new StorageReport(root, [], [], [], [], 0, 0, 0);
        var files = new List<StorageFile>(); var skipped = 0;
        var folderSize = new Dictionary<string, (long Bytes, int Files)>(StringComparer.OrdinalIgnoreCase);
        var rootFull = rootDir.FullName.TrimEnd('\\') + "\\";
        using (var e = rootDir.EnumerateFiles("*", Options).GetEnumerator())
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                FileInfo f;
                try { if (!e.MoveNext()) break; f = e.Current; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; break; }
                long size; DateTime write;
                try { size = f.Length; write = f.LastWriteTimeUtc; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; continue; }
                files.Add(new(f.FullName, size, write));
                // first-level folder under the root (files right in the root count as the root itself)
                var rel = f.FullName.Length > rootFull.Length ? f.FullName[rootFull.Length..] : f.Name;
                var first = rel.Contains('\\') ? rootFull + rel[..rel.IndexOf('\\')] : rootFull.TrimEnd('\\');
                var cur = folderSize.GetValueOrDefault(first); folderSize[first] = (cur.Bytes + size, cur.Files + 1);
                if (files.Count % 2000 == 0) progress?.Report(Math.Min(40, files.Count / 2000.0));
            }
        }
        progress?.Report(40);
        var large = files.Where(f => f.Bytes >= LargeFileMin).OrderByDescending(f => f.Bytes).Take(100).ToList();
        var top = folderSize.Select(kv => new StorageFolder(kv.Key, kv.Value.Bytes, kv.Value.Files)).OrderByDescending(f => f.Bytes).Take(30).ToList();
        var downloads = folders("DOWNLOADS") is { Length: > 0 } dl ? dl.TrimEnd('\\') + "\\" : null;
        var old = downloads is null ? [] : files.Where(f => f.Path.StartsWith(downloads, StringComparison.OrdinalIgnoreCase) && Now - f.LastWriteUtc >= OldDownloadAge).OrderByDescending(f => f.Bytes).Take(200).ToList();
        var dups = Duplicates(files, p => progress?.Report(40 + p * 0.6), token);
        progress?.Report(100);
        return new StorageReport(rootDir.FullName, large, top, dups, old, files.Sum(f => f.Bytes), files.Count, skipped);
    }, token);

    // Same size first (cheap), then the whole content (SHA-256). A name alone never makes two files "the same".
    public IReadOnlyList<DuplicateGroup> Duplicates(IEnumerable<StorageFile> files, Action<double>? progress = null, CancellationToken token = default)
    {
        var bySize = files.Where(f => f.Bytes >= DuplicateMin).GroupBy(f => f.Bytes).Where(g => g.Count() > 1).ToList();
        var total = Math.Max(1, bySize.Sum(g => g.Key * g.Count())); long done = 0;
        var groups = new List<DuplicateGroup>();
        foreach (var same in bySize)
        {
            var byHash = new Dictionary<string, List<StorageFile>>();
            foreach (var f in same)
            {
                token.ThrowIfCancellationRequested();
                if (Hash(f.Path, token) is { } h) { if (!byHash.TryGetValue(h, out var l)) byHash[h] = l = []; l.Add(f); }
                done += f.Bytes; progress?.Invoke(done * 100.0 / total);
            }
            groups.AddRange(byHash.Where(kv => kv.Value.Count > 1).Select(kv => new DuplicateGroup(kv.Key, same.Key, [.. kv.Value.OrderBy(f => f.LastWriteUtc)])));
        }
        return [.. groups.OrderByDescending(g => g.Wasted).Take(100)];
    }

    public static string? Hash(string path, CancellationToken token = default)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1 << 20]; int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); sha.AppendData(buffer, 0, read); }
            return Convert.ToHexString(sha.GetHashAndReset());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}

public sealed partial class StorageRow(string path, string title, string detail, long bytes, bool canRecycle, string? group) : ObservableObject
{
    public string Path { get; } = path;
    public string Title { get; } = title;
    public string Detail { get; } = detail;
    public long Bytes { get; } = bytes;
    public bool CanRecycle { get; } = canRecycle;
    public string? Group { get; } = group;    // duplicates: rows of the same content share it
    public bool IsHeader => Group is not null && Path.Length == 0;
    [ObservableProperty] private bool selected;
}

public sealed partial class StorageViewModel(StorageScanner scanner, IFileRecycler recycler, IFileRevealer revealer, Func<IReadOnlyList<DriveSpace>> drives,
    Func<string, string?> folders, ITextCatalog text, IMaintenanceJournal journal, Action<Exception> log) : ObservableObject
{
    public ITextCatalog Text { get; } = text;
    public ObservableCollection<StorageRow> Rows { get; } = [];
    public ObservableCollection<string> Drives { get; } = [];
    public ObservableCollection<string> Scopes { get; } = [];
    public ObservableCollection<string> Views { get; } = [];
    public TaskState Work { get; } = new();
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string summary = "";
    [ObservableProperty] private bool busy;
    [ObservableProperty] private int scopeIndex;
    [ObservableProperty] private int viewIndex;   // 0 large files, 1 folders, 2 duplicates, 3 old downloads
    [ObservableProperty] private bool hasReport;
    private readonly List<string> scopePaths = [];
    private StorageReport? report;
    private CancellationTokenSource? cts;
    public bool CanAct => !Busy;
    public string TileStatus => Summary.Length > 0 ? Summary : Text["StTileHint"];
    partial void OnSummaryChanged(string value) => OnPropertyChanged(nameof(TileStatus));
    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(CanAct));
    partial void OnViewIndexChanged(int value) => Fill();
    private System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.GetCultureInfo(Text.Language);
    public string Size(long bytes) => bytes >= 1L << 30 ? (bytes / (double)(1L << 30)).ToString("0.0", Culture) + " GB" : bytes >= 1L << 20 ? (bytes / (double)(1L << 20)).ToString("0", Culture) + " MB" : (bytes / 1024.0).ToString("0", Culture) + " KB";

    // Drive space and the places that can be analysed; reading only.
    public void Load()
    {
        Drives.Clear(); Scopes.Clear(); scopePaths.Clear();
        if (Views.Count == 0) { foreach (var k in new[] { "StViewLarge", "StViewFolders", "StViewDups", "StViewDownloads" }) Views.Add(Text[k]); }
        var list = drives();
        foreach (var d in list) Drives.Add(Text.Format("StDrive", d.Name, d.Label.Length > 0 ? d.Label : Text["StLocalDisk"], Size(d.Free), Size(d.Total), d.UsedPercent));
        void Add(string key, string? path) { if (path is { Length: > 0 } && Directory.Exists(path)) { Scopes.Add(Text.Format(key, path)); scopePaths.Add(path); } }
        Add("StScopeUser", folders("USERPROFILE"));
        Add("StScopeDownloads", folders("DOWNLOADS"));
        foreach (var d in list) Add("StScopeDrive", d.Name);
        if (ScopeIndex >= Scopes.Count) ScopeIndex = 0;
        var low = list.FirstOrDefault(d => d.Total > 0 && d.Free * 100.0 / d.Total < 10);
        if (Summary.Length == 0) Summary = low is null ? Text.Format("StSpaceOk", list.Count) : Text.Format("StSpaceLow", low.Name, Size(low.Free));
    }

    public async Task ScanAsync()
    {
        if (Busy || ScopeIndex < 0 || ScopeIndex >= scopePaths.Count) return;
        Busy = true; cts = new(); Work.Start(); Status = Text["StScanning"];
        try
        {
            report = await scanner.ScanAsync(scopePaths[ScopeIndex], Work.Progress(), cts.Token);
            HasReport = true; Fill();
            var wasted = report.Duplicates.Sum(g => g.Wasted);
            Summary = Text.Format("StFound", Size(report.ScannedBytes), report.ScannedFiles, report.LargeFiles.Count, Size(wasted));
            Status = Summary + (report.Skipped > 0 ? " " + Text.Format("StSkipped", report.Skipped) : "");
            Work.Finish(Text["StDone"]);
        }
        catch (OperationCanceledException) { Status = Text["ClCancelled"]; Work.Fail(); }
        catch (Exception ex) { log(ex); Status = Text["ToolFailed"]; Work.Fail(); }
        finally { Busy = false; cts?.Dispose(); cts = null; }
    }
    public void Cancel() => cts?.Cancel();

    private string Date(DateTime utc) => utc.ToLocalTime().ToString("d", Culture);
    private void Fill()
    {
        Rows.Clear();
        if (report is null) return;
        switch (ViewIndex)
        {
            case 0:
                foreach (var f in report.LargeFiles) Rows.Add(new(f.Path, System.IO.Path.GetFileName(f.Path), $"{Size(f.Bytes)} · {Date(f.LastWriteUtc)} · {System.IO.Path.GetDirectoryName(f.Path)}", f.Bytes, !scanner.IsSystemPath(f.Path), null));
                break;
            case 1:
                foreach (var d in report.Folders) Rows.Add(new(d.Path, d.Path, Text.Format("StFolderLine", Size(d.Bytes), d.Files), d.Bytes, false, null));
                break;
            case 2:
                var n = 0;
                foreach (var g in report.Duplicates)
                {
                    n++;
                    Rows.Add(new("", Text.Format("StDupGroup", n, g.Files.Count, Size(g.BytesEach), Size(g.Wasted)), "", 0, false, g.Hash));
                    foreach (var f in g.Files) Rows.Add(new(f.Path, System.IO.Path.GetFileName(f.Path), $"{Date(f.LastWriteUtc)} · {System.IO.Path.GetDirectoryName(f.Path)}", f.Bytes, !scanner.IsSystemPath(f.Path), g.Hash));
                }
                break;
            case 3:
                foreach (var f in report.OldDownloads) Rows.Add(new(f.Path, System.IO.Path.GetFileName(f.Path), $"{Size(f.Bytes)} · {Date(f.LastWriteUtc)}", f.Bytes, true, null));
                break;
        }
        Status = Rows.Count == 0 && HasReport ? Text["StViewEmpty"] : Status;
    }

    public IReadOnlyList<StorageRow> Chosen => [.. Rows.Where(r => r.Selected && r.CanRecycle && !r.IsHeader)];
    public long ChosenBytes => Chosen.Sum(r => r.Bytes);
    // Duplicates: at least one copy of each content always stays.
    public bool KeepsOneCopyOfEach() => Rows.Where(r => r.Group is not null && !r.IsHeader).GroupBy(r => r.Group).All(g => g.Any(r => !r.Selected));

    public async Task RecycleSelectedAsync()
    {
        var chosen = Chosen;
        if (Busy || chosen.Count == 0 || !KeepsOneCopyOfEach()) return;
        Busy = true; Work.Start(); Status = Text["StRecycling"];
        try
        {
            int moved = 0, failed = 0; long bytes = 0;
            for (var i = 0; i < chosen.Count; i++)
            {
                var r = chosen[i];
                // checked again right before: still the same file (size), a real file, not a system location
                var ok = await Task.Run(() =>
                {
                    try
                    {
                        var info = new FileInfo(r.Path);
                        if (!info.Exists || info.Length != r.Bytes || info.LinkTarget is not null || scanner.IsSystemPath(info.FullName)) return false;
                        return recycler.Recycle(info.FullName);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
                });
                if (ok) { moved++; bytes += r.Bytes; Rows.Remove(r); } else failed++;
                Work.Report((i + 1) * 100.0 / chosen.Count);
            }
            Status = Text.Format("StRecycled", moved, Size(bytes)) + (failed > 0 ? " " + Text.Format("StRecycleFailed", failed) : "");
            journal.Add(new(DateTimeOffset.Now, "storage", "recycle", $"{moved} files ({bytes} bytes) to the Recycle Bin, {failed} not moved", failed == 0));
            Work.Finish(Text.Format("StRecycledShort", Size(bytes)));
            Load();
        }
        catch (Exception ex) { log(ex); Status = Text["ToolFailed"]; Work.Fail(); }
        finally { Busy = false; }
    }

    public void Reveal(StorageRow row) { if (row.Path.Length > 0) try { revealer.Reveal(row.Path); } catch (Exception ex) { log(ex); } }
}
