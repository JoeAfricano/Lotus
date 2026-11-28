namespace Lotus.Core.Games;

public enum ProcessRole { Game, Helper, CrashReporter }

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

    // ---- Which process in the game's folder is the game (Gothic 1 Remake, 28/11/2026) ----
    // After a crash, Unreal's error window (Engine\Binaries\Win64\CrashReportClient.exe) stayed open hidden for 2.5 h and Lotus
    // counted it as the game: "Jogando agora", Perfil Jogo and the card tuning stayed on and the diary got a 151-minute session.
    // Helpers living in the game folder (web views, crash handlers, anti-cheat bootstrappers) never count as the game; a crash
    // reporter alone means "the game crashed". Small launcher stubs (G1R-Win64-Shipping.exe, 300 KB, at the root, only starts
    // G1R\Binaries\Win64\G1R-Win64-Shipping.exe) do not count either: the real executable is what keeps the session.
    private static readonly string[] CrashReporterFiles = ["crashreportclient.exe", "crashreportclienteditor.exe", "unitycrashhandler64.exe", "unitycrashhandler32.exe",
        "crashpad_handler.exe", "crashreporter.exe", "crashhandler.exe", "crashsender.exe", "crashsender1403.exe", "bugsplat.exe", "bssndrpt.exe"];
    private static readonly string[] HelperFiles = ["unrealcefsubprocess.exe", "epicwebhelper.exe", "cefsharp.browsersubprocess.exe", "qtwebengineprocess.exe",
        "easyanticheat.exe", "easyanticheat_eos_setup.exe", "start_protected_game.exe", "battleye.exe", "beservice.exe", "unins000.exe", "dxsetup.exe", "vc_redist.x64.exe"];
    public const long StubBytes = 2L << 20; // a launcher stub is tiny next to the real game executable

    public static ProcessRole Role(string imagePath, Func<string, bool>? isStub = null)
    {
        if (string.IsNullOrEmpty(imagePath)) return ProcessRole.Helper;
        var path = imagePath.Replace('/', '\\');
        var file = path[(path.LastIndexOf('\\') + 1)..];
        if (CrashReporterFiles.Contains(file, StringComparer.OrdinalIgnoreCase)) return ProcessRole.CrashReporter;
        if (HelperFiles.Contains(file, StringComparer.OrdinalIgnoreCase)) return ProcessRole.Helper;
        return isStub?.Invoke(path) == true ? ProcessRole.Helper : ProcessRole.Game;
    }

    // Unreal's launcher stub: a small exe whose name also exists, much bigger, under <root>\<project>\Binaries\Win64\.
    // exeSizes: size of every exe with this file name inside the game folder (the stub's own included).
    public static bool IsLauncherStub(string path, long size, IEnumerable<(string Path, long Size)> sameName) =>
        size is > 0 and < StubBytes && sameName.Any(o => !string.Equals(o.Path, path, StringComparison.OrdinalIgnoreCase) && o.Size > size * 4
            && o.Path.Contains(@"\Binaries\", StringComparison.OrdinalIgnoreCase));

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
