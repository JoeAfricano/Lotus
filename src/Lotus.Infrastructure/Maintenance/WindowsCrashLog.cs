using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;
using Lotus.Core.Maintenance;

namespace Lotus.Infrastructure.Maintenance;

// Read-only. Application log: "Application Error" (1000) and "Application Hang" (1002), read by field position so it
// works in any Windows language. System log: the signals that explain a crash (GPU driver reset, power loss, memory,
// hardware and disk errors).
[SupportedOSPlatform("windows")]
public sealed class WindowsCrashLog : ICrashLog
{
    public IReadOnlyList<AppCrash> Crashes(TimeSpan since)
    {
        var list = new List<AppCrash>();
        var query = $"*[System[(Provider[@Name='Application Error'] and EventID=1000) or (Provider[@Name='Application Hang'] and EventID=1002)] and System[TimeCreated[timediff(@SystemTime) <= {(long)since.TotalMilliseconds}]]]";
        foreach (var e in Read("Application", query, 400))
            using (e)
            {
                var p = e.Properties.Select(x => x.Value?.ToString()).ToList();
                string? At(int i) => i < p.Count && p[i] is { Length: > 0 } v ? v : null;
                if (e.Id == 1000)
                    list.Add(new(e.TimeCreated ?? DateTime.MinValue, CrashKind.Crash, At(0) ?? "?", At(10), At(3), At(6) is { } code ? code.Replace("0x", "", StringComparison.OrdinalIgnoreCase) : null));
                else
                    list.Add(new(e.TimeCreated ?? DateTime.MinValue, CrashKind.Hang, At(0) ?? "?", At(5) is { } path && path.Contains('\\') ? path : null, null, null));
            }
        return list;
    }

    public IReadOnlyList<SignalEvent> Signals(TimeSpan since)
    {
        var ms = (long)since.TotalMilliseconds;
        var query = $"*[System[(Provider[@Name='nvlddmkm' or @Name='amdkmdag' or @Name='amdwddmg' or @Name='igfx'] and (EventID=153 or EventID=4101 or EventID=13 or EventID=14)) "
            + "or (Provider[@Name='Display'] and EventID=4101) "
            + "or (Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41) "
            + "or (Provider[@Name='Microsoft-Windows-Resource-Exhaustion-Detector'] and EventID=2004) "
            + "or (Provider[@Name='Microsoft-Windows-WHEA-Logger'] and (Level=1 or Level=2)) "
            + $"or (Provider[@Name='disk' or @Name='Ntfs' or @Name='stornvme' or @Name='storahci'] and (Level=1 or Level=2))] and System[TimeCreated[timediff(@SystemTime) <= {ms}]]]";
        var list = new List<SignalEvent>();
        foreach (var e in Read("System", query, 400))
            using (e)
            {
                var source = e.ProviderName;
                var signal = source switch
                {
                    "Microsoft-Windows-Kernel-Power" => SystemSignal.PowerLoss,
                    "Microsoft-Windows-Resource-Exhaustion-Detector" => SystemSignal.OutOfMemory,
                    "Microsoft-Windows-WHEA-Logger" => SystemSignal.HardwareError,
                    "disk" or "Ntfs" or "stornvme" or "storahci" => SystemSignal.DiskError,
                    _ => SystemSignal.GpuReset,
                };
                list.Add(new(e.TimeCreated ?? DateTime.MinValue, signal, source.Replace("Microsoft-Windows-", "")));
            }
        return list;
    }

    private static IEnumerable<EventRecord> Read(string logName, string query, int max)
    {
        EventLogReader reader;
        try { reader = new EventLogReader(new EventLogQuery(logName, PathType.LogName, query) { ReverseDirection = true }); }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException) { yield break; }
        using (reader)
            for (var i = 0; i < max; i++)
            {
                EventRecord? e;
                try { e = reader.ReadEvent(); } catch (EventLogException) { yield break; }
                if (e is null) yield break;
                yield return e;
            }
    }
}
