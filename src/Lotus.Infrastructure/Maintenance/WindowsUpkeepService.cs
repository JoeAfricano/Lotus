using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using Lotus.Core.Maintenance;
using Microsoft.Win32;

namespace Lotus.Infrastructure.Maintenance;

// Pending restart markers Windows itself writes (read-only), uptime, Fast Startup; restore points through Windows'
// own Checkpoint-Computer in an administrator PowerShell that only writes a small result file.
[SupportedOSPlatform("windows")]
public sealed class WindowsUpkeepService : IWindowsUpkeep
{
    public WindowsStatus Read() => new(
        KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"),
        KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending"),
        Value(@"SYSTEM\CurrentControlSet\Control\Session Manager", "PendingFileRenameOperations") is string[] { Length: > 0 },
        TimeSpan.FromMilliseconds(Environment.TickCount64),
        Value(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled") is 1);

    private static bool KeyExists(string path)
    {
        try { using var k = Registry.LocalMachine.OpenSubKey(path); return k is not null; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return false; }
    }
    private static object? Value(string path, string name)
    {
        try { using var k = Registry.LocalMachine.OpenSubKey(path); return k?.GetValue(name); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return null; }
    }

    public async Task<RestorePointResult> CreateRestorePointAsync(string description, CancellationToken token = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "Lotus-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var result = Path.Combine(dir, "result.json");
        var safe = new string(description.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '/' or ':' or '.' or '-').ToArray());
        var script = Path.Combine(dir, "restore.ps1");
        await File.WriteAllTextAsync(script, $$"""
            $ErrorActionPreference = 'Stop'
            $before = @(Get-ComputerRestorePoint -ErrorAction SilentlyContinue).Count
            $err = $null
            try { Checkpoint-Computer -Description '{{safe}}' -RestorePointType 'MODIFY_SETTINGS' -WarningVariable w -WarningAction SilentlyContinue; if ($w) { $err = ($w | Select-Object -First 1).ToString() } }
            catch { $err = $_.Exception.Message }
            $after = @(Get-ComputerRestorePoint -ErrorAction SilentlyContinue).Count
            @{ before = $before; after = $after; error = $err } | ConvertTo-Json | Set-Content -LiteralPath '{{result.Replace("'", "''")}}' -Encoding UTF8
            """, token);
        try
        {
            using var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            if (p is null) return new(false, false, 0, 0, null);
            await p.WaitForExitAsync(token);
            if (!File.Exists(result)) return new(true, false, 0, 0, "no result");
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(result, token));
            int before = doc.RootElement.GetProperty("before").GetInt32(), after = doc.RootElement.GetProperty("after").GetInt32();
            var error = doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            return new(true, after > before, before, after, error);
        }
        catch (System.ComponentModel.Win32Exception) { return new(false, false, 0, 0, null); } // administrator prompt declined
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    public void OpenWindowsUpdate() => Process.Start(new ProcessStartInfo("ms-settings:windowsupdate") { UseShellExecute = true })?.Dispose();
    public void OpenSystemRestore() => Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "rstrui.exe")) { UseShellExecute = true })?.Dispose();
}
