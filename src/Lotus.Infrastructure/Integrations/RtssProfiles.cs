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
        ["Framerate"] = ["Limit", "LimitDenominator", "ReflexSleep", "SyncLimiter", "SyncScanline0"],
        ["Statistics"] = ["FrametimeCalc"]
    };
    [GeneratedRegex(@"^(Global|[A-Za-z0-9 _.\-]{1,120}\.exe)$")] private static partial Regex ProfileName();
    [GeneratedRegex(@"^-?[0-9A-Fa-f]{1,10}$")] private static partial Regex SafeValue();

    public static string DefaultProfilesDir => Path.Combine(RtssInstall.Dir, "Profiles");
    public static string DefaultBackupDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "RtssBackups");

    public static void Validate(RtssRequest request)
    {
        if (!ProfileName().IsMatch(request.Profile)) throw new InvalidOperationException("Invalid RivaTuner profile name.");
        foreach (var c in request.Changes)
            if (!Allowed.TryGetValue(c.Section, out var keys) || !keys.Contains(c.Key) || !SafeValue().IsMatch(c.Value))
                throw new InvalidOperationException($"Change not allowed: [{c.Section}] {c.Key}.");
            else if(c.Section=="Statistics" && c.Value is not ("0" or "1"))
                throw new InvalidOperationException("Invalid frametime calculation point.");
    }

    // Applies allowed changes, keeping the first original copy in backupDir. A new per-game profile holds only these keys:
    // RTSS reads everything else from Global (its own templates work the same way).
    // On disk the global profile is "Global" and a game profile is "<game>.exe.cfg" (as RTSS names them).
    public static string FileName(string profile) => profile == "Global" ? profile : profile + ".cfg";
    public static void Apply(RtssRequest request, string profilesDir, string backupDir)
    {
        Validate(request);
        var path = Path.Combine(profilesDir, FileName(request.Profile));
        Directory.CreateDirectory(backupDir);
        BackupOnce(path, Path.Combine(backupDir, request.Profile + ".original"));
        var ini = new IniText(File.Exists(path) ? ReadAnsi(path) : "");
        foreach (var c in request.Changes) ini.Set(c.Section, c.Key, c.Value);
        WriteAnsi(path, ini.ToString());
    }

    public static bool Restore(string profile, string profilesDir, string backupDir)
    {
        Validate(new(profile, []));
        return RestoreBackup(Path.Combine(backupDir, profile + ".original"), Path.Combine(profilesDir, FileName(profile)));
    }

    public static string? Read(string profile, string profilesDir) { var p = Path.Combine(profilesDir, FileName(profile)); return File.Exists(p) ? ReadAnsi(p) : null; }

    // RTSS files are ANSI. Latin-1 maps every byte to one char, so untouched lines are written back byte for byte.
    public static string ReadAnsi(string path) => File.ReadAllText(path, System.Text.Encoding.Latin1);
    public static void WriteAnsi(string path, string text)
    {
        // A RivaTuner that was never opened has no Profiles folder yet (it creates it on first run).
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } folder) Directory.CreateDirectory(folder);
        var temp = path + ".lotus.tmp"; File.WriteAllText(temp, text, System.Text.Encoding.Latin1); File.Move(temp, path, overwrite: true);
    }
    // Backups are exact byte copies (or a marker when the file did not exist); restore writes those bytes back.
    private static readonly byte[] AbsentMarker = "<absent>"u8.ToArray();
    public static void BackupOnce(string source, string backup)
    {
        if (File.Exists(backup)) return;
        if (File.Exists(source)) File.Copy(source, backup); else File.WriteAllBytes(backup, AbsentMarker);
    }
    public static bool RestoreBackup(string backup, string target)
    {
        if (!File.Exists(backup)) return false;
        var bytes = File.ReadAllBytes(backup);
        if (bytes.AsSpan().SequenceEqual(AbsentMarker)) File.Delete(target); else File.WriteAllBytes(target, bytes);
        File.Delete(backup);
        return true;
    }
}

// Where RivaTuner is installed. Its installer records the folder under HKLM\SOFTWARE\(WOW6432Node\)Unwinder\RTSS
// (InstallDir); only administrators can write there, so the elevated helper may trust it. Any value without RTSS.exe
// inside is ignored and the default folder is used.
public static class RtssInstall
{
    public static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "RivaTuner Statistics Server");
    public static string Dir => FromRegistry() ?? DefaultDir;
    private static string? FromRegistry()
    {
        if (!OperatingSystem.IsWindows()) return null;
        foreach (var key in new[] { @"SOFTWARE\WOW6432Node\Unwinder\RTSS", @"SOFTWARE\Unwinder\RTSS" })
        {
            try
            {
                using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key);
                if (k?.GetValue("InstallDir") is string dir && dir.Length > 0 && Valid(dir)) return Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return null;
    }
    public static bool Valid(string dir) => Path.IsPathFullyQualified(dir) && File.Exists(Path.Combine(dir, "RTSS.exe"));
}

// Closes RivaTuner and opens it again from its installed folder (elevated helper only, after the player's UAC "yes").
// Only RTSS.exe is closed; its profiles (limits, Reflex) are files and stay as they are.
// openIfClosed: also opens a closed RivaTuner, inside the same approved step (RTSS.exe itself needs administrator, so
// opening it from the helper spares the player a second Windows prompt), and waits until it answers.
[SupportedOSPlatform("windows")]
public static class RtssRestart
{
    public static void Run(bool openIfClosed = false)
    {
        var exe = Path.Combine(RtssInstall.Dir, "RTSS.exe");
        if (!File.Exists(exe)) return;
        var was = false;
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("RTSS"))
        {
            using (p) { was = true; try { p.Kill(); p.WaitForExit(5000); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { } }
        }
        if (!was && !openIfClosed) return; // not open: nothing to reopen (the next game start opens it as usual)
        if (was) Thread.Sleep(600);
        using var started = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = RtssInstall.Dir });
        for (var i = 0; i < 40 && !Games.RtssSharedMemory.IsAvailable(); i++) Thread.Sleep(250); // up to 10 s
    }
}

// Asks the running RTSS to reload profiles through its documented hooks DLL (same call MSI Afterburner uses).
[SupportedOSPlatform("windows")]
public static class RtssReload
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void UpdateProfilesProc();
    public static bool TryUpdate()
    {
        var dll = Path.Combine(RtssInstall.Dir, "RTSSHooks64.dll");
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
        // The invoking user's Lotus folder: UAC may elevate as another account, whose %LOCALAPPDATA% differs.
        var data = DataRoot(args);
        var backups = Path.Combine(data, "RtssBackups");
        try
        {
            if (args[i] == "--rtss-layout")
            {
                // The layouts run on RivaTuner's OverlayEditor plugin (shipped since RTSS 7.3); without it nothing would show.
                if (!File.Exists(Path.Combine(RtssInstall.Dir, "Plugins", "Client", "OverlayEditor.dll"))) return 6;
                RtssOverlayPlugin.ActivateLayout(args[i + 1], LotusOverlayLayout.DefaultConfig, Path.Combine(RtssInstall.Dir, "Profiles", "Config"), backups, Path.Combine(data, "Overlays"), Path.Combine(RtssInstall.Dir,"Profiles","Global"));
                if (args.Contains("--rtss-open")) RtssRestart.Run(openIfClosed: true); // ready to show: reopened, or opened when closed
                else if (args.Contains("--rtss-restart")) RtssRestart.Run(); // profiles and limiter live in files, untouched by a restart
            }
            else if (args[i] == "--rtss-layout-restore")
            {
                if (!RtssOverlayPlugin.RestoreLayout(LotusOverlayLayout.DefaultConfig, Path.Combine(RtssInstall.Dir, "Profiles", "Config"), backups, Path.Combine(RtssInstall.Dir,"Profiles","Global"))) return 4;
                if(args.Contains("--rtss-restart"))RtssRestart.Run();
            }
            else if (args[i] == "--rtss-apply")
            {
                var requestPath = Path.GetFullPath(args[i + 1]);
                var allowedRoot = Path.Combine(data, "Requests") + Path.DirectorySeparatorChar;
                if (!requestPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase)) return 3;
                var request = JsonSerializer.Deserialize<RtssRequest>(File.ReadAllText(requestPath)) ?? throw new InvalidDataException();
                RtssProfiles.Apply(request, RtssProfiles.DefaultProfilesDir, backups);
            }
            else if (!RtssProfiles.Restore(args[i + 1], RtssProfiles.DefaultProfilesDir, backups)) return 4;
            RtssReload.TryUpdate();
            // Frame generation preset without VRR: the NVIDIA driver's V-Sync for this game, with its own backup.
            var nvOn = Array.IndexOf(args, "--nv-vsync-on"); var nvOff = Array.IndexOf(args, "--nv-vsync-restore");
            if (nvOn >= 0 && (nvOn + 1 >= args.Length || !ValidExe(args[nvOn + 1]) || !NvidiaDriverVsync.ForceOnFor(args[nvOn + 1], backups))) return 5;
            if (nvOff >= 0 && (nvOff + 1 >= args.Length || !ValidExe(args[nvOff + 1]) || !NvidiaDriverVsync.Restore(args[nvOff + 1], backups))) return 5;
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or InvalidDataException) { return 2; }
    }

    private static bool ValidExe(string exe)
    {
        try { RtssProfiles.Validate(new(exe, [])); return exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase); }
        catch (InvalidOperationException) { return false; }
    }

    // Accepts only an existing folder named "Lotus"; otherwise uses this account's own %LOCALAPPDATA%\Lotus.
    public static string DataRoot(string[] args)
    {
        var at = Array.IndexOf(args, "--lotus-data");
        if (at >= 0 && at + 1 < args.Length)
        {
            var dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[at + 1]));
            if (Path.GetFileName(dir).Equals("Lotus", StringComparison.OrdinalIgnoreCase) && Directory.Exists(dir)) return dir;
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus");
    }
}

// Lotus overlay preset for RivaTuner's Global profile: FPS + frametime graph, rose-gold text on a translucent dark box, top-left.
[SupportedOSPlatform("windows")]
public sealed class WindowsRtssIntegration(string? profilesDir = null, string? backupDir = null, Func<string[], Task<int>>? elevatedRunner = null,
    string? overlayConfig = null, string? overlaysDir = null, string? sharedMemory = null) : Lotus.Core.Integrations.IRtssIntegration
{
    private string Map => sharedMemory ?? RtssSharedMemory.DefaultMap;
    private string InstallDir => Path.GetDirectoryName(Profiles)!;
    // Open = its shared memory exists. Nothing is started or changed by asking.
    public bool? Running() => RtssSharedMemory.IsAvailable(Map);
    // Opens RivaTuner (its own manifest asks Windows for administrator) and waits up to 10 s until it answers, so the
    // game started next is hooked. False when it is not installed, the player declined the prompt, or it did not come up.
    public async Task<bool> StartAsync()
    {
        if (Running() == true) return true;
        var exe = Path.Combine(InstallDir, "RTSS.exe");
        if (!File.Exists(exe)) return false;
        try { using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = InstallDir }); }
        catch (System.ComponentModel.Win32Exception) { return false; } // UAC declined
        for (var i = 0; i < 40; i++) { await Task.Delay(250); if (Running() == true) return true; }
        return false;
    }
    // The limit RivaTuner applies to every game without its own profile ([Framerate] of Global); null when none.
    public double? GlobalCap() => RtssProfiles.Read("Global", Profiles) is { } raw ? CapOf(new IniText(raw)) : null;
    private static double? CapOf(IniText ini)
    {
        if (!int.TryParse(ini.Get("Framerate", "Limit"), out var limit) || limit <= 0) return null;
        return int.TryParse(ini.Get("Framerate", "LimitDenominator"), out var d) && d > 1 ? limit / (double)d : limit;
    }
    // The 3D API of this game while it runs with RivaTuner ("Direct3D 12", "Vulkan"...); null when unknown.
    public string? GameApi(string exe) => RtssSharedMemory.ApiOf(exe, Map);
    private string OverlayConfig => overlayConfig ?? LotusOverlayLayout.DefaultConfig;
    private string Overlays => overlaysDir ?? LotusOverlayLayout.OverlaysDir;
    public string ExportLayout(string variant = LotusOverlayLayout.Main) => LotusOverlayLayout.Export(Overlays, variant);
    // Active when OverlayEditor uses one of the Lotus layouts (main or Neon) from Lotus's folder.
    public bool LayoutActive() => LotusOverlayLayout.ActiveLayout(OverlayConfig) is { } active && LotusOverlayLayout.Variants
        .Any(v => string.Equals(Path.GetFullPath(active), Path.GetFullPath(Path.Combine(Overlays, LotusOverlayLayout.FileFor(v))), StringComparison.OrdinalIgnoreCase));
    public bool HasLayoutBackup() => File.Exists(Path.Combine(Backups, "OverlayEditor.cfg.original"));
    public string? ActiveLayoutName() => LotusOverlayLayout.ActiveLayout(OverlayConfig) is { Length: > 0 } p ? Path.GetFileNameWithoutExtension(p) : null;
    // restart: in the same approved step RivaTuner is reopened (or opened, when closed), so the new layout shows at once
    // without the player doing anything and without a second Windows prompt.
    public async Task<bool> ActivateLayoutAsync(string variant = LotusOverlayLayout.Main, bool restart = false)
    {
        // Checked here too, so a RivaTuner without OverlayEditor does not cost the player a Windows permission prompt.
        if (!File.Exists(Path.Combine(InstallDir, "Plugins", "Client", "OverlayEditor.dll"))) { LastFailure = "plugin"; return false; }
        var code = await RunElevated(restart ? ["--rtss-layout", ExportLayout(variant), "--rtss-open"] : ["--rtss-layout", ExportLayout(variant)]);
        LastFailure = code switch { 0 => LayoutActive() ? null : "other", 1223 => "uac", 6 => "plugin", _ => "other" };
        return LastFailure is null;
    }
    public string? LastFailure { get; private set; }
    public async Task<bool> RestoreLayoutAsync() => await RunElevated(Running()==true?["--rtss-layout-restore","--rtss-restart"]:["--rtss-layout-restore"]) == 0 && !HasLayoutBackup();

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

    // ---- Low latency per game ----
    // [Framerate] SyncLimiter follows the order of RTSS's limiter modes: 0 async, 1 front edge sync, 2 back edge sync,
    // 3 NVIDIA Reflex (checked in RTSS 7.3.7 on 29/10/2026: the profile shows "NVIDIA Reflex"). RTSS documents that the
    // Reflex limiter also enables Reflex low-latency in games without native
    // Reflex; it works on NVIDIA GPUs with Direct3D 11/12 and falls back to async elsewhere.
    public const string ReflexLimiter = "3", FrontEdgeLimiter = "1";
    // Scanline sync ("scanline": responsive without V-Sync). RTSS presents each frame at a fixed scanline of the monitor,
    // so frames stay even like V-Sync without its queue; a negative index counts from the bottom of the visible picture,
    // which puts the tear line in the blanking interval. Needs GPU headroom: Lotus offers it only after a measurement
    // shows the game holding the refresh rate with the GPU partly idle. The frame-rate limit is off in this mode.
    public const string DefaultScanline = "-40";
    // "fluid" (front edge sync) | "responsive" (NVIDIA Reflex) | "scanline" | null when this game has no Lotus limiter.
    public string? LimiterMode(string exe)
    {
        try { RtssProfiles.Validate(new(exe, [])); } catch (InvalidOperationException) { return null; }
        if (RtssProfiles.Read(exe, Profiles) is not { } raw) return null;
        var ini = new IniText(raw);
        if (int.TryParse(ini.Get("Framerate", "SyncScanline0"), out var line) && line != 0) return "scanline";
        if (!int.TryParse(ini.Get("Framerate", "Limit"), out var limit) || limit <= 0) return null;
        // Reflex plus the NVIDIA driver's V-Sync set by Lotus for this game: the frame generation preset without VRR.
        return ini.Get("Framerate", "SyncLimiter") switch { ReflexLimiter => NvidiaDriverVsync.OwnedByLotus(Backups, exe) && NvidiaDriverVsync.Read(exe) == NvidiaDriverVsync.ForceOn ? "fgvsync" : "responsive", FrontEdgeLimiter => "fluid", "2" => "back", "0" => "async", _ => null };
    }
    public double? LimiterCap(string exe)
    {
        try { RtssProfiles.Validate(new(exe, [])); } catch (InvalidOperationException) { return null; }
        return RtssProfiles.Read(exe, Profiles) is { } raw ? CapOf(new IniText(raw)) : null;
    }
    // The cap may be fractional (37.5 on 75 Hz): RTSS stores Limit / LimitDenominator.
    public async Task<bool> EnableLimiterAsync(string exe, double fpsCap, string mode)
    {
        var uncapped = mode is "scanline";
        if (mode is not ("fluid" or "responsive" or "scanline" or "fgvsync" or "async" or "back") || !double.IsFinite(fpsCap) || (!uncapped && fpsCap is < 20 or > 1000)) return false;
        RtssChange[] changes;
        if (uncapped)
            changes = [new("Framerate", "Limit", "0"), new("Framerate", "LimitDenominator", "1"), new("Framerate", "SyncLimiter", "0"), new("Framerate", "SyncScanline0", DefaultScanline)];
        else
        {
            var whole = Math.Abs(fpsCap - Math.Round(fpsCap)) < 0.001;
            var limit = whole ? (int)Math.Round(fpsCap) : (int)Math.Round(fpsCap * 100);
            changes = [new("Framerate", "Limit", limit.ToString(System.Globalization.CultureInfo.InvariantCulture)), new("Framerate", "LimitDenominator", whole ? "1" : "100"),
                new("Framerate", "SyncLimiter", mode switch { "fluid" => FrontEdgeLimiter, "responsive" or "fgvsync" => ReflexLimiter, "back" => "2", _ => "0" }), new("Framerate", "SyncScanline0", "0")];
        }
        // Match the diagnostic time point to the pacing strategy; never smooth or discard hitch samples.
        // Front edge controls presentation; the other modes prioritize frame-start/return timing.
        var framePoint=mode=="fluid"?"1":"0";
        changes=[..changes,new("Statistics","FrametimeCalc",framePoint)];
        var dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Backups))!, "Requests");
        Directory.CreateDirectory(dir);
        var request = Path.Combine(dir, "rtss-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(request, JsonSerializer.Serialize(new RtssRequest(exe, changes)));
        // "fgvsync": frame generation on a fixed-refresh monitor. RivaTuner's Reflex limiter at the paced cap plus the NVIDIA
        // driver's V-Sync for this game (games lock their own V-Sync with DLSS FG). Any other mode gives a V-Sync Lotus set back.
        string[] driver = mode == "fgvsync" ? ["--nv-vsync-on", exe] : ["--nv-vsync-restore", exe];
        try { return await RunElevated(["--rtss-apply", request, ..driver]) == 0 && LimiterMode(exe) == mode
            && (uncapped || (LimiterCap(exe) is { } read && Math.Abs(read-fpsCap)<0.011))
            && RtssProfiles.Read(exe,Profiles) is {} applied && new IniText(applied).Get("Statistics","FrametimeCalc")==framePoint; }
        finally { File.Delete(request); }
    }
    public async Task<bool> DisableLowLatencyAsync(string exe)
    {
        RtssProfiles.Validate(new(exe, []));
        var backup=Path.Combine(Backups,exe+".original");
        if(!File.Exists(backup)) return false;
        var expected=await File.ReadAllBytesAsync(backup);
        if(await RunElevated(["--rtss-restore", exe, "--nv-vsync-restore", exe])!=0 || NvidiaDriverVsync.OwnedByLotus(Backups, exe)) return false;
        var target=Path.Combine(Profiles,RtssProfiles.FileName(exe));
        if(expected.AsSpan().SequenceEqual("<absent>"u8)) return !File.Exists(target);
        if(!File.Exists(target)) return false;
        var actual=await File.ReadAllBytesAsync(target);
        return expected.AsSpan().SequenceEqual(actual);
    }

    // Relaunches Lotus elevated (UAC) only for this operation; the helper exits without opening a window.
    private Task<int> RunElevated(string[] args) => elevatedRunner?.Invoke(args) ?? Task.Run(() =>
    {
        var info = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
        foreach (var a in args) info.ArgumentList.Add(a);
        info.ArgumentList.Add("--lotus-data"); info.ArgumentList.Add(Path.GetDirectoryName(Path.GetFullPath(Backups))!);
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
    public static string DefaultConfig => Path.Combine(RtssInstall.Dir, "Plugins", "Client", "OverlayEditor.cfg");
    // The layout is the user's own "Lotus Neon" design (ANSI, with its alpha artwork next to it); both are shipped byte for byte.
    public const string ArtFile = "Lotus-Arte-Alpha.png";
    private static byte[] Resource(string name)
    {
        using var stream = typeof(LotusOverlayLayout).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("Embedded overlay file missing: " + name);
        using var copy = new MemoryStream(); stream.CopyTo(copy); return copy.ToArray();
    }
    internal static byte[] PreviewAsset(string name)
    {
        if(name!=ArtFile&&!Variants.Select(PremiumArt).Contains(name))throw new InvalidDataException("Unknown overlay artwork.");
        return Resource(name);
    }
    // Two layouts by the player: "top" (main: "Lotus Top Sombra", a bar across the top) and "neon" (optional card).
    // Third: "lateral", a compact column in the top-left corner (tools\New-LotusLateralOverlay.ps1, Neon's sources).
    // Fourth: "pulse", generated here in the colors of the active theme with the FPS limit line (LotusPulseLayout).
    // Fifth: "window" (experimental), RivaTuner draws nothing and the Lotus overlay window shows the numbers.
    public const string Main = "top", Neon = "neon", Lateral = "lateral", Pulse = "pulse", Window = "window";
    public const string PremiumTop = "premium-top", PremiumNeon = "premium-neon", PremiumPulse = "premium-pulse";
    public static string FileFor(string variant) => variant switch { PremiumTop => "Lotus-Premium-equilibrio.ovl", PremiumNeon => "Lotus-Premium-neon.ovl", PremiumPulse => "Lotus-Premium-pulse.ovl", Neon => "Lotus-Neon.ovl", Lateral => "Lotus-Lateral.ovl", Pulse => "Lotus-Pulse.ovl", Window => "Lotus-Window.ovl", _ => "Lotus.ovl" };
    public static string VariantOf(string? layoutName) => layoutName switch { "Lotus-Premium-equilibrio" => PremiumTop, "Lotus-Premium-neon" => PremiumNeon, "Lotus-Premium-pulse" => PremiumPulse, "Lotus-Neon" => Neon, "Lotus-Lateral" => Lateral, "Lotus-Pulse" => Pulse, "Lotus-Window" => Window, "Lotus" => Main, _ => "" };
    public static readonly string[] Variants = [Main, Neon, Lateral, Pulse, Window, PremiumTop, PremiumNeon, PremiumPulse];
    public static string? PremiumArt(string variant) => variant switch { PremiumTop => "Lotus-atlas-equilibrio.png", PremiumNeon => "Lotus-atlas-neon.png", PremiumPulse => "Lotus-atlas-pulse.png", _ => null };
    // True when RivaTuner's active layout is the "window" one (the Lotus overlay window should show during games).
    public static bool WindowActive(string? configPath = null) =>
        ActiveLayout(configPath ?? DefaultConfig) is { } p && Path.GetFileNameWithoutExtension(p).Equals("Lotus-Window", StringComparison.OrdinalIgnoreCase);
    // Set by the app: the active theme's colors and RivaTuner's global FPS limit, read when Pulse is (re)generated.
    public static LotusPulseLayout.Colors PulseColors { get; set; } = LotusPulseLayout.Default;
    public static Func<double?>? PulseCap { get; set; }
    private static byte[] Bytes(string variant)
    {
        if (variant is not (Pulse or Window)) return Resource(FileFor(variant));
        var neon = System.Text.Encoding.Latin1.GetString(Resource(FileFor(Neon)));
        var sources = neon[neon.IndexOf("[Source0]", StringComparison.Ordinal)..neon.IndexOf("[Layer0]", StringComparison.Ordinal)];
        if (variant == Window) return System.Text.Encoding.Latin1.GetBytes(LotusPulseLayout.Empty(sources));
        double? cap = null;
        try { cap = PulseCap?.Invoke(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Serilog.Log.Debug(ex, "Pulse: FPS limit"); }
        return System.Text.Encoding.Latin1.GetBytes(LotusPulseLayout.Build(sources, PulseColors, cap));
    }
    public static string Text(string variant = Main) => System.Text.Encoding.Latin1.GetString(Bytes(variant));
    public static string Export(string? dir = null, string variant = Main)
    {
        var folder = dir ?? OverlaysDir;
        Directory.CreateDirectory(folder);
        var art = PremiumArt(variant) ?? ArtFile;
        File.WriteAllBytes(Path.Combine(folder, art), Resource(art)); // EmbeddedImage= expects it beside the layout
        var target = Path.Combine(folder, FileFor(variant));
        File.WriteAllBytes(target, Bytes(variant));
        return target;
    }
    // A new Lotus version may ship a changed generated layout (Lotus-Lateral, built by tools\New-LotusLateralOverlay.ps1):
    // its exported copy is renewed when it differs; RivaTuner reads it the next time it starts. The
    // player's own designs (Lotus.ovl, Lotus-Neon.ovl) are never rewritten here. True when the copy was renewed.
    public static bool RefreshGenerated(string? dir = null)
    {
        var renewed = false;
        foreach (var variant in new[] { Lateral, Pulse }) // Pulse also follows the theme and the FPS limit
        {
            var target = Path.Combine(dir ?? OverlaysDir, FileFor(variant));
            try
            {
                if (!File.Exists(target)) continue; // never exported: nothing to renew
                var shipped = Bytes(variant);
                if (File.ReadAllBytes(target).AsSpan().SequenceEqual(shipped)) continue;
                File.WriteAllBytes(target, shipped);
                renewed = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Serilog.Log.Warning(ex, "Overlay layout refresh"); }
        }
        return renewed;
    }
    // Only a .ovl inside the Lotus overlays folder may be activated.
    public static void Activate(string layoutPath, string configPath, string backupDir, string overlaysDir)
    {
        var full = Path.GetFullPath(layoutPath);
        if (!full.StartsWith(Path.GetFullPath(overlaysDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !full.EndsWith(".ovl", StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            throw new InvalidOperationException("Layout outside the Lotus overlays folder.");
        Directory.CreateDirectory(backupDir);
        RtssProfiles.BackupOnce(configPath, Path.Combine(backupDir, "OverlayEditor.cfg.original"));
        var ini = new IniText(File.Exists(configPath) ? RtssProfiles.ReadAnsi(configPath) : ""); ini.Set("Settings", "Layout", full);
        RtssProfiles.WriteAnsi(configPath, ini.ToString());
    }
    public static bool Restore(string configPath, string backupDir)
    {
        return RtssProfiles.RestoreBackup(Path.Combine(backupDir, "OverlayEditor.cfg.original"), configPath);
    }
    public static string? ActiveLayout(string configPath) => File.Exists(configPath) ? new IniText(RtssProfiles.ReadAnsi(configPath)).Get("Settings", "Layout") : null;
}
