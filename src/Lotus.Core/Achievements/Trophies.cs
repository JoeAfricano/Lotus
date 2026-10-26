using Lotus.Core.Diary;

namespace Lotus.Core.Achievements;

public enum TrophyTier { Bronze, Silver, Gold, Platinum }
public enum TrophyCategory { Diary, Hunter, Library, Lotus, Secret }

// Everything the Lotus achievements look at, with the derived numbers computed once.
public sealed class PlayerStats
{
    public PlayerStats(Progress diary, IReadOnlyList<DiaryEntry> entries, IReadOnlyList<SteamGameAchievements> steam,
        IReadOnlyDictionary<string, int> playtime, int installed, IReadOnlyDictionary<string, int> activity, DateTimeOffset now)
    {
        Diary = diary; Entries = entries; Steam = steam; Playtime = playtime; Installed = installed; Activity = activity; Now = now;
        var unlocked = steam.SelectMany(g => g.Items).Where(a => a.Unlocked).ToList();
        SteamUnlocked = unlocked.Count;
        Unlocks = [.. unlocked.Where(a => a.UnlockedAt is not null).Select(a => a.UnlockedAt!.Value.ToLocalTime()).OrderBy(t => t)];
        Perfect = steam.Count(g => g.Perfect);
        GamesWithUnlocks = steam.Count(g => g.Unlocked > 0);
        HalfGames = steam.Count(g => g.Total >= 4 && g.Fraction >= 0.5);
        rarities = [.. unlocked.Where(a => a.Percent is not null).Select(a => a.Percent!.Value)];
        var days = Unlocks.GroupBy(t => t.Date).Select(g => (Day: g.Key, Count: g.Count())).OrderBy(d => d.Day).ToList();
        BestDay = days.Count == 0 ? 0 : days.Max(d => d.Count);
        var run = 0; DateTime? prev = null;
        foreach (var (day, _) in days) { run = prev is { } p && (day - p).TotalDays == 1 ? run + 1 : 1; LongestStreak = Math.Max(LongestStreak, run); prev = day; }
        ThisYear = Unlocks.Count(t => t.Year == now.ToLocalTime().Year);
        NightOwl = Unlocks.Any(t => t.Hour is >= 2 and < 5);
        Christmas = Unlocks.Any(t => t is { Month: 12, Day: 25 });
        NewYear = Unlocks.Any(t => t is { Month: 1, Day: 1 });
        Comeback = steam.Any(g => g.Items.Where(a => a.UnlockedAt is not null).Select(a => a.UnlockedAt!.Value).OrderBy(t => t)
            .Zip(g.Items.Where(a => a.UnlockedAt is not null).Select(a => a.UnlockedAt!.Value).OrderBy(t => t).Skip(1), (a, b) => b - a).Any(gap => gap.TotalDays >= 365));
        FastPerfect = steam.Any(g => g.Perfect && g.Total >= 10 && g.FirstUnlock is { } f && g.LastUnlock is { } l && (l - f).TotalDays <= 7);
        TotalHours = playtime.Values.Sum() / 60;
        MaxHours = playtime.Count == 0 ? 0 : playtime.Values.Max() / 60;
        GamesOverHour = playtime.Values.Count(m => m >= 60);
    }
    private readonly List<double> rarities;
    public Progress Diary { get; }
    public IReadOnlyList<DiaryEntry> Entries { get; }
    public IReadOnlyList<SteamGameAchievements> Steam { get; }
    public IReadOnlyDictionary<string, int> Playtime { get; }
    public IReadOnlyDictionary<string, int> Activity { get; }
    public int Installed { get; }
    public DateTimeOffset Now { get; }
    public IReadOnlyList<DateTimeOffset> Unlocks { get; }
    public int SteamUnlocked { get; }
    public int Perfect { get; }
    public int GamesWithUnlocks { get; }
    public int HalfGames { get; }
    public int BestDay { get; }
    public int LongestStreak { get; }
    public int ThisYear { get; }
    public bool NightOwl { get; }
    public bool Christmas { get; }
    public bool NewYear { get; }
    public bool Comeback { get; }
    public bool FastPerfect { get; }
    public int TotalHours { get; }
    public int MaxHours { get; }
    public int GamesOverHour { get; }
    public int RareBelow(double percent) => rarities.Count(p => p < percent);
    public int Act(string id) => Activity.GetValueOrDefault(id);
    public (int, int) Badge(string id) => Diary.Badges.FirstOrDefault(b => b.Id == id) is { } b ? (b.Current, b.Goal) : (0, 1);
}

public sealed record TrophyDef(string Id, TrophyCategory Category, TrophyTier Tier, string Glyph, Func<PlayerStats, (int Current, int Goal)> Measure)
{
    public bool Secret => Category == TrophyCategory.Secret;
    public int Xp => Tier switch { TrophyTier.Bronze => 25, TrophyTier.Silver => 50, TrophyTier.Gold => 100, _ => 250 };
    // Diary trophies reuse the diary medal names; the rest have their own.
    public string NameKey => Category == TrophyCategory.Diary ? "DyBadge" + Id : "Tr" + Id;
    public string HintKey => NameKey + "Hint";
}

public sealed record TrophyState(TrophyDef Def, int Current, int Goal, bool Unlocked, DateTimeOffset? UnlockedAt)
{
    public double Fraction => Unlocked ? 1 : Goal <= 0 ? 0 : Math.Clamp((double)Current / Goal, 0, 1);
}

// The Lotus achievements. Diary ones mirror the diary medals; Hunter and Library read what Steam keeps on this PC;
// Lotus ones count what the player does here; secret ones stay hidden until earned.
public static class Trophies
{
    public const string PlatinumId = "lotus_platinum";
    private static (int, int) Count(int value, int goal) => (Math.Min(value, goal), goal);
    private static (int, int) Flag(bool value) => (value ? 1 : 0, 1);

    public static readonly IReadOnlyList<TrophyDef> All = Build();

    private static List<TrophyDef> Build()
    {
        var diaryTiers = new Dictionary<string, TrophyTier>
        {
            ["first"] = TrophyTier.Bronze, ["finisher5"] = TrophyTier.Silver, ["finisher25"] = TrophyTier.Gold, ["critic"] = TrophyTier.Silver,
            ["essayist"] = TrophyTier.Silver, ["masterpiece"] = TrophyTier.Bronze, ["platinum"] = TrophyTier.Silver, ["perfectionist"] = TrophyTier.Gold,
            ["hardcore"] = TrophyTier.Silver, ["speedrunner"] = TrophyTier.Silver, ["marathon"] = TrophyTier.Silver, ["explorer"] = TrophyTier.Gold,
            ["collector"] = TrophyTier.Bronze, ["honest"] = TrophyTier.Bronze,
        };
        var list = new List<TrophyDef>();
        foreach (var b in Progression.Of([]).Badges)
        {
            var id = b.Id;
            list.Add(new(id, TrophyCategory.Diary, diaryTiers.GetValueOrDefault(id), b.Glyph, s => s.Badge(id)));
        }
        void Add(string id, TrophyCategory c, TrophyTier t, string glyph, Func<PlayerStats, (int, int)> m) => list.Add(new(id, c, t, glyph, m));
        const TrophyCategory H = TrophyCategory.Hunter, L = TrophyCategory.Library, X = TrophyCategory.Lotus, S = TrophyCategory.Secret;
        const TrophyTier B = TrophyTier.Bronze, Ag = TrophyTier.Silver, Au = TrophyTier.Gold, Pt = TrophyTier.Platinum;
        // Trophy hunter (Steam)
        Add("hunt_first", H, B, "", s => Count(s.SteamUnlocked, 1));
        Add("hunt_100", H, B, "", s => Count(s.SteamUnlocked, 100));
        Add("hunt_500", H, Ag, "", s => Count(s.SteamUnlocked, 500));
        Add("hunt_1000", H, Au, "", s => Count(s.SteamUnlocked, 1000));
        Add("hunt_2500", H, Pt, "", s => Count(s.SteamUnlocked, 2500));
        Add("perfect_1", H, Ag, "", s => Count(s.Perfect, 1));
        Add("perfect_5", H, Au, "", s => Count(s.Perfect, 5));
        Add("perfect_15", H, Pt, "", s => Count(s.Perfect, 15));
        Add("rare_1", H, B, "", s => Count(s.RareBelow(10), 1));
        Add("rare_10", H, Au, "", s => Count(s.RareBelow(5), 10));
        Add("rare_ultra", H, Au, "", s => Count(s.RareBelow(1), 1));
        Add("day_10", H, Ag, "", s => Count(s.BestDay, 10));
        Add("streak_7", H, Au, "", s => Count(s.LongestStreak, 7));
        Add("tourist_25", H, Ag, "", s => Count(s.GamesWithUnlocks, 25));
        Add("half_10", H, Ag, "", s => Count(s.HalfGames, 10));
        Add("year_100", H, Ag, "", s => Count(s.ThisYear, 100));
        // Library (Steam hours, installed games)
        Add("hours_100", L, B, "", s => Count(s.TotalHours, 100));
        Add("hours_1000", L, Ag, "", s => Count(s.TotalHours, 1000));
        Add("hours_5000", L, Au, "", s => Count(s.TotalHours, 5000));
        Add("loyal_100", L, Ag, "", s => Count(s.MaxHours, 100));
        Add("loyal_500", L, Au, "", s => Count(s.MaxHours, 500));
        Add("library_25", L, Ag, "", s => Count(s.GamesOverHour, 25));
        Add("library_100", L, Au, "", s => Count(s.GamesOverHour, 100));
        Add("installed_10", L, B, "", s => Count(s.Installed, 10));
        // Lotus
        Add("lotus_score", X, B, "", s => Count(s.Act(Activity.Score), 1));
        Add("lotus_bench", X, B, "", s => Count(s.Act(Activity.Benchmark), 1));
        Add("lotus_bench10", X, Ag, "", s => Count(s.Act(Activity.Benchmark), 10));
        Add("lotus_optimize", X, B, "", s => Count(s.Act(Activity.Optimize), 1));
        Add("lotus_launch", X, B, "", s => Count(s.Act(Activity.Launch), 1));
        Add("lotus_launch50", X, Au, "", s => Count(s.Act(Activity.Launch), 50));
        Add("lotus_pick", X, B, "", s => Count(s.Act(Activity.Pick), 1));
        Add("lotus_curious", X, Ag, "", s => Count(s.Act(Activity.Details), 25));
        Add(PlatinumId, X, Pt, "", _ => (0, 1)); // measured from the others in Evaluate
        // Secret
        Add("night_owl", S, Ag, "", s => Flag(s.NightOwl));
        Add("comeback", S, Au, "", s => Flag(s.Comeback));
        Add("xmas", S, Ag, "", s => Flag(s.Christmas));
        Add("newyear", S, Ag, "", s => Flag(s.NewYear));
        Add("fast_perfect", S, Au, "", s => Flag(s.FastPerfect));
        Add("midnight", S, B, "", s => Count(s.Act(Activity.Midnight), 1));
        Add("harsh", S, B, "", s => Flag(s.Entries.Any(e => e.Rating is <= 2)));
        return list;
    }

    // Current state of every trophy. Anything already in `earned` stays unlocked (achievements are for keeps).
    public static IReadOnlyList<TrophyState> Evaluate(PlayerStats stats, IReadOnlyDictionary<string, DateTimeOffset> earned)
    {
        var states = new List<TrophyState>();
        foreach (var d in All.Where(d => d.Id != PlatinumId))
        {
            var (cur, goal) = d.Measure(stats);
            var at = earned.TryGetValue(d.Id, out var t) ? t : (DateTimeOffset?)null;
            states.Add(new(d, cur, goal, at is not null || cur >= goal, at));
        }
        var others = states.Where(s => !s.Def.Secret).ToList();
        var plat = All.First(d => d.Id == PlatinumId);
        var done = others.Count(s => s.Unlocked);
        var platAt = earned.TryGetValue(PlatinumId, out var pt) ? pt : (DateTimeOffset?)null;
        states.Add(new(plat, done, others.Count, platAt is not null || done >= others.Count, platAt));
        return states;
    }
}

// What the player does inside Lotus (counted for the Lotus trophies).
public static class Activity
{
    public const string Score = "score", Benchmark = "benchmark", Optimize = "optimize", Launch = "launch", Pick = "pick", Details = "details", Midnight = "midnight";
    // Any part of Lotus reports here; the trophy center listens.
    public static event Action<string>? Recorded;
    public static void Record(string id) => Recorded?.Invoke(id);
}
