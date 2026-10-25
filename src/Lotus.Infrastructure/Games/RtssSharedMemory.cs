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
    public sealed record AppSample(int ProcessId, string Name, uint Time0, uint Time1, uint Frames, uint FrameTimeMicroseconds);

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
                list.Add(new(pid, text, view.ReadUInt32(at + 268), view.ReadUInt32(at + 272), view.ReadUInt32(at + 276), view.ReadUInt32(at + 280)));
            }
            return list;
        }
        catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or IOException) { return []; } // RTSS not running
    }

    public static bool IsTracking(int pid, string mapName = DefaultMap) => Read(mapName).Any(a => a.ProcessId == pid && a.Time0 != 0);
}

// Uses RTSS when it is tracking the target process (no UAC prompt); otherwise falls back to the given PresentMon provider.
// RTSS gives per-second frame counts and a polled frametime, so its 1% low is an approximation (noted in Diagnostics).
[SupportedOSPlatform("windows")]
public sealed class RtssPreferredCaptureProvider(IFrameCaptureProvider fallback, TimeSpan? pollInterval = null, string mapName = RtssSharedMemory.DefaultMap) : IFrameCaptureProvider
{
    private readonly TimeSpan poll = pollInterval ?? TimeSpan.FromMilliseconds(10);

    public async Task<BenchmarkSession> CaptureAsync(CaptureRequest request, CancellationToken stop)
    {
        if (!RtssSharedMemory.IsTracking(request.Target.Pid, mapName)) return await fallback.CaptureAsync(request, stop);
        var started = DateTimeOffset.UtcNow; var end = started + TimeSpan.FromSeconds(request.Seconds);
        var frameTimes = new List<double>(); long frames = 0; double windowMs = 0; uint lastTime1 = 0;
        while (DateTimeOffset.UtcNow < end)
        {
            stop.ThrowIfCancellationRequested();
            if (RtssSharedMemory.Read(mapName).FirstOrDefault(a => a.ProcessId == request.Target.Pid) is { } s)
            {
                if (s.FrameTimeMicroseconds > 0) frameTimes.Add(s.FrameTimeMicroseconds / 1000.0);
                if (s.Time1 != lastTime1 && s.Time1 > s.Time0) { frames += s.Frames; windowMs += s.Time1 - s.Time0; lastTime1 = s.Time1; }
            }
            await Task.Delay(poll, stop);
        }
        FrameStatistics? stats = null;
        if (windowMs > 0 && frameTimes.Count >= 20)
        {
            var ordered = frameTimes.OrderBy(t => t).ToList();
            var tail = Math.Max(1, ordered.Count / 100);
            stats = new(frameTimes.Count, windowMs / 1000, 1000.0 * frames / windowMs, 1000 / ordered.TakeLast(tail).Average(),
                ordered[(int)(ordered.Count * 0.95)], ordered[(int)(ordered.Count * 0.99)], ordered[ordered.Count / 2], 0, 0);
        }
        var data = new ParsedCapture("RTSS", 0, 0, frameTimes.Count, [], stats, "rtss-polled", false);
        return new(Guid.NewGuid(), request.Target, started, (DateTimeOffset.UtcNow - started).TotalSeconds, stats is null ? "InsufficientData" : "Completed",
            "RTSS shared memory", "rtss-poll-v1", "", "", request.Conditions, request.HardwareJson, data, "RTSS polled frametime: 1% low is approximate.");
    }
}
