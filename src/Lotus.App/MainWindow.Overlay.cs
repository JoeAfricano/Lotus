using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Lotus.App;

// Generated overlay layouts (Lotus Lateral, Lotus Pulse) follow the version and, for Pulse, the active theme: the
// exported copy in %LOCALAPPDATA%\Lotus\Overlays is renewed in the background when it differs (RivaTuner reads it the next
// time it starts; Lotus does not restart it for that). The player's own layouts (Top Sombra, Neon) are never rewritten.
public sealed partial class MainWindow
{
    private bool OpenOverlayPreview()
    {
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory,
            ArgumentList = { Lotus.Infrastructure.Score.D3D11GpuBenchmark.PreviewArgument }
        });
        if (process is null) return false;
        // Janela Lotus is drawn by Lotus, not OverlayEditor. Attach it to the preview PID as well as library games.
        Lotus.Infrastructure.Games.GameOverlayWindow? window = null;
        try
        {
            if (Lotus.Infrastructure.Integrations.LotusOverlayLayout.WindowActive())
                window = new(process.Id, OverlaySensors, OverlayLook,
                    () => DispatcherQueue.TryEnqueue(() => UpdateOverlaySettings(s => s.NextPosition())),
                    () => DispatcherQueue.TryEnqueue(() => UpdateOverlaySettings(s => s with { Visible = !s.Visible })));
            var ownedWindow = window;
            var disposed = 0;
            void Cleanup()
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0) return;
                ownedWindow?.Dispose(); process.Dispose();
            }
            process.Exited += (_, _) => Cleanup(); process.EnableRaisingEvents = true;
            return true;
        }
        catch { window?.Dispose(); process.Dispose(); throw; }
    }
    private int overlayRefreshQueued;

    // "Lotus (janela)" (experimental): while a library game runs and that style is RivaTuner's active layout, the Lotus
    // overlay window follows the game; it closes with the game. Telemetry keeps sampling meanwhile (GPU/CPU/RAM).
    private Lotus.Infrastructure.Games.GameOverlayWindow? overlayWindow;
    private int overlayWindowPid;
    private async Task UpdateOverlayWindowAsync(Lotus.Core.Games.GameEntry? running)
    {
        try
        {
            var process = running is null ? null : await Task.Run(() => games.GameProcess(running));
            var want = process is not null && await Task.Run(() => Lotus.Infrastructure.Integrations.LotusOverlayLayout.WindowActive());
            if (want && overlayWindow is not null && overlayWindowPid == process!.Pid) return;
            overlayWindow?.Dispose(); overlayWindow = null; overlayWindowPid = 0;
            if (want)
            {
                overlayWindowPid = process!.Pid;
                overlayWindow = new(process.Pid, OverlaySensors, OverlayLook, () => DispatcherQueue.TryEnqueue(() => UpdateOverlaySettings(s => s.NextPosition())),
                    () => DispatcherQueue.TryEnqueue(() => UpdateOverlaySettings(s => s with { Visible = !s.Visible })));
                Serilog.Log.Information("Overlay window for {Game} (pid {Pid}), frames {Frames}", running!.Name, process.Pid, overlayWindow.ReceivingFrames);
            }
            UpdateMonitoringDemand();
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Overlay window"); }
    }

    private Lotus.Infrastructure.Games.GameOverlayWindow.Sensors OverlaySensors()
    {
        var s = monitoring.Snapshot; var g = s?.Gpu.Devices.FirstOrDefault();
        double? V(Lotus.Core.Telemetry.Reading<double>? r) => r is { Status: Lotus.Core.Telemetry.TelemetryStatus.Available, Value: { } v } && double.IsFinite(v) ? v : null;
        double? G(Lotus.Core.Telemetry.GpuSensor k) => g?.Get(k) is { Status: Lotus.Core.Telemetry.TelemetryStatus.Available, Value: { } v } && double.IsFinite(v) ? v : null;
        return new(G(Lotus.Core.Telemetry.GpuSensor.Utilization), G(Lotus.Core.Telemetry.GpuSensor.Temperature),
            V(s?.Cpu.TotalUtilizationPercent), null, G(Lotus.Core.Telemetry.GpuSensor.MemoryPercent), V(s?.Memory.UsedPhysicalPercent));
    }

    private Lotus.Infrastructure.Games.GameOverlayWindow.Look? overlayLook;
    private Lotus.Infrastructure.Games.GameOverlayWindow.Look OverlayLook()
    {
        if (overlayLook is { } cached) return cached;
        var c = Lotus.Infrastructure.Integrations.LotusOverlayLayout.PulseColors;
        System.Drawing.Color Col(string hex) => System.Drawing.Color.FromArgb(255, Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex[2..4], 16), Convert.ToInt32(hex[4..6], 16));
        double? cap = null; try { cap = integrations.Rtss.GlobalCap(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return overlayLook = new(Col(c.Accent), Col(c.Detail), cap, text["OwAverage"], "—", preferences.Current.OverlayWindow, Path.Combine(AppContext.BaseDirectory, "Assets", "Lotus.ico"));
    }

    // Changes the profile in use (the picker's controls and the Ctrl+Alt+O hotkey); the window redraws with it.
    public void UpdateOverlaySettings(Func<Lotus.Core.Benchmark.OverlayWindowSettings, Lotus.Core.Benchmark.OverlayWindowSettings> change)
    {
        var p = preferences.Current;
        var profiles = (p.OverlayProfiles is { Count: 3 } list ? list : Lotus.Core.Benchmark.OverlayWindowSettings.DefaultProfiles).ToList();
        var at = Math.Clamp(p.OverlayProfile, 0, 2);
        profiles[at] = change(profiles[at].Normalized()).Normalized();
        preferences.Update(p with { OverlayProfiles = profiles });
        overlayLook = null;
    }
    public void UseOverlayProfile(int index) { preferences.Update(preferences.Current with { OverlayProfile = Math.Clamp(index, 0, 2) }); overlayLook = null; }

    private void RefreshGeneratedOverlays()
    {
        Lotus.Infrastructure.Integrations.LotusOverlayLayout.PulseColors = PulseColors(); overlayLook = null; // the window picks the new colors at its next redraw
        if (Interlocked.Exchange(ref overlayRefreshQueued, 1) == 1) return; // a refresh is already on its way (theme previews fire often)
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            Interlocked.Exchange(ref overlayRefreshQueued, 0);
            if (Lotus.Infrastructure.Integrations.LotusOverlayLayout.RefreshGenerated()) Serilog.Log.Information("Overlay: generated layouts renewed");
        });
    }

    // The overlay sits on a dark translucent panel over the game: bright accent and detail colors read best there, so a
    // light theme keeps the Lotus defaults and a custom theme lends its accent fill and detail color.
    private static Lotus.Infrastructure.Integrations.LotusPulseLayout.Colors PulseColors()
    {
        var d = Lotus.Infrastructure.Integrations.LotusPulseLayout.Default;
        if (ThemePalette.Element == ElementTheme.Light) return d;
        string Hex(string key, string fallback) =>
            Application.Current.Resources.TryGetValue(key, out var v) && v is SolidColorBrush b ? $"{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2}" : fallback;
        if (ThemePalette.Instance?.CustomTokens is { } ct)
            return d with { Accent = Lotus.Infrastructure.Integrations.LotusPulseLayout.Hex(ct.AccentText.ToString(), d.Accent), Detail = Lotus.Infrastructure.Integrations.LotusPulseLayout.Hex(ct.Detail.ToString(), d.Detail) };
        return d with { Accent = Hex("RoseBrush", d.Accent), Detail = Hex("GoldBrush", d.Detail) };
    }
}
