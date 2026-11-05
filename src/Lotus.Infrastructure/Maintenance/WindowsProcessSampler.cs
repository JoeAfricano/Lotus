using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Lotus.Core.Maintenance;

namespace Lotus.Infrastructure.Maintenance;

// Only this user's session. RAM = working set (what the program holds in memory now); CPU = processor time used between
// two readings ~1 s apart, as a share of all cores. Nothing is trimmed or "optimized": reading changes nothing.
[SupportedOSPlatform("windows")]
public sealed class WindowsProcessSampler(Func<IReadOnlyCollection<string>>? gameExes = null, int intervalMs = 1000) : IProcessSampler
{
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, StringBuilder name, ref int size);
    private const uint QueryLimited = 0x1000;

    public static string? PathOf(int pid)
    {
        var h = OpenProcess(QueryLimited, false, pid);
        if (h == IntPtr.Zero) return null;
        try { var sb = new StringBuilder(1024); var size = sb.Capacity; return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString(0, size) : null; }
        finally { CloseHandle(h); }
    }

    private static Dictionary<int, TimeSpan> CpuTimes(IEnumerable<Process> list)
    {
        var d = new Dictionary<int, TimeSpan>();
        foreach (var p in list) try { d[p.Id] = p.TotalProcessorTime; } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
        return d;
    }

    public async Task<IReadOnlyList<ProcessSample>> SampleAsync(CancellationToken token = default) => await Task.Run(async () =>
    {
        var session = Process.GetCurrentProcess().SessionId;
        var first = Process.GetProcesses().Where(p => { try { return p.SessionId == session; } catch (InvalidOperationException) { return false; } }).ToList();
        var t0 = CpuTimes(first); var w = Stopwatch.StartNew();
        foreach (var p in first) p.Dispose();
        await Task.Delay(intervalMs, token);
        var second = Process.GetProcesses().Where(p => { try { return p.SessionId == session; } catch (InvalidOperationException) { return false; } }).ToList();
        var t1 = CpuTimes(second); var elapsed = w.Elapsed.TotalMilliseconds * Environment.ProcessorCount;
        var games = gameExes?.Invoke() ?? [];
        var groups = new Dictionary<string, (string Name, string? Path, long Bytes, double Cpu, bool Window, List<int> Pids)>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in second)
        {
            using (p)
            {
                try
                {
                    var exe = p.ProcessName;
                    var cpu = t1.TryGetValue(p.Id, out var b) && t0.TryGetValue(p.Id, out var a) && elapsed > 0 ? Math.Max(0, (b - a).TotalMilliseconds * 100 / elapsed) : 0;
                    if (!groups.TryGetValue(exe, out var g))
                    {
                        var path = PathOf(p.Id);
                        string name = exe;
                        try { if (path is not null && FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } desc) name = desc.Trim(); } catch (Exception ex) when (ex is IOException or FileNotFoundException) { }
                        g = (name, path, 0, 0, false, []);
                    }
                    g.Pids.Add(p.Id);
                    groups[exe] = (g.Name, g.Path, g.Bytes + p.WorkingSet64, g.Cpu + cpu, g.Window || p.MainWindowHandle != IntPtr.Zero, g.Pids);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
        return (IReadOnlyList<ProcessSample>)[.. groups.Select(kv => new ProcessSample(kv.Value.Name, kv.Key, kv.Value.Path, kv.Value.Bytes, Math.Round(kv.Value.Cpu, 1), kv.Value.Window, kv.Value.Pids,
            ProcessRules.Classify(kv.Key, kv.Value.Path, games)))];
    }, token);

    // Each process is checked again before anything is sent to it (same program, same session, still allowed).
    private static Process? Allowed(int pid, string exeName, IReadOnlyCollection<string> games)
    {
        try
        {
            var p = Process.GetProcessById(pid);
            if (p.SessionId != Process.GetCurrentProcess().SessionId || !p.ProcessName.Equals(exeName, StringComparison.OrdinalIgnoreCase)
                || ProcessRules.Classify(p.ProcessName, PathOf(pid), games) != ProcessKind.Normal || p.Id == Environment.ProcessId) { p.Dispose(); return null; }
            return p;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }

    public int CloseGracefully(ProcessSample app)
    {
        var asked = 0; var games = gameExes?.Invoke() ?? [];
        foreach (var pid in app.Pids)
            if (Allowed(pid, app.ExeName, games) is { } p)
                using (p) try { if (p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow()) asked++; } catch (InvalidOperationException) { }
        return asked;
    }

    public int ForceEnd(ProcessSample app)
    {
        var ended = 0; var games = gameExes?.Invoke() ?? [];
        foreach (var pid in app.Pids)
            if (Allowed(pid, app.ExeName, games) is { } p)
                using (p) try { p.Kill(); ended++; } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        return ended;
    }
}

public sealed class JsonCloseList(string file) : ICloseListStore
{
    public IReadOnlyList<string> Load()
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file)) ?? [] : []; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }
    public void Save(IReadOnlyList<string> exeNames)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(exeNames));
        File.Move(file + ".tmp", file, true);
    }
}
