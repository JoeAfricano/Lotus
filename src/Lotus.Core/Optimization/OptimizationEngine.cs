namespace Lotus.Core.Optimization;

public enum ActionRisk { Low }
public enum Eligibility { Eligible, Unsupported }
public enum OperationStage { Planned, Applying, Applied, Reverting, Reverted, Conflict, NoChange }
public sealed record ActionDefinition(string Id, string DescriptionKey, ActionRisk Risk, string Compatibility, bool Reversible);
public sealed record ConfigurationState(string? Value, long Revision, Guid? Writer);
public sealed record OptimizationPlan(Guid Id, string ActionId, ConfigurationState Before, ConfigurationState Desired,
    OperationStage Stage, DateTimeOffset CreatedUtc, ConfigurationState? RestoreTarget = null);
public interface IOptimizationAction
{
    ActionDefinition Definition { get; }
    Eligibility Eligibility { get; }
    ConfigurationState Detect();
    ConfigurationState Plan(ConfigurationState before, Guid operation);
    // Atomic compare-and-set: never overwrite a state different from expected.
    bool Exchange(ConfigurationState expected, ConfigurationState desired);
}
public interface IOptimizationJournal
{
    IDisposable Acquire();
    void Save(OptimizationPlan plan);
    OptimizationPlan? Find(Guid id);
    IReadOnlyList<OptimizationPlan> List();
}

// Only explicitly registered actions can execute. No scripts, commands or arbitrary paths.
public sealed class OptimizationEngine(IOptimizationJournal journal, IOptimizationAction action)
{
    public ActionDefinition Definition => action.Definition;
    private Task<T> Locked<T>(Func<T> work) => Task.Run(() => { using var lease = journal.Acquire(); return work(); });
    public Task<OptimizationPlan> AnalyzeAsync() => Locked(() =>
    {
        if (action.Eligibility != Eligibility.Eligible) throw new InvalidOperationException("Unsupported action.");
        if (journal.List().Any(p => p.Stage is OperationStage.Applying or OperationStage.Reverting))
            throw new InvalidOperationException("Recovery required.");
        var before = action.Detect(); var id = Guid.NewGuid(); var desired = action.Plan(before, id);
        var plan = new OptimizationPlan(id, action.Definition.Id, before, desired,
            desired == before ? OperationStage.NoChange : OperationStage.Planned, DateTimeOffset.UtcNow);
        journal.Save(plan); return plan;
    });
    public Task<OptimizationPlan> ApplyAsync(Guid id, bool confirmed) => Locked(() =>
    {
        if (!confirmed) throw new InvalidOperationException("Explicit confirmation required.");
        RequireRecovered();
        var p = Get(id);
        if (p.Stage == OperationStage.NoChange) return p;
        if (p.Stage == OperationStage.Applied) return action.Detect() == p.Desired ? p : Save(p with { Stage = OperationStage.Conflict });
        if (p.Stage != OperationStage.Planned || action.Eligibility != Eligibility.Eligible) throw new InvalidOperationException("Invalid execution state.");
        if (action.Detect() != p.Before) return Save(p with { Stage = OperationStage.Conflict });
        p = Save(p with { Stage = OperationStage.Applying }); // durable intent BEFORE mutation
        if (!action.Exchange(p.Before, p.Desired)) return Save(p with { Stage = OperationStage.Conflict });
        return Save(p with { Stage = action.Detect() == p.Desired ? OperationStage.Applied : OperationStage.Conflict });
    });
    public Task<OptimizationPlan> RevertAsync(Guid id, bool confirmed) => Locked(() =>
    {
        if (!confirmed) throw new InvalidOperationException("Explicit confirmation required.");
        RequireRecovered();
        var p = Get(id);
        if (p.Stage == OperationStage.Reverted) return p;
        if (p.Stage != OperationStage.Applied || !action.Definition.Reversible) throw new InvalidOperationException("Rollback unavailable.");
        if (action.Detect() != p.Desired) return Save(p with { Stage = OperationStage.Conflict });
        var target = new ConfigurationState(p.Before.Value, checked(p.Desired.Revision + 1), p.Id);
        p = Save(p with { Stage = OperationStage.Reverting, RestoreTarget = target });
        if (!action.Exchange(p.Desired, target)) return Save(p with { Stage = OperationStage.Conflict });
        return Save(p with { Stage = action.Detect() == target ? OperationStage.Reverted : OperationStage.Conflict });
    });
    public Task<IReadOnlyList<OptimizationPlan>> RecoverAsync() => Locked<IReadOnlyList<OptimizationPlan>>(() =>
    {
        foreach (var p in journal.List().Where(p => p.Stage is OperationStage.Applying or OperationStage.Reverting))
        {
            var current = action.Detect();
            var stage = p.ActionId != action.Definition.Id ? OperationStage.Conflict : p.Stage == OperationStage.Applying
                ? current == p.Desired ? OperationStage.Applied : current == p.Before ? OperationStage.Planned : OperationStage.Conflict
                : current == p.RestoreTarget ? OperationStage.Reverted : current == p.Desired ? OperationStage.Applied : OperationStage.Conflict;
            Save(p with { Stage = stage });
        }
        return journal.List(); // Recovery observes state; it NEVER resumes mutations automatically.
    });
    private OptimizationPlan Get(Guid id)
    {
        var plan = journal.Find(id) ?? throw new InvalidOperationException("Unknown plan.");
        if (plan.ActionId != action.Definition.Id) throw new InvalidOperationException("Unknown action.");
        return plan;
    }
    private OptimizationPlan Save(OptimizationPlan plan) { journal.Save(plan); return plan; }
    private void RequireRecovered()
    {
        if (journal.List().Any(p => p.Stage is OperationStage.Applying or OperationStage.Reverting))
            throw new InvalidOperationException("Recovery required.");
    }
}
