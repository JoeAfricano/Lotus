using Lotus.Core.Telemetry;

namespace Lotus.App;

// "Ajuste da placa": the assistant's view model, its read-only readiness check and the stability test runner.
// The test loads the GPU with the Lotus Score workload for the chosen time, reads temperature, clock and power from
// the shared sensor loop every 2 s, stops above 83 °C and reports a graphics driver reset recorded by Windows meanwhile.
public sealed partial class MainWindow
{
    private GpuTuneViewModel? gpuTune;
    private bool gpuTestRunning;
    private GpuTuneViewModel GpuTune => gpuTune ??= new(text,
        () => new TuneReadiness(
            Nvidia: monitoring.Snapshot?.Gpu.Devices.FirstOrDefault()?.Vendor?.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) == true || (experience.GpuModel?.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ?? false) || (experience.GpuModel?.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ?? false),
            Afterburner: Lotus.Infrastructure.Integrations.AfterburnerSetup.Installed,
            RivaTuner: integrations.Rtss.State().Installed,
            VoltageUnlocked: Lotus.Infrastructure.Integrations.AfterburnerSetup.VoltageUnlocked(),
            AutoProfiles: Lotus.Infrastructure.Integrations.AfterburnerSetup.AutoProfiles()),
        () => experience.GpuModel,
        RunGpuTestAsync,
        ex => Serilog.Log.Warning(ex, "GPU tune"))
    { AutoTune = AutoTuneAsync, AutoAvailable = () => afterburner.Available, ApplySetting = s => afterburner.Apply(s) };

    private async Task<(IReadOnlyList<TuneSample> Samples, bool DriverReset)> RunGpuTestAsync(TimeSpan length, IProgress<double> progress, CancellationToken token)
    {
        var (samples, reset, _) = await RunGpuLoadAsync(length, progress, token);
        return (samples, reset);
    }

    // The automatic tune: Afterburner's control memory applies, this load test judges every step.
    private sealed class LoadTest(MainWindow owner) : IGpuLoadTest
    {
        public Task<(IReadOnlyList<TuneSample> Samples, bool DriverReset, double Score)> RunAsync(TimeSpan length, IProgress<double>? progress, CancellationToken token) =>
            owner.RunGpuLoadAsync(length, progress, token);
    }
    private readonly Lotus.Infrastructure.Integrations.AfterburnerControl afterburner = new();
    private async Task<AutoTuneReport> AutoTuneAsync(AutoModes modes, IProgress<AutoStep> step, IProgress<string> status, CancellationToken token)
    {
        var tuner = new GpuAutoTuner(afterburner, new LoadTest(this), GpuTuning.For(experience.GpuModel),
            s => { Serilog.Log.Information("GPU auto step {Mode} {Setting} {Verdict} score {Score:0.0} temp {Temp:0} clock {Clock:0} power {Power:0}", s.Mode, s.Setting.ToString(), s.Verdict, s.Score, s.MaxTempC, s.AvgCoreMhz, s.AvgPowerW); step.Report(s); },
            s => status.Report(s));
        return await Task.Run(() => tuner.RunAsync(modes, token), token);
    }
    // At start: a tune that never finished (Lotus or the PC stopped mid-test) is undone as soon as Afterburner is open.
    private async void RecoverGpuTune()
    {
        if (!afterburner.HasPendingOriginal) return;
        for (var i = 0; i < 30 && !afterburner.Available; i++) await Task.Delay(2000);
        var ok = await Task.Run(afterburner.RestoreOriginal);
        Serilog.Log.Information("GPU tune recovery {Ok}", ok);
    }

    // Window state (AppWindow) may only be read on the UI thread: called from the tune worker, it is queued there.
    private void RunOnUi(Action action) { if (DispatcherQueue.HasThreadAccess) action(); else DispatcherQueue.TryEnqueue(() => action()); }

    private async Task<(IReadOnlyList<TuneSample> Samples, bool DriverReset, double Score)> RunGpuLoadAsync(TimeSpan length, IProgress<double>? progress, CancellationToken token)
    {
        var samples = new List<TuneSample>();
        var scores = new List<double>();
        var started = DateTimeOffset.Now;
        gpuTestRunning = true; RunOnUi(UpdateMonitoringDemand); // the automatic tune runs this off the UI thread
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            var load = Task.Run(() =>
            {
                var bench = new Lotus.Infrastructure.Score.D3D11GpuBenchmark();
                while (!stop.IsCancellationRequested && DateTimeOffset.Now - started < length)
                    if (bench.Run(1, TimeSpan.FromSeconds(20), _ => { }, stop.Token) is { Count: > 0 } pass) lock (scores) scores.AddRange(pass);
            }, stop.Token);
            while (!load.IsCompleted)
            {
                await Task.Delay(2000, stop.Token).ContinueWith(_ => { });
                if (monitoring.Snapshot?.Gpu.Devices.FirstOrDefault() is { } gpu)
                {
                    double V(GpuSensor s) => gpu.Get(s) is { Status: Lotus.Core.Telemetry.TelemetryStatus.Available, Value: { } v } ? v : 0;
                    var sample = new TuneSample(V(GpuSensor.Temperature), V(GpuSensor.CoreClock), V(GpuSensor.Power));
                    samples.Add(sample);
                    if (sample.TempC > GpuTuning.StopTempC) stop.Cancel(); // too hot: stop at once
                }
                progress?.Report(Math.Min(100, (DateTimeOffset.Now - started) / length * 100));
            }
            try { await load; } catch (OperationCanceledException) { }
            token.ThrowIfCancellationRequested();
        }
        finally { gpuTestRunning = false; RunOnUi(UpdateMonitoringDemand); }
        var since = DateTimeOffset.Now - started + TimeSpan.FromSeconds(10);
        var reset = new Lotus.Infrastructure.Maintenance.WindowsCrashLog().Signals(since).Any(s => s.Signal == Lotus.Core.Maintenance.SystemSignal.GpuReset);
        double score; lock (scores) score = scores.Count == 0 ? 0 : scores.Average();
        return (samples, reset, score);
    }
}
