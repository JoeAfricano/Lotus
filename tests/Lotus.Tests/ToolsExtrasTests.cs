using Lotus.Core;
using Lotus.Core.Maintenance;
using Lotus.Core.Optimization;
using Xunit;

namespace Lotus.Tests;

// Monitor, gaming features, Windows upkeep, game crashes and the check-up, all with fakes (nothing real changes).
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class ToolsExtrasTests
{
    private sealed class Features(params GameFeature[] list) : IGameFeatureInventory { public GameFeature[] List = list; public IReadOnlyList<GameFeature> Read() => List; }
    private sealed class Display(int current, int max) : IDisplayModes
    {
        public int Current = current;
        public DisplayModeState? Primary() => new(@"\\.\DISPLAY1", 1920, 1080, Current, max);
        public bool SetRefresh(string device, int hz) { Current = hz; return true; }
    }

    [Fact] public async Task TheMonitorIsRaisedThroughTheSameReversibleActionAndReadAgain()
    {
        var display = new Display(60, 144); var applied = false; var calls = new List<string>();
        var actions = new FeatureActions(id => { calls.Add("apply:" + id); display.Current = 144; applied = true; return Task.CompletedTask; },
            id => { calls.Add("undo:" + id); display.Current = 60; applied = false; return Task.CompletedTask; },
            id => id == DisplayRefreshAction.FeatureId, _ => applied, () => Task.CompletedTask, () => "");
        var vm = new GamingSetupViewModel(new Features(new GameFeature("gamemode", FeatureState.Off, "ms-settings:gaming-gamemode")), display, actions, new TextCatalog(), new MemoryMaintenanceJournal(), ex => throw ex);
        await vm.LoadAsync();
        Assert.True(vm.MonitorLow); Assert.True(vm.CanRaiseMonitor); Assert.Equal("60", vm.CurrentHz); Assert.Equal("144", vm.MaxHz);
        Assert.True(vm.Rows.Single().Attention);                                  // Game Mode off: worth adjusting
        Assert.False(vm.Rows.Single().CanApply);                                  // but no action registered for it here
        await vm.ApplyAsync(DisplayRefreshAction.FeatureId);
        Assert.False(vm.MonitorLow); Assert.True(vm.CanUndoMonitor);              // measured again, not assumed
        await vm.UndoAsync(DisplayRefreshAction.FeatureId);
        Assert.True(vm.MonitorLow);
        Assert.Equal(["apply:display-refresh", "undo:display-refresh"], calls);
        await vm.ApplyAsync("unknown");                                            // unknown ids never reach the actions
        Assert.Equal(2, calls.Count);
    }

    private sealed class Upkeep(WindowsStatus status, RestorePointResult restore) : IWindowsUpkeep
    {
        public WindowsStatus Read() => status;
        public Task<RestorePointResult> CreateRestorePointAsync(string d, CancellationToken t = default) => Task.FromResult(restore);
        public void OpenWindowsUpdate() { } public void OpenSystemRestore() { }
    }

    [Fact] public async Task PendingRestartAndRestorePointsAreReportedHonestly()
    {
        var text = new TextCatalog();
        var vm = new WindowsUpkeepViewModel(new Upkeep(new(true, false, false, TimeSpan.FromDays(12), true), new(true, false, 5, 5, null)), text, new MemoryMaintenanceJournal(), ex => throw ex);
        await vm.LoadAsync();
        Assert.True(vm.RestartNeeded); Assert.Contains("12", vm.UptimeText); Assert.Contains(text["WuUptimeFast"], vm.UptimeText);
        await vm.CreateRestorePointAsync();
        Assert.Equal(text["WuRestoreSkipped"], vm.RestoreStatus);                 // the list did not grow: not reported as created
        var ok = new WindowsUpkeepViewModel(new Upkeep(new(false, false, false, TimeSpan.FromHours(3), false), new(true, true, 5, 6, null)), text, new MemoryMaintenanceJournal(), ex => throw ex);
        await ok.LoadAsync(); await ok.CreateRestorePointAsync();
        Assert.False(ok.RestartNeeded); Assert.Equal(text.Format("WuRestoreCreated", 6), ok.RestoreStatus);
        var declined = new WindowsUpkeepViewModel(new Upkeep(new(false, false, false, TimeSpan.Zero, false), new(false, false, 0, 0, null)), text, new MemoryMaintenanceJournal(), ex => throw ex);
        await declined.CreateRestorePointAsync();
        Assert.Equal(text["WuRestoreDeclined"], declined.RestoreStatus);
    }

    private static readonly DateTime T = new(2026, 11, 1, 20, 0, 0);
    private static AppCrash Crash(string module = "ntdll.dll", CrashKind kind = CrashKind.Crash, string path = @"D:\SteamLibrary\steamapps\common\Palworld\Pal-Win64-Shipping.exe") =>
        new(T, kind, Path.GetFileName(path), path, module, "c0000005");

    [Theory]
    [InlineData("nvwgf2umx.dll", CrashCause.GpuDriver)]
    [InlineData("GameOverlayRenderer64.dll", CrashCause.Overlay)]
    [InlineData("d3d12.dll", CrashCause.GraphicsLibrary)]
    [InlineData("UnityPlayer.dll", CrashCause.Runtime)]
    [InlineData("Pal-Win64-Shipping.exe", CrashCause.Game)]
    public void TheFailingModuleHintsTheCause(string module, CrashCause cause) => Assert.Equal(cause, CrashRules.Cause(Crash(module), []));

    [Fact] public void WhatWindowsLoggedAtTheSameMomentWins()
    {
        Assert.Equal(CrashCause.GpuDriver, CrashRules.Cause(Crash(), [new(T.AddSeconds(-20), SystemSignal.GpuReset, "nvlddmkm")]));
        Assert.Equal(CrashCause.OutOfMemory, CrashRules.Cause(Crash(), [new(T.AddSeconds(30), SystemSignal.OutOfMemory, "Resource-Exhaustion-Detector")]));
        Assert.Equal(CrashCause.Game, CrashRules.Cause(Crash(), [new(T.AddHours(-3), SystemSignal.GpuReset, "nvlddmkm")]));   // too far apart: unrelated
        Assert.Equal(CrashCause.Hang, CrashRules.Cause(Crash(null!, CrashKind.Hang), []));
    }

    private sealed class Log(params AppCrash[] crashes) : ICrashLog
    {
        public IReadOnlyList<AppCrash> Crashes(TimeSpan since) => crashes;
        public IReadOnlyList<SignalEvent> Signals(TimeSpan since) => [new(T.AddDays(-1), SystemSignal.PowerLoss, "Kernel-Power")];
    }

    [Fact] public async Task OnlyGamesAreListedWithTheirLibraryName()
    {
        var vm = new GameCrashViewModel(new Log(Crash(), Crash(path: @"C:\Program Files\Office\WINWORD.EXE"), Crash(path: @"C:\Games\Modrinth\Modrinth App.exe")), new TextCatalog(), ex => throw ex)
        {
            GameExes = () => [@"C:\Games\Modrinth\Modrinth App.exe"],
            GameName = p => p.Contains("Palworld") ? "Palworld" : null,
        };
        await vm.LoadAsync();
        Assert.Equal(2, vm.Count);                                                  // Word is not a game
        Assert.Contains(vm.Rows, r => r.Title == "Palworld");
        Assert.Contains(vm.Rows, r => r.Title == "Modrinth App");
        Assert.Contains("1", vm.Status);                                            // the power loss is mentioned
    }

    private sealed class NoApps : Lotus.Core.Tools.IAppCloser { public IReadOnlyList<Lotus.Core.Tools.RunningApp> List() => []; public int CloseGracefully(Lotus.Core.Tools.RunningApp a) => 0; }
    private sealed class NoTemp : Lotus.Core.Tools.ITempCleaner { public Lotus.Core.Tools.TempScan Scan() => new(0, 0); public Lotus.Core.Tools.TempScan Recycle(IProgress<double>? p = null) => new(0, 0); }
    private sealed class NoNet : Lotus.Core.Tools.IConnectionTester { public Task<IReadOnlyList<Lotus.Core.Tools.PingStats>> RunAsync(CancellationToken t, IProgress<double>? p = null) => Task.FromResult<IReadOnlyList<Lotus.Core.Tools.PingStats>>([]); }
    private sealed class NoDisks : Lotus.Core.Tools.IDiskHealth { public (IReadOnlyList<Lotus.Core.Tools.DiskInfo>, IReadOnlyList<Lotus.Core.Tools.VolumeInfo>) Read() => ([], []); }
    private sealed class NoDriver : Lotus.Core.Tools.IDriverInfo { public Lotus.Core.Tools.GpuDriver? Read() => null; }
    private sealed class OneDisk : IDiskInspector
    {
        public IReadOnlyList<DiskDetail> Read() => [new("0", "SSD", "Ssd", "NVMe", 1L << 40, "Healthy", "OK", null, null, null, null, null, [new("C:", 20L << 30, 1000L << 30)])];
        public bool StartScan(string letter) => false;
    }

    [Fact] public async Task TheCheckupPutsWhatMattersFirstAndPointsToTheRightTool()
    {
        var text = new TextCatalog(); var journal = new MemoryMaintenanceJournal();
        var tools = new Lotus.Core.Tools.ToolsViewModel(new NoApps(), new NoTemp(), new NoNet(), new NoDisks(), new NoDriver(), text, ex => throw ex)
        {
            Gaming = new GamingSetupViewModel(new Features(new GameFeature("gamemode", FeatureState.On, "ms-settings:gaming-gamemode")), new Display(60, 165), null, text, journal, ex => throw ex),
            Crashes = new GameCrashViewModel(new Log(), text, ex => throw ex),
            DiskTool = new DiskToolViewModel(new OneDisk(), text, journal, ex => throw ex),
            Upkeep = new WindowsUpkeepViewModel(new Upkeep(new(true, false, false, TimeSpan.FromDays(1), false), new(false, false, 0, 0, null)), text, journal, ex => throw ex),
        };
        var checkup = new CheckupViewModel(tools, text, ex => throw ex);
        tools.Checkup = checkup;
        await checkup.RunAsync();
        Assert.True(checkup.Done);
        var first = checkup.Findings[0];
        Assert.Equal(CheckLevel.Attention, first.Level);                             // 60 Hz on a 165 Hz monitor comes first
        Assert.Equal("monitor", first.ToolId);
        Assert.Contains(checkup.Findings, f => f.ToolId == "store" && f.Level == CheckLevel.Attention);   // 2% free on C:
        Assert.Contains(checkup.Findings, f => f.ToolId == "windows" && f.Level == CheckLevel.Tip);
        Assert.Contains(checkup.Findings, f => f.ToolId == "crash" && f.Level == CheckLevel.Good);
        Assert.Equal(checkup.Findings.Select(f => f.Level).OrderByDescending(l => l), checkup.Findings.Select(f => f.Level));
        Assert.Equal(text.Format("CkHeadAttention", 2), checkup.Headline);
    }
}
