using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lotus.Core.Telemetry;

namespace Lotus.Core.Benchmark;

public sealed class BenchmarkViewModel : ObservableObject
{
    private readonly IFrameCaptureProvider provider;
    private readonly IBenchmarkRepository repository;
    private readonly ICaptureTargetSource targets;
    private readonly MonitoringViewModel monitoring;
    private readonly Action<Exception>? diagnostic;
    private CancellationTokenSource? stop;
    private Task? capture;
    private bool busy, refreshing, elevated;
    private CaptureTarget? selectedTarget;
    private BenchmarkSession? selectedSession, baseline;
    private string conditions = "", statusKey = "BenchReady";
    private int seconds = 60;
    public ITextCatalog Text { get; }
    public ObservableCollection<CaptureTarget> Targets { get; } = [];
    public ObservableCollection<BenchmarkSession> Sessions { get; } = [];
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand StartCommand { get; }
    public IRelayCommand StopCommand { get; }
    public bool Busy { get => busy; private set { SetProperty(ref busy, value); Notify(); } }
    public bool CanEdit => !Busy;
    public bool Elevated { get => elevated; set => SetProperty(ref elevated, value); }
    public int Seconds { get => seconds; set => SetProperty(ref seconds, Math.Clamp(value, 10, 120)); }
    public string Conditions { get => conditions; set => SetProperty(ref conditions, value); }
    public CaptureTarget? SelectedTarget { get => selectedTarget; set { SetProperty(ref selectedTarget, value); StartCommand.NotifyCanExecuteChanged(); } }
    public BenchmarkSession? SelectedSession { get => selectedSession; set { SetProperty(ref selectedSession, value); Notify(); } }
    public BenchmarkSession? Baseline { get => baseline; set { SetProperty(ref baseline, value); Notify(); } }
    public string Status => Text[statusKey];
    public bool HasResult => SelectedSession is not null;
    public string Average => Metric(SelectedSession?.Data?.Statistics?.AverageFps, " FPS");
    public string Low1 => Metric(SelectedSession?.Data?.Statistics?.Low1Fps, " FPS");
    public string P99 => Metric(SelectedSession?.Data?.Statistics?.P99Ms, " ms");
    public string Details => SelectedSession is not { } s ? "" : Text.Format("BenchDetails", s.Status, s.Data?.Statistics?.Count ?? s.Data?.Frames.Count ?? 0,
        s.Data?.Statistics?.DurationSeconds.ToString("F2", Culture) ?? "—", Metric(s.Data?.Statistics?.P95Ms, " ms"),
        s.Data?.Statistics?.SpikeCount.ToString(Culture) ?? "—", s.Data?.SwapChain ?? "—", s.Data?.OtherSwapChains ?? 0,
        s.Data?.InvalidRows ?? 0, s.Data?.FrameTypes ?? "—", s.RawPath) + "\n" + s.Diagnostics + "\n" + s.HardwareJson;
    public string Comparison
    {
        get
        {
            if (SelectedSession is not { } current || Baseline is not { } before) return Text["BenchChooseBaseline"];
            if (!BenchmarkMath.Comparable(before, current)) return Text["BenchIncompatible"];
            var a = before.Data!.Statistics!; var b = current.Data!.Statistics!;
            string Delta(double x, double y, string unit)
            {
                var d = MetricDifference.Between(x, y);
                return d.Absolute.ToString("+0.00;-0.00;0.00", Culture) + unit + " (" + d.Percent.ToString("+0.0;-0.0;0.0", Culture) + "%)";
            }
            return Text.Format("BenchComparison", Delta(a.AverageFps, b.AverageFps, " FPS"), Delta(a.Low1Fps, b.Low1Fps, " FPS"),
                Delta(a.P95Ms, b.P95Ms, " ms"), Delta(a.P99Ms, b.P99Ms, " ms")) + "\n" + Text.Format("BenchComparedConditions", before.Conditions, current.Conditions);
        }
    }
    private CultureInfo Culture => CultureInfo.GetCultureInfo(Text.Language);
    private string Metric(double? value, string unit) => value?.ToString("F1", Culture) + (value.HasValue ? unit : "—");
    public BenchmarkViewModel(IFrameCaptureProvider provider, IBenchmarkRepository repository, ICaptureTargetSource targets,
        MonitoringViewModel monitoring, ITextCatalog text, Action<Exception>? diagnostic = null)
    {
        this.provider = provider; this.repository = repository; this.targets = targets; this.monitoring = monitoring; Text = text; this.diagnostic = diagnostic;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !Busy && !refreshing);
        StartCommand = new AsyncRelayCommand(StartAsync, () => !Busy && !refreshing && SelectedTarget is not null);
        StopCommand = new RelayCommand(() => { stop?.Cancel(); statusKey = "BenchStopping"; Notify(); }, () => Busy && stop?.IsCancellationRequested == false);
    }
    public async Task RefreshAsync()
    {
        if (Busy || refreshing) return;
        refreshing = true; Notify();
        try
        {
            var list = await targets.ListAsync(); Targets.Clear(); foreach (var target in list) Targets.Add(target);
            SelectedTarget = null;
            var sessions = await repository.ListAsync(); Sessions.Clear(); foreach (var session in sessions) Sessions.Add(session);
            if (SelectedSession is { } selected) SelectedSession = Sessions.FirstOrDefault(s => s.Id == selected.Id);
        }
        catch (Exception ex) { diagnostic?.Invoke(ex); statusKey = "BenchLoadError"; }
        finally { refreshing = false; Notify(); }
    }
    public Task StartAsync()
    {
        if (Busy || SelectedTarget is null) return Task.CompletedTask;
        stop = new(); Busy = true; statusKey = "BenchCapturing"; Notify();
        var metadata = JsonSerializer.Serialize(new { Windows = Environment.OSVersion.VersionString,
            LastTelemetry = monitoring.Snapshot, Driver = "Unavailable", GameSettings = "User supplied notes only" });
        var request = new CaptureRequest(SelectedTarget, Seconds, Elevated, Conditions, metadata);
        capture = RunAsync(request, stop.Token); return capture;
    }
    private async Task RunAsync(CaptureRequest request, CancellationToken token)
    {
        try
        {
            var session = await provider.CaptureAsync(request, token);
            SelectedSession = session;
            await repository.SaveAsync(session);
            Sessions.Insert(0, session); while (Sessions.Count > 100) Sessions.RemoveAt(Sessions.Count - 1);
            statusKey = session.Status switch { "Completed" => "BenchCompleted", "AccessDenied" => "BenchPermission",
                "ToolMissing" => "BenchToolMissing", "InsufficientData" => "BenchInsufficient", "Cancelled" => "BenchCancelled", _ => "BenchFailed" };
        }
        catch (OperationCanceledException) { statusKey = "BenchCancelled"; }
        catch (Exception ex) { diagnostic?.Invoke(ex); statusKey = "BenchFailed"; }
        finally { stop?.Dispose(); stop = null; Busy = false; }
    }
    public async Task ShutdownAsync() { stop?.Cancel(); if (capture is not null) await capture; }
    public void RefreshLanguage() => Notify();
    private void Notify()
    {
        foreach (var property in new[] { nameof(CanEdit), nameof(Status), nameof(HasResult), nameof(Average), nameof(Low1), nameof(P99), nameof(Details), nameof(Comparison) }) OnPropertyChanged(property);
        RefreshCommand?.NotifyCanExecuteChanged(); StartCommand?.NotifyCanExecuteChanged(); StopCommand?.NotifyCanExecuteChanged();
    }
}
