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
}