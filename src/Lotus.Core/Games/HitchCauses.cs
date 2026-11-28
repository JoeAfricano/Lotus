using Lotus.Core.Benchmark;

namespace Lotus.Core.Games;

// "Por que está travando?" per hitch (Stellar Blade study, 25/11/2026): each frame above 50 ms is matched with what the PC
// was doing in the same second(s). Memory = Windows reading pages back from the page file; disk = the game reading files;
// graphics card = 3D engine full; otherwise processor/game (the game itself busy: shader compiling, streaming, its own logic).
// It is a likely cause per second, not a trace of the game's threads: the text says "provável".
public sealed record HitchSecond(double PageInsPerSecond, double GameReadMBps, double? GpuPercent);
public enum HitchCause { Cpu, Memory, Disk, Gpu }
public sealed record HitchBreakdown(int Total, int Cpu, int Memory, int Disk, int Gpu)
{
    public HitchCause Main => new[] { (HitchCause.Memory, Memory), (HitchCause.Disk, Disk), (HitchCause.Gpu, Gpu), (HitchCause.Cpu, Cpu) }.MaxBy(x => x.Item2).Item1;
    public int Count(HitchCause c) => c switch { HitchCause.Memory => Memory, HitchCause.Disk => Disk, HitchCause.Gpu => Gpu, _ => Cpu };
}

// One sample per second of the measurement, started together with the frame capture. Read-only.
public interface IHitchContextSampler { Task<IReadOnlyList<HitchSecond>> SampleAsync(int pid, TimeSpan duration, CancellationToken token); }

public static class HitchCauses
{
    public const double HitchMs = 50, MinPageIns = 250, MinReadMBps = 20, GpuFull = 97;
    public const int MinHitches = 3; // fewer than this says nothing about a pattern

    // busy (optional, aligned with frames): PresentMon's MsCPUBusy/MsGPUBusy of each frame. With it, a hitch whose own
    // graphics card time covers most of the frame is the card's; one with the processor busy most of it is the processor's
    // (the game itself); memory and disk still come first, because a processor waiting on them also shows as busy.
    public const double BusyShare = 0.6;
    public static HitchBreakdown? Classify(IReadOnlyList<FrameSample> frames, IReadOnlyList<HitchSecond> seconds, IReadOnlyList<FrameBusy>? busy = null)
    {
        if (frames.Count == 0 || seconds.Count == 0) return null;
        var start = frames[0].TimeSeconds - frames[0].Milliseconds / 1000;
        // "More than usual in this measurement": a busy game always reads a little; a hitch second stands out.
        var pageBar = Math.Max(MinPageIns, 2 * Median(seconds.Select(s => s.PageInsPerSecond)));
        var readBar = Math.Max(MinReadMBps, 2 * Median(seconds.Select(s => s.GameReadMBps)));
        int cpu = 0, mem = 0, disk = 0, gpu = 0;
        var aligned = busy is { Count: > 0 } && busy.Count == frames.Count;
        for (var index = 0; index < frames.Count; index++)
        {
            var f = frames[index];
            if (f.Milliseconds <= HitchMs) continue;
            var own = aligned ? busy![index] : null;
            // The seconds the long frame covers (it ends at TimeSeconds and lasted Milliseconds).
            int from = (int)Math.Floor(f.TimeSeconds - f.Milliseconds / 1000 - start), to = (int)Math.Floor(f.TimeSeconds - start);
            from = Math.Clamp(from, 0, seconds.Count - 1); to = Math.Clamp(to, 0, seconds.Count - 1);
            var span = seconds.Skip(from).Take(to - from + 1).ToList();
            if (span.Max(s => s.PageInsPerSecond) >= pageBar) mem++;
            else if (span.Max(s => s.GameReadMBps) >= readBar) disk++;
            else if (own?.GpuMs is { } g && g >= f.Milliseconds * BusyShare && g >= (own.CpuMs ?? 0)) gpu++;
            else if (own?.CpuMs is { } c && c >= f.Milliseconds * BusyShare) cpu++;
            else if (own?.GpuMs is null && span.Max(s => s.GpuPercent ?? 0) >= GpuFull) gpu++;
            else cpu++;
        }
        var total = cpu + mem + disk + gpu;
        return total == 0 ? null : new(total, cpu, mem, disk, gpu);
    }

    private static double Median(IEnumerable<double> values)
    {
        var v = values.Order().ToArray();
        return v.Length == 0 ? 0 : v.Length % 2 == 1 ? v[v.Length / 2] : (v[v.Length / 2 - 1] + v[v.Length / 2]) / 2;
    }
}
