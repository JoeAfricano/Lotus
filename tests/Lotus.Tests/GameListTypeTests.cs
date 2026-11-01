using Lotus.Core;
using Lotus.Core.Catalog;
using Xunit;

namespace Lotus.Tests;

public sealed class GameListTypeTests
{
    private sealed class Fake : IGameCatalog
    {
        public int TypeRequests;
        public bool Offline;
        public Task<GameInfo?> DetailsAsync(string s, string i, string n, CancellationToken t) => Task.FromResult<GameInfo?>(null);
        public Task<GameInfo?> SteamDetailsAsync(string id, CancellationToken t) => Task.FromResult<GameInfo?>(null);
        public Task<IReadOnlyList<CatalogItem>> SearchAsync(string q, CancellationToken t) => Task.FromResult<IReadOnlyList<CatalogItem>>([]);
        public Task<IReadOnlyList<CatalogItem>> DiscoverAsync(string? g, string? d, bool f, CancellationToken t) => Task.FromResult<IReadOnlyList<CatalogItem>>([]);
        public Task<string?> ArtAsync(string id, CancellationToken t) => Task.FromResult<string?>(null);
        public Task<CatalogChart?> ChartAsync(string id, TimeSpan maxAge, CancellationToken t) => Task.FromResult<CatalogChart?>(new(id,
            [new("1", "Doom", "c", "w", Free: false), new("2", "Phasmophobia", "c", "w", Free: false), new("3", "Warframe", "c", "w", Free: true), new("4", "Stardew", "c", "w")], DateTimeOffset.Now));
        public Task<IReadOnlySet<string>> TypeIdsAsync(string type, CancellationToken t)
        {
            TypeRequests++;
            if (Offline) throw new HttpRequestException("offline");
            return Task.FromResult<IReadOnlySet<string>>(type switch
            {
                "tag:FPS" => new HashSet<string> { "1", "3" },
                "tag:Horror" => new HashSet<string> { "2", "1" },
                "tag:Co-op" => new HashSet<string> { "2", "3", "4" },
                _ => new HashSet<string>(),
            });
        }
    }

    private static string[] Names(GameListViewModel vm) => [.. vm.Items.Select(i => i.Name)];

    [Fact] public async Task SeveralKindsShowAnyOrAllOfThem()
    {
        var fake = new Fake();
        var vm = new GameListViewModel(fake, new TextCatalog(), ex => throw ex);
        await vm.ShowAsync();
        await vm.SetTypesAsync(["tag:FPS", "tag:Horror"], all: false);
        Assert.Equal(["Doom", "Phasmophobia", "Warframe"], Names(vm));           // shooter OR horror
        await vm.SetTypesAsync(["tag:FPS", "tag:Horror"], all: true);
        Assert.Equal(["Doom"], Names(vm));                                         // shooter AND horror
        Assert.Equal(2, fake.TypeRequests);                                        // lists reused, not fetched again
        Assert.True(vm.HasActiveFilters); Assert.Contains("2", vm.TypeSummary);
        await vm.SetTypesAsync(["tag:Unknown kind"], all: false);                 // not in Lotus's list: ignored
        Assert.False(vm.HasTypes); Assert.Equal(4, vm.Items.Count);
        await vm.SetTypesAsync(["tag:Co-op"], all: false);
        Assert.Equal(new TextCatalog()["GtCoop"], vm.TypeSummary);
        vm.ClearFilters();
        Assert.Equal(4, vm.Items.Count); Assert.Empty(vm.SelectedTypes);
    }

    [Fact] public async Task PriceShowsFreeOnlyOrHidesFreeGames()
    {
        var vm = new GameListViewModel(new Fake(), new TextCatalog(), ex => throw ex);
        await vm.ShowAsync();
        vm.Price = "free"; Assert.Equal(["Warframe"], Names(vm)); Assert.True(vm.FreeOnly);
        vm.Price = "paid"; Assert.Equal(["Doom", "Phasmophobia", "Stardew"], Names(vm)); Assert.False(vm.FreeOnly);
        Assert.Contains(new TextCatalog()["GlPricepaid"], vm.ActiveFilters);
        vm.Price = "any"; Assert.Equal(4, vm.Items.Count);
    }

    [Fact] public async Task OfflineKindListsNeverHideEverything()
    {
        var vm = new GameListViewModel(new Fake { Offline = true }, new TextCatalog(), _ => { });
        await vm.ShowAsync();
        await vm.SetTypesAsync(["tag:FPS"], all: false);
        Assert.Equal(4, vm.Items.Count);
        Assert.Equal(new TextCatalog()["GlOffline"], vm.Status);
    }

    [Fact] public void EveryKindHasAPortugueseAndEnglishName()
    {
        var pt = new TextCatalog(); var en = new TextCatalog(); en.SetLanguage("en-US");
        foreach (var o in GameTypes.All) { Assert.False(string.IsNullOrWhiteSpace(pt[o.LabelKey]), o.Id); Assert.False(string.IsNullOrWhiteSpace(en[o.LabelKey]), o.Id); }
        foreach (var g in GameTypes.Groups) Assert.False(string.IsNullOrWhiteSpace(pt["GtGroup" + g.Key]));
        Assert.True(GameTypes.Popular.Count >= 16);
    }

    [Fact] public async Task BestSellersCanBeFilteredByKindAndKeepTheirSalesOrder()
    {
        var vm = new GameListViewModel(new Fake(), new TextCatalog(), ex => throw ex);
        vm.Chart = "topsellers"; await vm.ShowAsync();
        await vm.SetTypesAsync(["tag:Co-op"], all: false);
        Assert.Equal(["Phasmophobia", "Warframe", "Stardew"], Names(vm));
        Assert.Equal(["2", "3", "4"], vm.Items.Select(i => i.Rank));             // the sales position is kept
        Assert.Contains(new TextCatalog()["GlCharttopsellers"], vm.ChartHint);    // the hint says it is filtered
        vm.Price = "paid";
        Assert.Equal(["Phasmophobia", "Stardew"], Names(vm));
        Assert.Contains(new TextCatalog()["GlPricepaid"], vm.PickScope);
    }

    // With a kind chosen, the chart's own matches come first (with their rank), then popular games of that kind.
    private sealed class RichFake : IGameCatalog
    {
        public Task<GameInfo?> DetailsAsync(string s, string i, string n, CancellationToken t) => Task.FromResult<GameInfo?>(null);
        public Task<GameInfo?> SteamDetailsAsync(string id, CancellationToken t) => Task.FromResult<GameInfo?>(null);
        public Task<IReadOnlyList<CatalogItem>> SearchAsync(string q, CancellationToken t) => Task.FromResult<IReadOnlyList<CatalogItem>>([]);
        public Task<IReadOnlyList<CatalogItem>> DiscoverAsync(string? g, string? d, bool f, CancellationToken t) => Task.FromResult<IReadOnlyList<CatalogItem>>([]);
        public Task<string?> ArtAsync(string id, CancellationToken t) => Task.FromResult<string?>(null);
        public Task<CatalogChart?> ChartAsync(string id, TimeSpan maxAge, CancellationToken t) => Task.FromResult<CatalogChart?>(new(id,
            [new("1", "Chart Horror", "c", "w", 90, 50000), new("2", "Chart Racing", "c", "w", 90, 50000)], DateTimeOffset.Now));
        public Task<IReadOnlyList<CatalogItem>> TypeGamesAsync(string type, CancellationToken t) => Task.FromResult<IReadOnlyList<CatalogItem>>(type == "tag:Horror"
            ? [new("1", "Chart Horror", "c", "w", 90, 50000, 900), .. Enumerable.Range(10, 400).Select(i => new CatalogItem(i.ToString(), "Horror " + i, "c", "w", 80, 1000 + i, i, Free: i % 2 == 0)),
               new("999", "Obscure", "c", "w", 95, 12, 1), new("998", "Disliked", "c", "w", 30, 90000, 5000)]
            : []);
    }

    [Fact] public async Task FilteringFillsTheListWithPopularGamesOfTheKind()
    {
        var vm = new GameListViewModel(new RichFake(), new TextCatalog(), ex => throw ex);
        await vm.ShowAsync();                                                     // "trending"
        await vm.SetTypesAsync(["tag:Horror"], all: false);
        while (vm.CanShowMore) vm.ShowMore();                                     // every page
        Assert.Equal("Chart Horror", vm.Items[0].Name); Assert.Equal("1", vm.Items[0].Rank);      // the chart's own match first
        Assert.Equal("Horror 409", vm.Items[1].Name); Assert.Equal("", vm.Items[1].Rank);          // then by players now
        Assert.DoesNotContain(vm.Items, c => c.Name is "Obscure" or "Disliked");                   // unknown or disliked games stay out
        Assert.Equal(1 + GameListViewModel.MaxExtra, vm.Items.Count);
        Assert.Contains(GameListViewModel.MaxExtra.ToString(), vm.ChartHint);
        vm.Price = "paid"; while (vm.CanShowMore) vm.ShowMore();
        Assert.All(vm.Items.Skip(1), c => Assert.True(int.Parse(c.Item.AppId) % 2 == 1));       // price filter applies to the extra games too
        vm.Chart = "new"; await vm.ShowAsync();
        Assert.Single(vm.Items);                                                                    // no dates there: not filled
    }

    [Fact] public void StoreSearchResultsCarryReviewsAndFreeGames()
    {
        const string html = """
            <a data-ds-appid="1086940" href="#"><span class="title">Baldur&#39;s Gate 3</span><div class="search_released">3 Aug, 2023</div>
              <span class="search_review_summary positive" data-tooltip-html="Overwhelmingly Positive&lt;br&gt;96% of the 712,345 user reviews for this game are positive."></span>
              <div data-price-final="19999"></div></a>
            <a data-ds-appid="230410" href="#"><span class="title">Warframe</span>
              <span class="search_review_summary positive" data-tooltip-html="Muito positivas&lt;br&gt;87% das 600.123 análises de usuários são positivas."></span>
              <div data-price-final="0"></div></a>
            """;
        var items = Lotus.Infrastructure.Catalog.SteamCatalog.Html(html);
        Assert.Equal("Baldur's Gate 3", items[0].Name); Assert.Equal(96, items[0].PositivePercent); Assert.Equal(712345, items[0].Reviews); Assert.Null(items[0].Free);
        Assert.Equal(87, items[1].PositivePercent); Assert.Equal(600123, items[1].Reviews); Assert.True(items[1].Free);
    }
}