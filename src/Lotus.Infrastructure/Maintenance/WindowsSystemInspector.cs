using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lotus.Core.Maintenance;
using Lotus.Infrastructure.Tools;
using Microsoft.Win32;

namespace Lotus.Infrastructure.Maintenance;

// Read-only: hardware names (registry/WMI), Device Manager problems (Win32_PnPEntity) and recent critical/error events from
// the sources that matter for games (crashes, disks, GPU driver resets, hardware errors). Nothing is changed.
[SupportedOSPlatform("windows")]
public sealed class WindowsSystemInspector : ISystemInspector
{
    // Sources worth showing to a player; generic service noise is left out.
    private static readonly string[] Sources =
    [
        "Microsoft-Windows-Kernel-Power", "Microsoft-Windows-WHEA-Logger", "disk", "Ntfs", "volmgr", "stornvme", "storahci",
        "nvlddmkm", "amdkmdag", "amdwddmg", "igfx", "Display", "Microsoft-Windows-Resource-Exhaustion-Detector", "BugCheck", "EventLog",
    ];

    public SystemSnapshot Read(TimeSpan eventsSince) => new(Hardware(), Devices(), Events(eventsSince));

    private static List<HardwareFact> Hardware()
    {
        var facts = new List<HardwareFact>();
        var (cpu, gpu, ram) = QuickHardwareFacts.Read();
        if (cpu is { Length: > 0 }) facts.Add(new("Cpu", cpu));
        if (new WindowsDriverInfo().Read() is { } g) facts.Add(new("Gpu", $"{g.Name} · driver {g.FriendlyVersion}" + (g.Date is { } d ? $" ({d:d})" : "")));
        else if (gpu is { Length: > 0 }) facts.Add(new("Gpu", gpu));
        if (ram is { } r) facts.Add(new("Ram", $"{r / (double)(1L << 30):0.#} GB"));
        var board = string.Join(" ", new[] { Reg(@"HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardManufacturer"), Reg(@"HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardProduct") }.Where(s => s is { Length: > 0 }));
        if (board.Length > 0) facts.Add(new("Board", board));
        if (Reg(@"HARDWARE\DESCRIPTION\System\BIOS", "BIOSVersion") is { Length: > 0 } bios) facts.Add(new("Bios", bios + (Reg(@"HARDWARE\DESCRIPTION\System\BIOS", "BIOSReleaseDate") is { Length: > 0 } bd ? $" ({bd})" : "")));
        var os = Reg(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName") ?? "Windows";
        var build = Reg(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild");
        if (build is { } b && int.TryParse(b, out var n) && n >= 22000) os = os.Replace("Windows 10", "Windows 11"); // the registry still says 10 on 11
        facts.Add(new("Os", $"{os} {Reg(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion")} (build {build})".Trim()));
        return facts;
    }
    private static string? Reg(string path, string name)
    {
        try { using var k = Registry.LocalMachine.OpenSubKey(path); return k?.GetValue(name)?.ToString()?.Trim(); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return null; }
    }

    private static List<ProblemDevice> Devices()
    {
        var list = new List<ProblemDevice>();
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Name, PNPClass, ConfigManagerErrorCode, Service FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0");
            foreach (ManagementObject d in s.Get())
                using (d)
                {
                    var code = Convert.ToInt32(d["ConfigManagerErrorCode"] ?? 0);
                    if (code is 0 or 22) continue; // 22 = turned off on purpose (by the player or Windows): not a problem
                    list.Add(new(d["Name"]?.ToString()?.Trim() ?? "?", d["PNPClass"]?.ToString() ?? "", code, d["Service"]?.ToString()));
                }
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException) { }
        return list;
    }

    private static List<SystemEvent> Events(TimeSpan since)
    {
        var list = new List<SystemEvent>();
        var ms = (long)since.TotalMilliseconds;
        var providers = string.Join(" or ", Sources.Select(p => $"@Name='{p}'"));
        var query = $"*[System[Provider[{providers}] and (Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= {ms}]]]";
        try
        {
            using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, query) { ReverseDirection = true });
            for (var i = 0; i < 300 && reader.ReadEvent() is { } e; i++)
                using (e)
                {
                    string message;
                    try { message = e.FormatDescription() ?? ""; } catch (EventLogException) { message = ""; }
                    list.Add(new(e.TimeCreated ?? DateTime.MinValue, e.ProviderName.Replace("Microsoft-Windows-", ""), e.Id, e.Level == 1 ? "Critical" : "Error", message));
                }
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException) { }
        return list;
    }

    public bool StartRepair(string tool)
    {
        var args = tool switch { "sfc" => "/k sfc /scannow", "dism" => "/k DISM /Online /Cleanup-Image /RestoreHealth", _ => null };
        if (args is null) return false;
        try { using var p = Process.Start(new ProcessStartInfo("cmd.exe", args) { UseShellExecute = true, Verb = "runas" }); return p is not null; }
        catch (System.ComponentModel.Win32Exception) { return false; } // declined at the administrator prompt
    }
}
