using System.Text.Json.Nodes;
using Lotus.Core;
using Lotus.Core.Diary;
using Lotus.Infrastructure.Catalog;
using Xunit;

namespace Lotus.Tests;

public sealed class GameLengthTests
{
    private sealed class Memory : IGameDiaryStore { public DiaryData Saved = new(); public DiaryData Load() => Saved; public void Save(DiaryData d) => Saved = d; }
    private sealed class Source : IGameLengthSource
    {
        public Dictionary<string, GameLength> Known = [];
        public List<string> Asked = [];
        public bool Offline;
        public Task<GameLength?> FindAsync(string name, string? appId, CancellationToken t)
        {
            Asked.Add(name);
            if (Offline) throw new HttpRequestException("offline");
            return Task.FromResult(Known.GetValueOrDefault(name));
        }
    }

    [Fact] public void PicksBySteamIdOrOneExactTitle()
    {
        var rows = JsonNode.Parse("""
            [ { "game_id": 1, "game_name": "Hades II", "comp_main": 72000, "profile_steam": 1145350 },
              { "game_id": 2, "game_name": "Hades", "game_alias": "Hades: Battle Out of Hell", "comp_main": 79200, "comp_plus": 162000, "comp_100": 345600, "profile_steam": 1145360 } ]
            """)!.AsArray();
        var bySteam = HowLongToBeatClient.Pick(rows, "whatever", "1145360")!;
        Assert.Equal((1320, 2700, 5760), (bySteam.MainMinutes!.Value, bySteam.PlusMinutes!.Value, bySteam.AllMinutes!.Value));
        Assert.Equal("https://howlongtobeat.com/game/2", bySteam.Url);
        Assert.Equal(1200, HowLongToBeatClient.Pick(rows, "HADES II™", null)!.MainMinutes);
        Assert.NotNull(HowLongToBeatClient.Pick(rows, "Hades: Battle Out of Hell", null));    // alias
        Assert.Null(HowLongToBeatClient.Pick(rows, "Hades III", null));                         // close is not enough
        Assert.Null(HowLongToBeatClient.Pick(JsonNode.Parse("""[{"game_id":3,"game_name":"X","comp_main":0}]""")!.AsArray(), "X", null)); // no times
        Assert.Equal("Baldur's Gate 3", HowLongToBeatClient.Clean("  Baldur's  Gate 3™ "));
    }

    [Fact] public async Task LooksUpAFewListedGamesAndFeedsTheSuggestion()
    {
        var diary = new GameDiary(new Memory(), ex => throw ex);
        diary.Save(new DiaryEntry { Key = "a", Name = "Inside", Status = DiaryStatus.Want });
        diary.Save(new DiaryEntry { Key = "b", Name = "Hades", Status = DiaryStatus.Playing, Minutes = 20 * 60 });
        diary.Save(new DiaryEntry { Key = "c", Name = "Done", Status = DiaryStatus.Completed });
        diary.Save(new DiaryEntry { Key = "d", Name = "Unknown", Status = DiaryStatus.Want });
        var order = diary.Entries.Select(e => (e.Key, e.Updated)).ToList();
        var source = new Source { Known = { ["Inside"] = new(240, 300, 330), ["Hades"] = new(22 * 60, 45 * 60, 96 * 60) } };
        var lengths = new DiaryLengths(diary, source, ex => throw ex) { Pause = TimeSpan.Zero };

        Assert.Equal(2, await lengths.RefreshAsync());
        Assert.DoesNotContain("Done", source.Asked);                                        // finished games are not looked up
        Assert.Equal(240, diary.Get("a")!.BeatMain); Assert.Null(diary.Get("d")!.BeatMain); Assert.NotNull(diary.Get("d")!.BeatChecked);
        Assert.Equal(order, diary.Entries.Select(e => (e.Key, e.Updated)).ToList());         // shelf order kept
        Assert.Equal(0, await lengths.RefreshAsync());                                       // nothing asked again soon
        Assert.Equal(3, source.Asked.Count);

        var picks = PlayNext.Suggest(diary.Entries, PlayTime.Evening, "any", DateTime.Today, take: 5);
        Assert.Contains(picks.Single(p => p.Entry.Key == "b").Reasons, r => r.Key == "PnAlmostDone" && r.Arg == "2h");
        Assert.Contains(picks.Single(p => p.Entry.Key == "a").Reasons, r => r.Key == "PnShortGame" && r.Arg == "4h");
        Assert.Equal("Faltam cerca de 2h para zerar", Cap(new TextCatalog().Format("PnAlmostDone", "2h")));

        var editor = new DiaryEditorViewModel(diary, new TextCatalog());
        editor.Open(diary.Get("b")!);
        Assert.Equal("História 22h · Com extras 45h · 100% 96h", editor.LengthText);
    }

    [Fact] public async Task OfflineIsQuietAndAskedAgainLater()
    {
        var diary = new GameDiary(new Memory(), ex => throw ex);
        diary.Save(new DiaryEntry { Key = "a", Name = "Inside", Status = DiaryStatus.Want });
        var errors = 0;
        var lengths = new DiaryLengths(diary, new Source { Offline = true }, _ => errors++) { Pause = TimeSpan.Zero };
        Assert.Equal(0, await lengths.RefreshAsync());
        Assert.Equal(1, errors); Assert.Null(diary.Get("a")!.BeatChecked);                     // not marked as "not found"
    }

    [Fact] public async Task GamePageLookupUsesTheDiaryThenAsksOncePerGame()
    {
        var diary = new GameDiary(new Memory(), ex => throw ex);
        diary.Save(new DiaryEntry { Key = "steam:1", Name = "Saved", AppId = "1", Status = DiaryStatus.Want });
        diary.SetLength("steam:1", new(600, null, null));
        diary.Save(new DiaryEntry { Key = "steam:2", Name = "Listed", AppId = "2", Status = DiaryStatus.Completed });
        var source = new Source { Known = { ["Listed"] = new(120, 180, 240), ["Outside"] = new(60, null, null) } };
        var lookup = new GameLengthLookup(diary, source, ex => throw ex);

        Assert.Equal(600, (await lookup.FindAsync("steam:1", "Saved", "1"))!.MainMinutes); // saved: no lookup
        Assert.Empty(source.Asked);
        Assert.Equal(120, (await lookup.FindAsync("steam:2", "Listed", "2"))!.MainMinutes);
        Assert.Equal(180, diary.Get("steam:2")!.BeatPlus);                                       // a diary game keeps it
        Assert.Equal(60, (await lookup.FindAsync("name:outside", "Outside", null))!.MainMinutes);
        Assert.Null(await lookup.FindAsync("name:nope", "Nope", null));
        Assert.Null(await lookup.FindAsync("name:nope", "Nope", null));                           // a miss is remembered
        Assert.Equal(60, (await lookup.FindAsync("name:outside", "Outside", null))!.MainMinutes);
        Assert.Equal(["Listed", "Outside", "Nope"], source.Asked);
        Assert.Null(diary.Get("name:outside"));                                                   // not added to the diary
    }

    [Fact] public async Task GamePageShowsTheTimeToBeat()
    {
        var diary = new GameDiary(new Memory(), ex => throw ex);
        var source = new Source { Known = { ["Hades"] = new(22 * 60, 45 * 60, 96 * 60, "https://howlongtobeat.com/game/2") } };
        var page = new Lotus.Core.Catalog.GameDetailsViewModel(new NoCatalog(), new TextCatalog(), ex => throw ex) { Lengths = new GameLengthLookup(diary, source, ex => throw ex) };
        var changed = new List<string?>(); page.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        await page.LoadSteamAsync("1145360", "Hades", null, null);
        Assert.True(page.HasLength);
        Assert.Equal("22h", page.LengthMain);
        Assert.Contains("45h", page.LengthMore); Assert.Contains("96h", page.LengthMore);
        Assert.Equal("https://howlongtobeat.com/game/2", page.LengthUrl);
        Assert.Contains(nameof(page.HasLength), changed);
        await page.LoadSteamAsync("9", "Unknown game", null, null);
        Assert.False(page.HasLength); Assert.Equal("", page.LengthMore);
    }

    private sealed class NoCatalog : Lotus.Core.Catalog.IGameCatalog
    {
        public Task<Lotus.Core.Catalog.GameInfo?> DetailsAsync(string s, string i, string n, CancellationToken t) => Task.FromResult<Lotus.Core.Catalog.GameInfo?>(null);
        public Task<Lotus.Core.Catalog.GameInfo?> SteamDetailsAsync(string a, CancellationToken t) => Task.FromResult<Lotus.Core.Catalog.GameInfo?>(null);
        public Task<Lotus.Core.Catalog.CatalogChart?> ChartAsync(string id, TimeSpan m, CancellationToken t) => Task.FromResult<Lotus.Core.Catalog.CatalogChart?>(null);
        public Task<IReadOnlyList<Lotus.Core.Catalog.CatalogItem>> SearchAsync(string q, CancellationToken t) => Task.FromResult<IReadOnlyList<Lotus.Core.Catalog.CatalogItem>>([]);
        public Task<IReadOnlyList<Lotus.Core.Catalog.CatalogItem>> DiscoverAsync(string? g, string? d, bool free, CancellationToken t) => Task.FromResult<IReadOnlyList<Lotus.Core.Catalog.CatalogItem>>([]);
        public Task<string?> ArtAsync(string id, CancellationToken t) => Task.FromResult<string?>(null);
    }

    private static string Cap(string s) => char.ToUpperInvariant(s[0]) + s[1..];
}
