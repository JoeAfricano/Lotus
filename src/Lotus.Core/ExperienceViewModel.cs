using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lotus.Core.Diagnostics;
using Lotus.Core.Telemetry;
using System.Globalization;
using System.Diagnostics;

namespace Lotus.Core;

public enum AnalysisExperienceState { NotAnalyzed, Running, Completed, Partial }

// UI orchestration only: existing collector and existing RAM rule remain unchanged.
public sealed class ExperienceViewModel : ObservableObject, IDisposable
{
    private readonly MonitoringViewModel monitoring;
    private readonly MemoryAnalysisViewModel memory;
    private readonly ITextCatalog text;
    private readonly TimeSpan timeout;
    private readonly ShellViewModel shell;
    private readonly PreferencesService? preferences;
    private readonly Stopwatch elapsed = new();
    private CancellationTokenSource? cancellation;
    private TaskCompletionSource? ready;
    private HardwareTelemetrySnapshot? hardware;
    public ExperienceViewModel(MonitoringViewModel monitoring, MemoryAnalysisViewModel memory, ITextCatalog text, ShellViewModel shell, TimeSpan? timeout = null, PreferencesService? preferences = null)
    {
        this.monitoring = monitoring; this.memory = memory; this.text = text; this.shell = shell; this.preferences = preferences;
        if (preferences is not null) preferences.PropertyChanged += OnPreferencesChanged;
        this.timeout = timeout ?? TimeSpan.FromSeconds(45);
        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync, () => !IsAnalyzing);
        CancelCommand = new RelayCommand(() => cancellation?.Cancel(), () => IsAnalyzing);
        ViewResultCommand = new RelayCommand(() => shell.NavigateCommand.Execute("analysis"), () => HasResult);
        monitoring.Updated += OnUpdated;
    }
    public AnalysisExperienceState State { get; private set; }
    public bool IsAnalyzing => State == AnalysisExperienceState.Running;
    public bool ShowStart => State == AnalysisExperienceState.NotAnalyzed;
    public string CollectorStatus => text[monitoring.IsActive ? "UxCollectorActive" : "UxCollectorPaused"];
    public bool HasResult => State is AnalysisExperienceState.Completed or AnalysisExperienceState.Partial;
    public bool NeedsAttention => Assessment.Finding is MemoryFinding.LowAvailability or MemoryFinding.VeryLowAvailability;
    public MemoryAssessment Assessment { get; private set; } = MemoryAssessment.Empty;
    public ITextCatalog Text => text;
    public IAsyncRelayCommand AnalyzeCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand ViewResultCommand { get; }
    public event EventHandler? DemandChanged;
    private CultureInfo Culture => CultureInfo.GetCultureInfo(text.Language);
    public string Heading => text[IsAnalyzing ? "UxAnalyzingTitle" : State == AnalysisExperienceState.NotAnalyzed ? "UxReadyTitle"
        : State == AnalysisExperienceState.Partial ? "UxPartialTitle" : NeedsAttention ? "UxAttentionTitle" : "UxCompletedTitle"];
    public string Description => text[IsAnalyzing ? "UxAnalyzingDescription" : State == AnalysisExperienceState.NotAnalyzed ? "UxReadyDescription"
        : State == AnalysisExperienceState.Partial ? "UxPartialDescription" : NeedsAttention ? "UxAttentionDescription" : "UxCompletedDescription"];
    public string AnalysisAction => text[HasResult ? "UxAnalyzeAgain" : "UxAnalyzeNow"];
    public string Finding => text[ShowStart ? "UxNotAnalyzed" : IsAnalyzing ? "UxObservationRunning" : State == AnalysisExperienceState.Partial ? "UxPartialBadge" : NeedsAttention ? "UxAttentionBadge" : "UxNormalBadge"];
    public double ObservationSeconds => Math.Clamp(Assessment.ObservedDuration.TotalSeconds, 0, 30);
    public string ObservationProgress => text.Format("UxObservationTime", Math.Floor(ObservationSeconds).ToString("F0", Culture));
    public string ElapsedTime => text.Format("UxElapsedTime", elapsed.Elapsed.ToString(@"mm\:ss", Culture));
    public bool WaitingAnimation => IsAnalyzing && Assessment.AvailableBytes is null && preferences?.Current.ReduceMotion != true;
    private void OnPreferencesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => Notify();
    public string Advice => text[ShowStart ? "UxAdviceNoAnalysis" : IsAnalyzing ? "UxCurrentCheck" : State == AnalysisExperienceState.Partial ? "UxPartialDescription" : NeedsAttention ? "UxAdviceLow" : "UxAdviceNormal"];
    public string Available => Bytes(Assessment.AvailableBytes);
    public string Capacity => Bytes(Assessment.TotalBytes);
    public string MinimumAvailable => Bytes(Assessment.MinimumAvailableBytes);
    public string Evidence => text.Format("RamEvidence", Assessment.ObservedDuration.TotalSeconds.ToString("F0", Culture), Assessment.LowDuration.TotalSeconds.ToString("F0", Culture));
    public string Source => Assessment.Source ?? text["UxUnavailable"];
    public string LastAnalysis => Assessment.CapturedAt is { } at ? text.Format("LastUpdate", at.ToLocalTime().ToString("g", Culture)) : text["UxNoAnalysis"];
    // Names read straight from Windows at startup, so Home is filled before any analysis.
    public (string? Cpu, string? Gpu, long? RamBytes)? QuickFacts { get; set { field = value; foreach (var n in new[] { nameof(CpuModel), nameof(GpuModel), nameof(RamCapacity), nameof(SummaryTime) }) OnPropertyChanged(n); } }
    public string CpuModel => hardware?.Cpu.Model ?? QuickFacts?.Cpu ?? text["UxUnavailable"];
    public string GpuModel => hardware?.Gpu.Devices.FirstOrDefault()?.Name ?? QuickFacts?.Gpu ?? text["UxUnavailable"];
    public string RamCapacity => hardware?.Memory.TotalPhysicalBytes is { Status: TelemetryStatus.Available, Value: { } n } ? Bytes(n) : QuickFacts?.RamBytes is { } q ? Bytes((ulong)q) : text["UxUnavailable"];
    public string SummaryTime => hardware is null ? text[QuickFacts is null ? "UxSummaryWaiting" : "UxSummaryQuick"] : text.Format("UxSummaryTime", hardware.CapturedAt.ToLocalTime().ToString("g", Culture));
    private string Bytes(ulong? value) => value is { } n ? TelemetryMath.GiB(n).ToString("F2", Culture) + " GiB" : "—";
    private async Task AnalyzeAsync()
    {
        using var run = new CancellationTokenSource(); cancellation = run;
        ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Assessment = MemoryAssessment.Empty; State = AnalysisExperienceState.Running; elapsed.Restart();
        using var clockCancellation = new CancellationTokenSource();
        var clock = UpdateClockAsync(clockCancellation.Token);
        Notify(); DemandChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await ready.Task.WaitAsync(timeout, run.Token);
            State = AnalysisExperienceState.Completed;
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException) { State = AnalysisExperienceState.Partial; }
        finally
        {
            clockCancellation.Cancel(); await clock; elapsed.Stop();
            cancellation = null; ready = null;
            DemandChanged?.Invoke(this, EventArgs.Empty); Notify();
        }
        if (State == AnalysisExperienceState.Completed) shell.NavigateCommand.Execute("analysis");
    }
    private async Task UpdateClockAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(token)) OnPropertyChanged(nameof(ElapsedTime));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    private void OnUpdated(object? sender, EventArgs args)
    {
        if (monitoring.IsActive && monitoring.Snapshot is { } snapshot) hardware = snapshot;
        if (IsAnalyzing)
        {
            Assessment = memory.Assessment;
            if (Assessment.ObservedDuration >= MemoryAvailabilityAnalyzer.RequiredDuration &&
                Assessment.Finding is MemoryFinding.NoSustainedLowAvailability or MemoryFinding.LowAvailability or MemoryFinding.VeryLowAvailability)
                ready?.TrySetResult();
        }
        Notify();
    }
    public void Interrupt() => cancellation?.Cancel();
    public void RefreshLanguage() => Notify();
    private void Notify()
    {
        OnPropertyChanged(string.Empty); AnalyzeCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged(); ViewResultCommand.NotifyCanExecuteChanged();
    }
    public void Dispose() { Interrupt(); monitoring.Updated -= OnUpdated; if (preferences is not null) preferences.PropertyChanged -= OnPreferencesChanged; }
}
