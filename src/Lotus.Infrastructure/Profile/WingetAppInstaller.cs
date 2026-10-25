using System.Diagnostics;
using System.Runtime.Versioning;
using Lotus.Core.Profile;
using Microsoft.Win32;

namespace Lotus.Infrastructure.Profile;

// Detects programs by the "Installed apps" registry entries and installs through winget (official Windows package
// manager). Lotus stays asInvoker: an installer that needs administrator rights asks for them itself.
[SupportedOSPlatform("windows")]
public sealed class WingetAppInstaller : IAppInstaller
{
    private const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string WingetPath => Path.Combine(Local, "Microsoft", "WindowsApps", "winget.exe");

    public bool WingetAvailable() => File.Exists(WingetPath);

    public bool IsInstalled(RecommendedApp app)
    {
        if (app.Id == "scskiller" && File.Exists(Path.Combine(Local, "SCSKiller.App", "SCSKiller.exe"))) return true;
        foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(Uninstall);
            if (root is null) continue;
            foreach (var id in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(id);
                if (k?.GetValue("DisplayName") is string name && app.DetectNames.Any(n => name.StartsWith(n, StringComparison.OrdinalIgnoreCase))) return true;
            }
        }
        return false;
    }

    public async Task<bool> InstallAsync(RecommendedApp app, CancellationToken token)
    {
        if (app.WingetId is null || !WingetAvailable()) return false;
        var info = new ProcessStartInfo(WingetPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "install", "--id", app.WingetId, "--exact", "--source", "winget", "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity" })
            info.ArgumentList.Add(a);
        using var p = Process.Start(info) ?? throw new InvalidOperationException("winget did not start");
        _ = p.StandardOutput.ReadToEndAsync(token); _ = p.StandardError.ReadToEndAsync(token);
        await p.WaitForExitAsync(token);
        Serilog.Log.Information("winget install {Id} exit {Code}", app.WingetId, p.ExitCode);
        return IsInstalled(app);
    }
}
