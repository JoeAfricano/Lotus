using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;

namespace Lotus.Infrastructure.Games;

// V-Sync from the NVIDIA driver for one game (the "Vertical sync" of the NVIDIA Control Panel, per program), through
// NVAPI's driver settings (DRS). Used by the frame generation preset for monitors without G-Sync/FreeSync: games lock
// their own V-Sync while DLSS Frame Generation is on, and the driver's V-Sync is what keeps frames even without tearing
// (Gothic 1 Remake, RTX 5060, 75 Hz, 22/11/2026: smooth, no tearing, 50 ms PC latency; see docs/LOTUS_FRAMEGEN_NO_VRR.md).
// Reading works without administrator; saving needs it, so changes run inside the elevated helper only.
// Function IDs and structure layouts come from NVIDIA's public nvapi.h / nvapi_interface.h (R5xx).
// Each change keeps a backup (<exe>.nvvsync.json) so Restore puts back exactly what was there.
[SupportedOSPlatform("windows")]
public static class NvidiaDriverVsync
{
    public const uint VsyncModeId = 0x00A879CF, ForceOn = 0x47814940, ForceOff = 0x08416747, AppControlled = 0x60925292;
    private const uint QiInitialize = 0x0150e828, QiCreateSession = 0x0694d52e, QiDestroySession = 0xdad9cff8, QiLoad = 0x375dbd6b,
        QiSave = 0xfcbc7e14, QiFindApp = 0xeee566b2, QiGetSetting = 0x73bf8338, QiSetSetting = 0x577dd202, QiDeleteSetting = 0xe4a26362,
        QiCreateProfile = 0xcc176068, QiCreateApp = 0x4347a9de, QiDeleteProfile = 0x17093206;
    // NVDRS_SETTING_V1: version, name[2048 wchar], id, type, location, isCurrentPredefined, isPredefinedValid,
    // predefinedValue (union, 4100 bytes), currentValue (union, 4100 bytes).
    private const int SettingSize = 12320, SettingId = 4100, SettingType = 4104, SettingLocation = 4108, SettingCurrent = 8220;
    // NVDRS_APPLICATION_V1: version, isPredefined, appName[2048], userFriendlyName[2048], launcher[2048].
    private const int AppSize = 12296, AppName = 8;
    // NVDRS_PROFILE_V1: version, profileName[2048], gpuSupport, isPredefined, numOfApps, numOfSettings.
    private const int ProfileSize = 4116, ProfileName = 4;
    private const int NotFound = -166; // NVAPI_EXECUTABLE_NOT_FOUND
    private const int SettingNotFound = -160; // NVAPI_SETTING_NOT_FOUND

    public sealed record Backup(string Exe, uint? Value, bool CreatedProfile, string? ProfileName);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr QueryInterfaceFn(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SessionFn(out IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SessionOnlyFn(IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FindAppFn(IntPtr session, IntPtr appName, out IntPtr profile, IntPtr app);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetSettingFn(IntPtr session, IntPtr profile, uint id, IntPtr setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetSettingFn(IntPtr session, IntPtr profile, IntPtr setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeleteSettingFn(IntPtr session, IntPtr profile, uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateProfileFn(IntPtr session, IntPtr profileInfo, out IntPtr profile);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateAppFn(IntPtr session, IntPtr profile, IntPtr app);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeleteProfileFn(IntPtr session, IntPtr profile);

    // The V-Sync value set in this game's own driver profile; null when the game has no profile or follows the global one.
    public static uint? Read(string exe)
    {
        uint? result = null;
        Run(exe, false, (api, session, profile) => { result = api.Get(session, profile); return true; });
        return result;
    }
    public static string BackupPath(string backups, string exe) => Path.Combine(backups, exe + ".nvvsync.json");
    public static bool OwnedByLotus(string backups, string exe) => File.Exists(BackupPath(backups, exe));

    // Forces the driver's V-Sync on for this exe. The first call keeps the previous state in a backup.
    public static bool ForceOnFor(string exe, string backups)
    {
        Directory.CreateDirectory(backups);
        var path = BackupPath(backups, exe);
        return Run(exe, true, (api, session, profile) =>
        {
            if (!File.Exists(path))
                File.WriteAllText(path, JsonSerializer.Serialize(new Backup(exe, api.Get(session, profile), api.Created, api.Created ? ProfileNameFor(exe) : null)));
            return api.Set(session, profile, ForceOn) && api.Save(session) && api.Get(session, profile) == ForceOn;
        });
    }

    // Puts back what the backup recorded and removes it. No backup: nothing to undo.
    public static bool Restore(string exe, string backups)
    {
        var path = BackupPath(backups, exe);
        if (!File.Exists(path)) return true;
        var backup = JsonSerializer.Deserialize<Backup>(File.ReadAllText(path));
        if (backup is null || !string.Equals(backup.Exe, exe, StringComparison.OrdinalIgnoreCase)) return false;
        var ok = Run(exe, false, (api, session, profile) =>
        {
            if (backup.CreatedProfile) return api.DeleteProfile(session, profile) && api.Save(session);
            var done = backup.Value is { } v ? api.Set(session, profile, v) : api.Delete(session, profile);
            return done && api.Save(session) && api.Get(session, profile) == backup.Value;
        }, missingIsFine: true);
        if (ok) File.Delete(path);
        return ok;
    }

    private static string ProfileNameFor(string exe) => "Lotus - " + Path.GetFileNameWithoutExtension(exe);

    private sealed class Api(QueryInterfaceFn qi)
    {
        public bool Created;
        private T Fn<T>(uint id) where T : Delegate => qi(id) is var p && p != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<T>(p) : throw new EntryPointNotFoundException();
        public uint? Get(IntPtr session, IntPtr profile)
        {
            var s = NewSetting();
            try
            {
                var r = Fn<GetSettingFn>(QiGetSetting)(session, profile, VsyncModeId, s);
                // Location 0 = this profile; anything else is inherited (global/base/default).
                return r == 0 && Marshal.ReadInt32(s, SettingLocation) == 0 ? (uint)Marshal.ReadInt32(s, SettingCurrent) : null;
            }
            finally { Marshal.FreeHGlobal(s); }
        }
        public bool Set(IntPtr session, IntPtr profile, uint value)
        {
            var s = NewSetting();
            try
            {
                Marshal.WriteInt32(s, SettingId, (int)VsyncModeId); Marshal.WriteInt32(s, SettingType, 0); // NVDRS_DWORD_TYPE
                Marshal.WriteInt32(s, SettingCurrent, (int)value);
                return Fn<SetSettingFn>(QiSetSetting)(session, profile, s) == 0;
            }
            finally { Marshal.FreeHGlobal(s); }
        }
        public bool Delete(IntPtr session, IntPtr profile) => Fn<DeleteSettingFn>(QiDeleteSetting)(session, profile, VsyncModeId) is 0 or SettingNotFound;
        public bool DeleteProfile(IntPtr session, IntPtr profile) => Fn<DeleteProfileFn>(QiDeleteProfile)(session, profile) == 0;
        public bool Save(IntPtr session) => Fn<SessionOnlyFn>(QiSave)(session) == 0;
        public int Find(IntPtr session, string exe, out IntPtr profile)
        {
            var name = Wide(exe, 4096); var app = Zeroed(AppSize, 1);
            try { return Fn<FindAppFn>(QiFindApp)(session, name, out profile, app); }
            finally { Marshal.FreeHGlobal(name); Marshal.FreeHGlobal(app); }
        }
        public bool Create(IntPtr session, string exe, out IntPtr profile)
        {
            profile = IntPtr.Zero;
            var info = Zeroed(ProfileSize, 1); var app = Zeroed(AppSize, 1);
            try
            {
                WriteWide(info, ProfileName, ProfileNameFor(exe));
                if (Fn<CreateProfileFn>(QiCreateProfile)(session, info, out profile) != 0) return false;
                WriteWide(app, AppName, exe);
                if (Fn<CreateAppFn>(QiCreateApp)(session, profile, app) != 0) return false;
                Created = true;
                return true;
            }
            finally { Marshal.FreeHGlobal(info); Marshal.FreeHGlobal(app); }
        }
        private static IntPtr NewSetting() => Zeroed(SettingSize, 1);
        private static IntPtr Zeroed(int size, int version)
        {
            var p = Marshal.AllocHGlobal(size);
            Marshal.Copy(new byte[size], 0, p, size);
            Marshal.WriteInt32(p, 0, size | (version << 16));
            return p;
        }
        private static IntPtr Wide(string text, int bytes) { var p = Marshal.AllocHGlobal(bytes); Marshal.Copy(new byte[bytes], 0, p, bytes); WriteWide(p, 0, text); return p; }
        private static void WriteWide(IntPtr p, int offset, string text)
        {
            var chars = text.Length > 2047 ? text[..2047] : text;
            var data = System.Text.Encoding.Unicode.GetBytes(chars);
            Marshal.Copy(data, 0, p + offset, data.Length);
        }
    }

    // Opens a driver settings session, finds (or, when create, makes) this exe's profile and runs the action.
    private static bool Run(string exe, bool create, Func<Api, IntPtr, IntPtr, bool> action, bool missingIsFine = false)
    {
        if (string.IsNullOrWhiteSpace(exe) || exe.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        if (!NativeLibrary.TryLoad("nvapi64.dll", out var lib)) return false;
        var session = IntPtr.Zero;
        try
        {
            if (!NativeLibrary.TryGetExport(lib, "nvapi_QueryInterface", out var qiPtr)) return false;
            var qi = Marshal.GetDelegateForFunctionPointer<QueryInterfaceFn>(qiPtr);
            var api = new Api(qi);
            T Fn<T>(uint id) where T : Delegate => qi(id) is var p && p != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<T>(p) : throw new EntryPointNotFoundException();
            if (Fn<InitFn>(QiInitialize)() != 0) return false;
            if (Fn<SessionFn>(QiCreateSession)(out session) != 0) return false;
            if (Fn<SessionOnlyFn>(QiLoad)(session) != 0) return false;
            var found = api.Find(session, exe, out var profile);
            if (found == NotFound)
            {
                if (!create) return missingIsFine;
                if (!api.Create(session, exe, out profile)) return false;
            }
            else if (found != 0) return false;
            return action(api, session, profile);
        }
        catch (Exception ex) when (ex is MarshalDirectiveException or EntryPointNotFoundException or AccessViolationException or IOException or UnauthorizedAccessException or JsonException) { return false; }
        finally
        {
            if (session != IntPtr.Zero)
                try { if (qi_(lib) is { } q && q(QiDestroySession) is var d && d != IntPtr.Zero) Marshal.GetDelegateForFunctionPointer<SessionOnlyFn>(d)(session); } catch (EntryPointNotFoundException) { }
            NativeLibrary.Free(lib);
        }
    }
    private static QueryInterfaceFn? qi_(IntPtr lib) => NativeLibrary.TryGetExport(lib, "nvapi_QueryInterface", out var p) ? Marshal.GetDelegateForFunctionPointer<QueryInterfaceFn>(p) : null;
}
