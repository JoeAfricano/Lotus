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
    // Games played through the sibling app (diary only; they are not scanned as part of the Lotus library).
    public const string LotusPorts = "Lotus Ports";
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
    // The player's own name and cover for a game (any store). The cover file is copied into Lotus's folder.
    IReadOnlyDictionary<string, GameCustom> LoadCustom() => new Dictionary<string, GameCustom>();
    void SaveCustom(IReadOnlyDictionary<string, GameCustom> custom) { }
    string? ImportCover(string gameKey, string sourceFile) => null;
    void DeleteCover(string? coverPath) { }
}
public sealed record GameCustom(string? Name, string? Cover);
public interface IGameLauncher { void Launch(GameEntry game); }