using Lotus.Core.Telemetry;

namespace Lotus.Core.Diagnostics;

public enum MemoryFinding { Unavailable, Observing, NoSustainedLowAvailability, LowAvailability, VeryLowAvailability }

public sealed record MemoryAssessment(MemoryFinding Finding, TimeSpan ObservedDuration, TimeSpan LowDuration,
    ulong? TotalBytes, ulong? AvailableBytes, ulong? MinimumAvailableBytes, DateTimeOffset? CapturedAt, string? Source)
{
    public static MemoryAssessment Empty { get; } = new(MemoryFinding.Unavailable, TimeSpan.Zero, TimeSpan.Zero, null, null, null, null, null);
}

// RAM-AVAIL-001 v1: product triage heuristics, not Windows limits or proof of gaming impact.
// Serial caller; monotonic time, bounded window, invalid samples/gaps break continuity.
public sealed class MemoryAvailabilityAnalyzer
{
    public const string RuleVersion = "RAM-AVAIL-001 v1";
    public static readonly TimeSpan RequiredDuration = TimeSpan.FromSeconds(30);
    private readonly Queue<(TimeSpan Time, ulong Available)> window = new();
    private TimeSpan? last, lowSince, veryLowSince;
    private ulong? capacity;
    private string? source;
    public MemoryAssessment Current { get; private set; } = MemoryAssessment.Empty;
    public int SampleCount => window.Count;
    public void Reset()
    {
        window.Clear(); last = lowSince = veryLowSince = null; capacity = null; source = null;
        Current = MemoryAssessment.Empty;
    }
    public MemoryAssessment Add(HardwareTelemetrySnapshot snapshot)
    {
        var memory = snapshot.Memory;
        if (memory.TotalPhysicalBytes is not { Status: TelemetryStatus.Available, Value: > 0 } total ||
            memory.AvailablePhysicalBytes is not { Status: TelemetryStatus.Available, Value: { } available } ||
            available > total.Value.Value || string.IsNullOrWhiteSpace(total.Source) || total.Source != memory.AvailablePhysicalBytes.Source ||
            snapshot.MonotonicTime < TimeSpan.Zero)
        { Reset(); return Current; }
        var now = snapshot.MonotonicTime;
        if (last is { } previous)
        {
            if (now == previous) return Current; // language refresh is not a new sample
            if (now < previous) { Reset(); return Current; }
            if (now - previous > TimeSpan.FromSeconds(2.5) || capacity != total.Value || source != total.Source) Reset();
        }
        capacity = total.Value; source = total.Source; last = now;
        var low = available <= 1UL << 30 && available / (double)capacity.Value <= 0.10;
        var veryLow = available <= 512UL << 20 && available / (double)capacity.Value <= 0.05;
        lowSince = low ? lowSince ?? now : null;
        veryLowSince = veryLow ? veryLowSince ?? now : null;
        window.Enqueue((now, available));
        while (window.Count > 0 && (now - window.Peek().Time > TimeSpan.FromSeconds(60) || window.Count > 61)) window.Dequeue();
        var observed = now - window.Peek().Time;
        var lowDuration = lowSince is { } start ? now - start : TimeSpan.Zero;
        var finding = veryLowSince is { } critical && now - critical >= RequiredDuration ? MemoryFinding.VeryLowAvailability
            : lowDuration >= RequiredDuration ? MemoryFinding.LowAvailability
            : observed >= RequiredDuration ? MemoryFinding.NoSustainedLowAvailability : MemoryFinding.Observing;
        return Current = new(finding, observed, lowDuration, capacity, available, window.Min(x => x.Available), snapshot.CapturedAt, source);
    }
}
