using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lotus.Core.Optimization;

namespace Lotus.Infrastructure.Optimization;

// Per-user SystemParametersInfo settings for the optimization catalog. SPIF_UPDATEINIFILE saves them in the user's
// profile (HKCU\Control Panel) and SPIF_SENDCHANGE tells running apps, the same way the Settings app does. No admin.
[SupportedOSPlatform("windows")]
internal static class SystemParameters
{
    public const int GetMouse = 0x0003, SetMouse = 0x0004, GetFilterKeys = 0x0032, SetFilterKeys = 0x0033,
        GetToggleKeys = 0x0034, SetToggleKeys = 0x0035, GetStickyKeys = 0x003A, SetStickyKeys = 0x003B;
    public const int UpdateAndBroadcast = 0x1 | 0x2; // SPIF_UPDATEINIFILE | SPIF_SENDCHANGE

    [StructLayout(LayoutKind.Sequential)] public struct FlagsOnly { public int cbSize; public int dwFlags; } // STICKYKEYS / TOGGLEKEYS
    [StructLayout(LayoutKind.Sequential)] public struct FilterKeys { public int cbSize; public int dwFlags, iWaitMSec, iDelayMSec, iRepeatMSec, iBounceMSec; }

    [DllImport("user32.dll", SetLastError = true)] public static extern bool SystemParametersInfo(int action, int param, ref FlagsOnly value, int winIni);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SystemParametersInfo(int action, int param, ref FilterKeys value, int winIni);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SystemParametersInfo(int action, int param, [In, Out] int[] value, int winIni);

    public static void Check(bool ok) { if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error()); }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsAccessibilityKeys : IAccessibilityKeys
{
    public AccessibilityKeyFlags? Read()
    {
        var sticky = new SystemParameters.FlagsOnly { cbSize = Marshal.SizeOf<SystemParameters.FlagsOnly>() };
        var toggle = sticky;
        var filter = new SystemParameters.FilterKeys { cbSize = Marshal.SizeOf<SystemParameters.FilterKeys>() };
        if (!SystemParameters.SystemParametersInfo(SystemParameters.GetStickyKeys, sticky.cbSize, ref sticky, 0)
            || !SystemParameters.SystemParametersInfo(SystemParameters.GetFilterKeys, filter.cbSize, ref filter, 0)
            || !SystemParameters.SystemParametersInfo(SystemParameters.GetToggleKeys, toggle.cbSize, ref toggle, 0)) return null;
        return new(sticky.dwFlags, filter.dwFlags, toggle.dwFlags);
    }

    // Only the flags change; Filter Keys timings are read again and written back untouched.
    public void Write(AccessibilityKeyFlags flags)
    {
        var size = Marshal.SizeOf<SystemParameters.FlagsOnly>();
        var sticky = new SystemParameters.FlagsOnly { cbSize = size, dwFlags = flags.Sticky };
        var toggle = new SystemParameters.FlagsOnly { cbSize = size, dwFlags = flags.Toggle };
        var filter = new SystemParameters.FilterKeys { cbSize = Marshal.SizeOf<SystemParameters.FilterKeys>() };
        SystemParameters.Check(SystemParameters.SystemParametersInfo(SystemParameters.GetFilterKeys, filter.cbSize, ref filter, 0));
        filter.dwFlags = flags.Filter;
        SystemParameters.Check(SystemParameters.SystemParametersInfo(SystemParameters.SetStickyKeys, size, ref sticky, SystemParameters.UpdateAndBroadcast));
        SystemParameters.Check(SystemParameters.SystemParametersInfo(SystemParameters.SetFilterKeys, filter.cbSize, ref filter, SystemParameters.UpdateAndBroadcast));
        SystemParameters.Check(SystemParameters.SystemParametersInfo(SystemParameters.SetToggleKeys, size, ref toggle, SystemParameters.UpdateAndBroadcast));
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsMouseSettings : IMouseSettings
{
    public int[]? Read()
    {
        var values = new int[3];
        return SystemParameters.SystemParametersInfo(SystemParameters.GetMouse, 0, values, 0) ? values : null;
    }
    public void Write(int[] values)
    {
        if (values.Length != 3) throw new ArgumentException("Three mouse values expected.", nameof(values));
        SystemParameters.Check(SystemParameters.SystemParametersInfo(SystemParameters.SetMouse, 0, values.ToArray(), SystemParameters.UpdateAndBroadcast));
    }
}

public static class WindowsBuild
{
    public static bool AtLeast(int build) => OperatingSystem.IsWindows() && Environment.OSVersion.Version.Build >= build;
}
