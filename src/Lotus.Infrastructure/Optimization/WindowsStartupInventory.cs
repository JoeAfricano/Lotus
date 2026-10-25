using System.Runtime.Versioning;
using Lotus.Core.Optimization;
using Microsoft.Win32;

namespace Lotus.Infrastructure.Optimization;

// Read-only. Enabled state comes from Explorer\StartupApproved (same source as Task Manager); nothing is changed.
[SupportedOSPlatform("windows")]
public sealed class WindowsStartupInventory : IStartupInventory
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public StartupInventory Read()
    {
        var entries = new List<StartupEntry>(); var errors = new List<string>();
        // HKCU\...\Run is not redirected by WOW64: reading both views would duplicate every user entry.
        foreach (var (hive, view, kind, label) in new[]
        {
            (RegistryHive.CurrentUser, RegistryView.Default, "Run", @"HKCU\…\Run"),
            (RegistryHive.LocalMachine, RegistryView.Registry64, "Run", @"HKLM\…\Run"),
            (RegistryHive.LocalMachine, RegistryView.Registry32, "Run32", @"HKLM\…\Run (32 bits)")
        })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(RunPath, writable: false);
                if (key is null) continue;
                foreach (var name in key.GetValueNames().Take(200))
                    if (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string command)
                        entries.Add(new(name, label, command.Length <= 4096 ? command : command[..4096], Approved(hive, kind, name), kind, name, hive == RegistryHive.CurrentUser));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            { errors.Add(label + ": " + ex.GetType().Name); }
        }
        foreach (var (folder, hive) in new[] { (Environment.SpecialFolder.Startup, RegistryHive.CurrentUser), (Environment.SpecialFolder.CommonStartup, RegistryHive.LocalMachine) })
        {
            try
            {
                var path = Environment.GetFolderPath(folder);
                if (Directory.Exists(path)) foreach (var file in Directory.EnumerateFiles(path).Take(200))
                {
                    var name = Path.GetFileName(file);
                    if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    entries.Add(new(name, folder.ToString(), file, Approved(hive, "StartupFolder", name), "StartupFolder", name, hive == RegistryHive.CurrentUser));
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { errors.Add(folder + ": " + ex.GetType().Name); }
        }
        // Scheduled tasks and services are deliberately not inferred.
        return new(entries, errors);
    }

    private static bool? Approved(RegistryHive hive, string kind, string name)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = root.OpenSubKey(ApprovedPath + kind, writable: false);
            return StartupApproval.IsEnabled(key?.GetValue(name));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return null; }
    }
}
