using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lotus.Core.Tools;
using Microsoft.Win32;

namespace Lotus.Infrastructure.Tools;

// Heaviest user apps grouped by name; closing uses WM_CLOSE on the main window only (never kills).
[SupportedOSPlatform("windows")]
public sealed class WindowsAppCloser : IAppCloser
{
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "svchost", "csrss", "wininit", "winlogon", "services", "lsass", "dwm", "smss",
        "fontdrvhost", "explorer", "MsMpEng", "SecurityHealthService", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost",
        "TextInputHost", "RuntimeBroker", "sihost", "ctfmon", "audiodg", "Lotus.App", "RTSS", "MSIAfterburner", "claude", "Claude"
    };

    public IReadOnlyList<RunningApp> List()
    {
        var session = Process.GetCurrentProcess().SessionId;
        var groups = new Dictionary<string, (long Bytes, bool Window, List<int> Pids)>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.SessionId != session || Protected.Contains(p.ProcessName)) continue;
                    var name = p.MainModule?.FileVersionInfo.FileDescription is { Length: > 0 } d ? d.Trim() : p.ProcessName;
                    var g = groups.GetValueOrDefault(name, (0, false, new List<int>()));
                    g.Pids.Add(p.Id);
                    groups[name] = (g.Bytes + p.WorkingSet64, g.Window || p.MainWindowHandle != IntPtr.Zero, g.Pids);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            }
        }
        return [.. groups.Where(g => g.Value.Bytes > 150L << 20).OrderByDescending(g => g.Value.Bytes)
            .Select(g => new RunningApp(g.Key, g.Value.Bytes, g.Value.Window, g.Value.Pids))];
    }

    public int CloseGracefully(RunningApp app)
    {
        var asked = 0;
        foreach (var pid in app.Pids)
        {
            try { using var p = Process.GetProcessById(pid); if (p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow()) asked++; }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        return asked;
    }
}

// The user's %TEMP% only (no admin). Items untouched for 24 h go to the Recycle Bin, so nothing is lost for good.
[SupportedOSPlatform("windows")]
public sealed class WindowsTempCleaner(string? folder = null, Func<DateTime>? clock = null) : ITempCleaner
{
    private string Folder => folder ?? Path.GetTempPath();
    private IEnumerable<FileSystemInfo> Old()
    {
        var limit = (clock ?? (() => DateTime.UtcNow))() - TimeSpan.FromHours(24);
        var dir = new DirectoryInfo(Folder);
        if (!dir.Exists) yield break;
        foreach (var e in dir.EnumerateFileSystemInfos())
            if (e.LastWriteTimeUtc < limit && !e.Name.StartsWith("Lotus", StringComparison.OrdinalIgnoreCase)) yield return e;
    }
    private static long SizeOf(FileSystemInfo e)
    {
        try
        {
            return e is FileInfo f ? f.Length
                : ((DirectoryInfo)e).EnumerateFiles("*", new System.IO.EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).Sum(x => x.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }
    public TempScan Scan() { var items = Old().ToList(); return new(items.Count, items.Sum(SizeOf)); }
    public TempScan Recycle()
    {
        var items = Old().ToList(); int moved = 0; long bytes = 0;
        foreach (var e in items)
        {
            var size = SizeOf(e);
            if (RecycleBin.Send(e.FullName)) { moved++; bytes += size; } // in-use items stay where they are
        }
        return new(moved, bytes);
    }
}

[SupportedOSPlatform("windows")]
public static class RecycleBin
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOp { public IntPtr hwnd; public uint func; public string from; public string? to; public ushort flags; public bool aborted; public IntPtr mappings; public string? title; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHFileOperationW(ref ShFileOp op);
    private const uint Delete = 3; private const ushort AllowUndo = 0x40, NoConfirmation = 0x10, Silent = 0x4, NoErrorUi = 0x400;
    // True when the item left its folder (moved to the Recycle Bin).
    public static bool Send(string path)
    {
        var op = new ShFileOp { func = Delete, from = path + "\0\0", flags = AllowUndo | NoConfirmation | Silent | NoErrorUi };
        return SHFileOperationW(ref op) == 0 && !op.aborted && !File.Exists(path) && !Directory.Exists(path);
    }
}

// 20 pings to the router (local network) and to two public resolvers (internet).
public sealed class PingConnectionTester(int count = 20) : IConnectionTester
{
    public async Task<IReadOnlyList<PingStats>> RunAsync(CancellationToken token)
    {
        var targets = new List<(string Label, string Address)>();
        var gateway = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().GatewayAddresses).Select(g => g.Address)
            .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !a.Equals(System.Net.IPAddress.Any));
        if (gateway is not null) targets.Add(("gateway", gateway.ToString()));
        targets.Add(("Cloudflare 1.1.1.1", "1.1.1.1")); targets.Add(("Google 8.8.8.8", "8.8.8.8"));
        var results = await Task.WhenAll(targets.Select(t => Measure(t.Label, t.Address, token)));
        return results;
    }
    private async Task<PingStats> Measure(string label, string address, CancellationToken token)
    {
        using var ping = new Ping(); var times = new List<double>(); var lost = 0;
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            try { var r = await ping.SendPingAsync(address, 1000); if (r.Status == IPStatus.Success) times.Add(r.RoundtripTime); else lost++; }
            catch (PingException) { lost++; }
            await Task.Delay(100, token);
        }
        double? avg = times.Count > 0 ? times.Average() : null;
        double? jitter = times.Count > 1 ? times.Zip(times.Skip(1), (a, b) => Math.Abs(a - b)).Average() : null;
        return new(label, avg, jitter, lost * 100.0 / count);
    }
}

// Read-only: Storage WMI (no admin) for media type and health; DriveInfo for free space.
[SupportedOSPlatform("windows")]
public sealed class WindowsDiskHealth : IDiskHealth
{
    public (IReadOnlyList<DiskInfo> Disks, IReadOnlyList<VolumeInfo> Volumes) Read()
    {
        var disks = new List<DiskInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT FriendlyName, MediaType, HealthStatus, Size FROM MSFT_PhysicalDisk");
            foreach (ManagementObject d in searcher.Get())
                using (d)
                {
                    var media = Convert.ToInt32(d["MediaType"] ?? 0) switch { 3 => "Hdd", 4 => "Ssd", 5 => "Scm", _ => "Unknown" };
                    var health = Convert.ToInt32(d["HealthStatus"] ?? 5) switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => "Unknown" };
                    disks.Add(new(d["FriendlyName"]?.ToString()?.Trim() ?? "?", media, health, Convert.ToInt64(d["Size"] ?? 0L)));
                }
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException) { }
        var volumes = DriveInfo.GetDrives().Where(v => v.DriveType == DriveType.Fixed && v.IsReady)
            .Select(v => new VolumeInfo(v.Name.TrimEnd('\\'), v.AvailableFreeSpace, v.TotalSize)).ToList();
        return (disks, volumes);
    }
}

// Display adapter with the most dedicated memory: name, driver version and date (read-only registry).
[SupportedOSPlatform("windows")]
public sealed class WindowsDriverInfo : IDriverInfo
{
    private const string DisplayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    public GpuDriver? Read()
    {
        using var root = Registry.LocalMachine.OpenSubKey(DisplayClass);
        if (root is null) return null;
        GpuDriver? best = null; long bestMemory = -1;
        foreach (var sub in root.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
        {
            using var k = root.OpenSubKey(sub);
            if (k?.GetValue("DriverDesc") is not string name || k.GetValue("DriverVersion") is not string version) continue;
            var memory = k.GetValue("HardwareInformation.qwMemorySize") switch { long q => q, byte[] b when b.Length >= 8 => BitConverter.ToInt64(b), _ => 0 };
            if (memory <= bestMemory) continue;
            DateTime? date = k.GetValue("DriverDate") is string s && DateTime.TryParseExact(s, ["M-d-yyyy", "MM-dd-yyyy"], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null;
            var provider = (k.GetValue("ProviderName") as string ?? name);
            var vendor = provider.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? "NVIDIA"
                : provider.Contains("AMD", StringComparison.OrdinalIgnoreCase) || provider.Contains("Advanced Micro", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? "AMD"
                : provider.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "Intel" : "Other";
            best = new(name, version, date, vendor); bestMemory = memory;
        }
        return best;
    }
}
