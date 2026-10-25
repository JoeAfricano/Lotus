using Lotus.Core;
using Lotus.Core.Tools;
using Lotus.Infrastructure.Tools;
using Xunit;

namespace Lotus.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class UninstallerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "LotusUninstall-" + Guid.NewGuid());
    public UninstallerTests() => Directory.CreateDirectory(root);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Theory]
    [InlineData("\"C:\\Program Files\\App\\unins000.exe\" /SILENT", "C:\\Program Files\\App\\unins000.exe", "/SILENT")]
    [InlineData("MsiExec.exe /X{1234-ABCD}", "MsiExec.exe", "/X{1234-ABCD}")]
    [InlineData("C:\\Games\\Tool\\uninstall.exe", "C:\\Games\\Tool\\uninstall.exe", "")]
    public void SplitsUninstallCommands(string command, string file, string args) => Assert.Equal((file, args), WindowsUninstaller.Split(command));

    private InstalledApp App(string name, string? location) => new("k:" + name, name, "Pub", "1.0", null, null, location, "x.exe", true);

    [Fact] public void LeftoversAreTheInstallFolderButNeverAnotherProgramsFolder()
    {
        var gone = Path.Combine(root, "OldGame"); Directory.CreateDirectory(Path.Combine(gone, "data")); File.WriteAllText(Path.Combine(gone, "data", "save.bin"), "x");
        var shared = Path.Combine(root, "Shared"); var other = Path.Combine(shared, "Other"); Directory.CreateDirectory(other);
        var u = new WindowsUninstaller();
        var found = u.FindLeftovers(App("OldGame", gone), [App("Other", other)]);
        var l = Assert.Single(found, x => x.Path == gone);
        Assert.True(l.Recommended); Assert.False(l.NeedsAdmin); Assert.True(l.Bytes > 0);
        Assert.Empty(u.FindLeftovers(App("Shared", shared), [App("Other", other)])); // contains an installed program: never offered
        Assert.Equal(1, u.Recycle([l])); Assert.False(Directory.Exists(gone));
    }

    private sealed class Fake : IAppUninstaller
    {
        public List<InstalledApp> Installed = [new("a", "Old Game", "Pub", "1", 1L << 30, null, @"C:\Games\Old", "u.exe", true), new("b", "Keep", null, null, null, null, null, "k.exe", true)];
        public bool Declined; public int Recycled;
        public IReadOnlyList<InstalledApp> List() => Installed;
        public Task<int?> RunUninstallerAsync(InstalledApp app, CancellationToken t) { if (Declined) return Task.FromResult<int?>(null); Installed.RemoveAll(a => a.Key == app.Key); return Task.FromResult<int?>(0); }
        public Task<bool> WaitUntilRemovedAsync(InstalledApp app, TimeSpan timeout, CancellationToken t) => Task.FromResult(Installed.All(a => a.Key != app.Key));
        public IReadOnlyList<Leftover> FindLeftovers(InstalledApp app, IReadOnlyList<InstalledApp> still) => [new(@"C:\Games\Old", 10, true, false), new(@"C:\Program Files\Old", 10, true, true)];
        public int Recycle(IReadOnlyList<Leftover> items, IProgress<double>? progress = null) { Recycled += items.Count; return items.Count; }
    }

    [Fact] public async Task UninstallThenReviewLeftoversAndRecycleOnlyChecked()
    {
        var fake = new Fake(); var vm = new UninstallerViewModel(fake, new TextCatalog(), ex => throw ex);
        await vm.LoadAsync(); Assert.Equal(2, vm.Apps.Count);
        vm.Query = "old"; Assert.Equal("Old Game", Assert.Single(vm.Apps).App.Name); Assert.Contains("1.0 GB", vm.Apps[0].Detail.Replace(',', '.'));
        await vm.UninstallAsync(vm.Apps[0].App);
        Assert.True(vm.Job.Done); Assert.Equal(2, vm.Leftovers.Count);
        Assert.True(vm.Leftovers[0].Selected); Assert.False(vm.Leftovers[1].Selected); Assert.False(vm.Leftovers[1].CanRecycle); // admin-only stays out
        await vm.RecycleLeftoversAsync();
        Assert.Equal(1, fake.Recycled); Assert.Single(vm.Leftovers); Assert.True(vm.Job.Done);
        var declined = new Fake { Declined = true }; var vm2 = new UninstallerViewModel(declined, new TextCatalog(), ex => throw ex);
        await vm2.LoadAsync(); await vm2.UninstallAsync(declined.Installed[0]);
        Assert.False(vm2.Job.Done); Assert.Empty(vm2.Leftovers); Assert.Equal(2, declined.Installed.Count);
    }

    [Fact] public void RealListIsReadOnlyAndHasNames() => Assert.All(new WindowsUninstaller().List(), a => Assert.False(string.IsNullOrWhiteSpace(a.Name)));
}
