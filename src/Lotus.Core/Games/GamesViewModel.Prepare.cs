namespace Lotus.Core.Games;

// "Preparar e Jogar" behind every Jogar button: quick read-only checks, a question only for real problems, then the
// authorized Game profile (if any) and the usual launcher. Any failure here falls back to opening the game normally.
public enum PrepDecision { Play, Cancel, Rescan }

public sealed partial class GamesViewModel
{
    public PlayPreparation? Preparation { get; set; }
    public GamePrefsBook? PlayPrefs { get; set; }
    public Func<bool> PrepareByDefault { get; set; } = () => true;
    // The app shows the question (only when there is a warning or a blocking problem).
    public Func<GameEntry, PrepResult, Task<PrepDecision>>? AskBeforePlay { get; set; }
    // Notes worth a discreet notice (profile applied, saves need attention, or everything checked when asked to say so).
    public event EventHandler<(GameEntry Game, PrepResult Result, bool Quiet)>? Prepared;
    private readonly HashSet<string> preparing = new(StringComparer.OrdinalIgnoreCase);

    public GamePlayPrefs PrefsFor(GameEntry game) => PlayPrefs?.Get(game.Key) ?? new();
    public bool PrepareEnabled(GameEntry game) => PrefsFor(game).Prepare ?? PrepareByDefault();

    private async Task PrepareAndLaunchAsync(GameCard card, bool rescanned = false)
    {
        var game = card.Game;
        if (!PrepareEnabled(game)) { LaunchNow(card); return; }
        if (!preparing.Add(game.Key)) return; // a double click never prepares twice
        try
        {
            var prefs = PrefsFor(game);
            status = Text.Format("PrepChecking", card.Name); Notify();
            var result = await Preparation!.CheckAsync(game, prefs);
            Trace?.Invoke($"Play preparation {game.Name}: {(int)result.Took.TotalMilliseconds} ms [{string.Join(",", result.Findings.Select(f => f.Id))}]"); // to spot slow checks
            if (result.HasWarnings)
            {
                var decision = await AskBeforePlay!(game, result);
                if (decision == PrepDecision.Cancel) { status = ""; Notify(); return; }
                if (decision == PrepDecision.Rescan && !rescanned)
                {
                    await RefreshAsync();
                    var again = all.FirstOrDefault(g => string.Equals(g.Key, game.Key, StringComparison.OrdinalIgnoreCase));
                    preparing.Remove(game.Key);
                    if (again is null) { status = Text.Format("PrepGone", card.Name); Notify(); return; }
                    await PrepareAndLaunchAsync(Card(again), rescanned: true);
                    return;
                }
                Preparation.Snooze(game.Key, result.Findings); // "Jogar mesmo assim": the same warning is not repeated for a while
            }
            var findings = result.Findings.ToList();
            var applied = false;
            if (prefs.UseProfile)
            {
                var (ok, finding) = await Preparation.ApplyProfileAsync();
                applied = ok;
                if (finding is not null) findings.Add(finding);
                if (ok) PlayPrefs?.MarkRestore(game.Key, DateTimeOffset.Now); // put back after this session
            }
            LaunchNow(card);
            Prepared?.Invoke(this, (game, result with { Findings = findings, ProfileApplied = applied }, prefs.OnlyRelevant));
        }
        catch (Exception ex)
        {
            log(ex); // preparing is optional: the game still opens
            LaunchNow(card);
        }
        finally { preparing.Remove(game.Key); }
    }

    // Timing of the checks (set by the app to its log); never the content of notes.
    public Action<string>? Trace { get; set; }

    // After the session (or at start, if Lotus or the game closed unexpectedly): put back what "Preparar e Jogar"
    // applied, only where nothing else changed it since (the profile journal checks that).
    public async Task<bool?> RestoreAfterSessionAsync(string gameKey)
    {
        if (PlayPrefs is null || GameProfile is null || !PlayPrefs.PendingRestore.ContainsKey(gameKey)) return null;
        bool ok;
        try { ok = await GameProfile.UndoAsync(); }
        catch (Exception ex) { log(ex); ok = false; }
        PlayPrefs.ClearRestore(gameKey); // either restored, or changed meanwhile by someone else: never retried blindly
        return ok;
    }
}
