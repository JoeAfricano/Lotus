using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Lotus.Core.Display;

namespace Lotus.Infrastructure.Display;

// ABI of the DVC Ex calls informed by jNizM/NVIDIA_NvAPI (MIT), pinned in the research document.
// No third-party DLLs, registry tweaks, gamma ramps, ICC changes or per-frame processing.
[SupportedOSPlatform("windows")]
public sealed class WindowsMonitorColors : IMonitorColors
{
    public IReadOnlyList<MonitorColorState> Displays()
    {
        var outputs = Outputs();
        using var nv = Nvidia.Open();
        return outputs.Select(o => o with
        {
            // NVIDIA display handles can encompass mirrored outputs: don't change an ambiguous group.
            Vibrance = outputs.Count(p => p.Device == o.Device) == 1 ? nv?.Read(o.Device) : null
        }).ToArray();
    }
    public MonitorColorState? Read(string identity) => Displays().SingleOrDefault(d => d.Identity == identity);
    public bool TrySet(MonitorColorState expected, int value)
    {
        if (Read(expected.Identity) is not { CanAdjust: true } before || !before.SameSetting(expected) ||
            before.Vibrance is not { } range || value < range.Minimum || value > range.Maximum) return false;
        using var nv = Nvidia.Open();
        if (nv is null || !nv.Set(before.Device, value)) return false;
        return Read(expected.Identity) is { CanAdjust: true, Vibrance: { } after } state &&
            state.Device == before.Device && after == range with { Current = value };
    }

    internal static IReadOnlyList<MonitorColorState> Outputs()
    {
        if (GetDisplayConfigBufferSizes(2, out var count, out var modes) != 0 || count > 64 || modes > 256) return [];
        var paths = new byte[count * 72]; var modeData = new byte[modes * 64];
        if (QueryDisplayConfig(2, ref count, paths, ref modes, modeData, IntPtr.Zero) != 0) return [];
        var result = new List<MonitorColorState>();
        for (var i = 0; i < count; i++)
        {
            var source = Packet(1, 84, paths, i * 72);
            var target = Packet(2, 420, paths, i * 72 + 20);
            var color = Packet(9, 32, paths, i * 72 + 20);
            if (DisplayConfigGetDeviceInfo(source) != 0 || DisplayConfigGetDeviceInfo(target) != 0) continue;
            var device = Encoding.Unicode.GetString(source, 20, 64).TrimEnd('\0');
            var name = Encoding.Unicode.GetString(target, 36, 128).TrimEnd('\0');
            var identity = Encoding.Unicode.GetString(target, 164, 256).TrimEnd('\0');
            if (device.Length == 0 || identity.Length == 0) continue;
            bool? advanced = DisplayConfigGetDeviceInfo(color) == 0 ? (BitConverter.ToUInt32(color, 20) & 2) != 0 : null;
            result.Add(new(identity, device, string.IsNullOrWhiteSpace(name) ? device : name, advanced, null));
        }
        return result.DistinctBy(s => s.Identity).ToArray();
    }
    private static byte[] Packet(int type, int size, byte[] paths, int offset)
    {
        var packet = new byte[size]; BitConverter.GetBytes(type).CopyTo(packet, 0); BitConverter.GetBytes(size).CopyTo(packet, 4);
        Array.Copy(paths, offset, packet, 8, 12); return packet;
    }
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, byte[] pathInfo, ref uint modes, byte[] modeInfo, IntPtr topology);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(byte[] packet);

    internal sealed class Nvidia : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] private struct Dvc { public uint Version; public int Current, Minimum, Maximum, Default; }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Query(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Initialize();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Handle([MarshalAs(UnmanagedType.LPStr)] string name, out IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DvcCall(IntPtr handle, uint outputId, ref Dvc info);
        private readonly IntPtr library;
        private readonly Handle handle;
        private readonly DvcCall get, set;
        private Nvidia(IntPtr library, Handle handle, DvcCall get, DvcCall set)
        { this.library = library; this.handle = handle; this.get = get; this.set = set; }
        public static Nvidia? Open()
        {
            if (!NativeLibrary.TryLoad(Path.Combine(Environment.SystemDirectory, "nvapi64.dll"), out var lib)) return null;
            var keep = false;
            try
            {
                if (!NativeLibrary.TryGetExport(lib, "nvapi_QueryInterface", out var export)) return null;
                var query = Marshal.GetDelegateForFunctionPointer<Query>(export);
                T? Fn<T>(uint id) where T : Delegate => query(id) is var ptr && ptr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<T>(ptr) : null;
                if (Fn<Initialize>(0x0150e828)?.Invoke() != 0 || Fn<Handle>(0x35c29134) is not { } handle ||
                    Fn<DvcCall>(0x0e45002d) is not { } get || Fn<DvcCall>(0x4a82c2b1) is not { } set) return null;
                keep = true; return new(lib, handle, get, set);
            }
            finally { if (!keep) NativeLibrary.Free(lib); }
        }
        public ColorRange? Read(string device)
        {
            if (handle(device, out var display) != 0) return null;
            var data = new Dvc { Version = 20 | 0x10000 };
            if (get(display, 0, ref data) != 0) return null;
            var range = new ColorRange(data.Current, data.Minimum, data.Maximum, data.Default);
            return range.Valid ? range : null;
        }
        public bool Set(string device, int value)
        {
            if (handle(device, out var display) != 0) return false;
            var data = new Dvc { Version = 20 | 0x10000, Current = value };
            return set(display, 0, ref data) == 0;
        }
        public void Dispose() => NativeLibrary.Free(library);
    }
}

public sealed class JsonMonitorColorStore(string path) : IMonitorColorStore
{
    public IReadOnlyList<MonitorColorChange> Load()
    {
        if (!File.Exists(path)) return [];
        using var input = File.OpenRead(path);
        if (input.Length > 65536) throw new IOException("Monitor color recovery file too large.");
        return JsonSerializer.Deserialize<MonitorColorChange[]>(input) ?? throw new IOException("Invalid monitor color recovery file.");
    }
    public void Save(IReadOnlyList<MonitorColorChange> changes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, changes); stream.Flush(true); }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
