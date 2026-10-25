using Lotus.Infrastructure.Setup;
using Microsoft.Win32;
using Xunit;

namespace Lotus.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class SetupTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "LotusSetup-" + Guid.NewGuid());
    private readonly string key = @"Software\LotusTests\Uninstall-" + Guid.NewGuid().ToString("N");
    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
        Thread.Sleep(3500); // the uninstall cleanup waits ~2 s before removing the folder
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    [Fact] public async Task InstallsShortcutsAndWindowsEntryThenUninstalls()
    {
        var source = Path.Combine(root, "build"); Directory.CreateDirectory(Path.Combine(source, "Assets"));
        File.WriteAllText(Path.Combine(source, "Lotus.App.exe"), "exe"); File.WriteAllText(Path.Combine(source, "Assets", "a.png"), "png");
        var setup = new LotusSetup(Path.Combine(root, "Programs", "Lotus"), Path.Combine(root, "Start"), Path.Combine(root, "Desktop"), key);
        Assert.False(setup.IsInstalled);
        setup.Install(source);
        Assert.True(setup.IsInstalled);
        Assert.True(File.Exists(Path.Combine(setup.InstallDir, "Assets", "a.png")));
        Assert.True(File.Exists(Path.Combine(root, "Start", "Lotus.lnk"))); Assert.True(File.Exists(Path.Combine(root, "Desktop", "Lotus.lnk")));
        using (var k = Registry.CurrentUser.OpenSubKey(key)!)
        {
            Assert.Equal("Lotus", k.GetValue("DisplayName"));
            Assert.EndsWith(LotusSetup.UninstallArgument, (string)k.GetValue("UninstallString")!);
        }
        setup.Uninstall(removeData: false);
        Assert.False(setup.IsInstalled); Assert.False(File.Exists(Path.Combine(root, "Start", "Lotus.lnk")));
        for (var i = 0; i < 40 && Directory.Exists(setup.InstallDir); i++) await Task.Delay(200);
        Assert.False(Directory.Exists(setup.InstallDir));
    }
}
