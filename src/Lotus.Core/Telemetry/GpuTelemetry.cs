namespace Lotus.Core.Telemetry;

public enum SensorUnit { Percent, MHz, Celsius, Watts, Bytes, Flags }
public enum GpuSensor { Utilization, CoreClock, MemoryClock, Temperature, Power, PowerLimit, Fan,
    MemoryTotal, MemoryUsed, MemoryFree, MemoryReserved, MemoryPercent, ClockEventReasons }

// Timestamp is acquisition time, not a claim about the driver's internal sampling clock.
public sealed record SensorReading(double? Value, SensorUnit Unit, TelemetryStatus Status,
    string Source, DateTimeOffset CapturedAt, int? NativeError = null)
{
    public static SensorReading Create(double value, SensorUnit unit, string source, DateTimeOffset time, double maximum = double.MaxValue) =>
        double.IsFinite(value) && value >= 0 && value <= maximum
            ? new(value, unit, TelemetryStatus.Available, source, time)
            : new(null, unit, TelemetryStatus.Invalid, source, time);
}

public sealed record GpuTelemetrySnapshot(string Id, string Name, string Vendor,
    IReadOnlyDictionary<GpuSensor, SensorReading> Sensors)
{
    public SensorReading? Get(GpuSensor sensor) => Sensors.GetValueOrDefault(sensor);
}

public sealed record GpuTelemetryResult(IReadOnlyList<GpuTelemetrySnapshot> Devices, TelemetryStatus Status, string Source)
{
    public static GpuTelemetryResult Missing(TelemetryStatus status) => new(Array.Empty<GpuTelemetrySnapshot>(), status, "NVIDIA NVML");
}

// Vendor adapters own no polling loop. Stop runs on the collection worker after its final read.
public interface IGpuTelemetryProvider
{
    GpuTelemetryResult Read(CancellationToken token);
    void Stop();
}
public interface IMonitoringSessionProvider { void Stop(); }

public sealed class CompositeHardwareTelemetryProvider(IHardwareTelemetryProvider system, IGpuTelemetryProvider gpu,
    Action<Exception>? diagnostic = null) : IHardwareTelemetryProvider, IMonitoringSessionProvider
{
    private bool failed, systemFailed;
    public void Reset() => system.Reset();
    public async ValueTask<HardwareTelemetrySnapshot> ReadAsync(CancellationToken token)
    {
        HardwareTelemetrySnapshot snapshot;
        try { snapshot = await system.ReadAsync(token).ConfigureAwait(false); systemFailed = false; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            if (!systemFailed) diagnostic?.Invoke(ex);
            systemFailed = true;
            snapshot = HardwareTelemetrySnapshot.Missing(DateTimeOffset.UtcNow, System.Diagnostics.Stopwatch.GetElapsedTime(0), TelemetryStatus.Error);
        }
        token.ThrowIfCancellationRequested();
        GpuTelemetryResult result;
        try { result = gpu.Read(token); failed = false; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            if (!failed) diagnostic?.Invoke(ex);
            failed = true;
            result = GpuTelemetryResult.Missing(TelemetryStatus.Error);
            try { gpu.Stop(); }
            catch (Exception cleanupError) { diagnostic?.Invoke(cleanupError); }
        }
        token.ThrowIfCancellationRequested();
        return snapshot with { Gpu = result };
    }
    public void Stop() => gpu.Stop();
}
