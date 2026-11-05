using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lotus.Core.Maintenance;

namespace Lotus.Infrastructure.Maintenance;

// Windows Storage WMI (the same data as Settings > Storage > Disks): disk, partitions and, when Windows allows it,
// the reliability counters (temperature, wear, hours, errors). Those usually need administrator: then they stay null.
[SupportedOSPlatform("windows")]
public sealed class WindowsDiskInspector : IDiskInspector
{
    private const string Scope = @"root\Microsoft\Windows\Storage";

    public IReadOnlyList<DiskDetail> Read()
    {
        var volumes = Volumes();
        var list = new List<DiskDetail>();
        try
        {
            using var searcher = new ManagementObjectSearcher(Scope, "SELECT ObjectId, DeviceId, FriendlyName, Model, MediaType, BusType, Size, HealthStatus, OperationalStatus FROM MSFT_PhysicalDisk");
            foreach (ManagementObject d in searcher.Get())
                using (d)
                {
                    var id = d["DeviceId"]?.ToString() ?? "";
                    var media = Convert.ToInt32(d["MediaType"] ?? 0) switch { 3 => "Hdd", 4 => "Ssd", 5 => "Scm", _ => "Unknown" };
                    var health = Convert.ToInt32(d["HealthStatus"] ?? 5) switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => "Unknown" };
                    var bus = Convert.ToInt32(d["BusType"] ?? 0) switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", 8 => "RAID", 10 => "SAS", 3 => "ATA", 1 => "SCSI", _ => "" };
                    var operational = d["OperationalStatus"] is ushort[] { Length: > 0 } ops ? Operational(ops[0]) : null;
                    var model = (d["FriendlyName"] ?? d["Model"])?.ToString()?.Trim() ?? "?";
                    var (temp, wear, hours, reads, writes) = Reliability(d);
                    list.Add(new(id, model, media, bus, Convert.ToInt64(d["Size"] ?? 0L), health, operational, temp, wear, hours, reads, writes, volumes.GetValueOrDefault(id) ?? []));
                }
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException) { }
        return list;
    }

    private static string? Operational(ushort code) => code switch { 2 => "OK", 3 => "Degraded", 5 => "Predictive failure", 0xD00D => "Starting", 0xD012 => "Failed media", _ => null };

    // MSFT_PhysicalDisk → MSFT_StorageReliabilityCounter. Values Windows does not fill (or refuses) stay null.
    private static (int?, int?, long?, long?, long?) Reliability(ManagementObject disk)
    {
        try
        {
            foreach (ManagementObject r in disk.GetRelated("MSFT_StorageReliabilityCounter"))
                using (r)
                {
                    int? temp = r["Temperature"] is { } t && Convert.ToInt32(t) is > 0 and < 150 and var tv ? tv : null;
                    int? wear = r["Wear"] is { } w && Convert.ToInt32(w) is >= 0 and <= 100 and var wv ? wv : null;
                    long? hours = r["PowerOnHours"] is { } h ? Convert.ToInt64(h) : null;
                    long? reads = r["ReadErrorsTotal"] is { } re ? Convert.ToInt64(re) : null;
                    long? writes = r["WriteErrorsTotal"] is { } we ? Convert.ToInt64(we) : null;
                    return (temp, wear, hours, reads, writes);
                }
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException) { }
        return (null, null, null, null, null);
    }

    // Disk number → its lettered volumes (MSFT_Partition), with free space from DriveInfo.
    private static Dictionary<string, List<DiskVolume>> Volumes()
    {
        var map = new Dictionary<string, List<DiskVolume>>();
        try
        {
            using var searcher = new ManagementObjectSearcher(Scope, "SELECT DiskNumber, DriveLetter FROM MSFT_Partition");
            foreach (ManagementObject p in searcher.Get())
                using (p)
                {
                    if (p["DriveLetter"] is not char letter || letter == '\0') continue;
                    var drive = new DriveInfo(letter + ":\\");
                    if (!drive.IsReady) continue;
                    var key = Convert.ToString(p["DiskNumber"]) ?? "";
                    if (!map.TryGetValue(key, out var l)) map[key] = l = [];
                    l.Add(new(letter + ":", drive.AvailableFreeSpace, drive.TotalSize));
                }
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException or IOException) { }
        return map;
    }

    // chkdsk /scan: online and read-only (no repair, no restart). Visible window so the player sees the result.
    public bool StartScan(string letter)
    {
        if (letter.Length != 2 || !char.IsAsciiLetter(letter[0]) || letter[1] != ':') return false;
        try
        {
            using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/k chkdsk {letter} /scan") { UseShellExecute = true, Verb = "runas" });
            return p is not null;
        }
        catch (System.ComponentModel.Win32Exception) { return false; } // the player declined the administrator prompt
    }
}
