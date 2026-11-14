using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lotus.Core.Benchmark;

namespace Lotus.Infrastructure.Benchmark;

// Read-only: asks the display driver how many planes the primary display can show at once (D3DKMTGetMultiPlaneOverlayCaps)
// and reads DWM's OverlayTestMode switch. Nothing is changed. Idea from CapFrameX's MpoSupport (MIT, © DevTechProfile,
// github.com/CXWorld/CapFrameX); written for Lotus from the Windows documentation of the D3DKMT calls.
[SupportedOSPlatform("windows")]
public static class MpoProbe
{
    public static MpoStatus Query()
    {
        int? testMode = null;
        try { using var dwm = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\Dwm"); if (dwm?.GetValue("OverlayTestMode") is int v) testMode = v; }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        var (planes, rgb) = Caps(PrimaryDisplay());
        return PresentModes.Mpo(testMode, planes, rgb);
    }

    private static (int?, int?) Caps(string? device)
    {
        if (device is null) return (null, null);
        try
        {
            var open = new OpenAdapterFromGdiDisplayName { DeviceName = device };
            if (D3DKMTOpenAdapterFromGdiDisplayName(ref open) != 0) return (null, null);
            try
            {
                var caps = new MultiPlaneOverlayCaps { Adapter = open.Adapter, VidPnSourceId = open.VidPnSourceId };
                return D3DKMTGetMultiPlaneOverlayCaps(ref caps) == 0 ? ((int)caps.MaxPlanes, (int)caps.MaxRgbPlanes) : (null, null);
            }
            finally { var close = new CloseAdapter { Adapter = open.Adapter }; D3DKMTCloseAdapter(ref close); }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException) { return (null, null); }
    }

    private static string? PrimaryDisplay()
    {
        var d = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
        for (uint i = 0; EnumDisplayDevices(null, i, ref d, 0); i++, d.cb = Marshal.SizeOf<DisplayDevice>())
            if ((d.StateFlags & 0x4) != 0) return d.DeviceName; // DISPLAY_DEVICE_PRIMARY_DEVICE
        return null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenAdapterFromGdiDisplayName { [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName; public uint Adapter; public uint AdapterLuidLow; public int AdapterLuidHigh; public uint VidPnSourceId; } // LUID is two 4-byte fields (4-byte aligned)
    [StructLayout(LayoutKind.Sequential)]
    private struct MultiPlaneOverlayCaps
    {
        public uint Adapter, VidPnSourceId, MaxPlanes, MaxRgbPlanes, MaxYuvPlanes, OverlayCaps; public float MaxStretchFactor, MaxShrinkFactor;
        private ulong reserved0, reserved1, reserved2, reserved3; // room for drivers that write past the documented layout
    }
    [StructLayout(LayoutKind.Sequential)] private struct CloseAdapter { public uint Adapter; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int cb; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
    [DllImport("gdi32.dll")] private static extern int D3DKMTOpenAdapterFromGdiDisplayName(ref OpenAdapterFromGdiDisplayName data);
    [DllImport("gdi32.dll")] private static extern int D3DKMTGetMultiPlaneOverlayCaps(ref MultiPlaneOverlayCaps data);
    [DllImport("gdi32.dll")] private static extern int D3DKMTCloseAdapter(ref CloseAdapter data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice data, uint flags);
}
