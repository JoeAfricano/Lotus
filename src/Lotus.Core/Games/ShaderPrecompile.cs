using System.Text.RegularExpressions;

namespace Lotus.Core.Games;

// Shader pre-compilation is delegated to SCSKiller (GPL-3.0, separate program) through its documented CLI.
// Lotus never links, copies or bundles its code: it only runs "scskiller status" and "scskiller compile <id>".
public enum ShaderState { Ready, Stale, Compiled, Unsupported, Other }
public sealed record ShaderStatus(string Id, string Name, ShaderState State, string RawState, string? CacheSize, string? Detail);

public interface IShaderPrecompiler
{
    bool IsAvailable { get; }
    Task<IReadOnlyList<ShaderStatus>> StatusAsync(CancellationToken token);
    Task<int> CompileAsync(string id, IProgress<string> progress, CancellationToken token); // returns the CLI exit code
}

public static partial class ScsKillerOutput
{
    // "Stale   steam:1593500   God of War  [cache 512 MB]  - driver changed: 617.42 -> 621.01"; indented lines are details.
    [GeneratedRegex(@"^(?<state>\S+)\s+(?<id>[a-z]+:\S+)\s+(?<name>.+?)(?:\s+\[cache (?<cache>[^\]]+)\])?(?:\s+-\s+(?<detail>.+))?\s*$")]
    private static partial Regex Line();

    public static IReadOnlyList<ShaderStatus> ParseStatus(string text)
    {
        var list = new List<ShaderStatus>();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.Length == 0 || char.IsWhiteSpace(line[0])) continue;
            var m = Line().Match(line);
            if (!m.Success) continue;
            var raw = m.Groups["state"].Value;
            var state = raw.ToLowerInvariant() switch
            {
                "ready" => ShaderState.Ready, "stale" => ShaderState.Stale, "unsupported" => ShaderState.Unsupported,
                "compiled" or "warm" or "done" or "up-to-date" or "current" => ShaderState.Compiled, _ => ShaderState.Other
            };
            list.Add(new(m.Groups["id"].Value, m.Groups["name"].Value.Trim(), state, raw,
                m.Groups["cache"].Success ? m.Groups["cache"].Value : null, m.Groups["detail"].Success ? m.Groups["detail"].Value.Trim() : null));
        }
        return list;
    }

    // Steam ids map directly; other stores fall back to an exact, case-insensitive name match.
    public static ShaderStatus? Match(IReadOnlyList<ShaderStatus> statuses, GameEntry game) =>
        statuses.FirstOrDefault(s => game.Store == GameStores.Steam && s.Id == "steam:" + game.Id)
        ?? statuses.FirstOrDefault(s => string.Equals(s.Name, game.Name, StringComparison.OrdinalIgnoreCase));
}
