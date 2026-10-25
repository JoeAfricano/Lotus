using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Lotus.Core.Optimization;

namespace Lotus.Infrastructure.Optimization;

[SupportedOSPlatform("windows")]
public sealed class WindowsPowerSchemes : IPowerSchemeService
{
    public IReadOnlyList<PowerScheme> List()
    {
        var items = new List<PowerScheme>();
        for (uint index = 0; index < 256; index++)
        {
            var buffer = new byte[16]; uint size = 16;
            var result = PowerEnumerate(0, 0, 0, 16, index, buffer, ref size);
            if (result == 259) return items; // ERROR_NO_MORE_ITEMS
            Check(result); if (size != 16) throw new InvalidDataException("Invalid power GUID size.");
            var id = new Guid(buffer); items.Add(new(id, Name(id)));
        }
        throw new InvalidDataException("Power scheme enumeration limit exceeded.");
    }
    public Guid Active()
    {
        Check(PowerGetActiveScheme(0, out var pointer));
        if (pointer == 0) throw new InvalidDataException("Missing active power scheme.");
        try { return Marshal.PtrToStructure<Guid>(pointer); }
        finally { LocalFree(pointer); }
    }
    public void SetActive(Guid id) => Check(PowerSetActiveScheme(0, ref id));
    public bool? OnAcPower() => GetSystemPowerStatus(out var s) ? s.AcLineStatus switch { 0 => false, 1 => true, _ => null } : null;
    private static string Name(Guid id)
    {
        uint size = 0; var result = PowerReadFriendlyName(0, ref id, 0, 0, null, ref size);
        if (result != 0 && result != 234) return id.ToString("D");
        if (size is 0 or > 65536) return id.ToString("D");
        var buffer = new byte[size];
        if (PowerReadFriendlyName(0, ref id, 0, 0, buffer, ref size) != 0 || size > buffer.Length) return id.ToString("D");
        return Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0');
    }
    private static void Check(uint result) { if (result != 0) throw new Win32Exception((int)result); }
    [StructLayout(LayoutKind.Sequential)] private struct PowerStatus
    { public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public uint BatteryLifeTime, BatteryFullLifeTime; }
    [DllImport("powrprof.dll", ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PowerEnumerate(nint root, nint scheme, nint subgroup, uint flags, uint index, [Out] byte[] buffer, ref uint size);
    [DllImport("powrprof.dll", ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PowerGetActiveScheme(nint root, out nint active);
    [DllImport("powrprof.dll", ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PowerSetActiveScheme(nint root, ref Guid scheme);
    [DllImport("powrprof.dll", ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PowerReadFriendlyName(nint root, ref Guid scheme, nint subgroup, nint setting, [Out] byte[]? buffer, ref uint size);
    [DllImport("kernel32.dll", ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint LocalFree(nint memory);
    [DllImport("kernel32.dll", ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemPowerStatus(out PowerStatus status);
}
