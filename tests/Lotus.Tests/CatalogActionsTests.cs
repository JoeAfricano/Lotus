using Lotus.Core.Optimization;
using Lotus.Infrastructure.Optimization;
using Xunit;

namespace Lotus.Tests;

// Optimization catalog (winutil-based items). In-memory fakes only: the real Windows settings are never touched here.
public sealed class CatalogActionsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "LotusCatalogTests-" + Guid.NewGuid());
    private SqliteOptimizationJournal Journal => new(Path.Combine(directory, "journal.db"));
    public void Dispose() { try { Directory.Delete(directory, true); } catch (IOException) { } }

    private sealed class FakeKeys(AccessibilityKeyFlags? flags) : IAccessibilityKeys
    {
        public AccessibilityKeyFlags? Flags = flags; public int Writes;
        public AccessibilityKeyFlags? Read() => Flags;
        public void Write(AccessibilityKeyFlags f) { Writes++; Flags = f; }
    }
    private sealed class FakeMouse(int[]? values) : IMouseSettings
    {
        public int[]? Values = values;
        public int[]? Read() => Values?.ToArray();
        public void Write(int[] v) => Values = v.ToArray();
    }
    private sealed class FakeRegistry : IUserRegistry
    {
        public Dictionary<string, int> Values = new();
        public int? ReadDword(string path, string name) => Values.TryGetValue(path + "\\" + name, out var v) ? v : null;
        public void WriteDword(string path, string name, int? value) { if (value is null) Values.Remove(path + "\\" + name); else Values[path + "\\" + name] = value.Value; }
    }

    [Fact] public async Task AccessibilityShortcutsTurnOffOnlyTheHotkeyBitAndUndoExactly()
    {
        // Windows defaults: Sticky 510, Filter 126, Toggle 62 (shortcuts on, features off). One feature in use (Sticky on = bit 1).
        var keys = new FakeKeys(new(511, 126, 62));
        var engine = new OptimizationEngine(Journal, new AccessibilityHotkeysAction(keys));
        var applied = await engine.ApplyAsync((await engine.AnalyzeAsync()).Id, true);
        Assert.Equal(OperationStage.Applied, applied.Stage);
        Assert.Equal(new AccessibilityKeyFlags(507, 122, 58), keys.Flags); // Sticky Keys itself stays on (bit 1 kept)
        Assert.False(AccessibilityHotkeysAction.AnyShortcutOn(keys.Flags!));
        Assert.Equal(OperationStage.Reverted, (await engine.RevertAsync(applied.Id, true)).Stage);
        Assert.Equal(new AccessibilityKeyFlags(511, 126, 62), keys.Flags);
        Assert.Equal(OperationStage.NoChange, (await new OptimizationEngine(Journal, new AccessibilityHotkeysAction(new FakeKeys(new(506, 122, 58)))).AnalyzeAsync()).Stage);
    }

    [Fact] public async Task AccessibilityShortcutsChangedMeanwhileBecomeConflict()
    {
        var keys = new FakeKeys(new(510, 126, 62));
        var engine = new OptimizationEngine(Journal, new AccessibilityHotkeysAction(keys));
        var plan = await engine.AnalyzeAsync();
        keys.Flags = new(506, 126, 62); // changed in Settings before applying
        Assert.Equal(OperationStage.Conflict, (await engine.ApplyAsync(plan.Id, true)).Stage);
        Assert.Equal(0, keys.Writes);
        Assert.Equal(Eligibility.Unsupported, new AccessibilityHotkeysAction(new FakeKeys(null)).Eligibility);
    }

    [Fact] public async Task MouseAccelerationKeepsThresholdsAndUndoes()
    {
        var mouse = new FakeMouse([6, 10, 1]);
        var engine = new OptimizationEngine(Journal, new MouseAccelerationAction(mouse));
        var applied = await engine.ApplyAsync((await engine.AnalyzeAsync()).Id, true);
        Assert.Equal([6, 10, 0], mouse.Values!);
        Assert.Equal(OperationStage.Reverted, (await engine.RevertAsync(applied.Id, true)).Stage);
        Assert.Equal([6, 10, 1], mouse.Values!);
        Assert.Equal(OperationStage.NoChange, (await new OptimizationEngine(Journal, new MouseAccelerationAction(new FakeMouse([0, 0, 0]))).AnalyzeAsync()).Stage);
        Assert.Null(MouseAccelerationAction.Parse("1,2"));
    }

    [Fact] public async Task EndTaskIsOfferedOnlyOnSupportedWindowsAndRemovesTheValueOnUndo()
    {
        var reg = new FakeRegistry();
        Assert.Equal(Eligibility.Unsupported, RegistryDwordAction.TaskbarEndTask(reg, () => false).Eligibility);
        var engine = new OptimizationEngine(Journal, RegistryDwordAction.TaskbarEndTask(reg, () => true));
        var applied = await engine.ApplyAsync((await engine.AnalyzeAsync()).Id, true);
        Assert.Equal(1, reg.Values[RegistryDwordAction.EndTaskPath + @"\TaskbarEndTask"]);
        Assert.Equal(OperationStage.Reverted, (await engine.RevertAsync(applied.Id, true)).Stage);
        Assert.Empty(reg.Values); // it did not exist before: removed, not set to 0
    }

    [Theory]
    [InlineData(CatalogFeatures.AccessibilityHotkeys, FeatureState.SystemDefault, true)]
    [InlineData(CatalogFeatures.AccessibilityHotkeys, FeatureState.Off, false)]
    [InlineData(CatalogFeatures.MouseAcceleration, FeatureState.On, true)]
    [InlineData(CatalogFeatures.TaskbarEndTask, FeatureState.SystemDefault, true)]
    [InlineData(CatalogFeatures.TaskbarEndTask, FeatureState.Unavailable, false)]
    public void CatalogItemsAreRecommendedForGames(string id, FeatureState state, bool attention)
    {
        Assert.Equal(attention, GameFeatureRules.NeedsAttention(new(id, state, "ms-settings:x"), "game"));
        Assert.False(GameFeatureRules.NeedsAttention(new(id, state, "ms-settings:x"), "daily"));
    }

    // "Apply profile" never includes catalog items: they change how the PC feels outside games, so they are one by one.
    [Fact] public async Task ProfilePreviewLeavesCatalogItemsOut()
    {
        var mouse = new FakeMouse([6, 10, 1]);
        var engines = new Dictionary<string, OptimizationEngine>
        {
            [CatalogFeatures.MouseAcceleration] = new(Journal, new MouseAccelerationAction(mouse)),
            [CatalogFeatures.AccessibilityHotkeys] = new(Journal, new AccessibilityHotkeysAction(new FakeKeys(new(510, 126, 62)))),
        };
        var power = new SinglePower();
        var vm = new PowerProfilesViewModel(new OptimizationEngine(Journal, new PowerSchemeAction(power)), power, Journal, new NoStartup(), new Lotus.Core.TextCatalog(), _ => { },
            featureEngines: engines);
        await vm.RefreshAsync();
        var changes = await vm.PreviewProfileAsync();
        Assert.DoesNotContain(changes, c => c.Plan.ActionId is MouseAccelerationAction.ActionId or AccessibilityHotkeysAction.ActionId);
        await vm.ApplyFeatureAsync(CatalogFeatures.MouseAcceleration, confirmed: true); // one by one still works
        Assert.Equal([6, 10, 0], mouse.Values!);
        Assert.True(vm.IsFeatureApplied(CatalogFeatures.MouseAcceleration));
        await vm.UndoFeatureAsync(CatalogFeatures.MouseAcceleration, confirmed: true);
        Assert.Equal([6, 10, 1], mouse.Values!);
    }

    private sealed class SinglePower : IPowerSchemeService
    {
        private static readonly Guid Id = ProfileSuggestions.Balanced;
        public IReadOnlyList<PowerScheme> List() => [new(Id, "Balanced")];
        public Guid Active() => Id;
        public bool? OnAcPower() => true;
        public void SetActive(Guid id) { }
    }
    private sealed class NoStartup : IStartupInventory { public StartupInventory Read() => new([], []); }
}
