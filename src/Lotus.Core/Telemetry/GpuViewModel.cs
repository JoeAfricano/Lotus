using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;

namespace Lotus.Core.Telemetry;

public readonly record struct SensorHistoryPoint(TimeSpan Time, double? Value);

public sealed class GpuViewModel : ObservableObject
{
    private readonly ITextCatalog text;
    private GpuTelemetrySnapshot snapshot;
    private readonly List<SensorHistoryPoint> utilizationHistory = [], memoryHistory = [];
    public IReadOnlyList<SensorHistoryPoint> UtilizationHistory => utilizationHistory;
    public IReadOnlyList<SensorHistoryPoint> MemoryHistory => memoryHistory;
    public string Id => snapshot.Id;
    public string Name => snapshot.Name;
    public string Identity => snapshot.Vendor + " · " + Id;
    public string Utilization => Format(snapshot.Get(GpuSensor.Utilization));
    public string Temperature => Format(snapshot.Get(GpuSensor.Temperature));
    public string Power => Format(snapshot.Get(GpuSensor.Power));
    public string MemorySummary => MemoryUsed + " / " + MemoryTotal;
    public string MemoryPercent => Format(snapshot.Get(GpuSensor.MemoryPercent));
    public string MemoryTotal => Format(snapshot.Get(GpuSensor.MemoryTotal));
    public string MemoryUsed => Format(snapshot.Get(GpuSensor.MemoryUsed));
    public string MemoryFree => Format(snapshot.Get(GpuSensor.MemoryFree));
    public string MemoryReserved => Format(snapshot.Get(GpuSensor.MemoryReserved));
    public string UtilizationDetail => Detail(snapshot.Get(GpuSensor.Utilization));
    public string MemoryDetail => Detail(snapshot.Get(GpuSensor.MemoryUsed));
    public ITextCatalog Text => text;
    public IReadOnlyList<GpuSensorViewModel> Sensors { get; }
    public event EventHandler? Updated;
    public GpuViewModel(GpuTelemetrySnapshot snapshot, ITextCatalog text)
    {
        this.snapshot = snapshot; this.text = text;
        Sensors = new[] { GpuSensor.Temperature, GpuSensor.CoreClock, GpuSensor.MemoryClock, GpuSensor.Power,
            GpuSensor.PowerLimit, GpuSensor.Fan, GpuSensor.ClockEventReasons }.Select(s => new GpuSensorViewModel(s, this)).ToArray();
    }
    public void Update(GpuTelemetrySnapshot value, TimeSpan time)
    {
        snapshot = value;
        Add(utilizationHistory, time, snapshot.Get(GpuSensor.Utilization));
        Add(memoryHistory, time, snapshot.Get(GpuSensor.MemoryPercent));
        RefreshLanguage();
    }
    public void RefreshLanguage()
    {
        OnPropertyChanged(string.Empty);
        foreach (var sensor in Sensors) sensor.Refresh();
        Updated?.Invoke(this, EventArgs.Empty);
    }
    private static void Add(List<SensorHistoryPoint> history, TimeSpan time, SensorReading? reading)
    {
        if (history.Count > 0 && history[^1].Time >= time) return;
        history.RemoveAll(p => time - p.Time >= TimeSpan.FromSeconds(60));
        history.Add(new(time, reading is { Status: TelemetryStatus.Available, Value: >= 0 and <= 100 } ? reading.Value : null));
        if (history.Count > 60) history.RemoveRange(0, history.Count - 60);
    }
    internal SensorReading? Get(GpuSensor sensor) => snapshot.Get(sensor);
    internal string Format(SensorReading? reading)
    {
        if (reading is not { Status: TelemetryStatus.Available, Value: { } value } || !double.IsFinite(value) || value < 0) return "—";
        var culture = CultureInfo.GetCultureInfo(text.Language);
        if (reading.Unit == SensorUnit.Flags)
        {
            var bits = (ulong)value;
            // Only explicit thermal/power bits are classified. HW slowdown alone is ambiguous.
            var flags = new List<string>();
            if ((bits & 0x60) != 0) flags.Add(text["ThermalLimitReported"]);
            if ((bits & 0x84) != 0) flags.Add(text["PowerLimitReported"]);
            return $"0x{bits:X} · " + (flags.Count > 0 ? string.Join(" · ", flags) : text["NoThermalPowerFlag"]);
        }
        return reading.Unit switch
        {
            SensorUnit.Bytes => (value / 1_073_741_824d).ToString("F2", culture) + " GiB",
            SensorUnit.MHz => value.ToString("F0", culture) + " MHz",
            SensorUnit.Celsius => value.ToString("F0", culture) + " °C",
            SensorUnit.Watts => value.ToString("F2", culture) + " W",
            _ => value.ToString("F1", culture) + " %"
        };
    }
    internal string Detail(SensorReading? reading) => reading is null ? text["StatusUnavailable"] :
        text["Status" + reading.Status] + " · " + reading.Source + " · " + reading.CapturedAt.ToLocalTime().ToString("T", CultureInfo.GetCultureInfo(text.Language)) +
        (reading.NativeError is { } error ? $" · NVML {error}" : "");
    public sealed class GpuSensorViewModel(GpuSensor sensor, GpuViewModel owner) : ObservableObject
    {
        public string Label => owner.Text["Gpu" + sensor];
        public string Value => owner.Format(owner.Get(sensor));
        public string Detail => owner.Detail(owner.Get(sensor));
        public void Refresh() => OnPropertyChanged(string.Empty);
    }
}
