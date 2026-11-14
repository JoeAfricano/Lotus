using System.IO.MemoryMappedFiles;
using System.Text;
using Lotus.Core;
using Lotus.Core.Games;
using Lotus.Core.Integrations;
using Lotus.Infrastructure.Games;
using Lotus.Infrastructure.Integrations;
using Xunit;

namespace Lotus.Tests;

// RivaTuner around playing: open state, opening before the game, its Global limit, the game's 3D API, the OSD lock and
// the "held at the cap" diagnosis. Everything runs against private shared-memory names and temp folders.
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class RtssPlayTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "LotusRtssPlay-" + Guid.NewGuid());
    private readonly string map = "LotusRtssPlay_" + Guid.NewGuid().ToString("N");
    private string Profiles => Path.Combine(root, "Profiles");
    public RtssPlayTests() => Directory.CreateDirectory(Profiles);
    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    private static MemoryMappedFile FakeApps(string name, uint flags)
    {
        const int entry = 300, offset = 256;
        var m = MemoryMappedFile.CreateNew(name, offset + entry);
        using var v = m.CreateViewAccessor();
        v.Write(0, 0x52545353u); v.Write(4, 0x00020015u); v.Write(8, (uint)entry); v.Write(12, (uint)offset); v.Write(16, 1u);
        v.Write(offset, 4242); var exe = Encoding.ASCII.GetBytes(@"C:\Games\Cyber\game.exe" + "\0"); v.WriteArray(offset + 4, exe, 0, exe.Length);
        v.Write(offset + 264, flags); v.Write(offset + 268, 1000u); v.Write(offset + 272, 2000u);
        return m;
    }

    [Fact] public async Task OpenStateComesFromSharedMemoryAndStartNeedsTheProgram()
    {
        var rtss = new WindowsRtssIntegration(Profiles, Path.Combine(root, "Backups"), sharedMemory: map);
        Assert.False(rtss.Running());
        Assert.False(await rtss.StartAsync()); // no RTSS.exe in the temp install folder: nothing is started
        using (FakeApps(map, 0x0001_0008))
        {
            Assert.True(rtss.Running());
            Assert.True(await rtss.StartAsync()); // already open: nothing to do
        }
        Assert.False(rtss.Running());
    }

    [Theory]
    [InlineData(0x0001_0008u, "Direct3D 12")]
    [InlineData(0x0001_0007u, "Direct3D 11")]
    [InlineData(0x0001_000Au, "Vulkan")]
    [InlineData(0x0000_0001u, "OpenGL")]
    [InlineData(0u, null)]
    public void GameApiComesFromTheAppFlags(uint flags, string? api)
    {
        using var m = FakeApps(map, flags);
        Assert.Equal(api, new WindowsRtssIntegration(Profiles, sharedMemory: map).GameApi("game.exe"));
        Assert.Null(RtssSharedMemory.ApiOf("other.exe", map));
    }

    [Theory]
    [InlineData("[Framerate]\r\nLimit=75\r\nLimitDenominator=1", 75.0)]
    [InlineData("[Framerate]\r\nLimit=3750\r\nLimitDenominator=100", 37.5)]
    [InlineData("[Framerate]\r\nLimit=0", null)]
    [InlineData("[OSD]\r\nEnableOSD=1", null)]
    public void GlobalCapIsReadOnly(string global, double? cap)
    {
        var file = Path.Combine(Profiles, "Global"); File.WriteAllText(file, global);
        Assert.Equal(cap, new WindowsRtssIntegration(Profiles).GlobalCap());
        Assert.Equal(global, File.ReadAllText(file));
    }

    [Fact] public void InstallFolderMustHoldRtss()
    {
        Assert.False(RtssInstall.Valid("relative\\folder"));
        Assert.False(RtssInstall.Valid(root)); // no RTSS.exe
        File.WriteAllText(Path.Combine(root, "RTSS.exe"), "");
        Assert.True(RtssInstall.Valid(root));
        Assert.False(string.IsNullOrWhiteSpace(RtssInstall.Dir));
    }

    [Fact] public void OsdWaitsForTheWritersLock()
    {
        const uint entry = 512 + 4096, slots = 4, osdOffset = 64;
        using var m = MemoryMappedFile.CreateNew(map, osdOffset + entry * slots);
        using var v = m.CreateViewAccessor();
        v.Write(0, 0x52545353u); v.Write(4, 0x00020015u); v.Write(20, entry); v.Write(24, osdOffset); v.Write(28, slots);
        v.Write(36, 1); // another client is writing (dwBusy bit 0)
        var osd = new RtssOsd(map);
        Assert.False(osd.Show("x"));
        Assert.Equal(0u, v.ReadUInt32(32)); // nothing written while locked
        v.Write(36, 0);
        Assert.True(osd.Show("Lotus"));
        Assert.Equal(0, v.ReadInt32(36)); // lock released
        Assert.Equal(1u, v.ReadUInt32(32));
        Assert.True(osd.Clear());
    }

    [Fact] public void FpsHeldAtTheCapIsTheLimiter()
    {
        StutterInputs At(double avg, double? cap) => new(avg, avg * 0.9, 0, 30, 95, 144, null, 0, 40, [], true, FrameCap: cap);
        var capped = StutterDiagnosis.Analyze(At(74.6, 75));
        Assert.Equal("WhyCapped", capped[0].Key); Assert.Equal("75", capped[0].Detail);
        Assert.DoesNotContain(capped, f => f.Key == "WhyGpuLimit");
        Assert.Contains(StutterDiagnosis.Analyze(At(50, 75)), f => f.Key == "WhyGpuLimit"); // well below the cap: the GPU
        Assert.DoesNotContain(StutterDiagnosis.Analyze(At(74.6, null)), f => f.Key == "WhyCapped"); // no limiter known
    }

    [Fact] public void LimitAboveTheRealRefreshIsACause()
    {
        StutterInputs At(double? cap, bool vrr = false) => new(74.6, 70, 0, 30, 80, 75, null, 0, 40, [], true, FrameCap: cap, RealRefreshHz: 74.973, AdaptiveSync: vrr);
        var above = StutterDiagnosis.Analyze(At(75));
        Assert.Contains(above, f => f.Key == "WhyCapAboveRefresh" && f.Detail == "75 FPS / 74.973 Hz");
        Assert.DoesNotContain(StutterDiagnosis.Analyze(At(74.97)), f => f.Key == "WhyCapAboveRefresh");   // just under: right
        Assert.DoesNotContain(StutterDiagnosis.Analyze(At(75, vrr: true)), f => f.Key == "WhyCapAboveRefresh"); // VRR: no V-Sync queue
        Assert.DoesNotContain(StutterDiagnosis.Analyze(At(null)), f => f.Key == "WhyCapAboveRefresh");
    }

    [Theory]
    [InlineData(true, true, null, true)]           // frame generation on NVIDIA: Reflex first
    [InlineData(true, true, "Direct3D 12", true)]
    [InlineData(true, true, "Vulkan", false)]      // Reflex does not apply to Vulkan
    [InlineData(true, false, null, false)]         // AMD/Intel: no Reflex
    [InlineData(false, true, null, false)]         // no frame generation: Fluid stays first
    public void ReflexIsPreferredOnlyWhereItWorks(bool frameGen, bool nvidia, string? api, bool prefer)
    {
        var info = new GamesViewModel.LowLatencyInfo("ok", "game.exe", null, nvidia, FrameGen: frameGen, Api: api);
        Assert.Equal(prefer, info.PreferResponsive);
    }

    private sealed class NoLibrary : IGameLibrary { public LibraryScan Scan(IReadOnlyList<GameEntry> manual) => new([], []); }
    private sealed class NoStore : IManualGameStore { public IReadOnlyList<GameEntry> Load() => []; public void Save(IReadOnlyList<GameEntry> g) { } }
    private sealed class NoLauncher : IGameLauncher { public void Launch(GameEntry game) { } }
    private sealed class Rtss(bool installed, bool? running, bool overlay, string? limiterFor = null) : IRtssIntegration
    {
        public int Starts;
        public ToolState State() => new(installed, "7.3.7");
        public bool? OverlayConfigured() => overlay; public bool HasBackup() => false;
        public Task<bool> ApplyLotusOverlayAsync() => Task.FromResult(true); public Task<bool> RestoreAsync() => Task.FromResult(true);
        public string ExportLayout(string variant = "top") => ""; public bool LayoutActive() => false; public string? ActiveLayoutName() => null; public bool HasLayoutBackup() => false;
        public Task<bool> ActivateLayoutAsync(string variant = "top", bool restart = false) => Task.FromResult(true); public Task<bool> RestoreLayoutAsync() => Task.FromResult(true);
        public string? LimiterMode(string exe) => exe == limiterFor ? "fluid" : null;
        public bool? Running() => running;
        public Task<bool> StartAsync() { Starts++; return Task.FromResult(false); }
    }

    [Fact] public async Task RivaTunerOpensBeforeTheGameOnlyWhenLotusUsesIt()
    {
        var text = new TextCatalog(); var game = new GameEntry("Manual", "1", "Jogo", null, @"C:\Games\game.exe");
        GamesViewModel Vm(Rtss r) => new(new NoLibrary(), new NoStore(), new NoLauncher(), text, _ => { }) { Rtss = r };
        var closedOverlay = new Rtss(true, false, true); var vm = Vm(closedOverlay); string? warning = null; vm.RtssWarning += (_, m) => warning = m;
        Assert.False(await vm.EnsureRtssAsync(game)); Assert.Equal(1, closedOverlay.Starts); Assert.Equal(text["RtssNotOpened"], warning); // declined: warned, the game still opens
        var closedLimiter = new Rtss(true, false, false, "game.exe");
        Assert.False(await Vm(closedLimiter).EnsureRtssAsync(game)); Assert.Equal(1, closedLimiter.Starts);
        foreach (var idle in new[] { new Rtss(true, true, true), new Rtss(false, false, true), new Rtss(true, false, false), new Rtss(true, null, true) })
        {
            Assert.Null(await Vm(idle).EnsureRtssAsync(game)); Assert.Equal(0, idle.Starts); // open, not installed, not in use, unknown
        }
    }
}
