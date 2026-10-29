using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lotus.Core.Games;

namespace Lotus.Infrastructure.Games;

// Detects variable refresh rate without asking the player. NVIDIA: the driver's own answer through NVAPI
// (NvAPI_Disp_GetVRRInfo on the primary display; IDs from NVIDIA's public nvapi_interface.h). Other GPUs: the monitor's
// EDID read through WMI (AMD FreeSync block, or a wide continuous range). Read-only; nothing is changed.
[SupportedOSPlatform("windows")]
public sealed class WindowsDisplaySync(Func<int>? refreshHz = null) : IDisplaySyncProbe
{
    public DisplaySync Read()
    {
        var (name, min, max, freeSync, continuous) = Edid();
        var hz = refreshHz?.Invoke() ?? 0;
        if (Nvidia() is { } nv) return new(name, hz, nv.Possible, nv.Enabled, "nvapi");
        if (name is null && max == 0) return DisplaySync.Unknown with { RefreshHz = hz };
        var possible = freeSync || (continuous && min > 0 && max - min >= 20);
        return new(name, hz, possible, null, "edid");
    }

    // ---- NVIDIA ----
    private const uint NvInitialize = 0x0150e828, NvPrimaryDisplay = 0x1e9d8a31, NvGetVrrInfo = 0xdf8fda57;
    [StructLayout(LayoutKind.Sequential)] private struct VrrInfo { public uint Version, Flags, R0, R1, R2, R3; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr QueryInterface(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitializeFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int PrimaryFn(out uint displayId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int VrrFn(uint displayId, ref VrrInfo info);

    private static (bool Possible, bool Enabled)? Nvidia()
    {
        if (!NativeLibrary.TryLoad("nvapi64.dll", out var lib)) return null;
        try
        {
            if (!NativeLibrary.TryGetExport(lib, "nvapi_QueryInterface", out var qiPtr)) return null;
            var qi = Marshal.GetDelegateForFunctionPointer<QueryInterface>(qiPtr);
            T? Fn<T>(uint id) where T : Delegate => qi(id) is var p && p != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<T>(p) : null;
            if (Fn<InitializeFn>(NvInitialize)?.Invoke() != 0) return null;
            if (Fn<PrimaryFn>(NvPrimaryDisplay) is not { } primary || primary(out var display) != 0) return null;
            if (Fn<VrrFn>(NvGetVrrInfo) is not { } getVrr) return null;
            var info = new VrrInfo { Version = (uint)Marshal.SizeOf<VrrInfo>() | (1u << 16) };
            if (getVrr(display, ref info) != 0) return null;
            return ((info.Flags & 2) != 0, (info.Flags & 1) != 0);
        }
        catch (Exception ex) when (ex is MarshalDirectiveException or EntryPointNotFoundException or AccessViolationException) { return null; }
        finally { NativeLibrary.Free(lib); }
    }

    // ---- EDID of the active monitor (first one Windows reports) ----
    private static (string? Name, int Min, int Max, bool FreeSync, bool Continuous) Edid()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorDescriptorMethods WHERE Active = TRUE");
            foreach (ManagementObject monitor in searcher.Get())
            {
                using (monitor)
                {
                    var bytes = new List<byte>();
                    for (byte block = 0; block < 4; block++)
                    {
                        var input = monitor.GetMethodParameters("WmiGetMonitorRawEEdidV1Block"); input["BlockId"] = block;
                        try { if (monitor.InvokeMethod("WmiGetMonitorRawEEdidV1Block", input, null)?["BlockContent"] is byte[] b) bytes.AddRange(b); else break; }
                        catch (ManagementException) { break; }
                    }
                    if (bytes.Count >= 128) return DisplaySync.ParseEdid([.. bytes]);
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException) { }
        return (null, 0, 0, false, false);
    }
}
