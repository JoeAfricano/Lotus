using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lotus.Core.Telemetry;
using Microsoft.Win32;

namespace Lotus.Infrastructure.Telemetry;

[SupportedOSPlatform("windows")]
public sealed class WindowsHardwareTelemetryProvider : IHardwareTelemetryProvider
{
    private readonly CpuUsageCalculator calculator = new();
    private string? model;
    private int? cores, logical;
    private bool inventoryRead;
    private bool multipleGroups;
    public void Reset() => calculator.Reset();

    public ValueTask<HardwareTelemetrySnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!inventoryRead)
        {
            ReadInventory();
            inventoryRead = true;
        }
        var monotonic = Stopwatch.GetElapsedTime(0);
        var utc = DateTimeOffset.UtcNow;
        var cpu = ReadCpu(monotonic);
        var memory = ReadMemory();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new HardwareTelemetrySnapshot(utc, monotonic, cpu, memory));
    }
    private void ReadInventory()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", writable: false);
            model = (key?.GetValue("ProcessorNameString") as string)?.Trim();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { model = null; }
        var count = Native.GetActiveProcessorCount(ushort.MaxValue);
        logical = count is > 0 and < int.MaxValue ? (int)count : null;
        multipleGroups = Native.GetActiveProcessorGroupCount() > 1;
        uint size = 0;
        Native.GetLogicalProcessorInformationEx(0, null, ref size);
        if (Marshal.GetLastWin32Error() != 122 || size is < 8 or > 4_194_304) return;
        var bytes = new byte[size];
        if (!Native.GetLogicalProcessorInformationEx(0, bytes, ref size)) return;
        int found = 0;
        for (var offset = 0; offset + 8 <= size;)
        {
            var relationship = BitConverter.ToInt32(bytes, offset);
            var length = BitConverter.ToInt32(bytes, offset + 4);
            if (length < 8 || offset + (long)length > size) return;
            if (relationship == 0) found++;
            offset += length;
        }
        cores = found > 0 ? found : null;
    }
    private CpuTelemetrySnapshot ReadCpu(TimeSpan monotonic)
    {
        ulong? process = null;
        var processStatus = TelemetryStatus.Available;
        if (Native.GetProcessTimes(new IntPtr(-1), out _, out _, out var pk, out var pu))
            process = checked(pk.Ticks + pu.Ticks);
        else processStatus = NativeStatus();
        Reading<double> total, own;
        if (Native.GetSystemTimes(out var idle, out var kernel, out var user))
            (total, own) = calculator.Calculate(new(idle.Ticks, kernel.Ticks, user.Ticks, process, monotonic), logical ?? 0);
        else
        {
            var status = NativeStatus(); calculator.Reset();
            total = Reading<double>.Missing(status, "GetSystemTimes");
            own = Reading<double>.Missing(status, "GetProcessTimes");
        }
        if (multipleGroups) total = Reading<double>.Missing(TelemetryStatus.Unsupported, "GetSystemTimes: processor groups");
        if (process is null) own = Reading<double>.Missing(processStatus, "GetProcessTimes");
        return new(model, cores, logical, total, own, Reading<double>.Missing(TelemetryStatus.Unsupported, "No validated effective-frequency source"));
    }
    private static MemoryTelemetrySnapshot ReadMemory()
    {
        var memory = new Native.MemoryStatus { Length = (uint)Marshal.SizeOf<Native.MemoryStatus>() };
        var success = Native.GlobalMemoryStatusEx(ref memory);
        var status = success ? TelemetryStatus.Available : NativeStatus();
        var used = success ? TelemetryMath.UsedPhysicalBytes(memory.TotalPhysical, memory.AvailablePhysical) : null;
        double? percent = used is { } bytesUsed ? TelemetryMath.Percent(bytesUsed, memory.TotalPhysical) : null;
        if (success && percent is null) status = TelemetryStatus.Invalid;
        Reading<ulong> Physical(ulong bytes) => percent is not null ? Reading<ulong>.Available(bytes, "GlobalMemoryStatusEx") : Reading<ulong>.Missing(status, "GlobalMemoryStatusEx");
        var counters = new Native.ProcessMemoryCounters { Size = (uint)Marshal.SizeOf<Native.ProcessMemoryCounters>() };
        var processSuccess = Native.K32GetProcessMemoryInfo(new IntPtr(-1), ref counters, counters.Size);
        var processStatus = processSuccess ? TelemetryStatus.Available : NativeStatus();
        Reading<ulong> Process(nuint bytes) => processSuccess ? Reading<ulong>.Available((ulong)bytes, "K32GetProcessMemoryInfo") : Reading<ulong>.Missing(processStatus, "K32GetProcessMemoryInfo");
        return new(Physical(memory.TotalPhysical), Physical(memory.AvailablePhysical),
            Physical(memory.TotalPhysical >= memory.AvailablePhysical ? memory.TotalPhysical - memory.AvailablePhysical : 0),
            percent is { } p ? Reading<double>.Available(p, "GlobalMemoryStatusEx") : Reading<double>.Missing(status, "GlobalMemoryStatusEx"),
            Process(counters.WorkingSet), Process(counters.PrivateUsage));
    }
    private static TelemetryStatus NativeStatus() => Marshal.GetLastWin32Error() == 5 ? TelemetryStatus.AccessDenied : TelemetryStatus.Unavailable;

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct FileTime { public uint Low, High; public readonly ulong Ticks => ((ulong)High << 32) | Low; }
        [StructLayout(LayoutKind.Sequential)] public struct MemoryStatus
        {
            public uint Length, Load;
            public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
        }
        [StructLayout(LayoutKind.Sequential)] public struct ProcessMemoryCounters
        {
            public uint Size, PageFaultCount;
            public nuint PeakWorkingSet, WorkingSet, QuotaPeakPagedPool, QuotaPagedPool, QuotaPeakNonPagedPool, QuotaNonPagedPool, PagefileUsage, PeakPagefileUsage, PrivateUsage;
        }
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetProcessTimes(IntPtr process, out FileTime created, out FileTime exited, out FileTime kernel, out FileTime user);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool K32GetProcessMemoryInfo(IntPtr process, ref ProcessMemoryCounters counters, uint size);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetLogicalProcessorInformationEx(int relationship, [Out] byte[]? buffer, ref uint length);
        [DllImport("kernel32.dll")] public static extern uint GetActiveProcessorCount(ushort group);
        [DllImport("kernel32.dll")] public static extern ushort GetActiveProcessorGroupCount();
    }
}
