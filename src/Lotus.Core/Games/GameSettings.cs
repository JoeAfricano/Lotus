using System.Text.RegularExpressions;

namespace Lotus.Core.Games;

public enum QualityTier { Low, Medium, High, Ultra }
public enum Upscaler { None, Dlss, Fsr, Xess }
public enum UpscaleMode { Off, Quality, Balanced, Performance }

// What the PC can do: GPU class/VRAM, CPU threads, RAM and the display the game runs on.
// ExactRefreshHz: the refresh rate as the display really runs it (74.973 on a "75 Hz" HDMI monitor), 0 when unknown.
// CpuName: the processor as Windows names it ("AMD Ryzen 5 5500"), "" when unknown; used to look it up in the parts table.
public sealed record SystemProfile(string GpuName, long VramMb, int CpuThreads, long RamMb, int Width, int Height, int RefreshHz, double ExactRefreshHz = 0, string CpuName = "");

// Frame limiter cap for a monitor without FreeSync/G-Sync, with V-Sync on. Measured on the Wukong benchmark (RTX 5060,
// HDMI monitor at 74.973 Hz, 12/11/2026): a cap of 75 (above the real rate) let frames queue behind V-Sync (more delay,
// uneven line); 74.95 (too far below) repeated one frame every ~43 s; 74.97, just under the real rate, gave the
// flattest line and the lowest delay. So the cap is the exact rate (or exact fraction) rounded down to 0.01 FPS.
public static class FramePacing
{
    // The exact rate when Windows reports one close to the nominal rate; otherwise the nominal rate itself.
    public static double RealRefresh(int nominalHz, double exactHz) => exactHz > 0 && Math.Abs(exactHz - nominalHz) < 1.5 ? exactHz : nominalHz;
    public static double FixedCap(double realHz, int divisor = 1) => Math.Floor((realHz / Math.Max(1, divisor) - 0.001) * 100) / 100;
    // A limit above the monitor's real rate makes frames wait in line behind V-Sync. 0.005 FPS of tolerance for rounding.
    public static bool AboveRefresh(double cap, double realHz) => cap > 0 && realHz > 0 && cap > realHz + 0.005;
}

// PacedCap: exact cap for a frame limiter (can be fractional, e.g. 37.5 on 75 Hz); AdaptiveSync: computed for a VRR monitor.
// ExternalLimiter: RivaTuner paces this game (per-game plan saved), so the game's own limit should stay off.
// EstimatedFps: what this game should reach with these settings on this PC (0 = no estimate for the game; never shown as measured).
public sealed record SettingsRecommendation(QualityTier Tier, QualityTier TextureTier, Upscaler Upscaler, UpscaleMode Mode, int FpsCap, int TargetFps,
    int GpuScore, IReadOnlyList<string> ReasonKeys, double PacedCap = 0, bool AdaptiveSync = false, double EstimatedFps = 0, GameFpsCurve? Game = null,
    bool CanPrefer60 = false, bool Prefer60 = false, Upscaler Available = Upscaler.None, string Why60 = "", bool ExternalLimiter = false);

// How one game runs on this PC at the display's resolution, per quality tier (Low → Ultra), without upscaling:
// GpuFps = what the graphics card can draw, CpuFps = what the processor can feed (lighter graphics barely raise it),
// Cap = the game's own frame limit. Basis: "measured" (calibrated by a Lotus measurement of this game on this PC),
// "lab" (benchmark table), "requirements" (Steam recommended parts). Always an estimate unless Basis is "measured".
// RamGb: the game's recommended memory (Steam), when known.
public sealed record GameFpsCurve(IReadOnlyList<double> GpuFps, IReadOnlyList<double> CpuFps, int? Cap, string Basis, string Confidence = "", int? RamGb = null)
{
    public bool Valid => GpuFps.Count == 4 && CpuFps.Count == 4 && GpuFps.All(f => f > 0 && double.IsFinite(f)) && CpuFps.All(f => f > 0 && double.IsFinite(f));
    // Upscaling draws fewer pixels: a coarse, documented gain on the GPU side only (the processor's work does not shrink).
    public static double UpscaleGain(UpscaleMode m) => m switch { UpscaleMode.Quality => 1.3, UpscaleMode.Balanced => 1.5, UpscaleMode.Performance => 1.8, _ => 1.0 };
    // The slower of the two limits wins (soft minimum, as real frames overlap a little), then the game's own cap.
    public double Fps(QualityTier tier, UpscaleMode mode)
    {
        double g = GpuFps[(int)tier] * UpscaleGain(mode), c = CpuFps[(int)tier];
        const double k = 6;
        var fps = Math.Pow(Math.Pow(g, -k) + Math.Pow(c, -k), -1 / k);
        return Cap is { } cap && fps > cap ? cap : fps;
    }
    // Lighter graphics cannot lift the frame rate above what the processor feeds.
    public double CpuLimit => CpuFps.Max();
}

// Relative GPU performance (≈100 = RTX 3060 class). A coarse, documented heuristic; the measurement loop corrects it on the real PC.
public static partial class GpuScore
{
    [GeneratedRegex(@"RTX\s*(\d)0(\d)0\s*(Ti|SUPER)?", RegexOptions.IgnoreCase)] private static partial Regex Rtx();
    [GeneratedRegex(@"RX\s*(\d)(\d)\d0\s*(XTX|XT|GRE)?", RegexOptions.IgnoreCase)] private static partial Regex Radeon();
    [GeneratedRegex(@"GTX\s*(1[06])(\d)0\s*(Ti|SUPER)?", RegexOptions.IgnoreCase)] private static partial Regex Gtx();
    [GeneratedRegex(@"Arc.*?([AB])(\d{3})", RegexOptions.IgnoreCase)] private static partial Regex Arc();

    public static int Estimate(string name, long vramMb)
    {
        var m = Rtx().Match(name);
        if (m.Success)
        {
            var gen = m.Groups[1].Value[0] switch { '2' => 0.8, '3' => 1.0, '4' => 1.25, '5' => 1.45, _ => 1.0 };
            var cls = m.Groups[2].Value[0] switch { '5' => 60, '6' => 100, '7' => 140, '8' => 190, '9' => 260, _ => 80 };
            var bonus = m.Groups[3].Value.ToUpperInvariant() switch { "TI" => 1.12, "SUPER" => 1.08, _ => 1.0 };
            return (int)(cls * gen * bonus);
        }
        m = Radeon().Match(name);
        if (m.Success)
        {
            var gen = m.Groups[1].Value[0] switch { '5' => 0.8, '6' => 1.0, '7' => 1.2, '9' => 1.45, _ => 0.6 };
            var cls = m.Groups[2].Value[0] switch { '5' => 55, '6' => 95, '7' => 140, '8' => 180, '9' => 240, _ => 60 };
            var bonus = m.Groups[3].Value.ToUpperInvariant() switch { "XTX" => 1.18, "XT" => 1.1, "GRE" => 1.05, _ => 1.0 };
            return (int)(cls * gen * bonus);
        }
        m = Gtx().Match(name);
        if (m.Success)
        {
            var family = m.Groups[1].Value; var cls = m.Groups[2].Value[0];
            var score = family == "16" ? (cls == '5' ? 40 : 60) : cls switch { '5' => 25, '6' => 45, '7' => 60, '8' => 75, _ => 35 };
            return (int)(score * (m.Groups[3].Success ? 1.12 : 1.0));
        }
        m = Arc().Match(name);
        if (m.Success) return (m.Groups[1].Value.ToUpperInvariant(), m.Groups[2].Value) switch
        {
            ("B", var n) when n.StartsWith('5') => 105, ("A", "770") => 95, ("A", "750") => 85, ("A", "580") => 70, _ => 35
        };
        if (Regex.IsMatch(name, @"UHD|Iris|Radeon\(TM\)\s*Graphics|Radeon Graphics|Vega \d", RegexOptions.IgnoreCase)) return 15;
        // Unknown model: fall back to VRAM, which tracks performance class loosely.
        return vramMb >= 12_000 ? 150 : vramMb >= 8_000 ? 100 : vramMb >= 6_000 ? 70 : vramMb >= 4_000 ? 40 : 20;
    }
}

// Starting point from hardware + display. Demand grows with pixels and target FPS; upscalers add headroom before the tier is chosen.
// With a game's own FPS curve (see GameFpsCurve) the choice follows that game: the best-looking settings that still hold
// the target on this PC, the processor's ceiling in that game and its own frame cap. Without one, the generic rule.
public static class SettingsAdvisor
{
    // goal (player profile): "balanced" = steady FPS at the monitor rate; "fps" = one quality level lower, higher cap;
    // "competitive" = two levels lower and the highest cap, for latency.
    // adaptiveSync: the player said FreeSync/G-Sync is on (asked before the analysis).
    public static SettingsRecommendation Recommend(SystemProfile p, string goal = "balanced", bool adaptiveSync = false) => Generic(p, goal, adaptiveSync, null, false);

    // tech: what a scan of the game's folder found. When it found no upscaler at all, none is suggested (Minecraft Java
    // has no DLSS); GameTech.Unknown (nothing scanned) keeps the card's own upscaler, as before.
    private static SettingsRecommendation Generic(SystemProfile p, string goal, bool adaptiveSync, GameTech? tech, bool prefer60)
    {
        var reasons = new List<string>();
        var score = GpuScore.Estimate(p.GpuName, p.VramMb);
        var refresh = Refresh(p);
        double Capability(int fps) => score / Math.Max(0.5, Math.Max(1, p.Width) * Math.Max(1, p.Height) / (1920.0 * 1080.0) * (fps / 60.0));
        // Weak PCs aim for a steady 60 instead of chasing a high-refresh monitor they cannot feed.
        var target = refresh > 60 && Capability(refresh) < 70 ? 60 : refresh;
        var available = PickUpscaler(p.GpuName, tech);
        (UpscaleMode Mode, QualityTier Tier) For(int fps)
        {
            var native = Capability(fps);
            var m = native >= 160 ? UpscaleMode.Off : native >= 110 ? UpscaleMode.Quality : native >= 70 ? UpscaleMode.Balanced : UpscaleMode.Performance;
            if (m == UpscaleMode.Performance && p.Height <= 1080) m = UpscaleMode.Balanced; // Performance mode is too blurry at 1080p and below
            if (available == Upscaler.None) m = UpscaleMode.Off;
            var effective = native * GameFpsCurve.UpscaleGain(m);
            return (m, effective >= 160 ? QualityTier.Ultra : effective >= 110 ? QualityTier.High : effective >= 70 ? QualityTier.Medium : QualityTier.Low);
        }
        var (mode, tier) = For(target);
        // Optional per game (José, 19/11/2026): lock 60 on a faster monitor, whatever the PC holds.
        var canPrefer60 = refresh > 60 && target > 60;
        var why60 = canPrefer60 ? For(60) is var at60 && (at60.Tier > tier || at60.Mode < mode) ? "looks" : "lighter" : "";
        if (canPrefer60 && prefer60) { (mode, tier) = For(60); target = 60; reasons.Add("GsReasonPrefer60"); }
        var upscaler = mode == UpscaleMode.Off ? Upscaler.None : available;
        if (upscaler != Upscaler.None) reasons.Add(upscaler == Upscaler.Dlss ? "GsReasonDlss" : "GsReasonFsr");
        if (target < refresh && !reasons.Contains("GsReasonPrefer60")) reasons.Add("GsReasonTarget60");
        if (Capability(target) < 40) reasons.Add("GsReasonLowEnd");
        return Finish(p, goal, adaptiveSync, reasons, score, refresh, target, tier, upscaler, mode, null)
            with { Available = available, CanPrefer60 = canPrefer60, Prefer60 = canPrefer60 && prefer60, Why60 = why60 };
    }

    // Per game: every quality/upscaling pair is ranked by how it looks; the first one whose estimate holds the target
    // (with 8% to spare for the heavy moments) wins. No curve, or an invalid one: the generic rule above.
    // José's choice (19/11/2026): to hold the monitor rate, graphics go down first (to Low if needed). When that costs
    // looks and the game has no frame generation, the player may instead lock 60 FPS with better graphics (prefer60).
    public static SettingsRecommendation Recommend(SystemProfile p, string goal, bool adaptiveSync, GameFpsCurve? game, GameTech? tech = null, bool prefer60 = false)
    {
        if (game is not { Valid: true }) return Generic(p, goal, adaptiveSync, tech, prefer60);
        var reasons = new List<string> { "GsReasonGameAware" };
        var score = GpuScore.Estimate(p.GpuName, p.VramMb);
        var refresh = Refresh(p);
        var upscaler = PickUpscaler(p.GpuName, tech);
        UpscaleMode[] modes = upscaler == Upscaler.None ? [UpscaleMode.Off] : p.Height > 1080 ? [UpscaleMode.Off, UpscaleMode.Quality, UpscaleMode.Balanced, UpscaleMode.Performance] : [UpscaleMode.Off, UpscaleMode.Quality, UpscaleMode.Balanced];
        // Look: two points per quality level, one lost per upscaling step; a tie keeps the native image. FSR/XeSS blur
        // more at 1080p and below (fewer source pixels), so there each of their steps costs a whole quality level.
        var stepCost = upscaler != Upscaler.Dlss && p.Height <= 1080 ? 2 : 1;
        var combos = (from t in Enum.GetValues<QualityTier>() from m in modes select (Tier: t, Mode: m, Look: (int)t * 2 - (int)m * stepCost))
            .OrderByDescending(c => c.Look).ThenBy(c => c.Mode).ToList();
        double Best(QualityTier floor) => combos.Where(c => c.Tier >= floor).Max(c => game.Fps(c.Tier, c.Mode));

        // Target: the monitor rate (or the game's own cap, when lower), lowering graphics as far as Low to hold it.
        // Below it: with FreeSync/G-Sync, what the PC holds; without, an exact fraction of the refresh rate (every frame
        // on screen for the same time).
        var top = game.Cap is { } cap && cap < refresh ? cap : refresh;
        // What the PC holds with 8% to spare for heavy moments; a game that reaches its own cap holds the cap itself.
        double Reach(QualityTier f) => game.Cap is { } c && Best(f) >= c - 0.5 ? c : Best(f) / 1.08;
        double reachAny = Reach(QualityTier.Low);
        var reach = reachAny;
        var target = top;
        if (reach < top * 0.97)
        {
            if (adaptiveSync) target = Math.Clamp((int)Math.Floor(reach), 30, top);
            else { var n = 1; while (refresh / (double)n > reach && refresh / (double)(n + 1) >= 30) n++; target = (int)Math.Floor(refresh / (double)n); }
            reasons.Add(target == 60 ? "GsReasonTarget60" : "GsReasonTargetLower");
            if (reachAny < 40) reasons.Add("GsReasonLowEnd");
        }
        if (game.Cap is { } own && own <= refresh) reasons.Add("GsReasonGameCap");
        // The processor feeds less than the target in this game: lighter graphics will not fix it.
        if (game.CpuLimit < top * 0.97) reasons.Add("GsReasonCpuGame");
        // Less memory than the game recommends (Windows reports a little under the installed amount: 0.5 GB of slack).
        if (game.RamGb is { } ram && p.RamMb > 0 && p.RamMb / 1024.0 + 0.5 < ram) reasons.Add("GsReasonRamGame");

        (QualityTier Tier, UpscaleMode Mode, int Look) PickFor(int fps)
        {
            var need = Math.Min(fps * 1.08, game.Cap is { } limit ? limit - 0.5 : double.MaxValue);
            var holds = combos.FindIndex(c => game.Fps(c.Tier, c.Mode) >= need);
            return holds >= 0 ? combos[holds] : combos.OrderByDescending(c => game.Fps(c.Tier, c.Mode)).ThenByDescending(c => c.Look).First();
        }
        var pick = PickFor(target);
        // Locking 60 is an opt-in per game, offered when the PC holds 60 (why60 tells the player what it gives):
        // "cant-hold" = the PC fell to a fraction below 60, so 60 is smoother; "looks" = holding the monitor rate cost
        // graphics, 60 looks better; "lighter" = same image, less heat and power.
        var at60 = PickFor(60);
        // José (19/11/2026): optional in every game on a monitor faster than 60, frame generation or not, even when the
        // PC already holds the monitor rate (then 60 only makes the game lighter: less heat and power).
        var canPrefer60 = top > 60 && target != 60 && Reach(QualityTier.Low) >= 60;
        var why60 = !canPrefer60 ? "" : target < 60 ? "cant-hold" : at60.Look > pick.Look ? "looks" : "lighter";
        var use60 = canPrefer60 && prefer60;
        if (use60) { pick = at60; target = 60; reasons.Add("GsReasonPrefer60"); }
        var mode = pick.Mode;
        var chosenUpscaler = mode == UpscaleMode.Off ? Upscaler.None : upscaler;
        if (chosenUpscaler != Upscaler.None) reasons.Add(chosenUpscaler == Upscaler.Dlss ? "GsReasonDlss" : "GsReasonFsr");
        return Finish(p, goal, adaptiveSync, reasons, score, refresh, target, pick.Tier, chosenUpscaler, mode, game) with { CanPrefer60 = canPrefer60, Prefer60 = use60, Available = upscaler, Why60 = why60 };
    }

    // The real refresh rate of the player's monitor (60 when Windows does not report it); 165, 240 Hz and up are kept.
    // Why no upscaler, per game: the game ships none, this PC already holds the target without it here, or (no data for
    // the game) the card has room at this resolution.
    public static string NoUpscaleKey(SettingsRecommendation r) =>
        r.Available == Upscaler.None ? "GsUpscaleNoneInGame" : r.Game is not null ? "GsUpscaleNotNeeded" : "GsLineNoUpscale";

    private static int Refresh(SystemProfile p) => Math.Clamp(p.RefreshHz <= 0 ? 60 : p.RefreshHz, 50, 500);

    // Upscaler: the one the game ships that fits the card; none when a scan found none; the card's own when not scanned.
    private static Upscaler PickUpscaler(string gpuName, GameTech? tech)
    {
        var rtx = gpuName.Contains("RTX", StringComparison.OrdinalIgnoreCase);
        var arc = gpuName.Contains("Arc", StringComparison.OrdinalIgnoreCase);
        if (tech is null || ReferenceEquals(tech, GameTech.Unknown)) return rtx ? Upscaler.Dlss : arc ? Upscaler.Xess : Upscaler.Fsr;
        var t = tech;
        return t.Dlss && rtx ? Upscaler.Dlss : t.Xess && arc ? Upscaler.Xess : t.Fsr ? Upscaler.Fsr : t.Xess ? Upscaler.Xess : Upscaler.None;
    }

    private static SettingsRecommendation Finish(SystemProfile p, string goal, bool adaptiveSync, List<string> reasons, int score, int refresh, int target,
        QualityTier tier, Upscaler upscaler, UpscaleMode mode, GameFpsCurve? game)
    {
        // Textures cost VRAM, not GPU time: cap them by memory, independently of the general tier.
        var textures = p.VramMb switch { >= 10_000 => QualityTier.Ultra, >= 7_500 => QualityTier.High, >= 5_500 => QualityTier.Medium, > 0 => QualityTier.Low, _ => tier };
        if (textures < tier) reasons.Add("GsReasonVram");
        if (p.CpuThreads is > 0 and <= 4) reasons.Add("GsReasonCpu");
        if (p.RamMb is > 0 and < 12_000) reasons.Add("GsReasonRam");
        // FPS cap follows the monitor. With FreeSync/G-Sync: 3 below the refresh rate (VRR stays engaged, no V-Sync lag,
        // no tearing). Without it: the refresh rate itself, or an exact fraction of it (1/2, 1/3) when the PC cannot hold
        // it, so every frame stays on screen for the same time (console-like pacing, no judder); V-Sync on in the game.
        // The goal changes quality, not the cap.
        // With the exact rate known, the fixed-rate cap sits just under it (FramePacing); the game's own whole-number
        // limit stays at the nominal rate, since a whole number below it would repeat a frame every second.
        double paced; int cap;
        if (adaptiveSync) { paced = target < refresh ? target : Math.Max(57, refresh - 3); cap = (int)Math.Floor(paced); }
        else
        {
            var n = 1; while (refresh / (double)n > target + 0.5 && refresh / (double)(n + 1) >= 30) n++;
            cap = (int)Math.Floor(refresh / (double)n);
            paced = p.ExactRefreshHz > 0 && p.RefreshHz == refresh ? FramePacing.FixedCap(FramePacing.RealRefresh(refresh, p.ExactRefreshHz), n) : refresh / (double)n;
        }
        // A game with its own lower limit (60 on a 75 Hz monitor): that limit is the cap, not a fraction of the refresh rate.
        if (game?.Cap is { } own && own < refresh && target >= own) { cap = own; paced = own; }
        // The player chose to lock 60 with better graphics: 60 itself, not a fraction of the refresh rate.
        else if (target == 60 && refresh > 60 && reasons.Contains("GsReasonPrefer60")) { cap = 60; paced = 60; }
        if (goal is "fps" or "competitive")
        {
            var drop = goal == "fps" ? 1 : 2;
            tier = (QualityTier)Math.Max((int)QualityTier.Low, (int)tier - drop);
            textures = (QualityTier)Math.Min((int)textures, Math.Max((int)tier, (int)textures - drop + 1));
            reasons.Add(goal == "fps" ? "GsReasonGoalFps" : "GsReasonGoalCompetitive");
        }
        else reasons.Add("GsReasonCap");
        reasons.Add(adaptiveSync ? "GsReasonMonitorCap" : cap < refresh ? "GsReasonHalfRate" : "GsReasonFixedCap");
        var estimate = game is null ? 0 : Math.Round(game.Fps(tier, mode));
        return new(tier, textures, upscaler, mode, cap, target, score, reasons, paced, adaptiveSync, estimate, game);
    }
}

// Where a game keeps its settings and how Lotus can change them. Every write is preceded by a backup.
public sealed record GameSettingsSupport(string RecipeId, string EngineLabel, bool CanApply, string? Detail = null);
public interface IGameSettingsRecipe
{
    string Id { get; }
    GameSettingsSupport? Probe(GameEntry game);                     // null = recipe does not apply to this game
    string? ReadRaw(GameEntry game);                                 // current settings text (INI, JSON...)
    void WriteRaw(GameEntry game, string raw);
    string Transform(string raw, SettingsRecommendation recommendation); // pure: only known keys change
    // One quality level up (+1) or down (-1); null when the recipe cannot step quality or is already at the limit.
    string? StepQuality(string raw, int step) => null;
}
// Keeps the first (original) settings text per game so "Restore original" always returns to the pre-Lotus state.
public interface IGameSettingsStore
{
    string? LoadOriginal(string gameKey);
    void SaveOriginalOnce(string gameKey, string raw);
    void ClearOriginal(string gameKey);
}
public interface ISystemProfileSource { SystemProfile Read(); }
public interface IGameProcessCheck
{
    bool IsRunning(GameEntry game);
    RunningGame? Find(GameEntry game) => null; // main game process, used by the measurement loop
    // First library game with a process running; one pass over the process list when the implementation can.
    GameEntry? FirstRunning(IReadOnlyList<GameEntry> games) => games.FirstOrDefault(IsRunning);
}

// ---- Measurement loop ----
// One adjustment per measurement: lower quality only when the GPU is the bottleneck, raise it only with clear headroom.
public sealed record TuningDecision(int Step, string ReasonKey);
public static class QualityTuner
{
    public static TuningDecision Decide(double averageFps, double low1Fps, int targetFps, double? gpuBusyPercent, bool canRaise, bool canLower)
    {
        var below = averageFps < targetFps * 0.95 || low1Fps < targetFps * 0.70;
        if (below)
        {
            // A GPU that is not busy cannot be helped by lighter graphics: the CPU (or the game) is the limit.
            if (gpuBusyPercent is { } busy && busy < 80) return new(0, "GsTuneCpuBound");
            return canLower ? new(-1, "GsTuneLower") : new(0, "GsTuneAtMinimum");
        }
        if (gpuBusyPercent is { } load && load < 65 && canRaise) return new(+1, "GsTuneRaise");
        return new(0, "GsTuneGood");
    }
}
public interface IGpuLoadSampler { Task<double?> AverageAsync(TimeSpan duration, CancellationToken token); }
public sealed record RunningGame(int Pid, string Executable, DateTimeOffset StartedUtc);
