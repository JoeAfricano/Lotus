using System.Text.RegularExpressions;

namespace Lotus.Core.Games;

public enum QualityTier { Low, Medium, High, Ultra }
public enum Upscaler { None, Dlss, Fsr, Xess }
public enum UpscaleMode { Off, Quality, Balanced, Performance }

// What the PC can do: GPU class/VRAM, CPU threads, RAM and the display the game runs on.
public sealed record SystemProfile(string GpuName, long VramMb, int CpuThreads, long RamMb, int Width, int Height, int RefreshHz);

public sealed record SettingsRecommendation(QualityTier Tier, QualityTier TextureTier, Upscaler Upscaler, UpscaleMode Mode, int FpsCap, int TargetFps,
    int GpuScore, IReadOnlyList<string> ReasonKeys);

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
public static class SettingsAdvisor
{
    // goal (player profile): "balanced" = steady FPS at the monitor rate; "fps" = one quality level lower, higher cap;
    // "competitive" = two levels lower and the highest cap, for latency.
    public static SettingsRecommendation Recommend(SystemProfile p, string goal = "balanced")
    {
        var reasons = new List<string>();
        var score = GpuScore.Estimate(p.GpuName, p.VramMb);
        var refresh = Math.Clamp(p.RefreshHz <= 0 ? 60 : p.RefreshHz, 60, 144);
        double Capability(int fps) => score / Math.Max(0.5, Math.Max(1, p.Width) * Math.Max(1, p.Height) / (1920.0 * 1080.0) * (fps / 60.0));
        // Weak PCs aim for a steady 60 instead of chasing a high-refresh monitor they cannot feed.
        var target = refresh > 60 && Capability(refresh) < 70 ? 60 : refresh;
        var native = Capability(target);

        var nvidiaRtx = p.GpuName.Contains("RTX", StringComparison.OrdinalIgnoreCase);
        var intelArc = p.GpuName.Contains("Arc", StringComparison.OrdinalIgnoreCase);
        var mode = native >= 160 ? UpscaleMode.Off : native >= 110 ? UpscaleMode.Quality : native >= 70 ? UpscaleMode.Balanced : UpscaleMode.Performance;
        if (mode == UpscaleMode.Performance && p.Height <= 1080) mode = UpscaleMode.Balanced; // Performance mode is too blurry at 1080p and below
        var upscaler = mode == UpscaleMode.Off ? Upscaler.None : nvidiaRtx ? Upscaler.Dlss : intelArc ? Upscaler.Xess : Upscaler.Fsr;
        if (upscaler != Upscaler.None) reasons.Add(upscaler == Upscaler.Dlss ? "GsReasonDlss" : "GsReasonFsr");
        var effective = native * mode switch { UpscaleMode.Quality => 1.3, UpscaleMode.Balanced => 1.5, UpscaleMode.Performance => 1.8, _ => 1.0 };
        var tier = effective >= 160 ? QualityTier.Ultra : effective >= 110 ? QualityTier.High : effective >= 70 ? QualityTier.Medium : QualityTier.Low;

        // Textures cost VRAM, not GPU time: cap them by memory, independently of the general tier.
        var textures = p.VramMb switch { >= 10_000 => QualityTier.Ultra, >= 7_500 => QualityTier.High, >= 5_500 => QualityTier.Medium, > 0 => QualityTier.Low, _ => tier };
        if (textures < tier) reasons.Add("GsReasonVram");
        if (target < refresh) reasons.Add("GsReasonTarget60");
        if (native < 40) reasons.Add("GsReasonLowEnd");
        if (p.CpuThreads is > 0 and <= 4) reasons.Add("GsReasonCpu");
        if (p.RamMb is > 0 and < 12_000) reasons.Add("GsReasonRam");
        var cap = target;
        if (goal is "fps" or "competitive")
        {
            var drop = goal == "fps" ? 1 : 2;
            tier = (QualityTier)Math.Max((int)QualityTier.Low, (int)tier - drop);
            textures = (QualityTier)Math.Min((int)textures, Math.Max((int)tier, (int)textures - drop + 1));
            cap = Math.Min(goal == "fps" ? 240 : 300, refresh * 2);
            reasons.Add(goal == "fps" ? "GsReasonGoalFps" : "GsReasonGoalCompetitive");
        }
        else reasons.Add("GsReasonCap");
        return new(tier, textures, upscaler, mode, cap, target, score, reasons);
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
