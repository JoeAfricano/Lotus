using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lotus.Infrastructure.Games;

namespace Lotus.Infrastructure.Integrations;

// One allowed change to a RivaTuner profile (INI). Only the sections/keys below are accepted; anything else is rejected.
public sealed record RtssChange(string Section, string Key, string Value);
public sealed record RtssRequest(string Profile, IReadOnlyList<RtssChange> Changes);

public static partial class RtssProfiles
{
    public static readonly IReadOnlyDictionary<string, string[]> Allowed = new Dictionary<string, string[]>
    {
        ["OSD"] = ["EnableOSD", "EnableBgnd", "EnableFill", "BaseColor", "BgndColor", "FillColor", "PositionX", "PositionY", "ZoomRatio",
                   "EnableFrametimeHistory", "FrametimeHistoryWidth", "FrametimeHistoryHeight", "FrametimeHistoryStyle", "RefreshPeriod", "IntegerFramerate"],
        ["Framerate"] = ["Limit", "LimitDenominator", "ReflexSleep"]
    };
    [GeneratedRegex(@"^(Global|[A-Za-z0-9 _.\-]{1,120}\.exe)$")] private static partial Regex ProfileName();
    [GeneratedRegex(@"^-?[0-9A-Fa-f]{1,10}$")] private static partial Regex SafeValue();

    public static string DefaultProfilesDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "RivaTuner Statistics Server", "Profiles");
    public static string DefaultBackupDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "RtssBackups");

    public static void Validate(RtssRequest request)
    {
        if (!ProfileName().IsMatch(request.Profile)) throw new InvalidOperationException("Invalid RivaTuner profile name.");
        foreach (var c in request.Changes)
            if (!Allowed.TryGetValue(c.Section, out var keys) || !keys.Contains(c.Key) || !SafeValue().IsMatch(c.Value))
                throw new InvalidOperationException($"Change not allowed: [{c.Section}] {c.Key}.");
    }

    // Applies allowed changes, keeping the first original copy in backupDir. New per-game profiles start from the Global profile.
    public static void Apply(RtssRequest request, string profilesDir, string backupDir)
    {
        Validate(request);
        var path = Path.Combine(profilesDir, request.Profile);
        var original = File.Exists(path) ? File.ReadAllText(path) : null;
        Directory.CreateDirectory(backupDir);
        var backup = Path.Combine(backupDir, request.Profile + ".original");
        if (!File.Exists(backup)) File.WriteAllText(backup, original ?? "<absent>");
        var ini = new IniText(original ?? "");
        foreach (var c in request.Changes) ini.Set(c.Section, c.Key, c.Value);
        var temp = path + ".lotus.tmp"; File.WriteAllText(temp, ini.ToString()); File.Move(temp, path, overwrite: true);
    }

    public static bool Restore(string profile, string profilesDir, string backupDir)
    {
        Validate(new(profile, []));
        var backup = Path.Combine(backupDir, profile + ".original");
        if (!File.Exists(backup)) return false;
        var original = File.ReadAllText(backup); var path = Path.Combine(profilesDir, profile);
        if (original == "<absent>") File.Delete(path); else File.WriteAllText(path, original);
        File.Delete(backup);
        return true;
    }

    public static string? Read(string profile, string profilesDir) { var p = Path.Combine(profilesDir, profile); return File.Exists(p) ? File.ReadAllText(p) : null; }
}

// Asks the running RTSS to reload profiles through its documented hooks DLL (same call MSI Afterburner uses).
[SupportedOSPlatform("windows")]
public static class RtssReload
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void UpdateProfilesProc();
    public static bool TryUpdate()
    {
        var dll = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "RivaTuner Statistics Server", "RTSSHooks64.dll");
        if (!File.Exists(dll) || !NativeLibrary.TryLoad(dll, out var handle)) return false;
        try
        {
            if (!NativeLibrary.TryGetExport(handle, "UpdateProfiles", out var proc)) return false;
            Marshal.GetDelegateForFunctionPointer<UpdateProfilesProc>(proc)();
            return true;
        }
        finally { NativeLibrary.Free(handle); }
    }
}

// Entry point of the short-lived elevated helper: "Lotus.App.exe --rtss-apply <request.json>" or "--rtss-restore <profile>".
[SupportedOSPlatform("windows")]
public static class RtssElevatedHelper
{
    public static int? TryRun(string[] args)
    {
        var i = Array.FindIndex(args, a => a is "--rtss-apply" or "--rtss-restore" or "--rtss-layout" or "--rtss-layout-restore");
        if (i < 0 || (args[i] != "--rtss-layout-restore" && i + 1 >= args.Length)) return null;
        try
        {
            if (args[i] == "--rtss-layout")
                LotusOverlayLayout.Activate(args[i + 1], LotusOverlayLayout.DefaultConfig, RtssProfiles.DefaultBackupDir, LotusOverlayLayout.OverlaysDir);
            else if (args[i] == "--rtss-layout-restore")
            { if (!LotusOverlayLayout.Restore(LotusOverlayLayout.DefaultConfig, RtssProfiles.DefaultBackupDir)) return 4; }
            else if (args[i] == "--rtss-apply")
            {
                var requestPath = Path.GetFullPath(args[i + 1]);
                var allowedRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "Requests") + Path.DirectorySeparatorChar;
                if (!requestPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase)) return 3;
                var request = JsonSerializer.Deserialize<RtssRequest>(File.ReadAllText(requestPath)) ?? throw new InvalidDataException();
                RtssProfiles.Apply(request, RtssProfiles.DefaultProfilesDir, RtssProfiles.DefaultBackupDir);
            }
            else if (!RtssProfiles.Restore(args[i + 1], RtssProfiles.DefaultProfilesDir, RtssProfiles.DefaultBackupDir)) return 4;
            RtssReload.TryUpdate();
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or InvalidDataException) { return 2; }
    }
}

// Lotus overlay preset for RivaTuner's Global profile: FPS + frametime graph, rose-gold text on a translucent dark box, top-left.
[SupportedOSPlatform("windows")]
public sealed class WindowsRtssIntegration(string? profilesDir = null, string? backupDir = null, Func<string[], Task<int>>? elevatedRunner = null,
    string? overlayConfig = null, string? overlaysDir = null) : Lotus.Core.Integrations.IRtssIntegration
{
    private string OverlayConfig => overlayConfig ?? LotusOverlayLayout.DefaultConfig;
    private string Overlays => overlaysDir ?? LotusOverlayLayout.OverlaysDir;
    public string ExportLayout() => LotusOverlayLayout.Export(Overlays);
    public bool LayoutActive() =>
        LotusOverlayLayout.ActiveLayout(OverlayConfig) is { } active && string.Equals(Path.GetFullPath(active), Path.GetFullPath(Path.Combine(Overlays, "Lotus.ovl")), StringComparison.OrdinalIgnoreCase);
    public bool HasLayoutBackup() => File.Exists(Path.Combine(Backups, "OverlayEditor.cfg.original"));
    public async Task<bool> ActivateLayoutAsync() => await RunElevated(["--rtss-layout", ExportLayout()]) == 0 && LayoutActive();
    public async Task<bool> RestoreLayoutAsync() => await RunElevated(["--rtss-layout-restore"]) == 0 && !HasLayoutBackup();

    public static readonly IReadOnlyList<RtssChange> LotusOverlay =
    [
        new("OSD", "EnableOSD", "1"), new("OSD", "EnableBgnd", "1"), new("OSD", "EnableFill", "1"),
        new("OSD", "BaseColor", "FFEEC1A8"), new("OSD", "FillColor", "8C12090F"), new("OSD", "PositionX", "1"), new("OSD", "PositionY", "1"),
        new("OSD", "ZoomRatio", "2"), new("OSD", "EnableFrametimeHistory", "1"), new("OSD", "FrametimeHistoryWidth", "-32"),
        new("OSD", "FrametimeHistoryHeight", "-4"), new("OSD", "IntegerFramerate", "1"), new("OSD", "RefreshPeriod", "500")
    ];
    private string Profiles => profilesDir ?? RtssProfiles.DefaultProfilesDir;
    private string Backups => backupDir ?? RtssProfiles.DefaultBackupDir;

    public Lotus.Core.Integrations.ToolState State()
    {
        var exe = Path.Combine(Path.GetDirectoryName(Profiles)!, "RTSS.exe");
        return File.Exists(exe) ? new(true, System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).ProductVersion) : new(false, null);
    }
    public bool? OverlayConfigured()
    {
        if (RtssProfiles.Read("Global", Profiles) is not { } raw) return null;
        var ini = new IniText(raw);
        return LotusOverlay.All(c => string.Equals(ini.Get(c.Section, c.Key), c.Value, StringComparison.OrdinalIgnoreCase));
    }
    public bool HasBackup() => File.Exists(Path.Combine(Backups, "Global.original"));

    public async Task<bool> ApplyLotusOverlayAsync()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "Requests");
        Directory.CreateDirectory(dir);
        var request = Path.Combine(dir, "rtss-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(request, JsonSerializer.Serialize(new RtssRequest("Global", LotusOverlay)));
        try { return await RunElevated(["--rtss-apply", request]) == 0 && OverlayConfigured() == true; }
        finally { File.Delete(request); }
    }
    public async Task<bool> RestoreAsync() => await RunElevated(["--rtss-restore", "Global"]) == 0 && !HasBackup();

    // Relaunches Lotus elevated (UAC) only for this operation; the helper exits without opening a window.
    private Task<int> RunElevated(string[] args) => elevatedRunner?.Invoke(args) ?? Task.Run(() =>
    {
        var info = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
        foreach (var a in args) info.ArgumentList.Add(a);
        try { using var p = System.Diagnostics.Process.Start(info)!; p.WaitForExit(); return p.ExitCode; }
        catch (System.ComponentModel.Win32Exception) { return 1223; } // user declined the UAC prompt
    });
}

[SupportedOSPlatform("windows")]
public sealed class WindowsToolDetector : Lotus.Core.Integrations.IToolDetector
{
    private static Lotus.Core.Integrations.ToolState Of(string exe) =>
        File.Exists(exe) ? new(true, System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).ProductVersion) : new(false, null);
    public Lotus.Core.Integrations.ToolState Afterburner() => Of(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "MSI Afterburner", "MSIAfterburner.exe"));
    public Lotus.Core.Integrations.ToolState ScsKiller() => Of(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCSKiller.App", "current", "SCSKiller.exe"));
}

// Lotus overlay layout for RivaTuner's OverlayEditor plugin (FPS, 1%/0.1% low, frametime graph, CPU/GPU/VRAM/RAM rows).
// Exported to %LOCALAPPDATA%\Lotus\Overlays; activation changes only [Settings] Layout in OverlayEditor.cfg (elevated helper).
public static class LotusOverlayLayout
{
    public static string OverlaysDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "Overlays");
    public static string DefaultConfig => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "RivaTuner Statistics Server", "Plugins", "Client", "OverlayEditor.cfg");
    public static string Text()
    {
        using var stream = typeof(LotusOverlayLayout).Assembly.GetManifestResourceStream("Lotus.ovl") ?? throw new InvalidOperationException("Embedded overlay missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    public static string Export(string? dir = null)
    {
        var target = Path.Combine(dir ?? OverlaysDir, "Lotus.ovl");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, Text(), new System.Text.UTF8Encoding(false));
        return target;
    }
    // Only a .ovl inside the Lotus overlays folder may be activated.
    public static void Activate(string layoutPath, string configPath, string backupDir, string overlaysDir)
    {
        var full = Path.GetFullPath(layoutPath);
        if (!full.StartsWith(Path.GetFullPath(overlaysDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !full.EndsWith(".ovl", StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            throw new InvalidOperationException("Layout outside the Lotus overlays folder.");
        var original = File.Exists(configPath) ? File.ReadAllText(configPath) : null;
        Directory.CreateDirectory(backupDir);
        var backup = Path.Combine(backupDir, "OverlayEditor.cfg.original");
        if (!File.Exists(backup)) File.WriteAllText(backup, original ?? "<absent>");
        var ini = new IniText(original ?? ""); ini.Set("Settings", "Layout", full);
        var temp = configPath + ".lotus.tmp"; File.WriteAllText(temp, ini.ToString()); File.Move(temp, configPath, overwrite: true);
    }
    public static bool Restore(string configPath, string backupDir)
    {
        var backup = Path.Combine(backupDir, "OverlayEditor.cfg.original");
        if (!File.Exists(backup)) return false;
        var original = File.ReadAllText(backup);
        if (original == "<absent>") File.Delete(configPath); else File.WriteAllText(configPath, original);
        File.Delete(backup);
        return true;
    }
    public static string? ActiveLayout(string configPath) => File.Exists(configPath) ? new IniText(File.ReadAllText(configPath)).Get("Settings", "Layout") : null;
}
