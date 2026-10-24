namespace Lotus.Core.Telemetry;

public enum TelemetryStatus { Available, WarmingUp, Unavailable, Invalid, AccessDenied, Unsupported, Error }

public sealed record Reading<T>(T? Value, TelemetryStatus Status, string Source) where T : struct
{
    public static Reading<T> Available(T value, string source) => new(value, TelemetryStatus.Available, source);
    public static Reading<T> Missing(TelemetryStatus status, string source) => new(null, status, source);
}

public sealed record CpuTelemetrySnapshot(string? Model, int? PhysicalCores, int? LogicalProcessors,
    Reading<double> TotalUtilizationPercent, Reading<double> LotusUtilizationPercent, Reading<double> EffectiveFrequencyMHz);

public sealed record MemoryTelemetrySnapshot(Reading<ulong> TotalPhysicalBytes, Reading<ulong> AvailablePhysicalBytes,
    Reading<ulong> UsedPhysicalBytes, Reading<double> UsedPhysicalPercent,
    Reading<ulong> LotusWorkingSetBytes, Reading<ulong> LotusPrivateCommitBytes);

public sealed record HardwareTelemetrySnapshot(DateTimeOffset CapturedAt, TimeSpan MonotonicTime,
    CpuTelemetrySnapshot Cpu, MemoryTelemetrySnapshot Memory)
{
    public GpuTelemetryResult Gpu { get; init; } = GpuTelemetryResult.Missing(TelemetryStatus.Unavailable);
    public static HardwareTelemetrySnapshot Missing(DateTimeOffset utc, TimeSpan monotonic, TelemetryStatus status) => new(utc, monotonic,
        new(null, null, null, Reading<double>.Missing(status, "GetSystemTimes"), Reading<double>.Missing(status, "GetProcessTimes"),
            Reading<double>.Missing(TelemetryStatus.Unsupported, "None")),
        new(Reading<ulong>.Missing(status, "GlobalMemoryStatusEx"), Reading<ulong>.Missing(status, "GlobalMemoryStatusEx"),
            Reading<ulong>.Missing(status, "GlobalMemoryStatusEx"), Reading<double>.Missing(status, "GlobalMemoryStatusEx"),
            Reading<ulong>.Missing(status, "K32GetProcessMemoryInfo"), Reading<ulong>.Missing(status, "K32GetProcessMemoryInfo")));
}

// Implementations are called serially on a worker. Reset must invalidate CPU baselines.
public interface IHardwareTelemetryProvider
{
    void Reset();
    ValueTask<HardwareTelemetrySnapshot> ReadAsync(CancellationToken cancellationToken);
}

public interface IUiDispatcher { bool Post(Action action); }

public static class TelemetryMath
{
    public static double? Percent(double part, double total) =>
        double.IsFinite(part) && double.IsFinite(total) && total > 0 && part >= 0 && part <= total ? 100 * (part / total) : null;
    public static double GiB(ulong bytes) => bytes / 1_073_741_824d;
    public static double MiB(ulong bytes) => bytes / 1_048_576d;
    public static ulong? UsedPhysicalBytes(ulong total, ulong available) => total > 0 && available <= total ? total - available : null;
}

public readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User, ulong? Process, TimeSpan Monotonic);

public sealed class CpuUsageCalculator
{
    private CpuTimes? previous;
    public void Reset() => previous = null;
    public (Reading<double> Total, Reading<double> Process) Calculate(CpuTimes current, int logicalProcessors)
    {
        var old = previous;
        previous = current;
        if (old is not { } p) return (Missing(TelemetryStatus.WarmingUp, "GetSystemTimes"), Missing(TelemetryStatus.WarmingUp, "GetProcessTimes"));
        double? total = null, process = null;
        if (current.Monotonic > p.Monotonic && current.Idle >= p.Idle && current.Kernel >= p.Kernel && current.User >= p.User)
        {
            var elapsed = (double)(current.Kernel - p.Kernel) + (current.User - p.User);
            total = TelemetryMath.Percent(elapsed - (current.Idle - p.Idle), elapsed);
        }
        var seconds = (current.Monotonic - p.Monotonic).TotalSeconds;
        if (current.Process is { } now && p.Process is { } before && now >= before && logicalProcessors > 0)
            process = TelemetryMath.Percent((now - before) / 10_000_000d, seconds * logicalProcessors);
        return (Value(total, "GetSystemTimes"), Value(process, "GetProcessTimes"));
    }
    private static Reading<double> Missing(TelemetryStatus status, string source) => Reading<double>.Missing(status, source);
    private static Reading<double> Value(double? value, string source) => value is { } v ? Reading<double>.Available(v, source) : Missing(TelemetryStatus.Invalid, source);
}

public readonly record struct HistoryPoint(TimeSpan Time, double? CpuPercent, double? MemoryPercent);

public sealed class TelemetryHistory
{
    private readonly List<HistoryPoint> points = [];
    public IReadOnlyList<HistoryPoint> Points => points;
    public void Clear() => points.Clear();
    public void Add(HardwareTelemetrySnapshot snapshot)
    {
        var time = snapshot.MonotonicTime;
        if (points.Count > 0 && time <= points[^1].Time) return;
        points.RemoveAll(p => time - p.Time >= TimeSpan.FromSeconds(60));
        points.Add(new(time, Valid(snapshot.Cpu.TotalUtilizationPercent), Valid(snapshot.Memory.UsedPhysicalPercent)));
        // Bound allocations even if an internal test/provider interval is much shorter than 1 s.
        if (points.Count > 60) points.RemoveRange(0, points.Count - 60);
    }
    private static double? Valid(Reading<double> reading) => reading.Status == TelemetryStatus.Available && reading.Value is >= 0 and <= 100 ? reading.Value : null;
}
