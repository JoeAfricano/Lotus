using System.Text.RegularExpressions;

namespace Lotus.Core.Games;

// Shader pre-compilation is delegated to SCSKiller (GPL-3.0, separate program) through its documented CLI.
// Lotus never links, copies or bundles its code: it only runs "scskiller status" and "scskiller compile <id>".
public enum ShaderState { Ready, Stale, Compiled, Unsupported, Other }
public sealed record ShaderStatus(string Id, string Name, ShaderState State, string RawState, string? CacheSize, string? Detail);
// Stage: Waiting, Indexing, Planning, Materializing, Warming, Paused, Done, Failed, Stopped (SCSKiller QueueStage).
public sealed record ShaderProgress(string Id, string Stage, int Done, int Total, double PerSecond)
{
    public double? Fraction => Total > 0 ? Math.Clamp((double)Done / Total, 0, 1) : null;
    // Time left from the current rate; only while warming with a known rate.
    public TimeSpan? Remaining => Stage == "Warming" && PerSecond > 0 && Total > Done ? TimeSpan.FromSeconds((Total - Done) / PerSecond) : null;
}

public interface IShaderPrecompiler
{
    bool IsAvailable { get; }
    Task<IReadOnlyList<ShaderStatus>> StatusAsync(CancellationToken token);
    Task<int> CompileAsync(string id, IProgress<string> progress, CancellationToken token); // returns the CLI exit code
    bool OpenApp(); // opens the user's SCSKiller window (optional, to see details); false when not installed
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

    // Compile progress line (SCSKiller CLI): "10:06:56 steam:1593500 Warming 625/25351 (0 failed) 412/s[, ...][ - note]".
    // Indexing reports "done/total packages" without a rate. Lines without counts (Waiting, Planning, Done...) give Done = Total = 0.
    [GeneratedRegex(@"^\d{1,2}:\d{2}:\d{2}\s+(?<id>[a-z]+:\S+)\s+(?<stage>[A-Za-z]+)(?:\s+(?<done>\d+)/(?<total>\d+)(?:\s+packages|\s+\([^)]*\)\s+(?<rate>\d+(?:[.,]\d+)?)/s)?)?")]
    private static partial Regex ProgressLine();

    public static ShaderProgress? ParseProgress(string line)
    {
        var m = ProgressLine().Match(line);
        if (!m.Success) return null;
        int.TryParse(m.Groups["done"].Value, out var done); int.TryParse(m.Groups["total"].Value, out var total);
        double.TryParse(m.Groups["rate"].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rate);
        return new(m.Groups["id"].Value, m.Groups["stage"].Value, done, total, rate);
    }

    // Steam ids map directly; other stores fall back to an exact, case-insensitive name match.
    public static ShaderStatus? Match(IReadOnlyList<ShaderStatus> statuses, GameEntry game) =>
        statuses.FirstOrDefault(s => game.Store == GameStores.Steam && s.Id == "steam:" + game.Id)
        ?? statuses.FirstOrDefault(s => string.Equals(s.Name, game.Name, StringComparison.OrdinalIgnoreCase));
}
