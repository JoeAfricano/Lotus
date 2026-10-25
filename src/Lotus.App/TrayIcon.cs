using System.Runtime.InteropServices;

namespace Lotus.App;

// Notification-area icon and the global hotkey, owned by a hidden message-only window.
// Left click opens Lotus; right click shows "Open Lotus / Exit". The hotkey (Ctrl+Shift+L) works even while a game is in front.
public sealed class TrayIcon : IDisposable
{
    public event EventHandler? Open, Exit, Hotkey;
    private const uint WmUser = 0x0400, Callback = WmUser + 1, WmHotkey = 0x0312, WmLButtonUp = 0x0202, WmRButtonUp = 0x0205, WmCommand = 0x0111;
    private const int HotkeyId = 0x4C54; // "LT"
    public const uint ModCtrl = 0x2, ModShift = 0x4, KeyL = 0x4C;
    private delegate IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    private readonly WndProc proc;
    private readonly IntPtr hwnd;
    private NotifyIconData data;
    private bool added;
    private readonly string openText, exitText;
    public bool HotkeyRegistered { get; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize; public IntPtr hWnd; public uint uID, uFlags, uCallbackMessage; public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass { public uint cbSize, style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIconW(uint msg, ref NotifyIconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassExW(ref WndClass c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowExW(int ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string text);
    [DllImport("user32.dll")] private static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int r, IntPtr h, IntPtr rect);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImageW(IntPtr inst, string name, uint type, int cx, int cy, uint load);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);

    public TrayIcon(string tooltip, string openText, string exitText)
    {
        this.openText = openText; this.exitText = exitText;
        proc = Handle;
        var inst = GetModuleHandleW(null);
        var wc = new WndClass { cbSize = (uint)Marshal.SizeOf<WndClass>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc), hInstance = inst, lpszClassName = "LotusTray" + Environment.ProcessId };
        RegisterClassExW(ref wc);
        hwnd = CreateWindowExW(0, wc.lpszClassName, "Lotus tray", 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, inst, IntPtr.Zero); // HWND_MESSAGE
        var ico = Path.Combine(AppContext.BaseDirectory, "Assets", "Lotus.ico");
        data = new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(), hWnd = hwnd, uID = 1, uFlags = 0x1 | 0x2 | 0x4, uCallbackMessage = Callback,
            hIcon = File.Exists(ico) ? LoadImageW(IntPtr.Zero, ico, 1, 32, 32, 0x10) : IntPtr.Zero, szTip = tooltip, szInfo = "", szInfoTitle = ""
        };
        HotkeyRegistered = RegisterHotKey(hwnd, HotkeyId, ModCtrl | ModShift, KeyL);
    }

    public void Show() { if (!added) added = Shell_NotifyIconW(0, ref data); } // NIM_ADD
    public void Hide() { if (added) { Shell_NotifyIconW(2, ref data); added = false; } } // NIM_DELETE
    // A small balloon next to the clock (used the first time Lotus goes to the background).
    public void Notify(string title, string text)
    {
        Show();
        var n = data; n.uFlags = 0x10; n.szInfoTitle = title; n.szInfo = text; n.dwInfoFlags = 0x1; // NIF_INFO, NIIF_INFO
        Shell_NotifyIconW(1, ref n); // NIM_MODIFY
    }

    private IntPtr Handle(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        if (msg == WmHotkey && w.ToInt32() == HotkeyId) { Hotkey?.Invoke(this, EventArgs.Empty); return IntPtr.Zero; }
        if (msg == Callback)
        {
            var e = (uint)l.ToInt64() & 0xFFFF;
            if (e == WmLButtonUp) Open?.Invoke(this, EventArgs.Empty);
            else if (e == WmRButtonUp) ShowMenu();
            return IntPtr.Zero;
        }
        return DefWindowProcW(h, msg, w, l);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenuW(menu, 0, new UIntPtr(1), openText); AppendMenuW(menu, 0x800, UIntPtr.Zero, ""); AppendMenuW(menu, 0, new UIntPtr(2), exitText);
        GetCursorPos(out var p); SetForegroundWindow(hwnd);
        var chosen = TrackPopupMenu(menu, 0x100 | 0x80, p.X, p.Y, 0, hwnd, IntPtr.Zero); // TPM_RETURNCMD | TPM_NONOTIFY
        DestroyMenu(menu);
        if (chosen == 1) Open?.Invoke(this, EventArgs.Empty); else if (chosen == 2) Exit?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        Hide();
        if (HotkeyRegistered) UnregisterHotKey(hwnd, HotkeyId);
        DestroyWindow(hwnd);
    }
}
