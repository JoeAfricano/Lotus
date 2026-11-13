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

    // The same game as a diary entry (Steam id when known, so the Game list and Meus Jogos share one entry).
    public Diary.DiaryEntry ToDiaryEntry()
    {
        var id = Info?.SteamAppId ?? (Installed is { Store: GameStores.Steam } g ? g.Id : null);
        return new()
        {
            Key = id is not null ? Diary.DiaryEntry.SteamKey(id) : Installed?.Key ?? "name:" + Title.ToLowerInvariant(),
            Name = Info?.Name ?? Title, AppId = id,
            Cover = id is not null ? CatalogText.Portrait(id) : Installed?.CoverPath ?? FallbackArt,
            Wide = Info?.HeaderImage ?? (id is not null ? CatalogText.Header(id) : FallbackArt),
            Genres = [.. Info?.Genres ?? []],
        };
    }
    public bool HasTitle => Title.Length > 0;

    // "Tempo para zerar" (HowLongToBeat), looked up after the page opens; the card stays hidden when nothing is known.
    public Diary.GameLengthLookup? Lengths { get; set; }
    public Diary.GameLength? Length { get; private set; }
    public bool HasLength => Length is { HasAny: true };
    public string LengthMain => Length is { } l ? LengthHours(l.MainMinutes ?? l.PlusMinutes ?? l.AllMinutes) : "";
    public string LengthMainLabel => Length switch { { MainMinutes: > 0 } => Text["GiBeatStory"], { PlusMinutes: > 0 } => Text["GiBeatPlus"], _ => Text["GiBeatAll"] };
    public string LengthMore => Length is not { } l ? "" : string.Join(" · ", new[]
    {
        l.MainMinutes is > 0 && l.PlusMinutes is > 0 ? Text.Format("DyBeatPlus", LengthHours(l.PlusMinutes)) : null,
        (l.MainMinutes is > 0 || l.PlusMinutes is > 0) && l.AllMinutes is > 0 ? Text.Format("DyBeatAll", LengthHours(l.AllMinutes)) : null,
    }.Where(x => x is not null));
    public string? LengthUrl => Length?.Url;
    private static string LengthHours(int? minutes) => minutes is > 0 and var m ? Diary.DiaryText.Hours(m) : "";

    private async Task LoadLengthAsync(CancellationToken token)
    {
        if (Lengths is not { } lengths || !HasTitle) return;
        var entry = ToDiaryEntry();
        try
        {
            var length = await lengths.FindAsync(entry.Key, entry.Name, entry.AppId, token);
            if (token.IsCancellationRequested) return;
            Length = length;
            foreach (var n in new[] { nameof(Length), nameof(HasLength), nameof(LengthMain), nameof(LengthMainLabel), nameof(LengthMore), nameof(LengthUrl) }) OnPropertyChanged(n);
        }
        catch (OperationCanceledException) { }
    }

    public string SourceText => Info is null ? "" : Text.Format("GiSource", Info.Source);

    // Set by Home's "Continue playing" card: Meus Jogos opens straight on this game.
    public GameEntry? Pending { get; set; }
    public Task LoadInstalledAsync(GameEntry game) => Load(game.Name, game, game.CoverPath, t => catalog.DetailsAsync(game.Store, game.Id, game.Name, t));
    public Task LoadSteamAsync(string appId, string name, GameEntry? installed, string? art) { global::Lotus.Core.Achievements.Activity.Record(global::Lotus.Core.Achievements.Activity.Details); return Load(name, installed, art, t => catalog.SteamDetailsAsync(appId, t)); }

    private async Task Load(string title, GameEntry? installed, string? art, Func<CancellationToken, Task<GameInfo?>> fetch)
    {
        cts?.Cancel(); cts = new CancellationTokenSource(); var token = cts.Token;
        Title = title; Installed = installed; FallbackArt = art; Info = null; Length = null; Loading = true; Notify();
        try { var info = await fetch(token); if (token.IsCancellationRequested) return; Info = info; }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { log(ex); }
        Loading = false;
        if (Info is { } i && Title.Length == 0) Title = i.Name;
        Notify();
        await LoadLengthAsync(token);
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
    // Period (Semanal / Mensal / Anual / Sempre): by release date, the only date these lists really carry. Past window for
    // the charts, the coming window for "Em breve". Lists without dates (SteamSpy) keep the pills off instead of guessing.
    private string period = "all";
    public string Period { get => period; set { if (value is not null && SetProperty(ref period, value)) ApplyFilters(); } }
    public bool PeriodAvailable => !Searching && source.Any(i => i.Released is not null);
    private bool InPeriod(CatalogItem i)
    {
        if (period == "all" || !PeriodAvailable) return true;
        if (i.Released is not { } d) return false;
        var days = period switch { "week" => 7, "month" => 30, _ => 365 };
        var today = DateTime.Today;
        return chart == "upcoming" ? d >= today && d <= today.AddDays(days) : d <= today && d >= today.AddDays(-days);
    }
    public int? ChartCount(string id) => id == chart && !Searching ? filtered.Count : charts.TryGetValue(id, out var c) ? c.Items.Count : null; // null until that list was loaded once
    public IReadOnlyList<ChartTab> Tabs =>[.. CatalogText.Charts.Select(c => new ChartTab(c, text["GlChart" + c]))];
    public BatchCollection<CatalogCard> Items { get; } = [];
    public string Chart { get => chart; set { if (value is not null && SetProperty(ref chart, value)) _ = ShowAsync(); } }
    public bool Loading { get => loading; private set => SetProperty(ref loading, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }

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
    private string sort = "rank", minRating = "0", price = "any";
    private bool hideOwned, matchAll;
    private List<string> types = [];
    private readonly Dictionary<string, IReadOnlySet<string>> typeSets = [];
    private readonly Dictionary<string, IReadOnlyList<CatalogItem>> typeGames = [];
    public const int MaxExtra = 300; // popular games of the chosen kinds shown after the chart's own matches
    private int extraCount;

    // Kinds of game (several at once): "any of the chosen" or "all of them at once". Lists come from Steam (cached).
    public IReadOnlyList<string> SelectedTypes => types;
    public bool MatchAll => matchAll;
    public string TypeLabel(string id) => text[new GameTypeOption(id).LabelKey];
    public string TypeSummary => types.Count switch { 0 => text["GtButton"], 1 => TypeLabel(types[0]), _ => text.Format("GtButtonMany", types.Count) };
    public bool HasTypes => types.Count > 0;
    // What the draw uses (the list's kind and price filters), in words.
    public string PickScope => (types.Count == 0 ? text["GtAnyKind"] : TypeSummary) + (price != "any" ? " · " + text["GlPrice" + price] : "");
    public string ChartHint => Searching || !HasActiveFilters ? text["GlHint" + chart]
        : extraCount > 0 ? text.Format("GlHintFilteredMore", filtered.Count - extraCount, text["GlChart" + chart], extraCount)
        : text.Format("GlHintFiltered", filtered.Count, text["GlChart" + chart]);
    public async Task SetTypesAsync(IEnumerable<string> ids, bool all)
    {
        var wanted = ids.Where(GameTypes.IsKnown).Distinct().ToList();
        types = wanted; matchAll = all;
        OnPropertyChanged(nameof(SelectedTypes)); OnPropertyChanged(nameof(TypeSummary)); OnPropertyChanged(nameof(HasTypes)); OnPropertyChanged(nameof(MatchAll)); OnPropertyChanged(nameof(PickScope));
        var missing = wanted.Where(t => !typeSets.ContainsKey(t)).ToList();
        if (missing.Count > 0)
        {
            var before = Status;
            Loading = true; Status = text["GlTypeLoading"];
            try
            {
                foreach (var t in missing)
                {
                    var games = await catalog.TypeGamesAsync(t, CancellationToken.None);
                    typeGames[t] = games;
                    typeSets[t] = games.Count > 0 ? games.Select(g => g.AppId).ToHashSet() : await catalog.TypeIdsAsync(t, CancellationToken.None);
                }
                Status = before;
            }
            catch (Exception ex) { log(ex); Status = text["GlOffline"]; }
            finally { Loading = false; }
            if (!ReferenceEquals(types, wanted)) return; // the player changed the choice meanwhile
        }
        ApplyFilters();
    }
    private bool MatchesTypes(string appId)
    {
        if (types.Count == 0) return true;
        var sets = types.Select(t => typeSets.GetValueOrDefault(t)).OfType<IReadOnlySet<string>>().ToList();
        if (sets.Count == 0) return true; // lists unavailable (offline): do not hide everything
        return matchAll ? sets.All(s => s.Contains(appId)) : sets.Any(s => s.Contains(appId));
    }
    // Price: every game, only free ones, or without free ones.
    public IReadOnlyList<ChartTab> PriceOptions => [.. new[] { "any", "free", "paid" }.Select(p => new ChartTab(p, text["GlPrice" + p])) ];
    public string Price { get => price; set { if (value is not null && SetProperty(ref price, value)) { OnPropertyChanged(nameof(FreeOnly)); OnPropertyChanged(nameof(PickScope)); ApplyFilters(); } } }
    public string Sort { get => sort; set { if (value is not null && SetProperty(ref sort, value)) ApplyFilters(); } }
    public string MinRating { get => minRating; set { if (value is not null && SetProperty(ref minRating, value)) ApplyFilters(); } }
    public bool HideOwned { get => hideOwned; set { if (SetProperty(ref hideOwned, value)) ApplyFilters(); } }
    public bool FreeOnly { get => price == "free"; set => Price = value ? "free" : price == "free" ? "any" : price; }
    public bool IsEmpty => !Loading && Items.Count == 0;
    // Filters always have a textual indicator (guide 10), and one action clears them all.
    public bool HasActiveFilters => hideOwned || price != "any" || types.Count > 0 || minRating != "0" || sort != "rank";
    public string ActiveFilters => !HasActiveFilters ? text["GlFiltersNone"] : text.Format("GlFiltersActive", string.Join(" · ", new[]
    {
        sort != "rank" ? text["GlSort" + sort] : null, minRating != "0" ? text["GlMin" + minRating] : null,
        types.Count > 0 ? TypeSummary : null, price != "any" ? text["GlPrice" + price] : null, hideOwned ? text["GlHideOwned"] : null,
    }.OfType<string>()));
    public void ClearFilters()
    {
        sort = "rank"; minRating = "0"; hideOwned = false; price = "any"; types = []; matchAll = false;
        foreach (var n in new[] { nameof(Sort), nameof(MinRating), nameof(HideOwned), nameof(FreeOnly), nameof(Price), nameof(SelectedTypes), nameof(TypeSummary), nameof(HasTypes), nameof(PickScope) }) OnPropertyChanged(n);
        ApplyFilters();
    }

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
        if (!prefetched) { prefetched = true; _ = PrefetchAsync(); }
    }
    // The other lists download quietly, one at a time, after the first one shows: the first click on each tab is then instant.
    private bool prefetched;
    private async Task PrefetchAsync()
    {
        foreach (var id in CatalogText.Charts.Where(c => c != chart))
        {
            try { await Get(id, false); } catch (Exception ex) { log(ex); }
        }
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
        bool Keep(CatalogItem i) => (!hideOwned || !owned.Contains(i.AppId))
            && price switch { "free" => i.Free == true, "paid" => i.Free != true, _ => true }
            && (min == 0 || (i.PositivePercent ?? i.Metacritic ?? 0) >= min)
            && InPeriod(i);
        var rows = source.Select((item, index) => (item, index, chart: true)).Where(r => MatchesTypes(r.item.AppId) && Keep(r.item)).ToList();
        // With kinds chosen, the chart alone holds few games: popular games of those kinds follow, ordered by what the
        // chart means (players now, best rated, most reviewed). New and upcoming lists are not filled (no dates there).
        var extra = Extras(source.Select(i => i.AppId).ToHashSet()).Where(Keep).Take(MaxExtra).ToList();
        extraCount = extra.Count;
        rows.AddRange(extra.Select((item, i) => (item, source.Count + i, chart: false)));
        IEnumerable<(CatalogItem item, int index, bool chart)> ordered = sort switch
        {
            "rating" => rows.OrderByDescending(r => r.item.PositivePercent ?? -1),
            "metacritic" => rows.OrderByDescending(r => r.item.Metacritic ?? -1),
            "name" => rows.OrderBy(r => r.item.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => rows.OrderBy(r => r.index),
        };
        // Shown in pages: a long chart never realizes every cover at once (the page asks for more while scrolling).
        filtered = [.. ordered.Select(r => new CatalogCard(r.item, Meta(r.item), owned.Contains(r.item.AppId)) { Rank = Searching || !r.chart ? "" : (r.index + 1).ToString() })];
        Items.ReplaceAll(filtered.Take(PageSize)); // one reset instead of one event per card: the grid lays out once
        OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(PeriodAvailable)); OnPropertyChanged(nameof(ActiveFilters)); OnPropertyChanged(nameof(HasActiveFilters)); OnPropertyChanged(nameof(ChartHint)); OnPropertyChanged(nameof(CanShowMore)); OnPropertyChanged(nameof(MoreText));
    }

    private IEnumerable<CatalogItem> Extras(HashSet<string> inChart)
    {
        if (types.Count == 0 || Searching || chart is "new" or "upcoming") return [];
        var lists = types.Select(t => typeGames.GetValueOrDefault(t)).OfType<IReadOnlyList<CatalogItem>>().Where(l => l.Count > 0).ToList();
        if (lists.Count == 0) return [];
        var pool = matchAll
            ? lists[0].Where(g => types.All(t => typeSets.GetValueOrDefault(t)?.Contains(g.AppId) ?? true))
            : lists.SelectMany(l => l).DistinctBy(g => g.AppId);
        // Known, liked games only (when the numbers are known); the source order (best sellers of that kind) is kept otherwise.
        var list = pool.Where(g => !inChart.Contains(g.AppId) && (g.Reviews is null || g.Reviews >= 300) && (g.PositivePercent is null || g.PositivePercent >= 60)).ToList();
        return chart switch
        {
            "trending" when list.Any(g => g.Players > 0) => list.OrderByDescending(g => g.Players ?? 0).ThenByDescending(g => g.Reviews ?? 0),
            "toprated" when list.Any(g => g.PositivePercent is not null) => list.Where(g => (g.Reviews ?? 0) >= 2000).OrderByDescending(g => g.PositivePercent ?? 0).ThenByDescending(g => g.Reviews ?? 0),
            _ => list,
        };
    }    public const int PageSize = 48;
    private List<CatalogCard> filtered = [];
    public bool CanShowMore => Items.Count < filtered.Count;
    public string MoreText => text.Format("GlShowMore", Items.Count, filtered.Count);
    public void ShowMore()
    {
        if (!CanShowMore) return;
        Items.AddRange(filtered.Skip(Items.Count).Take(PageSize).ToList());
        OnPropertyChanged(nameof(CanShowMore)); OnPropertyChanged(nameof(MoreText));
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
    // A big picture for banners when the key art is missing: the first store screenshot (1920 wide).
    public async Task<string?> ScreenshotAsync(string appId) { try { var i = await catalog.SteamDetailsAsync(appId, CancellationToken.None); return i?.Screenshots.FirstOrDefault() ?? i?.Background; } catch (Exception ex) { log(ex); return null; } }
    public async Task<string?> ArtAsync(string appId) { try { return await catalog.ArtAsync(appId, CancellationToken.None); } catch (Exception ex) { log(ex); return null; } }
    public async Task<string?> PortraitAsync(string appId) { try { return await catalog.PortraitAsync(appId, CancellationToken.None); } catch (Exception ex) { log(ex); return null; } }
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
            // The draw follows the list's filters: one Steam genre narrows the pool directly; tags and several kinds filter it.
            var genre = types.Count == 1 && types[0].StartsWith("genre:") ? types[0][6..] : Genre == "any" ? null : Genre;
            var pool = (await catalog.DiscoverAsync(genre, DifficultyWanted == "any" ? null : DifficultyWanted, OnlyFree || price == "free", CancellationToken.None)).ToList();
            var fromStore = pool.Count > 0;
            if (!fromStore)
                foreach (var id in CatalogText.Charts.Where(c => c != "upcoming"))
                    if (await Get(id, false) is { } c) pool.AddRange(c.Items);
            var owned = OwnedIds();
            var candidates = pool.DistinctBy(i => i.AppId).Where(i => !OnlyNotOwned || !owned.Contains(i.AppId))
                .Where(i => MatchesTypes(i.AppId) && (price != "paid" || i.Free != true)).OrderBy(_ => random.Next()).ToList();
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
        Picked = item; PickStatus = ""; global::Lotus.Core.Achievements.Activity.Record(global::Lotus.Core.Achievements.Activity.Pick);
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

// An ObservableCollection that can swap or extend its whole content with a single notification.
public sealed class BatchCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var i in items) Items.Add(i);
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
    }
    public void AddRange(IReadOnlyList<T> items)
    {
        if (items.Count == 0) return;
        foreach (var i in items) Add(i); // appended items keep the grid's realized rows; few per page
    }
}