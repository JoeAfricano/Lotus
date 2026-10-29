using System.Collections.Concurrent;
using Lotus.Core.Games;

namespace Lotus.Infrastructure.Games;

// Reads the game's install folder (names only, never contents) to find upscaler / frame generation libraries and the
// engine. Bounded: 5 folder levels and 40 000 entries, so even huge games are scanned in well under a second.
public sealed class GameTechScanner : IGameTechScanner
{
    private readonly ConcurrentDictionary<string, GameTech> cache = new();

    public GameTech Scan(GameEntry game) => game.InstallDir is { Length: > 3 } dir && Directory.Exists(dir)
        ? cache.GetOrAdd(game.Key, _ => GameTech.FromFiles(Names(dir)))
        : GameTech.Unknown;

    private static IEnumerable<string> Names(string root)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 5, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var seen = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*", options))
        {
            if (++seen > 40_000) yield break;
            yield return Path.GetFileName(path);
        }
    }
}
