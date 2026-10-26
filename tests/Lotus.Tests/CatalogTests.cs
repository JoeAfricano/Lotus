using System.Text.Json.Nodes;
using Lotus.Core;
using Lotus.Core.Catalog;
using Lotus.Core.Games;
using Lotus.Infrastructure.Catalog;
using Xunit;

namespace Lotus.Tests;

public sealed class CatalogTests
{
    [Fact] public void DifficultyComesFromStrongCommunityTags()
    {
        Assert.Equal(Difficulty.VeryHard, GameDifficulty.Estimate(new Dictionary<string, long> { ["Souls-like"] = 7000, ["Difficult"] = 4600, ["RPG"] = 4700 }));
        Assert.Equal(Difficulty.Hard, GameDifficulty.Estimate(new Dictionary<string, long> { ["Action"] = 1000, ["Roguelike"] = 600 }));
        Assert.Equal(Difficulty.Easy, GameDifficulty.Estimate(new Dictionary<string, long> { ["Farming Sim"] = 900, ["Relaxing"] = 700 }));
        Assert.Equal(Difficulty.Medium, GameDifficulty.Estimate(new Dictionary<string, long> { ["Action"] = 1000, ["Difficult"] = 50 })); // a few votes do not count
        Assert.Equal(Difficulty.Unknown, GameDifficulty.Estimate(new Dictionary<string, long>()));
    }

    [Fact] public void ParsesSteamDetailsWithMetacriticReviewsAndPlainText()
    {
        var store = JsonNode.Parse("""{"1593500":{"success":true,"data":{"name":"God of War","short_description":"Kratos &amp; Atreus<br>no norte","genres":[{"description":"Ação"}],"release_date":{"coming_soon":false,"date":"14/jan./2022"},"developers":["Santa Monica Studio"],"metacritic":{"score":93,"url":"https://mc"},"header_image":"h.jpg","background_raw":"bg.jpg","screenshots":[{"path_full":"s1.jpg"}],"is_free":false}}}""");
        var spy = JsonNode.Parse("""{"name":"God of War","positive":151706,"negative":6311,"tags":{"Action":1008,"Story Rich":899}}""");
        var i = SteamCatalog.ParseSteam("1593500", store, spy)!;
        Assert.Equal("God of War", i.Name); Assert.Equal("Kratos & Atreus\nno norte", i.Summary);
        Assert.Equal(93, i.Metacritic); Assert.Equal(96, i.PositivePercent); Assert.Equal(["Ação"], i.Genres);
        Assert.Equal(Difficulty.Medium, i.Difficulty); Assert.Equal("bg.jpg", i.Background); Assert.Equal(["s1.jpg"], i.Screenshots);
        Assert.Null(SteamCatalog.ParseSteam("1", JsonNode.Parse("""{"1":{"success":false}}"""), JsonNode.Parse("{}")));
    }

    [Fact] public void ChartsDropSoftwareAndBundles()
    {
        var spy = JsonNode.Parse("""{"730":{"appid":730,"name":"Counter-Strike 2","positive":9,"negative":1,"ccu":900},"431960":{"appid":431960,"name":"Wallpaper Engine","ccu":5000},"570":{"appid":570,"name":"Dota 2","positive":8,"negative":2,"ccu":800}}""");
        Assert.Equal(["730", "570"], SteamCatalog.SpyList(spy, byRating: false).Select(i => i.AppId));
        var search = JsonNode.Parse("""{"items":[{"name":"A","logo":"https://x/steam/apps/1/abc/capsule_sm_120.jpg"},{"name":"WE","logo":"https://x/steam/apps/431960/capsule_sm_120.jpg"},{"name":"A","logo":"https://x/steam/apps/1/capsule.jpg"},{"name":"No id","logo":"https://x/other.jpg"}]}""");
        var r = SteamCatalog.Results(search);
        Assert.Equal(["1"], r.Select(i => i.AppId)); Assert.EndsWith("capsule_sm_120.jpg", r[0].Note);
    }

    private sealed class FakeCatalog : IGameCatalog
    {
        public Dictionary<string, GameInfo> Info = [];
        public int Lookups;
        public Task<GameInfo?> DetailsAsync(string store, string id, string name, CancellationToken t) => Task.FromResult<GameInfo?>(null);
        public Task<GameInfo?> SteamDetailsAsync(string appId, CancellationToken t) { Lookups++; return Task.FromResult(Info.GetValueOrDefault(appId)); }
        public Task<IReadOnlyList<CatalogItem>> SearchAsync(string q, CancellationToken t) => Task.FromResult<IReadOnlyList<CatalogItem>>([new("99", "Found " + q, "c", "w", Metacritic: 88, Free: true), new("98", "Paid", "c", "w", Metacritic: 60, Free: false)]);
        public List<CatalogItem> Discovered = []; public (string? Genre, string? Diff, bool Free) Asked;
        public Task<IReadOnlyList<CatalogItem>> DiscoverAsync(string? g, string? d, bool free, CancellationToken t) { Asked = (g, d, free); return Task.FromResult<IReadOnlyList<CatalogItem>>(Discovered); }
        public Task<string?> ArtAsync(string id, CancellationToken t) => Task.FromResult<string?>("art-" + id);
        public Task<CatalogChart?> ChartAsync(string id, TimeSpan maxAge, CancellationToken t) =>
            Task.FromResult<CatalogChart?>(new(id, [new("10", "Easy Farm", "c", "w"), new("20", "Hard Souls", "c", "w"), new("30", "Owned Game", "c", "w")], DateTimeOffset.Now));
    }
    private static GameInfo G(string name, Difficulty d, params string[] genres) => new(name, null, "", genres, null, null, null, null, null, null, null, [], d, null, null, [], null, false, "Steam");

    [Fact] public async Task PickerHonoursFiltersAndSkipsOwnedGames()
    {
        var fake = new FakeCatalog { Info = { ["10"] = G("Easy Farm", Difficulty.Easy, "Simulação"), ["20"] = G("Hard Souls", Difficulty.VeryHard, "Ação"), ["30"] = G("Owned Game", Difficulty.Hard, "Ação") } };
        var owned = new List<GameEntry> { new(GameStores.Steam, "30", "Owned Game") };
        var vm = new GameListViewModel(fake, new TextCatalog(), ex => throw ex, () => owned) { DifficultyWanted = "hard", Genre = "Action" };
        await vm.ShowAsync();
        Assert.Equal(3, vm.Items.Count); Assert.True(vm.Items.Single(i => i.Item.AppId == "30").Installed);
        for (var seed = 0; seed < 5; seed++) { await vm.PickAsync(new Random(seed)); Assert.Equal("20", vm.Picked?.AppId); } // never the owned one
        vm.DifficultyWanted = "easy"; vm.Genre = "Action"; await vm.PickAsync(new Random(1));
        Assert.Null(vm.Picked); Assert.Equal(new TextCatalog()["GlPickNone"], vm.PickStatus);
        vm.OnlyNotOwned = false; vm.DifficultyWanted = "any"; vm.Genre = "Simulation"; await vm.PickAsync(new Random(2));
        Assert.Equal("10", vm.Picked?.AppId); Assert.Equal("Easy Farm", vm.Details.Title);
    }
}

public sealed class GameListFilterTests
{
    private sealed class Fake : IGameCatalog
    {
        public Task<GameInfo?> DetailsAsync(string s, string i, string n, CancellationToken t) => Task.FromResult<GameInfo?>(null);
        public Task<GameInfo?> SteamDetailsAsync(string a, CancellationToken t) => Task.FromResult<GameInfo?>(null);
        public Task<CatalogChart?> ChartAsync(string id, TimeSpan m, CancellationToken t) => Task.FromResult<CatalogChart?>(new(id,
            [new("1", "Zeta", "c", "w", PositivePercent: 95), new("2", "Alpha", "c", "w", PositivePercent: 60), new("3", "Mid", "c", "w", PositivePercent: 85)], DateTimeOffset.Now));
        public Task<IReadOnlyList<CatalogItem>> SearchAsync(string q, CancellationToken t) => Task.FromResult<IReadOnlyList<CatalogItem>>([new("9", "Hit " + q, "c", "w", Metacritic: 91, Free: true), new("8", "Paid", "c", "w", Metacritic: 60, Free: false)]);
        public Task<IReadOnlyList<CatalogItem>> DiscoverAsync(string? g, string? d, bool free, CancellationToken t) => Task.FromResult<IReadOnlyList<CatalogItem>>([]);
        public Task<string?> ArtAsync(string id, CancellationToken t) => Task.FromResult<string?>(null);
    }

    [Fact] public async Task SortsFiltersAndSearchesTheStore()
    {
        var owned = new List<GameEntry> { new(GameStores.Steam, "1", "Zeta") };
        var vm = new GameListViewModel(new Fake(), new TextCatalog(), ex => throw ex, () => owned);
        await vm.ShowAsync();
        Assert.Equal(["Zeta", "Alpha", "Mid"], vm.Items.Select(i => i.Name));
        vm.Sort = "name"; Assert.Equal(["Alpha", "Mid", "Zeta"], vm.Items.Select(i => i.Name));
        vm.MinRating = "80"; Assert.Equal(["Mid", "Zeta"], vm.Items.Select(i => i.Name));
        vm.HideOwned = true; Assert.Equal(["Mid"], vm.Items.Select(i => i.Name));
        vm.MinRating = "0"; vm.HideOwned = false; vm.Sort = "rank";
        vm.Query = "doom"; await Task.Delay(600);
        Assert.Equal(["Hit doom", "Paid"], vm.Items.Select(i => i.Name));
        vm.FreeOnly = true; Assert.Equal(["Hit doom"], vm.Items.Select(i => i.Name));
        vm.FreeOnly = false; vm.Query = ""; await Task.Delay(100);
        Assert.Equal(3, vm.Items.Count); // back to the chart
    }
}

public sealed class DiscoveryPickTests
{
    [Fact] public async Task DrawAsksTheStoreWithTagsAndSkipsOwned()
    {
        var store = new StoreFake();
        store.Discovered = [new("50", "Owned Strategy", "c", "w"), new("60", "Hard Strategy", "c", "w")];
        store.Info["60"] = new("Hard Strategy", "60", "", ["Estratégia"], null, null, null, null, null, null, null, [], Difficulty.VeryHard, null, null, [], null, false, "Steam");
        store.Info["50"] = store.Info["60"] with { Name = "Owned Strategy" };
        var owned = new List<GameEntry> { new(GameStores.Steam, "50", "Owned Strategy") };
        var vm = new GameListViewModel(store, new TextCatalog(), ex => throw ex, () => owned) { Genre = "Strategy", DifficultyWanted = "hard" };
        await vm.PickAsync(new Random(3));
        Assert.Equal(("Strategy", "hard", false), store.Asked); // genre list + difficulty pre-filter
        Assert.Equal("60", vm.Picked?.AppId);
    }

    private sealed class StoreFake : IGameCatalog
    {
        public Dictionary<string, GameInfo> Info = []; public List<CatalogItem> Discovered = []; public (string?, string?, bool) Asked;
        public Task<GameInfo?> DetailsAsync(string s, string i, string n, CancellationToken t) => Task.FromResult<GameInfo?>(null);
        public Task<GameInfo?> SteamDetailsAsync(string a, CancellationToken t) => Task.FromResult(Info.GetValueOrDefault(a));
        public Task<CatalogChart?> ChartAsync(string id, TimeSpan m, CancellationToken t) => Task.FromResult<CatalogChart?>(null);
        public Task<IReadOnlyList<CatalogItem>> SearchAsync(string q, CancellationToken t) => Task.FromResult<IReadOnlyList<CatalogItem>>([]);
        public Task<IReadOnlyList<CatalogItem>> DiscoverAsync(string? g, string? d, bool free, CancellationToken t) { Asked = (g, d, free); return Task.FromResult<IReadOnlyList<CatalogItem>>(Discovered); }
        public Task<string?> ArtAsync(string id, CancellationToken t) => Task.FromResult<string?>(null);
    }
}

public sealed class StoreHtmlTests
{
    [Fact] public void ReadsAppIdsAndTitlesFromTheSearchPage()
    {
        const string html = """<a href="x" data-ds-appid="367520" data-ds-itemkey="App_367520"><div><span class="title">Hollow Knight</span></div></a><a data-ds-appid="431960"><span class="title">Wallpaper Engine</span></a><a data-ds-appid="1"><span class="title">Tom &amp; Jerry</span></a>""";
        var r = SteamCatalog.Html(html);
        Assert.Equal(["367520", "1"], r.Select(i => i.AppId)); Assert.Equal("Tom & Jerry", r[1].Name);
    }
}
