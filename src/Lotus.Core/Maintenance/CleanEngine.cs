using System.Diagnostics;

namespace Lotus.Core.Maintenance;

// ---- Lotus Clean: one engine for every cleanup (detect → analyse → classify → recommend → confirm → run → verify → log) ----

public enum CleanCategory { Windows, Browsers, Apps, Games }

// Where a rule looks: a known folder token (TEMP, LOCALAPPDATA, APPDATA, PROGRAMDATA, WINDIR) plus a relative path; "*" in a
// path segment means "every folder here" (browser profiles). Only files are removed, never the folders a rule points at.
public sealed record CleanRule(
    string Id, CleanCategory Category, MaintenanceRisk Risk, string[] Roots, TimeSpan MinAge,
    string[]? Patterns = null, bool Recursive = true, string[]? BlockedBy = null, bool SelectedByDefault = true)
{
    public string TitleKey => "Cl" + Id;
}

// Folder tokens → real folders (the app passes Windows' folders; tests pass temporary ones).
public interface ICleanLocations { string? Resolve(string token); }
// Is any of these programs running? (browser caches are skipped while the browser is open)
public interface IProcessProbe { bool AnyRunning(IEnumerable<string> processNames); }

public sealed record CleanFinding(CleanRule Rule, int Files, long Bytes, string? BlockedBy)
{
    public bool Blocked => BlockedBy is not null;
}
public sealed record CleanReport(IReadOnlyList<CleanFinding> Findings, DateTimeOffset At)
{
    public long Bytes => Findings.Where(f => !f.Blocked).Sum(f => f.Bytes);
    public int Files => Findings.Where(f => !f.Blocked).Sum(f => f.Files);
}
// Skipped = the program that owns the files was open, so the rule did not run.
public sealed record CleanRuleResult(string RuleId, int Deleted, long BytesFreed, int InUse, int Failed, bool Skipped = false);
public sealed record CleanResult(IReadOnlyList<CleanRuleResult> Rules, DateTimeOffset At, bool Cancelled)
{
    public long BytesFreed => Rules.Sum(r => r.BytesFreed);
    public int Deleted => Rules.Sum(r => r.Deleted);
    public int InUse => Rules.Sum(r => r.InUse);
    public int Failed => Rules.Sum(r => r.Failed);
    public int Skipped => Rules.Count(r => r.Skipped);
}

// The rules Lotus knows. Only disposable data: temporary files, caches that programs rebuild by themselves, old logs and
// crash reports. Never saves, mods, settings, cookies, sessions, passwords, sync data or shader caches.
public static class CleanRules
{
    private static readonly TimeSpan Day = TimeSpan.FromDays(1), Week = TimeSpan.FromDays(7), Month = TimeSpan.FromDays(30);
    private static readonly string[] Logs = ["*.log", "*.log.*", "*.txt", "*.etl"], Dumps = ["*.dmp", "*.mdmp", "*.hdmp", "*.wer"];
    private static readonly string[] Chromium = ["chrome", "msedge", "brave", "opera", "launcher"];

    public static readonly IReadOnlyList<CleanRule> All =
    [
        // Windows
        new("WinTemp", CleanCategory.Windows, MaintenanceRisk.Low, ["TEMP"], Day),
        new("WinSysTemp", CleanCategory.Windows, MaintenanceRisk.Low, ["WINDIR\\Temp"], Week),
        new("WinCrashDumps", CleanCategory.Windows, MaintenanceRisk.Low, ["LOCALAPPDATA\\CrashDumps"], Week, Dumps),
        new("WinReports", CleanCategory.Windows, MaintenanceRisk.Low, ["LOCALAPPDATA\\Microsoft\\Windows\\WER\\ReportArchive", "LOCALAPPDATA\\Microsoft\\Windows\\WER\\ReportQueue"], Week),
        new("WinThumbs", CleanCategory.Windows, MaintenanceRisk.Review, ["LOCALAPPDATA\\Microsoft\\Windows\\Explorer"], Week, ["thumbcache_*.db", "iconcache_*.db"], Recursive: false, BlockedBy: null, SelectedByDefault: false),
        // Browsers: only the web cache (Cache_Data / cache2). Cookies, logins, history and GPU/shader caches stay.
        new("Chrome", CleanCategory.Browsers, MaintenanceRisk.Low, ["LOCALAPPDATA\\Google\\Chrome\\User Data\\*\\Cache\\Cache_Data"], TimeSpan.Zero, BlockedBy: ["chrome"]),
        new("Edge", CleanCategory.Browsers, MaintenanceRisk.Low, ["LOCALAPPDATA\\Microsoft\\Edge\\User Data\\*\\Cache\\Cache_Data"], TimeSpan.Zero, BlockedBy: ["msedge"]),
        new("Brave", CleanCategory.Browsers, MaintenanceRisk.Low, ["LOCALAPPDATA\\BraveSoftware\\Brave-Browser\\User Data\\*\\Cache\\Cache_Data"], TimeSpan.Zero, BlockedBy: ["brave"]),
        new("Opera", CleanCategory.Browsers, MaintenanceRisk.Low, ["LOCALAPPDATA\\Opera Software\\Opera Stable\\Cache\\Cache_Data", "LOCALAPPDATA\\Opera Software\\Opera Stable\\Default\\Cache\\Cache_Data"], TimeSpan.Zero, BlockedBy: ["opera"]),
        new("OperaGx", CleanCategory.Browsers, MaintenanceRisk.Low, ["LOCALAPPDATA\\Opera Software\\Opera GX Stable\\Cache\\Cache_Data", "LOCALAPPDATA\\Opera Software\\Opera GX Stable\\Default\\Cache\\Cache_Data"], TimeSpan.Zero, BlockedBy: ["opera"]),
        new("Firefox", CleanCategory.Browsers, MaintenanceRisk.Low, ["LOCALAPPDATA\\Mozilla\\Firefox\\Profiles\\*\\cache2\\entries"], TimeSpan.Zero, BlockedBy: ["firefox"]),
        // Apps
        new("Discord", CleanCategory.Apps, MaintenanceRisk.Low, ["APPDATA\\discord\\Cache\\Cache_Data"], TimeSpan.Zero, BlockedBy: ["discord"]),
        new("AppLogs", CleanCategory.Apps, MaintenanceRisk.Low, ["LOCALAPPDATA\\Discord\\logs", "APPDATA\\discord\\logs", "LOCALAPPDATA\\NVIDIA Corporation\\NvTmRep"], Month, Logs),
        // Games and launchers: old logs, web caches of the launchers' screens, crash reports. Shader caches stay.
        new("SteamWeb", CleanCategory.Games, MaintenanceRisk.Low, ["LOCALAPPDATA\\Steam\\htmlcache\\Cache\\Cache_Data", "LOCALAPPDATA\\Steam\\htmlcache\\Default\\Cache\\Cache_Data"], TimeSpan.Zero, BlockedBy: ["steam", "steamwebhelper"]),
        new("EpicWeb", CleanCategory.Games, MaintenanceRisk.Low, ["LOCALAPPDATA\\EpicGamesLauncher\\Saved\\webcache_4430\\Cache\\Cache_Data", "LOCALAPPDATA\\EpicGamesLauncher\\Saved\\webcache\\Cache\\Cache_Data"], TimeSpan.Zero, BlockedBy: ["EpicGamesLauncher", "EpicWebHelper"]),
        new("LauncherLogs", CleanCategory.Games, MaintenanceRisk.Low, ["LOCALAPPDATA\\EpicGamesLauncher\\Saved\\Logs", "LOCALAPPDATA\\Electronic Arts\\EA Desktop\\Logs", "LOCALAPPDATA\\Ubisoft Game Launcher\\logs", "LOCALAPPDATA\\Riot Games\\Riot Client\\Logs"], Week, Logs),
        new("GameCrashes", CleanCategory.Games, MaintenanceRisk.Low, ["LOCALAPPDATA\\CrashReportClient\\Saved\\Crashes", "LOCALAPPDATA\\UnrealEngine\\Common\\Crashes"], Week),
        // Recycle Bin: never selected by itself.
        new("RecycleBin", CleanCategory.Windows, MaintenanceRisk.Review, [], TimeSpan.Zero, SelectedByDefault: false),
    ];
}

// Empties the Recycle Bin (Windows does it; the engine only asks for size and emptying).
public interface IRecycleBin { (long Bytes, long Items) Query(); bool Empty(); }

public sealed class CleanEngine(ICleanLocations locations, IProcessProbe processes, IRecycleBin? recycleBin = null, IReadOnlyList<CleanRule>? rules = null, Func<DateTime>? clock = null, Action<string>? trace = null)
{
    public IReadOnlyList<CleanRule> Rules { get; } = rules ?? CleanRules.All;
    private DateTime Now => (clock ?? (() => DateTime.UtcNow))();
    private static readonly EnumerationOptions Shallow = new() { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System, RecurseSubdirectories = false };

    // The real folders a rule points at ("*" expanded). Unknown tokens, links and missing folders give nothing.
    public IEnumerable<string> RootsOf(CleanRule rule)
    {
        foreach (var root in rule.Roots)
        {
            var parts = root.Split('\\', 2);
            if (locations.Resolve(parts[0]) is not { Length: > 0 } baseDir) continue;
            IEnumerable<string> current = [baseDir];
            if (parts.Length == 2)
                foreach (var segment in parts[1].Split('\\', StringSplitOptions.RemoveEmptyEntries))
                    current = segment == "*"
                        ? current.SelectMany(c => SafeDirectories(c))
                        : current.Select(c => Path.Combine(c, segment));
            foreach (var dir in current)
                if (IsRealDirectory(dir) && !IsProtected(Path.GetFullPath(dir))) yield return Path.GetFullPath(dir);
        }
    }

    // A rule never works on a drive root or on (or above) the user's main folders, even if TEMP or another variable points there.
    private static readonly string[] Guarded = ["USERPROFILE", "DOCUMENTS", "DESKTOP", "DOWNLOADS", "PICTURES", "WINDIR", "LOCALAPPDATA", "APPDATA", "PROGRAMDATA", "PROGRAMFILES"];
    public bool IsProtected(string dir)
    {
        var full = Path.GetFullPath(dir).TrimEnd('\\');
        if (full.Length <= 3 || string.Equals(Path.GetPathRoot(full)?.TrimEnd('\\'), full, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var token in Guarded)
            if (locations.Resolve(token) is { Length: > 0 } g)
            {
                var guarded = Path.GetFullPath(g).TrimEnd('\\');
                if (guarded.Equals(full, StringComparison.OrdinalIgnoreCase) || guarded.StartsWith(full + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            }
        return false;
    }

    private static IEnumerable<string> SafeDirectories(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.EnumerateDirectories(dir, "*", Shallow).ToList() : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }
    private static bool IsRealDirectory(string dir)
    {
        try { var info = new DirectoryInfo(dir); return info.Exists && info.LinkTarget is null && !info.Attributes.HasFlag(FileAttributes.ReparsePoint); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    // Files a rule would remove: inside its folders (links never followed), old enough, matching its patterns.
    private IEnumerable<(string Path, long Size)> Candidates(CleanRule rule, string root, CancellationToken token)
    {
        var limit = Now - rule.MinAge;
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = rule.Recursive, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
        var patterns = rule.Patterns is { Length: > 0 } p ? p : ["*"];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rootFull = root.TrimEnd('\\') + "\\";
        foreach (var pattern in patterns)
        {
            IEnumerable<FileInfo> files;
            try { files = new DirectoryInfo(root).EnumerateFiles(pattern, options); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            using var e = files.GetEnumerator();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                FileInfo f;
                try { if (!e.MoveNext()) break; f = e.Current; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { break; }
                if (rule.MinAge > TimeSpan.Zero && f.LastWriteTimeUtc > limit) continue;
                // Lotus's own temporary files and folders stay
                if (f.FullName.Length > rootFull.Length && f.FullName[rootFull.Length..].Split('\\').Any(s => s.StartsWith("Lotus", StringComparison.OrdinalIgnoreCase))) continue;
                if (!seen.Add(f.FullName)) continue;
                long size;
                try { size = f.Length; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                yield return (f.FullName, size);
            }
        }
    }

    // Analysis: counts and sizes only, nothing changes. Rules blocked by a running program are reported as such.
    public async Task<CleanReport> AnalyzeAsync(IProgress<double>? progress = null, CancellationToken token = default) => await Task.Run(() =>
    {
        var findings = new List<CleanFinding>();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < Rules.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var rule = Rules[i];
            progress?.Report(i * 100.0 / Rules.Count);
            if (rule.Id == "RecycleBin")
            {
                if (recycleBin is not null) { var (bytes, items) = recycleBin.Query(); findings.Add(new(rule, (int)Math.Min(int.MaxValue, items), bytes, null)); }
                continue;
            }
            var blocked = rule.BlockedBy is { Length: > 0 } names && processes.AnyRunning(names) ? string.Join(", ", names) : null;
            int count = 0; long total = 0;
            foreach (var root in RootsOf(rule).Distinct(StringComparer.OrdinalIgnoreCase))
                foreach (var (_, size) in Candidates(rule, root, token)) { count++; total += size; }
            if (count > 0 || blocked is not null) findings.Add(new(rule, count, total, count > 0 ? blocked : null));
        }
        progress?.Report(100);
        trace?.Invoke($"Clean analysis: {findings.Count} rules, {findings.Sum(f => f.Files)} files, {findings.Sum(f => f.Bytes)} bytes in {watch.ElapsedMilliseconds} ms");
        return new CleanReport(findings.Where(f => f.Files > 0 || f.Bytes > 0).ToList(), DateTimeOffset.Now);
    }, token);

    // Cleaning: each file is checked again right before it goes (still inside the rule's folder, not a link, still old
    // enough). Files in use are left alone. Only what really left the disk is counted. Empty subfolders are tidied.
    public async Task<CleanResult> CleanAsync(IEnumerable<string> ruleIds, IProgress<double>? progress = null, CancellationToken token = default) => await Task.Run(() =>
    {
        var chosen = Rules.Where(r => ruleIds.Contains(r.Id)).ToList();
        var results = new List<CleanRuleResult>();
        var cancelled = false;
        for (var i = 0; i < chosen.Count && !cancelled; i++)
        {
            if (token.IsCancellationRequested) { cancelled = true; break; }
            var rule = chosen[i];
            progress?.Report(i * 100.0 / Math.Max(1, chosen.Count));
            if (rule.Id == "RecycleBin")
            {
                if (recycleBin is null) continue;
                var (before, items) = recycleBin.Query();
                var ok = recycleBin.Empty();
                var (after, _) = recycleBin.Query();
                results.Add(new(rule.Id, ok ? (int)Math.Min(int.MaxValue, items) : 0, Math.Max(0, before - after), 0, ok ? 0 : 1));
                continue;
            }
            if (rule.BlockedBy is { Length: > 0 } names && processes.AnyRunning(names)) { results.Add(new(rule.Id, 0, 0, 0, 0, Skipped: true)); continue; }
            int deleted = 0, inUse = 0, failed = 0; long freed = 0;
            foreach (var root in RootsOf(rule).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var rootFull = root.TrimEnd('\\') + "\\";
                try
                {
                    foreach (var (path, _) in Candidates(rule, root, token).ToList())
                    {
                        if (token.IsCancellationRequested) { cancelled = true; break; }
                        switch (DeleteChecked(path, rootFull, rule, out var size))
                        {
                            case 0: deleted++; freed += size; break;
                            case 1: inUse++; break;
                            default: failed++; break;
                        }
                    }
                }
                catch (OperationCanceledException) { cancelled = true; }
                if (!cancelled) RemoveEmptyFolders(root);
            }
            results.Add(new(rule.Id, deleted, freed, inUse, failed));
            trace?.Invoke($"Clean {rule.Id}: {deleted} files, {freed} bytes freed, {inUse} in use, {failed} failed");
        }
        progress?.Report(100);
        return new CleanResult(results, DateTimeOffset.Now, cancelled);
    }, CancellationToken.None);

    // 0 = removed, 1 = in use (or changed meanwhile), 2 = refused or failed.
    private int DeleteChecked(string path, string rootFull, CleanRule rule, out long size)
    {
        size = 0;
        try
        {
            var full = Path.GetFullPath(path);
            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return 2;      // never outside the rule's folder
            var info = new FileInfo(full);
            if (!info.Exists) return 1;
            if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return 2;
            // every folder between the root and the file must be a real folder too (a link placed there meanwhile is refused)
            for (var d = info.Directory; d is not null && d.FullName.Length >= rootFull.Length - 1; d = d.Parent)
                if (d.LinkTarget is not null || d.Attributes.HasFlag(FileAttributes.ReparsePoint)) return 2;
            if (rule.MinAge > TimeSpan.Zero && info.LastWriteTimeUtc > Now - rule.MinAge) return 1; // touched since the analysis
            size = info.Length;
            if (info.IsReadOnly) return 2;
            info.Delete();
            if (File.Exists(full)) { size = 0; return 1; }
            return 0;
        }
        catch (IOException) { size = 0; return 1; }                 // in use
        catch (UnauthorizedAccessException) { size = 0; return 2; } // needs administrator: left alone
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.Security.SecurityException) { size = 0; return 2; }
    }

    private static void RemoveEmptyFolders(string root)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                         .OrderByDescending(d => d.Length))
                try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
