using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lotus.Core.Games;
using Lotus.Core.Maintenance;
using Lotus.Infrastructure.Maintenance;

namespace Lotus.Infrastructure.Games;

// The Windows side of "Liberar memória ao abrir um jogo". Only this user's session, no administrator: a process of
// another user or a protected one simply refuses (skipped). Trimming = SetProcessWorkingSetSizeEx(-1, -1): the pages go
// to the standby list / page file and come back on use; nothing is ended, nothing is changed in Windows.
[SupportedOSPlatform("windows")]
public sealed class WindowsMemoryRelief(Func<IReadOnlyCollection<string>>? gameExes = null) : IMemoryReliefSystem
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus { public uint Length, Load; public ulong TotalPhys, AvailPhys, TotalPage, AvailPage, TotalVirtual, AvailVirtual, AvailExtended; }
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetProcessWorkingSetSizeEx(IntPtr h, nint min, nint max, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
    private const uint SetQuota = 0x0100, QueryLimited = 0x1000;

    public long AvailableBytes()
    {
        var s = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref s) ? (long)s.AvailPhys : 0;
    }

    public IReadOnlyList<ReliefProcess> Processes()
    {
        var session = Process.GetCurrentProcess().SessionId;
        var games = gameExes?.Invoke() ?? [];
        var list = new List<ReliefProcess>();
        foreach (var p in Process.GetProcesses())
            using (p)
                try
                {
                    if (p.SessionId != session) continue;
                    var path = WindowsProcessSampler.PathOf(p.Id);
                    list.Add(new(p.Id, p.ProcessName, path, ProcessRules.Classify(p.ProcessName, path, games), p.MainWindowHandle != IntPtr.Zero, p.WorkingSet64));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        return list;
    }

    public int ForegroundPid() { GetWindowThreadProcessId(GetForegroundWindow(), out var pid); return pid; }

    // Same as clicking X (the program may ask to save); checked again that it is still the same, allowed program.
    public bool CloseGracefully(ReliefProcess process)
    {
        try
        {
            using var p = Process.GetProcessById(process.Pid);
            if (!p.ProcessName.Equals(process.ExeName, StringComparison.OrdinalIgnoreCase) || p.SessionId != Process.GetCurrentProcess().SessionId) return false;
            if (ProcessRules.Classify(p.ProcessName, WindowsProcessSampler.PathOf(p.Id), gameExes?.Invoke() ?? []) != ProcessKind.Normal) return false;
            return p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    public long Trim(ReliefProcess process)
    {
        try
        {
            using var p = Process.GetProcessById(process.Pid);
            if (!p.ProcessName.Equals(process.ExeName, StringComparison.OrdinalIgnoreCase)) return 0;
            var before = p.WorkingSet64;
            var h = OpenProcess(SetQuota | QueryLimited, false, process.Pid);
            if (h == IntPtr.Zero) return 0;
            try { if (!SetProcessWorkingSetSizeEx(h, -1, -1, 0)) return 0; } finally { CloseHandle(h); }
            p.Refresh();
            return Math.Max(0, before - p.WorkingSet64);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return 0; }
    }

    // Lotus itself, once each time it goes to the tray (after its page and images were released): a compacting
    // collection, then the same trim. Returns working set and private bytes before/after (measured, for the log).
    public static (long WorkingBefore, long WorkingAfter, long PrivateBefore, long PrivateAfter) TrimSelf()
    {
        using var p = Process.GetCurrentProcess();
        long ws0 = p.WorkingSet64, pr0 = p.PrivateMemorySize64;
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        SetProcessWorkingSetSizeEx(p.Handle, -1, -1, 0);
        p.Refresh();
        return (ws0, p.WorkingSet64, pr0, p.PrivateMemorySize64);
    }

    // Wallpaper Engine's own command line (documented by the app): "-control pause" / "-control play".
    public bool PauseWallpaper(string exePath) => Run(exePath, "-control pause");
    public bool ResumeWallpaper(string exePath) => Run(exePath, "-control play");
    private static bool Run(string exe, string args)
    {
        try
        {
            if (!File.Exists(exe)) return false;
            using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true });
            return p is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { return false; }
    }

    public bool IsRunning(string exeName)
    {
        var found = Process.GetProcessesByName(exeName);
        foreach (var p in found) p.Dispose();
        return found.Length > 0;
    }

    // Reopened the way the player opens it (shell), minimized so it does not cover what they are doing now.
    public bool Reopen(string exePath)
    {
        try
        {
            if (!File.Exists(exePath)) return false;
            // The shell may hand the start to an instance already running (no process object back): still started.
            using var p = Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Minimized, WorkingDirectory = Path.GetDirectoryName(exePath) ?? "" });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
    }

    public void Settle(TimeSpan time) => Thread.Sleep(time);
}
