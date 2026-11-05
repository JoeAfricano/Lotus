using Lotus.Core;
using Lotus.Core.Maintenance;
using Lotus.Infrastructure.Maintenance;
using Xunit;

namespace Lotus.Tests;

// Memory and processes with a fake sampler: nothing real is ever closed.
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class ProcessesTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "lotus-proc-" + Guid.NewGuid().ToString("N"));
    public ProcessesTests() => Directory.CreateDirectory(dir);
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

    private sealed class FakeSampler(List<ProcessSample> running) : IProcessSampler
    {
        public List<string> Closed { get; } = []; public List<string> Forced { get; } = [];
        public Task<IReadOnlyList<ProcessSample>> SampleAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<ProcessSample>>([.. running]);
        public int CloseGracefully(ProcessSample app) { Closed.Add(app.ExeName); if (app.HasWindow) { running.RemoveAll(p => p.ExeName == app.ExeName); return 1; } return 0; }
        public int ForceEnd(ProcessSample app) { Forced.Add(app.ExeName); running.RemoveAll(p => p.ExeName == app.ExeName); return 1; }
    }
    private static ProcessSample P(string exe, long mb, ProcessKind kind = ProcessKind.Normal, bool window = true, double cpu = 2) => new(exe, exe, $@"C:\Apps\{exe}.exe", mb << 20, cpu, window, [1], kind);

    [Theory]
    [InlineData("svchost", null, ProcessKind.System)]
    [InlineData("EasyAntiCheat", null, ProcessKind.AntiCheat)]
    [InlineData("vgc", null, ProcessKind.AntiCheat)]
    [InlineData("RTSS", null, ProcessKind.Lotus)]
    [InlineData("PresentMon-x64", null, ProcessKind.Lotus)]
    [InlineData("Palworld-Win64-Shipping", @"D:\SteamLibrary\steamapps\common\Palworld\Pal.exe", ProcessKind.Game)]
    [InlineData("chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", ProcessKind.Normal)]
    public void ProcessesAreClassifiedSafely(string exe, string? path, ProcessKind kind) => Assert.Equal(kind, ProcessRules.Classify(exe, path));

    [Fact] public void KnownGameExecutablesAreProtectedWherever_TheyLive()
        => Assert.Equal(ProcessKind.Game, ProcessRules.Classify("Modrinth App", @"C:\Games\Modrinth\Modrinth App.exe", [@"C:\Games\Modrinth\Modrinth App.exe"]));

    [Fact] public async Task OnlyAllowedProgramsCanBeSelectedAndClosed()
    {
        var sampler = new FakeSampler([P("chrome", 900), P("EasyAntiCheat", 200, ProcessKind.AntiCheat), P("Game", 4000, ProcessKind.Game), P("svchost", 300, ProcessKind.System)]);
        var vm = new ProcessesViewModel(sampler, new MemoryCloseList(), new TextCatalog(), new MemoryMaintenanceJournal(), ex => throw ex);
        await vm.RefreshAsync();
        Assert.Equal(4, vm.Rows.Count);
        foreach (var r in vm.Rows) r.Selected = true;                        // even if the UI let it, protected ones are ignored
        Assert.Equal(["chrome"], vm.Chosen.Select(c => c.Sample.ExeName));
        await vm.CloseSelectedAsync();
        Assert.Equal(["chrome"], sampler.Closed);
        Assert.DoesNotContain(vm.Rows, r => r.Sample.ExeName == "chrome");     // measured again: really gone
        Assert.Contains("900", vm.Status);
    }

    [Fact] public async Task WhatDoesNotCloseIsReportedNotHidden()
    {
        var sampler = new FakeSampler([P("tray", 500, window: false)]);
        var journal = new MemoryMaintenanceJournal();
        var vm = new ProcessesViewModel(sampler, new MemoryCloseList(), new TextCatalog(), journal, ex => throw ex);
        await vm.RefreshAsync(); vm.Rows.Single().Selected = true;
        await vm.CloseSelectedAsync();
        Assert.Single(vm.Rows);                                               // still open: nothing invented
        Assert.Empty(sampler.Forced);                                         // forcing never happens by itself
        Assert.False(vm.Rows.Single().Selected);                              // the list was measured again; nothing stays ticked
        vm.Rows.Single().Selected = true;
        await vm.CloseSelectedAsync(force: true);
        Assert.Equal(["tray"], sampler.Forced);
        Assert.Contains(journal.Recent(), e => e.Action == "force-end");
    }

    private sealed class Net : Lotus.Core.Tools.IConnectionTester
    {
        public Task<IReadOnlyList<Lotus.Core.Tools.PingStats>> RunAsync(CancellationToken t, IProgress<double>? p = null) =>
            Task.FromResult<IReadOnlyList<Lotus.Core.Tools.PingStats>>([new("gateway", 2, 1, 0), new("Cloudflare 1.1.1.1", 20, 2, 0), new("Google 8.8.8.8", 30, 4, 5)]);
    }
    private sealed class NoApps : Lotus.Core.Tools.IAppCloser { public IReadOnlyList<Lotus.Core.Tools.RunningApp> List() => []; public int CloseGracefully(Lotus.Core.Tools.RunningApp a) => 0; }
    private sealed class NoTemp : Lotus.Core.Tools.ITempCleaner { public Lotus.Core.Tools.TempScan Scan() => new(0, 0); public Lotus.Core.Tools.TempScan Recycle(IProgress<double>? p = null) => new(0, 0); }
    private sealed class NoDisks : Lotus.Core.Tools.IDiskHealth { public (IReadOnlyList<Lotus.Core.Tools.DiskInfo>, IReadOnlyList<Lotus.Core.Tools.VolumeInfo>) Read() => ([], []); }
    private sealed class NoDriver : Lotus.Core.Tools.IDriverInfo { public Lotus.Core.Tools.GpuDriver? Read() => null; }

    [Fact] public async Task ConnectionTestsKeepASimpleMeasuredHistory()
    {
        var tools = new Lotus.Core.Tools.ToolsViewModel(new NoApps(), new NoTemp(), new Net(), new NoDisks(), new NoDriver(), new TextCatalog(), ex => throw ex)
        { Journal = new JsonMaintenanceJournal(Path.Combine(dir, "history.json")) };
        await tools.TestConnectionAsync(); await tools.TestConnectionAsync();
        Assert.Equal(2, tools.NetHistory.Count);
        Assert.Contains("25", tools.NetHistory[0].Detail);                     // average of the two internet targets
        Assert.True(tools.NetHistory[0].Warning);                               // 5% loss is shown, not hidden
        var again = new Lotus.Core.Tools.ToolsViewModel(new NoApps(), new NoTemp(), new Net(), new NoDisks(), new NoDriver(), new TextCatalog(), ex => throw ex)
        { Journal = new JsonMaintenanceJournal(Path.Combine(dir, "history.json")) };
        again.LoadNetHistory();
        Assert.Equal(2, again.NetHistory.Count);
    }

    [Fact] public async Task LightProgramsAreHiddenUnlessAskedOrListed()
    {
        var store = new JsonCloseList(Path.Combine(dir, "close.json"));
        var sampler = new FakeSampler([P("heavy", 800), P("tiny", 20, cpu: 0), P("helper", 10, cpu: 0)]);
        var vm = new ProcessesViewModel(sampler, store, new TextCatalog(), new MemoryMaintenanceJournal(), ex => throw ex);
        await vm.RefreshAsync();
        Assert.Equal(["heavy"], vm.Rows.Select(r => r.Sample.ExeName));
        vm.ShowAll = true; Assert.Equal(3, vm.Rows.Count);
        vm.ToggleInList(vm.Rows.Single(r => r.Sample.ExeName == "helper"));
        vm.ShowAll = false;
        Assert.Contains(vm.Rows, r => r.Sample.ExeName == "helper");          // listed programs always show
        // the list survives a restart and ticks only what is open
        var again = new ProcessesViewModel(sampler, new JsonCloseList(Path.Combine(dir, "close.json")), new TextCatalog(), new MemoryMaintenanceJournal(), ex => throw ex);
        await again.RefreshAsync();
        Assert.Equal(1, again.SelectCloseList());
        Assert.Equal(["helper"], again.Chosen.Select(c => c.Sample.ExeName));
    }

}

// Reads every real process once (read-only). Runs alone so its short burst of work never disturbs timing tests.
[CollectionDefinition("RealSystem", DisableParallelization = true)] public sealed class RealSystemCollection;
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
[Collection("RealSystem")]
public sealed class RealProcessTests
{
    [Fact] public async Task RealSamplingReadsThisProcessWithoutChangingAnything()
    {
        var samples = await new WindowsProcessSampler(intervalMs: 200).SampleAsync();
        var me = samples.FirstOrDefault(s => s.Pids.Contains(Environment.ProcessId));
        Assert.NotNull(me); Assert.True(me!.Bytes > 0); Assert.True(me.CpuPercent >= 0);
        Assert.Contains(samples, s => s.Kind == ProcessKind.System);
    }
}
