using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lotus.Core.Diagnostics;

namespace Lotus.Infrastructure.Diagnostics;

// Read-only native queries: display modes (user32), SMBIOS firmware table (kernel32) and processor limits (powrprof).
[SupportedOSPlatform("windows")]
public sealed class WindowsHardwareFacts : IHardwareFactsSource
{
    public HardwareFacts Read()
    {
        var errors = new List<string>();
        var adapters = new List<string>();
        var displays = Try(() => Displays(adapters), "display", errors) ?? [];
        var memory = Try(Memory, "smbios", errors) ?? [];
        var cpu = Try(() => Cpu() ?? throw new InvalidOperationException("CallNtPowerInformation failed"), "cpu", errors);
        return new(displays, memory, cpu, adapters, errors);
    }

    private static T? Try<T>(Func<T> read, string name, List<string> errors) where T : class
    {
        try { return read(); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException or OutOfMemoryException) { errors.Add(name + ": " + ex.GetType().Name); return null; }
    }

    // --- Displays ---
    private const int EnumCurrentSettings = -1;
    private const int DisplayAttached = 0x1;
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

    private static List<DisplayFact> Displays(List<string> adapters)
    {
        var result = new List<DisplayFact>();
        for (var i = 0; i < 16; i++)
        {
            var adapter = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, i, ref adapter, 0)) break;
            adapters.Add(adapter.DeviceString);
            if ((adapter.StateFlags & DisplayAttached) == 0) continue;
            var current = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
            if (!EnumDisplaySettings(adapter.DeviceName, EnumCurrentSettings, ref current) || current.dmPelsWidth == 0) continue;
            var max = current.dmDisplayFrequency;
            for (var m = 0; m < 4096; m++)
            {
                var mode = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
                if (!EnumDisplaySettings(adapter.DeviceName, m, ref mode)) break;
                if (mode.dmPelsWidth == current.dmPelsWidth && mode.dmPelsHeight == current.dmPelsHeight && mode.dmDisplayFrequency > max) max = mode.dmDisplayFrequency;
            }
            var monitor = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
            var name = EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, 0) && !string.IsNullOrWhiteSpace(monitor.DeviceString) ? monitor.DeviceString : adapter.DeviceName;
            result.Add(new(name.Trim(), current.dmPelsWidth, current.dmPelsHeight, current.dmDisplayFrequency, max));
        }
        return result;
    }

    // --- Memory (SMBIOS) ---
    private const uint Rsmb = 0x52534D42; // 'RSMB'
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetSystemFirmwareTable(uint provider, uint id, byte[]? buffer, uint size);
    private static IReadOnlyList<MemoryModuleFact> Memory()
    {
        var size = GetSystemFirmwareTable(Rsmb, 0, null, 0);
        if (size == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var buffer = new byte[size];
        if (GetSystemFirmwareTable(Rsmb, 0, buffer, size) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        return SmbiosMemoryParser.Parse(buffer);
    }

    // --- CPU frequency limit ---
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessorPowerInformation { public uint Number, MaxMhz, CurrentMhz, MhzLimit, MaxIdleState, CurrentIdleState; }
    [DllImport("powrprof.dll")] private static extern uint CallNtPowerInformation(int level, IntPtr input, uint inputLength, [Out] ProcessorPowerInformation[] output, uint outputLength);
    private static CpuLimitFact? Cpu()
    {
        var count = Environment.ProcessorCount;
        var info = new ProcessorPowerInformation[count];
        var status = CallNtPowerInformation(11, IntPtr.Zero, 0, info, (uint)(count * Marshal.SizeOf<ProcessorPowerInformation>()));
        if (status != 0 || info.Length == 0) return null;
        return new((int)info.Max(i => i.MaxMhz), (int)info.Min(i => i.MhzLimit));
    }
}
