using Lotus.Core;
using Lotus.Core.Tools;
using Xunit;

namespace Lotus.Tests;

public sealed class GuidedSetupTests
{
    private sealed class Store : IPreferencesStore
    {
        public InterfacePreferences Saved = new();
        public PreferencesLoadResult Load() => new(Saved, "Loaded");
        public bool TrySave(InterfacePreferences p) { Saved = p; return true; }
    }
    private sealed class Apps : IAppCloser
    {
        public List<RunningApp> Running = [new("Chrome", 3L << 30, true, [1]), new("Notepad", 20L << 20, true, [2])];
        public List<string> Closed = [];
        public IReadOnlyList<RunningApp> List() => Running;
        public int CloseGracefully(RunningApp app) { Closed.Add(app.Name); Running.RemoveAll(a => a.Name == app.Name); return 1; }
    }
    private sealed class Temp : ITempCleaner { public int Recycled; public TempScan Scan() => Recycled > 0 ? new(0, 0) : new(40, 200L << 20); public TempScan Recycle(IProgress<double>? p = null) { Recycled++; return new(40, 200L << 20); } }
    private sealed class Net : IConnectionTester { public Task<IReadOnlyList<PingStats>> RunAsync(CancellationToken t, IProgress<double>? p = null) => Task.FromResult<IReadOnlyList<PingStats>>([]); }
    private sealed class Disks : IDiskHealth { public (IReadOnlyList<DiskInfo>, IReadOnlyList<VolumeInfo>) Read() => ([], [new("C:", 5L << 30, 100L << 30)]); }
    private sealed class Driver : IDriverInfo { public GpuDriver? Read() => null; }

    [Fact] public async Task AsksThenAppliesOnlyWhatWasAcceptedAndShowsOnce()
    {
        var store = new Store(); var prefs = new PreferencesService(store); var apps = new Apps(); var temp = new Temp();
        var tools = new ToolsViewModel(apps, temp, new Net(), new Disks(), new Driver(), new TextCatalog(), ex => throw ex);
        var vm = new GuidedSetupViewModel(prefs, new TextCatalog(), ex => throw ex, tools: tools);
        Assert.True(vm.ShowWizard); Assert.True(vm.IsIntro);

        await vm.AnalyzeAsync();
        Assert.True(vm.IsQuestions);
        var tempQ = Assert.Single(vm.Questions, q => q.Id == "temp"); Assert.True(tempQ.Accepted);   // recommended
        var appsQ = Assert.Single(vm.Questions, q => q.Id == "apps"); Assert.False(appsQ.Accepted);  // closing apps is opt-in
        Assert.Equal(["Chrome"], appsQ.Apps.Select(a => a.Name));                                      // only heavy ones are offered
        Assert.Contains(vm.Questions, q => q.Id == "disk" && q.IsInfo);                                // 5% free: advice only
        Assert.Equal(1, vm.AcceptedCount);
        Assert.Empty(apps.Closed); Assert.Equal(0, temp.Recycled);                                     // analysing changed nothing

        appsQ.Accepted = true; appsQ.Apps[0].Selected = true;
        await vm.ApplyAsync();
        Assert.True(vm.IsDone); Assert.True(tempQ.IsOk); Assert.True(appsQ.IsOk);
        Assert.Equal(1, temp.Recycled); Assert.Equal(["Chrome"], apps.Closed);
        Assert.True(store.Saved.GuidedDone); Assert.Equal(2, store.Saved.GuidedChanges);

        vm.Close();
        Assert.False(vm.ShowWizard); Assert.True(vm.ShowReady);
        vm.Restart(); Assert.True(vm.ShowWizard); Assert.False(store.Saved.GuidedDone);
    }

    [Fact] public async Task DeclinedAnswersAreSkippedAndSkippingHidesTheWizard()
    {
        var store = new Store(); var prefs = new PreferencesService(store); var temp = new Temp();
        var tools = new ToolsViewModel(new Apps(), temp, new Net(), new Disks(), new Driver(), new TextCatalog(), ex => throw ex);
        var vm = new GuidedSetupViewModel(prefs, new TextCatalog(), ex => throw ex, tools: tools);
        await vm.AnalyzeAsync();
        var tempQ = vm.Questions.Single(q => q.Id == "temp"); tempQ.Accepted = false;
        Assert.Equal(0, vm.AcceptedCount);
        await vm.ApplyAsync();
        Assert.True(tempQ.IsSkipped); Assert.Equal(0, temp.Recycled); Assert.Equal(0, store.Saved.GuidedChanges);

        var fresh = new GuidedSetupViewModel(new PreferencesService(new Store()), new TextCatalog(), ex => throw ex);
        fresh.Close(); Assert.True(fresh.ShowReady); // "Not now" counts as seen
    }
}

public sealed class ReadinessTests
{
    [Fact] public void ScoreAndTheBiggestIssue()
    {
        var calm = PcReadiness.Compute(40, 10, 55, ("Notepad", 50L << 20), false);
        Assert.Equal(100, calm.Score); Assert.Equal(ReadinessIssue.None, calm.Issue);
        var full = PcReadiness.Compute(91, 10, 55, ("Chrome", 3L << 30), false);
        Assert.Equal(ReadinessIssue.Memory, full.Issue); Assert.True(full.Score < 70);
        var hot = PcReadiness.Compute(40, 10, 90, null, false);
        Assert.Equal(ReadinessIssue.GpuHot, hot.Issue);
        var heavy = PcReadiness.Compute(50, 10, null, ("Chrome", 2L << 30), false);
        Assert.Equal(ReadinessIssue.HeavyApps, heavy.Issue); Assert.Equal("Chrome", heavy.HeavyApp);
        Assert.InRange(PcReadiness.Compute(null, null, null, null, false).Score, 60, 90); // no sensors yet: neutral, not alarming
    }
}
