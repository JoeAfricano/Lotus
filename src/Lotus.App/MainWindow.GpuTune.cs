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
        ex => Serilog.Log.Warning(ex, "GPU tune"));

    private async Task<(IReadOnlyList<TuneSample> Samples, bool DriverReset)> RunGpuTestAsync(TimeSpan length, IProgress<double> progress, CancellationToken token)
    {
        var samples = new List<TuneSample>();
        var started = DateTimeOffset.Now;
        gpuTestRunning = true; UpdateMonitoringDemand();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            var load = Task.Run(() =>
            {
                var bench = new Lotus.Infrastructure.Score.D3D11GpuBenchmark();
                while (!stop.IsCancellationRequested && DateTimeOffset.Now - started < length)
                    bench.Run(1, TimeSpan.FromSeconds(20), _ => { }, stop.Token);
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
                progress.Report(Math.Min(100, (DateTimeOffset.Now - started) / length * 100));
            }
            try { await load; } catch (OperationCanceledException) { }
            token.ThrowIfCancellationRequested();
        }
        finally { gpuTestRunning = false; UpdateMonitoringDemand(); }
        var since = DateTimeOffset.Now - started + TimeSpan.FromSeconds(10);
        var reset = new Lotus.Infrastructure.Maintenance.WindowsCrashLog().Signals(since).Any(s => s.Signal == Lotus.Core.Maintenance.SystemSignal.GpuReset);
        return (samples, reset);
    }
}
