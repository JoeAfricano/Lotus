using Lotus.Core.Integrations;
using Lotus.Core;
using Xunit;
using Lotus.Infrastructure.Games;
using Lotus.Infrastructure.Integrations;

namespace Lotus.Tests;

public sealed class OverlayHotkeyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "LotusHotkey-" + Guid.NewGuid());
    public OverlayHotkeyTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Fact] public void CodesFollowRivaTunersFormatAndReadBack()
    {
        Assert.Equal(0x91u, OverlayHotkeys.Code("ScrollLock"));
        Assert.Equal(0x60079u, OverlayHotkeys.Code("CtrlAltF10"));
        Assert.Equal("Ctrl + Alt + F10", OverlayHotkeys.Describe(0x60079));
        Assert.Equal("Scroll Lock", OverlayHotkeys.Describe(0x91));
        Assert.Equal(OverlayHotkeys.None, OverlayHotkeys.ChoiceOf(0));
        Assert.Equal(OverlayHotkeys.Custom, OverlayHotkeys.ChoiceOf(0x20050));
        Assert.Equal(0x91u, RtssOverlayHotkey.Parse("00000091h")); // MSI Afterburner writes an "h"
        Assert.Equal(0u, RtssOverlayHotkey.Parse(null));
        foreach (var c in OverlayHotkeys.Choices.Where(c => c != OverlayHotkeys.None)) Assert.NotEqual(0x20050u, OverlayHotkeys.Code(c)); // Ctrl+P is RivaTuner's own
    }

    [Fact] public void FreshRivaTunerGetsTheKeyAndThePluginAndNoneGivesBothBack()
    {
        var cfg = Path.Combine(root, "Plugins", "Client", "HotkeyHandler.cfg"); var plugin = Path.Combine(root, "Profiles", "Config"); var backups = Path.Combine(root, "Backups");
        RtssOverlayHotkey.Apply("CtrlF10", cfg, plugin, backups); // no file, no folders, plugin never listed
        Assert.Equal(0x20079u, RtssOverlayHotkey.Read(cfg, plugin));
        Assert.Equal("00020079", new IniText(File.ReadAllText(cfg)).Get("Settings", "OSDToggleHotkey"));
        Assert.Equal("1", new IniText(File.ReadAllText(plugin)).Get("Plugins", "HotkeyHandler.dll"));
        RtssOverlayHotkey.Apply("ScrollLock", cfg, plugin, backups); // second change keeps the first originals
        Assert.Equal(0x91u, RtssOverlayHotkey.Read(cfg, plugin));
        RtssOverlayHotkey.Apply(OverlayHotkeys.None, cfg, plugin, backups);
        Assert.False(File.Exists(cfg));
        Assert.Equal("0", new IniText(File.ReadAllText(plugin)).Get("Plugins", "HotkeyHandler.dll"));
        Assert.Equal(0u, RtssOverlayHotkey.Read(cfg, plugin));
    }

    [Fact] public void OnlyTheToggleKeyChangesAndAPluginTurnedOffMeansNoKey()
    {
        var cfg = Path.Combine(root, "HotkeyHandler.cfg"); var plugin = Path.Combine(root, "Config"); var backups = Path.Combine(root, "Backups");
        const string original = "[Settings]\r\nOSDToggleHotkey=00000000\r\nLimiterToggleHotkey=00020046";
        File.WriteAllText(cfg, original); File.WriteAllText(plugin, "[Plugins]\r\nHotkeyHandler.dll=0\r\nOverlayEditor.dll=1");
        Assert.Equal(0u, RtssOverlayHotkey.Read(cfg, plugin));
        RtssOverlayHotkey.Apply("Pause", cfg, plugin, backups);
        var ini = new IniText(File.ReadAllText(cfg));
        Assert.Equal("00000013", ini.Get("Settings", "OSDToggleHotkey")); Assert.Equal("00020046", ini.Get("Settings", "LimiterToggleHotkey"));
        Assert.Equal("1", new IniText(File.ReadAllText(plugin)).Get("Plugins", "OverlayEditor.dll"));
        RtssOverlayHotkey.Apply(OverlayHotkeys.None, cfg, plugin, backups);
        Assert.Equal(original, File.ReadAllText(cfg));
        Assert.Throws<InvalidOperationException>(() => RtssOverlayHotkey.Apply("F5", cfg, plugin, backups));
    }

    private sealed class HotkeyRtss(uint? afterburner) : IRtssIntegration
    {
        public List<string> Set { get; } = [];
        public ToolState State() => new(true, "7.3.7");
        public bool? OverlayConfigured() => true; public bool HasBackup() => false;
        public Task<bool> ApplyLotusOverlayAsync() => Task.FromResult(true); public Task<bool> RestoreAsync() => Task.FromResult(true);
        public string ExportLayout(string variant = "top") => @"C:\x\Lotus.ovl"; public bool LayoutActive() => false;
        public string? ActiveLayoutName() => null; public bool HasLayoutBackup() => false;
        public Task<bool> ActivateLayoutAsync(string variant = "top", bool restart = false) => Task.FromResult(true);
        public Task<bool> RestoreLayoutAsync() => Task.FromResult(true);
        public uint? AfterburnerOverlayHotkey() => afterburner;
        public uint OverlayHotkey() => Set.Count == 0 ? 0 : OverlayHotkeys.Code(Set[^1]) ?? 0;
        public Task<bool> SetOverlayHotkeyAsync(string choice) { Set.Add(choice); return Task.FromResult(true); }
    }
    private sealed class Tools : IToolDetector { public ToolState Afterburner() => new(true, "4.6.6"); public ToolState ScsKiller() => new(false, null); }

    [Fact] public async Task ViewModelSavesAKeyButNeverAfterburnersOneOrUnderAGame()
    {
        var text = new TextCatalog(); var rtss = new HotkeyRtss(0x91);
        var vm = new IntegrationsViewModel(rtss, new Tools(), text, _ => { }) { GameRunning = () => Task.FromResult(false) };
        vm.Refresh(); Assert.Equal("Scroll Lock", vm.AfterburnerHotkeyText); Assert.Equal(OverlayHotkeys.None, vm.OverlayHotkey);
        await vm.SetOverlayHotkeyAsync("ScrollLock"); Assert.Empty(rtss.Set); Assert.Equal(text.Format("OsHotkeyAfterburnerSame", "Scroll Lock"), vm.Message);
        await vm.SetOverlayHotkeyAsync("CtrlF10"); Assert.Equal(["CtrlF10"], rtss.Set); Assert.Equal("CtrlF10", vm.OverlayHotkey);
        Assert.Equal(text.Format("OsHotkeySaved", "Ctrl + F10"), vm.Message);
        vm.GameRunning = () => Task.FromResult(true);
        await vm.SetOverlayHotkeyAsync("Pause"); Assert.Single(rtss.Set); Assert.Equal(text["OsHotkeyCloseGame"], vm.Message);
    }
}
