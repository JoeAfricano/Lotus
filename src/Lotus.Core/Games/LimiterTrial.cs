using Lotus.Core.Benchmark;

namespace Lotus.Core.Games;

// ---- RivaTuner limiter chosen per game, by measurement (study of 25-28/11/2026) ----
// No single limiter suits every game: Reflex at 60 Hz in Stellar Blade repeated 4,514 frames in 5 min while the front edge
// was clean; in Wukong (75 Hz) and Gothic (60 Hz) Reflex was the best (the front edge had the same line with +23 ms in
// Gothic). With Modo Console on, Lotus measures each candidate once, quietly, during play (RivaTuner's own counters, no
// administrator prompt), and after both offers the one with fewer repeated frames, then lower latency.
public sealed record LimiterTrialResult(string Mode, double OffBeatPerMinute, double? LatencyMs, double? LatencyP95Ms, int Frames, DateTimeOffset When);

// "Linha reta" (default): RivaTuner limits just under the screen's rate. "Menor latência": the game's own limiter at the
// screen's rate and RivaTuner without a limit (Gothic at 60 Hz: 36 ms instead of 46, same p99, ~22 doubled frames in 3 min).
public static class PacingPresets
{
    public const string Straight = "straight", LowLatency = "latency";
    public const string GameLimiter = "game"; // AppliedLimiter of the low-latency preset: no RivaTuner cap
}

public static class LimiterTrial
{
    public const string Reflex = "responsive", FrontEdge = "fluid";
    public static readonly TimeSpan MeasureFor = TimeSpan.FromSeconds(60), SettleFor = TimeSpan.FromSeconds(90);
    public const double ClearlyFewerPerMinute = 5, ClearlyFewerShare = 0.3;

    // Frames out of the screen's beat, per minute, from the time between frames (what RivaTuner sees, no administrator):
    // at a fixed refresh with Modo Console every frame should take one refresh period. A frame 25% away from it is off the
    // beat; one that took two or more periods also counts the refreshes it made the screen repeat. This is an estimate of
    // the repeated frames PresentMon counts on screen, good for comparing two limiters in the same game.
    public static double OffBeatPerMinute(IReadOnlyList<FrameSample> frames, double refreshHz)
    {
        if (frames.Count < 2 || refreshHz <= 0) return 0;
        var period = 1000 / refreshHz; var repeats = 0;
        foreach (var f in frames)
        {
            if (f.Milliseconds > period * 1.5) repeats += (int)Math.Round(f.Milliseconds / period) - 1;
            else if (f.Milliseconds > period * 1.25 || f.Milliseconds < period * 0.75) repeats++;
        }
        var minutes = (frames[^1].TimeSeconds - frames[0].TimeSeconds + frames[0].Milliseconds / 1000) / 60;
        return minutes <= 0 ? 0 : repeats / minutes;
    }

    // The candidates for this game: Reflex only where it works (NVIDIA, Direct3D 11/12).
    public static IReadOnlyList<string> Candidates(bool reflexAvailable) => reflexAvailable ? [Reflex, FrontEdge] : [FrontEdge];

    // Next mode to measure (the one in use first), or null when every candidate has a result.
    public static string? Next(string current, IReadOnlyDictionary<string, LimiterTrialResult>? done, bool reflexAvailable)
    {
        var candidates = Candidates(reflexAvailable);
        if (!candidates.Contains(current) && current != PacingPresets.GameLimiter) return null; // a mode the player picked by hand: not a trial
        if (done?.ContainsKey(current) != true) return current;
        return candidates.FirstOrDefault(c => done?.ContainsKey(c) != true);
    }

    // Fewer repeated frames when clearly fewer, otherwise lower latency. Above the player's latency ceiling a result is
    // never offered (null when none fits).
    public static LimiterTrialResult? Pick(IEnumerable<LimiterTrialResult> results, int ceilingMs = 0)
    {
        var fit = results.Where(r => ceilingMs <= 0 || r.LatencyMs is not { } l || l <= ceilingMs).ToList();
        if (fit.Count == 0) return null;
        return fit.Aggregate((best, r) =>
        {
            var gap = Math.Abs(best.OffBeatPerMinute - r.OffBeatPerMinute);
            var clear = gap > ClearlyFewerPerMinute && gap > ClearlyFewerShare * Math.Max(best.OffBeatPerMinute, r.OffBeatPerMinute);
            if (clear) return r.OffBeatPerMinute < best.OffBeatPerMinute ? r : best;
            if (best.LatencyMs is { } a && r.LatencyMs is { } b) return b < a ? r : best;
            return r.OffBeatPerMinute < best.OffBeatPerMinute ? r : best;
        });
    }

    // Median and 95th percentile of per-frame latencies (null below 10 samples: not enough to say).
    public static (double? Median, double? P95) Latency(IReadOnlyList<double> values)
    {
        var v = values.Where(x => double.IsFinite(x) && x > 0).Order().ToArray();
        if (v.Length < 10) return (null, null);
        return (v[v.Length / 2], v[Math.Min(v.Length - 1, (int)Math.Ceiling(v.Length * 0.95) - 1)]);
    }
}
