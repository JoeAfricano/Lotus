namespace Lotus.Core.Games;

// "Preparar e jogar": one confirmation lists what changes; Lotus applies the Game profile (journaled, reversible),
// optionally pre-loads shaders, launches the game, follows it, and asks about undo after it closes.
public sealed record ProfileStep(string Title, string From, string To, bool AlreadyApplied);
public interface IGameProfile
{
    Task<IReadOnlyList<ProfileStep>> PreviewAsync();   // Game profile changes from the current state
    Task<bool> ApplyAsync();                           // applies the last preview; true when every pending step applied
    Task<bool> UndoAsync();                            // restores Lotus's latest changes where nothing changed them since
}
public sealed record PlayPlan(GameCard Card, IReadOnlyList<ProfileStep> Steps, bool OfferShaders, bool ShadersRecommended)
{
    public int Pending => Steps.Count(s => !s.AlreadyApplied);
}
public sealed record PlaySessionEnded(GameEntry Game, bool ProfileApplied, TimeSpan Played);

public interface IGameWatch { Task<TimeSpan?> WaitForSessionAsync(GameEntry game, TimeSpan startTimeout, CancellationToken token); }

// Waits for the game to start (launchers can take a while), then until its last process exits.
public sealed class PollingGameWatch(IGameProcessCheck processes, TimeSpan? poll = null) : IGameWatch
{
    private readonly TimeSpan every = poll ?? TimeSpan.FromSeconds(5);
    public async Task<TimeSpan?> WaitForSessionAsync(GameEntry game, TimeSpan startTimeout, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + startTimeout;
        while (!processes.IsRunning(game))
        {
            if (DateTime.UtcNow > deadline) return null; // never started (launcher closed, login prompt...)
            await Task.Delay(every, token);
        }
        var started = DateTime.UtcNow;
        while (processes.IsRunning(game)) await Task.Delay(every, token);
        return DateTime.UtcNow - started;
    }
}

// The same "which game is open" check Lotus already does every few seconds (light mode), shared: anything that needs to
// wait for a game to start or close listens here instead of polling the process list on its own.
public sealed class SessionHub : IGameWatch
{
    private GameEntry? current;
    private DateTimeOffset since;
    private readonly List<Waiter> waiters = [];
    private sealed class Waiter(GameEntry game, DateTimeOffset deadline)
    {
        public GameEntry Game = game; public DateTimeOffset Deadline = deadline; public DateTimeOffset? Started;
        public readonly TaskCompletionSource<TimeSpan?> Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public event EventHandler<GameEntry>? Started;
    public event EventHandler<(GameEntry Game, TimeSpan Played)>? Stopped;

    // Called with each check's result (null = no library game open).
    public void Observe(GameEntry? running, DateTimeOffset now)
    {
        Waiter[] list; lock (waiters) list = [.. waiters];
        if (current is not null && !Same(current, running))
        {
            var played = now - since; var ended = current; current = null;
            Stopped?.Invoke(this, (ended, played));
            foreach (var w in list.Where(w => w.Started is not null && Same(w.Game, ended))) Finish(w, now - w.Started!.Value);
        }
        if (running is not null && current is null)
        {
            current = running; since = now;
            Started?.Invoke(this, running);
            foreach (var w in list.Where(w => w.Started is null && Same(w.Game, running))) w.Started = now;
        }
        foreach (var w in list.Where(w => w.Started is null && now > w.Deadline)) Finish(w, null); // never started (launcher closed, login…)
    }
    private void Finish(Waiter w, TimeSpan? played) { lock (waiters) waiters.Remove(w); w.Done.TrySetResult(played); }
    private static bool Same(GameEntry a, GameEntry? b) => b is not null && string.Equals(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);

    public async Task<TimeSpan?> WaitForSessionAsync(GameEntry game, TimeSpan startTimeout, CancellationToken token)
    {
        var w = new Waiter(game, DateTimeOffset.Now + startTimeout);
        if (current is not null && Same(game, current)) w.Started = since;
        lock (waiters) waiters.Add(w);
        using var reg = token.Register(() => { lock (waiters) waiters.Remove(w); w.Done.TrySetCanceled(token); });
        return await w.Done.Task;
    }
}
