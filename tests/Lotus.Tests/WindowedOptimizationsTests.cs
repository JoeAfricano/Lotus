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
        Assert.Equal(vm.Text["FeatApplied"], vm.FeatureStatus);
        row = Assert.Single(vm.Features); Assert.False(row.CanApply); Assert.True(row.CanUndo); Assert.False(row.NeedsAttention);
        await vm.UndoFeatureAsync(WindowedOptimizationsAction.FeatureId, confirmed: true);
        Assert.Equal(vm.Text["FeatReverted"], vm.FeatureStatus); Assert.Equal("AutoHDREnable=1;", dx.Value);
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
        await vm.RefreshAsync(); Assert.True(vm.IsGame); Assert.False(vm.CanUndoProfile);
        var changes = await vm.PreviewProfileAsync();
        Assert.Equal(2, changes.Count(c => !c.AlreadyApplied));
        Assert.Contains(changes, c => c.From == "Balanced" && c.To == "High performance");
        Assert.Equal(ProfileSuggestions.Balanced, power.Current); Assert.Equal(0, dx.Writes); // preview never mutates
        await vm.ApplyProfileAsync(changes);
        Assert.Equal(vm.Text.Format("PfApplied", 2), vm.ProfileStatus);
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

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
