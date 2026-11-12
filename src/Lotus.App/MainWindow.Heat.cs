using Lotus.Core.Games;

namespace Lotus.App;

// "Processador no limite": while a library game runs, Windows' counters are read every 15 s (no driver, no
// administrator) and one quiet notice per session is shown when the processor stays held back for minutes or the
// thermal zone stays very hot. "Por que está travando?" uses the same sampler during its 30-second measurement.
public sealed partial class MainWindow
{
    private readonly Lotus.Infrastructure.Games.PdhCpuHeatSampler cpuHeat = new();
    private readonly CpuHeatWatch heatWatch = new();
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? heatTimer;

    private void WatchCpuHeat()
    {
        sessionHub.Started += (_, game) => DispatcherQueue.TryEnqueue(() => StartHeatWatch(game));
        sessionHub.Stopped += (_, _) => DispatcherQueue.TryEnqueue(StopHeatWatch);
    }

    private void StartHeatWatch(GameEntry game)
    {
        heatWatch.Reset();
        heatTimer?.Stop();
        heatTimer = DispatcherQueue.CreateTimer(); heatTimer.Interval = CpuHeatWatch.Every; heatTimer.IsRepeating = true;
        var busy = false;
        heatTimer.Tick += async (_, _) =>
        {
            if (busy) return;
            busy = true;
            try
            {
                var sample = await Task.Run(cpuHeat.Sample);
                if (heatWatch.Feed(sample) && heatWatch.Last is { } last)
                {
                    Serilog.Log.Information("CPU heat during {Game}: limit {Limit} zone {Zone}", game.Name, last.AveragePerformanceLimit, last.MaxZoneCelsius);
                    tray?.Notify(text["HeatAlertTitle"], last.MaxZoneCelsius is >= CpuHeatWatch.AlertZone and var c
                        ? text.Format("HeatAlertHot", Math.Round(c)) : text["HeatAlertLimited"]);
                    StopHeatWatch(); // once per session
                }
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "CPU heat"); StopHeatWatch(); }
            finally { busy = false; }
        };
        heatTimer.Start();
    }

    private void StopHeatWatch() { heatTimer?.Stop(); heatTimer = null; }
}
