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
    public static void Set(bool enabled, string exePath, string name = "Lotus")
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(name, $"\"{exePath}\" {MinimizedArgument}"); else key.DeleteValue(name, throwOnMissingValue: false);
    }
}
