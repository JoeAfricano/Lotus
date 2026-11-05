using Lotus.Core;
using Lotus.Core.Maintenance;
using Lotus.Infrastructure.Maintenance;
using Xunit;

namespace Lotus.Tests;

// Lotus Clean on throw-away folders only: real sizes, ages, links, files in use, open programs, protected folders,
// cancellation and the history. Nothing outside the test folder is ever touched.
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class CleanEngineTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "clean-test-" + Guid.NewGuid().ToString("N"));
    private readonly DateTime now = DateTime.UtcNow;
    public CleanEngineTests() => Directory.CreateDirectory(dir);
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private sealed class Locations(Dictionary<string, string> map) : ICleanLocations { public string? Resolve(string token) => map.GetValueOrDefault(token); }
    private sealed class Processes(params string[] running) : IProcessProbe { public bool AnyRunning(IEnumerable<string> names) => names.Any(n => running.Contains(n, StringComparer.OrdinalIgnoreCase)); }
    private sealed class FakeBin(long bytes, long items) : IRecycleBin
    {
        public bool Emptied; public (long, long) Query() => Emptied ? (0, 0) : (bytes, items); public bool Empty() { Emptied = true; return true; }
    }

    private string File(string relative, int size, TimeSpan age)
    {
        var path = Path.Combine(dir, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, new byte[size]);
        System.IO.File.SetLastWriteTimeUtc(path, now - age);
        return path;
    }
    private CleanEngine Engine(IReadOnlyList<CleanRule> rules, IProcessProbe? probe = null, IRecycleBin? bin = null, Dictionary<string, string>? extra = null)
    {
        var map = new Dictionary<string, string> { ["ROOT"] = dir, ["USERPROFILE"] = Path.Combine(dir, "profile") };
        foreach (var (k, v) in extra ?? []) map[k] = v;
        return new CleanEngine(new Locations(map), probe ?? new Processes(), bin, rules, () => now);
    }
    private static readonly TimeSpan Old = TimeSpan.FromDays(3);

    [Fact] public async Task AnalysisCountsRealSizesAndChangesNothing()
    {
        var a = File(@"tmp\a.tmp", 1000, Old); var b = File(@"tmp\sub\b.tmp", 2500, Old); var fresh = File(@"tmp\new.tmp", 400, TimeSpan.FromMinutes(5));
        var engine = Engine([new("T", CleanCategory.Windows, MaintenanceRisk.Low, [@"ROOT\tmp"], TimeSpan.FromDays(1))]);
        var report = await engine.AnalyzeAsync();
        var f = Assert.Single(report.Findings);
        Assert.Equal(2, f.Files); Assert.Equal(3500, f.Bytes);                  // the fresh file is not counted
        Assert.True(System.IO.File.Exists(a) && System.IO.File.Exists(b) && System.IO.File.Exists(fresh));
    }

    [Fact] public async Task CleaningRemovesOnlyOldMatchingFilesAndReportsRealBytes()
    {
        File(@"logs\old.log", 700, Old); var keep = File(@"logs\settings.json", 300, Old); var fresh = File(@"logs\today.log", 100, TimeSpan.FromHours(1));
        var engine = Engine([new("L", CleanCategory.Apps, MaintenanceRisk.Low, [@"ROOT\logs"], TimeSpan.FromDays(1), ["*.log"])]);
        var result = await engine.CleanAsync(["L"]);
        Assert.Equal(1, result.Deleted); Assert.Equal(700, result.BytesFreed);
        Assert.True(System.IO.File.Exists(keep)); Assert.True(System.IO.File.Exists(fresh));
    }

    [Fact] public async Task FilesInUseStayAndAreCounted()
    {
        var locked = File(@"c\locked.bin", 500, Old); File(@"c\free.bin", 200, Old);
        var engine = Engine([new("C", CleanCategory.Apps, MaintenanceRisk.Low, [@"ROOT\c"], TimeSpan.Zero)]);
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = await engine.CleanAsync(["C"]);
            Assert.Equal(1, result.Deleted); Assert.Equal(200, result.BytesFreed); Assert.Equal(1, result.InUse);
        }
        Assert.True(System.IO.File.Exists(locked));
    }

    [Fact] public async Task ReadOnlyFilesAreRefusedNotForced()
    {
        var ro = File(@"r\ro.bin", 50, Old); System.IO.File.SetAttributes(ro, FileAttributes.ReadOnly);
        try
        {
            var result = await Engine([new("R", CleanCategory.Apps, MaintenanceRisk.Low, [@"ROOT\r"], TimeSpan.Zero)]).CleanAsync(["R"]);
            Assert.Equal(0, result.Deleted); Assert.Equal(1, result.Failed); Assert.True(System.IO.File.Exists(ro));
        }
        finally { System.IO.File.SetAttributes(ro, FileAttributes.Normal); }
    }

    [Fact] public async Task LinksAreNeverFollowed()
    {
        var outside = File(@"outside\precious.sav", 900, Old);
        Directory.CreateDirectory(Path.Combine(dir, "cache"));
        try { Directory.CreateSymbolicLink(Path.Combine(dir, "cache", "link"), Path.Combine(dir, "outside")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; } // symbolic links need developer mode: skip
        var engine = Engine([new("K", CleanCategory.Apps, MaintenanceRisk.Low, [@"ROOT\cache"], TimeSpan.Zero)]);
        var report = await engine.AnalyzeAsync();
        Assert.Empty(report.Findings);
        await engine.CleanAsync(["K"]);
        Assert.True(System.IO.File.Exists(outside));
        // a rule whose own folder is a link gives nothing either
        Directory.CreateSymbolicLink(Path.Combine(dir, "linkroot"), Path.Combine(dir, "outside"));
        Assert.Empty(Engine([new("J", CleanCategory.Apps, MaintenanceRisk.Low, [@"ROOT\linkroot"], TimeSpan.Zero)]).RootsOf(new("J", CleanCategory.Apps, MaintenanceRisk.Low, [@"ROOT\linkroot"], TimeSpan.Zero)));
    }

    [Fact] public async Task ProtectedFoldersAreRefusedEvenIfAVariablePointsThere()
    {
        var profile = Path.Combine(dir, "profile"); var doc = File(@"profile\Documents\thesis.docx", 100, Old);
        // TEMP misconfigured to the user's folder, or to a folder above it: nothing is scanned or removed.
        var engine = Engine([new("Bad", CleanCategory.Windows, MaintenanceRisk.Low, ["TEMP"], TimeSpan.Zero), new("Up", CleanCategory.Windows, MaintenanceRisk.Low, ["ROOT"], TimeSpan.Zero)], extra: new() { ["TEMP"] = profile });
        Assert.Empty((await engine.AnalyzeAsync()).Findings);
        var result = await engine.CleanAsync(["Bad", "Up"]);
        Assert.Equal(0, result.Deleted); Assert.True(System.IO.File.Exists(doc));
        Assert.True(engine.IsProtected(Path.GetPathRoot(dir)!));
    }

    [Fact] public async Task OpenProgramsBlockTheirCaches()
    {
        File(@"chrome\x.bin", 400, Old);
        var rule = new CleanRule("Chrome", CleanCategory.Browsers, MaintenanceRisk.Low, [@"ROOT\chrome"], TimeSpan.Zero, BlockedBy: ["chrome"]);
        var engine = Engine([rule], new Processes("chrome"));
        var f = Assert.Single((await engine.AnalyzeAsync()).Findings);
        Assert.True(f.Blocked);
        var result = await engine.CleanAsync(["Chrome"]);
        Assert.Equal(1, result.Skipped); Assert.Equal(0, result.Deleted);
    }

    [Fact] public async Task LotusOwnFilesAndWildcardProfilesAreHandled()
    {
        File(@"tmp\Lotus-update\setup.exe", 100, Old); File(@"tmp\LotusX.tmp", 100, Old);
        File(@"browser\Default\Cache\Cache_Data\f1", 10, Old); File(@"browser\Profile 1\Cache\Cache_Data\f2", 20, Old); var cookie = File(@"browser\Default\Network\Cookies", 30, Old);
        var engine = Engine([
            new("T", CleanCategory.Windows, MaintenanceRisk.Low, [@"ROOT\tmp"], TimeSpan.Zero),
            new("B", CleanCategory.Browsers, MaintenanceRisk.Low, [@"ROOT\browser\*\Cache\Cache_Data"], TimeSpan.Zero)]);
        var report = await engine.AnalyzeAsync();
        Assert.DoesNotContain(report.Findings, f => f.Rule.Id == "T");
        Assert.Equal(30, report.Findings.Single(f => f.Rule.Id == "B").Bytes);
        await engine.CleanAsync(["B"]);
        Assert.True(System.IO.File.Exists(cookie));                             // cookies are never in a clean rule
    }

    [Fact] public async Task CancellationStopsAndKeepsWhatWasCounted()
    {
        for (var i = 0; i < 50; i++) File($@"many\f{i}.tmp", 10, Old);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var engine = Engine([new("M", CleanCategory.Windows, MaintenanceRisk.Low, [@"ROOT\many"], TimeSpan.Zero)]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.AnalyzeAsync(token: cts.Token));
        var result = await engine.CleanAsync(["M"], token: cts.Token);
        Assert.True(result.Cancelled); Assert.Equal(0, result.Deleted);
        Assert.Equal(50, Directory.GetFiles(Path.Combine(dir, "many")).Length);
    }

    [Fact] public async Task RecycleBinIsNeverPreselectedAndReportsRealBytes()
    {
        var bin = new FakeBin(5000, 3);
        var vm = new CleanViewModel(Engine([.. CleanRules.All.Where(r => r.Id == "RecycleBin"), new("WinTemp", CleanCategory.Windows, MaintenanceRisk.Low, [@"ROOT\t"], TimeSpan.Zero)], bin: bin), new TextCatalog(), new MemoryMaintenanceJournal(), ex => throw ex);
        File(@"t\a.tmp", 100, Old);
        await vm.AnalyzeAsync();
        Assert.False(vm.Rows.Single(r => r.Id == "RecycleBin").Selected);
        Assert.True(vm.Rows.Single(r => r.Id == "WinTemp").Selected);
        Assert.Equal(100, vm.SelectedBytes);
        vm.Rows.Single(r => r.Id == "RecycleBin").Selected = true;
        await vm.CleanAsync();
        Assert.True(bin.Emptied);
        Assert.Empty(vm.Rows);                                                   // verified: a new analysis finds nothing left
    }

    [Fact] public async Task TheHistoryKeepsWhatWasDoneAndSurvivesARestart()
    {
        var file = Path.Combine(dir, "Maintenance", "history.json");
        File(@"t\a.tmp", 100, Old);
        var vm = new CleanViewModel(Engine([new("WinTemp", CleanCategory.Windows, MaintenanceRisk.Low, [@"ROOT\t"], TimeSpan.Zero)]), new TextCatalog(), new JsonMaintenanceJournal(file), ex => throw ex);
        await vm.AnalyzeAsync(); await vm.CleanAsync();
        var entry = Assert.Single(new JsonMaintenanceJournal(file).Recent());
        Assert.Equal("clean", entry.Tool); Assert.Contains("100 bytes", entry.Summary);
    }

    [Fact] public void BuiltInRulesNeverPointAtPersonalOrSensitiveData()
    {
        string[] forbidden = ["Cookies", "Login Data", "Saved Games", "Documents", "GPUCache", "ShaderCache", "DXCache", "GrShaderCache", "steamapps", "Sessions", "Local Storage", "IndexedDB", "userdata"];
        foreach (var rule in CleanRules.All)
            foreach (var root in rule.Roots)
                Assert.DoesNotContain(forbidden, f => root.Contains(f, StringComparison.OrdinalIgnoreCase));
        Assert.All(CleanRules.All.Where(r => r.Category == CleanCategory.Browsers), r => Assert.NotNull(r.BlockedBy));
        Assert.All(CleanRules.All.Where(r => r.Risk != MaintenanceRisk.Low), r => Assert.False(r.SelectedByDefault));
        var text = new TextCatalog();
        Assert.All(CleanRules.All, r => Assert.NotEqual(r.TitleKey, text[r.TitleKey]));    // every rule has a name
    }
}
