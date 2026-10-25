using Lotus.Core.Telemetry;

namespace Lotus.Core;

// "Is my PC ready to play right now?" One number (0–100) from what can be read live, and one plain sentence about
// the biggest thing holding it back. Read-only: the card only points to where it can be fixed.
public enum ReadinessIssue { None, Memory, Cpu, GpuHot, HeavyApps, Disk }
public sealed record ReadinessResult(int Score, ReadinessIssue Issue, double? MemoryPercent, double? CpuPercent, double? GpuTemp, string? HeavyApp, long HeavyBytes);

public static class PcReadiness
{
    // Weights: memory 40, CPU 25, GPU temperature 15, heavy apps 10, disk space 10.
    public static ReadinessResult Compute(double? memoryUsedPercent, double? cpuPercent, double? gpuTempC, (string Name, long Bytes)? heaviestApp, bool diskLow)
    {
        var memory = memoryUsedPercent switch { null => 30, < 60 => 40, < 75 => 30, < 85 => 18, _ => 6 };
        var cpu = cpuPercent switch { null => 20, < 25 => 25, < 50 => 18, < 75 => 10, _ => 4 };
        var gpu = gpuTempC switch { null => 13, < 70 => 15, < 80 => 10, < 87 => 5, _ => 0 };
        var heavy = heaviestApp is { Bytes: >= 1L << 30 } ? 3 : heaviestApp is { Bytes: >= 500L << 20 } ? 7 : 10;
        var disk = diskLow ? 4 : 10;
        var score = Math.Clamp(memory + cpu + gpu + heavy + disk, 0, 100);
        // The issue costing the most points is the one worth mentioning.
        var losses = new (ReadinessIssue Issue, int Lost)[] { (ReadinessIssue.Memory, 40 - memory), (ReadinessIssue.Cpu, 25 - cpu), (ReadinessIssue.GpuHot, 15 - gpu), (ReadinessIssue.HeavyApps, 10 - heavy), (ReadinessIssue.Disk, 10 - disk) };
        var worst = losses.MaxBy(l => l.Lost);
        var issue = worst.Lost >= 6 ? worst.Issue : ReadinessIssue.None;
        return new(score, issue, memoryUsedPercent, cpuPercent, gpuTempC, heaviestApp?.Name, heaviestApp?.Bytes ?? 0);
    }

    public static ReadinessResult From(HardwareTelemetrySnapshot? snapshot, (string Name, long Bytes)? heaviestApp, bool diskLow)
    {
        var gpuTemp = snapshot?.Gpu.Devices.Select(d => d.Get(GpuSensor.Temperature)?.Value).FirstOrDefault(v => v is not null);
        return Compute(snapshot?.Memory.UsedPhysicalPercent.Value, snapshot?.Cpu.TotalUtilizationPercent.Value, gpuTemp, heaviestApp, diskLow);
    }
}
