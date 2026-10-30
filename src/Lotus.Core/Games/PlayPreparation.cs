using System.Diagnostics;

namespace Lotus.Core.Games;

// "Preparar e Jogar": a few quick checks behind every Jogar button (Home, library, player panel, game page), then the
// game opens through the same launcher as before. Nothing here is slow or blocking: each check has a short time limit
// and an unavailable check is simply skipped. Only real problems become a question; everything else stays silent.

// Per-game choices (Meus Jogos › game page). Prepare null = follow the global setting.
public sealed record GamePlayPrefs
{
    public bool? Prepare { get; set; }
    public bool UseProfile { get; set; }       // apply the authorized Game profile before playing, restore after
    public bool CheckController { get; set; }  // the player prefers a controller for this game
    public bool CheckSystem { get; set; } = true; // memory and free space
    public bool OnlyRelevant { get; set; } = true; // no "all good" notice
}

public sealed class GamePrefsData
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;
    public Dictionary<string, GamePlayPrefs> Games { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    // Profile applied by "Preparar e Jogar" for a session and not restored yet (kept on disk so a crash of Lotus or of
    // the game still ends with the previous settings back).
    public Dictionary<string, DateTimeOffset> PendingRestore { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
public interface IGamePrefsStore { GamePrefsData Load(); void Save(GamePrefsData data); }

public sealed class GamePrefsBook
{
    private readonly IGamePrefsStore store;
    private readonly Action<Exception> log;
    private GamePrefsData data;
    public event EventHandler<string>? Changed;
    public GamePrefsBook(IGamePrefsStore store, Action<Exception> log)
    {
        this.store = store; this.log = log;
        try { data = Normalize(store.Load()); } catch (Exception ex) { log(ex); data = new(); }
    }
    private static GamePrefsData Normalize(GamePrefsData? d) => new()
    {
        Games = new(d?.Games ?? [], StringComparer.OrdinalIgnoreCase),
        PendingRestore = new(d?.PendingRestore ?? [], StringComparer.OrdinalIgnoreCase),
    };
    public GamePlayPrefs Get(string key) => data.Games.GetValueOrDefault(key) ?? new();
    public bool Set(string key, GamePlayPrefs prefs) => Commit(d => d.Games[key] = prefs, key);
    public IReadOnlyDictionary<string, DateTimeOffset> PendingRestore => data.PendingRestore;
    public bool MarkRestore(string key, DateTimeOffset at) => Commit(d => d.PendingRestore[key] = at, key);
    public bool ClearRestore(string key) => !data.PendingRestore.ContainsKey(key) || Commit(d => d.PendingRestore.Remove(key), key);
    private bool Commit(Action<GamePrefsData> change, string key)
    {
        var copy = new GamePrefsData { Games = new(data.Games, StringComparer.OrdinalIgnoreCase), PendingRestore = new(data.PendingRestore, StringComparer.OrdinalIgnoreCase) };
        change(copy);
        try { store.Save(copy); } catch (Exception ex) { log(ex); return false; }
        data = copy; Changed?.Invoke(this, key); return true;
    }
}

public enum PrepLevel { Info, Warning, Blocking }
public sealed record PrepFinding(string Id, PrepLevel Level, string Title, string Detail, string? Fix = null);
public sealed record PrepResult(IReadOnlyList<PrepFinding> Findings, bool ProfileApplied, TimeSpan Took)
{
    public bool Blocked => Findings.Any(f => f.Level == PrepLevel.Blocking);
    public bool HasWarnings => Findings.Any(f => f.Level >= PrepLevel.Warning);
}

// Read-only facts about the PC, each answered quickly or not at all.
public interface IPrepareProbes
{
    bool InstallPresent(GameEntry game);                 // install folder / executable still there (store URIs need nothing)
    long? FreeBytesFor(GameEntry game);                  // drive of the game (null = unknown)
    (double LoadPercent, IReadOnlyList<string> TopApps)? Memory(GameEntry game);
    int? Controllers();                                  // connected game controllers (null = unknown)
}

public sealed class PlayPreparation(IPrepareProbes probes, ITextCatalog text, Action<Exception> log)
{
    public IGameProfile? Profile { get; set; }
    public Func<GameEntry, string?>? SaveWarning { get; set; } // a short note when save protection needs attention
    public static readonly TimeSpan CheckLimit = TimeSpan.FromMilliseconds(600);
    public const double MemoryWarnPercent = 90;
    public const long LowSpaceBytes = 2L * 1024 * 1024 * 1024;
    // A warning the player already chose to ignore for this game is not repeated for a while.
    private readonly Dictionary<string, DateTimeOffset> snoozed = [];
    public static readonly TimeSpan SnoozeFor = TimeSpan.FromHours(12);
    public void Snooze(string gameKey, IEnumerable<PrepFinding> findings, DateTimeOffset? now = null)
    {
        foreach (var f in findings.Where(f => f.Level == PrepLevel.Warning)) snoozed[gameKey + "|" + f.Id] = (now ?? DateTimeOffset.Now) + SnoozeFor;
    }
    private bool IsSnoozed(string gameKey, string id, DateTimeOffset now) => snoozed.TryGetValue(gameKey + "|" + id, out var until) && until > now;

    public async Task<PrepResult> CheckAsync(GameEntry game, GamePlayPrefs prefs, CancellationToken token = default, DateTimeOffset? now = null)
    {
        var clock = Stopwatch.StartNew();
        var at = now ?? DateTimeOffset.Now;
        var key = game.Key;
        var findings = new List<PrepFinding>();

        // A — the game is still where the library found it.
        var present = await Quick(() => probes.InstallPresent(game), token);
        if (present == false)
            findings.Add(new("missing", PrepLevel.Blocking, text["PrepMissingTitle"], text.Format("PrepMissingBody", game.Name), "rescan"));

        // D — memory and free space, only on clear evidence.
        if (prefs.CheckSystem && present != false)
        {
            var memory = Quick(() => probes.Memory(game), token);
            var space = Quick(() => probes.FreeBytesFor(game), token);
            if (await memory is { } m && m.LoadPercent >= MemoryWarnPercent && !IsSnoozed(key, "memory", at))
                findings.Add(new("memory", PrepLevel.Warning, text["PrepMemoryTitle"],
                    m.TopApps.Count > 0 ? text.Format("PrepMemoryBody", Math.Round(m.LoadPercent), string.Join(", ", m.TopApps.Take(3))) : text.Format("PrepMemoryBodyPlain", Math.Round(m.LoadPercent)), "memory"));
            if (await space is { } free && free < LowSpaceBytes && !IsSnoozed(key, "space", at))
                findings.Add(new("space", PrepLevel.Warning, text["PrepSpaceTitle"], text.Format("PrepSpaceBody", Math.Round(free / 1024d / 1024 / 1024, 1)), "space"));
        }

        // C — the preferred controller (never blocks: most games also work with keyboard and mouse).
        if (prefs.CheckController && await Quick(probes.Controllers, token) is 0 && !IsSnoozed(key, "controller", at))
            findings.Add(new("controller", PrepLevel.Warning, text["PrepControllerTitle"], text["PrepControllerBody"]));

        // E — save protection that needs attention: a note, never a reason to stop the game.
        if (SaveWarning?.Invoke(game) is { Length: > 0 } saveNote)
            findings.Add(new("saves", PrepLevel.Info, text["PrepSavesTitle"], saveNote));

        return new(findings, false, clock.Elapsed);
    }

    // B — the Game profile the player authorized for this game, applied only after the checks (and the player's
    // answer, when there was a question) and only when something is not applied yet. Returns null when nothing changed.
    public async Task<(bool Applied, PrepFinding? Finding)> ApplyProfileAsync(CancellationToken token = default)
    {
        if (Profile is not { } profile) return (false, null);
        try
        {
            // No time limit here on purpose: a change still running in the background would escape the restore after the session.
            var steps = await profile.PreviewAsync();
            if (steps.All(s => s.AlreadyApplied)) return (false, null);
            var applied = await profile.ApplyAsync();
            return (applied, applied
                ? new("profile", PrepLevel.Info, text["PrepProfileTitle"], text["PrepProfileApplied"])
                : new("profile", PrepLevel.Warning, text["PrepProfileTitle"], text["PrepProfilePartial"]));
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { log(ex); return (false, new("profile", PrepLevel.Warning, text["PrepProfileTitle"], text["PrepProfilePartial"])); }
    }

    // Runs a probe off the UI thread with a short time limit; null (unknown) when it is too slow or fails. Unknown is
    // never treated as "no": a slow disk must not make Lotus say a game is missing.
    private async Task<T?> Quick<T>(Func<T> probe, CancellationToken token) where T : struct
    {
        try
        {
            var task = Task.Run(probe, token);
            return await Task.WhenAny(task, Task.Delay(CheckLimit, token)) == task ? await task : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { log(ex); return null; }
    }
    private async Task<T?> Quick<T>(Func<T?> probe, CancellationToken token) where T : struct
    {
        try
        {
            var task = Task.Run(probe, token);
            return await Task.WhenAny(task, Task.Delay(CheckLimit, token)) == task ? await task : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { log(ex); return null; }
    }}
