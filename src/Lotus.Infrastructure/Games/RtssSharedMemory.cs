using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using System.Text;
using Lotus.Core.Benchmark;

namespace Lotus.Infrastructure.Games;

// Read-only access to RivaTuner Statistics Server's documented shared memory ("RTSSSharedMemoryV2", SDK RTSSSharedMemory.h).
// No admin rights and no injection by Lotus: RTSS already tracks the game; Lotus only reads its counters.
[SupportedOSPlatform("windows")]
public static class RtssSharedMemory
{
    private const uint Signature = 0x52545353; // 'RTSS'
    public sealed record AppSample(int ProcessId, string Name, uint Time0, uint Time1, uint Frames, uint FrameTimeMicroseconds, uint Flags = 0)
    {
        // Low word of dwFlags = the 3D API RTSS hooked (APPFLAG_OGL 1 ... APPFLAG_VULKAN 10 in the SDK).
        public string? Api => (Flags & 0xFFFF) switch
        {
            1 => "OpenGL", 2 => "DirectDraw", 3 => "Direct3D 8", 4 => "Direct3D 9", 5 => "Direct3D 9", 6 => "Direct3D 10",
            7 => "Direct3D 11", 8 or 9 => "Direct3D 12", 10 => "Vulkan", _ => null,
        };
    }

    public const string DefaultMap = "RTSSSharedMemoryV2";
    public static IReadOnlyList<AppSample> Read(string mapName = DefaultMap)
    {
        try
        {
            using var map = MemoryMappedFile.OpenExisting(mapName, MemoryMappedFileRights.Read);
            using var view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            if (view.ReadUInt32(0) != Signature) return [];
            uint entrySize = view.ReadUInt32(8), arrOffset = view.ReadUInt32(12), arrSize = view.ReadUInt32(16);
            if (entrySize < 284 || arrSize > 1024) return [];
            var list = new List<AppSample>();
            var name = new byte[260];
            for (var i = 0u; i < arrSize; i++)
            {
                long at = arrOffset + i * (long)entrySize;
                if (at + 284 > view.Capacity) break;
                var pid = view.ReadInt32(at);
                if (pid == 0) continue;
                view.ReadArray(at + 4, name, 0, 260);
                var text = Encoding.ASCII.GetString(name, 0, Math.Max(0, Array.IndexOf(name, (byte)0) is var z and >= 0 ? z : 260));
                list.Add(new(pid, text, view.ReadUInt32(at + 268), view.ReadUInt32(at + 272), view.ReadUInt32(at + 276), view.ReadUInt32(at + 280), view.ReadUInt32(at + 264)));
            }
            return list;
        }
        catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or IOException) { return []; } // RTSS not running
    }

    // Per-frame timings from NVIDIA Reflex latency markers (native in the game, or injected by RTSS in Direct3D 11/12),
    // shared memory v2.21+, in microseconds like NVAPI's latency report. Zero = the game gives no markers.
    public sealed record ReflexFrame(ulong InputSample, ulong SimStart, ulong GpuRenderEnd, uint GpuActiveUs, uint GpuFrameUs)
    {
        // From the moment the game reads input (or starts the frame) until the GPU finishes it; the screen comes after.
        public double? PcLatencyMs
        {
            get
            {
                var start = InputSample > 0 ? InputSample : SimStart;
                return start > 0 && GpuRenderEnd > start && GpuRenderEnd - start < 1_000_000 ? (GpuRenderEnd - start) / 1000.0 : null;
            }
        }
        // Share of the GPU's frame time spent on this game's frame (the "GPU busy" idea of PresentMon).
        public double? GpuBusyPercent => GpuFrameUs > 0 && GpuActiveUs > 0 && GpuActiveUs <= GpuFrameUs * 1.05 ? Math.Min(100, 100.0 * GpuActiveUs / GpuFrameUs) : null;
        public bool Empty => GpuRenderEnd == 0 && GpuActiveUs == 0;
    }
    // Offsets of the v2.21 fields inside an app entry (MSVC default packing, as RTSSSharedMemory.h is compiled). The
    // header's own offset of arrPerfCounters (dwProcessPerfCountersArrOffset, at 76) must be PerfCountersOffset: that
    // proves the layout before anything is read, so a future RTSS with a different layout gives "unavailable".
    public const int InputSampleOffset = 9232, SimStartOffset = 9240, GpuRenderEndOffset = 9328, GpuActiveOffset = 9336, GpuFrameOffset = 9340, PerfCountersOffset = 9344;
    public static ReflexFrame? ReadReflex(int pid, string mapName = DefaultMap)
    {
        try
        {
            using var map = MemoryMappedFile.OpenExisting(mapName, MemoryMappedFileRights.Read);
            using var view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            if (view.ReadUInt32(0) != Signature || view.ReadUInt32(4) < 0x00020015 || view.ReadUInt32(76) != PerfCountersOffset) return null;
            uint entrySize = view.ReadUInt32(8), arrOffset = view.ReadUInt32(12), arrSize = view.ReadUInt32(16);
            if (entrySize < PerfCountersOffset || arrSize > 1024) return null;
            for (var i = 0u; i < arrSize; i++)
            {
                long at = arrOffset + i * (long)entrySize;
                if (at + PerfCountersOffset > view.Capacity) break;
                if (view.ReadInt32(at) != pid) continue;
                var frame = new ReflexFrame(view.ReadUInt64(at + InputSampleOffset), view.ReadUInt64(at + SimStartOffset), view.ReadUInt64(at + GpuRenderEndOffset),
                    view.ReadUInt32(at + GpuActiveOffset), view.ReadUInt32(at + GpuFrameOffset));
                return frame.Empty ? null : frame;
            }
            return null;
        }
        catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or IOException) { return null; }
    }

    // Process ID of the app entry at this index (the frametime pipe names apps by index), or 0.
    public static int ProcessAt(uint index, string mapName = DefaultMap)
    {
        try
        {
            using var map = MemoryMappedFile.OpenExisting(mapName, MemoryMappedFileRights.Read);
            using var view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            if (view.ReadUInt32(0) != Signature) return 0;
            uint entrySize = view.ReadUInt32(8), arrOffset = view.ReadUInt32(12), arrSize = view.ReadUInt32(16);
            if (entrySize < 284 || index >= arrSize || arrSize > 1024) return 0;
            long at = arrOffset + index * (long)entrySize;
            return at + 4 <= view.Capacity ? view.ReadInt32(at) : 0;
        }
        catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or IOException) { return 0; }
    }

    // The mapping exists (with RTSS's signature) only while RivaTuner is running: no process list, no admin.
    public static bool IsAvailable(string mapName = DefaultMap)
    {
        try
        {
            using var map = MemoryMappedFile.OpenExisting(mapName, MemoryMappedFileRights.Read);
            using var view = map.CreateViewAccessor(0, 4, MemoryMappedFileAccess.Read);
            return view.ReadUInt32(0) == Signature;
        }
        catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or IOException) { return false; }
    }

    // The 3D API of a running game RTSS is tracking ("Direct3D 12", "Vulkan"...), matched by executable name; null when unknown.
    public static string? ApiOf(string exe, string mapName = DefaultMap) =>
        Read(mapName).Where(a => string.Equals(Path.GetFileName(a.Name), exe, StringComparison.OrdinalIgnoreCase)).Select(a => a.Api).FirstOrDefault(a => a is not null);

    public static bool IsTracking(int pid, string mapName = DefaultMap) => Read(mapName).Any(a => a.ProcessId == pid && a.Time0 != 0);
}

// Uses RTSS when it is tracking the target process (no UAC prompt); otherwise falls back to the given PresentMon provider.
// Every frame comes through RivaTuner's frametime pipe; if the pipe gives nothing, the shared memory is polled instead
// (then the 1% low is approximate, noted in Diagnostics). Reflex timings (GPU busy, PC latency) are read alongside.
[SupportedOSPlatform("windows")]
public sealed class RtssPreferredCaptureProvider(IFrameCaptureProvider fallback, TimeSpan? pollInterval = null, string mapName = RtssSharedMemory.DefaultMap,
    RtssFrametimeListener? listener = null, TimeSpan? firstFrameWait = null) : IFrameCaptureProvider
{
    private readonly TimeSpan poll = pollInterval ?? TimeSpan.FromMilliseconds(10);
    private readonly RtssFrametimeListener pipe = listener ?? RtssFrametimeListener.Shared;

    public async Task<BenchmarkSession> CaptureAsync(CaptureRequest request, CancellationToken stop)
    {
        var pid = request.Target.Pid;
        if (!RtssSharedMemory.IsTracking(pid, mapName)) return await fallback.CaptureAsync(request, stop);
        var started = DateTimeOffset.UtcNow;
        using var reflexStop = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var reflex = Task.Run(() => SampleReflexAsync(pid, reflexStop.Token));
        FrameStatistics? stats; int count; string algorithm, diagnostics;
        try
        {
            // The pipe only has frames of games opened after Lotus started listening; otherwise shared memory.
            var piped = await pipe.CollectAsync(pid, TimeSpan.FromSeconds(request.Seconds), firstFrameWait ?? TimeSpan.FromSeconds(2), stop);
            if (piped is { Count: > 0 })
            {
                (stats, count, algorithm, diagnostics) = (Statistics(piped), piped.Count, "rtss-pipe-v1", "RTSS frametime pipe: every frame.");
            }
            else
            {
                var polled = await PollAsync(pid, request.Seconds, stop);
                (stats, count, algorithm, diagnostics) = (polled.Stats, polled.Count, "rtss-poll-v1", "RTSS polled frametime: 1% low is approximate.");
            }
        }
        finally { reflexStop.Cancel(); }
        var (busy, latency) = await reflex;
        var extras = new FrameExtras(busy, latency, RtssSharedMemory.Read(mapName).FirstOrDefault(a => a.ProcessId == pid)?.Api);
        var data = new ParsedCapture("RTSS", 0, 0, count, [], stats, algorithm == "rtss-pipe-v1" ? "rtss-pipe" : "rtss-polled", false);
        return new(Guid.NewGuid(), request.Target, started, (DateTimeOffset.UtcNow - started).TotalSeconds, stats is null ? "InsufficientData" : "Completed",
            "RTSS shared memory", algorithm, "", "", request.Conditions, request.HardwareJson, data, diagnostics) { Extras = extras };
    }

    // Same definitions as the benchmark (1% low = mean of the slowest 1%, spikes above max(50 ms, 3x median)), with
    // a lower minimum so a 30 s check of a 30 FPS game still counts.
    public static FrameStatistics? Statistics(IReadOnlyList<double> frameTimesMs)
    {
        var ordered = frameTimesMs.Where(t => double.IsFinite(t) && t > 0).Order().ToArray();
        var sum = ordered.Sum();
        if (ordered.Length < 60 || sum < 5000) return null;
        double Quantile(double p) => ordered[Math.Max(0, (int)Math.Ceiling(ordered.Length * p) - 1)];
        var median = Quantile(.5);
        var threshold = Math.Max(50, 3 * median);
        var tail = (int)Math.Ceiling(ordered.Length * .01);
        return new(ordered.Length, sum / 1000, 1000 * ordered.Length / sum, 1000 / ordered.TakeLast(tail).Average(),
            Quantile(.95), Quantile(.99), median, ordered.Count(v => v > threshold), threshold);
    }

    private async Task<(FrameStatistics? Stats, int Count)> PollAsync(int pid, int seconds, CancellationToken stop)
    {
        var end = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(seconds);
        var frameTimes = new List<double>(); long frames = 0; double windowMs = 0; uint lastTime1 = 0;
        while (DateTimeOffset.UtcNow < end)
        {
            stop.ThrowIfCancellationRequested();
            if (RtssSharedMemory.Read(mapName).FirstOrDefault(a => a.ProcessId == pid) is { } s)
            {
                if (s.FrameTimeMicroseconds > 0) frameTimes.Add(s.FrameTimeMicroseconds / 1000.0);
                if (s.Time1 != lastTime1 && s.Time1 > s.Time0) { frames += s.Frames; windowMs += s.Time1 - s.Time0; lastTime1 = s.Time1; }
            }
            await Task.Delay(poll, stop);
        }
        if (windowMs <= 0 || frameTimes.Count < 20) return (null, frameTimes.Count);
        var ordered = frameTimes.OrderBy(t => t).ToList();
        var tail = Math.Max(1, ordered.Count / 100);
        return (new(frameTimes.Count, windowMs / 1000, 1000.0 * frames / windowMs, 1000 / ordered.TakeLast(tail).Average(),
            ordered[(int)(ordered.Count * 0.95)], ordered[(int)(ordered.Count * 0.99)], ordered[ordered.Count / 2], 0, 0), frameTimes.Count);
    }

    // Medians over distinct frames (a new GPU end time = a new frame); null when the game gives no Reflex markers.
    private async Task<(double? Busy, double? Latency)> SampleReflexAsync(int pid, CancellationToken stop)
    {
        var busy = new List<double>(); var latency = new List<double>(); ulong last = 0;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                if (RtssSharedMemory.ReadReflex(pid, mapName) is { } f && f.GpuRenderEnd != last)
                {
                    last = f.GpuRenderEnd;
                    if (f.GpuBusyPercent is { } b) busy.Add(b);
                    if (f.PcLatencyMs is { } l) latency.Add(l);
                }
                await Task.Delay(50, stop);
            }
        }
        catch (OperationCanceledException) { }
        static double? Median(List<double> v) => v.Count < 10 ? null : v.Order().ElementAt(v.Count / 2);
        return (Median(busy), Median(latency));
    }
}
