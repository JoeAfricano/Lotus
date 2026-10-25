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
