using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lotus.Core.Games;

namespace Lotus.Infrastructure.Games;

// GlobalMemoryStatusEx for the load; the three largest user-visible programs (by working set, the game excluded).
[SupportedOSPlatform("windows")]
public sealed class WindowsMemoryPressure : IMemoryPressure
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus { public uint Length, Load; public ulong TotalPhys, AvailPhys, TotalPage, AvailPage, TotalVirtual, AvailVirtual, AvailExtended; }
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    private static readonly HashSet<string> SystemProcesses = new(StringComparer.OrdinalIgnoreCase)
        { "System", "Idle", "Registry", "Memory Compression", "svchost", "csrss", "wininit", "winlogon", "services", "lsass", "dwm", "smss", "fontdrvhost", "MsMpEng", "Lotus.App" };

    public (double LoadPercent, IReadOnlyList<string> TopApps) Read(GameEntry? exclude)
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        var load = GlobalMemoryStatusEx(ref status) ? status.Load : 0;
        var gameDir = exclude?.InstallDir is { Length: > 0 } d ? Path.GetFullPath(d).TrimEnd('\\') + "\\" : null;
        var usage = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (SystemProcesses.Contains(p.ProcessName) || p.SessionId == 0) continue;
                    if (gameDir is not null && p.MainModule?.FileName is { } f && f.StartsWith(gameDir, StringComparison.OrdinalIgnoreCase)) continue;
                    var name = p.MainModule?.FileVersionInfo.FileDescription is { Length: > 0 } desc ? desc : p.ProcessName;
                    usage[name] = usage.GetValueOrDefault(name) + p.WorkingSet64; // group multi-process apps (browsers)
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            }
        }
        var top = usage.OrderByDescending(u => u.Value).Take(3).Select(u => $"{u.Key} ({u.Value / (1024.0 * 1024 * 1024):0.0} GB)").ToList();
        return (load, top);
    }
}
