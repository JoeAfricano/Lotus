namespace Lotus.Core.Achievements;

// One Steam achievement as Steam keeps it on this PC (name in the player's language, icons, unlock time).
public sealed record SteamAchievement(string ApiName, string Name, string Description, bool Hidden, string? Icon, string? IconGray, bool Unlocked, DateTimeOffset? UnlockedAt)
{
    public double? Percent { get; init; } // share of all players who have it; null until known
}

public sealed record SteamGameAchievements(string AppId, string Name, IReadOnlyList<SteamAchievement> Items)
{
    public int Unlocked => Items.Count(a => a.Unlocked);
    public int Total => Items.Count;
    public double Fraction => Total == 0 ? 0 : (double)Unlocked / Total;
    public bool Perfect => Total > 0 && Unlocked == Total;
    public DateTimeOffset? LastUnlock => Items.Max(a => a.UnlockedAt);
    public DateTimeOffset? FirstUnlock => Items.Min(a => a.UnlockedAt);
    public static string IconUrl(string appId, string? icon) => icon is { Length: > 0 } ? $"https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/{appId}/{icon}" : "";
    public static string HeaderUrl(string appId) => $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg";
}

public interface ISteamAchievementSource { IReadOnlyList<SteamGameAchievements> Read(); }
// Global unlock rates per achievement (API name → percent), or null when unavailable.
public interface IAchievementRarity { Task<IReadOnlyDictionary<string, double>?> GetAsync(string appId, CancellationToken token); }

public static class Rarity
{
    // Rarity bands used for colours, labels and XP.
    public static string Kind(double? percent) => percent switch { null => "unknown", < 1 => "legendary", < 5 => "epic", < 20 => "rare", < 50 => "uncommon", _ => "common" };

    // XP for a Steam achievement: rarer is worth more. A 100% game adds a bonus.
    public static int Xp(SteamAchievement a) => !a.Unlocked ? 0 : Kind(a.Percent) switch
    {
        "legendary" => 15, "epic" => 8, "rare" => 4, "uncommon" => 2, "common" => 1, _ => 2,
    };
    public const int PerfectBonus = 50;
    public static int Xp(IEnumerable<SteamGameAchievements> games) => games.Sum(g => g.Items.Sum(Xp) + (g.Perfect ? PerfectBonus : 0));
}
