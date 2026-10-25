using System.Text.Json;
using Lotus.Core;
using Lotus.Core.Integrations;
using Lotus.Infrastructure.Games;
using Lotus.Infrastructure.Integrations;
using Xunit;

namespace Lotus.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class IntegrationsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "LotusIntegrations-" + Guid.NewGuid());
    private string Profiles => Path.Combine(root, "Profiles");
    private string Backups => Path.Combine(root, "Backups");
    private const string Global = "[OSD]\r\nEnableOSD=0\r\nBaseColor=FF00FF00\r\n\r\n[Framerate]\r\nLimit=75\r\n\r\n[Hooking]\r\nEnableHooking=1";

    public IntegrationsTests() { Directory.CreateDirectory(Profiles); File.WriteAllText(Path.Combine(Profiles, "Global"), Global); }

    [Theory]
    [InlineData("Global", "OSD", "EnableOSD", "1", true)]
    [InlineData("game.exe", "Framerate", "Limit", "75", true)]
    [InlineData("Global", "Hooking", "EnableHooking", "0", false)]      // section not allowed
    [InlineData("Global", "OSD", "SomethingElse", "1", false)]          // key not allowed
    [InlineData("..\\Config", "OSD", "EnableOSD", "1", false)]           // path in profile name
    [InlineData("Global", "OSD", "EnableOSD", "1\r\n[Hooking]", false)] // value injection
    public void AllowListIsStrict(string profile, string section, string key, string value, bool ok)
    {
        var request = new RtssRequest(profile, [new(section, key, value)]);
        if (ok) RtssProfiles.Validate(request); else Assert.Throws<InvalidOperationException>(() => RtssProfiles.Validate(request));
    }

    [Fact] public void ApplyKeepsOtherLinesBacksUpOnceAndRestoresExactly()
    {
        RtssProfiles.Apply(new("Global", [new("OSD", "EnableOSD", "1")]), Profiles, Backups);
        RtssProfiles.Apply(new("Global", [new("OSD", "BaseColor", "FFEEC1A8")]), Profiles, Backups);
        var ini = new IniText(File.ReadAllText(Path.Combine(Profiles, "Global")));
        Assert.Equal("1", ini.Get("OSD", "EnableOSD")); Assert.Equal("FFEEC1A8", ini.Get("OSD", "BaseColor")); Assert.Equal("1", ini.Get("Hooking", "EnableHooking"));
        Assert.Equal(Global, File.ReadAllText(Path.Combine(Backups, "Global.original"))); // first original only
        Assert.True(RtssProfiles.Restore("Global", Profiles, Backups));
        Assert.Equal(Global, File.ReadAllText(Path.Combine(Profiles, "Global"))); Assert.False(RtssProfiles.Restore("Global", Profiles, Backups));
        RtssProfiles.Apply(new("game.exe", [new("Framerate", "Limit", "60")]), Profiles, Backups);
        Assert.True(RtssProfiles.Restore("game.exe", Profiles, Backups)); Assert.False(File.Exists(Path.Combine(Profiles, "game.exe"))); // new profile removed on restore
    }

    // Simulates the elevated helper in-process against the temp folders (tests never request admin rights).
    private Task<int> FakeElevated(string[] args)
    {
        if (args[0] == "--rtss-apply") { RtssProfiles.Apply(JsonSerializer.Deserialize<RtssRequest>(File.ReadAllText(args[1]))!, Profiles, Backups); return Task.FromResult(0); }
        return Task.FromResult(RtssProfiles.Restore(args[1], Profiles, Backups) ? 0 : 4);
    }

    [Fact] public async Task LotusOverlayIsAppliedVerifiedAndRestored()
    {
        var rtss = new WindowsRtssIntegration(Profiles, Backups, FakeElevated);
        Assert.False(rtss.OverlayConfigured()); Assert.False(rtss.HasBackup());
        Assert.True(await rtss.ApplyLotusOverlayAsync());
        Assert.True(rtss.OverlayConfigured()); Assert.True(rtss.HasBackup());
        Assert.Equal("75", new IniText(File.ReadAllText(Path.Combine(Profiles, "Global"))).Get("Framerate", "Limit")); // limiter untouched
        Assert.True(await rtss.RestoreAsync()); Assert.Equal(Global, File.ReadAllText(Path.Combine(Profiles, "Global")));
        var declined = new WindowsRtssIntegration(Profiles, Backups, _ => Task.FromResult(1223));
        Assert.False(await declined.ApplyLotusOverlayAsync()); Assert.Equal(Global, File.ReadAllText(Path.Combine(Profiles, "Global")));
    }

    private sealed class FakeRtss(bool installed, bool? configured, bool backup) : IRtssIntegration
    {
        public ToolState State() => new(installed, installed ? "7.3.7" : null);
        public bool? OverlayConfigured() => configured; public bool HasBackup() => backup;
        public Task<bool> ApplyLotusOverlayAsync() => Task.FromResult(true); public Task<bool> RestoreAsync() => Task.FromResult(false);
    }
    private sealed class Tools(bool ab, bool scs) : IToolDetector
    {
        public ToolState Afterburner() => new(ab, ab ? "4.6.6" : null); public ToolState ScsKiller() => new(scs, scs ? "1.3.2" : null);
    }

    [Fact] public async Task ViewModelDescribesEachToolHonestly()
    {
        var text = new TextCatalog();
        var vm = new IntegrationsViewModel(new FakeRtss(true, false, true), new Tools(true, false), text, _ => { });
        vm.Refresh();
        Assert.Equal(text.Format("IntRtssDefault", "7.3.7"), vm.RtssStatus); Assert.True(vm.CanRestoreRtss);
        Assert.Equal(text.Format("IntAfterburnerOn", "4.6.6"), vm.AfterburnerStatus); Assert.Equal(text["IntScsOff"], vm.ScsStatus);
        await vm.ApplyOverlayAsync(); Assert.Equal(text["IntOverlayApplied"], vm.Message);
        await vm.RestoreOverlayAsync(); Assert.Equal(text["IntOverlayFailed"], vm.Message);
        var none = new IntegrationsViewModel(new FakeRtss(false, null, false), new Tools(false, true), text, _ => { });
        none.Refresh(); Assert.False(none.RtssInstalled); Assert.Equal(text["IntNotInstalled"], none.RtssStatus); Assert.False(none.CanRestoreRtss);
    }

    [Fact] public void RealRtssStateIsReadOnly()
    {
        var real = new WindowsRtssIntegration();
        var state = real.State();
        if (!state.Installed) return;
        Assert.False(string.IsNullOrWhiteSpace(state.Version));
        Assert.NotNull(real.OverlayConfigured()); // the installed Global profile must be readable without admin rights
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
