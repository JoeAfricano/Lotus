using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App;

// Light mode: Lotus gets out of the way of the game without losing anything.
// - A library game is running and Lotus is not in front: Windows efficiency mode (EcoQoS, the leaf in Task Manager),
//   lower priority and the memory Lotus is not using goes back to Windows.
// - The window is minimized or in the tray for a while: the current page and its images are released too; they come
//   back as soon as the window is shown again (same section, same state of the view models).
// Everything the player started keeps running (in-game analysis, Score test, measurements live in view models).
public sealed partial class MainWindow
{
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? lightTimer;
    private Lotus.Core.Games.GameEntry? lightGame;
    private DateTimeOffset? hiddenSince;
    private bool pageReleased, efficiency, checkingGame;
    private bool gameCheckPending;
    private CancellationTokenSource? gameExitWatch;
    private string? watchedGame;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? releasePageTimer;
    public bool LightModeActive => efficiency || pageReleased;
    public string? LightModeGame => lightGame?.Name;

    private void StartLightMode()
    {
        lightTimer = DispatcherQueue.CreateTimer();
        lightTimer.Interval = TimeSpan.FromSeconds(10);
        lightTimer.Tick += async (_, _) => await CheckGameAsync();
        lightTimer.Start();
        _ = CheckGameAsync();
    }

    private async Task CheckGameAsync()
    {
        if (!initialized) return;
        if (checkingGame) { gameCheckPending = true; return; }
        checkingGame = true;
        try { lightGame = await Task.Run(games.RunningGame); }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Light mode: game check"); }
        // Keep the guard until all async side effects finish: a slow restore must not overlap the next tick.
        try { WatchGameExit(lightGame); await OnGameCheckAsync(lightGame); UpdateLightMode(); }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Light mode: game update"); }
        finally
        {
            checkingGame = false;
            if (gameCheckPending) { gameCheckPending = false; DispatcherQueue.TryEnqueue(() => _ = CheckGameAsync()); }
        }
    }

    private void WatchGameExit(Lotus.Core.Games.GameEntry? game)
    {
        if (watchedGame == game?.Key) return;
        gameExitWatch?.Cancel(); gameExitWatch?.Dispose(); gameExitWatch = null;
        watchedGame = game?.Key;
        if (game is null) return;
        var watch = gameExitWatch = new CancellationTokenSource();
        var token = watch.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var process = games.GameProcess(game);
                if (process is null || !await Lotus.Infrastructure.Games.GameExitSignal.WaitAsync(process, token)) return;
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!initialized || !ReferenceEquals(gameExitWatch, watch)) return;
                    watchedGame = null; // reattach if this was a launcher handing off to the real game
                    _ = CheckGameAsync();
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Serilog.Log.Debug(ex,"Game exit notification; periodic discovery retained"); }
        });
    }

    private void ResumePerformance()
    {
        if (efficiency) { efficiency = false; SetEfficiency(false); }
    }

    private void StopLightMode()
    {
        lightTimer?.Stop(); releasePageTimer?.Stop();
        gameExitWatch?.Cancel(); gameExitWatch?.Dispose(); gameExitWatch = null;
    }

    private void UpdateLightMode()
    {
        if (!initialized) return;
        // Title bar: "Playing now · game" while a library game runs.
        PlayingPill.Visibility = lightGame is null ? Visibility.Collapsed : Visibility.Visible;
        if (lightGame is { } playing) { PlayingText.Text = text.Format("TitlePlaying", playing.Name); ToolTipService.SetToolTip(PlayingPill, text["TitlePlayingHint"]); }
        var visible = AppWindow.IsVisible && AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized };
        hiddenSince = visible ? null : hiddenSince ?? DateTimeOffset.Now;

        // Hidden or minimized always counts as "not in front" (releasing the page can raise a spurious activation).
        // Never while something is being measured: throttling Lotus would skew a Score test, a capture or the in-game check.
        var measuring = analysing || score.Busy || benchmark.Busy || experience.IsAnalyzing || gpuTestRunning || gpuTune?.Testing == true || gpuTune?.Saving == true || CalibrationKeepsPage;
        var wantEfficiency = !measuring && (!visible || (!windowActive && lightGame is not null));
        if (wantEfficiency != efficiency) { efficiency = wantEfficiency; SetEfficiency(efficiency); }

        // Restore normal scheduling BEFORE rebuilding the page, rather than rebuilding under EcoQoS.
        if (visible && pageReleased) { pageReleased = false; ShowRoute(force: true); }

        releasePageTimer ??= DispatcherQueue.CreateTimer();
        releasePageTimer.IsRepeating = false;
        releasePageTimer.Interval = TimeSpan.FromSeconds(3);
        releasePageTimer.Tick -= OnReleasePage;
        releasePageTimer.Tick += OnReleasePage;
        if (visible || measuring || pageReleased) releasePageTimer.Stop();
        else if (!releasePageTimer.IsRunning) releasePageTimer.Start();

        // Release hidden visuals after a short grace period; preserve active measurements and view models.
        if (!visible && !pageReleased && hiddenSince is { } since && DateTimeOffset.Now - since >= TimeSpan.FromSeconds(3) && !measuring)
        {
            pageReleased = true;
            ContentFrame.Content = null; ContentFrame.BackStack.Clear(); displayedSectionId = null;
            Controls.HomeBanner.ReleaseCachedPhotos();
            // One optional background collection of Lotus's unreachable wrappers, not a working-set purge or a
            // periodic blocking/compacting GC. The runtime may skip it if a collection would not be worthwhile.
            _ = Task.Run(() => GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking:false, compacting:false));
            Serilog.Log.Information("Light mode: hidden page and banner cache released");
        }
    }
    private void OnReleasePage(Microsoft.UI.Dispatching.DispatcherQueueTimer timer, object args) => UpdateLightMode();

    // Efficiency mode = EcoQoS (execution speed throttling) + below-normal priority; turned off as soon as Lotus is in front.
    private static void SetEfficiency(bool on)
    {
        try
        {
            var state = new PowerThrottlingState { Version = 1, ControlMask = 1, StateMask = on ? 1u : 0u };
            SetProcessInformation(GetCurrentProcess(), 4 /* ProcessPowerThrottling */, ref state, (uint)Marshal.SizeOf<PowerThrottlingState>());
            SetPriorityClass(GetCurrentProcess(), on ? 0x4000u /* BELOW_NORMAL */ : 0x20u /* NORMAL */);
            Serilog.Log.Information("Light mode efficiency {State}", on);
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Light mode: efficiency"); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct PowerThrottlingState { public uint Version, ControlMask, StateMask; }
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool SetPriorityClass(IntPtr process, uint priority);
    [DllImport("kernel32.dll")] private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottlingState info, uint size);
}
