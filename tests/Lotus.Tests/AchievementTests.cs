using System.Text;
using Lotus.Core.Achievements;
using Lotus.Core.Diary;
using Lotus.Infrastructure.Games;
using Xunit;

namespace Lotus.Tests;

public sealed class AchievementTests
{
    private sealed class DiaryMemory : IGameDiaryStore { public DiaryData Saved = new(); public DiaryData Load() => Saved; public void Save(DiaryData d) => Saved = d; }
    private sealed class LedgerMemory : ITrophyLedgerStore { public TrophyLedger Saved = new(); public int Saves; public TrophyLedger Load() => Saved; public void Save(TrophyLedger l) { Saved = l; Saves++; } }
    private sealed class FakeSteam : ISteamAchievementSource { public List<SteamGameAchievements> Games = []; public IReadOnlyList<SteamGameAchievements> Read() => Games; }
    private sealed class FakeRarity(Dictionary<string, double> rates) : IAchievementRarity
    {
        public Task<IReadOnlyDictionary<string, double>?> GetAsync(string appId, CancellationToken token) => Task.FromResult<IReadOnlyDictionary<string, double>?>(rates);
    }
    private static readonly DateTimeOffset Day = new(2025, 12, 25, 12, 0, 0, TimeSpan.Zero);
    private static SteamAchievement A(string id, bool unlocked, DateTimeOffset? at = null) => new(id, "Name " + id, "Desc", false, "i.jpg", "g.jpg", unlocked, unlocked ? at ?? Day : null);
    private static SteamGameAchievements Game(string appId, int unlocked, int total, DateTimeOffset? at = null) =>
        new(appId, "Game " + appId, [.. Enumerable.Range(0, total).Select(i => A(appId + "_" + i, i < unlocked, at?.AddMinutes(i)))]);

    private static (TrophyCenter Center, GameDiary Diary, FakeSteam Steam, LedgerMemory Ledger, List<TrophyNews> News) Setup(Dictionary<string, double>? rates = null)
    {
        var diary = new GameDiary(new DiaryMemory(), ex => throw ex);
        var steam = new FakeSteam(); var ledger = new LedgerMemory(); var news = new List<TrophyNews>();
        var center = new TrophyCenter(diary, steam, new FakeRarity(rates ?? []), ledger, () => new Dictionary<string, int> { ["10"] = 120 * 60 }, () => 3, ex => throw ex,
            (id, _) => Task.FromResult<string?>("Store " + id), listen: false);
        center.News += (_, n) => news.Add(n);
        return (center, diary, steam, ledger, news);
    }

    [Fact] public void CatalogIsCompleteAndLocalized()
    {
        Assert.True(Trophies.All.Count >= 50);
        Assert.Equal(Trophies.All.Count, Trophies.All.Select(t => t.Id).Distinct().Count());
        var pt = new Lotus.Core.TextCatalog("pt-BR"); var en = new Lotus.Core.TextCatalog("en-US");
        foreach (var t in Trophies.All)
            foreach (var key in new[] { t.NameKey, t.HintKey })
            {
                Assert.False(string.IsNullOrWhiteSpace(pt[key]) || pt[key] == key, "pt missing " + key);
                Assert.False(string.IsNullOrWhiteSpace(en[key]) || en[key] == key, "en missing " + key);
            }
        Assert.Equal(25, Trophies.All.First(t => t.Tier == TrophyTier.Bronze).Xp);
    }

    [Fact] public async Task SteamHistoryUnlocksTrophiesOnceAndAddsXp()
    {
        var (center, diary, steam, ledger, news) = Setup(new() { ["7_0"] = 0.5, ["7_1"] = 30 });
        center.Evaluate();
        Assert.DoesNotContain(news.SelectMany(n => n.Trophies), t => t.Def.Id == "hours_100"); // hours come with the sync
        news.Clear();
        steam.Games = [Game("7", 2, 2, Day), Game("8", 0, 5), new("9", "9", [A("9_0", false)])];
        await center.SyncAsync();
        var n = Assert.Single(news);
        Assert.True(n.FirstImport); Assert.Equal(2, n.NewSteam);
        var ids = n.Trophies.Select(t => t.Def.Id).ToList();
        Assert.Contains("hunt_first", ids); Assert.Contains("perfect_1", ids); Assert.Contains("rare_ultra", ids);
        Assert.Contains("xmas", ids); Assert.Contains("hours_100", ids);                   // 120 h in one game
        Assert.Equal(15 + 2 + Rarity.PerfectBonus, center.SteamXp);                         // legendary + uncommon + 100%
        Assert.Equal("Store 9", center.Game("9")!.Name); Assert.Equal("Store 8", center.Game("8")!.Name); // store names win over cache codenames
        Assert.True(diary.Progress.Xp >= center.ExtraXp);

        news.Clear();
        await center.SyncAsync();                                                            // nothing new
        Assert.Empty(news);

        steam.Games = [Game("7", 2, 2, Day), Game("8", 1, 5, DateTimeOffset.Now)];
        await center.SyncAsync();
        var later = Assert.Single(news);
        Assert.False(later.FirstImport); Assert.Equal(1, later.NewSteam); Assert.Equal("Store 8", later.SteamGame);

        steam.Games = [];                                                                    // Steam data gone: earned stays earned
        await center.SyncAsync();
        Assert.True(center.States.First(s => s.Def.Id == "perfect_1").Unlocked);
        Assert.Equal(15 + 2 + Rarity.PerfectBonus + 2, center.SteamXp);                     // + the later unlock (unknown rarity)
    }

    [Fact] public void ActivityCountsAndDiaryTrophiesStayQuiet()
    {
        var (center, diary, _, ledger, news) = Setup();
        center.Evaluate(); news.Clear();
        center.Record(Activity.Launch);
        Assert.Contains(news.SelectMany(n => n.Trophies), t => t.Def.Id == "lotus_launch");
        Assert.Equal(1, ledger.Saved.Activity[Activity.Launch]);
        news.Clear();
        var game = new DiaryEntry { Key = "steam:1", Name = "Hades", Status = DiaryStatus.Completed, Rating = 1 };
        var change = diary.Save(game);
        Assert.Contains(change.NewBadges, b => b.Id == "first");                               // the diary celebration shows it
        var shown = news.SelectMany(n => n.Trophies).Select(t => t.Def.Id).ToList();
        Assert.DoesNotContain("first", shown); Assert.Contains("harsh", shown);              // secret: harsh critic
        Assert.True(center.States.First(s => s.Def.Id == "first").Unlocked);
        Assert.True(change.XpAfter - change.XpBefore >= 100 + 25);                           // finish + the trophy
    }

    [Fact] public void StatsFindStreaksBestDayAndComebacks()
    {
        var start = new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var items = Enumerable.Range(0, 7).Select(i => A("s" + i, true, start.AddDays(i))).Append(A("late", true, start.AddDays(400))).ToList();
        var stats = new PlayerStats(Progression.Of([]), [], [new("1", "G", items)], new Dictionary<string, int>(), 0, new Dictionary<string, int>(), start.AddDays(401));
        Assert.Equal(7, stats.LongestStreak); Assert.Equal(1, stats.BestDay); Assert.True(stats.Comeback);
        var states = Trophies.Evaluate(stats, new Dictionary<string, DateTimeOffset>());
        Assert.True(states.First(s => s.Def.Id == "streak_7").Unlocked);
        var plat = states.First(s => s.Def.Id == Trophies.PlatinumId);
        Assert.False(plat.Unlocked); Assert.Equal(states.Count(s => !s.Def.Secret && s.Def.Id != Trophies.PlatinumId), plat.Goal);
    }

    [Fact] public void ReadsSteamBinaryCache()
    {
        // Schema: game name, one stat with two achievement bits (one localized, one plain) ; user: bit 1 unlocked.
        var schema = Kv(w =>
        {
            w.Begin("440"); w.Str("gamename", "Test Game"); w.Begin("stats"); w.Begin("1"); w.Begin("bits");
            w.Begin("0"); w.Str("name", "ACH_A"); w.Begin("display"); w.Begin("name"); w.Str("english", "First"); w.Str("brazilian", "Primeira"); w.End();
            w.Str("desc", "Do it"); w.Int("hidden", 0); w.Str("icon", "a.jpg"); w.Str("icon_gray", "ag.jpg"); w.End(); w.End();
            w.Begin("1"); w.Str("name", "ACH_B"); w.Begin("display"); w.Str("name", "Second"); w.Str("hidden", "1"); w.End(); w.End();
            w.End(); w.End(); w.End(); w.End();
        });
        var user = Kv(w => { w.Begin("cache"); w.Int("crc", 1); w.Begin("1"); w.Int("data", 2); w.Begin("AchievementTimes"); w.Int("1", 1_700_000_000); w.End(); w.End(); w.End(); });
        var reader = new SteamAchievementReader("pt-BR");
        var s = reader.ParseSchema("440", BinaryKv.Parse(schema))!;
        Assert.Equal("Test Game", s.Name);
        var g = SteamAchievementReader.Build("440", s, BinaryKv.Parse(user));
        Assert.Equal(["Primeira", "Second"], g.Items.Select(i => i.Name));
        Assert.False(g.Items[0].Unlocked); Assert.True(g.Items[1].Unlocked && g.Items[1].Hidden);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), g.Items[1].UnlockedAt);
        Assert.Equal(0.5, g.Fraction);
    }

    [Fact] public void ParsesGlobalRarity()
    {
        var r = SteamRarityClient.Parse("""{"achievementpercentages":{"achievements":[{"name":"A","percent":"12.3456"},{"name":"B","percent":0.4},{"percent":3}]}}""");
        Assert.Equal(12.35, r["A"]); Assert.Equal(0.4, r["B"]); Assert.Equal(2, r.Count);
        Assert.Equal("legendary", Rarity.Kind(0.4)); Assert.Equal("common", Rarity.Kind(80)); Assert.Equal("unknown", Rarity.Kind(null));
    }

    [Fact] public void LedgerSurvivesRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lotus-ledger-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var store = new JsonTrophyLedgerStore(Path.Combine(dir, "a.json"));
            var l = new TrophyLedger { SteamXp = 42 }; l.Earned["x"] = Day; l.Activity["launch"] = 3; l.SteamCounts["7"] = 2;
            store.Save(l); store.Save(l);
            var back = store.Load();
            Assert.Equal(42, back.SteamXp); Assert.Equal(Day, back.Earned["x"]); Assert.Equal(3, back.Activity["launch"]);
            File.WriteAllText(Path.Combine(dir, "a.json"), "{broken");
            Assert.Equal(42, store.Load().SteamXp);                                             // falls back to .bak
        }
        finally { Directory.Delete(dir, true); }
    }

    // Tiny binary KeyValues writer for the tests.
    private sealed class KvWriter
    {
        public readonly MemoryStream S = new();
        private void Name(byte type, string name) { S.WriteByte(type); var b = Encoding.UTF8.GetBytes(name); S.Write(b); S.WriteByte(0); }
        public void Begin(string name) => Name(0, name);
        public void End() => S.WriteByte(8);
        public void Str(string name, string value) { Name(1, name); var b = Encoding.UTF8.GetBytes(value); S.Write(b); S.WriteByte(0); }
        public void Int(string name, int value) { Name(2, name); S.Write(BitConverter.GetBytes(value)); }
    }
    private static byte[] Kv(Action<KvWriter> build) { var w = new KvWriter(); build(w); w.End(); return w.S.ToArray(); }
}
