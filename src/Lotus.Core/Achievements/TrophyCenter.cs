using Lotus.Core.Diary;

namespace Lotus.Core.Achievements;

// What has been earned so far. Kept on this PC; achievements and XP never go backwards.
public sealed class TrophyLedger
{
    public Dictionary<string, DateTimeOffset> Earned { get; set; } = [];
    public Dictionary<string, int> Activity { get; set; } = [];
    public Dictionary<string, int> SteamCounts { get; set; } = []; // unlocked Steam achievements per game at the last sync
    public int SteamXp { get; set; }
    public DateTimeOffset? LastSync { get; set; }
    public Dictionary<string, string> Names { get; set; } = []; // store names of Steam games
}
public interface ITrophyLedgerStore { TrophyLedger Load(); void Save(TrophyLedger ledger); }

// News for the toast: freshly earned Lotus achievements and new Steam unlocks since the last sync.
public sealed record TrophyNews(IReadOnlyList<TrophyState> Trophies, int NewSteam, string? SteamGame, bool FirstImport, int SteamTotal, int SteamGames, int XpGained);

// Ties the diary, Steam and Lotus activity into one progression: Lotus achievements, Steam XP and the diary level.
public sealed class TrophyCenter
{
    private readonly GameDiary diary;
    private readonly ISteamAchievementSource steam;
    private readonly IAchievementRarity rarity;
    private readonly ITrophyLedgerStore store;
    private readonly Func<IReadOnlyDictionary<string, int>> playtimeSource;
    private readonly Func<int> installed;
    private readonly Action<Exception> log;
    private readonly TrophyLedger ledger;
    private readonly object gate = new();
    private IReadOnlyDictionary<string, int> playtime = new Dictionary<string, int>();
    private bool syncing;
    private readonly Func<string, CancellationToken, Task<string?>>? names; // store name for games whose Steam data has none

    public TrophyCenter(GameDiary diary, ISteamAchievementSource steam, IAchievementRarity rarity, ITrophyLedgerStore store,
        Func<IReadOnlyDictionary<string, int>> playtime, Func<int> installed, Action<Exception> log, Func<string, CancellationToken, Task<string?>>? names = null, bool listen = true)
    {
        this.names = names;
        this.diary = diary; this.steam = steam; this.rarity = rarity; this.store = store; playtimeSource = playtime; this.installed = installed; this.log = log;
        try { ledger = store.Load(); } catch (Exception ex) { log(ex); ledger = new(); }
        diary.ExtraXp = () => ExtraXp;
        diary.Changed += (_, _) => Evaluate();
        if (listen) Activity.Recorded += Record; // tests call Record directly
        // The first Evaluate is left to the app, after it has subscribed to News.
    }

    public event EventHandler<TrophyNews>? News;
    public event EventHandler? Changed;
    public IReadOnlyList<SteamGameAchievements> Steam { get; private set; } = [];
    public IReadOnlyList<TrophyState> States { get; private set; } = [];
    public int TrophyXp => States.Where(s => s.Unlocked).Sum(s => s.Def.Xp);
    public int SteamXp => ledger.SteamXp;
    public int ExtraXp => TrophyXp + SteamXp;
    public DateTimeOffset? LastSync => ledger.LastSync;
    public bool HasSteam => Steam.Count > 0;
    public SteamGameAchievements? Game(string? appId) => appId is null ? null : Steam.FirstOrDefault(g => g.AppId == appId);

    private readonly SynchronizationContext? ui = SynchronizationContext.Current; // the screen thread, when there is one
    public void Record(string id)
    {
        if (ui is not null && SynchronizationContext.Current != ui) { ui.Post(_ => Record(id), null); return; }
        lock (gate) ledger.Activity[id] = ledger.Activity.GetValueOrDefault(id) + 1;
        Evaluate(dirty: true);
    }
    public bool Syncing => syncing;

    // Reads Steam's local achievement cache and hours (off the UI thread), then fills in global rarity.
    public async Task SyncAsync(CancellationToken token = default)
    {
        if (syncing) return;
        syncing = true; Changed?.Invoke(this, EventArgs.Empty); // shows "reading…"
        try
        {
            var (games, hours) = await Task.Run(() => (steam.Read(), playtimeSource()), token);
            playtime = hours;
            if (Steam.Count == 0) { Steam = games; Changed?.Invoke(this, EventArgs.Empty); } // list the games while rarity loads
            using var slots = new SemaphoreSlim(6);
            var enriched = await Task.WhenAll(games.Select(async g =>
            {
                await slots.WaitAsync(token);
                try
                {
                    // Steam's cache sometimes has a codename (or no name): the store name is looked up once and kept.
                    string? known; lock (gate) known = ledger.Names.GetValueOrDefault(g.AppId);
                    if (known is null && names is not null && await names(g.AppId, token) is { Length: > 0 } name) { known = name; lock (gate) ledger.Names[g.AppId] = name; }
                    if (known is { Length: > 0 }) g = g with { Name = known };
                    if (g.Unlocked == 0) return g;
                    var rates = await rarity.GetAsync(g.AppId, token);
                    return rates is null ? g : g with { Items = [.. g.Items.Select(a => a with { Percent = rates.TryGetValue(a.ApiName, out var p) ? p : null })] };
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { log(ex); return g; }
                finally { slots.Release(); }
            }));
            Steam = enriched;
            lock (gate) ledger.LastSync = DateTimeOffset.Now;
            Evaluate(dirty: true);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { log(ex); }
        finally { syncing = false; }
    }

    public void Evaluate(bool announce = true, bool dirty = false)
    {
        TrophyNews? news = null;
        lock (gate)
        {
            var xpBefore = ExtraXp;
            var stats = new PlayerStats(Progression.Of(diary.Entries), diary.Entries, Steam, playtime, SafeInstalled(), ledger.Activity, DateTimeOffset.Now);
            var states = Trophies.Evaluate(stats, ledger.Earned);
            var fresh = new List<TrophyState>();
            foreach (var s in states.Where(s => s.Unlocked && s.UnlockedAt is null))
            {
                var now = DateTimeOffset.Now;
                ledger.Earned[s.Def.Id] = now;
                fresh.Add(s with { UnlockedAt = now });
            }
            States = [.. states.Select(s => s.Unlocked && s.UnlockedAt is null ? s with { UnlockedAt = ledger.Earned[s.Def.Id] } : s)];
            var steamXp = Math.Max(ledger.SteamXp, Rarity.Xp(Steam));
            if (steamXp != ledger.SteamXp) { ledger.SteamXp = steamXp; dirty = true; }

            // New Steam unlocks since the last sync (the very first sync is the import of the player's history).
            int newSteam = 0; string? newGame = null;
            var first = ledger.SteamCounts.Count == 0 && Steam.Any(g => g.Unlocked > 0);
            foreach (var g in Steam)
            {
                var before = ledger.SteamCounts.GetValueOrDefault(g.AppId);
                if (g.Unlocked > before) { newSteam += g.Unlocked - before; newGame = newGame is null ? g.Name : newGame == g.Name ? newGame : ""; }
                if (g.Unlocked > before || !ledger.SteamCounts.ContainsKey(g.AppId)) { ledger.SteamCounts[g.AppId] = Math.Max(before, g.Unlocked); dirty = true; }
            }
            var visible = fresh.Where(s => s.Def.Category != TrophyCategory.Diary).ToList(); // the diary celebration shows its own
            if (fresh.Count > 0 || dirty) Save();
            if (announce && (visible.Count > 0 || newSteam > 0))
                news = new(visible, newSteam, newGame is { Length: > 0 } ? newGame : null, first, Steam.Sum(g => g.Unlocked), Steam.Count(g => g.Unlocked > 0), ExtraXp - xpBefore);
        }
        Changed?.Invoke(this, EventArgs.Empty);
        diary.NotifyProgress();
        if (news is not null) News?.Invoke(this, news);
    }

    private int SafeInstalled() { try { return installed(); } catch (Exception ex) { log(ex); return 0; } }
    private void Save() { try { store.Save(ledger); } catch (Exception ex) { log(ex); } }
}
