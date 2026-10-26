using System.Globalization;
using System.Text.Json;
using Lotus.Core.Achievements;

namespace Lotus.Infrastructure.Games;

// Global unlock rates from Steam's public stats API (no key needed). Kept for a week in %LOCALAPPDATA%\Lotus\Catalog;
// offline, the last saved rates are used.
public sealed class SteamRarityClient(HttpClient? http = null, string? cacheDir = null) : IAchievementRarity
{
    private static readonly TimeSpan Fresh = TimeSpan.FromDays(7);
    private readonly HttpClient http = http ?? Default();
    private readonly string cache = cacheDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "Catalog");

    public async Task<IReadOnlyDictionary<string, double>?> GetAsync(string appId, CancellationToken token)
    {
        if (!appId.All(char.IsDigit)) return null;
        var file = Path.Combine(cache, $"rarity-{appId}.json");
        var saved = Load(file);
        if (saved is not null && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < Fresh) return saved;
        try
        {
            var json = await http.GetStringAsync($"https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v0002/?gameid={appId}&format=json", token);
            var rates = Parse(json);
            if (rates.Count > 0)
            {
                Directory.CreateDirectory(cache);
                await File.WriteAllTextAsync(file, JsonSerializer.Serialize(rates, RarityJson.Default.DictionaryStringDouble), token);
                return rates;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException && !token.IsCancellationRequested) { }
        return saved;
    }

    public static Dictionary<string, double> Parse(string json)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("achievementpercentages", out var root) || !root.TryGetProperty("achievements", out var list) || list.ValueKind != JsonValueKind.Array) return result;
        foreach (var a in list.EnumerateArray())
        {
            if (!a.TryGetProperty("name", out var n) || n.GetString() is not { } name || !a.TryGetProperty("percent", out var p)) continue;
            double? value = p.ValueKind switch
            {
                JsonValueKind.Number => p.GetDouble(),
                JsonValueKind.String when double.TryParse(p.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
                _ => null,
            };
            if (value is { } v) result[name] = Math.Round(v, 2);
        }
        return result;
    }

    private static Dictionary<string, double>? Load(string file)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize(File.ReadAllText(file), RarityJson.Default.DictionaryStringDouble) : null; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }
    private static HttpClient Default()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Lotus/1.0 (+desktop game helper)");
        return c;
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, double>))]
internal sealed partial class RarityJson : System.Text.Json.Serialization.JsonSerializerContext { }
