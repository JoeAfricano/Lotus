namespace Lotus.Core.Games;

// "Por que está travando?": turns one measurement of the running game plus the PC's state into likely causes,
// ranked, each with one fix the player can apply. Read-only: it never changes anything by itself.
public enum StutterFix { None, Precompile, PrepareProfile, LowerQuality, CloseApps }
public sealed record StutterFinding(string Key, StutterFix Fix, int Weight, string? Detail = null);
public sealed record StutterInputs(double AverageFps, double Low1Fps, int SpikeCount, double DurationSeconds, double? GpuBusy, int TargetFps,
    ShaderState? Shaders, int PendingProfileSteps, double? MemoryLoadPercent, IReadOnlyList<string> TopMemoryApps, bool CanLowerQuality);

public static class StutterDiagnosis
{
    public static IReadOnlyList<StutterFinding> Analyze(StutterInputs i)
    {
        var list = new List<StutterFinding>();
        var stutters = i.Low1Fps < i.AverageFps * 0.6 || (i.DurationSeconds > 0 && i.SpikeCount / i.DurationSeconds > 0.5);
        var lowFps = i.AverageFps < i.TargetFps * 0.9;
        // Sudden drops: shader compilation is the classic cause in modern DirectX 12 games.
        if (stutters && i.Shaders is ShaderState.Ready or ShaderState.Stale) list.Add(new("WhyShaders", StutterFix.Precompile, 90));
        if (i.MemoryLoadPercent is >= 85) list.Add(new("WhyMemory", StutterFix.CloseApps, stutters ? 80 : 50, string.Join(", ", i.TopMemoryApps.Take(3))));
        if (i.PendingProfileSteps > 0) list.Add(new("WhyProfile", StutterFix.PrepareProfile, stutters || lowFps ? 60 : 30));
        if (lowFps && i.GpuBusy is >= 90) list.Add(new("WhyGpuLimit", i.CanLowerQuality ? StutterFix.LowerQuality : StutterFix.None, 70));
        else if (lowFps && i.GpuBusy is < 70) list.Add(new("WhyCpuLimit", i.TopMemoryApps.Count > 0 ? StutterFix.CloseApps : StutterFix.None, 55));
        if (stutters && list.Count == 0) list.Add(new("WhyUnknownStutter", StutterFix.None, 20));
        if (list.Count == 0) list.Add(new("WhyAllGood", StutterFix.None, 0));
        return [.. list.OrderByDescending(f => f.Weight)];
    }
}

// System memory load and the heaviest programs (names only), read without changing anything.
public interface IMemoryPressure { (double LoadPercent, IReadOnlyList<string> TopApps) Read(GameEntry? exclude); }
