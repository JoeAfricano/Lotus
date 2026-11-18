using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lotus.Core.Benchmark;

namespace Lotus.Infrastructure.Games;

// "Janela Lotus", experimental: the overlay drawn by Lotus in a window of its own above the game, nothing injected.
// - A layered, topmost, click-through tool window (WS_EX_LAYERED | TRANSPARENT | TOPMOST | TOOLWINDOW | NOACTIVATE):
//   no taskbar, no Alt+Tab, never takes focus. Drawn with GDI+ into a premultiplied bitmap and handed to DWM with
//   UpdateLayeredWindow 4 times a second, never per game frame.
// - Hidden by alpha 0 instead of ShowWindow (a window leaving composition makes DWM reshuffle display planes: CapFrameX
//   saw 1-2 s black screens with VRR and frame generation), and while the game is not in front or runs in exclusive
//   fullscreen (a window cannot sit above it).
// - What it shows follows OverlayWindowSettings (items and order, position, opacity, size, three profiles); Ctrl+Alt+O
//   moves it to the next corner. Frames from RivaTuner's frametime pipe (every frame), sensors from Lotus's telemetry.
// Written for Lotus from the Windows documentation. The approach (window above the game, MPO check, transparent hide)
// and the set of options follow CapFrameX's overlay (MIT, © DevTechProfile, github.com/CXWorld/CapFrameX).
[SupportedOSPlatform("windows")]
public sealed class GameOverlayWindow : IDisposable
{
    public sealed record Sensors(double? GpuLoad, double? GpuTemp, double? CpuLoad, double? CpuTemp, double? VramLoad, double? RamLoad);
    public sealed record Look(Color Accent, Color Detail, double? CapFps, string AverageLabel, string Unavailable, OverlayWindowSettings Settings, string? LogoPath = null);

    private readonly int pid;
    private readonly OverlayFrames frames = new();
    private readonly Func<Sensors> sensors;
    private readonly Func<Look> look;
    private readonly Action? nextPosition;
    private readonly Action? toggleVisible;
    private bool positionHotkey, toggleHotkey;
    private uint toggleKey;
    private readonly Thread thread;
    private IntPtr hwnd;
    private IDisposable? watch;
    private volatile bool closing;
    private uint threadId;
    private Bitmap? logo;
    private string? logoPath;
    public int Renders { get; private set; }
    public bool Shown { get; private set; }

    public GameOverlayWindow(int gamePid, Func<Sensors> sensorsSource, Func<Look> lookSource, Action? onNextPosition = null, Action? onToggleVisible = null)
    {
        pid = gamePid; sensors = sensorsSource; look = lookSource; nextPosition = onNextPosition; toggleVisible = onToggleVisible;
        watch = RtssFrametimeListener.Shared.Watch(pid, frames.Add);
        thread = new Thread(Run) { IsBackground = true, Name = "Lotus overlay window" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    // False when RivaTuner's pipe is not available for this game (it opened before Lotus, or RivaTuner is closed).
    public bool ReceivingFrames => watch is not null;

    public void Dispose()
    {
        closing = true;
        watch?.Dispose(); watch = null;
        if (threadId != 0) PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join(2000);
        logo?.Dispose();
    }

    public const int Width = 268;
    private WndProc? proc; // kept alive for the window's lifetime

    private void Run()
    {
        threadId = GetCurrentThreadId();
        proc = (h, m, w, l) => DefWindowProc(h, m, w, l);
        var className = "LotusGameOverlay_" + pid + "_" + threadId;
        var cls = new WNDCLASSEX { cbSize = Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc), hInstance = GetModuleHandle(null), lpszClassName = className };
        RegisterClassEx(ref cls);
        hwnd = CreateWindowEx(WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, className, "Lotus overlay",
            WS_POPUP, 0, 0, Width, 100, IntPtr.Zero, IntPtr.Zero, cls.hInstance, IntPtr.Zero);
        if (hwnd == IntPtr.Zero) return;
        ShowWindow(hwnd, SW_SHOWNOACTIVATE);
        SetTimer(hwnd, (IntPtr)1, 100, IntPtr.Zero);
        Tick();
        while (!closing && GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_TIMER) Tick();
            else if (msg.message == WM_HOTKEY) { if(msg.wParam.ToInt64()==1) nextPosition?.Invoke(); else if(msg.wParam.ToInt64()==2) toggleVisible?.Invoke(); Tick(); }
            else { TranslateMessage(ref msg); DispatchMessage(ref msg); }
        }
        ReleaseHotkeys();
        KillTimer(hwnd, (IntPtr)1);
        DestroyWindow(hwnd);
        UnregisterClass(className, cls.hInstance);
    }

    // Shown only while the game's window is in front and not in exclusive fullscreen.
    private void Tick()
    {
        try
        {
            var fg = GetForegroundWindow();
            GetWindowThreadProcessId(fg, out var fgPid);
            var exclusive = SHQueryUserNotificationState(out var state) == 0 && state == 3; // QUNS_RUNNING_D3D_FULL_SCREEN
            if (fgPid != pid || exclusive) { ReleaseHotkeys(); if (Shown) { Hide(); Shown = false; } return; }
            var lk = look();
            if (!positionHotkey && nextPosition is not null) positionHotkey = RegisterHotKey(hwnd,1,MOD_CONTROL|MOD_ALT|MOD_NOREPEAT,0x4F);
            var wanted = lk.Settings.ToggleKey is "none" ? 0u : uint.TryParse(lk.Settings.ToggleKey.AsSpan(1),out var fn) ? 0x6Fu+fn : 0u;
            if (wanted != toggleKey) { if(toggleHotkey)UnregisterHotKey(hwnd,2); toggleHotkey=false;toggleKey=wanted; }
            if (!toggleHotkey && toggleKey!=0 && toggleVisible is not null) toggleHotkey=RegisterHotKey(hwnd,2,MOD_CONTROL|MOD_ALT|MOD_NOREPEAT,toggleKey);
            if (!lk.Settings.Visible) { if(Shown){Hide();Shown=false;} return; }
            // Anchor to the render client's screen coordinates, not to the monitor containing the window.
            var origin = new POINT();
            if (!GetClientRect(fg, out var client) || !ClientToScreen(fg, ref origin)
                || client.Right <= 0 || client.Bottom <= 0) { if (Shown) { Hide(); Shown = false; } return; }
            var monitor = MonitorFromWindow(fg, 2);
            GetDpiForMonitor(monitor, 0, out var dpiX, out _);
            var scale = Math.Clamp(dpiX / 96.0, 1, 3) * lk.Settings.Zoom;
            Render(new Rectangle(origin.x, origin.y, client.Right, client.Bottom), scale, lk);
            Shown = true;
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or InvalidOperationException or IOException) { Serilog.Log.Debug(ex, "Overlay window"); }
    }

    private void ReleaseHotkeys()
    {
        if(positionHotkey)UnregisterHotKey(hwnd,1);
        if(toggleHotkey)UnregisterHotKey(hwnd,2);
        positionHotkey=toggleHotkey=false;
    }

    private void Hide()
    {
        // Same size, alpha 0: the window stays composed (no plane reshuffle), it just shows nothing.
        var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 0, AlphaFormat = 1 };
        UpdateLayeredWindow(hwnd, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref blend, ULW_ALPHA);
    }

    // Shared geometry for real windows and regression tests. Small clients scale the complete card down to fit.
    public static (Rectangle Bounds, double Scale) Placement(Rectangle client, double requestedScale, OverlayWindowSettings settings)
    {
        if (client.Width <= 0 || client.Height <= 0 || !double.IsFinite(requestedScale) || requestedScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(client));
        var height = Measure(settings);
        var margin = Math.Min((int)Math.Ceiling(16 * requestedScale / settings.Zoom), Math.Min(client.Width, client.Height) / 4);
        var availableWidth = client.Width - 2 * margin; var availableHeight = client.Height - 2 * margin;
        var scale = Math.Min(requestedScale, Math.Min(availableWidth / (double)Width, availableHeight / (double)height));
        var w = Math.Min(availableWidth, (int)Math.Ceiling(Width * scale));
        var h = Math.Min(availableHeight, (int)Math.Ceiling(height * scale));
        var x = settings.Position is OverlayWindowSettings.TopRight or OverlayWindowSettings.BottomRight ? client.Right - w - margin : client.Left + margin;
        var y = settings.Position is OverlayWindowSettings.BottomLeft or OverlayWindowSettings.BottomRight ? client.Bottom - h - margin : client.Top + margin;
        return (new Rectangle(x,y,w,h), scale);
    }

    private void Render(Rectangle client, double scale, Look lk)
    {
        if (lk.LogoPath != logoPath) { logo?.Dispose(); logo = LoadLogo(lk.LogoPath); logoPath = lk.LogoPath; }
        var s = frames.Summarize();
        var placement = Placement(client, scale, lk.Settings);
        scale = placement.Scale;
        var bounds = placement.Bounds; int w = bounds.Width, h = bounds.Height;
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; // ClearType does not mix with per-pixel alpha
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.ScaleTransform((float)scale, (float)scale);
            Draw(g, s, sensors(), lk, logo);
        }
        var screen = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(screen);
        var hbmp = bmp.GetHbitmap(Color.FromArgb(0));
        var old = SelectObject(mem, hbmp);
        try
        {
            var size = new SIZE { cx = w, cy = h }; var src = new POINT(); var pos = new POINT { x = bounds.X, y = bounds.Y };
            var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(hwnd, screen, ref pos, ref size, mem, ref src, 0, ref blend, ULW_ALPHA);
            Renders++;
        }
        finally { SelectObject(mem, old); DeleteObject(hbmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen); }
    }

    private static Bitmap? LoadLogo(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        try { using var icon = new Icon(path, 32, 32); return icon.ToBitmap(); }
        catch (Exception ex) when (ex is ArgumentException or IOException or ExternalException) { return null; }
    }

    // ---- Layout (logical pixels at 96 DPI): one block per shown item, in the player's order ----
    private const float Pad = 16, Header = 30;
    public static float RowHeight(string item, OverlayWindowSettings st) => item switch
    {
        "fps" => 50,
        "lows" => st.Items.ToList() is var order && order.Contains("fps") && order.IndexOf("lows") == order.IndexOf("fps") + 1 ? 0 : 30, // next to the FPS when right after it
        "frametime" => 22, "graph" => 62, "gpu" or "cpu" => 30, "memory" => 22, "average" => 20, _ => 0,
    };
    public static float Measure(OverlayWindowSettings st) => 12 + Header + st.Items.Sum(i => RowHeight(i, st)) + 10;

    private static readonly Color Ink = Color.FromArgb(255, 0xF8, 0xF5, 0xFA), Muted = Color.FromArgb(255, 0xBE, 0xB8, 0xC1);

    public static void Draw(Graphics g, OverlayFrames.Summary? s, Sensors sen, Look lk, Image? logo = null)
    {
        var st = lk.Settings;
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        string N(double? v, string fmt = "0") => v is { } d && double.IsFinite(d) ? d.ToString(fmt, culture) : lk.Unavailable;
        var height = Measure(st);
        // Card: dark glass body (opacity from the settings), rounded, accent → detail border, a soft accent glow on top.
        var card = new RectangleF(1, 1, Width - 2, height - 2);
        using (var path = Rounded(card, 16))
        {
            using var body = new SolidBrush(Color.FromArgb((int)(255 * st.Opacity), 0x14, 0x0B, 0x14)); g.FillPath(body, path);
            using var glow = new LinearGradientBrush(new RectangleF(0, 0, Width, 60), Color.FromArgb((int)(60 * st.Opacity), lk.Accent), Color.FromArgb(0, lk.Accent), 90f);
            g.SetClip(path); g.FillRectangle(glow, 0, 0, Width, 60); g.ResetClip();
            using var border = new LinearGradientBrush(card, lk.Accent, lk.Detail, 45f);
            using var pen = new Pen(border, 1.5f); g.DrawPath(pen, path);
        }
        using var detail = new SolidBrush(lk.Detail); using var accent = new SolidBrush(lk.Accent);
        using var ink = new SolidBrush(Ink); using var muted = new SolidBrush(Muted);
        using var small = new Font("Segoe UI Semibold", 8.5f, GraphicsUnit.Point);
        using var body9 = new Font("Segoe UI", 9.5f, GraphicsUnit.Point);
        using var big = new Font("Segoe UI Semibold", 28f, GraphicsUnit.Point);

        // Header: the Lotus flower and the name, like the app's sidebar.
        float y = 12;
        if (logo is not null) g.DrawImage(logo, Pad, y, 20, 20);
        g.DrawString("LOTUS", small, detail, Pad + (logo is null ? 0 : 26), y + 2);
        y += Header;

        foreach (var item in st.Items)
        {
            switch (item)
            {
                case "fps":
                    g.DrawString(N(s?.Fps), big, ink, Pad - 4, y - 8);
                    var fpsWidth = g.MeasureString(N(s?.Fps), big).Width;
                    g.DrawString("FPS", small, accent, Pad - 4 + fpsWidth - 6, y + 26);
                    if (RowHeight("lows", st) == 0) Pills(g, s, N, small, lk, 160, y - 4);
                    break;
                case "lows":
                    if (RowHeight("lows", st) > 0) Pills(g, s, N, small, lk, Pad, y, horizontal: true);
                    break;
                case "frametime":
                    g.DrawString(N(s?.FrametimeMs, "0.0") + " ms", body9, ink, Pad, y);
                    break;
                case "graph":
                    Graph(g, s, lk, new RectangleF(Pad, y + 4, 200, 50), small, muted, accent, culture);
                    break;
                case "gpu": Row(g, y, "GPU", sen.GpuLoad, sen.GpuTemp, N, small, body9, detail, ink, accent); break;
                case "cpu": Row(g, y, "CPU", sen.CpuLoad, sen.CpuTemp, N, small, body9, detail, ink, accent); break;
                case "memory":
                    g.DrawString("VRAM", small, detail, Pad, y + 2); g.DrawString(N(sen.VramLoad) + "%", body9, ink, 56, y);
                    g.DrawString("RAM", small, detail, 120, y + 2); g.DrawString(N(sen.RamLoad) + "%", body9, ink, 154, y);
                    break;
                case "average":
                    g.DrawString(string.Format(culture, lk.AverageLabel, N(s?.Average)), small, muted, Pad, y + 2);
                    break;
            }
            y += RowHeight(item, st);
        }
    }

    private static void Pills(Graphics g, OverlayFrames.Summary? s, Func<double?, string, string> n, Font font, Look lk, float x, float y, bool horizontal = false)
    {
        Pill(g, new RectangleF(x, y, 92, 22), "1%  " + n(s?.Low1, "0"), lk.Accent, 0x66, font);
        Pill(g, horizontal ? new RectangleF(x + 100, y, 92, 22) : new RectangleF(x, y + 26, 92, 22), "0,1%  " + n(s?.Low01, "0"), lk.Accent, 0x38, font);
    }

    // Frametime graph: 0–33.3 ms, reference lines (33 and 16.7 ms, the FPS limit dashed in the accent), recent frames.
    private static void Graph(Graphics g, OverlayFrames.Summary? s, Look lk, RectangleF graph, Font small, Brush muted, Brush accent, IFormatProvider culture)
    {
        float Y(double ms) => graph.Bottom - (float)(Math.Clamp(ms, 0, 33.3) / 33.3 * graph.Height);
        using var grid = new Pen(Color.FromArgb(0x40, Muted));
        g.DrawLine(grid, graph.Left, Y(33.3), graph.Right, Y(33.3)); g.DrawString("33", small, muted, graph.Right + 4, Y(33.3) - 8);
        var capMs = lk.CapFps is > 30 and < 1000 ? 1000 / lk.CapFps.Value : (double?)null;
        if (capMs is null || Math.Abs(Y(capMs.Value) - Y(16.7)) >= 12) { g.DrawLine(grid, graph.Left, Y(16.7), graph.Right, Y(16.7)); g.DrawString(16.7.ToString("0.0", culture), small, muted, graph.Right + 4, Y(16.7) - 8); }
        if (capMs is { } c)
        {
            using var capPen = new Pen(Color.FromArgb(0x99, lk.Accent)) { DashStyle = DashStyle.Dash };
            g.DrawLine(capPen, graph.Left, Y(c), graph.Right, Y(c)); g.DrawString(c.ToString("0.0", culture), small, accent, graph.Right + 4, Y(c) - 8);
        }
        if (s is not { Graph.Count: > 1 } sum) return;
        var pts = new PointF[sum.Graph.Count];
        for (var i = 0; i < pts.Length; i++) pts[i] = new(graph.Left + graph.Width * i / (pts.Length - 1), Y(sum.Graph[i]));
        // A faint fill under the line, then the line itself.
        using (var area = new GraphicsPath())
        {
            area.AddLines(pts); area.AddLine(pts[^1], new PointF(graph.Right, graph.Bottom)); area.AddLine(new PointF(graph.Right, graph.Bottom), new PointF(graph.Left, graph.Bottom));
            using var fill = new LinearGradientBrush(graph, Color.FromArgb(0x40, lk.Accent), Color.FromArgb(0, lk.Accent), 90f); g.FillPath(fill, area);
        }
        using var line = new Pen(lk.Accent, 1.6f) { LineJoin = LineJoin.Round };
        g.DrawLines(line, pts);
    }

    private static void Row(Graphics g, float y, string label, double? load, double? temp, Func<double?, string, string> n, Font small, Font body, Brush detail, Brush ink, Brush accent)
    {
        g.DrawString(label, small, detail, Pad, y + 2);
        g.DrawString(n(load, "0") + "%" + (temp is null ? "" : "   " + n(temp, "0") + " °C"), body, ink, 56, y);
        using var track = new SolidBrush(Color.FromArgb(0x26, 255, 255, 255));
        using var trackPath = Rounded(new RectangleF(Pad, y + 20, 236, 5), 2.5f); g.FillPath(track, trackPath);
        if (load is { } l && l > 0.5) { using var fill = Rounded(new RectangleF(Pad, y + 20, (float)Math.Max(5, 236 * Math.Clamp(l, 0, 100) / 100), 5), 2.5f); g.FillPath(accent, fill); }
    }

    private static void Pill(Graphics g, RectangleF r, string text, Color color, int alpha, Font font)
    {
        using var path = Rounded(r, r.Height / 2);
        using var fill = new SolidBrush(Color.FromArgb(alpha, color)); g.FillPath(fill, path);
        using var ink = new SolidBrush(Ink);
        var size = g.MeasureString(text, font);
        g.DrawString(text, font, ink, r.Left + (r.Width - size.Width) / 2, r.Top + (r.Height - size.Height) / 2);
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath(); var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    // ---- Win32 ----
    private const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;
    private const uint WS_POPUP = 0x80000000; private const int SW_SHOWNOACTIVATE = 4, ULW_ALPHA = 2; private const uint WM_TIMER = 0x113, WM_QUIT = 0x12, WM_HOTKEY = 0x312;
    private const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000;
    private delegate IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WNDCLASSEX { public int cbSize; public uint style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm; }
    [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string c, IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(int ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG m, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG m);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint id, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr SetTimer(IntPtr h, IntPtr id, uint ms, IntPtr fn);
    [DllImport("user32.dll")] private static extern bool KillTimer(IntPtr h, IntPtr id);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref POINT origin);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO i);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr m, int type, out uint x, out uint y);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref POINT pos, ref SIZE size, IntPtr src, ref POINT srcPos, int key, ref BLENDFUNCTION blend, int flags);
    [DllImport("user32.dll", EntryPoint = "UpdateLayeredWindow")] private static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, IntPtr pos, IntPtr size, IntPtr src, IntPtr srcPos, int key, ref BLENDFUNCTION blend, int flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
