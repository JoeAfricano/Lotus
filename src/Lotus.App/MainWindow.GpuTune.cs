using Lotus.Core.Telemetry;

namespace Lotus.App;

// "Ajuste da placa": the assistant's view model, its read-only readiness check and the stability test runner.
// The test loads the GPU with the Lotus Score workload for the chosen time, reads temperature, clock and power from
// the shared sensor loop every 2 s, stops above 83 Â°C and reports a graphics driver reset recorded by Windows meanwhile.
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
            AutoProfiles: Lotus.Infrastructure.Integrations.AfterburnerSetup.AutoProfiles() || preferences.Current is { GpuTuneOnGames: true, GpuTuneKept: not null }),
        () => experience.GpuModel,
        RunGpuTestAsync,
        ex => Serilog.Log.Warning(ex, "GPU tune"))
    {
        AutoTune = AutoTuneAsync, AutoAvailable = () => afterburner.Available,
        GpuIdentity = () => monitoring.Snapshot?.Gpu.Devices.FirstOrDefault()?.Id,
        LoadCalibration = () => preferences.Current.GpuTuneCalibration,
        SaveCalibration = value => preferences.Update(preferences.Current with { GpuTuneCalibration = value }),
        ApplySetting = ApplyGpuSetting,
        OpenAfterburner = OpenAfterburnerAsync,
        SaveToProfiles = () => abProfiles.SaveCurrentAsync(), RestoreProfiles = () => abProfiles.RestoreAsync(), HasProfilesBackup = () => abProfiles.HasBackup,
        Kept = s => preferences.Update(preferences.Current with { GpuTuneKept = s }), KeptSetting = () => preferences.Current.GpuTuneKept,
        ClearKept = () => preferences.Update(preferences.Current with { GpuTuneKept = null }),
        GetOnGames = () => preferences.Current.GpuTuneOnGames, SetOnGames = on => preferences.Update(preferences.Current with { GpuTuneOnGames = on }),
    };
    private Lotus.Infrastructure.Integrations.AfterburnerProfiles? abProfilesField;
    private Lotus.Infrastructure.Integrations.AfterburnerProfiles abProfiles => abProfilesField ??= new(afterburner);

    private bool ApplyGpuSetting(GpuSetting setting)
    {
        if (!afterburner.SaveOriginal()) return false;
        gameTuneRecoveryPending = true;
        try { if (afterburner.Apply(setting)) { gameTuneRecoveryPending = false; return true; } }
        finally
        {
            if (gameTuneRecoveryPending)
            {
                try { gameTuneRecoveryPending = !afterburner.RestoreOriginal(); }
                catch (Exception ex) { Serilog.Log.Warning(ex, "GPU tune immediate recovery pending"); }
            }
        }
        return false;
    }

    private async Task OpenAfterburnerAsync()
    {
        if (!afterburner.Available && Lotus.Infrastructure.Integrations.AfterburnerSetup.Installed)
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Lotus.Infrastructure.Integrations.AfterburnerSetup.Exe) { UseShellExecute = true, WorkingDirectory = Lotus.Infrastructure.Integrations.AfterburnerSetup.Dir });
            for (var i = 0; i < 20 && !afterburner.Available; i++) await Task.Delay(500);
        }
        GpuTune.Refresh();
    }

    // No-administrator option: the kept tune is applied while a library game runs (journaled first, so a crash is undone
    // at the next start) and the card goes back to how it was when the game closes. Skipped when Afterburner's own
    // 3D profile already does it, or while a test is running.
    private bool gameTuneActive, gameTuneRecoveryPending;
    private async Task UpdateGameTuneAsync(Lotus.Core.Games.GameEntry? running)
    {
        try
        {
            if (gpuTestRunning || gpuTune?.Testing == true || gpuTune?.Saving == true) return;
            if (gameTuneRecoveryPending)
            {
                if (await Task.Run(afterburner.RestoreOriginal)) { gameTuneRecoveryPending = false; gameTuneActive = false; }
                return;
            }
            var want = running is not null && preferences.Current is { GpuTuneOnGames: true, GpuTuneKept: not null } && !gpuTestRunning && gpuTune?.Testing != true && gpuTune?.Saving != true && !DevBesideInstalled()
                && !Lotus.Infrastructure.Integrations.AfterburnerSetup.AutoProfiles();
            if (want && !gameTuneActive && afterburner.Available)
            {
                var setting = preferences.Current.GpuTuneKept!;
                afterburner.MaxOffsetMhz = GpuTuning.For(experience.GpuModel).OcCoreMax;
                gameTuneActive = await Task.Run(() => ApplyGpuSetting(setting));
                Serilog.Log.Information("GPU tune for {Game}: {Setting} {Ok}", running!.Name, setting.ToString(), gameTuneActive);
            }
            else if ((!want && gameTuneActive) || gameTuneRecoveryPending)
            {
                var ok = await Task.Run(afterburner.RestoreOriginal);
                if (ok) { gameTuneActive = false; gameTuneRecoveryPending = false; }
                Serilog.Log.Information("GPU tune after the game: restored {Ok}", ok);
            }
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "GPU tune for games"); }
    }

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
        if (await Task.Run(games.RunningGame) is not null) return new(null,null,null,null,[],false,"game");
        afterburner.MaxOffsetMhz = GpuTuning.For(experience.GpuModel).OcCoreMax; // no curve point beyond the family ceiling
        var budget = GpuTestBudget.For(GpuTune.TestDepth);
        var tuner = new GpuAutoTuner(afterburner, new LoadTest(this), GpuTuning.For(experience.GpuModel),
            s => { Serilog.Log.Information("GPU auto step {Mode} {Setting} {Verdict} score {Score:0.0} temp {Temp:0} clock {Clock:0} power {Power:0}", s.Mode, s.Setting.ToString(), s.Verdict, s.Score, s.MaxTempC, s.AvgCoreMhz, s.AvgPowerW); step.Report(s); },
            s => status.Report(s), p => RunOnUi(() => GpuTune.Progress = p))
        {
            StepLength = TimeSpan.FromSeconds(budget.Seconds), MaxUndervoltSteps = budget.UndervoltSteps,
            MaxMemorySteps = budget.MemorySteps, MaxCoreSteps = budget.CoreSteps
        };
        try { return await Task.Run(() => tuner.RunAsync(modes, token), token); }
        finally { gameTuneActive = false; }
    }
    // At start: a tune that never finished (Lotus or the PC stopped mid-test) is undone as soon as Afterburner is open.
    private async void RecoverGpuTune()
    {
        if (DevBesideInstalled()) return;
        try
        {
            if (!afterburner.HasPendingOriginal) return;
            for (var i = 0; i < 30 && !afterburner.Available; i++) await Task.Delay(2000);
            var ok = await Task.Run(afterburner.RestoreOriginal);
            Serilog.Log.Information("GPU tune recovery {Ok}", ok);
            gameTuneRecoveryPending = !ok;
        }
        catch (Exception ex) { gameTuneRecoveryPending = true; Serilog.Log.Warning(ex, "GPU tune startup recovery pending"); }
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
        Task load = Task.CompletedTask;
        try
        {
            load = Task.Run(() =>
            {
                var bench = new Lotus.Infrastructure.Score.D3D11GpuBenchmark();
                while (!stop.IsCancellationRequested)
                {
                    var remaining = length - (DateTimeOffset.Now - started);
                    if (remaining.TotalSeconds < 2) break;
                    // Run includes a warm-up and a measured pass. Bound both by the remaining budget.
                    var passLength = TimeSpan.FromSeconds(Math.Min(20, remaining.TotalSeconds / 2));
                    if (bench.Run(1, passLength, _ => { }, stop.Token) is { Count: > 0 } pass) lock (scores) scores.AddRange(pass);
                }
            }, stop.Token);
            var missingSensors = 0;
            while (!load.IsCompleted)
            {
                await Task.Delay(2000, stop.Token).ContinueWith(_ => { });
                if (monitoring.Snapshot?.Gpu.Devices.FirstOrDefault() is { } gpu)
                {
                    double V(GpuSensor s) => gpu.Get(s) is { Status: Lotus.Core.Telemetry.TelemetryStatus.Available, Value: { } v } reading && DateTimeOffset.UtcNow - reading.CapturedAt < TimeSpan.FromSeconds(10) ? v : 0;
                    var sample = new TuneSample(V(GpuSensor.Temperature), V(GpuSensor.CoreClock), V(GpuSensor.Power));
                    if (sample.TempC > 0 && sample.CoreMhz > 0) { samples.Add(sample); missingSensors = 0; }
                    else missingSensors++;
                    if (sample.TempC > GpuTuning.StopTempC) stop.Cancel(); // too hot: stop at once
                }
                else missingSensors++;
                if (missingSensors >= 3) { stop.Cancel(); throw new InvalidOperationException("GPU temperature/clock readings missing or stale; load stopped."); }
                progress?.Report(Math.Min(100, (DateTimeOffset.Now - started) / length * 100));
            }
            try { await load; } catch (OperationCanceledException) { }
            token.ThrowIfCancellationRequested();
        }
        finally
        {
            stop.Cancel();
            try { await load; } catch (OperationCanceledException) { }
            finally { gpuTestRunning = false; RunOnUi(UpdateMonitoringDemand); }
        }
        var since = DateTimeOffset.Now - started + TimeSpan.FromSeconds(10);
        var reset = new Lotus.Infrastructure.Maintenance.WindowsCrashLog().Signals(since).Any(s => s.Signal == Lotus.Core.Maintenance.SystemSignal.GpuReset);
        double score; lock (scores) score = scores.Count == 0 ? 0 : scores.Average();
        return (samples, reset, score);
    }
}
