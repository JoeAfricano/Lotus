using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Lotus.Core.Games;

namespace Lotus.Infrastructure.Games;

// Reads the game's install folder (names and sizes only, never contents) to find upscaler / frame generation libraries,
// the engine and the main executable. Bounded: 5 folder levels and 40 000 entries, so even huge games scan quickly.
public sealed partial class GameTechScanner : IGameTechScanner
{
    private readonly ConcurrentDictionary<string, GameTech> cache = new();
    // Executables that are never the game itself (installers, crash reporters, anti-cheat, redistributables, launchers).
    [GeneratedRegex(@"unins|setup|install|redist|vc_?redist|dxsetup|crash|report|easyanticheat|battleye|eac|launcher|helper|update|prereq|dotnet|ue4prereq|cefprocess|webhelper", RegexOptions.IgnoreCase)]
    private static partial Regex NotTheGame();

    public GameTech Scan(GameEntry game) => game.InstallDir is { Length: > 3 } dir && Directory.Exists(dir)
        ? cache.GetOrAdd(game.Key, _ => Read(dir, game.ExePath))
        : GameTech.Unknown;

    private static GameTech Read(string root, string? knownExe)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 5, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var names = new List<string>(); string? exe = null; long exeSize = -1; var seen = 0;
        foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", options))
        {
            if (++seen > 40_000) break;
            names.Add(file.Name);
            if (file.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) && !NotTheGame().IsMatch(file.Name) && file.Length > exeSize) { exe = file.Name; exeSize = file.Length; }
        }
        // Unreal games ship a small launcher next to the real "*-Win64-Shipping.exe": prefer the shipping binary.
        var shipping = names.FirstOrDefault(n => n.EndsWith("-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase) || n.EndsWith("-WinGDK-Shipping.exe", StringComparison.OrdinalIgnoreCase));
        var main = shipping ?? (knownExe is { Length: > 0 } k && !NotTheGame().IsMatch(Path.GetFileName(k)) ? Path.GetFileName(k) : exe);
        return GameTech.FromFiles(names) with { MainExe = main };
    }
}
