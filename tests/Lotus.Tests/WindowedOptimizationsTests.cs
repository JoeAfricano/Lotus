using Lotus.Core;
using Lotus.Core.Optimization;
using Lotus.Infrastructure.Optimization;
using Xunit;

namespace Lotus.Tests;

// All tests use an in-memory fake; the real HKCU value is never written by automated tests.
public sealed class WindowedOptimizationsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "LotusWindowedTests-" + Guid.NewGuid());
    private SqliteOptimizationJournal Journal => new(Path.Combine(directory, "journal.db"));
    private sealed class FakeDx(string? value = null, bool supported = true) : IDirectXUserSettings
    {
        public string? Value = value; public int Writes;
        public bool Supported => supported;
        public string? Read() => Value;
        public void Write(string? value) { Value = value; Writes++; }
    }
    private sealed class DxFeatures(FakeDx dx) : IGameFeatureInventory
    {
        public IReadOnlyList<GameFeature> Read() =>
            [new(WindowedOptimizationsAction.FeatureId, dx.Value is null ? FeatureState.SystemDefault : WindowedOptimizationsAction.IsEnabled(dx.Value) ? FeatureState.On : FeatureState.Off, "ms-settings:display-advancedgraphics")];
    }
    private sealed class FakePower : IPowerSchemeService
    {
        public IReadOnlyList<PowerScheme> List() => [new(ProfileSuggestions.Balanced, "Balanced"), new(ProfileSuggestions.HighPerformance, "High performance")];
        public Guid Active() => ProfileSuggestions.Balanced;
        public bool? OnAcPower() => true;
        public void SetActive(Guid id) => throw new InvalidOperationException("Not expected in these tests.");
    }
    private sealed class EmptyStartup : IStartupInventory { public StartupInventory Read() => new([], []); }
    private OptimizationEngine Engine(FakeDx dx) => new(Journal, new WindowedOptimizationsAction(dx));

    [Theory]
    [InlineData(null, "SwapEffectUpgradeEnable=1;")]
    [InlineData("AutoHDREnable=1;", "AutoHDREnable=1;SwapEffectUpgradeEnable=1;")]
    [InlineData("SwapEffectUpgradeEnable=0;VRROptimizeEnable=0;", "SwapEffectUpgradeEnable=1;VRROptimizeEnable=0;")]
    public void EnablingPreservesOtherPairsAndOrder(string? raw, string expected)
    {
        Assert.Equal(expected, WindowedOptimizationsAction.WithEnabled(raw));
        Assert.True(WindowedOptimizationsAction.IsEnabled(expected));
        Assert.False(WindowedOptimizationsAction.IsEnabled(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("SwapEffectUpgradeEnable=0;AutoHDREnable=1;")]
    public async Task ApplyVerifiesAndUndoRestoresExactOriginal(string? original)
    {
        var dx = new FakeDx(original); var engine = Engine(dx);
        var plan = await engine.AnalyzeAsync();
        Assert.Equal(OperationStage.Planned, plan.Stage); Assert.Equal(0, dx.Writes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ApplyAsync(plan.Id, false));
        plan = await engine.ApplyAsync(plan.Id, true);
        Assert.Equal(OperationStage.Applied, plan.Stage); Assert.True(WindowedOptimizationsAction.IsEnabled(dx.Value));
        plan = await engine.RevertAsync(plan.Id, true);
        Assert.Equal(OperationStage.Reverted, plan.Stage); Assert.Equal(original, dx.Value); Assert.Equal(2, dx.Writes);
    }

    [Fact] public async Task AlreadyEnabledIsNoChangeEvenWithDifferentFormatting()
    {
        var dx = new FakeDx(" SwapEffectUpgradeEnable = 1 ; AutoHDREnable=1"); var engine = Engine(dx);
        var plan = await engine.AnalyzeAsync();
        Assert.Equal(OperationStage.NoChange, plan.Stage);
        Assert.Equal(OperationStage.NoChange, (await engine.ApplyAsync(plan.Id, true)).Stage);
        Assert.Equal(0, dx.Writes);
    }

    [Fact] public async Task ExternalChangesAreNeverOverwritten()
    {
        var dx = new FakeDx("AutoHDREnable=1;"); var engine = Engine(dx);
        var plan = await engine.AnalyzeAsync();
        dx.Value = "AutoHDREnable=0;"; // user changed it before apply
        Assert.Equal(OperationStage.Conflict, (await engine.ApplyAsync(plan.Id, true)).Stage);
        Assert.Equal("AutoHDREnable=0;", dx.Value);
        var second = await engine.ApplyAsync((await engine.AnalyzeAsync()).Id, true);
        dx.Value = "SwapEffectUpgradeEnable=0;"; // user changed it after apply
        Assert.Equal(OperationStage.Conflict, (await engine.RevertAsync(second.Id, true)).Stage);
        Assert.Equal("SwapEffectUpgradeEnable=0;", dx.Value);
    }

    [Fact] public async Task UnsupportedWindowsBlocksAnalysis() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => Engine(new FakeDx(null, supported: false)).AnalyzeAsync());

    [Fact] public async Task SharedJournalKeepsEachActionsRecoveryIsolated()
    {
        var dx = new FakeDx(); var windowed = Engine(dx);
        var applied = await windowed.ApplyAsync((await windowed.AnalyzeAsync()).Id, true);
        var power = new OptimizationEngine(Journal, new PowerSchemeAction(new FakePower()));
        Assert.DoesNotContain(await power.RecoverAsync(), p => p.ActionId == WindowedOptimizationsAction.ActionId);
        Assert.Equal(OperationStage.Applied, Assert.Single(await windowed.RecoverAsync()).Stage);
        Assert.Equal(applied.Id, Journal.Find(applied.Id)!.Id);
    }

    [Fact] public async Task ViewModelAppliesAndUndoesFeatureWithConfirmation()
    {
        var dx = new FakeDx("AutoHDREnable=1;"); var power = new FakePower();
        var vm = new PowerProfilesViewModel(new OptimizationEngine(Journal, new PowerSchemeAction(power)), power, Journal, new EmptyStartup(), new TextCatalog(), _ => { },
            new DxFeatures(dx), Engine(dx));
        await vm.RefreshAsync();
        var row = Assert.Single(vm.Features); Assert.True(row.CanApply); Assert.False(row.CanUndo); Assert.True(row.NeedsAttention);
        await vm.ApplyFeatureAsync(WindowedOptimizationsAction.FeatureId, confirmed: true);
        Assert.Equal(vm.Text.Format("FeatApplied", vm.Text["FeatNamewindowed-optimizations"]), vm.FeatureStatus);
        row = Assert.Single(vm.Features); Assert.False(row.CanApply); Assert.True(row.CanUndo); Assert.False(row.NeedsAttention);
        await vm.UndoFeatureAsync(WindowedOptimizationsAction.FeatureId, confirmed: true);
        Assert.Equal(vm.Text.Format("FeatReverted", vm.Text["FeatNamewindowed-optimizations"]), vm.FeatureStatus); Assert.Equal("AutoHDREnable=1;", dx.Value);
        Assert.True(Assert.Single(vm.Features).CanApply);
    }

    private sealed class MutablePower : IPowerSchemeService
    {
        public Guid Current = ProfileSuggestions.Balanced;
        public IReadOnlyList<PowerScheme> List() => [new(ProfileSuggestions.Balanced, "Balanced"), new(ProfileSuggestions.HighPerformance, "High performance")];
        public Guid Active() => Current;
        public bool? OnAcPower() => true;
        public void SetActive(Guid id) => Current = id;
    }
    private PowerProfilesViewModel ProfileVm(MutablePower power, FakeDx dx) =>
        new(new OptimizationEngine(Journal, new PowerSchemeAction(power)), power, Journal, new EmptyStartup(), new TextCatalog(), _ => { }, new DxFeatures(dx), Engine(dx));

    [Fact] public async Task OneClickProfileAppliesEverythingThenUndoesEverything()
    {
        var power = new MutablePower(); var dx = new FakeDx("AutoHDREnable=1;"); var vm = ProfileVm(power, dx);
        await vm.RefreshAsync(); Assert.True(vm.IsGame); Assert.False(vm.CanUndoProfile); Assert.Equal("", vm.Status); // no stale "analyze first" hint
        var shown = 0; vm.ResultShown += (_, _) => shown++;
        var changes = await vm.PreviewProfileAsync();
        Assert.Equal(2, changes.Count(c => !c.AlreadyApplied));
        Assert.Contains(changes, c => c.From == "Balanced" && c.To == "High performance");
        Assert.Equal(ProfileSuggestions.Balanced, power.Current); Assert.Equal(0, dx.Writes); // preview never mutates
        await vm.ApplyProfileAsync(changes);
        Assert.Equal(vm.Text.Format("PfApplied", 2), vm.ProfileStatus); Assert.Equal(vm.Text.Format("PfResultApplied", vm.Text["PfGameTitle"]), vm.ResultTitle); Assert.True(vm.ResultOk); Assert.Equal(1, shown);
        Assert.Equal(ProfileSuggestions.HighPerformance, power.Current); Assert.True(WindowedOptimizationsAction.IsEnabled(dx.Value));
        Assert.True(vm.CanUndoProfile); Assert.False(vm.Features.Single().NeedsAttention);
        var again = await vm.PreviewProfileAsync(); Assert.All(again, c => Assert.True(c.AlreadyApplied));
        await vm.ApplyProfileAsync(again); Assert.Equal(vm.Text["PfPreviewNothing"], vm.ProfileStatus);
        await vm.UndoProfileAsync();
        Assert.Equal(vm.Text.Format("PfUndone", 2), vm.ProfileStatus);
        Assert.Equal(ProfileSuggestions.Balanced, power.Current); Assert.Equal("AutoHDREnable=1;", dx.Value); Assert.False(vm.CanUndoProfile);
    }

    [Fact] public async Task OneClickProfileNeverOverwritesExternalChanges()
    {
        var power = new MutablePower(); var dx = new FakeDx(); var vm = ProfileVm(power, dx);
        await vm.RefreshAsync();
        var changes = await vm.PreviewProfileAsync();
        dx.Value = "SwapEffectUpgradeEnable=0;"; // changed outside Lotus between preview and apply
        await vm.ApplyProfileAsync(changes);
        Assert.Equal(vm.Text.Format("PfAppliedWithConflicts", 1, 1), vm.ProfileStatus);
        Assert.Equal("SwapEffectUpgradeEnable=0;", dx.Value); Assert.Equal(ProfileSuggestions.HighPerformance, power.Current);
    }

    [Fact] public async Task DailyProfileOnlyIncludesItsOwnActions()
    {
        var power = new MutablePower(); var dx = new FakeDx(); var vm = ProfileVm(power, dx);
        await vm.RefreshAsync(); vm.SelectProfileCommand.Execute("daily");
        var changes = await vm.PreviewProfileAsync();
        Assert.Equal(vm.Text["PfRowPower"], Assert.Single(changes).Title);
        Assert.True(changes[0].AlreadyApplied); // Daily suggests Balanced, which is already active
    }

    private sealed class FakeRegistry : IUserRegistry
    {
        public Dictionary<string, int> Values = new(); public int Writes;
        public int? ReadDword(string path, string name) => Values.TryGetValue(path + "\\" + name, out var v) ? v : null;
        public void WriteDword(string path, string name, int? value) { Writes++; if (value is null) Values.Remove(path + "\\" + name); else Values[path + "\\" + name] = value.Value; }
    }
    private sealed class FakeDisplay(int current, int max, bool accept = true) : IDisplayModes
    {
        public int Current = current;
        public DisplayModeState? Primary() => new(@"\\.\DISPLAY1", 1920, 1080, Current, max);
        public bool SetRefresh(string device, int hz) { if (!accept) return false; Current = hz; return true; }
    }

    [Fact] public async Task RegistryActionsRespectWindowsDefaultsAndRestoreExactly()
    {
        var reg = new FakeRegistry();
        var recording = new OptimizationEngine(Journal, RegistryDwordAction.BackgroundRecording(reg));
        Assert.Equal(OperationStage.NoChange, (await recording.AnalyzeAsync()).Stage); // absent = off = already recommended
        reg.Values[@"Software\Microsoft\Windows\CurrentVersion\GameDVR\HistoricalCaptureEnabled"] = 1;
        var applied = await recording.ApplyAsync((await recording.AnalyzeAsync()).Id, true);
        Assert.Equal(OperationStage.Applied, applied.Stage); Assert.Equal(0, reg.Values[@"Software\Microsoft\Windows\CurrentVersion\GameDVR\HistoricalCaptureEnabled"]);
        Assert.Equal(OperationStage.Reverted, (await recording.RevertAsync(applied.Id, true)).Stage);
        Assert.Equal(1, reg.Values[@"Software\Microsoft\Windows\CurrentVersion\GameDVR\HistoricalCaptureEnabled"]);
        var gameMode = new OptimizationEngine(Journal, RegistryDwordAction.GameMode(reg));
        Assert.Equal(OperationStage.NoChange, (await gameMode.AnalyzeAsync()).Stage); // absent = on
        reg.Values[@"Software\Microsoft\GameBar\AutoGameModeEnabled"] = 0;
        var on = await gameMode.ApplyAsync((await gameMode.AnalyzeAsync()).Id, true);
        Assert.Equal(1, reg.Values[@"Software\Microsoft\GameBar\AutoGameModeEnabled"]);
        reg.Values[@"Software\Microsoft\GameBar\AutoGameModeEnabled"] = 0; // user changed it afterwards
        Assert.Equal(OperationStage.Conflict, (await gameMode.RevertAsync(on.Id, true)).Stage);
    }

    [Fact] public async Task DisplayRefreshRaisesToMaximumAndUndoes()
    {
        var display = new FakeDisplay(60, 144); var engine = new OptimizationEngine(Journal, new DisplayRefreshAction(display));
        var applied = await engine.ApplyAsync((await engine.AnalyzeAsync()).Id, true);
        Assert.Equal(OperationStage.Applied, applied.Stage); Assert.Equal(144, display.Current);
        Assert.Equal(OperationStage.Reverted, (await engine.RevertAsync(applied.Id, true)).Stage); Assert.Equal(60, display.Current);
        Assert.Equal(OperationStage.NoChange, (await new OptimizationEngine(Journal, new DisplayRefreshAction(new FakeDisplay(143, 144))).AnalyzeAsync()).Stage);
        var rejected = new OptimizationEngine(Journal, new DisplayRefreshAction(new FakeDisplay(60, 144, accept: false)));
        Assert.Equal(OperationStage.Conflict, (await rejected.ApplyAsync((await rejected.AnalyzeAsync()).Id, true)).Stage);
    }

    [Fact] public async Task ProfilesIncludeEveryRecommendedFeatureAction()
    {
        var power = new MutablePower(); var dx = new FakeDx(); var reg = new FakeRegistry(); var display = new FakeDisplay(60, 144);
        reg.Values[@"Software\Microsoft\Windows\CurrentVersion\GameDVR\HistoricalCaptureEnabled"] = 1;
        var engines = new Dictionary<string, OptimizationEngine>
        {
            ["background-recording"] = new(Journal, RegistryDwordAction.BackgroundRecording(reg)),
            ["gamemode"] = new(Journal, RegistryDwordAction.GameMode(reg)),
            [DisplayRefreshAction.FeatureId] = new(Journal, new DisplayRefreshAction(display))
        };
        var vm = new PowerProfilesViewModel(new OptimizationEngine(Journal, new PowerSchemeAction(power)), power, Journal, new EmptyStartup(), new TextCatalog(), _ => { },
            new DxFeatures(dx), Engine(dx), engines);
        await vm.RefreshAsync();
        var game = await vm.PreviewProfileAsync();
        Assert.Equal(5, game.Count); Assert.Equal(4, game.Count(c => !c.AlreadyApplied)); // power, windowed, recording, refresh; Game Mode already on
        Assert.Contains(game, c => c.From == "60 Hz" && c.To == "144 Hz");
        await vm.ApplyProfileAsync(game);
        Assert.Equal(vm.Text.Format("PfApplied", 4), vm.ProfileStatus); Assert.Equal(144, display.Current);
        vm.SelectProfileCommand.Execute("daily");
        var daily = await vm.PreviewProfileAsync();
        Assert.Equal([vm.Text["PfRowPower"], vm.Text["FeatNamegamemode"], vm.Text["FeatNamedisplay-refresh"]], daily.Select(c => c.Title).OrderBy(t => t == vm.Text["PfRowPower"] ? 0 : t == vm.Text["FeatNamegamemode"] ? 1 : 2));
        await vm.UndoProfileAsync();
        Assert.Equal(60, display.Current); Assert.Equal(1, reg.Values[@"Software\Microsoft\Windows\CurrentVersion\GameDVR\HistoricalCaptureEnabled"]);
    }

    private sealed class FakeApproval : IStartupApprovalStore
    {
        public Dictionary<string, byte[]> Values = new();
        public byte[]? Read(string kind, string name) => Values.GetValueOrDefault(kind + "\\" + name);
        public void Write(string kind, string name, byte[]? value) { if (value is null) Values.Remove(kind + "\\" + name); else Values[kind + "\\" + name] = value; }
    }
    private sealed class ApprovalInventory(FakeApproval approval) : IStartupInventory
    {
        public StartupInventory Read() => new([
            new("Discord", @"HKCU\…\Run", "discord.exe", StartupApproval.IsEnabled(approval.Read("Run", "Discord")), "Run", "Discord", UserScope: true),
            new("SecurityHealth", @"HKLM\…\Run", "sh.exe", true, "Run", "SecurityHealth", UserScope: false)], []);
    }

    [Fact] public async Task StartupToggleIsOptionalReversibleAndUserScopeOnly()
    {
        var approval = new FakeApproval(); var original = new byte[] { 6, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 };
        approval.Values["Run\\Discord"] = original;
        var power = new MutablePower();
        var vm = new PowerProfilesViewModel(new OptimizationEngine(Journal, new PowerSchemeAction(power)), power, Journal, new ApprovalInventory(approval), new TextCatalog(), _ => { },
            approval: approval, engineFactory: a => new OptimizationEngine(Journal, a));
        await vm.StartupCommand.ExecuteAsync(null);
        var discord = vm.StartupEntries.Single(r => r.Name == "Discord"); var system = vm.StartupEntries.Single(r => r.Name == "SecurityHealth");
        Assert.True(discord.CanToggle); Assert.Equal(vm.Text["StartupDisable"], discord.ToggleLabel); Assert.False(system.CanToggle);
        await vm.ToggleStartupAsync(discord);
        Assert.Equal(3, approval.Values["Run\\Discord"][0]); Assert.True(vm.StartupEntries.Single(r => r.Name == "Discord").IsDisabled);
        var disabled = vm.StartupEntries.Single(r => r.Name == "Discord"); Assert.Equal(vm.Text["StartupEnable"], disabled.ToggleLabel);
        await vm.ToggleStartupAsync(disabled);
        Assert.Equal(original, approval.Values["Run\\Discord"]); // exact bytes restored
        // Profiles never touch startup items.
        await vm.ApplyProfileAsync(await vm.PreviewProfileAsync()); Assert.Equal(original, approval.Values["Run\\Discord"]);
    }

    [Fact] public async Task StartupToggleNeverOverwritesExternalChanges()
    {
        var approval = new FakeApproval(); var action = new StartupToggleAction(approval, "Run", "Discord", disable: true, () => new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc));
        var engine = new OptimizationEngine(Journal, action);
        var plan = await engine.AnalyzeAsync(); Assert.Equal(OperationStage.Planned, plan.Stage); // absent = enabled
        approval.Values["Run\\Discord"] = [3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]; // disabled elsewhere meanwhile
        Assert.Equal(OperationStage.Conflict, (await engine.ApplyAsync(plan.Id, true)).Stage);
        Assert.Equal(OperationStage.NoChange, (await engine.AnalyzeAsync()).Stage); // already disabled
        Assert.Equal(Eligibility.Unsupported, new StartupToggleAction(approval, "Run32", "X", true).Eligibility);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
