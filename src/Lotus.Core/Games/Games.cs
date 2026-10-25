namespace Lotus.Core.Games;

// A game found in a launcher or added by the user. Detection is read-only; launching uses only
// known launcher URI schemes or an existing .exe (validated by IGameLauncher).
public sealed record GameEntry(string Store, string Id, string Name, string? InstallDir = null, string? ExePath = null,
    string? LaunchUri = null, string? Args = null, string? CoverPath = null, DateTimeOffset? LastPlayed = null)
{
    public string Key => Store + ":" + Id;
    public bool IsManual => Store == GameStores.Manual;
    public bool CanLaunch => LaunchUri is not null || ExePath is not null;
}

public static class GameStores
{
    public const string Steam = "Steam", Epic = "Epic Games", Xbox = "Xbox", Gog = "GOG", Ea = "EA app", Ubisoft = "Ubisoft Connect", Manual = "Manual";
    public static readonly string[] All = [Steam, Epic, Xbox, Gog, Ea, Ubisoft, Manual];
}

public sealed record LibraryScan(IReadOnlyList<GameEntry> Games, IReadOnlyList<string> Errors);
public interface IGameLibrary { LibraryScan Scan(IReadOnlyList<GameEntry> manual); }
public interface IManualGameStore
{
    IReadOnlyList<GameEntry> Load();
    void Save(IReadOnlyList<GameEntry> games);
    // Keys of launcher games the user chose to hide (for example apps that Steam lists as games).
    IReadOnlyList<string> LoadHidden() => [];
    void SaveHidden(IReadOnlyList<string> keys) { }
}
public interface IGameLauncher { void Launch(GameEntry game); }