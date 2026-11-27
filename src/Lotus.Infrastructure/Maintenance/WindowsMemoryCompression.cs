using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Lotus.Core.Maintenance;
using Microsoft.Win32;

namespace Lotus.Infrastructure.Maintenance;

// Reads (no administrator): the "Memory Compression" process, SysMain's start type (registry) and running state (SCM query).
// Changes run Windows' own Set-Service / Enable-MMAgent in an administrator PowerShell that only writes a small result file.
// The state before the first change is kept in %LOCALAPPDATA%\Lotus\MemoryCompression.json so Desfazer restores it exactly.
[SupportedOSPlatform("windows")]
public sealed partial class WindowsMemoryCompression(string? dataRoot = null) : IMemoryCompression
{
    private string StatePath => Path.Combine(dataRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus"), "MemoryCompression.json");
    private sealed record State(MemoryCompressionBefore? Before, bool Want, DateTimeOffset ChangedAt);

    public MemoryCompressionStatus Read()
    {
        var active = CompressionRunning();
        var state = Load();
        var boot = DateTimeOffset.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
        var pending = state is not null && state.ChangedAt > boot && state.Want != active;
        return new(active, StartType(), ServiceRunning(), (long)Math.Max(0, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes), state?.Before, pending);
    }

    public async Task<MemoryCompressionResult> EnableAsync(CancellationToken token = default)
    {
        var now = Read();
        var before = Load()?.Before ?? new MemoryCompressionBefore(now.Active, now.SysMain, now.SysMainRunning, DateTimeOffset.Now);
        var r = await RunAsync("""
            Set-Service -Name SysMain -StartupType Automatic
            Start-Service -Name SysMain
            Enable-MMAgent -MemoryCompression
            """, token);
        if (!r.Started) return new(false, false, null);
        if (r.Compression is not null || r.Error is null) Save(new(before, true, DateTimeOffset.Now)); // something may have changed: keep the way back
        var ok = r.Error is null && r.Compression == true && r.Start == SysMainStart.Automatic && r.Running;
        return new(true, ok, r.Error);
    }

    public async Task<MemoryCompressionResult> UndoAsync(CancellationToken token = default)
    {
        if (Load()?.Before is not { } b) return new(true, false, "nothing saved");
        var start = b.SysMain switch { SysMainStart.Disabled => "Disabled", SysMainStart.Manual => "Manual", _ => "Automatic" };
        // Disable-MMAgent needs SysMain running, so the service is stopped last (only if it was stopped before).
        var r = await RunAsync((b.Compression ? "" : "Disable-MMAgent -MemoryCompression\n") + $"Set-Service -Name SysMain -StartupType {start}\n" + (b.SysMainRunning ? "" : "Stop-Service -Name SysMain -Force\n"), token);
        if (!r.Started) return new(false, false, null);
        var ok = r.Error is null && r.Compression == b.Compression && (b.SysMain == SysMainStart.Unknown || r.Start == b.SysMain);
        if (ok) Save(new(null, b.Compression, DateTimeOffset.Now));
        return new(true, ok, r.Error);
    }

    private sealed record Outcome(bool Started, bool? Compression, SysMainStart Start, bool Running, string? Error);
    private async Task<Outcome> RunAsync(string body, CancellationToken token)
    {
        var dir = Path.Combine(Path.GetTempPath(), "Lotus-memory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var result = Path.Combine(dir, "result.json");
        var script = Path.Combine(dir, "memory.ps1");
        await File.WriteAllTextAsync(script, $$"""
            $ErrorActionPreference = 'Stop'
            $err = $null
            try {
            {{body}}
            } catch { $err = $_.Exception.Message }
            $mm = $null; try { $mm = [bool](Get-MMAgent).MemoryCompression } catch { if (-not $err) { $err = $_.Exception.Message } }
            $svc = Get-Service -Name SysMain
            @{ compression = $mm; start = [string]$svc.StartType; running = ($svc.Status -eq 'Running'); error = $err } | ConvertTo-Json | Set-Content -LiteralPath '{{result.Replace("'", "''")}}' -Encoding UTF8
            """, token);
        try
        {
            using var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            if (p is null) return new(false, null, SysMainStart.Unknown, false, null);
            await p.WaitForExitAsync(token);
            if (!File.Exists(result)) return new(true, null, SysMainStart.Unknown, false, "no result");
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(result, token));
            var root = doc.RootElement;
            bool? mm = root.TryGetProperty("compression", out var c) && c.ValueKind is JsonValueKind.True or JsonValueKind.False ? c.GetBoolean() : null;
            var start = root.TryGetProperty("start", out var s) ? Parse(s.GetString()) : SysMainStart.Unknown;
            var running = root.TryGetProperty("running", out var r) && r.ValueKind == JsonValueKind.True;
            var error = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            return new(true, mm, start, running, error);
        }
        catch (System.ComponentModel.Win32Exception) { return new(false, null, SysMainStart.Unknown, false, null); } // administrator prompt declined
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private static SysMainStart Parse(string? s) => s switch
    {
        "Automatic" or "AutomaticDelayedStart" => SysMainStart.Automatic, "Manual" => SysMainStart.Manual, "Disabled" => SysMainStart.Disabled, _ => SysMainStart.Unknown,
    };

    private State? Load()
    {
        try { return File.Exists(StatePath) ? JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath)) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
    private void Save(State state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(state));
    }

    private static bool CompressionRunning()
    {
        var list = Process.GetProcessesByName("Memory Compression");
        foreach (var p in list) p.Dispose();
        return list.Length > 0;
    }

    private static SysMainStart StartType()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SysMain");
            return k?.GetValue("Start") switch { 2 => SysMainStart.Automatic, 3 => SysMainStart.Manual, 4 => SysMainStart.Disabled, _ => SysMainStart.Unknown };
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return SysMainStart.Unknown; }
    }

    private static bool ServiceRunning()
    {
        var scm = OpenSCManagerW(null, null, 0x0001); // SC_MANAGER_CONNECT
        if (scm == IntPtr.Zero) return false;
        try
        {
            var svc = OpenServiceW(scm, "SysMain", 0x0004); // SERVICE_QUERY_STATUS
            if (svc == IntPtr.Zero) return false;
            try { return QueryServiceStatus(svc, out var status) && status.CurrentState == 4; } // SERVICE_RUNNING
            finally { CloseServiceHandle(svc); }
        }
        finally { CloseServiceHandle(scm); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus { public int ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint; }
    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)] private static partial IntPtr OpenSCManagerW(string? machine, string? database, int access);
    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)] private static partial IntPtr OpenServiceW(IntPtr scm, string name, int access);
    [LibraryImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool QueryServiceStatus(IntPtr service, out ServiceStatus status);
    [LibraryImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool CloseServiceHandle(IntPtr handle);
}
