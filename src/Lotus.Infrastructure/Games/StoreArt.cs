using System.Runtime.Versioning;
using System.Text.Json;
using Lotus.Core.Catalog;
using Lotus.Core.Games;

namespace Lotus.Infrastructure.Games;

// Wide banner art from the store the game came from (read-only): Steam hero art, Epic's own catalog art
// (kept locally by the Epic launcher), GOG's store background. Returns candidates best first; the caller
// tries them in order and falls back to the next one when an image fails to load.
public static class StoreArt
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly Dictionary<string, string?> GogCache = new(StringComparer.Ordinal);
    private static Dictionary<string, string>? epicWide;

    [SupportedOSPlatform("windows")]
    public static async Task<IReadOnlyList<string>> BannerAsync(string key, string? appId, CancellationToken cancel = default)
    {
        var list = new List<string>();
        var sep = key.IndexOf(':');
        var store = sep > 0 ? key[..sep] : ""; var id = sep > 0 ? key[(sep + 1)..] : "";
        var steamId = appId ?? (store.Equals("steam", StringComparison.OrdinalIgnoreCase) ? id : null);
        if (steamId is { Length: > 0 })
        {
            // Local hero first (no download), then the store's hero, then the store header.
            if (WindowsGameLibrary.DefaultSteamPath() is { } steam && LocalSteamHero(steam, steamId) is { } local) list.Add(local);
            list.Add(CatalogText.Hero(steamId));
            list.Add(CatalogText.Header(steamId));
        }
        else if (store == GameStores.Epic && EpicWide(id) is { } epic) list.Add(epic);
        else if (store == GameStores.Gog && await GogBackgroundAsync(id, cancel).ConfigureAwait(false) is { } gog) list.Add(gog);
        return list;
    }

    private static string? LocalSteamHero(string steam, string appId)
    {
        var cache = Path.Combine(steam, "appcache", "librarycache");
        var flat = Path.Combine(cache, appId + "_library_hero.jpg");
        if (File.Exists(flat)) return flat;
        var folder = Path.Combine(cache, appId);
        try { return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "library_hero*.jpg", SearchOption.AllDirectories).FirstOrDefault() : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    // The Epic launcher keeps its catalog as base64 JSON; "DieselGameBox" is the wide 16:9 key art.
    public static string? EpicWide(string appName, string? catalogPath = null)
    {
        if (epicWide is null || catalogPath is not null)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var path = catalogPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Catalog", "catcache.bin");
            try { if (File.Exists(path)) ParseEpicCatalog(File.ReadAllText(path), map); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or JsonException) { }
            if (catalogPath is not null) return map.GetValueOrDefault(appName);
            epicWide = map;
        }
        return epicWide.GetValueOrDefault(appName);
    }

    public static void ParseEpicCatalog(string base64, Dictionary<string, string> map)
    {
        using var doc = JsonDocument.Parse(Convert.FromBase64String(base64.Trim()));
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("keyImages", out var images) || images.ValueKind != JsonValueKind.Array) continue;
            string? wide = null;
            foreach (var t in new[] { "DieselGameBox", "OfferImageWide", "DieselStoreFrontWide" })
            {
                wide = images.EnumerateArray().Where(i => i.TryGetProperty("type", out var ty) && ty.GetString() == t)
                    .Select(i => i.TryGetProperty("url", out var u) ? u.GetString() : null).FirstOrDefault(u => u is { Length: > 0 });
                if (wide is not null) break;
            }
            if (wide is null || !item.TryGetProperty("releaseInfo", out var releases) || releases.ValueKind != JsonValueKind.Array) continue;
            foreach (var r in releases.EnumerateArray())
                if (r.TryGetProperty("appId", out var a) && a.GetString() is { Length: > 0 } app) map.TryAdd(app, wide);
        }
    }

    // GOG's public product API: images.background is the store page's wide art.
    private static async Task<string?> GogBackgroundAsync(string productId, CancellationToken cancel)
    {
        if (!productId.All(char.IsAsciiDigit) || productId.Length == 0) return null;
        lock (GogCache) if (GogCache.TryGetValue(productId, out var cached)) return cached;
        string? url = null;
        try
        {
            using var stream = await Http.GetStreamAsync($"https://api.gog.com/products/{productId}", cancel).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancel).ConfigureAwait(false);
            if (doc.RootElement.TryGetProperty("images", out var img) && img.TryGetProperty("background", out var bg) && bg.GetString() is { Length: > 0 } b)
                url = b.StartsWith("//", StringComparison.Ordinal) ? "https:" + b : b;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { }
        lock (GogCache) GogCache[productId] = url;
        return url;
    }
}
