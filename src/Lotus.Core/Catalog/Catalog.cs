namespace Lotus.Core.Catalog;

// Game facts from open store data (Steam store, SteamSpy community tags, GOG). Read-only, cached on disk, no account.
public enum Difficulty { Unknown, Easy, Medium, Hard, VeryHard }

public sealed record GameInfo(
    string Name, string? SteamAppId, string Summary, IReadOnlyList<string> Genres, string? ReleaseDate,
    string? Developer, string? Publisher, int? Metacritic, string? MetacriticUrl, int? PositivePercent, long? Reviews,
    IReadOnlyList<string> Tags, Difficulty Difficulty, string? HeaderImage, string? Background, IReadOnlyList<string> Screenshots,
    string? StoreUrl, bool IsFree, string Source, bool ComingSoon = false);

// One row of a Game list chart (enough to draw the cover; details load when opened).
public sealed record CatalogItem(string AppId, string Name, string Cover, string Wide, int? PositivePercent = null, long? Reviews = null, long? Players = null, string? Note = null, int? Metacritic = null, bool? Free = null);
public sealed record CatalogChart(string Id, IReadOnlyList<CatalogItem> Items, DateTimeOffset FetchedAt);

public interface IGameCatalog
{
    // Installed game → details (Steam id direct, GOG id via GOG, anything else by name on the Steam store).
    Task<GameInfo?> DetailsAsync(string store, string id, string name, CancellationToken token);
    Task<GameInfo?> SteamDetailsAsync(string appId, CancellationToken token);
    // Charts: "trending" (most played 2 weeks), "toprated", "topsellers", "new", "upcoming". Cached; maxAge decides refresh.
    Task<CatalogChart?> ChartAsync(string id, TimeSpan maxAge, CancellationToken token);
    // Any PC game on the Steam store by name (results carry the Metacritic score when Steam has it).
    Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, CancellationToken token);
}

public static class CatalogText
{
    public static readonly string[] Charts = ["trending", "toprated", "topsellers", "new", "upcoming"];
    // Steam portrait and wide art for an app id (served by Steam's CDN).
    public static string Portrait(string appId) => $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900.jpg";
    public static string Header(string appId) => $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg";
    public static string Hero(string appId) => $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/library_hero.jpg";
    public static string Store(string appId) => $"https://store.steampowered.com/app/{appId}/";

    // Store text is HTML; keep plain readable text.
    public static string Plain(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var s = System.Text.RegularExpressions.Regex.Replace(html, @"<br\s*/?>|</p>|</li>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", "");
        s = System.Net.WebUtility.HtmlDecode(s);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[ \t]+", " ");
        return System.Text.RegularExpressions.Regex.Replace(s, @"\n\s*\n+", "\n\n").Trim();
    }
}

// Difficulty has no official source: Lotus estimates it from what Steam players tag the game with, and says so.
public static class GameDifficulty
{
    private static readonly string[] Brutal = ["Souls-like", "Masocore"];
    private static readonly string[] Hard = ["Difficult", "Precision Platformer", "Bullet Hell", "Roguelike", "Permadeath", "Hardcore"];
    private static readonly string[] Easy = ["Casual", "Relaxing", "Cozy", "Family Friendly", "Cute", "Wholesome", "Point & Click", "Visual Novel", "Walking Simulator"];

    public static Difficulty Estimate(IReadOnlyDictionary<string, long> tags)
    {
        if (tags.Count == 0) return Difficulty.Unknown;
        var top = tags.OrderByDescending(t => t.Value).Take(15).ToList();
        var lead = Math.Max(1, top[0].Value);
        bool Strong(string[] names, double share) => top.Any(t => names.Contains(t.Key, StringComparer.OrdinalIgnoreCase) && t.Value >= lead * share);
        var brutal = Strong(Brutal, 0.25); var hard = Strong(Hard, 0.25); var easy = Strong(Easy, 0.25);
        if (brutal && hard) return Difficulty.VeryHard;
        if (brutal || hard) return Difficulty.Hard;
        if (easy) return Difficulty.Easy;
        return Difficulty.Medium;
    }
}
