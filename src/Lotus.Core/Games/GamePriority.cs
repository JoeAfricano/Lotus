namespace Lotus.Core.Games;

// While a library game is open (only when the player turns it on in Settings): the game runs above normal priority and
// known background apps (browsers, launchers, sync) below normal. Everything goes back as it was when the game closes
// or Lotus exits. Never realtime/high, never system processes, never voice chat (Discord stays as it is).
public interface IGamePriority
{
    // Called on every check while the game runs (new browser tabs start new processes). Returns how many were changed.
    int Apply(RunningGame game);
    // Puts back every priority Lotus changed that is still the one Lotus set.
    int Restore();
}

public static class GamePriorityRules
{
    public static readonly string[] Background =
    [
        "chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "vivaldi", "msedgewebview2",
        "steamwebhelper", "EpicGamesLauncher", "EpicWebHelper", "GalaxyClient", "GalaxyClient Helper", "EADesktop", "EABackgroundService",
        "upc", "UbisoftConnect", "Battle.net", "RiotClientServices", "XboxPcApp",
        "OneDrive", "Dropbox", "GoogleDriveFS", "Teams", "ms-teams", "Spotify",
    ];
    public static bool IsBackground(string processName) => Background.Contains(processName, StringComparer.OrdinalIgnoreCase);
}
