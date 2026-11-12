using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lotus.Core.Games;

namespace Lotus.Infrastructure.Games;

// CpuHeat from Windows performance counters, readable without administrator rights (no kernel driver, unlike tools
// that read the CPU's own sensors): "\Processor Information(_Total)\% Performance Limit" and the ACPI thermal zones
// ("\Thermal Zone Information(*)\Temperature", in kelvin). Readings outside 25–115 °C are ignored: many desktops expose a fixed zone (often 290 K ≈ 17 °C) that is not a CPU reading.
[SupportedOSPlatform("windows")]
public sealed class PdhCpuHeatSampler : ICpuHeatSampler, IDisposable
{
    private const uint FmtDouble = 0x200, MoreData = 0x800007D2;
    [StructLayout(LayoutKind.Sequential)] private struct CounterValue { public uint Status; public double Value; }
    [StructLayout(LayoutKind.Sequential)] private struct CounterItem { public IntPtr Name; public CounterValue Value; }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);

    private readonly object gate = new();
    private IntPtr query, limit, zones;
    private bool opened, failed;

    private bool Open()
    {
        if (opened) return true;
        if (failed) return false;
        try
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0) { failed = true; return false; }
            var hasLimit = PdhAddEnglishCounter(query, @"\Processor Information(_Total)\% Performance Limit", IntPtr.Zero, out limit) == 0;
            var hasZones = PdhAddEnglishCounter(query, @"\Thermal Zone Information(*)\Temperature", IntPtr.Zero, out zones) == 0;
            if (!hasLimit) limit = IntPtr.Zero;
            if (!hasZones) zones = IntPtr.Zero;
            if (!hasLimit && !hasZones) { PdhCloseQuery(query); query = IntPtr.Zero; failed = true; return false; }
            PdhCollectQueryData(query); // first collection primes the counters
            opened = true;
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { failed = true; return false; }
    }

    public CpuHeatSample? Sample()
    {
        lock (gate)
        {
            if (!Open() || PdhCollectQueryData(query) != 0) return null;
            var perf = limit != IntPtr.Zero ? Values(limit).Select(v => (double?)Math.Clamp(v, 0, 100)).FirstOrDefault() : null;
            var celsius = zones != IntPtr.Zero ? Values(zones).Select(k => k - 273.15).Where(c => c is >= 25 and <= 115).Select(c => (double?)c).DefaultIfEmpty(null).Max() : null;
            return perf is null && celsius is null ? null : new(perf, celsius);
        }
    }

    public async Task<CpuHeat?> MeasureAsync(TimeSpan duration, CancellationToken token)
    {
        var samples = new List<CpuHeatSample>();
        var end = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < end)
        {
            await Task.Delay(1000, token).ConfigureAwait(false);
            if (Sample() is { } s) samples.Add(s);
        }
        return CpuHeat.From(samples);
    }

    private static List<double> Values(IntPtr counter)
    {
        var list = new List<double>();
        uint size = 0;
        if (PdhGetFormattedCounterArray(counter, FmtDouble, ref size, out _, IntPtr.Zero) != MoreData || size == 0) return list;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(counter, FmtDouble, ref size, out var count, buffer) != 0) return list;
            var itemSize = Marshal.SizeOf<CounterItem>();
            for (var i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<CounterItem>(buffer + i * itemSize);
                if (item.Value.Status == 0) list.Add(item.Value.Value);
            }
            return list;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public void Dispose()
    {
        lock (gate) { if (opened) { PdhCloseQuery(query); opened = false; query = IntPtr.Zero; } }
    }
}
