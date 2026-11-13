using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using Lotus.Core.Games;
using Microsoft.Win32;

namespace Lotus.Infrastructure.Games;

// The Windows side of the Perfil Jogo session tweaks (see Lotus.Core.Games.GameBoost). Only per-user values (HKCU),
// no administrator, nothing that needs a restart.

// A per-user DWORD set for the game and put back after. "absent" = the value did not exist (it is deleted again).
[SupportedOSPlatform("windows")]
public abstract class RegistryBoostStep(string id, (string Key, string Name)[] values, int during) : IBoostStep
{
    public string Id => id;
    public string? Apply(RunningGame game)
    {
        var saved = new Dictionary<string, string>();
        foreach (var (key, name) in values)
        {
            using var k = Registry.CurrentUser.CreateSubKey(key, writable: true);
            var now = k.GetValue(name);
            if (now is int v && v == during) continue;
            saved[key + "|" + name] = now is int old ? old.ToString(System.Globalization.CultureInfo.InvariantCulture) : "absent";
            k.SetValue(name, during, RegistryValueKind.DWord);
        }
        Changed();
        return saved.Count == 0 ? null : JsonSerializer.Serialize(saved);
    }
    public bool Revert(string saved)
    {
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(saved) ?? [];
        var any = false;
        foreach (var (path, old) in map)
        {
            var bar = path.LastIndexOf('|'); if (bar < 0) continue;
            using var k = Registry.CurrentUser.OpenSubKey(path[..bar], writable: true);
            if (k is null || k.GetValue(path[(bar + 1)..]) is not int now || now != during) continue; // changed by the player meanwhile
            if (old == "absent") k.DeleteValue(path[(bar + 1)..], throwOnMissingValue: false);
            else k.SetValue(path[(bar + 1)..], int.Parse(old, System.Globalization.CultureInfo.InvariantCulture), RegistryValueKind.DWord);
            any = true;
        }
        Changed();
        return any;
    }
    protected virtual void Changed() { }
}

// Windows notifications (banners) off while playing: the same switch as Settings > System > Notifications.
// Toasts and their sound break the game's direct flip and showed up as stutter spikes on the player's PC.
[SupportedOSPlatform("windows")]
public sealed class NotificationsBoostStep() : RegistryBoostStep(BoostSteps.Notifications,
    [(@"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled")], 0)
{
    protected override void Changed() => SettingChanged("Notifications");
    internal static void SettingChanged(string area)
    {
        try { SendMessageTimeout(0xFFFF, 0x001A, 0, area, 0x0002, 1000, out _); } catch (Exception) { } // WM_SETTINGCHANGE, best effort
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern nint SendMessageTimeout(nint hWnd, uint msg, nint wParam, string lParam, uint flags, uint timeout, out nint result);
}

// Xbox Game Bar background recording off while playing (it keeps encoding the last minutes in the background).
[SupportedOSPlatform("windows")]
public sealed class RecordingBoostStep() : RegistryBoostStep(BoostSteps.BackgroundRecording,
    [(@"System\GameConfigStore", "GameDVR_Enabled"), (@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled")], 0);

// The undo record on disk (%LOCALAPPDATA%\Lotus\game-boost.json), written before each change.
public sealed class FileBoostJournal(string path) : IBoostJournal
{
    public IReadOnlyList<BoostRecord> Load() => File.Exists(path) ? JsonSerializer.Deserialize<List<BoostRecord>>(File.ReadAllText(path)) ?? [] : [];
    public void Save(IReadOnlyList<BoostRecord> records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(records));
        File.Move(temp, path, overwrite: true);
    }
    public void Clear() { if (File.Exists(path)) File.Delete(path); }
}
