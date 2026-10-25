using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lotus.Core.Games;

namespace Lotus.Infrastructure.Games;

// Vendor-neutral GPU load from Windows performance counters: "\GPU Engine(*engtype_3D)\Utilization Percentage".
// Instances are summed per adapter (LUID); the busiest adapter is reported. Read-only, one sample per second.
[SupportedOSPlatform("windows")]
public sealed class PdhGpuLoadSampler : IGpuLoadSampler
{
    private const uint FmtDouble = 0x200, MoreData = 0x800007D2;
    [StructLayout(LayoutKind.Sequential)] private struct CounterValue { public uint Status; public double Value; }
    [StructLayout(LayoutKind.Sequential)] private struct CounterItem { public IntPtr Name; public CounterValue Value; }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);

    public async Task<double?> AverageAsync(TimeSpan duration, CancellationToken token)
    {
        if (PdhOpenQuery(null, IntPtr.Zero, out var query) != 0) return null;
        try
        {
            if (PdhAddEnglishCounter(query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out var counter) != 0) return null;
            PdhCollectQueryData(query);
            var samples = new List<double>();
            var end = DateTime.UtcNow + duration;
            while (DateTime.UtcNow < end)
            {
                await Task.Delay(1000, token);
                if (PdhCollectQueryData(query) == 0 && Read(counter) is { } v) samples.Add(v);
            }
            return samples.Count == 0 ? null : samples.Average();
        }
        finally { PdhCloseQuery(query); }
    }

    private static double? Read(IntPtr counter)
    {
        uint size = 0;
        if (PdhGetFormattedCounterArray(counter, FmtDouble, ref size, out _, IntPtr.Zero) != MoreData || size == 0) return null;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(counter, FmtDouble, ref size, out var count, buffer) != 0) return null;
            var perAdapter = new Dictionary<string, double>();
            var itemSize = Marshal.SizeOf<CounterItem>();
            for (var i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<CounterItem>(buffer + i * itemSize);
                if (item.Value.Status != 0) continue;
                var name = Marshal.PtrToStringUni(item.Name) ?? "";
                var luid = name.IndexOf("luid_", StringComparison.Ordinal) is var at and >= 0 ? name.Substring(at, Math.Min(26, name.Length - at)) : "";
                perAdapter[luid] = perAdapter.GetValueOrDefault(luid) + item.Value.Value;
            }
            return perAdapter.Count == 0 ? null : Math.Min(100, perAdapter.Values.Max());
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
