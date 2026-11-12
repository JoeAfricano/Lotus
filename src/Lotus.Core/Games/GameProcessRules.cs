namespace Lotus.Core.Games;

// "Is a game open?" must not stay true forever because of an app that is always running (Wallpaper Engine lives in
// steamapps\common like a game) or a library entry whose folder is a whole library or drive. Both are skipped by the
// background check that pauses Lotus's own work while a game runs.
public static class GameProcessRules
{
    private static readonly string[] AlwaysOnFolders = [@"\steamapps\common\wallpaper_engine\", @"\steamapps\common\steamworks shared\", @"\steamapps\common\steamvr\"];
    private static readonly string[] AlwaysOnFiles = ["wallpaper32.exe", "wallpaper64.exe", "steam.exe", "steamwebhelper.exe", "steamservice.exe", "epicgameslauncher.exe", "epicwebhelper.exe", "galaxyclient.exe", "eadesktop.exe", "ubisoftconnect.exe", "upc.exe"];
    private static readonly string[] BroadEnds = [@"\steamapps\common\", @"\steamapps\", @"\steam\", @"\program files\", @"\program files (x86)\", @"\epic games\", @"\gog galaxy\games\", @"\xboxgames\", @"\windowsapps\"];

    public static bool AlwaysOn(string imagePath)
    {
        if (string.IsNullOrEmpty(imagePath)) return false;
        var path = imagePath.Replace('/', '\\');
        var file = path[(path.LastIndexOf('\\') + 1)..];
        return AlwaysOnFiles.Contains(file, StringComparer.OrdinalIgnoreCase)
            || AlwaysOnFolders.Any(f => path.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    // dir ends with "\"; a drive root, a whole library or a user's home folder is not one game's folder.
    public static bool TooBroad(string dir)
    {
        var d = dir.Replace('/', '\\');
        if (!d.EndsWith('\\')) d += "\\";
        if (d.Length <= 3) return true;
        if (BroadEnds.Any(e => d.EndsWith(e, StringComparison.OrdinalIgnoreCase))) return true;
        var parts = d.TrimEnd('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 3 && parts.Length >= 2 && parts[1].Equals("Users", StringComparison.OrdinalIgnoreCase); // C:\Users or C:\Users\<name>
    }
}
