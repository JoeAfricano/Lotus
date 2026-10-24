using CommunityToolkit.Mvvm.ComponentModel;
using Lotus.Core.Telemetry;
using System.Globalization;

namespace Lotus.Core.Diagnostics;

// Reuses the existing singleton presentation stream; creates no worker/timer/provider.
public sealed class MemoryAnalysisViewModel : ObservableObject, IDisposable
{
    private readonly MonitoringViewModel monitoring;
    private readonly ITextCatalog text;
    private readonly MemoryAvailabilityAnalyzer analyzer = new();
    public MemoryAnalysisViewModel(MonitoringViewModel monitoring, ITextCatalog text)
    {
        this.monitoring = monitoring; this.text = text;
        monitoring.Updated += OnUpdated;
    }
    public ITextCatalog Text => text;
    public bool IsActive { get; private set; }
    public MemoryAssessment Assessment => analyzer.Current;
    private CultureInfo Culture => CultureInfo.GetCultureInfo(text.Language);
    public string Status => text["RamFinding" + Assessment.Finding];
    public string Available => Bytes(Assessment.AvailableBytes);
    public string Capacity => Bytes(Assessment.TotalBytes);
    public string MinimumAvailable => Bytes(Assessment.MinimumAvailableBytes);
    public string Evidence => text.Format("RamEvidence", Assessment.ObservedDuration.TotalSeconds.ToString("F0", Culture),
        Assessment.LowDuration.TotalSeconds.ToString("F0", Culture));
    public string Recommendation => text[Assessment.Finding is MemoryFinding.LowAvailability or MemoryFinding.VeryLowAvailability
        ? "RamAdviceLow" : Assessment.Finding == MemoryFinding.Unavailable ? "RamAdviceUnavailable" : "RamAdviceObserve"];
    public string Confidence => text[Assessment.Finding is MemoryFinding.LowAvailability or MemoryFinding.VeryLowAvailability
        ? "RamConfidenceObserved" : "RamConfidenceLimited"];
    public string Source => Assessment.Source ?? "—";
    public string Rule => MemoryAvailabilityAnalyzer.RuleVersion;
    public string LastUpdate => Assessment.CapturedAt is { } at ? text.Format("LastUpdate", at.ToLocalTime().ToString("T", Culture)) : text["WaitingSample"];
    public void SetActive(bool active)
    {
        if (IsActive == active) return;
        IsActive = active; analyzer.Reset(); OnPropertyChanged(string.Empty);
    }
    private void OnUpdated(object? sender, EventArgs args)
    {
        if (!IsActive) return;
        if (monitoring.Snapshot is { } snapshot) analyzer.Add(snapshot); else analyzer.Reset();
        OnPropertyChanged(string.Empty);
    }
    private string Bytes(ulong? value) => value is { } bytes ? TelemetryMath.GiB(bytes).ToString("F2", Culture) + " GiB" : "—";
    public void Dispose() { SetActive(false); monitoring.Updated -= OnUpdated; }
}
