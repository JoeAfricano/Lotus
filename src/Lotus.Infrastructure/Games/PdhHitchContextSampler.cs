using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lotus.Core.Games;

namespace Lotus.Infrastructure.Games;

// One sample per second during "Por que está travando?": hard page-ins (\Memory\Pages Input/sec), the game's own read
// bytes (GetProcessIoCounters, limited query access) and the busiest GPU's 3D load. Read-only, no administrator.
[SupportedOSPlatform("windows")]
public sealed class PdhHitchContextSampler : IHitchContextSampler
{
    private const uint FmtDouble = 0x200;
    [StructLayout(LayoutKind.Sequential)] private struct CounterValue { public uint Status; public double Value; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out CounterValue value);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    public async Task<IReadOnlyList<HitchSecond>> SampleAsync(int pid, TimeSpan duration, CancellationToken token)
    {
        var list = new List<HitchSecond>();
        if (PdhOpenQuery(null, IntPtr.Zero, out var query) != 0) return list;
        var process = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        try
        {
            var hasPages = PdhAddEnglishCounter(query, @"\Memory\Pages Input/sec", IntPtr.Zero, out var pages) == 0;
            var hasGpu = PdhAddEnglishCounter(query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out var gpu) == 0;
            PdhCollectQueryData(query);
            var lastRead = Reads(process); var lastAt = DateTime.UtcNow;
            var end = lastAt + duration;
            while (DateTime.UtcNow < end)
            {
                await Task.Delay(1000, token);
                PdhCollectQueryData(query);
                var now = DateTime.UtcNow; var read = Reads(process);
                var mbps = read is { } r && lastRead is { } l && r >= l ? (r - l) / 1048576.0 / Math.Max(0.2, (now - lastAt).TotalSeconds) : 0;
                lastRead = read; lastAt = now;
                double pageIns = hasPages && PdhGetFormattedCounterValue(pages, FmtDouble, out _, out var v) == 0 && v.Status == 0 ? v.Value : 0;
                list.Add(new(pageIns, mbps, hasGpu ? PdhGpuLoadSampler.Read(gpu) : null));
            }
            return list;
        }
        catch (OperationCanceledException) { return list; }
        finally { PdhCloseQuery(query); if (process != IntPtr.Zero) CloseHandle(process); }
    }

    private static ulong? Reads(IntPtr process) => process != IntPtr.Zero && GetProcessIoCounters(process, out var c) ? c.ReadBytes : null;
}
