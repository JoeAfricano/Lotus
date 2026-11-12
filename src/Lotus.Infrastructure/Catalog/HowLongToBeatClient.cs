using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lotus.Core.Diary;

namespace Lotus.Infrastructure.Catalog;

// HowLongToBeat has no public API: the site's own search is used the way its web page does it (the search address
// comes from the site's script, then a short-lived token from "<address>/init"). The same approach is kept working by
// the open-source Playnite plugin (github.com/Lacro59/playnite-howlongtobeat-plugin, MIT). When the site changes,
// lookups fail quietly and the diary simply shows no times. Only titles are sent; nothing about the player.
public sealed partial class HowLongToBeatClient(HttpClient? http = null) : IGameLengthSource
{
    private const string Site = "https://howlongtobeat.com";
    private readonly HttpClient client = http ?? Create();
    private string? endpoint;
    private (string Token, string? Key, string? Value, DateTimeOffset At)? auth;
    private readonly SemaphoreSlim gate = new(1, 1);

    private static HttpClient Create()
    {
        var c = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/javascript, */*; q=0.01");
        return c;
    }

    [GeneratedRegex("""<script[^>]*src=["']([^"']+)["'][^>]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptSrc();
    [GeneratedRegex("""fetch\s*\(\s*["']/api/([a-zA-Z0-9_/]+)[^"']*["']\s*,\s*\{[^}]*method:\s*["']POST["'][^}]*\}""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PostApi();

    public async Task<GameLength?> FindAsync(string name, string? steamAppId, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var rows = await SearchAsync(Clean(name), token).ConfigureAwait(false);
            return Pick(rows, name, steamAppId);
        }
        finally { gate.Release(); }
    }

    // Trademark signs and edition words get in the way of the site's search; the match itself stays exact.
    public static string Clean(string name) => Regex.Replace(name.Replace("™", "").Replace("®", "").Replace("©", ""), @"\s+", " ").Trim();

    // The Steam id when the site links it; otherwise one row with exactly this title (or alias). Times come in seconds.
    public static GameLength? Pick(JsonArray? rows, string name, string? steamAppId)
    {
        if (rows is null) return null;
        var list = rows.OfType<JsonObject>().ToList();
        JsonObject? hit = null;
        if (steamAppId is { Length: > 0 }) hit = list.FirstOrDefault(r => Num(r["profile_steam"])?.ToString() == steamAppId);
        if (hit is null)
        {
            var wanted = DiaryArt.Normalize(name);
            var exact = list.Where(r => DiaryArt.Normalize(Str(r["game_name"])) == wanted
                || Str(r["game_alias"]).Split(',').Any(a => DiaryArt.Normalize(a) == wanted)).ToList();
            if (exact.Count == 1) hit = exact[0];
        }
        if (hit is null) return null;
        static int? Minutes(JsonNode? seconds) => Num(seconds) is { } s && s > 0 ? (int)Math.Round(s / 60.0) : null;
        var id = Num(hit["game_id"]);
        var length = new GameLength(Minutes(hit["comp_main"]), Minutes(hit["comp_plus"]), Minutes(hit["comp_100"]), id is > 0 ? $"{Site}/game/{id}" : null);
        return length.HasAny ? length : null;
    }

    private static long? Num(JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<double>(out var d)) return (long)d;
        return v.TryGetValue<string>(out var s) && long.TryParse(s, out var p) ? p : null;
    }
    private static string Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    private async Task<JsonArray?> SearchAsync(string name, CancellationToken token)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            endpoint ??= await DiscoverAsync(token).ConfigureAwait(false);
            if (endpoint is null) return null;
            if (auth is null || DateTimeOffset.UtcNow - auth.Value.At > TimeSpan.FromMinutes(1)) auth = await AuthAsync(endpoint, token).ConfigureAwait(false);
            if (auth is not { } a) { endpoint = null; continue; }

            var body = new JsonObject
            {
                ["searchType"] = "games",
                ["searchTerms"] = new JsonArray([.. name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => (JsonNode)t)]),
                ["searchPage"] = 1, ["size"] = 20,
                ["searchOptions"] = new JsonObject
                {
                    ["games"] = new JsonObject
                    {
                        ["userId"] = 0, ["platform"] = "", ["sortCategory"] = "popular", ["rangeCategory"] = "main",
                        ["rangeTime"] = new JsonObject { ["min"] = 0, ["max"] = 0 },
                        ["gameplay"] = new JsonObject { ["perspective"] = "", ["flow"] = "", ["genre"] = "", ["difficulty"] = "" },
                        ["rangeYear"] = new JsonObject { ["min"] = "", ["max"] = "" }, ["modifier"] = "",
                    },
                    ["users"] = new JsonObject { ["sortCategory"] = "postcount" }, ["lists"] = new JsonObject { ["sortCategory"] = "follows" },
                    ["filter"] = "", ["sort"] = 0, ["randomizer"] = 0,
                },
                ["useCache"] = true,
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, Site + endpoint) { Content = JsonContent.Create(body) };
            request.Headers.Referrer = new Uri(Site + "/"); request.Headers.Add("Origin", Site);
            request.Headers.Add("x-auth-token", a.Token);
            if (a.Key is { Length: > 0 } key && a.Value is { Length: > 0 } value)
            {
                body[key] = value; request.Content = JsonContent.Create(body);
                request.Headers.Add("x-hp-key", key); request.Headers.Add("x-hp-val", value);
            }
            using var response = await client.SendAsync(request, token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Unauthorized) { endpoint = null; auth = null; continue; } // site changed: look again once
            response.EnsureSuccessStatusCode();
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false))?["data"] as JsonArray;
        }
        return null;
    }

    private async Task<string?> DiscoverAsync(CancellationToken token)
    {
        var html = await client.GetStringAsync(Site + "/", token).ConfigureAwait(false);
        var scripts = ScriptSrc().Matches(html).Select(m => m.Groups[1].Value).Distinct().OrderBy(u => u.Contains("_app-") ? 0 : 1).Take(20);
        foreach (var src in scripts)
        {
            var url = src.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? src : Site + "/" + src.TrimStart('/');
            if (!url.StartsWith(Site + "/", StringComparison.OrdinalIgnoreCase)) continue; // only the site's own scripts
            string script;
            try { script = await client.GetStringAsync(url, token).ConfigureAwait(false); }
            catch (HttpRequestException) { continue; }
            foreach (Match m in PostApi().Matches(script))
            {
                var suffix = m.Groups[1].Value.Trim('/');
                if (suffix.Length == 0 || suffix.Split('/')[0].Equals("find", StringComparison.OrdinalIgnoreCase)) continue;
                return "/api/" + suffix;
            }
        }
        return null;
    }

    private async Task<(string Token, string? Key, string? Value, DateTimeOffset At)?> AuthAsync(string api, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Site}{api}/init?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
        request.Headers.Referrer = new Uri(Site + "/");
        using var response = await client.SendAsync(request, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        try
        {
            var data = JsonNode.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
            var t = data?["token"]?.ToString();
            return t is { Length: > 0 } ? (t, data?["hpKey"]?.ToString(), data?["hpVal"]?.ToString(), DateTimeOffset.UtcNow) : null;
        }
        catch (JsonException) { return null; }
    }
}
