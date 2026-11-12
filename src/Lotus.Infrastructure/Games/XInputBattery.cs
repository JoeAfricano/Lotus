using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lotus.Core.Games;

namespace Lotus.Infrastructure.Games;

// Battery level of XInput (Xbox-style) wireless controllers, read-only, straight from Windows (xinput1_4.dll). Only a
// level (empty/low/medium/full), so it is used when Windows.Gaming.Input gives no capacity. Wired pads are skipped.
[SupportedOSPlatform("windows")]
public static class XInputBattery
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryInformation { public byte Type, Level; }
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetBatteryInformation")]
    private static extern int GetBatteryInformation(int userIndex, byte devType, out BatteryInformation info);
    private const byte Gamepad = 0, Disconnected = 0, Wired = 1;

    public static IReadOnlyList<ControllerBattery> Read(string nameFormat = "Controle {0}")
    {
        var list = new List<ControllerBattery>();
        try
        {
            for (var i = 0; i < 4; i++)
            {
                if (GetBatteryInformation(i, Gamepad, out var info) != 0 || info.Type is Disconnected or Wired) continue;
                var level = info.Level switch { 0 => BatteryLevel.Empty, 1 => BatteryLevel.Low, 2 => BatteryLevel.Medium, 3 => BatteryLevel.Full, _ => BatteryLevel.Unknown };
                list.Add(new(string.Format(nameFormat, i + 1), null, level, false));
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        return list;
    }
}
