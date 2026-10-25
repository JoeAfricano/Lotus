using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Lotus.Infrastructure.Setup;

// Per-user install, no administrator: %LOCALAPPDATA%\Programs\Lotus, Start menu and desktop shortcuts, and an entry in
// Windows' "Installed apps" whose Uninstall runs "Lotus.App.exe --uninstall-self". The player's data (%LOCALAPPDATA%\Lotus)
// is kept on uninstall unless they choose to remove it.
[SupportedOSPlatform("windows")]
public sealed class LotusSetup(string? installDir = null, string? startMenuDir = null, string? desktopDir = null, string uninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Lotus")
{
    public const string UninstallArgument = "--uninstall-self";
    public string InstallDir { get; } = installDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Lotus");
    private string StartMenu => startMenuDir ?? Environment.GetFolderPath(Environment.SpecialFolder.Programs);
    private string Desktop => desktopDir ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    private static string Here => Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\');
    public bool IsInstalledCopy => string.Equals(Here, Path.GetFullPath(InstallDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    public bool IsInstalled => File.Exists(Path.Combine(InstallDir, "Lotus.App.exe")) && Registry.CurrentUser.OpenSubKey(uninstallKey) is not null;
    public string InstalledExe => Path.Combine(InstallDir, "Lotus.App.exe");

    // Copies this build (the folder Lotus runs from) into the install folder, then creates shortcuts and the Windows entry.
    public void Install(string? sourceDir = null, IProgress<double>? progress = null)
    {
        var source = Path.GetFullPath(sourceDir ?? Here).TrimEnd('\\');
        if (string.Equals(source, Path.GetFullPath(InstallDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Already running the installed copy.");
        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToList();
        Directory.CreateDirectory(InstallDir);
        for (var i = 0; i < files.Count; i++)
        {
            var target = Path.Combine(InstallDir, Path.GetRelativePath(source, files[i]));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(files[i], target, overwrite: true);
            progress?.Report((i + 1) * 90.0 / Math.Max(1, files.Count));
        }
        var exe = InstalledExe;
        Shortcut(Path.Combine(StartMenu, "Lotus.lnk"), exe);
        Shortcut(Path.Combine(Desktop, "Lotus.lnk"), exe);
        using (var k = Registry.CurrentUser.CreateSubKey(uninstallKey))
        {
            k.SetValue("DisplayName", "Lotus"); k.SetValue("Publisher", "Lotus");
            k.SetValue("DisplayVersion", typeof(LotusSetup).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
            k.SetValue("DisplayIcon", exe); k.SetValue("InstallLocation", InstallDir);
            k.SetValue("UninstallString", $"\"{exe}\" {UninstallArgument}");
            k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            k.SetValue("EstimatedSize", (int)(files.Sum(f => new FileInfo(f).Length) / 1024), RegistryValueKind.DWord);
            k.SetValue("NoModify", 1, RegistryValueKind.DWord); k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        progress?.Report(100);
    }

    // Removes shortcuts and the Windows entry; the program folder is deleted right after this process exits.
    public void Uninstall(bool removeData)
    {
        foreach (var lnk in new[] { Path.Combine(StartMenu, "Lotus.lnk"), Path.Combine(Desktop, "Lotus.lnk") }) if (File.Exists(lnk)) File.Delete(lnk);
        Registry.CurrentUser.DeleteSubKeyTree(uninstallKey, throwOnMissingSubKey: false);
        var folders = new List<string> { InstallDir };
        if (removeData) folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus"));
        // A short-lived cmd waits for Lotus to close, then removes the folders (only these fixed paths).
        var script = "ping 127.0.0.1 -n 3 > nul & " + string.Join(" & ", folders.Select(f => $"rmdir /s /q \"{f}\""));
        Process.Start(new ProcessStartInfo("cmd.exe", "/c " + script) { CreateNoWindow = true, UseShellExecute = false })?.Dispose();
    }

    private static void Shortcut(string path, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Shell unavailable.");
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic link = shell.CreateShortcut(path);
        link.TargetPath = target; link.WorkingDirectory = Path.GetDirectoryName(target); link.IconLocation = target + ",0"; link.Description = "Lotus";
        link.Save();
    }
}
