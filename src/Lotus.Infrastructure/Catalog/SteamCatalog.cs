using System.Text.Json;
using System.Text.Json.Nodes;
using Lotus.Core.Catalog;

namespace Lotus.Infrastructure.Catalog;

// Open, keyless sources: Steam store (details, Metacritic, charts), SteamSpy (community tags, reviews, most played)
// and GOG (descriptions of GOG games). Everything is cached under %LOCALAPPDATA%\Lotus\Catalog so the list and the
// details keep working offline; requests are spaced out so the free APIs are not hammered.
public sealed class SteamCatalog : IGameCatalog
{
    private readonly HttpClient http;
    private readonly string cache;
    private readonly string language, country;
    private readonly SemaphoreSlim gate = new(2);
    private static readonly TimeSpan DetailsAge = TimeSpan.FromDays(5);

    public SteamCatalog(HttpClient? http = null, string? cacheDir = null, string language = "pt-BR")
    {
        this.http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        if (http is null) this.http.DefaultRequestHeaders.UserAgent.ParseAdd("Lotus/1.0 (+desktop game helper)");
        cache = cacheDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "Catalog");
        Directory.CreateDirectory(cache);
        (this.language, country) = language == "en-US" ? ("english", "US") : ("brazilian", "BR");
    }

    public async Task<GameInfo?> DetailsAsync(string store, string id, string name, CancellationToken token)
    {
        if (store == "Steam" && id.All(char.IsDigit)) return await SteamDetailsAsync(id, token);
        GameInfo? gog = store == "GOG" && id.All(char.IsDigit) ? await GogAsync(id, token) : null;
        // Any store: the same game on the Steam store gives Metacritic, tags and art.
        var appId = await SearchSteamAsync(name, token);
        var steam = appId is null ? null : await SteamDetailsAsync(appId, token);
        if (steam is null) return gog;
        return gog is null ? steam with { Source = steam.Source + " (por nome)" } : steam with { Summary = steam.Summary.Length > 0 ? steam.Summary : gog.Summary, Source = "GOG + Steam" };
    }

    // Any game for the Lotus Score FPS estimate: first store hit and its recommended (else minimum) PC requirements as text.
    public async Task<(string Name, string AppId, string? Requirements)?> RequirementsAsync(string query, CancellationToken token)
    {
        var hit = (await SearchAsync(query, token)).FirstOrDefault();
        if (hit is null) return null;
        var store = await JsonAsync($"https://store.steampowered.com/api/appdetails?appids={hit.AppId}&l={language}&cc={country}", $"app-{hit.AppId}-{language}.json", DetailsAge, token);
        var req = store?[hit.AppId]?["data"]?["pc_requirements"];
        var html = (req?["recommended"] ?? req?["minimum"])?.ToString();
        var text = html is null ? null : System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(html.Replace("<br>", "\n").Replace("<li>", "\n"), "<[^>]+>", " "));
        return (hit.Name, hit.AppId, text);
    }

    public async Task<GameInfo?> SteamDetailsAsync(string appId, CancellationToken token)
    {
        var store = await JsonAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&l={language}&cc={country}", $"app-{appId}-{language}.json", DetailsAge, token);
        var spy = await JsonAsync($"https://steamspy.com/api.php?request=appdetails&appid={appId}", $"spy-{appId}.json", DetailsAge, token);
        return ParseSteam(appId, store, spy);
    }

    public static GameInfo? ParseSteam(string appId, JsonNode? store, JsonNode? spy)
    {
        var data = store?[appId]?["success"]?.GetValue<bool>() == true ? store[appId]!["data"] : null;
        var tags = Tags(spy);
        if (data is null && spy?["name"] is null) return null;
        long pos = Long(spy?["positive"]), neg = Long(spy?["negative"]);
        int? percent = pos + neg >= 50 ? (int)Math.Round(pos * 100.0 / (pos + neg)) : null;
        var name = data?["name"]?.GetValue<string>() ?? spy?["name"]?.GetValue<string>() ?? appId;
        var shots = data?["screenshots"]?.AsArray().Select(s => s?["path_full"]?.GetValue<string>()).OfType<string>().Take(6).ToList() ?? [];
        return new GameInfo(
            name, appId,
            CatalogText.Plain(data?["short_description"]?.GetValue<string>()),
            data?["genres"]?.AsArray().Select(g => g?["description"]?.GetValue<string>()).OfType<string>().ToList() ?? (spy?["genre"]?.GetValue<string>() is { Length: > 0 } g2 ? [.. g2.Split(", ")] : []),
            data?["release_date"]?["date"]?.GetValue<string>(),
            Join(data?["developers"]) ?? spy?["developer"]?.GetValue<string>(), Join(data?["publishers"]) ?? spy?["publisher"]?.GetValue<string>(),
            data?["metacritic"]?["score"]?.GetValue<int>(), data?["metacritic"]?["url"]?.GetValue<string>(),
            percent, pos + neg > 0 ? pos + neg : null,
            [.. tags.OrderByDescending(t => t.Value).Take(8).Select(t => t.Key)], GameDifficulty.Estimate(tags),
            data?["header_image"]?.GetValue<string>() ?? CatalogText.Header(appId), data?["background_raw"]?.GetValue<string>() ?? CatalogText.Hero(appId), shots,
            CatalogText.Store(appId), data?["is_free"]?.GetValue<bool>() == true, "Steam", data?["release_date"]?["coming_soon"]?.GetValue<bool>() == true);
    }

    private async Task<GameInfo?> GogAsync(string id, CancellationToken token)
    {
        var node = await JsonAsync($"https://api.gog.com/products/{id}?expand=description&locale={(language == "english" ? "en-US" : "pt-BR")}", $"gog-{id}.json", DetailsAge, token);
        if (node?["title"]?.GetValue<string>() is not { } title) return null;
        var text = CatalogText.Plain(node["description"]?["lead"]?.GetValue<string>() is { Length: > 0 } lead ? lead : node["description"]?["full"]?.GetValue<string>());
        if (text.Length > 600) text = text[..600].TrimEnd() + "…";
        var released = node["release_date"]?.GetValue<string>(); if (released is { Length: > 10 }) released = released[..10];
        return new GameInfo(title, null, text, [], released, null, null, null, null, null, null, [], Difficulty.Unknown,
            node["images"]?["logo2x"]?.GetValue<string>() is { } logo ? "https:" + logo.TrimStart(':') : null, null, [], node["links"]?["product_card"]?.GetValue<string>(), false, "GOG");
    }

    private async Task<string?> SearchSteamAsync(string name, CancellationToken token)
    {
        var clean = new string(name.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Trim();
        if (clean.Length < 2) return null;
        var node = await JsonAsync($"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(clean)}&l={language}&cc={country}", $"search-{Safe(clean)}.json", TimeSpan.FromDays(14), token);
        var items = node?["items"]?.AsArray();
        if (items is null || items.Count == 0) return null;
        static string Norm(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var exact = items.FirstOrDefault(i => Norm(i?["name"]?.GetValue<string>() ?? "") == Norm(clean)) ?? items[0];
        return exact?["id"]?.GetValue<long>().ToString();
    }

    // Store search page as JSON (up to 50 games, no DLC/bundles: category1=998).
    private string StoreSearch(string query, int start = 0) => $"https://store.steampowered.com/search/results/?infinite=1&start={start}&count=100&category1=998&cc={country}&l={language}&{query}";
    public const int ChartSize = 250;

    // Several pages of the store search (100 games each), joined without repeats.
    private async Task<List<CatalogItem>> StorePagesAsync(string query, TimeSpan maxAge, CancellationToken token, Func<string, string>? url = null, int pages = 3, string? cachePrefix = null)
    {
        var all = new List<CatalogItem>();
        var seen = new HashSet<string>();
        for (var p = 0; p < pages; p++)
        {
            var page = Results(await JsonAsync((url ?? (u => u))(StoreSearch(query, p * 100)), cachePrefix is null ? null : $"{cachePrefix}-{p}.json", maxAge, token));
            if (page.Count == 0) break;
            all.AddRange(page.Where(i => seen.Add(i.AppId)));
        }
        return all;
    }

    // Draw pool from SteamSpy's full lists: every game of a Steam genre (or the 1,000 most owned), well reviewed,
    // most reviewed first. "Hard" keeps games the community tags Difficult; "easy" drops them. The final difficulty
    // check happens on the game's own top tags (see GameListViewModel.Matches).
    public async Task<IReadOnlyList<CatalogItem>> DiscoverAsync(string? genre, string? difficulty, bool freeOnly, CancellationToken token)
    {
        var source = genre is null ? "https://steamspy.com/api.php?request=all&page=0" : $"https://steamspy.com/api.php?request=genre&genre={Uri.EscapeDataString(genre)}";
        var games = Spy(await JsonAsync(source, $"spy-{(genre ?? "all").ToLowerInvariant()}.json", TimeSpan.FromDays(3), token));
        if (difficulty is "hard" or "easy")
        {
            var difficult = Spy(await JsonAsync("https://steamspy.com/api.php?request=tag&tag=Difficult", "spy-tag-difficult.json", TimeSpan.FromDays(3), token)).Select(g => g.Id).ToHashSet();
            if (difficult.Count > 0) games = [.. games.Where(g => difficult.Contains(g.Id) == (difficulty == "hard"))];
        }
        return [.. games.Where(g => g.Pos + g.Neg >= 1000 && g.Pos * 100 >= (g.Pos + g.Neg) * 70 && (!freeOnly || g.Free) && !NotGames.Contains(g.Id))
            .OrderByDescending(g => g.Pos + g.Neg).Take(300)
            .Select(g => new CatalogItem(g.Id, g.Name, CatalogText.Portrait(g.Id), CatalogText.Header(g.Id), (int)Math.Round(g.Pos * 100.0 / (g.Pos + g.Neg)), g.Pos + g.Neg, Free: g.Free))];
    }
    // Every Steam game of a genre or community tag (SteamSpy full lists, cached 3 days, shared with the draw).
    public async Task<IReadOnlySet<string>> TypeIdsAsync(string type, CancellationToken token) => (await TypeGamesAsync(type, token)).Select(g => g.AppId).ToHashSet();

    public async Task<IReadOnlyList<CatalogItem>> TypeGamesAsync(string type, CancellationToken token)
    {
        var parts = type.Split(':', 2);
        if (parts.Length != 2 || parts[1].Length == 0) return [];
        var name = parts[1];
        // The store's own tag search: only games whose main tags include it (players' top tags), best sellers first.
        if (await TagIdAsync(name, token) is { } tagId)
        {
            var store = await StorePagesAsync($"tags={tagId}&filter=topsellers", TimeSpan.FromDays(1), token, pages: 5, cachePrefix: $"tag-{tagId}-{country}");
            if (store.Count > 0) return store;
        }
        // Fallback: SteamSpy's list (broader: any game that ever received the tag).
        var (url, cache) = parts[0] == "tag"
            ? ($"https://steamspy.com/api.php?request=tag&tag={Uri.EscapeDataString(name)}", $"spy-tag-{name.ToLowerInvariant().Replace(' ', '-')}.json")
            : ($"https://steamspy.com/api.php?request=genre&genre={Uri.EscapeDataString(name)}", $"spy-{name.ToLowerInvariant()}.json");
        if (await JsonAsync(url, cache, TimeSpan.FromDays(3), token) is not JsonObject list) return [];
        return [.. list.Select(kv => kv.Value).OfType<JsonObject>().Select(a => (Id: Long(a["appid"]).ToString(), Name: a["name"]?.GetValue<string>() ?? "", Pos: Long(a["positive"]), Neg: Long(a["negative"]), Ccu: Long(a["ccu"]), Free: a["price"]?.ToString() == "0"))
            .Where(g => g.Name.Length > 0 && !NotGames.Contains(g.Id))
            .Select(g => new CatalogItem(g.Id, g.Name, CatalogText.Portrait(g.Id), CatalogText.Header(g.Id), g.Pos + g.Neg > 0 ? (int)Math.Round(g.Pos * 100.0 / (g.Pos + g.Neg)) : null, g.Pos + g.Neg, g.Ccu, Free: g.Free))];
    }
    // Steam's tag names → ids (English names, the same ones Lotus's kinds use). Cached for a week.
    private Dictionary<string, long>? tagIds;
    private async Task<long?> TagIdAsync(string name, CancellationToken token)
    {
        if (tagIds is null && await JsonAsync("https://store.steampowered.com/tagdata/populartags/english", "steam-tags.json", TimeSpan.FromDays(7), token) is JsonArray tags)
        {
            var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in tags.OfType<JsonObject>()) if (t["name"]?.GetValue<string>() is { Length: > 0 } n) map.TryAdd(n, Long(t["tagid"]));
            if (map.Count > 0) tagIds = map;
        }
        return tagIds is not null && tagIds.TryGetValue(name, out var id) && id > 0 ? id : null;
    }
    private static List<(string Id, string Name, long Pos, long Neg, bool Free)> Spy(JsonNode? node) => node is JsonObject o
        ? [.. o.Select(kv => kv.Value).OfType<JsonObject>().Select(a => (Long(a["appid"]).ToString(), a["name"]?.GetValue<string>() ?? "", Long(a["positive"]), Long(a["negative"]), a["price"]?.ToString() == "0")).Where(a => a.Item2.Length > 0)]
        : [];

    // Header art: the guessed CDN path first, the store API (hashed path) when that one does not exist.
    // One request per 40 games (the store accepts a list when only the price is asked); cached for a few hours.
    public async Task<IReadOnlyDictionary<string, GamePrice>> PricesAsync(IReadOnlyList<string> appIds, CancellationToken token)
    {
        var result = new Dictionary<string, GamePrice>();
        foreach (var chunk in appIds.Where(a => a.Length > 0 && a.All(char.IsAsciiDigit)).Distinct().Order(StringComparer.Ordinal).Chunk(40))
        {
            var ids = string.Join(",", chunk);
            var node = await JsonAsync($"https://store.steampowered.com/api/appdetails?appids={ids}&filters=price_overview&cc={country}", $"prices-{country}-{Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(ids)))[..12]}.json", PriceAge, token);
            if (node is not System.Text.Json.Nodes.JsonObject all) continue;
            foreach (var id in chunk)
            {
                if (all[id]?["data"]?["price_overview"] is not System.Text.Json.Nodes.JsonObject p) continue;
                var discount = (int?)p["discount_percent"] ?? 0;
                result[id] = new(id, discount, (string?)p["final_formatted"] ?? "", (string?)p["initial_formatted"] ?? "");
            }
        }
        return result;
    }
    private static readonly TimeSpan PriceAge = TimeSpan.FromHours(6);
    public async Task<string?> ArtAsync(string appId, CancellationToken token) => (await SteamDetailsAsync(appId, token))?.HeaderImage;

    // Exact portrait art (600x900) from the store API: newer games keep it under a hashed path the guessed URL misses.
    // Covers that fail together are asked in one request (up to 50); answers are kept on disk ("" = no portrait art).
    private readonly Dictionary<string, string> portraits = [];
    private readonly Dictionary<string, TaskCompletionSource<string?>> portraitWaiting = [];
    private bool portraitsLoaded, portraitBatchQueued;
    private readonly object portraitLock = new();
    private string PortraitFile => Path.Combine(cache, "portraits.json");

    public Task<string?> PortraitAsync(string appId, CancellationToken token)
    {
        lock (portraitLock)
        {
            if (!portraitsLoaded)
            {
                portraitsLoaded = true;
                try { if (File.Exists(PortraitFile) && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(PortraitFile)) is { } saved) foreach (var kv in saved) portraits[kv.Key] = kv.Value; }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
            }
            if (portraits.TryGetValue(appId, out var known)) return Task.FromResult<string?>(known.Length > 0 ? known : null);
            if (!portraitWaiting.TryGetValue(appId, out var waiting)) portraitWaiting[appId] = waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!portraitBatchQueued) { portraitBatchQueued = true; _ = PortraitBatchAsync(); }
            return waiting.Task;
        }
    }

    private async Task PortraitBatchAsync()
    {
        await Task.Delay(150); // let the covers failing at the same moment join this request
        List<KeyValuePair<string, TaskCompletionSource<string?>>> batch;
        lock (portraitLock) { batch = [.. portraitWaiting.Take(50)]; foreach (var kv in batch) portraitWaiting.Remove(kv.Key); portraitBatchQueued = false; if (portraitWaiting.Count > 0) { portraitBatchQueued = true; _ = PortraitBatchAsync(); } }
        var found = new Dictionary<string, string>();
        try
        {
            var ids = string.Join(",", batch.Select(kv => $"{{\"appid\":{kv.Key}}}"));
            var input = $"{{\"ids\":[{ids}],\"context\":{{\"language\":\"english\",\"country_code\":\"US\"}},\"data_request\":{{\"include_assets\":true}}}}";
            var text = await http.GetStringAsync("https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json=" + Uri.EscapeDataString(input));
            if (JsonNode.Parse(text)?["response"]?["store_items"] is JsonArray items)
                foreach (var item in items.OfType<JsonObject>())
                {
                    var id = Long(item["appid"] ?? item["id"]).ToString();
                    var assets = item["assets"];
                    var file = assets?["library_capsule_2x"]?.GetValue<string>() ?? assets?["library_capsule"]?.GetValue<string>();
                    var format = assets?["asset_url_format"]?.GetValue<string>();
                    found[id] = file is { Length: > 0 } && format is { Length: > 0 } ? "https://shared.akamai.steamstatic.com/store_item_assets/" + format.Replace("${FILENAME}", file) : "";
                }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException) { Serilog.Log.Warning(ex, "Portrait art lookup"); }
        lock (portraitLock)
        {
            foreach (var (id, waiting) in batch)
            {
                var url = found.GetValueOrDefault(id);
                if (url is not null) portraits[id] = url; // unanswered (offline) ids are asked again next time
                waiting.TrySetResult(url is { Length: > 0 } ? url : null);
            }
            try { Directory.CreateDirectory(cache); File.WriteAllText(PortraitFile, JsonSerializer.Serialize(portraits)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    // Store search page (infinite mode): results_html carries data-ds-appid and the title of each game.
    public static List<CatalogItem> Results(JsonNode? node)
    {
        if (node?["results_html"]?.GetValue<string>() is { } html) return Html(html);
        if (node?["items"] is not JsonArray items) return [];
        var seen = new HashSet<string>();
        var list = new List<CatalogItem>();
        foreach (var i in items.OfType<JsonObject>())
        {
            var name = i["name"]?.GetValue<string>() ?? ""; var logo = i["logo"]?.GetValue<string>() ?? "";
            var m = System.Text.RegularExpressions.Regex.Match(logo, @"/apps/(\d+)/");
            if (!m.Success || name.Length == 0) continue;
            var id = m.Groups[1].Value;
            if (NotGames.Contains(id) || !seen.Add(id)) continue;
            list.Add(new(id, name, CatalogText.Portrait(id), CatalogText.Header(id), Note: logo));
        }
        return list;
    }

    public static List<CatalogItem> Html(string html)
    {
        var list = new List<CatalogItem>(); var seen = new HashSet<string>();
        // One block per result: from its data-ds-appid up to the next one.
        foreach (System.Text.RegularExpressions.Match block in System.Text.RegularExpressions.Regex.Matches(html, @"data-ds-appid=""(\d+)""([\s\S]*?)(?=data-ds-appid=""|$)"))
        {
            var id = block.Groups[1].Value; var body = block.Groups[2].Value;
            var title = System.Text.RegularExpressions.Regex.Match(body, @"<span class=""title"">([^<]+)</span>");
            if (!title.Success) continue;
            var name = System.Net.WebUtility.HtmlDecode(title.Groups[1].Value).Trim();
            if (name.Length == 0 || NotGames.Contains(id) || !seen.Add(id)) continue;
            var date = System.Text.RegularExpressions.Regex.Match(body, @"search_released[^>]*>\s*([^<]+?)\s*<");
            DateTime? released = date.Success && DateTime.TryParseExact(date.Groups[1].Value.Trim(), ["d MMM, yyyy", "MMM d, yyyy"], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null;
            // Review summary tooltip: "93% of the 12,345 user reviews…" (any language: first percent, then the first number after it).
            int? percent = null; long? reviews = null;
            var tip = System.Text.RegularExpressions.Regex.Match(body, @"search_review_summary[^""]*""\s+data-tooltip-html=""([^""]+)""");
            if (tip.Success)
            {
                var r = System.Text.RegularExpressions.Regex.Match(System.Net.WebUtility.HtmlDecode(tip.Groups[1].Value), @"(\d{1,3})%\D+?(\d[\d.,]*)");
                if (r.Success)
                {
                    percent = int.Parse(r.Groups[1].Value);
                    if (long.TryParse(new string([.. r.Groups[2].Value.Where(char.IsDigit)]), out var n)) reviews = n;
                }
            }
            var free = System.Text.RegularExpressions.Regex.IsMatch(body, @"data-price-final=""0""");
            list.Add(new(id, name, CatalogText.Portrait(id), CatalogText.Header(id), percent, reviews, Released: released, Free: free ? true : null));
        }
        return list;
    }

    public async Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, CancellationToken token)
    {
        var q = query.Trim();
        if (q.Length < 2) return [];
        // Relevance-ordered quick search (10 best, with Metacritic) + the full store search (up to 50) for completeness.
        var quick = Search(await JsonAsync($"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(q)}&l={language}&cc={country}", $"search-{Safe(q)}.json", TimeSpan.FromDays(1), token));
        var full = Results(await JsonAsync(StoreSearch("term=" + Uri.EscapeDataString(q)), $"searchfull-{Safe(q)}.json", TimeSpan.FromDays(1), token));
        if (full.Count == 0) return quick; // offline / page changed: the quick list is better than nothing
        var games = full.Select(f => f.AppId).ToHashSet();
        var first = quick.Where(q => games.Contains(q.AppId)).ToList(); // drops soundtracks and other non-game apps
        return [.. first.Concat(full.Where(f => first.All(x => x.AppId != f.AppId)))];
    }

    public static List<CatalogItem> Search(JsonNode? node) => node?["items"] is JsonArray items
        ? [.. items.OfType<JsonObject>().Where(i => i["type"]?.GetValue<string>() == "app" && i["platforms"]?["windows"]?.GetValue<bool>() != false)
            .Select(i => (Id: Long(i["id"]).ToString(), Name: i["name"]?.GetValue<string>() ?? "", Meta: (int)Long(i["metascore"]), Free: i["price"] is null))
            .Where(i => i.Name.Length > 0 && !NotGames.Contains(i.Id))
            .Select(i => new CatalogItem(i.Id, i.Name, CatalogText.Portrait(i.Id), CatalogText.Header(i.Id), Metacritic: i.Meta > 0 ? i.Meta : null, Free: i.Free))]
        : [];

    // ---- Charts ----
    public async Task<CatalogChart?> ChartAsync(string id, TimeSpan maxAge, CancellationToken token)
    {
        var file = Path.Combine(cache, $"chart4-{id}-{language}.json"); // "4": up to 250 items per chart (older caches are ignored)
        if (Fresh(file, maxAge) && ReadChart(file) is { } cached) return cached;
        try
        {
            var items = id switch
            {
                // SteamSpy's top 100 lists joined with its 1,000 most owned games (players now / share of positive reviews).
                "trending" => SpyList([await JsonAsync("https://steamspy.com/api.php?request=top100in2weeks", null, maxAge, token),
                    await JsonAsync("https://steamspy.com/api.php?request=all&page=0", "spy-all.json", TimeSpan.FromDays(1), token)], byRating: false),
                "toprated" => SpyList([await JsonAsync("https://steamspy.com/api.php?request=top100forever", null, maxAge, token),
                    await JsonAsync("https://steamspy.com/api.php?request=all&page=0", "spy-all.json", TimeSpan.FromDays(1), token)], byRating: true),
                // The store's own search, games only: what Steam shows as top sellers / popular new / popular upcoming (3 pages).
                "topsellers" => [.. (await StorePagesAsync("filter=topsellers", maxAge, token)).Take(ChartSize)],
                "upcoming" => [.. (await StorePagesAsync("filter=popularcomingsoon", maxAge, token)).Take(ChartSize)],
                // "New": best sellers released in the last 120 days (English page so the dates parse).
                "new" => [.. (await StorePagesAsync("filter=topsellers", maxAge, token, u => u.Replace("l=" + language, "l=english"), pages: 5))
                    .Where(i => i.Released is { } d && d <= DateTime.Today && DateTime.Today - d <= TimeSpan.FromDays(120)).Take(ChartSize)],
                _ => null,
            };
            if (items is { Count: > 0 })
            {
                var chart = new CatalogChart(id, items, DateTimeOffset.Now);
                await File.WriteAllTextAsync(file, JsonSerializer.Serialize(chart, CatalogJson.Default.CatalogChart), token);
                return chart;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException) { Serilog.Log.Warning(ex, "Catalog chart {Id}", id); }
        return ReadChart(file); // offline: whatever was saved last
    }

    // Software that shows up in Steam charts but is not a game.
    private static readonly HashSet<string> NotGames = ["431960", "1070560", "1391110", "1628350", "228980", "250820", "1007", "993090", "365670", "323910", "1905180", "2507950"];

    public static List<CatalogItem> SpyList(JsonNode? node, bool byRating) => SpyList([node], byRating);
    public static List<CatalogItem> SpyList(IEnumerable<JsonNode?> nodes, bool byRating)
    {
        var rows = nodes.OfType<JsonObject>().SelectMany(o => o.Select(kv => kv.Value).OfType<JsonObject>())
            .Select(a => (Id: Long(a["appid"]).ToString(), Name: a["name"]?.GetValue<string>() ?? "", Pos: Long(a["positive"]), Neg: Long(a["negative"]), Ccu: Long(a["ccu"])))
            .Where(a => a.Name.Length > 0 && !NotGames.Contains(a.Id)).DistinctBy(a => a.Id);
        var ordered = byRating ? rows.Where(a => a.Pos + a.Neg >= 20000).OrderByDescending(a => a.Pos * 1.0 / (a.Pos + a.Neg)) : rows.OrderByDescending(a => a.Ccu);
        return [.. ordered.Take(ChartSize).Select(a => new CatalogItem(a.Id, a.Name, CatalogText.Portrait(a.Id), CatalogText.Header(a.Id),
            a.Pos + a.Neg > 0 ? (int)Math.Round(a.Pos * 100.0 / (a.Pos + a.Neg)) : null, a.Pos + a.Neg, a.Ccu > 0 ? a.Ccu : null))];
    }

    // ---- HTTP + cache ----
    private async Task<JsonNode?> JsonAsync(string url, string? cacheName, TimeSpan maxAge, CancellationToken token)
    {
        var file = cacheName is null ? null : Path.Combine(cache, cacheName);
        if (file is not null && Fresh(file, maxAge)) { try { return JsonNode.Parse(await File.ReadAllTextAsync(file, token)); } catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { } }
        await gate.WaitAsync(token);
        try
        {
            var text = await http.GetStringAsync(url, token);
            var node = JsonNode.Parse(text);
            // A cache that cannot be written (locked, disk full) must not throw away a good download.
            if (file is not null && node is not null) { try { await File.WriteAllTextAsync(file, text, CancellationToken.None); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Serilog.Log.Warning(ex, "Catalog cache write {File}", file); } }
            return node;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Serilog.Log.Warning(ex, "Catalog request failed {Url}", url);
            if (file is not null && File.Exists(file)) { try { return JsonNode.Parse(await File.ReadAllTextAsync(file, CancellationToken.None)); } catch (Exception e2) when (e2 is JsonException or IOException or UnauthorizedAccessException) { } }
            return null;
        }
        finally { await Task.Delay(250, CancellationToken.None); gate.Release(); }
    }

    private static bool Fresh(string file, TimeSpan maxAge) => File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < maxAge;
    private static CatalogChart? ReadChart(string file) { try { return File.Exists(file) ? JsonSerializer.Deserialize(File.ReadAllText(file), CatalogJson.Default.CatalogChart) : null; } catch (Exception ex) when (ex is JsonException or IOException) { return null; } }
    private static string Safe(string s) { var x = new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray()); return x.Length > 40 ? x[..40] : x; }
    private static long Long(JsonNode? n) => n is JsonValue v ? (v.TryGetValue<long>(out var l) ? l : v.TryGetValue<string>(out var s) && long.TryParse(s, out var p) ? p : 0) : 0;
    private static string? Join(JsonNode? arr) => arr is JsonArray a && a.Count > 0 ? string.Join(", ", a.Select(x => x?.GetValue<string>()).OfType<string>()) : null;
    private static Dictionary<string, long> Tags(JsonNode? spy) => spy?["tags"] is JsonObject t ? t.ToDictionary(kv => kv.Key, kv => Long(kv.Value)) : [];
}

// Source-generated JSON for the chart cache (trimming-safe).
[System.Text.Json.Serialization.JsonSerializable(typeof(CatalogChart))]
internal sealed partial class CatalogJson : System.Text.Json.Serialization.JsonSerializerContext { }
