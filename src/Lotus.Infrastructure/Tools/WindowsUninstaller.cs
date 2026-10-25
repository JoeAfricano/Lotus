using System.Diagnostics;
using System.Runtime.Versioning;
using Lotus.Core.Tools;
using Microsoft.Win32;

namespace Lotus.Infrastructure.Tools;

// Installed programs from the same registry keys Windows' "Installed apps" uses (machine 64/32-bit and the user).
// Removal always goes through the program's registered uninstaller; leftovers are only proposed, never removed silently.
[SupportedOSPlatform("windows")]
public sealed class WindowsUninstaller : IAppUninstaller
{
    private const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    // Folders that are never offered as leftovers, whatever their name.
    private static readonly HashSet<string> Never = new(StringComparer.OrdinalIgnoreCase)
    { "Microsoft", "Windows", "Common Files", "Intel", "AMD", "NVIDIA", "NVIDIA Corporation", "Packages", "Temp", "Programs", "Steam", "Lotus", "Google", "Mozilla", "Microsoft Office" };

    public IReadOnlyList<InstalledApp> List()
    {
        var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, view, user) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64, false), (RegistryHive.LocalMachine, RegistryView.Registry32, false), (RegistryHive.CurrentUser, RegistryView.Default, true) })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(Uninstall);
            if (root is null) continue;
            foreach (var id in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(id);
                if (k?.GetValue("DisplayName") is not string name || name.Trim().Length == 0) continue;
                if (k.GetValue("UninstallString") is not string command || command.Trim().Length == 0) continue;
                if (k.GetValue("SystemComponent") is int sc && sc == 1) continue;
                if (k.GetValue("ParentKeyName") is string || k.GetValue("ReleaseType") is string rt && rt.Contains("Update", StringComparison.OrdinalIgnoreCase)) continue;
                long? size = k.GetValue("EstimatedSize") is int kb && kb > 0 ? kb * 1024L : null;
                DateTime? installed = k.GetValue("InstallDate") is string d && DateTime.TryParseExact(d, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var dt) ? dt : null;
                var location = (k.GetValue("InstallLocation") as string)?.Trim().Trim('"');
                var app = new InstalledApp($"{hive}:{view}:{id}", name.Trim(), (k.GetValue("Publisher") as string)?.Trim(), (k.GetValue("DisplayVersion") as string)?.Trim(),
                    size, installed, string.IsNullOrWhiteSpace(location) ? null : location, command.Trim(), user, (k.GetValue("DisplayIcon") as string)?.Trim());
                apps.TryAdd(app.Name + "|" + app.Version, app); // the same program listed in two views counts once
            }
        }
        return [.. apps.Values];
    }

    // Splits "C:\x\uninst.exe" /S or MsiExec.exe /X{GUID} into file and arguments; started through the shell so the
    // uninstaller can ask for its own administrator permission.
    public static (string File, string Args) Split(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"')) { var end = command.IndexOf('"', 1); return end > 0 ? (command[1..end], command[(end + 1)..].Trim()) : (command.Trim('"'), ""); }
        var exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? (command[..(exe + 4)], command[(exe + 4)..].Trim()) : (command, "");
    }

    public async Task<int?> RunUninstallerAsync(InstalledApp app, CancellationToken token)
    {
        var (file, args) = Split(app.UninstallCommand);
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true });
            if (p is null) return null;
            await p.WaitForExitAsync(token);
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception) { return null; } // not found, or the user declined the UAC prompt
    }

    // Some uninstallers hand over to a copy in %TEMP% and exit at once: wait until the entry really disappears.
    public async Task<bool> WaitUntilRemovedAsync(InstalledApp app, TimeSpan timeout, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!List().Any(a => a.Key == app.Key || (a.Name == app.Name && a.Version == app.Version))) return true;
            await Task.Delay(2000, token);
        }
        return false;
    }

    public IReadOnlyList<Leftover> FindLeftovers(InstalledApp app, IReadOnlyList<InstalledApp> stillInstalled)
    {
        var candidates = new List<(string Path, bool Recommended)>();
        if (app.InstallLocation is { } loc) candidates.Add((loc, true));
        var names = new[] { app.Name, StripVersion(app.Name) }.Where(n => n.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var roots = new[]
        {
            Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.CommonApplicationData,
            Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86
        }.Select(Environment.GetFolderPath).Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var root in roots)
            foreach (var n in names)
            {
                candidates.Add((Path.Combine(root, n), true));
                if (app.Publisher is { Length: >= 2 } pub && !Never.Contains(pub)) candidates.Add((Path.Combine(root, pub, n), true));
            }
        var keep = stillInstalled.Where(a => a.InstallLocation is not null).Select(a => Path.GetFullPath(a.InstallLocation!).TrimEnd('\\') + "\\").ToList();
        var result = new List<Leftover>();
        foreach (var (path, recommended) in candidates.DistinctBy(c => c.Path.TrimEnd('\\'), StringComparer.OrdinalIgnoreCase))
        {
            string full;
            try { full = Path.GetFullPath(path).TrimEnd('\\'); } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            if (!Directory.Exists(full) || Never.Contains(Path.GetFileName(full)) || roots.Any(r => string.Equals(r.TrimEnd('\\'), full, StringComparison.OrdinalIgnoreCase))) continue;
            if (Path.GetDirectoryName(full) is null || full.Count(c => c == '\\') < 2) continue; // never a drive root or top-level folder
            if (keep.Any(k => k.StartsWith(full + "\\", StringComparison.OrdinalIgnoreCase) || (full + "\\").StartsWith(k, StringComparison.OrdinalIgnoreCase))) continue; // belongs to another installed program
            result.Add(new(full, SizeOf(full), recommended, !CanWrite(full)));
        }
        return result;
    }

    public int Recycle(IReadOnlyList<Leftover> items, IProgress<double>? progress = null)
    {
        var moved = 0;
        for (var i = 0; i < items.Count; i++)
        {
            if (!items[i].NeedsAdmin && RecycleBin.Send(items[i].Path)) moved++;
            progress?.Report((i + 1) * 100.0 / items.Count);
        }
        return moved;
    }

    // Icon from DisplayIcon ("file.exe,0" or an .ico), else the uninstaller's own file; cached as PNG in %LOCALAPPDATA%\Lotus\Icons.
    public string? IconFor(InstalledApp app)
    {
        var source = app.IconSource ?? Split(app.UninstallCommand).File;
        if (string.IsNullOrWhiteSpace(source)) return null;
        var index = 0; var comma = source.LastIndexOf(',');
        if (comma > 1 && int.TryParse(source[(comma + 1)..].Trim(), out var i)) { index = i; source = source[..comma]; }
        source = Environment.ExpandEnvironmentVariables(source.Trim().Trim('"'));
        if (!File.Exists(source) || source.EndsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase)) return null;
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "Icons");
        var target = Path.Combine(cache, Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(source + "|" + index))) + ".png");
        if (File.Exists(target)) return target;
        try
        {
            using var icon = source.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ? new System.Drawing.Icon(source, 48, 48)
                : System.Drawing.Icon.ExtractIcon(source, index, 48) ?? System.Drawing.Icon.ExtractAssociatedIcon(source);
            if (icon is null) return null;
            Directory.CreateDirectory(cache);
            using var bitmap = icon.ToBitmap();
            bitmap.Save(target, System.Drawing.Imaging.ImageFormat.Png);
            return target;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or System.Runtime.InteropServices.ExternalException or UnauthorizedAccessException) { return null; }
    }

    private static string StripVersion(string name) => System.Text.RegularExpressions.Regex.Replace(name, @"[\s\-_]*(v?\d+(\.\d+)*)(\s*\(.*\))?\s*$", "").Trim();
    private static long SizeOf(string dir)
    {
        try { return new DirectoryInfo(dir).EnumerateFiles("*", new System.IO.EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).Sum(f => f.Length); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }
    private static bool CanWrite(string dir)
    {
        try { var probe = Path.Combine(dir, ".lotus-write-test"); File.WriteAllText(probe, ""); File.Delete(probe); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
