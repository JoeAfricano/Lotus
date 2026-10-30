using System.Runtime.Versioning;
using Lotus.Core.Games;
using Microsoft.Win32;

namespace Lotus.Infrastructure.Games;

// Read-only discovery from launcher manifests and registry. Design informed by ResetExplode's GameLibrary (MIT,
// GustavoReset, commit ce1d9b3), reimplemented here with Xbox/Game Pass and EA app support.
[SupportedOSPlatform("windows")]
public sealed class WindowsGameLibrary(string? steamPath = null, string? epicManifests = null, IReadOnlyList<string>? xboxRoots = null) : IGameLibrary
{
    public LibraryScan Scan(IReadOnlyList<GameEntry> manual)
    {
        var games = new List<GameEntry>(); var errors = new List<string>();
        void Run(string store, Func<IEnumerable<GameEntry>> scan)
        {
            try { games.AddRange(scan()); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or FormatException or System.Text.Json.JsonException or System.Xml.XmlException)
            { errors.Add(store + ": " + ex.GetType().Name); }
        }
        Run(GameStores.Steam, Steam); Run(GameStores.Epic, Epic); Run(GameStores.Xbox, Xbox);
        Run(GameStores.Gog, Gog); Run(GameStores.Ea, Ea); Run(GameStores.Ubisoft, Ubisoft);
        games.AddRange(manual.Where(m => m.ExePath is not null && File.Exists(m.ExePath)));
        var unique = games.GroupBy(g => g.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        return new(unique, errors);
    }

    // --- Steam ---
    internal static string? DefaultSteamPath()
    {
        var p = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string
            ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")?.GetValue("InstallPath") as string;
        return p is null ? null : Path.GetFullPath(p.Replace('/', '\\'));
    }
    private IEnumerable<GameEntry> Steam()
    {
        var steam = steamPath ?? DefaultSteamPath();
        if (steam is null || !Directory.Exists(steam)) yield break;
        var libraries = new List<string> { steam };
        var folders = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(folders)) libraries.AddRange(GameParsers.SteamLibraries(File.ReadAllText(folders)));
        foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                SteamApp? app;
                try { app = GameParsers.SteamManifest(File.ReadAllText(acf)); } catch (IOException) { continue; }
                if (app is null) continue;
                var dir = Path.Combine(apps, "common", app.InstallDir);
                if (!Directory.Exists(dir)) continue;
                yield return new(GameStores.Steam, app.AppId, app.Name, dir, LaunchUri: "steam://rungameid/" + app.AppId, CoverPath: SteamCover(steam, app.AppId), LastPlayed: app.LastPlayed);
            }
        }
    }
    // Steam keeps library art locally: older flat names or newer per-app folders (sometimes hashed subfolders).
    public static string? SteamCover(string steam, string appId)
    {
        var cache = Path.Combine(steam, "appcache", "librarycache");
        foreach (var c in new[] { Path.Combine(cache, appId + "_library_600x900.jpg"), Path.Combine(cache, appId, "library_600x900.jpg") })
            if (File.Exists(c)) return c;
        var folder = Path.Combine(cache, appId);
        if (Directory.Exists(folder))
        {
            // Portrait art first (old "library_600x900", newer "library_capsule"), then header and hero as fallbacks.
            foreach (var pattern in new[] { "library_600x900*.jpg", "library_capsule*.jpg", "library_header*.jpg", "header*.jpg", "library_hero.jpg" })
            {
                var found = Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories).FirstOrDefault();
                if (found is not null) return found;
            }
        }
        var header = Path.Combine(cache, appId + "_header.jpg");
        return File.Exists(header) ? header : null;
    }

    // --- Epic ---
    private IEnumerable<GameEntry> Epic()
    {
        var dir = epicManifests ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(dir)) yield break;
        foreach (var f in Directory.EnumerateFiles(dir, "*.item"))
        {
            EpicItem? item;
            try { item = GameParsers.EpicManifest(File.ReadAllText(f)); } catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { continue; }
            if (item is null || !Directory.Exists(item.InstallLocation)) continue;
            var exe = item.LaunchExecutable is { Length: > 0 } e ? Path.Combine(item.InstallLocation, e) : null;
            yield return new(GameStores.Epic, item.AppName, item.Name, item.InstallLocation, exe is not null && File.Exists(exe) ? exe : null, item.LaunchUri);
        }
    }

    // --- Xbox / Game Pass ---
    private IEnumerable<string> XboxRoots()
    {
        if (xboxRoots is not null) { foreach (var r in xboxRoots) yield return r; yield break; }
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            var marker = Path.Combine(drive.RootDirectory.FullName, ".GamingRoot");
            var folders = File.Exists(marker) ? GameParsers.GamingRootFolders(File.ReadAllBytes(marker)) : [];
            if (folders.Count == 0) folders = ["XboxGames"];
            foreach (var f in folders) { var p = Path.Combine(drive.RootDirectory.FullName, f); if (Directory.Exists(p)) yield return p; }
        }
    }
    private IEnumerable<GameEntry> Xbox()
    {
        foreach (var root in XboxRoots())
        foreach (var gameDir in Directory.EnumerateDirectories(root))
        {
            var content = Path.Combine(gameDir, "Content");
            var config = Path.Combine(content, "MicrosoftGame.config");
            if (!File.Exists(config)) continue;
            XboxGame? g;
            try { g = GameParsers.MicrosoftGameConfig(File.ReadAllText(config)); } catch (Exception ex) when (ex is IOException or System.Xml.XmlException) { continue; }
            if (g is null) continue;
            var name = g.Name == g.IdentityName ? Path.GetFileName(gameDir) : g.Name;
            var exe = g.Executable is { Length: > 0 } x ? Path.Combine(content, x) : null;
            var logo = g.Logo is { Length: > 0 } l ? Path.Combine(content, l) : null;
            var uri = g.PackageFamilyName is not null && g.AppId is not null ? $"shell:AppsFolder\\{g.PackageFamilyName}!{g.AppId}" : null;
            yield return new(GameStores.Xbox, g.IdentityName, name, gameDir, exe is not null && File.Exists(exe) ? exe : null, uri, CoverPath: logo is not null && File.Exists(logo) ? logo : null);
        }
    }

    // --- GOG ---
    private static IEnumerable<GameEntry> Gog()
    {
        using var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
        if (root is null) yield break;
        foreach (var sub in root.GetSubKeyNames())
        {
            using var k = root.OpenSubKey(sub);
            if (k?.GetValue("gameName") is not string name || k.GetValue("path") is not string path || !Directory.Exists(path)) continue;
            var exe = k.GetValue("exe") as string;
            yield return new(GameStores.Gog, sub, name, path, exe is not null && File.Exists(exe) ? exe : null, Args: k.GetValue("launchParam") as string);
        }
    }

    // --- EA app: games installed through it ship __Installer\installerdata.xml ---
    private static IEnumerable<GameEntry> Ea()
    {
        foreach (var (key, view) in UninstallEntries())
        {
            if ((key.GetValue("Publisher") as string)?.Contains("Electronic Arts", StringComparison.OrdinalIgnoreCase) != true) continue;
            if ((key.GetValue("InstallLocation") as string)?.Trim('"') is not { Length: > 0 } dir || !Directory.Exists(dir)) continue;
            var data = Path.Combine(dir, "__Installer", "installerdata.xml");
            if (!File.Exists(data)) continue;
            EaGame? g;
            try { g = GameParsers.EaInstallerData(File.ReadAllText(data)); } catch (Exception ex) when (ex is IOException or System.Xml.XmlException) { continue; }
            var name = g?.Name ?? key.GetValue("DisplayName") as string;
            if (name is null) continue;
            var uri = g is { ContentIds.Count: > 0 } ? "origin2://game/launch?offerIds=" + string.Join(",", g.ContentIds) : null;
            yield return new(GameStores.Ea, g?.ContentIds.FirstOrDefault() ?? view, name, dir, LaunchUri: uri);
        }
    }

    // --- Ubisoft Connect ---
    private static IEnumerable<GameEntry> Ubisoft()
    {
        using var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
        if (root is null) yield break;
        foreach (var sub in root.GetSubKeyNames())
        {
            using var k = root.OpenSubKey(sub);
            var dir = (k?.GetValue("InstallDir") as string)?.Replace('/', '\\').TrimEnd('\\');
            if (dir is null || !Directory.Exists(dir)) continue;
            using var uninstall = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Uplay Install " + sub);
            var name = uninstall?.GetValue("DisplayName") as string ?? Path.GetFileName(dir);
            yield return new(GameStores.Ubisoft, sub, name, dir, LaunchUri: $"uplay://launch/{sub}/0");
        }
    }

    private static IEnumerable<(RegistryKey Key, string Name)> UninstallEntries()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var root = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (root is null) continue;
            foreach (var name in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(name);
                if (k is not null) yield return (k, name);
            }
        }
    }
}
