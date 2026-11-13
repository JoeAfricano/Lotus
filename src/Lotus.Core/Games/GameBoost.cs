namespace Lotus.Core.Games;

// Perfil Jogo (game sessions only): while a library game is open Lotus applies a few reversible Windows tweaks and puts
// every one back when the game closes. Each step records what it found before changing it; the record is written to
// disk first, so a crash or a power cut is undone at the next start. A step only reverts what is still the value Lotus
// set (if the player changed it meanwhile, it stays as the player left it).
public interface IBoostStep
{
    string Id { get; }
    // Applies the change and returns what it needs to undo it, or null when nothing changed (already as wanted,
    // unavailable on this PC). May throw: the session goes on without that step.
    string? Apply(RunningGame game);
    // Puts back the value saved by Apply. Returns false when the value had been changed by someone else (left alone).
    bool Revert(string saved);
}

public sealed record BoostRecord(string StepId, string Saved);
public interface IBoostJournal
{
    IReadOnlyList<BoostRecord> Load();
    void Save(IReadOnlyList<BoostRecord> records);
    void Clear();
}

// Which steps the player wants (Otimizar > "Ajustar o perfil Jogo"). Process priority keeps its own setting
// (InterfacePreferences.GamePriority) and its own engine (IGamePriority), which already restores on exit.
public sealed record BoostOptions(bool Power = true, bool Notifications = true, bool BackgroundRecording = true, bool CloseApps = false)
{
    public bool Wants(string stepId) => stepId switch
    {
        BoostSteps.Power => Power,
        BoostSteps.Notifications => Notifications,
        BoostSteps.BackgroundRecording => BackgroundRecording,
        BoostSteps.CloseApps => CloseApps,
        _ => false
    };
}

public static class BoostSteps
{
    public const string Power = "power";
    public const string Notifications = "notifications";
    public const string BackgroundRecording = "background-recording";
    public const string CloseApps = "close-apps";
    public static readonly string[] All = [Power, Notifications, BackgroundRecording, CloseApps];
}

public sealed record BoostResult(IReadOnlyList<string> Applied, IReadOnlyList<string> Failed);

public sealed class GameBoost(IEnumerable<IBoostStep> steps, IBoostJournal journal)
{
    private readonly IReadOnlyList<IBoostStep> steps = [.. steps];
    private readonly List<BoostRecord> active = [];
    private readonly object gate = new();
    public bool IsActive { get { lock (gate) return active.Count > 0; } }

    public BoostResult Start(RunningGame game, BoostOptions options)
    {
        lock (gate)
        {
            if (active.Count > 0) return new([], []); // already boosted for this session
            var applied = new List<string>(); var failed = new List<string>();
            foreach (var step in steps.Where(s => options.Wants(s.Id)))
            {
                try
                {
                    var saved = step.Apply(game);
                    if (saved is null) continue;
                    active.Add(new(step.Id, saved));
                    journal.Save([.. active]); // before the next change, so a crash never loses an undo
                    applied.Add(step.Id);
                }
                catch (Exception) { failed.Add(step.Id); }
            }
            return new(applied, failed);
        }
    }

    // Reverts in reverse order. Returns the steps that were put back.
    public IReadOnlyList<string> Stop()
    {
        lock (gate)
        {
            var done = RevertAll(active);
            active.Clear(); journal.Clear();
            return done;
        }
    }

    // At start: undoes a session that never got its Stop (Lotus closed by force, PC turned off).
    public IReadOnlyList<string> Recover()
    {
        lock (gate)
        {
            IReadOnlyList<BoostRecord> left;
            try { left = journal.Load(); } catch (Exception) { left = []; }
            if (left.Count == 0) return [];
            var done = RevertAll(left);
            journal.Clear();
            return done;
        }
    }

    private List<string> RevertAll(IReadOnlyList<BoostRecord> records)
    {
        var done = new List<string>();
        foreach (var r in records.Reverse())
        {
            var step = steps.FirstOrDefault(s => s.Id == r.StepId);
            try { if (step is not null && step.Revert(r.Saved)) done.Add(r.StepId); }
            catch (Exception) { /* left as is; the next start does not retry a step it could not read */ }
        }
        return done;
    }
}

// High performance plan for the game, when this PC has it and it is not already active.
public sealed class PowerBoostStep(Lotus.Core.Optimization.IPowerSchemeService power) : IBoostStep
{
    public string Id => BoostSteps.Power;
    public string? Apply(RunningGame game)
    {
        var target = Lotus.Core.Optimization.ProfileSuggestions.HighPerformance;
        if (!power.List().Any(s => s.Id == target)) return null;
        var before = power.Active();
        if (before == target) return null;
        power.SetActive(target);
        return before.ToString("D");
    }
    public bool Revert(string saved)
    {
        if (!Guid.TryParse(saved, out var before) || power.Active() != Lotus.Core.Optimization.ProfileSuggestions.HighPerformance) return false;
        if (!power.List().Any(s => s.Id == before)) return false;
        power.SetActive(before);
        return true;
    }
}
