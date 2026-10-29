using System.Runtime;
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
    private DateTimeOffset lastTrim = DateTimeOffset.MinValue;
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
        if (checkingGame || !initialized) return;
        checkingGame = true;
        try { lightGame = await Task.Run(games.RunningGame); }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Light mode: game check"); }
        finally { checkingGame = false; }
        UpdateLightMode();
    }

    private void UpdateLightMode()
    {
        if (!initialized) return;
        // Title bar: "Playing now · game" while a library game runs.
        PlayingPill.Visibility = lightGame is null ? Visibility.Collapsed : Visibility.Visible;
        if (lightGame is { } playing) { PlayingText.Text = text.Format("TitlePlaying", playing.Name); ToolTipService.SetToolTip(PlayingPill, text["TitlePlayingHint"]); }
        var visible = AppWindow.IsVisible && AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized };
        hiddenSince = visible ? null : hiddenSince ?? DateTimeOffset.Now;

        // Back in front: the page returns exactly where it was.
        if (visible && pageReleased) { pageReleased = false; ShowRoute(force: true); }

        // Hidden or minimized always counts as "not in front" (releasing the page can raise a spurious activation).
        var wantEfficiency = !visible || (!windowActive && lightGame is not null);
        if (wantEfficiency != efficiency) { efficiency = wantEfficiency; SetEfficiency(efficiency); }

        // Minimized or in the tray for 20 s: release the page (not while an analysis or a Score test runs on it).
        if (!visible && !pageReleased && hiddenSince is { } since && DateTimeOffset.Now - since > TimeSpan.FromSeconds(20) && !analysing && !score.Busy)
        {
            pageReleased = true;
            ContentFrame.Content = null; ContentFrame.BackStack.Clear(); displayedSectionId = null;
            Trim(force: true);
        }
        else if (efficiency) Trim(force: false);
    }

    // Gives unused memory back to Windows: a compacting collection, then the working set is trimmed (pages Lotus needs
    // again are read back on demand, so nothing is lost). At most once a minute unless forced.
    private void Trim(bool force)
    {
        if (!force && DateTimeOffset.Now - lastTrim < TimeSpan.FromMinutes(1)) return;
        lastTrim = DateTimeOffset.Now;
        _ = Task.Run(() =>
        {
            try
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                K32EmptyWorkingSet(GetCurrentProcess());
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Light mode: trim"); }
        });
    }

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
    [DllImport("kernel32.dll")] private static extern bool K32EmptyWorkingSet(IntPtr process);
    [DllImport("kernel32.dll")] private static extern bool SetPriorityClass(IntPtr process, uint priority);
    [DllImport("kernel32.dll")] private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottlingState info, uint size);
}
