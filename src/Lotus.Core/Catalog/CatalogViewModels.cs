using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lotus.Core.Games;

namespace Lotus.Core.Catalog;

// The game page (Meus Jogos and Game list): synopsis, Metacritic, Steam reviews, estimated difficulty, art.
public sealed class GameDetailsViewModel(IGameCatalog catalog, ITextCatalog text, Action<Exception> log) : ObservableObject
{
    private CancellationTokenSource? cts;
    public ITextCatalog Text { get; } = text;
    public GameInfo? Info { get; private set; }
    public GameEntry? Installed { get; private set; } // set when the game is on this PC (Play button)
    public string Title { get; private set; } = "";
    public string? FallbackArt { get; private set; }
    public bool Loading { get; private set; }
    public bool HasInfo => Info is not null;
    public bool Failed => !Loading && Info is null && Title.Length > 0;
    public bool CanPlay => Installed is not null;
    public string Synopsis => Info?.Summary is { Length: > 0 } s ? s : Text["GiNoSynopsis"];
    public string Genres => Info is { Genres.Count: > 0 } i ? string.Join(" · ", i.Genres.Take(4)) : "";
    public string Release => Info?.ReleaseDate is { Length: > 0 } d ? Text.Format(Info.ComingSoon ? "GiReleaseSoon" : "GiRelease", d) : "";
    public string Studio => Info?.Developer is { Length: > 0 } dev ? dev : "";
    public bool HasMetacritic => Info?.Metacritic is not null;
    public string MetacriticText => Info?.Metacritic?.ToString() ?? "—";
    public string MetacriticKind => Info?.Metacritic switch { >= 75 => "good", >= 50 => "mixed", not null => "bad", _ => "none" };
    public bool HasReviews => Info?.PositivePercent is not null;
    public string ReviewsText => Info?.PositivePercent is { } p ? Text.Format("GiReviews", p, Compact(Info.Reviews ?? 0)) : "";
    public string ReviewsLabel => Info?.PositivePercent switch { >= 95 => Text["GiRevOverwhelming"], >= 80 => Text["GiRevVeryPositive"], >= 70 => Text["GiRevPositive"], >= 40 => Text["GiRevMixed"], not null => Text["GiRevNegative"], _ => "" };
    public int DifficultyLevel => (int)(Info?.Difficulty ?? Difficulty.Unknown);
    public bool HasDifficulty => DifficultyLevel > 0;
    public string DifficultyText => Text["GiDiff" + (Info?.Difficulty ?? Difficulty.Unknown)];
    public string TagsText => Info is { Tags.Count: > 0 } i ? string.Join(" · ", i.Tags.Take(6)) : "";
    // Big key art first (library hero), the store page background as fallback.
    public string? HeroArt => Info?.SteamAppId is { } id ? CatalogText.Hero(id) : null;
    public string MetacriticNote => HasMetacritic ? Text["GiOpenMetacritic"] : Text["GiNoMetacritic"];
    public string Art => Info?.Background ?? Info?.HeaderImage ?? FallbackArt ?? "";
    public string Cover => Info?.HeaderImage ?? FallbackArt ?? "";
    public IReadOnlyList<string> Screenshots => Info?.Screenshots ?? [];
    public string? StoreUrl => Info?.StoreUrl;
    public string? MetacriticUrl => Info?.MetacriticUrl;
    public string SourceText => Info is null ? "" : Text.Format("GiSource", Info.Source);

    // Set by Home's "Continue playing" card: Meus Jogos opens straight on this game.
    public GameEntry? Pending { get; set; }
    public Task LoadInstalledAsync(GameEntry game) => Load(game.Name, game, game.CoverPath, t => catalog.DetailsAsync(game.Store, game.Id, game.Name, t));
    public Task LoadSteamAsync(string appId, string name, GameEntry? installed, string? art) => Load(name, installed, art, t => catalog.SteamDetailsAsync(appId, t));

    private async Task Load(string title, GameEntry? installed, string? art, Func<CancellationToken, Task<GameInfo?>> fetch)
    {
        cts?.Cancel(); cts = new CancellationTokenSource(); var token = cts.Token;
        Title = title; Installed = installed; FallbackArt = art; Info = null; Loading = true; Notify();
        try { var info = await fetch(token); if (token.IsCancellationRequested) return; Info = info; }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { log(ex); }
        Loading = false;
        if (Info is { } i && Title.Length == 0) Title = i.Name;
        Notify();
    }

    public static string Compact(long n) => n >= 1_000_000 ? (n / 1_000_000.0).ToString("0.#") + " mi" : n >= 1000 ? (n / 1000.0).ToString("0.#") + " mil" : n.ToString();
    private void Notify() { foreach (var p in typeof(GameDetailsViewModel).GetProperties().Where(p => p.Name != nameof(Text))) OnPropertyChanged(p.Name); }
}

public sealed record ChartTab(string Id, string Label);
public sealed record GenreChip(string Id, string Label);

public sealed partial class CatalogCard(CatalogItem item, string meta, bool installed) : ObservableObject
{
    public CatalogItem Item { get; } = item;
    public string Name => Item.Name;
    public string Cover => Item.Cover;
    public string Wide => Item.Wide;
    public string Meta { get; } = meta;
    public bool Installed { get; } = installed;
    public string Rank { get; init; } = "";
}

// "Game list": charts that refresh on their own from open data, plus "I don't know what to play" (a filtered draw).
public sealed class GameListViewModel(IGameCatalog catalog, ITextCatalog text, Action<Exception> log, Func<IReadOnlyList<GameEntry>>? installed = null) : ObservableObject
{
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromHours(6);
    private readonly Dictionary<string, CatalogChart> charts = [];
    private string chart = "trending";
    private string status = "";
    private bool loading;
    public ITextCatalog Text => text;
    public GameDetailsViewModel Details { get; } = new(catalog, text, log);
    public IReadOnlyList<ChartTab> Tabs => [.. CatalogText.Charts.Select(c => new ChartTab(c, text["GlChart" + c]))];
    public ObservableCollection<CatalogCard> Items { get; } = [];
    public string Chart { get => chart; set { if (value is not null && SetProperty(ref chart, value)) _ = ShowAsync(); } }
    public bool Loading { get => loading; private set => SetProperty(ref loading, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string ChartHint => text["GlHint" + chart];

    // Picker filters
    public IReadOnlyList<GenreChip> GenreChips => [.. new[] { "any", "Action", "Adventure", "RPG", "Strategy", "Indie", "Racing", "Sports", "Simulation", "Casual" }.Select(g => new GenreChip(g, text["GlGenre" + g]))];
    public string Genre { get; set; } = "any";
    public string DifficultyWanted { get; set; } = "any"; // any | easy | medium | hard
    public bool OnlyNotOwned { get; set; } = true;
    public bool OnlyFree { get; set; }
    public bool Picking { get; private set; }
    public string PickStatus { get; private set; } = "";
    public CatalogItem? Picked { get; private set; }
    public bool HasPick => Picked is not null;

    // ---- Search and list filters ----
    private IReadOnlyList<CatalogItem> source = [];
    private string query = "";
    private CancellationTokenSource? searchCts;
    public string Query { get => query; set { if (SetProperty(ref query, value ?? "")) _ = SearchAsync(); } }
    public bool Searching => query.Trim().Length >= 2;
    public IReadOnlyList<ChartTab> SortOptions => [.. new[] { "rank", "rating", "metacritic", "name" }.Select(s => new ChartTab(s, text["GlSort" + s]))];
    public IReadOnlyList<ChartTab> RatingOptions => [.. new[] { "0", "70", "80", "90" }.Select(s => new ChartTab(s, text["GlMin" + s]))];
    private string sort = "rank", minRating = "0";
    private bool hideOwned, freeOnly;
    public string Sort { get => sort; set { if (value is not null && SetProperty(ref sort, value)) ApplyFilters(); } }
    public string MinRating { get => minRating; set { if (value is not null && SetProperty(ref minRating, value)) ApplyFilters(); } }
    public bool HideOwned { get => hideOwned; set { if (SetProperty(ref hideOwned, value)) ApplyFilters(); } }
    public bool FreeOnly { get => freeOnly; set { if (SetProperty(ref freeOnly, value)) ApplyFilters(); } }
    public bool IsEmpty => !Loading && Items.Count == 0;

    public async Task ShowAsync(bool force = false)
    {
        if (Searching) { await SearchAsync(); return; }
        Loading = true; Status = text["GlLoading"];
        try
        {
            var c = await Get(chart, force);
            source = c?.Items ?? [];
            Status = c is null ? text["GlOffline"] : text.Format("GlUpdated", c.FetchedAt.ToLocalTime().ToString("g", System.Globalization.CultureInfo.GetCultureInfo(text.Language)));
        }
        catch (Exception ex) { log(ex); Status = text["GlOffline"]; }
        finally { Loading = false; ApplyFilters(); OnPropertyChanged(nameof(ChartHint)); }
    }

    // Typing searches the whole Steam store (after a short pause); clearing the box goes back to the chart.
    private async Task SearchAsync()
    {
        searchCts?.Cancel(); var cts = searchCts = new CancellationTokenSource();
        OnPropertyChanged(nameof(Searching));
        if (!Searching) { await ShowAsync(); return; }
        try
        {
            await Task.Delay(350, cts.Token);
            Loading = true; Status = text["GlSearching"];
            var found = await catalog.SearchAsync(query, cts.Token);
            if (cts.IsCancellationRequested) return;
            source = found; Status = text.Format(found.Count == 0 ? "GlSearchNone" : "GlSearchFound", found.Count, query.Trim());
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { log(ex); Status = text["GlOffline"]; }
        Loading = false; ApplyFilters();
    }

    public void ApplyFilters()
    {
        var owned = OwnedIds(); var min = int.TryParse(minRating, out var m) ? m : 0;
        var rows = source.Select((item, index) => (item, index))
            .Where(r => !hideOwned || !owned.Contains(r.item.AppId))
            .Where(r => !freeOnly || r.item.Free == true)
            .Where(r => min == 0 || (r.item.PositivePercent ?? r.item.Metacritic ?? 0) >= min);
        rows = sort switch
        {
            "rating" => rows.OrderByDescending(r => r.item.PositivePercent ?? -1),
            "metacritic" => rows.OrderByDescending(r => r.item.Metacritic ?? -1),
            "name" => rows.OrderBy(r => r.item.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => rows.OrderBy(r => r.index),
        };
        Items.Clear();
        foreach (var (item, index) in rows) Items.Add(new(item, Meta(item), owned.Contains(item.AppId)) { Rank = Searching ? "" : (index + 1).ToString() });
        OnPropertyChanged(nameof(IsEmpty));
    }

    // Home banner: a chart without touching the visible list.
    public Task<CatalogChart?> ChartForBannerAsync(string id) => Get(id, false);

    // Called by a timer while Lotus is open: re-downloads charts older than six hours.
    public Task RefreshIfStaleAsync() => charts.TryGetValue(chart, out var c) && DateTimeOffset.Now - c.FetchedAt < RefreshEvery ? Task.CompletedTask : ShowAsync(force: true);

    private async Task<CatalogChart?> Get(string id, bool force)
    {
        if (!force && charts.TryGetValue(id, out var c) && DateTimeOffset.Now - c.FetchedAt < RefreshEvery) return c;
        var fresh = await catalog.ChartAsync(id, force ? TimeSpan.Zero : RefreshEvery, CancellationToken.None);
        if (fresh is not null) charts[id] = fresh;
        return fresh ?? charts.GetValueOrDefault(id);
    }

    private string Meta(CatalogItem i) => i.Players is { } p ? text.Format("GlPlayers", GameDetailsViewModel.Compact(p))
        : i.PositivePercent is { } pct ? text.Format("GlPositive", pct) : i.Metacritic is { } mc ? text.Format("GlMetacritic", mc) : i.Free == true ? text["GlFree"] : "";

    private HashSet<string> OwnedIds() => [.. (installed?.Invoke() ?? []).Where(g => g.Store == GameStores.Steam).Select(g => g.Id)];
    public GameEntry? InstalledFor(string appId) => installed?.Invoke().FirstOrDefault(g => g.Store == GameStores.Steam && g.Id == appId);

    // Set by Home's banner: the Game list opens straight on this game's page.
    public CatalogItem? PendingOpen { get; set; }
    public async Task<string?> ArtAsync(string appId) { try { return await catalog.ArtAsync(appId, CancellationToken.None); } catch (Exception ex) { log(ex); return null; } }
    public Task OpenAsync(CatalogItem item) => Details.LoadSteamAsync(item.AppId, item.Name, InstalledFor(item.AppId), item.Wide);

    // Draw: asks the store for games with the chosen genre/difficulty tags (top sellers first), skips the ones the
    // player has, and confirms with the game's details before showing it. Offline, it falls back to the saved charts.
    public async Task PickAsync(Random? random = null)
    {
        if (Picking) return;
        random ??= Random.Shared;
        Picking = true; Picked = null; PickStatus = text["GlPicking"]; NotifyPick();
        try
        {
            var pool = (await catalog.DiscoverAsync(Genre == "any" ? null : Genre, DifficultyWanted == "any" ? null : DifficultyWanted, OnlyFree, CancellationToken.None)).ToList();
            var fromStore = pool.Count > 0;
            if (!fromStore)
                foreach (var id in CatalogText.Charts.Where(c => c != "upcoming"))
                    if (await Get(id, false) is { } c) pool.AddRange(c.Items);
            var owned = OwnedIds();
            var candidates = pool.DistinctBy(i => i.AppId).Where(i => !OnlyNotOwned || !owned.Contains(i.AppId)).OrderBy(_ => random.Next()).ToList();
            (CatalogItem Item, GameInfo Info)? gentle = null; // "easy" fallback: a game nobody calls difficult
            foreach (var item in candidates.Take(12))
            {
                var info = await catalog.SteamDetailsAsync(item.AppId, CancellationToken.None);
                if (info is null || info.ComingSoon) continue;
                if (!Matches(info, trustTags: fromStore))
                {
                    if (DifficultyWanted == "easy" && info.Difficulty is Difficulty.Medium && Matches(info with { Difficulty = Difficulty.Easy }, fromStore)) gentle ??= (item, info);
                    continue;
                }
                await Show(item, info); return;
            }
            if (gentle is { } g) { await Show(g.Item, g.Info); return; }
            PickStatus = text[candidates.Count == 0 ? "GlPickEmpty" : "GlPickNone"];
        }
        catch (Exception ex) { log(ex); PickStatus = text["GlOffline"]; }
        finally { Picking = false; NotifyPick(); }
    }

    private async Task Show(CatalogItem item, GameInfo info)
    {
        Picked = item; PickStatus = "";
        await Details.LoadSteamAsync(item.AppId, info.Name, InstalledFor(item.AppId), item.Wide);
    }

    // trustTags: the pool already comes from the chosen Steam genre; difficulty is always checked on the game's top tags.
    public bool Matches(GameInfo info, bool trustTags = false)
    {
        if (OnlyFree && !info.IsFree) return false;
        if (!trustTags && Genre != "any" && !info.Genres.Concat(info.Tags).Any(g => GenreAliases(Genre).Contains(g, StringComparer.OrdinalIgnoreCase))) return false;
        return DifficultyWanted switch
        {
            "easy" => info.Difficulty == Difficulty.Easy,
            "medium" => info.Difficulty is Difficulty.Medium or Difficulty.Unknown,
            "hard" => info.Difficulty is Difficulty.Hard or Difficulty.VeryHard,
            _ => true,
        };
    }
    // Steam genre names come in the store language; the tags are always English.
    private static string[] GenreAliases(string g) => g switch
    {
        "Action" => ["Action", "Ação"], "Adventure" => ["Adventure", "Aventura"], "RPG" => ["RPG"], "Strategy" => ["Strategy", "Estratégia"],
        "Indie" => ["Indie"], "Racing" => ["Racing", "Corrida"], "Sports" => ["Sports", "Esportes"], "Simulation" => ["Simulation", "Simulação"], "Casual" => ["Casual"], _ => [g],
    };
    private void NotifyPick() { foreach (var n in new[] { nameof(Picking), nameof(PickStatus), nameof(Picked), nameof(HasPick) }) OnPropertyChanged(n); }
}
