using Lotus.Core.Catalog;
using Lotus.Core.Diary;
using Xunit;

namespace Lotus.Tests;

public sealed class DiaryArtTests
{
    private sealed class Memory : IGameDiaryStore { public DiaryData Saved = new(); public DiaryData Load() => Saved; public void Save(DiaryData d) => Saved = d; }
    private sealed class Store : IGameCatalog
    {
        public Dictionary<string, string> Portraits = [];
        public Dictionary<string, List<CatalogItem>> Results = new(StringComparer.OrdinalIgnoreCase);
        public int Searches;
        public Task<GameInfo?> DetailsAsync(string s, string i, string n, CancellationToken t) => Task.FromResult<GameInfo?>(null);
        public Task<GameInfo?> SteamDetailsAsync(string id, CancellationToken t) => Task.FromResult<GameInfo?>(null);
        public Task<IReadOnlyList<CatalogItem>> SearchAsync(string q, CancellationToken t) { Searches++; return Task.FromResult<IReadOnlyList<CatalogItem>>(Results.GetValueOrDefault(q) ?? []); }
        public Task<IReadOnlyList<CatalogItem>> DiscoverAsync(string? g, string? d, bool f, CancellationToken t) => Task.FromResult<IReadOnlyList<CatalogItem>>([]);
        public Task<string?> ArtAsync(string id, CancellationToken t) => Task.FromResult<string?>("header-" + id);
        public Task<CatalogChart?> ChartAsync(string id, TimeSpan m, CancellationToken t) => Task.FromResult<CatalogChart?>(null);
        public Task<string?> PortraitAsync(string id, CancellationToken t) => Task.FromResult(Portraits.GetValueOrDefault(id));
    }

    [Fact] public async Task MissingAndGuessedCoversAreRepairedWithoutReorderingTheShelf()
    {
        var diary = new GameDiary(new Memory(), ex => throw ex);
        diary.Save(new DiaryEntry { Key = "steam:3041230", AppId = "3041230", Name = "The Blood of Dawnwalker", Cover = CatalogText.Portrait("3041230"), Status = DiaryStatus.Playing });
        diary.Save(new DiaryEntry { Key = "Epic Games:Ghost", Name = "Ghost of Tsushima™ DIRECTOR'S CUT", Status = DiaryStatus.Playing });
        diary.Save(new DiaryEntry { Key = "Manual:custom-1", Name = "Sonic Unleashed", Status = DiaryStatus.Playing });
        diary.Save(new DiaryEntry { Key = "Manual:custom-2", Name = "Modrinth App (Minecraft)", Status = DiaryStatus.Want });
        diary.Save(new DiaryEntry { Key = "steam:10", AppId = "10", Name = "Fine", Cover = "https://ok/cover.jpg" });
        var updated = diary.Entries.ToDictionary(e => e.Key, e => e.Updated);

        var store = new Store
        {
            Portraits = { ["3041230"] = "https://hashed/3041230.jpg", ["2215430"] = "https://hashed/ghost.jpg" },
            Results =
            {
                ["Ghost of Tsushima™ DIRECTOR'S CUT"] = [new("2215430", "Ghost of Tsushima DIRECTOR'S CUT", "c", "w")],
                ["Sonic Unleashed"] = [new("71340", "Sonic Generations", "c", "w")], // a different game: never used
            },
        };
        var art = new DiaryArt(diary, store, ex => throw ex) { LibraryCover = k => k == "Manual:custom-2" ? @"C:\covers\modrinth.png" : null, FileExists = _ => true };
        Assert.Equal(3, await art.RepairAllAsync());

        Assert.Equal("https://hashed/3041230.jpg", diary.Get("steam:3041230")!.Cover);
        Assert.Equal("https://hashed/ghost.jpg", diary.Get("Epic Games:Ghost")!.Cover);
        Assert.Equal("w", diary.Get("Epic Games:Ghost")!.Wide);
        Assert.Null(diary.Get("Manual:custom-1")!.Cover);                       // no exact title on the store
        Assert.Equal(@"C:\covers\modrinth.png", diary.Get("Manual:custom-2")!.Cover);
        Assert.Equal("https://ok/cover.jpg", diary.Get("steam:10")!.Cover);     // good covers are left alone
        Assert.All(diary.Entries, e => Assert.Equal(updated[e.Key], e.Updated)); // shelf order kept
        Assert.Equal(2, store.Searches);                                         // only entries without an id or library art

        Assert.Equal(0, await art.RepairAllAsync());                              // each failure is looked up once per run
        Assert.Equal(2, store.Searches);
    }

    [Fact] public async Task AnImageThatFailsOnScreenFallsBackToTheStoreHeader()
    {
        var diary = new GameDiary(new Memory(), ex => throw ex);
        diary.Save(new DiaryEntry { Key = "steam:5", AppId = "5", Name = "Old", Cover = "https://gone/5.jpg" });
        var art = new DiaryArt(diary, new Store(), ex => throw ex);
        Assert.False(art.NeedsArt(diary.Get("steam:5")!));
        Assert.True(await art.RepairAsync("steam:5", broken: "https://gone/5.jpg"));
        Assert.Equal("header-5", diary.Get("steam:5")!.Cover);
        Assert.False(await art.RepairAsync("steam:5", broken: "https://gone/5.jpg")); // not retried in a loop
    }

    [Fact] public void TitlesMatchIgnoringSymbolsAccentsAndCase()
    {
        Assert.Equal(DiaryArt.Normalize("Baldur's Gate 3"), DiaryArt.Normalize("BALDUR’S GATE 3™"));
        Assert.Equal("pokemon", DiaryArt.Normalize("Pokémon"));
        Assert.NotEqual(DiaryArt.Normalize("Sonic Unleashed"), DiaryArt.Normalize("Sonic Generations"));
    }
}
