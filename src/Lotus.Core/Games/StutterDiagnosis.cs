namespace Lotus.Core.Games;

// "Por que está travando?": turns one measurement of the running game plus the PC's state into likely causes,
// ranked, each with one fix the player can apply. Read-only: it never changes anything by itself.
public enum StutterFix { None, Precompile, PrepareProfile, LowerQuality, CloseApps }
public sealed record StutterFinding(string Key, StutterFix Fix, int Weight, string? Detail = null);
public sealed record StutterInputs(double AverageFps, double Low1Fps, int SpikeCount, double DurationSeconds, double? GpuBusy, int TargetFps,
    ShaderState? Shaders, int PendingProfileSteps, double? MemoryLoadPercent, IReadOnlyList<string> TopMemoryApps, bool CanLowerQuality, IReadOnlyList<string>? Overlays = null, CpuHeat? Heat = null,
    double? FrameCap = null, // a frame limiter in force (RivaTuner), when known
    double RealRefreshHz = 0, bool AdaptiveSync = false, // the monitor's real rate (e.g. 74.973) and whether FreeSync/G-Sync is on
    HitchBreakdown? Hitches = null, // likely cause of each frame above 50 ms (see HitchCauses); Detail is filled in by the caller
    double? GpuFrameMs = null, bool ConsoleMode = false); // the card's own time per frame (Reflex) and whether Modo Console is set for this game

public static class StutterDiagnosis
{
    public static IReadOnlyList<StutterFinding> Analyze(StutterInputs i)
    {
        var list = new List<StutterFinding>();
        var stutters = i.Low1Fps < i.AverageFps * 0.6 || (i.DurationSeconds > 0 && i.SpikeCount / i.DurationSeconds > 0.5);
        var lowFps = i.AverageFps < i.TargetFps * 0.9;
        // FPS held at a limiter's cap is the limiter, not the hardware: say so instead of blaming the CPU or the GPU.
        if (lowFps && i.FrameCap is { } cap and > 0 && i.AverageFps >= cap * 0.97)
        {
            list.Add(new("WhyCapped", StutterFix.None, 65, Math.Round(cap, 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            lowFps = false;
        }
        // Without FreeSync/G-Sync, a limit above the monitor's real rate (75 on a 74.973 Hz monitor) lets frames queue
        // behind V-Sync: more delay and an uneven line, even with FPS at the top (measured on Wukong, 12/11/2026).
        if (!i.AdaptiveSync && i.FrameCap is { } limit && FramePacing.AboveRefresh(limit, i.RealRefreshHz))
            list.Add(new("WhyCapAboveRefresh", StutterFix.None, stutters ? 58 : 40,
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{limit:0.##} FPS / {i.RealRefreshHz:0.###} Hz")));
        // A limit in the game below the monitor's rate, without FreeSync/G-Sync and without Modo Console: Wukong at 60 FPS on a
        // 75 Hz screen repeated ~2,000 frames in 2 min (75% in rhythm). Two measured ways out: no limit in the game + RivaTuner
        // just under 75 (99.8% in rhythm, 75 FPS) when the card holds it, or Modo Console at 60 Hz. 75 Hz needs under ~13 ms
        // per frame; with the card's time known the advice names one of them, otherwise both.
        if (GameCapBelowRefresh(i) is { } capped)
        {
            var key = i.GpuFrameMs is { } gpuMs ? (gpuMs < FastEnoughMs(i.RealRefreshHz) ? "WhyGameCapUncap" : "WhyGameCapConsole") : "WhyGameCapBoth";
            list.Add(new(key, StutterFix.None, 62, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{capped:0} FPS / {i.RealRefreshHz:0.###} Hz")));
            lowFps = false; // the game's own limit, not the processor or the card
        }        // Where the hitches came from, when there were enough to show a pattern. Memory as the main cause offers closing apps.
        if (i.Hitches is { Total: >= HitchCauses.MinHitches } h)
            list.Add(new("WhyHitch" + h.Main, h.Main == HitchCause.Memory && i.TopMemoryApps.Count > 0 ? StutterFix.CloseApps : StutterFix.None, 85));
        // Sudden drops: shader compilation is the classic cause in modern DirectX 12 games.
        if (stutters && i.Shaders is ShaderState.Ready or ShaderState.Stale) list.Add(new("WhyShaders", StutterFix.Precompile, 90));
        if (i.MemoryLoadPercent is >= 85) list.Add(new("WhyMemory", StutterFix.CloseApps, stutters ? 80 : 50, string.Join(", ", i.TopMemoryApps.Take(3))));
        if (i.PendingProfileSteps > 0) list.Add(new("WhyProfile", StutterFix.PrepareProfile, stutters || lowFps ? 60 : 30));
        // The processor slowing itself down (heat or power limit): a cause even when the graphics card looks free.
        if ((stutters || lowFps) && i.Heat is { } heat && (heat.Limited || heat.Hot))
            list.Add(heat.MaxZoneCelsius is { } c and >= CpuHeat.HotZone - 15
                ? new("WhyCpuHotTemp", StutterFix.None, 75, Math.Round(c).ToString(System.Globalization.CultureInfo.InvariantCulture))
                : new("WhyCpuHot", StutterFix.None, 75));
        if (lowFps && i.GpuBusy is >= 90) list.Add(new("WhyGpuLimit", i.CanLowerQuality ? StutterFix.LowerQuality : StutterFix.None, 70));
        else if (lowFps && i.GpuBusy is < 70 && !list.Any(f => f.Key.StartsWith("WhyCpuHot", StringComparison.Ordinal))) list.Add(new("WhyCpuLimit", i.TopMemoryApps.Count > 0 ? StutterFix.CloseApps : StutterFix.None, 55));
        // Heavy overlays and capture apps running (Discord with hardware acceleration, Overwolf, animated wallpapers...).
        if (stutters && i.Overlays is { Count: > 0 } o) list.Add(new("WhyOverlays", StutterFix.None, 45, string.Join(", ", o.Take(4))));
        if (stutters && list.Count == 0) list.Add(new("WhyUnknownStutter", StutterFix.None, 20));
        if (list.Count == 0) list.Add(new("WhyAllGood", StutterFix.None, 0));
        return [.. list.OrderByDescending(f => f.Weight)];
    }

    // Frames held steady clearly below the monitor's real rate, by the game itself (not a RivaTuner cap): the FPS it holds.
    public static double? GameCapBelowRefresh(StutterInputs i)
    {
        if (i.AdaptiveSync || i.ConsoleMode || i.RealRefreshHz < 50 || i.AverageFps < 25) return null;
        if (i.AverageFps > i.RealRefreshHz * 0.9) return null;                       // at the screen's rate (or close): fine
        if (i.FrameCap is { } cap && cap < i.RealRefreshHz * 0.98) return null;        // a RivaTuner cap below the rate is Lotus's or the player's own plan
        if (i.Low1Fps < i.AverageFps * 0.85) return null;                            // not steady: a hardware limit, not a cap
        return i.AverageFps;
    }
    // Time per frame the screen's rate allows, with a little margin (74.973 Hz: 13.3 ms -> ~13 ms).
    public static double FastEnoughMs(double refreshHz) => 1000 / refreshHz - 0.3;
}

// System memory load and the heaviest programs (names only), read without changing anything.
public interface IMemoryPressure { (double LoadPercent, IReadOnlyList<string> TopApps) Read(GameEntry? exclude); }
