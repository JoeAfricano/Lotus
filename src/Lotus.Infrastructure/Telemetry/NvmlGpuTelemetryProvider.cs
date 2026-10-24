using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Lotus.Core.Telemetry;

namespace Lotus.Infrastructure.Telemetry;

// Read-only bindings written for Lotus. No NVIDIA binary/header is redistributed.
// Serial access is guaranteed by HardwareMonitoringService, including Stop.
public sealed class NvmlGpuTelemetryProvider : IGpuTelemetryProvider
{
    private nint library;
    private bool initialized;
    private long retryAt;
    private TelemetryStatus initializationStatus = TelemetryStatus.Unavailable;
    private readonly Dictionary<string, Delegate?> exports = [];
    private readonly Dictionary<nint, (string Id, string Name)> identities = [];

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Simple();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Count(out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Handle(uint index, out nint device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeviceString(nint device, StringBuilder text, uint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int UintValue(nint device, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DomainValue(nint device, uint domain, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Utilization(nint device, out UtilizationNative value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Memory(nint device, ref MemoryNative value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Reasons(nint device, out ulong value);
    [StructLayout(LayoutKind.Sequential)] private struct UtilizationNative { public uint Gpu, Memory; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryNative { public uint Version; public ulong Total, Reserved, Free, Used; }

    private T? Function<T>(string name) where T : Delegate
    {
        if (!exports.TryGetValue(name, out var value))
        {
            value = NativeLibrary.TryGetExport(library, name, out var address) ? Marshal.GetDelegateForFunctionPointer<T>(address) : null;
            exports.Add(name, value);
        }
        return (T?)value;
    }
    private bool Initialize()
    {
        if (initialized) return true;
        if (Stopwatch.GetTimestamp() < retryAt) return false;
        retryAt = Stopwatch.GetTimestamp() + 30 * Stopwatch.Frequency;
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) { initializationStatus = TelemetryStatus.Unsupported; return false; }
        // Absolute driver locations only: never resolve nvml.dll from the working directory/PATH.
        var paths = new[] { Path.Combine(Environment.SystemDirectory, "nvml.dll"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll") };
        foreach (var path in paths)
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out library)) break;
        if (library == 0) { initializationStatus = TelemetryStatus.Unavailable; return false; }
        try
        {
            var code = Function<Simple>("nvmlInit_v2")?.Invoke() ?? 13;
            initializationStatus = Status(code);
            initialized = code == 0;
            if (!initialized) ReleaseLibrary();
            return initialized;
        }
        catch { ReleaseLibrary(); throw; }
    }
    public GpuTelemetryResult Read(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Initialize()) return GpuTelemetryResult.Missing(initializationStatus);
        var getCount = Function<Count>("nvmlDeviceGetCount_v2");
        uint count = 0;
        var countCode = getCount is null ? 13 : getCount(out count);
        if (countCode != 0)
        {
            var status = Status(countCode);
            Stop(); initializationStatus = status; retryAt = Stopwatch.GetTimestamp() + 30 * Stopwatch.Frequency;
            return GpuTelemetryResult.Missing(status);
        }
        if (count > 64) return GpuTelemetryResult.Missing(TelemetryStatus.Invalid);
        var devices = new List<GpuTelemetrySnapshot>();
        var seenHandles = new HashSet<nint>();
        var getHandle = Function<Handle>("nvmlDeviceGetHandleByIndex_v2");
        var overall = count == 0 ? TelemetryStatus.Unavailable : TelemetryStatus.Available;
        for (uint i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            nint handle = 0;
            var code = getHandle is null ? 13 : getHandle(i, out handle);
            if (code != 0) { overall = Status(code); continue; }
            seenHandles.Add(handle);
            if (!identities.TryGetValue(handle, out var identity))
            {
                string? ReadString(string function)
                {
                    var text = new StringBuilder(256);
                    return Function<DeviceString>(function)?.Invoke(handle, text, 256) == 0 ? text.ToString() : null;
                }
                identity = (ReadString("nvmlDeviceGetUUID") ?? $"NVML-index-{i}", ReadString("nvmlDeviceGetName") ?? $"NVIDIA GPU {i}");
                identities[handle] = identity;
            }
            var metrics = new Dictionary<GpuSensor, SensorReading>();
            void Put(GpuSensor key, double value, int error, SensorUnit unit, string function, double max = double.MaxValue)
            {
                var time = DateTimeOffset.UtcNow;
                metrics[key] = error == 0 ? SensorReading.Create(value, unit, "NVML / " + function, time, max)
                    : new(null, unit, Status(error), "NVML / " + function, time, error);
            }
            void ReadUint(GpuSensor key, string function, SensorUnit unit, double scale = 1, double max = double.MaxValue)
            {
                token.ThrowIfCancellationRequested();
                uint value = 0;
                var f = Function<UintValue>(function);
                var error = f is null ? 13 : f(handle, out value);
                Put(key, value * scale, error, unit, function, max);
            }
            void ReadDomain(GpuSensor key, string function, uint domain, SensorUnit unit)
            {
                token.ThrowIfCancellationRequested();
                uint value = 0;
                var f = Function<DomainValue>(function);
                var error = f is null ? 13 : f(handle, domain, out value);
                Put(key, value, error, unit, function, unit == SensorUnit.Celsius ? 250 : 100_000);
            }
            var utilization = default(UtilizationNative);
            var util = Function<Utilization>("nvmlDeviceGetUtilizationRates");
            var utilCode = util is null ? 13 : util(handle, out utilization);
            Put(GpuSensor.Utilization, utilization.Gpu, utilCode, SensorUnit.Percent, "nvmlDeviceGetUtilizationRates", 100);
            ReadDomain(GpuSensor.CoreClock, "nvmlDeviceGetClockInfo", 0, SensorUnit.MHz);
            ReadDomain(GpuSensor.MemoryClock, "nvmlDeviceGetClockInfo", 2, SensorUnit.MHz);
            ReadDomain(GpuSensor.Temperature, "nvmlDeviceGetTemperature", 0, SensorUnit.Celsius);
            ReadUint(GpuSensor.Power, "nvmlDeviceGetPowerUsage", SensorUnit.Watts, 0.001, 10_000);
            ReadUint(GpuSensor.PowerLimit, "nvmlDeviceGetPowerManagementLimit", SensorUnit.Watts, 0.001, 10_000);
            // NVML reports intended fan speed, not tachometer RPM. It may exceed 100% on some devices.
            ReadUint(GpuSensor.Fan, "nvmlDeviceGetFanSpeed", SensorUnit.Percent);
            var memory = new MemoryNative { Version = (uint)Marshal.SizeOf<MemoryNative>() | (2u << 24) };
            var mem = Function<Memory>("nvmlDeviceGetMemoryInfo_v2");
            var memCode = mem is null ? 13 : mem(handle, ref memory);
            var valid = memory.Total > 0 && memory.Used <= memory.Total && memory.Free <= memory.Total && memory.Reserved <= memory.Total;
            // Preserve each field verbatim. Do not synthesize free = total - used: reserved is separate.
            foreach (var (key, value) in new[] { (GpuSensor.MemoryTotal, memory.Total), (GpuSensor.MemoryUsed, memory.Used),
                (GpuSensor.MemoryFree, memory.Free), (GpuSensor.MemoryReserved, memory.Reserved) })
            {
                Put(key, value, memCode, SensorUnit.Bytes, "nvmlDeviceGetMemoryInfo_v2");
                if (memCode == 0 && !valid) metrics[key] = metrics[key] with { Value = null, Status = TelemetryStatus.Invalid };
            }
            var percent = valid ? TelemetryMath.Percent(memory.Used, memory.Total) : null;
            Put(GpuSensor.MemoryPercent, percent ?? double.NaN, memCode, SensorUnit.Percent, "nvmlDeviceGetMemoryInfo_v2", 100);
            ulong reasons = 0;
            var reason = Function<Reasons>("nvmlDeviceGetCurrentClocksEventReasons");
            var reasonCode = reason is null ? 13 : reason(handle, out reasons);
            Put(GpuSensor.ClockEventReasons, reasons, reasonCode, SensorUnit.Flags, "nvmlDeviceGetCurrentClocksEventReasons", 9_007_199_254_740_991d);
            devices.Add(new(identity.Id, identity.Name, "NVIDIA", new ReadOnlyDictionary<GpuSensor, SensorReading>(metrics)));
        }
        foreach (var handle in identities.Keys.Where(h => !seenHandles.Contains(h)).ToArray()) identities.Remove(handle);
        token.ThrowIfCancellationRequested();
        return new(devices.AsReadOnly(), overall, "NVIDIA NVML");
    }
    public static TelemetryStatus Status(int code) => code switch
    {
        0 => TelemetryStatus.Available, 2 => TelemetryStatus.Invalid, 3 or 13 => TelemetryStatus.Unsupported,
        4 => TelemetryStatus.AccessDenied, 6 or 9 => TelemetryStatus.Unavailable, _ => TelemetryStatus.Error
    };
    public void Stop()
    {
        try { if (initialized) Function<Simple>("nvmlShutdown")?.Invoke(); }
        finally { initialized = false; ReleaseLibrary(); retryAt = 0; }
    }
    private void ReleaseLibrary()
    {
        exports.Clear(); identities.Clear();
        if (library != 0) { NativeLibrary.Free(library); library = 0; }
    }
}
