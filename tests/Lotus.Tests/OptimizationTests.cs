using Lotus.Core.Optimization;
using Lotus.Infrastructure.Optimization;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lotus.Tests;

public sealed class OptimizationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "LotusOptimizer-" + Guid.NewGuid());
    private string PathName => Path.Combine(directory, "test.db");
    private DisposableOptimizationStore Store => new(PathName);
    private OptimizationEngine Engine => new(Store, Store);
    [Fact] public async Task ApplyReopenRevertAndIdempotence()
    {
        var engine = Engine; var original = Store.Detect(); var plan = await engine.AnalyzeAsync();
        Assert.Equal(original, plan.Before);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ApplyAsync(plan.Id, false));
        Assert.Equal(original, Store.Detect());
        Assert.Equal(OperationStage.Applied, (await engine.ApplyAsync(plan.Id, true)).Stage);
        var applied = Store.Detect(); await engine.ApplyAsync(plan.Id, true); Assert.Equal(applied, Store.Detect());
        Assert.Equal(OperationStage.NoChange, (await engine.AnalyzeAsync()).Stage);
        var reopened = Engine;
        await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.RevertAsync(plan.Id, false));
        Assert.Equal(OperationStage.Reverted, (await reopened.RevertAsync(plan.Id, true)).Stage);
        var restored = Store.Detect(); Assert.Equal(original.Value, restored.Value);
        await reopened.RevertAsync(plan.Id, true); Assert.Equal(restored, Store.Detect());
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task ExternalChangesBlockApplyAndRollback(bool rollback)
    {
        var e = Engine; var p = await e.AnalyzeAsync(); if (rollback) await e.ApplyAsync(p.Id, true);
        var current = Store.Detect(); var external = new ConfigurationState("external", current.Revision + 1, Guid.NewGuid());
        Assert.True(Store.Exchange(current, external));
        var result = rollback ? await e.RevertAsync(p.Id, true) : await e.ApplyAsync(p.Id, true);
        Assert.Equal(OperationStage.Conflict, result.Stage); Assert.Equal(external, Store.Detect());
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task RecoveryObservesInterruptedApplyWithoutRepeatingMutation(bool wrote)
    {
        var e = Engine; var p = await e.AnalyzeAsync(); Store.Save(p with { Stage = OperationStage.Applying });
        if (wrote) Assert.True(Store.Exchange(p.Before, p.Desired));
        var before = Store.Detect(); var recovered = Assert.Single(await Engine.RecoverAsync());
        Assert.Equal(wrote ? OperationStage.Applied : OperationStage.Planned, recovered.Stage);
        Assert.Equal(before, Store.Detect());
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task RecoveryObservesInterruptedRollback(bool wrote)
    {
        var e = Engine; var p = await e.AnalyzeAsync(); p = await e.ApplyAsync(p.Id, true);
        var restored = new ConfigurationState(p.Before.Value, p.Desired.Revision + 1, p.Id);
        Store.Save(p with { Stage = OperationStage.Reverting, RestoreTarget = restored });
        if (wrote) Assert.True(Store.Exchange(p.Desired, restored));
        var result = Assert.Single(await Engine.RecoverAsync());
        Assert.Equal(wrote ? OperationStage.Reverted : OperationStage.Applied, result.Stage);
    }
    [Fact] public async Task PartialFailureLeavesDurableIntentAndRecovers()
    {
        var store = Store; var action = new FaultAction(store); var e = new OptimizationEngine(store, action);
        var p = await e.AnalyzeAsync(); await Assert.ThrowsAsync<IOException>(() => e.ApplyAsync(p.Id, true));
        Assert.Equal(OperationStage.Applying, store.Find(p.Id)!.Stage);
        Assert.Equal(p.Desired, store.Detect());
        Assert.Equal(OperationStage.Applied, Assert.Single(await Engine.RecoverAsync()).Stage);
    }
    [Fact] public async Task SeparateInstancesCannotExecuteUnderSameLease()
    {
        var e = Engine; var p = await e.AnalyzeAsync();
        using (Store.Acquire()) await Assert.ThrowsAsync<IOException>(() => Engine.ApplyAsync(p.Id, true));
        Assert.Equal(p.Before, Store.Detect());
    }
    [Fact] public async Task RecoveryDoesNotOverwriteUnknownState()
    {
        var p = await Engine.AnalyzeAsync(); Store.Save(p with { Stage = OperationStage.Applying });
        var external = new ConfigurationState("enabled", 1, Guid.NewGuid()); Store.Exchange(p.Before, external);
        Assert.Equal(OperationStage.Conflict, Assert.Single(await Engine.RecoverAsync()).Stage);
        Assert.Equal(external, Store.Detect());
    }
    [Fact] public async Task NewerSchemaIsRejectedWithoutMutation()
    {
        await Engine.AnalyzeAsync();
        using var db = new SqliteConnection($"Data Source={PathName};Pooling=False"); db.Open();
        using var cmd = db.CreateCommand(); cmd.CommandText = "PRAGMA user_version=99"; cmd.ExecuteNonQuery();
        await Assert.ThrowsAsync<InvalidDataException>(() => Engine.RecoverAsync());
        cmd.CommandText = "PRAGMA user_version"; Assert.Equal(99L, cmd.ExecuteScalar());
    }
    [Fact] public async Task JournalFailurePreventsMutation()
    {
        var store = Store; var e = new OptimizationEngine(new FaultJournal(store), store);
        var plan = await e.AnalyzeAsync();
        await Assert.ThrowsAsync<IOException>(() => e.ApplyAsync(plan.Id, true));
        Assert.Equal(plan.Before, store.Detect());
        Assert.Equal(OperationStage.Planned, store.Find(plan.Id)!.Stage);
    }
    [Fact] public async Task FailedVerificationCannotReportSuccess()
    {
        var store = Store; var e = new OptimizationEngine(store, new VerificationFault(store));
        var plan = await e.AnalyzeAsync();
        Assert.Equal(OperationStage.Conflict, (await e.ApplyAsync(plan.Id, true)).Stage);
    }
    private sealed class FaultJournal(DisposableOptimizationStore store) : IOptimizationJournal
    {
        public IDisposable Acquire() => store.Acquire();
        public void Save(OptimizationPlan plan)
        { if (plan.Stage == OperationStage.Applying) throw new IOException("Journal write failed."); store.Save(plan); }
        public OptimizationPlan? Find(Guid id) => store.Find(id);
        public IReadOnlyList<OptimizationPlan> List() => store.List();
    }
    private sealed class VerificationFault(DisposableOptimizationStore store) : IOptimizationAction
    {
        public ActionDefinition Definition => store.Definition;
        public Eligibility Eligibility => store.Eligibility;
        public ConfigurationState Detect() => store.Detect();
        public ConfigurationState Plan(ConfigurationState state, Guid operation) => store.Plan(state, operation);
        public bool Exchange(ConfigurationState expected, ConfigurationState desired)
        { store.Exchange(expected, desired with { Value = "unexpected" }); return true; }
    }
    private sealed class FaultAction(DisposableOptimizationStore store) : IOptimizationAction
    {
        public ActionDefinition Definition => store.Definition;
        public Eligibility Eligibility => store.Eligibility;
        public ConfigurationState Detect() => store.Detect();
        public ConfigurationState Plan(ConfigurationState state, Guid operation) => store.Plan(state, operation);
        public bool Exchange(ConfigurationState expected, ConfigurationState desired)
        { store.Exchange(expected, desired); throw new IOException("Simulated interruption after atomic mutation."); }
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
