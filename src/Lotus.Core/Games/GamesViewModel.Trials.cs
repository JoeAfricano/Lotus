namespace Lotus.Core.Games;

// Modo Console per game: which RivaTuner limiter (measured, see LimiterTrial) and which preset ("linha reta" or "menor
// latência"). Measuring is quiet and read-only (RivaTuner's counters, no administrator prompt); changing the limiter only
// happens with the game closed and the player's click, through the same RivaTuner profile writes as Save.
public sealed partial class GamesViewModel
{
    // The player's latency ceiling in ms (Settings), 0 = none: a preset or limiter above it is never suggested.
    public Func<int>? LatencyCeiling { get; set; }
    public int CeilingMs => Math.Max(0, LatencyCeiling?.Invoke() ?? 0);

    private bool ReflexFor(GameEntry game, string exe)
    {
        var gpu = profileSource?.Read().GpuName ?? "";
        var nvidia = System.Text.RegularExpressions.Regex.IsMatch(gpu, "NVIDIA|GeForce|RTX|GTX", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return PresentationPlan.ReflexAvailable(nvidia, Rtss?.GameApi(exe));
    }

    // The limiter this game still needs measured with Modo Console on, or null (nothing to measure, or a mode set by hand).
    public string? TrialPending(GameEntry game)
    {
        var prefs = PrefsFor(game); var p = prefs.Presentation;
        if (!p.Enabled || p.Exe.Length == 0 || prefs.UseFrameGen) return null; // frame generation: only Reflex fits, nothing to compare
        var next = LimiterTrial.Next(p.AppliedLimiter, prefs.LimiterTrials, ReflexFor(game, p.Exe));
        return next == p.AppliedLimiter ? next : null; // only the mode in use can be measured now
    }

    // ~60 s of the running game with the current limiter; saved per mode. Never changes anything.
    public async Task<LimiterTrialResult?> MeasureTrialAsync(GameEntry game, double refreshHz, CancellationToken token = default)
    {
        if (capture is null || processCheck?.Find(game) is not { } run || TrialPending(game) is not { } mode) return null;
        try
        {
            var session = await capture.CaptureAsync(new(new(run.Pid, run.Executable, run.StartedUtc), (int)LimiterTrial.MeasureFor.TotalSeconds, Elevated: false,
                "Lotus limiter trial: " + game.Name, "{}"), token);
            if (session.Data?.Frames is not { Count: >= 600 } frames) return null; // too little to compare (RivaTuner pipe needed)
            var result = new LimiterTrialResult(mode, LimiterTrial.OffBeatPerMinute(frames, refreshHz), session.Extras?.PcLatencyMs, session.Extras?.PcLatencyP95Ms, frames.Count, DateTimeOffset.Now);
            var prefs = PrefsFor(game);
            var trials = new Dictionary<string, LimiterTrialResult>(prefs.LimiterTrials ?? [], StringComparer.OrdinalIgnoreCase) { [mode] = result };
            PlayPrefs?.Set(game.Key, prefs with { LimiterTrials = trials });
            return result;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex) { log(ex); return null; }
    }

    // What to tell the player after a session: the next limiter to try, or the best one once both were measured.
    public sealed record TrialAdvice(string Kind, string Mode, IReadOnlyList<LimiterTrialResult> Results); // Kind: "try-next" | "best"
    public TrialAdvice? AdviceAfterSession(GameEntry game)
    {
        var prefs = PrefsFor(game); var p = prefs.Presentation;
        if (!p.Enabled || prefs.UseFrameGen || prefs.LimiterTrials is not { Count: > 0 } done || prefs.PacingPreset == PacingPresets.LowLatency) return null;
        var candidates = LimiterTrial.Candidates(ReflexFor(game, p.Exe));
        var results = candidates.Where(done.ContainsKey).Select(c => done[c]).ToList();
        if (candidates.FirstOrDefault(c => !done.ContainsKey(c)) is { } next) return new("try-next", next, results);
        return LimiterTrial.Pick(results, CeilingMs) is { } best && best.Mode != p.AppliedLimiter ? new("best", best.Mode, results) : null;
    }

    // Switches this game's RivaTuner limiter (game closed). "game" = the low-latency preset: RivaTuner without a cap.
    public async Task<bool> UseLimiterAsync(GameEntry game, string mode)
    {
        var prefs = PrefsFor(game); var p = prefs.Presentation;
        if (!p.Enabled || Rtss is null || PlayPrefs is null || p.Exe.Length == 0) return false;
        if (processCheck?.FirstRunning([game]) is not null) { status = Text["GpCloseFirst"]; Notify(); return false; }
        try
        {
            var ok = mode == PacingPresets.GameLimiter ? await Rtss.DisableLowLatencyAsync(p.Exe) : await Rtss.EnableLimiterAsync(p.Exe, p.Cap, mode);
            if (!ok) { status = Text["GpRtssFailed"]; Notify(); return false; }
            var preset = mode == PacingPresets.GameLimiter ? PacingPresets.LowLatency : PacingPresets.Straight;
            return PlayPrefs.Set(game.Key, prefs with { Presentation = p with { AppliedLimiter = mode }, PacingPreset = preset });
        }
        catch (Exception ex) { log(ex); status = Text["GpRtssFailed"]; Notify(); return false; }
    }

    // "Linha reta" back: the best measured limiter (or Reflex/front edge as the automatic choice made it), RivaTuner capped again.
    public Task<bool> UsePresetAsync(GameEntry game, string preset)
    {
        var prefs = PrefsFor(game);
        if (preset == PacingPresets.LowLatency) return UseLimiterAsync(game, PacingPresets.GameLimiter);
        var reflex = ReflexFor(game, prefs.Presentation.Exe);
        var best = prefs.LimiterTrials is { Count: > 0 } t ? LimiterTrial.Pick(t.Values.Where(r => r.Mode != PacingPresets.GameLimiter), CeilingMs) : null;
        return UseLimiterAsync(game, best?.Mode ?? (reflex ? LimiterTrial.Reflex : LimiterTrial.FrontEdge));
    }
}
