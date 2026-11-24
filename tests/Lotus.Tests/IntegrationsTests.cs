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
        Assert.True(File.Exists(Path.Combine(Profiles, "game.exe.cfg"))); // RTSS names game profiles "<game>.exe.cfg"
        Assert.True(RtssProfiles.Restore("game.exe", Profiles, Backups)); Assert.False(File.Exists(Path.Combine(Profiles, "game.exe.cfg"))); // new profile removed on restore
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

    [Fact] public async Task FrameLimiterWritesOnlyThatGamesProfileAndRestoresIt()
    {
        var rtss = new WindowsRtssIntegration(Profiles, Backups, FakeElevated);
        Assert.Null(rtss.LimiterMode("Cyberpunk2077.exe")); Assert.Null(rtss.LimiterMode("..\evil.exe"));
        Assert.True(await rtss.EnableLimiterAsync("Cyberpunk2077.exe", 75, "fluid"));
        var ini = new IniText(File.ReadAllText(Path.Combine(Profiles, "Cyberpunk2077.exe.cfg")));
        Assert.Equal("75", ini.Get("Framerate", "Limit")); Assert.Equal(WindowsRtssIntegration.FrontEdgeLimiter, ini.Get("Framerate", "SyncLimiter"));
        Assert.Equal(Global, File.ReadAllText(Path.Combine(Profiles, "Global")));            // Global untouched
        Assert.True(await rtss.EnableLimiterAsync("Cyberpunk2077.exe", 37.5, "responsive")); // switch mode, fractional cap
        ini = new IniText(File.ReadAllText(Path.Combine(Profiles, "Cyberpunk2077.exe.cfg")));
        Assert.Equal(("3750", "100", WindowsRtssIntegration.ReflexLimiter), (ini.Get("Framerate", "Limit"), ini.Get("Framerate", "LimitDenominator"), ini.Get("Framerate", "SyncLimiter")));
        Assert.Equal("responsive", rtss.LimiterMode("Cyberpunk2077.exe"));
        Assert.True(await rtss.EnableLimiterAsync("Cyberpunk2077.exe", 0, "scanline"));         // scanline sync: no cap, fixed scanline
        ini = new IniText(File.ReadAllText(Path.Combine(Profiles, "Cyberpunk2077.exe.cfg")));
        Assert.Equal(("0", "0", WindowsRtssIntegration.DefaultScanline), (ini.Get("Framerate", "Limit"), ini.Get("Framerate", "SyncLimiter"), ini.Get("Framerate", "SyncScanline0")));
        Assert.True(await rtss.EnableLimiterAsync("Cyberpunk2077.exe", 75, "fluid"));            // back to a limiter clears the scanline
        Assert.Equal(("fluid", "0"), (rtss.LimiterMode("Cyberpunk2077.exe"), new IniText(File.ReadAllText(Path.Combine(Profiles, "Cyberpunk2077.exe.cfg"))).Get("Framerate", "SyncScanline0")));
        Assert.True(await rtss.DisableLowLatencyAsync("Cyberpunk2077.exe"));
        Assert.False(File.Exists(Path.Combine(Profiles, "Cyberpunk2077.exe.cfg")));           // the profile did not exist before
        Assert.False(await rtss.EnableLimiterAsync("game.exe", 5, "fluid"));                     // nonsense cap refused
        Assert.False(await rtss.EnableLimiterAsync("game.exe", 60, "turbo"));                    // unknown mode refused
    }

    [Theory]
    [InlineData("async", "0")]
    [InlineData("fluid", "1")]
    [InlineData("back", "2")]
    [InlineData("responsive", "3")]
    public async Task AllLimiterModesRoundTripAndOriginalLimiterIsRestored(string mode,string setting)
    {
        const string original="[Framerate]\r\nLimit=61\r\nSyncLimiter=0\r\n; keep original\r\n";
        var path=Path.Combine(Profiles,"game.exe.cfg");File.WriteAllText(path,original);
        var rtss=new WindowsRtssIntegration(Profiles,Backups,FakeElevated);
        Assert.True(await rtss.EnableLimiterAsync("game.exe",59.93,mode));
        Assert.Equal(mode,rtss.LimiterMode("game.exe"));Assert.Equal(59.93,rtss.LimiterCap("game.exe"));
        Assert.Equal(setting,new IniText(File.ReadAllText(path)).Get("Framerate","SyncLimiter"));
        Assert.Equal(mode=="fluid"?"1":"0",new IniText(File.ReadAllText(path)).Get("Statistics","FrametimeCalc"));
        Assert.True(await rtss.DisableLowLatencyAsync("game.exe"));Assert.Equal(original,File.ReadAllText(path));
        Assert.Equal(Global,File.ReadAllText(Path.Combine(Profiles,"Global")));
    }
    [Theory]
    [InlineData("0",true)] [InlineData("1",true)] [InlineData("2",false)] [InlineData("FFFFFFFF",false)]
    public void FrametimePointAllowListAcceptsOnlyDocumentedChoices(string value,bool allowed)
    {
        var request=new RtssRequest("game.exe",[new("Statistics","FrametimeCalc",value)]);
        if(allowed) RtssProfiles.Validate(request);else Assert.Throws<InvalidOperationException>(()=>RtssProfiles.Validate(request));
    }
    private sealed class FakeRtss(bool installed, bool? configured, bool backup) : IRtssIntegration
    {
        public ToolState State() => new(installed, installed ? "7.3.7" : null);
        public bool? OverlayConfigured() => configured; public bool HasBackup() => backup;
        public Task<bool> ApplyLotusOverlayAsync() => Task.FromResult(true); public Task<bool> RestoreAsync() => Task.FromResult(false);
        public string ExportLayout(string variant = "top") => @"C:\x\Lotus.ovl"; public bool LayoutActive() => configured == true; public string? ActiveLayoutName() => "Riva Overlay"; public bool HasLayoutBackup() => backup;
        public Task<bool> ActivateLayoutAsync(string variant = "top", bool restart = false) => Task.FromResult(true); public Task<bool> RestoreLayoutAsync() => Task.FromResult(true);
    }
    // Records what the overlay style switch asked RivaTuner to do.
    private sealed class StyleRtss(bool running) : IRtssIntegration
    {
        public List<(string Variant, bool Restart)> Calls { get; } = [];
        public int Starts { get; private set; }
        public int Restores { get; private set; }
        public Task<bool> StartAsync(){Starts++;return Task.FromResult(true);}
        public ToolState State() => new(true, "7.3.5");
        public bool? OverlayConfigured() => true; public bool HasBackup() => false;
        public Task<bool> ApplyLotusOverlayAsync() => Task.FromResult(true); public Task<bool> RestoreAsync() => Task.FromResult(true);
        public string ExportLayout(string variant = "top") => @"C:\x\Lotus.ovl"; public bool LayoutActive() => Calls.Count > 0;
        public string? ActiveLayoutName() => Calls.Count == 0 ? null : Calls[^1].Variant switch { "neon" => "Lotus-Neon", "lateral" => "Lotus-Lateral", _ => "Lotus" };
        public bool HasLayoutBackup() => false;
        public Task<bool> ActivateLayoutAsync(string variant = "top", bool restart = false) { Calls.Add((variant, restart)); return Task.FromResult(true); }
        public Task<bool> RestoreLayoutAsync() { Restores++; return Task.FromResult(true); }
        public bool? Running() => running;
    }

    private sealed class FailingStyleRtss(string? reason) : IRtssIntegration
    {
        public ToolState State() => new(true, "7.3.7");
        public bool? OverlayConfigured() => true; public bool HasBackup() => false;
        public Task<bool> ApplyLotusOverlayAsync() => Task.FromResult(true); public Task<bool> RestoreAsync() => Task.FromResult(true);
        public string ExportLayout(string variant = "top") => @"C:\x\Lotus.ovl"; public bool LayoutActive() => false;
        public string? ActiveLayoutName() => null; public bool HasLayoutBackup() => false;
        public Task<bool> ActivateLayoutAsync(string variant = "top", bool restart = false) => Task.FromResult(false);
        public Task<bool> RestoreLayoutAsync() => Task.FromResult(true);
        public string? LastFailure => reason;
    }
    [Theory, InlineData("uac", "OsFailedUac"), InlineData("plugin", "OsFailedPlugin"), InlineData("other", "OsFailed")]
    public async Task OverlayStyle_failure_explains_the_reason(string reason, string key)
    {
        var text = new TextCatalog();
        var vm = new IntegrationsViewModel(new FailingStyleRtss(reason), new Tools(true, false), text, _ => { });
        await vm.UseStyleAsync("premium-top");
        Assert.Equal(text[key], vm.Message);
    }
    [Fact] public async Task OverlayRestore_declined_permission_says_so()
    {
        var text = new TextCatalog();
        var vm = new IntegrationsViewModel(new FailingRestoreRtss(), new Tools(true, false), text, _ => { });
        await vm.RestoreLayoutAsync();
        Assert.Equal(text["OsFailedUac"], vm.Message);
    }
    private sealed class FailingRestoreRtss : IRtssIntegration
    {
        public ToolState State() => new(true, "7.3.7");
        public bool? OverlayConfigured() => true; public bool HasBackup() => false;
        public Task<bool> ApplyLotusOverlayAsync() => Task.FromResult(true); public Task<bool> RestoreAsync() => Task.FromResult(true);
        public string ExportLayout(string variant = "top") => @"C:\x\Lotus.ovl"; public bool LayoutActive() => true;
        public string? ActiveLayoutName() => "Lotus"; public bool HasLayoutBackup() => true;
        public Task<bool> ActivateLayoutAsync(string variant = "top", bool restart = false) => Task.FromResult(true);
        public Task<bool> RestoreLayoutAsync() => Task.FromResult(false);
        public string? LastFailure => "uac";
    }

    [Fact] public async Task OverlayStyle_is_busy_before_game_detection_and_rejects_duplicate_commands()
    {
        var rtss = new StyleRtss(false); var detection = new TaskCompletionSource<bool>();
        var vm = new IntegrationsViewModel(rtss,new Tools(true,false),new TextCatalog(),_=>{}) { GameRunning=()=>detection.Task };
        var first=vm.UseStyleAsync("neon"); Assert.True(vm.Busy);
        await vm.UseStyleAsync("lateral"); Assert.Empty(rtss.Calls);
        detection.SetResult(false); await first; Assert.False(vm.Busy); Assert.Equal([("neon",true)],rtss.Calls);
    }

    [Fact] public void Overlay_preview_uses_the_app_callback_for_the_Lotus_window_renderer()
    {
        var opened=0;
        var vm = new IntegrationsViewModel(new StyleRtss(false),new Tools(true,false),new TextCatalog(),_=>{}) { OpenPreview=()=>{opened++;return true;} };
        Assert.True(vm.CanPreview); vm.PreviewOverlay(); Assert.Equal(1,opened); Assert.Equal(vm.Text["IntPreviewOpened"],vm.Message);
        vm.OpenPreview=()=>false; vm.PreviewOverlay(); Assert.Equal(vm.Text["IntPreviewFailed"],vm.Message);
    }

    [Fact] public async Task OverlayStyleNeverSwitchesUnderARunningGame()
    {
        var rtss = new StyleRtss(running: true);
        var game = true;
        var vm = new IntegrationsViewModel(rtss, new Tools(true, false), new TextCatalog(), _ => { }) { GameRunning = () => Task.FromResult(game) };
        await vm.UseStyleAsync("lateral");
        Assert.Empty(rtss.Calls);                        // nothing touched while playing
        Assert.Equal("lateral", vm.PendingVariant);
        game = false;
        await vm.OnGameClosedAsync();
        Assert.Equal([("lateral", true)], rtss.Calls);   // applied after the game, RivaTuner reopened (it was open)
        Assert.Null(vm.PendingVariant);
        Assert.Equal("lateral", vm.ActiveVariant);
    }

    [Fact] public async Task OverlayRestoreDoesNotRestartRtssUnderARunningGame()
    {
        var rtss = new StyleRtss(true);
        var vm = new IntegrationsViewModel(rtss, new Tools(true,false), new TextCatalog(), _=>{}) { GameRunning=()=>Task.FromResult(true) };
        await vm.RestoreLayoutAsync(); Assert.Equal(0,rtss.Restores);
        Assert.Equal(vm.Text["OsRestoreCloseGame"],vm.Message);
        vm.GameRunning=()=>Task.FromResult(false);
        await vm.RestoreLayoutAsync(); Assert.Equal(1,rtss.Restores);
    }

    [Fact] public async Task OverlayStyleOutsideAGameAppliesNowAndLeavesRivaTunerOpen()
    {
        var closed = new StyleRtss(running: false);
        var vm = new IntegrationsViewModel(closed, new Tools(true, false), new TextCatalog(), _ => { }) { GameRunning = () => Task.FromResult(false) };
        await vm.UseStyleAsync("neon");
        Assert.Equal([("neon", true)], closed.Calls);    // every style leaves RivaTuner open in the same approved step
        Assert.Equal("neon", vm.ActiveVariant);
        await vm.OnGameClosedAsync();                    // nothing pending: nothing more happens
        Assert.Single(closed.Calls);
    }
    [Fact] public async Task PremiumStyleOpensRtssAsPartOfTheSameAction()
    {
        var rtss=new StyleRtss(false);var vm=new IntegrationsViewModel(rtss,new Tools(false,false),new TextCatalog(),_=>{});
        await vm.UseStyleAsync("premium-neon");Assert.Equal([("premium-neon",true)],rtss.Calls);Assert.Equal(1,rtss.Starts);
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

    [Fact] public void EmbeddedLayoutIsConsistent()
    {
        Assert.Contains("Lotus Top Sombra", LotusOverlayLayout.Text()); Assert.Contains("Lotus Neon", LotusOverlayLayout.Text(LotusOverlayLayout.Neon));
        foreach (var variant in new[] { LotusOverlayLayout.Main, LotusOverlayLayout.Neon }) LayoutIsConsistent(LotusOverlayLayout.Text(variant));
    }
    private static void LayoutIsConsistent(string text)
    {
        var ini = new IniText(text);
        Assert.Contains("EmbeddedImage=" + LotusOverlayLayout.ArtFile, text); Assert.Contains("<G=Frametime", text); Assert.Contains("%FPS Low1 Live%", text);
        Assert.Contains("%CPU Temp% °C", text); // ANSI degree sign survives (byte 0xB0)
        Assert.DoesNotContain("<FRAVG>", text); // benchmark-only tag replaced by a live formula
        Assert.DoesNotContain("<S=", text); // OverlayEditor shows data sources as %Name%, not <S=...>
        // Every %Name% macro refers to a declared source.
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"%([A-Za-z][^%\r\n]*?)%"))
            Assert.Contains($"Name={m.Groups[1].Value}\r\n", text);
        var layers = System.Text.RegularExpressions.Regex.Matches(text, @"^\[Layer\d+\]", System.Text.RegularExpressions.RegexOptions.Multiline).Count;
        var sources = System.Text.RegularExpressions.Regex.Matches(text, @"^\[Source\d+\]", System.Text.RegularExpressions.RegexOptions.Multiline).Count;
        Assert.Equal(layers.ToString(), ini.Get("General", "Layers")); Assert.Equal(sources.ToString(), ini.Get("General", "Sources"));
        // Every <S=name> / <G=name,...> refers to a declared source or a built-in one.
        var declared = System.Text.RegularExpressions.Regex.Matches(text, @"^Name=(.+)$", System.Text.RegularExpressions.RegexOptions.Multiline).Select(m => m.Groups[1].Value.Trim()).ToHashSet();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"<[SG]=([^,>]+)"))
            Assert.True(declared.Contains(m.Groups[1].Value) || m.Groups[1].Value == "Frametime", m.Groups[1].Value);
    }

    [Fact] public async Task LayoutIsExportedActivatedOnlyFromLotusFolderAndRestored()
    {
        var overlays = Path.Combine(root, "Overlays"); var config = Path.Combine(root, "OverlayEditor.cfg");
        const string original = "[Settings]\r\nLayout=C:\\Users\\me\\Desktop\\Riva Overlay.ovl";
        File.WriteAllText(config, original);
        Assert.Throws<InvalidOperationException>(() => LotusOverlayLayout.Activate(config, config, Backups, overlays)); // not a Lotus overlay
        Task<int> Runner(string[] a)
        {
            if (a[0] == "--rtss-layout") { LotusOverlayLayout.Activate(a[1], config, Backups, overlays); return Task.FromResult(0); }
            return Task.FromResult(LotusOverlayLayout.Restore(config, Backups) ? 0 : 4);
        }
        var rtss = new WindowsRtssIntegration(Profiles, Backups, Runner, config, overlays);
        Assert.False(rtss.LayoutActive());
        // RivaTuner without its OverlayEditor plugin: refused before any permission prompt, with the reason.
        Assert.False(await rtss.ActivateLayoutAsync()); Assert.Equal("plugin", rtss.LastFailure); Assert.Equal(original, File.ReadAllText(config));
        Directory.CreateDirectory(Path.Combine(root, "Plugins", "Client")); File.WriteAllText(Path.Combine(root, "Plugins", "Client", "OverlayEditor.dll"), "");
        Assert.True(await rtss.ActivateLayoutAsync()); Assert.Null(rtss.LastFailure);
        Assert.True(rtss.LayoutActive()); Assert.True(rtss.HasLayoutBackup()); Assert.True(File.Exists(Path.Combine(overlays, "Lotus.ovl")));
        Assert.True(await rtss.RestoreLayoutAsync()); Assert.Equal(original, File.ReadAllText(config)); Assert.False(rtss.LayoutActive());
    }

    [Fact] public void AnsiFilesWithAccentsAreBackedUpAndRestoredByteForByte()
    {
        var overlays = Path.Combine(root, "Overlays"); var config = Path.Combine(root, "OverlayEditor.cfg");
        var original = System.Text.Encoding.Latin1.GetBytes("[Settings]\r\nLayout=C:\\Users\\Jos\u00e9\\Riva Overlay.ovl\r\nOther=\u00e7\u00e3o");
        File.WriteAllBytes(config, original);
        LotusOverlayLayout.Activate(LotusOverlayLayout.Export(overlays), config, Backups, overlays);
        Assert.EndsWith("Other=\u00e7\u00e3o", RtssProfiles.ReadAnsi(config)); // untouched line keeps its ANSI bytes
        Assert.True(LotusOverlayLayout.Restore(config, Backups));
        Assert.Equal(original, File.ReadAllBytes(config));
    }

    [Fact] public void OnlyTheGeneratedLateralLayoutIsRenewed()
    {
        var overlays = Path.Combine(root, "Overlays");
        Assert.False(LotusOverlayLayout.RefreshGenerated(overlays)); // never exported: nothing written
        var lateral = LotusOverlayLayout.Export(overlays, LotusOverlayLayout.Lateral);
        var neon = LotusOverlayLayout.Export(overlays, LotusOverlayLayout.Neon);
        var main = LotusOverlayLayout.Export(overlays, LotusOverlayLayout.Main);
        var shipped = File.ReadAllBytes(lateral);
        Assert.False(LotusOverlayLayout.RefreshGenerated(overlays)); // same as shipped
        File.WriteAllText(lateral, "old Lateral"); File.WriteAllText(neon, "player's Neon"); File.WriteAllText(main, "player's Top");
        Assert.True(LotusOverlayLayout.RefreshGenerated(overlays));
        Assert.Equal(shipped, File.ReadAllBytes(lateral));
        Assert.Equal("player's Neon", File.ReadAllText(neon));
        Assert.Equal("player's Top", File.ReadAllText(main));
    }

    [Fact] public void HelperUsesOnlyAnExistingLotusDataFolder()
    {
        var data = Path.Combine(root, "Lotus"); Directory.CreateDirectory(data);
        Assert.Equal(data, RtssElevatedHelper.DataRoot(["--rtss-layout-restore", "--lotus-data", data + "\\"]));
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus");
        Assert.Equal(fallback, RtssElevatedHelper.DataRoot(["--rtss-layout-restore", "--lotus-data", root]));         // not named Lotus
        Assert.Equal(fallback, RtssElevatedHelper.DataRoot(["--rtss-layout-restore", "--lotus-data", data + "-missing\\Lotus"])); // missing
        Assert.Equal(fallback, RtssElevatedHelper.DataRoot(["--rtss-layout-restore"]));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
