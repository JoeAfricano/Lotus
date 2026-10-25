using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lotus.Core.Optimization;
using Microsoft.Win32;

namespace Lotus.Infrastructure.Optimization;

// HKCU only: never touches machine-wide keys.
[SupportedOSPlatform("windows")]
public sealed class WindowsUserRegistry : IUserRegistry
{
    public int? ReadDword(string path, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path, writable: false);
        return key?.GetValue(name) is int v ? v : null;
    }
    public void WriteDword(string path, string name, int? value)
    {
        if (value is null)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(path, writable: true);
            existing?.DeleteValue(name, throwOnMissingValue: false);
            return;
        }
        using var key = Registry.CurrentUser.CreateSubKey(path, writable: true);
        key.SetValue(name, value.Value, RegistryValueKind.DWord);
    }
}

// Primary display mode through user32. Each change is tested with CDS_TEST before being committed.
[SupportedOSPlatform("windows")]
public sealed class WindowsDisplayModes : IDisplayModes
{
    private const int EnumCurrent = -1, PrimaryDevice = 0x4, DmDisplayFrequency = 0x400000, CdsUpdateRegistry = 0x1, CdsTest = 0x2;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string? device, int index, ref DisplayDevice info, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettings(string device, int mode, ref DevMode info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsEx(string device, ref DevMode mode, IntPtr hwnd, int flags, IntPtr param);

    public DisplayModeState? Primary()
    {
        for (var i = 0; i < 16; i++)
        {
            var adapter = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, i, ref adapter, 0)) break;
            if ((adapter.StateFlags & PrimaryDevice) == 0) continue;
            var current = NewMode();
            if (!EnumDisplaySettings(adapter.DeviceName, EnumCurrent, ref current) || current.dmPelsWidth == 0) return null;
            var max = current.dmDisplayFrequency;
            for (var m = 0; m < 4096; m++)
            {
                var mode = NewMode();
                if (!EnumDisplaySettings(adapter.DeviceName, m, ref mode)) break;
                if (mode.dmPelsWidth == current.dmPelsWidth && mode.dmPelsHeight == current.dmPelsHeight && mode.dmBitsPerPel == current.dmBitsPerPel && mode.dmDisplayFrequency > max)
                    max = mode.dmDisplayFrequency;
            }
            return new(adapter.DeviceName, current.dmPelsWidth, current.dmPelsHeight, current.dmDisplayFrequency, max);
        }
        return null;
    }

    public bool SetRefresh(string device, int hz)
    {
        var mode = NewMode();
        if (!EnumDisplaySettings(device, EnumCurrent, ref mode)) return false;
        mode.dmDisplayFrequency = hz; mode.dmFields = DmDisplayFrequency;
        if (ChangeDisplaySettingsEx(device, ref mode, IntPtr.Zero, CdsTest, IntPtr.Zero) != 0) return false;
        return ChangeDisplaySettingsEx(device, ref mode, IntPtr.Zero, CdsUpdateRegistry, IntPtr.Zero) == 0;
    }
    private static DevMode NewMode() => new() { dmSize = (short)Marshal.SizeOf<DevMode>() };
}
