namespace Lotus.Core.Benchmark;

public sealed record CaptureTarget(int Pid, string Executable, DateTimeOffset ProcessStartedUtc)
{
    public string Label => $"{Path.GetFileName(Executable)} · PID {Pid}";
}
public sealed record CaptureRequest(CaptureTarget Target, int Seconds, bool Elevated, string Conditions, string HardwareJson);
public sealed record FrameSample(double TimeSeconds, double Milliseconds);
public sealed record FrameStatistics(int Count, double DurationSeconds, double AverageFps, double Low1Fps,
    double P95Ms, double P99Ms, double MedianMs, int SpikeCount, double SpikeThresholdMs);
public sealed record ParsedCapture(string SwapChain, int OtherSwapChains, int InvalidRows, int TotalRows,
    IReadOnlyList<FrameSample> Frames, FrameStatistics? Statistics, string FrameTypes, bool Truncated,
    long? LostEvents = null, long? LostBuffers = null, long? OverflowedPresents = null);
public sealed record BenchmarkSession(Guid Id, CaptureTarget Target, DateTimeOffset StartedUtc, double WallSeconds,
    string Status, string ProviderVersion, string AlgorithmVersion, string RawPath, string RawSha256,
    string Conditions, string HardwareJson, ParsedCapture? Data, string Diagnostics)
{
    public CaptureResourceUsage? ResourceUsage { get; init; }
    public int? CollectorExitCode { get; init; }
    // Optional for backward-compatible JSON payloads. Missing evidence never means zero loss.
    public EtwIntegrityEvidence? EtwEvidence { get; init; }
    public bool MathematicallyValid => Data is { Statistics: not null, InvalidRows: 0, Truncated: false };
    public CaptureIntegrity Integrity => BenchmarkQuality.Classify(this);
    public string Label => $"{Path.GetFileName(Target.Executable)} · {StartedUtc.ToLocalTime():g} · {Status}";
}
public enum CaptureIntegrity { Unknown, Verified, LossDetected, Invalid }
public sealed record EtwIntegrityEvidence(long? EventsLost, long? BuffersLost, long? OverflowedPresents,
    string Source, bool CompleteFinalCounters);
public static class BenchmarkQuality
{
    public static CaptureIntegrity Classify(BenchmarkSession session)
    {
        if (!session.MathematicallyValid || session.Status is not ("Completed" or "EventLoss") ||
            session.CollectorExitCode is not (null or 0)) return CaptureIntegrity.Invalid;
        var e = session.EtwEvidence;
        if (e?.EventsLost > 0 || e?.BuffersLost > 0 || e?.OverflowedPresents > 0 ||
            session.Data?.LostEvents > 0 || session.Data?.LostBuffers > 0 || session.Data?.OverflowedPresents > 0 ||
            session.Status == "EventLoss") return CaptureIntegrity.LossDetected;
        if (e is { EventsLost: 0, BuffersLost: 0, OverflowedPresents: 0, CompleteFinalCounters: true } &&
            !string.IsNullOrWhiteSpace(e.Source) && session.CollectorExitCode == 0 &&
            session.Data?.LostEvents is null or 0 && session.Data?.LostBuffers is null or 0 &&
            session.Data?.OverflowedPresents is null or 0) return CaptureIntegrity.Verified;
        return CaptureIntegrity.Unknown;
    }
    // Necessary only: even this cannot establish experimental comparability or a causal gain.
    public static bool IntegrityAllowsGainAssessment(BenchmarkSession a, BenchmarkSession b) =>
        BenchmarkMath.Comparable(a, b) && a.Integrity == CaptureIntegrity.Verified && b.Integrity == CaptureIntegrity.Verified;
}
public sealed record CaptureResourceUsage(double SampleSeconds, int Samples, double? LotusCpuPercent,
    double LotusWorkingSetMiB, double? CollectorCpuPercent, double? CollectorWorkingSetMiB);
public interface IFrameCaptureProvider
{
    Task<BenchmarkSession> CaptureAsync(CaptureRequest request, CancellationToken stop);
}
public interface IBenchmarkRepository
{
    Task SaveAsync(BenchmarkSession session, CancellationToken token = default);
    Task<IReadOnlyList<BenchmarkSession>> ListAsync(CancellationToken token = default);
}
public interface ICaptureTargetSource { Task<IReadOnlyList<CaptureTarget>> ListAsync(); }
public readonly record struct MetricDifference(double Absolute, double Percent)
{
    public static MetricDifference Between(double baseline, double current) =>
        baseline > 0 && double.IsFinite(baseline) && double.IsFinite(current)
            ? new(current - baseline, 100 * (current / baseline - 1)) : throw new ArgumentOutOfRangeException(nameof(baseline));
}
public static class BenchmarkMath
{
    public const string Version = "present-interval-v1";
    // At least 1,000 intervals (10 slow-tail observations) AND 10 seconds. No outlier removal.
    public static FrameStatistics? Calculate(IReadOnlyList<FrameSample> frames)
    {
        if (frames.Count < 1000 || frames.Any(f => !double.IsFinite(f.Milliseconds) || f.Milliseconds <= 0 ||
            !double.IsFinite(f.TimeSeconds) || f.TimeSeconds < 0)) return null;
        var ordered = frames.Select(f => f.Milliseconds).Order().ToArray();
        var sum = ordered.Sum();
        if (sum < 10000) return null;
        var median = Quantile(ordered, .5);
        var threshold = Math.Max(50, 3 * median);
        var tail = (int)Math.Ceiling(ordered.Length * .01);
        return new(ordered.Length, sum / 1000, 1000 * ordered.Length / sum,
            1000 / ordered.TakeLast(tail).Average(), Quantile(ordered, .95), Quantile(ordered, .99),
            median, ordered.Count(v => v > threshold), threshold);
    }
    // Nearest rank, 1-based. 1% low is reciprocal of mean slowest 1%, not P99 FPS.
    private static double Quantile(double[] sorted, double p) => sorted[(int)Math.Ceiling(sorted.Length * p) - 1];
    public static bool Comparable(BenchmarkSession a, BenchmarkSession b) => a.Id != b.Id &&
        a.Status == "Completed" && b.Status == "Completed" && a.Data?.Statistics is not null && b.Data?.Statistics is not null &&
        string.Equals(a.Target.Executable, b.Target.Executable, StringComparison.OrdinalIgnoreCase) &&
        a.AlgorithmVersion == b.AlgorithmVersion && a.ProviderVersion == b.ProviderVersion;
}
