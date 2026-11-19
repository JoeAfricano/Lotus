using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Lotus.Core.Display;

namespace Lotus.Infrastructure.Display;

[SupportedOSPlatform("windows")]
public sealed class WindowsDisplayCalibration : IDisplayCalibration
{
    public IReadOnlyList<CalibrationState> Displays()
    {
        var outputs = WindowsMonitorColors.Outputs();
        return outputs.Select(o => ReadOutput(o, outputs.Count(p => p.Device == o.Device) == 1)).ToArray();
    }
    public CalibrationState? Read(string identity)
    {
        // Enumerate topology for identity/conflict safety, but do not perform DDC reads on unrelated monitors.
        var outputs = WindowsMonitorColors.Outputs();
        var output = outputs.SingleOrDefault(o => o.Identity == identity);
        return output is null ? null : ReadOutput(output, outputs.Count(o => o.Device == output.Device) == 1);
    }
    private static CalibrationState ReadOutput(MonitorColorState output, bool unique)
    {
        MonitorLevel? brightness = null, contrast = null; ushort[]? ramp = null;
        if (unique && output.AdvancedColor == false)
        {
            using var monitor = Physical.Open(output.Device);
            if (monitor is not null)
            {
                if (GetMonitorCapabilities(monitor.Handle, out var caps, out _))
                {
                    if ((caps & 2) != 0 && GetMonitorBrightness(monitor.Handle, out var min, out var value, out var max)) brightness = Level(min, value, max);
                    if ((caps & 4) != 0 && GetMonitorContrast(monitor.Handle, out min, out value, out max)) contrast = Level(min, value, max);
                }
            }
            var dc = CreateDC("DISPLAY", output.Device, null, IntPtr.Zero);
            if (dc != IntPtr.Zero)
            {
                try { var data = new ushort[768]; if (GetDeviceGammaRamp(dc, data) && CalibrationState.ValidRamp(data)) ramp = data; }
                finally { DeleteDC(dc); }
            }
        }
        ColorRange? vibrance = null;
        if (unique && output.AdvancedColor == false) { using var nv = WindowsMonitorColors.Nvidia.Open(); vibrance = nv?.Read(output.Device); }
        return new(output.Identity, output.Device, output.Name, output.AdvancedColor, brightness, contrast, ramp, vibrance);
    }
    private static MonitorLevel? Level(uint min, uint current, uint max)
    { var value = new MonitorLevel((int)current, (int)min, (int)max); return value.Valid ? value : null; }
    public bool TryApply(CalibrationState expected, CalibrationState target)
    {
        // A single DDC/CI read can glitch; allow a short settle before refusing as a conflict.
        var current = DisplaySettle.Until(() => Read(expected.Identity), s => s.Same(expected), Sleep, DisplaySettle.ExpectBudget, DisplaySettle.ReadStep);
        if (current is not { CanAdjust: true } || !current.Same(expected)) return Fail("expected-state", expected, current);
        if (!target.CanAdjust ||
            target.Identity != current.Identity || target.Device != current.Device || target.AdvancedColor != false ||
            (current.Brightness is null) != (target.Brightness is null) || (current.Contrast is null) != (target.Contrast is null) ||
            (current.Ramp is null) != (target.Ramp is null) ||
            (current.Vibrance is null) != (target.Vibrance is null) ||
            current.Vibrance is { } vc && target.Vibrance is { } vt && (vc.Minimum != vt.Minimum || vc.Maximum != vt.Maximum || vc.Default != vt.Default) ||
            current.Brightness is { } bounds && target.Brightness is { } brTarget && (bounds.Minimum != brTarget.Minimum || bounds.Maximum != brTarget.Maximum) ||
            current.Contrast is { } coBounds && target.Contrast is { } coTarget && (coBounds.Minimum != coTarget.Minimum || coBounds.Maximum != coTarget.Maximum))
            return Fail("target-shape", target, current);
        var wroteDdc = false;
        if (current.Brightness != target.Brightness || current.Contrast != target.Contrast)
        {
            using var monitor = Physical.Open(current.Device); if (monitor is null) return Fail("ddc-open", target, current);
            if (current.Brightness != target.Brightness && target.Brightness is { } br &&
                (!br.Valid || !Write(() => SetMonitorBrightness(monitor.Handle, (uint)br.Current)))) return Fail("ddc-brightness", target, current);
            if (current.Brightness != target.Brightness) wroteDdc = true;
            if (current.Contrast != target.Contrast && target.Contrast is { } co)
            {
                // DDC/CI requires a pause between commands; back-to-back writes are dropped by many monitors.
                if (wroteDdc) Sleep(DisplaySettle.WriteGap);
                if (!co.Valid || !Write(() => SetMonitorContrast(monitor.Handle, (uint)co.Current))) return Fail("ddc-contrast", target, current);
                wroteDdc = true;
            }
        }
        if (!CalibrationState.RampEqual(current.Ramp, target.Ramp))
        {
            var dc = CreateDC("DISPLAY", current.Device, null, IntPtr.Zero); if (dc == IntPtr.Zero) return Fail("gamma-dc", target, current);
            try { if (!SetDeviceGammaRamp(dc, target.Ramp!)) return Fail("gamma-set", target, current); }
            finally { DeleteDC(dc); }
        }
        if (current.Vibrance != target.Vibrance && target.Vibrance is { } saturation)
        {
            using var nv = WindowsMonitorColors.Nvidia.Open();
            if (nv is null || !nv.Set(current.Device, saturation.Current)) return Fail("nvidia-vibrance", target, current);
        }
        // The Windows gamma API can return success while ignoring the ramp, and DDC/CI monitors report
        // new values with a delay. Never trust BOOL alone, but give the monitor time to settle.
        if (wroteDdc) Sleep(DisplaySettle.WriteGap);
        var verified = DisplaySettle.Until(() => Read(expected.Identity), s => s.Same(target, 256), Sleep, DisplaySettle.VerifyBudget, DisplaySettle.ReadStep);
        return verified is not null && verified.Same(target, 256) || Fail("verify", target, verified);
    }
    private static void Sleep(TimeSpan delay) => Thread.Sleep(delay);
    private static bool Write(Func<bool> write)
    {
        if (write()) return true;
        Sleep(TimeSpan.FromMilliseconds(100)); return write(); // one retry for a transient DDC/CI NAK
    }
    private static bool Fail(string stage, CalibrationState wanted, CalibrationState? seen)
    {
        Serilog.Log.Warning("Display calibration step {Stage} failed: wanted brightness {WB} contrast {WC} vibrance {WV} ramp {WR}; read brightness {SB} contrast {SC} vibrance {SV} ramp {SR} advanced {SA}",
            stage, wanted.Brightness?.Current, wanted.Contrast?.Current, wanted.Vibrance?.Current, wanted.Ramp is not null,
            seen?.Brightness?.Current, seen?.Contrast?.Current, seen?.Vibrance?.Current,
            seen is null ? "none" : CalibrationState.RampEqual(seen.Ramp, wanted.Ramp, 256) ? "match" : seen.Ramp is null ? "missing" : "differs", seen?.AdvancedColor);
        return false;
    }
    private sealed class Physical(IntPtr handle) : IDisposable
    {
        public IntPtr Handle { get; } = handle;
        public static Physical? Open(string device)
        {
            IntPtr logical = IntPtr.Zero;
            MonitorCallback callback = (IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data) =>
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(monitor, ref info) && string.Equals(info.Device, device, StringComparison.OrdinalIgnoreCase)) logical = monitor;
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            if (logical == IntPtr.Zero || !GetNumberOfPhysicalMonitorsFromHMONITOR(logical, out var count) || count != 1) return null;
            var monitors = new PhysicalInfo[1];
            return GetPhysicalMonitorsFromHMONITOR(logical, 1, monitors) ? new(monitors[0].Handle) : null;
        }
        public void Dispose() => DestroyPhysicalMonitor(Handle);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
    { public int Size; public Rect Monitor, Work; public int Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct PhysicalInfo
    { public IntPtr Handle; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Name; }
    private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("dxva2.dll")] private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
    [DllImport("dxva2.dll")] private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PhysicalInfo[] data);
    [DllImport("dxva2.dll")] private static extern bool DestroyPhysicalMonitor(IntPtr monitor);
    [DllImport("dxva2.dll")] private static extern bool GetMonitorCapabilities(IntPtr monitor, out uint caps, out uint temperatures);
    [DllImport("dxva2.dll")] private static extern bool GetMonitorBrightness(IntPtr monitor, out uint min, out uint current, out uint max);
    [DllImport("dxva2.dll")] private static extern bool GetMonitorContrast(IntPtr monitor, out uint min, out uint current, out uint max);
    [DllImport("dxva2.dll")] private static extern bool SetMonitorBrightness(IntPtr monitor, uint value);
    [DllImport("dxva2.dll")] private static extern bool SetMonitorContrast(IntPtr monitor, uint value);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateDC(string driver, string device, string? output, IntPtr data);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool GetDeviceGammaRamp(IntPtr dc, [Out] ushort[] ramp);
    [DllImport("gdi32.dll")] private static extern bool SetDeviceGammaRamp(IntPtr dc, [In] ushort[] ramp);
}

public sealed class JsonCalibrationStore(string path) : ICalibrationStore
{
    public IReadOnlyList<CalibrationBackup> Load()
    {
        if (!File.Exists(path)) return [];
        using var stream = File.OpenRead(path);
        if (stream.Length > 524288) throw new IOException("Calibration recovery file too large.");
        return JsonSerializer.Deserialize<CalibrationBackup[]>(stream) ?? throw new IOException("Invalid calibration recovery data.");
    }
    public void Save(IReadOnlyList<CalibrationBackup> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, entries); stream.Flush(true); if (stream.Length > 524288) throw new IOException("Calibration recovery file too large."); }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
