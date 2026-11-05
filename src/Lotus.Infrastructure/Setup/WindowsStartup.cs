using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Lotus.Infrastructure.Setup;

// "Iniciar com o Windows": one value in the user's Run key (no admin), opening Lotus minimized to the tray.
[SupportedOSPlatform("windows")]
public static class WindowsStartup
{
    public const string MinimizedArgument = "--minimized";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool IsEnabled(string name = "Lotus") => Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue(name) is string;
    public static string? Registered(string name = "Lotus") => Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue(name) as string;
    public static string CommandFor(string exePath) => $"\"{exePath}\" {MinimizedArgument}";
    // What must change so the Run value matches the preference and points at this Lotus (an old or moved copy is corrected).
    public static bool? NeededChange(bool wanted, string? registered, string exePath) =>
        wanted ? (string.Equals(registered, CommandFor(exePath), StringComparison.OrdinalIgnoreCase) ? null : true) : (registered is null ? null : false);
    public static void Set(bool enabled, string exePath, string name = "Lotus")
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(name, CommandFor(exePath)); else key.DeleteValue(name, throwOnMissingValue: false);
    }
}
