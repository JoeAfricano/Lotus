using System.ComponentModel;
using Lotus.Core;
using Lotus.Core.Optimization;
using Lotus.Infrastructure.Optimization;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Lotus.Tests;

public sealed class PowerProfilesTests(ITestOutputHelper output) : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "LotusPowerTests-" + Guid.NewGuid());
    private SqliteOptimizationJournal Journal => new(Path.Combine(directory, "journal.db"));
    private sealed class FakePower : IPowerSchemeService
    {
        public Guid Current = ProfileSuggestions.Balanced;
        public int Writes;
        public bool Denied, Interrupted;
        public List<PowerScheme> Schemes = [new(ProfileSuggestions.Balanced, "Balanced"), new(ProfileSuggestions.HighPerformance, "High performance")];
        public IReadOnlyList<PowerScheme> List() => Schemes;
        public Guid Active() => Current;
        public bool? OnAcPower() => true;
        public void SetActive(Guid id)
        {
            if (Denied) throw new Win32Exception(5);
            Current = id; Writes++;
            if (Interrupted) throw new IOException("Interrupted after native mutation.");
        }
    }
    private sealed class EmptyStartup : IStartupInventory { public StartupInventory Read() => new([], []); }
    private OptimizationEngine Engine(FakePower power) => new(Journal, new PowerSchemeAction(power));
    [Fact] public async Task RealContractAppliesAndRestoresUsingFakeApiOnly()
    {
        var power = new FakePower(); var engine = Engine(power);
        var p = await engine.AnalyzeAsync(ProfileSuggestions.HighPerformance.ToString(), "game");
        Assert.Equal("game", Journal.Find(p.Id)!.ProfileId); Assert.Equal(0, power.Writes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ApplyAsync(p.Id, false));
        Assert.Equal(OperationStage.Applied, (await engine.ApplyAsync(p.Id, true)).Stage);
        await engine.ApplyAsync(p.Id, true); Assert.Equal(1, power.Writes);
        Assert.Equal(OperationStage.Reverted, (await engine.RevertAsync(p.Id, true)).Stage);
        Assert.Equal(ProfileSuggestions.Balanced, power.Current); Assert.Equal(2, power.Writes);
    }
    [Fact] public async Task MissingPlanAndExternalChangeCannotOverwrite()
    {
        var power = new FakePower(); var engine = Engine(power);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.AnalyzeAsync(Guid.NewGuid().ToString()));
        var p = await engine.AnalyzeAsync(ProfileSuggestions.HighPerformance.ToString());
        power.Current = Guid.NewGuid();
        Assert.Equal(OperationStage.Conflict, (await engine.ApplyAsync(p.Id, true)).Stage); Assert.Equal(0, power.Writes);
    }
    [Fact] public async Task ExternalChangeBlocksUndoAndProfileSwitchConflicts()
    {
        var power = new FakePower(); var engine = Engine(power);
        var a = await engine.AnalyzeAsync(ProfileSuggestions.HighPerformance.ToString(), "game");
        await engine.ApplyAsync(a.Id, true);
        var b = await engine.AnalyzeAsync(ProfileSuggestions.Balanced.ToString(), "daily");
        await engine.ApplyAsync(b.Id, true);
        Assert.Equal(OperationStage.Conflict, (await engine.RevertAsync(a.Id, true)).Stage);
        Assert.Equal(2, power.Writes);
    }
    [Fact] public async Task SameDesiredPlanRequiresNoWrite()
    {
        var power = new FakePower(); var p = await Engine(power).AnalyzeAsync(power.Current.ToString(), "daily");
        Assert.Equal(OperationStage.NoChange, p.Stage); Assert.Equal(0, power.Writes);
    }
    [Fact] public async Task DeletedTargetBeforeApplyDoesNotWrite()
    {
        var power = new FakePower(); var engine = Engine(power);
        var plan = await engine.AnalyzeAsync(ProfileSuggestions.HighPerformance.ToString(), "game");
        power.Schemes.RemoveAt(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ApplyAsync(plan.Id, true));
        Assert.Equal(0, power.Writes);
        Assert.Equal(OperationStage.Conflict, Assert.Single(await engine.RecoverAsync()).Stage);
    }
    [Fact] public async Task ViewModelAppliesAndShowsActualUndoTarget()
    {
        var power = new FakePower();
        var vm = new PowerProfilesViewModel(Engine(power), power, Journal, new EmptyStartup(), new TextCatalog(), _ => { });
        await vm.RefreshAsync(); await vm.AnalyzeCommand.ExecuteAsync(null);
        vm.Confirmed = true; await vm.ApplyCommand.ExecuteAsync(null);
        Assert.Equal(OperationStage.Applied, vm.SelectedOperation!.Plan.Stage);
        Assert.False(vm.Confirmed);
        Assert.Equal(vm.Text.Format("PowerUndoDifference", "High performance", "Balanced"), vm.Summary);
        vm.Confirmed = true; await vm.RevertCommand.ExecuteAsync(null);
        Assert.Equal(OperationStage.Reverted, vm.SelectedOperation!.Plan.Stage);
        Assert.Equal(ProfileSuggestions.Balanced, power.Current);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task FailureNeverFabricatesOwnershipDuringRecovery(bool interrupted)
    {
        var power = new FakePower { Denied = !interrupted, Interrupted = interrupted }; var e = Engine(power);
        var p = await e.AnalyzeAsync(ProfileSuggestions.HighPerformance.ToString());
        await Assert.ThrowsAnyAsync<Exception>(() => e.ApplyAsync(p.Id, true));
        Assert.Equal(OperationStage.Applying, Journal.Find(p.Id)!.Stage);
        Assert.Equal(OperationStage.Conflict, Assert.Single(await Engine(power).RecoverAsync()).Stage);
        Assert.Equal(interrupted ? 1 : 0, power.Writes);
    }
    [Fact] public void SuggestionsAreConditionalAndNeverInventPlans()
    {
        var power = new FakePower();
        Assert.Equal(ProfileSuggestions.HighPerformance, ProfileSuggestions.Suggest("game", power.List(), true));
        Assert.Equal(ProfileSuggestions.Balanced, ProfileSuggestions.Suggest("game", power.List(), null));
        Assert.Equal(ProfileSuggestions.Balanced, ProfileSuggestions.Suggest("daily", power.List(), true));
        Assert.Null(ProfileSuggestions.Suggest("custom", power.List(), true));
        Assert.Null(ProfileSuggestions.Suggest("game", [], true));
    }
    [Fact] public async Task ProfilesPersistAndSelectionInvalidatesConfirmation()
    {
        var power = new FakePower();
        var vm = new PowerProfilesViewModel(Engine(power), power, Journal, new EmptyStartup(), new TextCatalog(), _ => { });
        await vm.RefreshAsync(); vm.SelectProfileCommand.Execute("custom");
        vm.SelectedScheme = vm.Schemes[1]; await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(ProfileSuggestions.HighPerformance, Assert.Single(Journal.LoadProfiles()).PowerSchemeId);
        await vm.AnalyzeCommand.ExecuteAsync(null); vm.Confirmed = true; Assert.True(vm.ApplyCommand.CanExecute(null));
        vm.SelectProfileCommand.Execute("daily"); Assert.False(vm.Confirmed); Assert.False(vm.ApplyCommand.CanExecute(null));
        vm.SelectProfileCommand.Execute("custom"); Assert.Equal(ProfileSuggestions.HighPerformance, vm.SelectedScheme!.Id);
        vm.PowerEnabled = false; await vm.SaveCommand.ExecuteAsync(null);
        var reopened = new PowerProfilesViewModel(Engine(power), power, Journal, new EmptyStartup(), new TextCatalog(), _ => { });
        await reopened.RefreshAsync(); reopened.SelectProfileCommand.Execute("custom");
        Assert.False(reopened.PowerEnabled); Assert.False(reopened.AnalyzeCommand.CanExecute(null)); Assert.Equal(0, power.Writes);
    }
    [Fact] public async Task VersionOneMigrationPreservesJournalAndState()
    {
        Directory.CreateDirectory(directory); var path = Path.Combine(directory, "journal.db");
        using (var db = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            db.Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "CREATE TABLE operations(id TEXT PRIMARY KEY,payload TEXT NOT NULL); CREATE TABLE test_setting(id INTEGER PRIMARY KEY,value TEXT,revision INTEGER,writer TEXT); INSERT INTO test_setting VALUES(1,'keep',3,NULL); PRAGMA user_version=1;"; cmd.ExecuteNonQuery();
        }
        Journal.SaveProfile(new("custom", ProfileSuggestions.Balanced));
        var store = new DisposableOptimizationStore(path); Assert.Equal("keep", store.Detect().Value);
        Assert.Empty(await new OptimizationEngine(store, store).RecoverAsync());
        Assert.Single(Journal.LoadProfiles());
    }
    [Fact, Trait("Category", "WindowsReadOnly")]
    public void NativeReadOnlyInventoryNeverCallsSetActive()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var service = new WindowsPowerSchemes(); var before = service.Active(); var schemes = service.List(); var after = service.Active();
        Assert.NotEmpty(schemes); Assert.Equal(before, after);
        output.WriteLine($"Active={before}; AC={service.OnAcPower()}");
        foreach (var s in schemes) output.WriteLine($"{s.Id} {s.Name}");
        var startup = new WindowsStartupInventory().Read(); output.WriteLine($"Startup entries={startup.Entries.Count}, unavailable sources={startup.Errors.Count}");
        Assert.Equal(startup.Entries.Count, startup.Entries.DistinctBy(e => (e.Location, e.Name)).Count());
        output.WriteLine($"Enabled={startup.Entries.Count(e => e.Enabled == true)} Disabled={startup.Entries.Count(e => e.Enabled == false)} Unknown={startup.Entries.Count(e => e.Enabled is null)}");
    }
    [Theory]
    [InlineData(null, true)]
    [InlineData(new byte[0], true)]
    [InlineData(new byte[] { 2, 0, 0 }, true)]
    [InlineData(new byte[] { 6, 0 }, true)]
    [InlineData(new byte[] { 3, 0 }, false)]
    [InlineData(new byte[] { 7, 0 }, false)]
    public void StartupApprovalFollowsTaskManagerConvention(byte[]? value, bool enabled) => Assert.Equal(enabled, StartupApproval.IsEnabled(value));
    [Fact]
    public void StartupApprovalIgnoresNonBinaryValues() => Assert.True(StartupApproval.IsEnabled("x"));
    private sealed class FakeFeatures(params GameFeature[] items) : IGameFeatureInventory { public IReadOnlyList<GameFeature> Read() => items; }
    [Theory]
    [InlineData("gamemode", FeatureState.Off, "game", true)]
    [InlineData("gamemode", FeatureState.SystemDefault, "game", false)]
    [InlineData("background-recording", FeatureState.On, "game", true)]
    [InlineData("background-recording", FeatureState.SystemDefault, "game", false)]
    [InlineData("windowed-optimizations", FeatureState.SystemDefault, "game", false)]
    [InlineData("windowed-optimizations", FeatureState.Off, "game", true)]
    [InlineData("hags", FeatureState.Off, "game", false)]
    [InlineData("background-recording", FeatureState.On, "daily", false)]
    [InlineData("gamemode", FeatureState.Unavailable, "game", false)]
    [InlineData("gamemode", FeatureState.Off, "custom", false)]
    public void GameFeatureRulesOnlyFlagKnownDifferences(string id, FeatureState state, string profile, bool attention) =>
        Assert.Equal(attention, GameFeatureRules.NeedsAttention(new(id, state, "ms-settings:x"), profile));
    [Fact] public async Task ViewModelRebuildsFeatureRowsPerProfileWithoutWriting()
    {
        var power = new FakePower();
        var features = new FakeFeatures(new("gamemode", FeatureState.Off, "ms-settings:gaming-gamemode"), new("background-recording", FeatureState.On, "ms-settings:gaming-gamedvr"),
            new("windowed-optimizations", FeatureState.SystemDefault, "ms-settings:display-advancedgraphics"), new("hags", FeatureState.On, "ms-settings:display-advancedgraphics"));
        var vm = new PowerProfilesViewModel(Engine(power), power, Journal, new EmptyStartup(), new TextCatalog(), _ => { }, features);
        await vm.RefreshAsync();
        Assert.Equal(4, vm.Features.Count); Assert.Equal(2, vm.Features.Count(f => f.NeedsAttention));
        Assert.Equal(vm.Text.Format("FeatAttention", 2), vm.FeaturesSummary);
        vm.SelectProfileCommand.Execute("daily"); Assert.Equal(1, vm.Features.Count(f => f.NeedsAttention));
        vm.SelectProfileCommand.Execute("custom"); Assert.Equal(vm.Text["FeatAllGood"], vm.FeaturesSummary);
        Assert.All(vm.Features, f => Assert.StartsWith("ms-settings:", f.SettingsUri));
        Assert.Equal(0, power.Writes);
    }
    [Fact] public void RealWindowsGameFeaturesAreReadOnlyQueries()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var before = new WindowsPowerSchemes().Active();
        var items = new WindowsGameFeatures().Read();
        Assert.Equal(["gamemode", "background-recording", "windowed-optimizations", "hags"], items.Select(i => i.Id));
        Assert.Equal(before, new WindowsPowerSchemes().Active());
        foreach (var i in items) output.WriteLine($"{i.Id}={i.State}");
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
