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

    public async Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, CancellationToken token)
    {
        var q = query.Trim();
        if (q.Length < 2) return [];
        var node = await JsonAsync($"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(q)}&l={language}&cc={country}", $"search-{Safe(q)}.json", TimeSpan.FromDays(1), token);
        return Search(node);
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
        var file = Path.Combine(cache, $"chart-{id}-{language}.json");
        if (Fresh(file, maxAge) && ReadChart(file) is { } cached) return cached;
        try
        {
            var items = id switch
            {
                "trending" => SpyList(await JsonAsync("https://steamspy.com/api.php?request=top100in2weeks", null, maxAge, token), byRating: false),
                "toprated" => SpyList(await JsonAsync("https://steamspy.com/api.php?request=top100forever", null, maxAge, token), byRating: true),
                "topsellers" or "new" or "upcoming" => Featured(await JsonAsync($"https://store.steampowered.com/api/featuredcategories?cc={country}&l={language}", null, maxAge, token),
                    id switch { "topsellers" => "top_sellers", "new" => "new_releases", _ => "coming_soon" }),
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

    public static List<CatalogItem> SpyList(JsonNode? node, bool byRating)
    {
        if (node is not JsonObject obj) return [];
        var rows = obj.Select(kv => kv.Value).OfType<JsonObject>()
            .Select(a => (Id: Long(a["appid"]).ToString(), Name: a["name"]?.GetValue<string>() ?? "", Pos: Long(a["positive"]), Neg: Long(a["negative"]), Ccu: Long(a["ccu"])))
            .Where(a => a.Name.Length > 0 && !NotGames.Contains(a.Id));
        var ordered = byRating ? rows.Where(a => a.Pos + a.Neg >= 20000).OrderByDescending(a => a.Pos * 1.0 / (a.Pos + a.Neg)) : rows.OrderByDescending(a => a.Ccu);
        return [.. ordered.Take(60).Select(a => new CatalogItem(a.Id, a.Name, CatalogText.Portrait(a.Id), CatalogText.Header(a.Id),
            a.Pos + a.Neg > 0 ? (int)Math.Round(a.Pos * 100.0 / (a.Pos + a.Neg)) : null, a.Pos + a.Neg, a.Ccu > 0 ? a.Ccu : null))];
    }

    public static List<CatalogItem> Featured(JsonNode? node, string key)
    {
        var items = node?[key]?["items"]?.AsArray();
        if (items is null) return [];
        var seen = new HashSet<string>();
        return [.. items.OfType<JsonObject>()
            .Where(i => i["type"]?.GetValue<int>() is null or 0) // 0 = app (bundles and packages skipped)
            .Select(i => (Id: Long(i["id"]).ToString(), Name: i["name"]?.GetValue<string>() ?? ""))
            .Where(i => i.Name.Length > 0 && !NotGames.Contains(i.Id) && seen.Add(i.Id))
            .Select(i => new CatalogItem(i.Id, i.Name, CatalogText.Portrait(i.Id), CatalogText.Header(i.Id)))];
    }

    // ---- HTTP + cache ----
    private async Task<JsonNode?> JsonAsync(string url, string? cacheName, TimeSpan maxAge, CancellationToken token)
    {
        var file = cacheName is null ? null : Path.Combine(cache, cacheName);
        if (file is not null && Fresh(file, maxAge)) { try { return JsonNode.Parse(await File.ReadAllTextAsync(file, token)); } catch (JsonException) { } }
        await gate.WaitAsync(token);
        try
        {
            var text = await http.GetStringAsync(url, token);
            var node = JsonNode.Parse(text);
            if (file is not null && node is not null) await File.WriteAllTextAsync(file, text, token);
            return node;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Serilog.Log.Warning(ex, "Catalog request failed {Url}", url);
            if (file is not null && File.Exists(file)) { try { return JsonNode.Parse(await File.ReadAllTextAsync(file, token)); } catch (JsonException) { } }
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
