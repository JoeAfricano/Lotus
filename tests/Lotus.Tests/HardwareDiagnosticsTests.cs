using System.Text;
using Lotus.Core;
using Lotus.Core.Diagnostics;
using Lotus.Infrastructure.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace Lotus.Tests;

public sealed class HardwareDiagnosticsTests(ITestOutputHelper output)
{
    private static readonly TextCatalog Text = new();
    private static MemoryModuleFact Dimm(string locator, int configured = 3200, int rated = 3200, int type = HardwareRules.Ddr4, long mb = 8192, string bank = "") =>
        new(locator, bank, mb, rated, configured, type);

    [Theory]
    [InlineData(60, 144, FindingLevel.Attention)]
    [InlineData(143, 144, FindingLevel.Ok)]
    [InlineData(59, 60, FindingLevel.Ok)]
    [InlineData(165, 165, FindingLevel.Ok)]
    public void DisplayFlagsOnlyRealRefreshGaps(int current, int max, FindingLevel level) =>
        Assert.Equal(level, HardwareRules.Displays([new("Monitor", 2560, 1440, current, max)], Text).Single().Level);

    [Theory]
    [InlineData("ChannelA-DIMM0", "A")]
    [InlineData("P0 CHANNEL B", "B")]
    [InlineData("DIMM_A2", "A")]
    [InlineData("B1", "B")]
    [InlineData("DIMM 0", null)]
    [InlineData("", null)]
    public void ChannelParsingRecognizesCommonLocators(string locator, string? channel) => Assert.Equal(channel, HardwareRules.Channel(locator));

    [Fact] public void MemoryChannelRules()
    {
        Level(FindingLevel.Attention, "ram-channels", [Dimm("DIMM_A2")]);
        Level(FindingLevel.Attention, "ram-channels", [Dimm("DIMM_A1"), Dimm("DIMM_A2")]);
        Level(FindingLevel.Ok, "ram-channels", [Dimm("DIMM_A2"), Dimm("DIMM_B2")]);
        Level(FindingLevel.Ok, "ram-channels", [Dimm("DIMM 0", bank: "P0 CHANNEL A"), Dimm("DIMM 0", bank: "P0 CHANNEL B")]);
        Level(FindingLevel.Info, "ram-channels", [Dimm("DIMM 0"), Dimm("DIMM 1")]);
        Level(FindingLevel.Unavailable, "ram", [Dimm("DIMM_A2", mb: 0)]);
    }

    [Fact] public void MemorySpeedRules()
    {
        Level(FindingLevel.Attention, "ram-speed", [Dimm("A1", configured: 2133, rated: 3200), Dimm("B1", configured: 2133, rated: 3200)]);
        Level(FindingLevel.Info, "ram-speed", [Dimm("A1", configured: 2400, rated: 2400), Dimm("B1", configured: 2400, rated: 2400)]);
        Level(FindingLevel.Info, "ram-speed", [Dimm("A1", 4800, 4800, HardwareRules.Ddr5), Dimm("B1", 4800, 4800, HardwareRules.Ddr5)]);
        Level(FindingLevel.Ok, "ram-speed", [Dimm("A1", 3600, 3600), Dimm("B1", 3600, 3600)]);
        Level(FindingLevel.Ok, "ram-speed", [Dimm("A1", 6000, 4800, HardwareRules.Ddr5), Dimm("B1", 6000, 4800, HardwareRules.Ddr5)]);
        Level(FindingLevel.Unavailable, "ram-speed", [Dimm("A1", configured: 0), Dimm("B1", configured: 0)]);
    }
    private static void Level(FindingLevel level, string id, MemoryModuleFact[] modules) =>
        Assert.Equal(level, HardwareRules.Memory(modules, Text).Single(f => f.Id == id).Level);

    [Fact] public void CpuLimitRules()
    {
        Assert.Equal(FindingLevel.Attention, HardwareRules.Cpu(new(4200, 2000), Text).Level);
        Assert.Equal(FindingLevel.Ok, HardwareRules.Cpu(new(4200, 4200), Text).Level);
        Assert.Equal(FindingLevel.Unavailable, HardwareRules.Cpu(null, Text).Level);
    }

    [Theory]
    [InlineData(null, FindingLevel.Unavailable)]
    [InlineData(0u, FindingLevel.Ok)]
    [InlineData(HardwareRules.GpuIdle, FindingLevel.Info)]
    [InlineData(HardwareRules.SwPowerCap, FindingLevel.Info)]
    [InlineData(HardwareRules.SwThermal, FindingLevel.Attention)]
    [InlineData(HardwareRules.HwThermal | HardwareRules.SwPowerCap, FindingLevel.Attention)]
    [InlineData(HardwareRules.HwPowerBrake, FindingLevel.Attention)]
    public void GpuThrottleRules(uint? reasons, FindingLevel level) => Assert.Equal(level, HardwareRules.GpuThrottle(reasons, Text).Level);

    [Fact] public void GpuAdapterRules()
    {
        Assert.Equal(FindingLevel.Info, HardwareRules.GpuAdapters(["Intel(R) UHD Graphics 630", "NVIDIA GeForce RTX 4060 Laptop GPU"], Text).Level);
        Assert.Equal(FindingLevel.Info, HardwareRules.GpuAdapters(["AMD Radeon(TM) Graphics", "NVIDIA GeForce RTX 3050"], Text).Level);
        var dedicated = HardwareRules.GpuAdapters(["NVIDIA GeForce RTX 5060", "NVIDIA GeForce RTX 5060", "Microsoft Basic Display Adapter"], Text);
        Assert.Equal(FindingLevel.Ok, dedicated.Level); Assert.Contains("RTX 5060", dedicated.Detail);
        Assert.Equal(FindingLevel.Info, HardwareRules.GpuAdapters(["Intel(R) Iris(R) Xe Graphics"], Text).Level);
        Assert.False(HardwareRules.IsIntegrated("AMD Radeon RX 7800 XT"));
    }

    [Fact] public void SmbiosParserReadsType17ModulesAndSkipsEmptySlots()
    {
        var table = new List<byte>();
        table.AddRange(Structure(0, 0x18, [], "BIOS Vendor"));                                   // type 0 with a string
        table.AddRange(Memory17(8192, "DIMM_A2", "BANK 1", 3200, 2400));
        table.AddRange(Memory17(0, "DIMM_A1", "BANK 0", 0, 0));                                  // empty slot
        table.AddRange(Memory17(0x7FFF, "DIMM_B2", "BANK 3", 4800, 6000, extendedMb: 32768, type: HardwareRules.Ddr5));
        table.AddRange([127, 4, 0, 0, 0, 0]);
        var raw = new byte[8 + table.Count]; BitConverter.GetBytes(table.Count).CopyTo(raw, 4); table.CopyTo(raw, 8);
        var modules = SmbiosMemoryParser.Parse(raw);
        Assert.Equal(3, modules.Count);
        Assert.Equal(new MemoryModuleFact("DIMM_A2", "BANK 1", 8192, 3200, 2400, HardwareRules.Ddr4), modules[0]);
        Assert.Equal(0, modules[1].SizeMb);
        Assert.Equal(32768, modules[2].SizeMb); Assert.Equal(6000, modules[2].ConfiguredMts); Assert.Equal(HardwareRules.Ddr5, modules[2].SmbiosType);
        Assert.Empty(SmbiosMemoryParser.Parse(new byte[4]));
    }
    private static IEnumerable<byte> Memory17(ushort size, string locator, string bank, ushort rated, ushort configured, uint extendedMb = 0, int type = HardwareRules.Ddr4)
    {
        var f = new byte[0x28];
        BitConverter.GetBytes(size).CopyTo(f, 0x0C); f[0x10] = 1; f[0x11] = 2; f[0x12] = (byte)type;
        BitConverter.GetBytes(rated).CopyTo(f, 0x15); BitConverter.GetBytes(extendedMb).CopyTo(f, 0x1C); BitConverter.GetBytes(configured).CopyTo(f, 0x20);
        return Structure(17, 0x28, f, locator, bank);
    }
    private static IEnumerable<byte> Structure(byte type, byte length, byte[] formatted, params string[] strings)
    {
        var f = formatted.Length == length ? formatted : new byte[length];
        f[0] = type; f[1] = length;
        var bytes = new List<byte>(f);
        if (strings.Length == 0) bytes.AddRange([0, 0]);
        else { foreach (var s in strings) { bytes.AddRange(Encoding.ASCII.GetBytes(s)); bytes.Add(0); } bytes.Add(0); }
        return bytes;
    }

    [Fact] public async Task ViewModelSummarizesAttentionWithoutMonitoringSample()
    {
        var facts = new HardwareFacts([new("Monitor", 1920, 1080, 60, 144)], [Dimm("DIMM_A2"), Dimm("DIMM_B2")], new(4200, 4200), ["NVIDIA GeForce RTX 5060"], []);
        var vm = new HardwareDiagnosticsViewModel(new FakeFacts(facts), null, Text, _ => { });
        await vm.CheckAsync();
        Assert.Equal(Text.Format("HwSummaryAttention", 1), vm.Summary);
        Assert.True(vm.Findings[0].IsAttention); Assert.True(vm.Findings[0].HasSettings);
        Assert.Contains(vm.Findings, f => f.Title == Text["HwGpuThrottleTitle"] && f.Level == Text["HwLevelUnavailable"]);
    }
    private sealed class FakeFacts(HardwareFacts facts) : IHardwareFactsSource { public HardwareFacts Read() => facts; }

    [Fact] public void RealHardwareFactsAreReadOnlyQueries()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var facts = new WindowsHardwareFacts().Read();
        foreach (var d in facts.Displays) output.WriteLine($"display {d.Name} {d.Width}x{d.Height} {d.CurrentHz}/{d.MaxHz} Hz");
        foreach (var m in facts.Memory) output.WriteLine($"dimm '{m.Locator}' '{m.Bank}' {m.SizeMb} MB rated {m.RatedMts} configured {m.ConfiguredMts} type {m.SmbiosType}");
        output.WriteLine($"cpu {facts.Cpu?.MaxMhz} max / {facts.Cpu?.LimitMhz} limit; adapters: {string.Join(" | ", facts.GpuAdapters)}; errors: {string.Join(",", facts.Errors)}");
        foreach (var f in HardwareRules.Evaluate(facts, null, Text)) output.WriteLine($"{f.Level}: {f.Title} — {f.Detail}");
        Assert.Empty(facts.Errors);
        Assert.NotEmpty(facts.Displays); Assert.NotEmpty(facts.Memory);
    }
}
