using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;

namespace Lotus.Core.Telemetry;

// Presentation receives immutable snapshots; all notifications run on the UI dispatcher.
public sealed class MonitoringViewModel(HardwareMonitoringService service, ITextCatalog text, IUiDispatcher dispatcher) : ObservableObject
{
    private readonly object pendingGate = new();
    private HardwareTelemetrySnapshot? pending;
    private bool queued;
    private int generation;
    private IDisposable? lease;
    private HardwareTelemetrySnapshot? snapshot;
    public ITextCatalog Text => text;
    public TelemetryHistory History { get; } = new();
    public HardwareTelemetrySnapshot? Snapshot => snapshot;
    public bool IsActive => lease is not null;
    public string CpuName => snapshot?.Cpu.Model ?? text["StatusUnavailable"];
    public string CpuPercent => Percent(snapshot?.Cpu.TotalUtilizationPercent);
    public string ProcessCpu => Percent(snapshot?.Cpu.LotusUtilizationPercent);
    public string CpuStatus => Status(snapshot?.Cpu.TotalUtilizationPercent.Status);
    public string MemoryStatus => Status(snapshot?.Memory.UsedPhysicalPercent.Status);
    public string MemoryPercent => Percent(snapshot?.Memory.UsedPhysicalPercent);
    public string TotalMemory => Bytes(snapshot?.Memory.TotalPhysicalBytes, false);
    public string UsedMemory => Bytes(snapshot?.Memory.UsedPhysicalBytes, false);
    public string AvailableMemory => Bytes(snapshot?.Memory.AvailablePhysicalBytes, false);
    public string WorkingSet => Bytes(snapshot?.Memory.LotusWorkingSetBytes, true);
    public string PrivateCommit => Bytes(snapshot?.Memory.LotusPrivateCommitBytes, true);
    public string CoreCounts => text.Format("CoreCounts", snapshot?.Cpu.PhysicalCores?.ToString(Culture) ?? "—", snapshot?.Cpu.LogicalProcessors?.ToString(Culture) ?? "—");
    public string LastUpdate => snapshot is null ? text["WaitingSample"] : text.Format("LastUpdate", snapshot.CapturedAt.ToLocalTime().ToString("T", Culture));
    public string CpuSource => snapshot?.Cpu.TotalUtilizationPercent.Source ?? "GetSystemTimes";
    public string MemorySource => snapshot?.Memory.UsedPhysicalPercent.Source ?? "GlobalMemoryStatusEx";
    private CultureInfo Culture => CultureInfo.GetCultureInfo(text.Language);
    public event EventHandler? Updated;

    public void SetActive(bool active)
    {
        if (active == IsActive) return;
        Interlocked.Increment(ref generation);
        if (active)
        {
            History.Clear(); snapshot = null;
            lease = service.Subscribe(Receive);
        }
        else
        {
            lease?.Dispose(); lease = null;
            lock (pendingGate) pending = null;
        }
        Notify();
    }
    private void Receive(HardwareTelemetrySnapshot value)
    {
        int version;
        lock (pendingGate)
        {
            pending = value;
            if (queued) return;
            queued = true; version = Volatile.Read(ref generation);
        }
        if (!dispatcher.Post(() =>
        {
            HardwareTelemetrySnapshot? next;
            lock (pendingGate) { next = pending; pending = null; queued = false; }
            if (version != Volatile.Read(ref generation) || !IsActive || next is null) return;
            snapshot = next;
            History.Add(next);
            Notify();
        })) { lock (pendingGate) { queued = false; pending = null; } }
    }
    public void RefreshLanguage() => Notify();
    private void Notify() { OnPropertyChanged(string.Empty); Updated?.Invoke(this, EventArgs.Empty); }
    private string Status(TelemetryStatus? status) => text["Status" + (status?.ToString() ?? "WarmingUp")];
    private string Percent(Reading<double>? value) => value is { Status: TelemetryStatus.Available, Value: { } n } && double.IsFinite(n) && n is >= 0 and <= 100 ? n.ToString("F1", Culture) + " %" : "—";
    private string Bytes(Reading<ulong>? value, bool mib) => value is { Status: TelemetryStatus.Available, Value: { } n } ?
        (mib ? TelemetryMath.MiB(n) : TelemetryMath.GiB(n)).ToString(mib ? "F1" : "F2", Culture) + (mib ? " MiB" : " GiB") : "—";
}
