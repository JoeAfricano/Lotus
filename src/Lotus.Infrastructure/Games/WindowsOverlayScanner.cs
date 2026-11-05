using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using Lotus.Core.Games;
using Microsoft.Win32;

namespace Lotus.Infrastructure.Games;

// Read-only: running processes, the per-user Xbox capture setting and Discord's own settings file. Nothing is changed.
[SupportedOSPlatform("windows")]
// includeContext (the Tools screen only): also lists open launchers and the tools Lotus works with (RTSS, Afterburner,
// PresentMon) as compatible. Preparing a game keeps the original list, so those are never counted as problems.
public sealed class WindowsOverlayScanner(string? discordSettings = null, bool includeContext = false) : IOverlayScanner
{
    // Process name → (id, display name, heavy). RTSS is left out: Lotus uses it for its own overlay and FPS limit.
    private static readonly (string Process, string Id, string Name, bool Heavy)[] Apps =
    [
        ("NVIDIA Overlay", "nvidia", "NVIDIA overlay", false),
        ("NVIDIA Share", "nvidia", "NVIDIA overlay", false),
        ("RadeonSoftware", "amd", "AMD Software overlay", false),
        ("AMDRSServ", "amd", "AMD Software overlay", false),
        ("Overwolf", "overwolf", "Overwolf", true),
        ("Medal", "medal", "Medal", true),
        ("obs64", "obs", "OBS Studio", false),
        ("Outplayed", "overwolf", "Outplayed (Overwolf)", true),
        ("wallpaper64", "wallpaper", "Wallpaper Engine", true),
        ("wallpaper32", "wallpaper", "Wallpaper Engine", true),
        ("Lively", "wallpaper", "Lively Wallpaper", true),
        ("SignalRgb", "rgb", "SignalRGB", false),
        ("iCUE", "rgb", "Corsair iCUE", false),
        ("ArmouryCrate", "rgb", "Armoury Crate", false),
    ];

private static readonly (string Process, string Id, string Name)[] Context =
    [
        ("steam", "launcher-steam", "Steam"), ("steamwebhelper", "launcher-steam", "Steam"),
        ("EpicGamesLauncher", "launcher-epic", "Epic Games Launcher"), ("EpicWebHelper", "launcher-epic", "Epic Games Launcher"),
        ("EADesktop", "launcher-ea", "EA app"), ("EABackgroundService", "launcher-ea", "EA app"),
        ("upc", "launcher-ubisoft", "Ubisoft Connect"), ("UbisoftConnect", "launcher-ubisoft", "Ubisoft Connect"),
        ("Battle.net", "launcher-battlenet", "Battle.net"), ("RiotClientServices", "launcher-riot", "Riot Client"), ("GalaxyClient", "launcher-gog", "GOG Galaxy"),
        ("RTSS", "compat-rtss", "RivaTuner Statistics Server"), ("MSIAfterburner", "compat-afterburner", "MSI Afterburner"),
        ("PresentMon", "compat-presentmon", "PresentMon"), ("PresentMon-x64", "compat-presentmon", "PresentMon"),
    ];

    public static IReadOnlyList<OverlayFinding> FromProcesses(IEnumerable<string> running, bool discordHardwareAcceleration, bool xboxCapture, bool includeContext)
    {
        var names = running.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var list = FromProcesses(names, discordHardwareAcceleration, xboxCapture).ToList();
        if (!includeContext) return list;
        foreach (var group in Context.Where(c => names.Contains(c.Process)).GroupBy(c => c.Id))
            list.Add(new(group.Key, group.First().Name, false, [.. Context.Where(c => c.Id == group.Key).Select(c => c.Process)]));
        return list;
    }

    public static IReadOnlyList<OverlayFinding> FromProcesses(IEnumerable<string> running, bool discordHardwareAcceleration, bool xboxCapture)
    {
        var names = running.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var list = new List<OverlayFinding>();
        if (names.Contains("Discord") && discordHardwareAcceleration) list.Add(new("discord", "Discord", true, ["Discord"]));
        if (xboxCapture) list.Add(new("xbox", "Xbox Game Bar", false, ["GameBar", "GameBarFTServer"]));
        foreach (var a in Apps.Where(a => names.Contains(a.Process)))
            if (!list.Any(f => f.Id == a.Id)) list.Add(new(a.Id, a.Name, a.Heavy, [.. Apps.Where(x => x.Id == a.Id).Select(x => x.Process)]));
        return list;
    }

    public IReadOnlyList<OverlayFinding> Scan()
    {
        var running = new List<string>();
        foreach (var p in Process.GetProcesses()) using (p) { try { running.Add(p.ProcessName); } catch (InvalidOperationException) { } }
        return FromProcesses(running, DiscordHardwareAcceleration(), XboxCapture(), includeContext);
    }

    // Discord keeps "enableHardwareAcceleration" in its settings file; missing means on (the default).
    public bool DiscordHardwareAcceleration()
    {
        var file = discordSettings ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "discord", "settings.json");
        try
        {
            if (!File.Exists(file)) return true;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            return !doc.RootElement.TryGetProperty("enableHardwareAcceleration", out var v) || v.ValueKind != JsonValueKind.False;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return true; }
    }

    // Settings > Gaming > Captures: "Record what happened" and the capture feature itself.
    private static bool XboxCapture()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", writable: false);
            return key?.GetValue("AppCaptureEnabled") is int v && v == 1;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return false; }
    }
}
