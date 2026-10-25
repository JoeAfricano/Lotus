using System.Text;
using System.Text.RegularExpressions;

namespace Lotus.Core.Diagnostics;

// Read-only facts about configuration issues that commonly hold hardware back.
public sealed record DisplayFact(string Name, int Width, int Height, int CurrentHz, int MaxHz);
public sealed record MemoryModuleFact(string Locator, string Bank, long SizeMb, int RatedMts, int ConfiguredMts, int SmbiosType);
public sealed record CpuLimitFact(int MaxMhz, int LimitMhz);
public sealed record HardwareFacts(IReadOnlyList<DisplayFact> Displays, IReadOnlyList<MemoryModuleFact> Memory, CpuLimitFact? Cpu,
    IReadOnlyList<string> GpuAdapters, IReadOnlyList<string> Errors);
public interface IHardwareFactsSource { HardwareFacts Read(); }

public enum FindingLevel { Attention, Info, Ok, Unavailable }
public sealed record HardwareFinding(string Id, FindingLevel Level, string Title, string Detail, string? SettingsUri = null);

public static class HardwareRules
{
    public const int Ddr4 = 26, Ddr5 = 34;

    public static IReadOnlyList<HardwareFinding> Evaluate(HardwareFacts facts, uint? gpuReasons, ITextCatalog text)
    {
        var list = new List<HardwareFinding>();
        list.AddRange(Displays(facts.Displays, text));
        list.AddRange(Memory(facts.Memory, text));
        list.Add(Cpu(facts.Cpu, text));
        list.Add(GpuThrottle(gpuReasons, text));
        list.Add(GpuAdapters(facts.GpuAdapters, text));
        return list.OrderBy(f => f.Level).ToList();
    }

    public static IEnumerable<HardwareFinding> Displays(IReadOnlyList<DisplayFact> displays, ITextCatalog text)
    {
        if (displays.Count == 0) { yield return new("display", FindingLevel.Unavailable, text["HwDisplayTitle"], text["HwDisplayUnavailable"]); yield break; }
        foreach (var d in displays)
        {
            var title = text.Format("HwDisplayNamed", d.Name);
            // A 1 Hz margin avoids flagging 59/60 Hz or 143/144 Hz rounding variants.
            yield return d.MaxHz - d.CurrentHz > 1
                ? new("display", FindingLevel.Attention, title, text.Format("HwDisplayLow", d.CurrentHz, d.MaxHz, d.Width, d.Height), "ms-settings:display-advanced")
                : new("display", FindingLevel.Ok, title, text.Format("HwDisplayOk", d.CurrentHz, d.Width, d.Height));
        }
    }

    public static IEnumerable<HardwareFinding> Memory(IReadOnlyList<MemoryModuleFact> modules, ITextCatalog text)
    {
        var used = modules.Where(m => m.SizeMb > 0).ToList();
        if (used.Count == 0) { yield return new("ram", FindingLevel.Unavailable, text["HwRamTitle"], text["HwRamUnavailable"]); yield break; }
        var total = used.Sum(m => m.SizeMb) / 1024.0;
        // Channels
        if (used.Count == 1)
            yield return new("ram-channels", FindingLevel.Attention, text["HwChannelsTitle"], text.Format("HwChannelsSingle", total.ToString("0.#")));
        else
        {
            var channels = used.Select(m => Channel(m.Locator) ?? Channel(m.Bank)).ToList();
            yield return channels.Any(c => c is null)
                ? new("ram-channels", FindingLevel.Info, text["HwChannelsTitle"], text.Format("HwChannelsUnknown", used.Count, total.ToString("0.#")))
                : channels.Distinct().Count() == 1
                    ? new("ram-channels", FindingLevel.Attention, text["HwChannelsTitle"], text.Format("HwChannelsSame", used.Count, channels[0]!))
                    : new("ram-channels", FindingLevel.Ok, text["HwChannelsTitle"], text.Format("HwChannelsDual", used.Count, total.ToString("0.#")));
        }
        // Speed: configured below rated is a clear sign; JEDEC-base speeds are only a hint, since SMBIOS rated speed is often JEDEC too.
        var configured = used.Where(m => m.ConfiguredMts > 0).Select(m => m.ConfiguredMts).DefaultIfEmpty(0).Min();
        var rated = used.Where(m => m.RatedMts > 0).Select(m => m.RatedMts).DefaultIfEmpty(0).Min();
        var type = used[0].SmbiosType;
        var ddr = type == Ddr5 ? "DDR5" : type == Ddr4 ? "DDR4" : "";
        if (configured == 0) yield return new("ram-speed", FindingLevel.Unavailable, text["HwSpeedTitle"], text["HwSpeedUnavailable"]);
        else if (rated > 0 && configured < rated * 0.95)
            yield return new("ram-speed", FindingLevel.Attention, text["HwSpeedTitle"], text.Format("HwSpeedBelowRated", configured, rated, ddr));
        else if ((type == Ddr4 && configured <= 2666) || (type == Ddr5 && configured <= 4800))
            yield return new("ram-speed", FindingLevel.Info, text["HwSpeedTitle"], text.Format("HwSpeedJedec", configured, ddr));
        else yield return new("ram-speed", FindingLevel.Ok, text["HwSpeedTitle"], text.Format("HwSpeedOk", configured, ddr));
    }

    // Recognizes common board locator formats: "ChannelA-DIMM0", "P0 CHANNEL B", "DIMM_A2", "A1".
    public static string? Channel(string? locator)
    {
        if (string.IsNullOrWhiteSpace(locator)) return null;
        var m = Regex.Match(locator, @"CHANNEL\s*([A-H])\b|CHANNEL([A-H])|DIMM[_\s-]?([A-H])\d|^\s*([A-H])\d\s*$", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value.ToUpperInvariant() : null;
    }

    public static HardwareFinding Cpu(CpuLimitFact? cpu, ITextCatalog text) =>
        cpu is null || cpu.MaxMhz <= 0 ? new("cpu", FindingLevel.Unavailable, text["HwCpuTitle"], text["HwCpuUnavailable"])
        : cpu.LimitMhz > 0 && cpu.LimitMhz < cpu.MaxMhz * 0.9 ? new("cpu", FindingLevel.Attention, text["HwCpuTitle"], text.Format("HwCpuLimited", cpu.LimitMhz, cpu.MaxMhz))
        : new("cpu", FindingLevel.Ok, text["HwCpuTitle"], text.Format("HwCpuOk", cpu.MaxMhz));

    // NVML clock event reasons (bit flags).
    public const uint GpuIdle = 0x1, SwPowerCap = 0x4, HwSlowdown = 0x8, SwThermal = 0x20, HwThermal = 0x40, HwPowerBrake = 0x80;
    public static HardwareFinding GpuThrottle(uint? reasons, ITextCatalog text)
    {
        var title = text["HwGpuThrottleTitle"];
        if (reasons is not { } r) return new("gpu-throttle", FindingLevel.Unavailable, title, text["HwGpuThrottleNoSample"]);
        if ((r & (SwThermal | HwThermal)) != 0) return new("gpu-throttle", FindingLevel.Attention, title, text["HwGpuThermal"]);
        if ((r & (HwSlowdown | HwPowerBrake)) != 0) return new("gpu-throttle", FindingLevel.Attention, title, text["HwGpuHwSlowdown"]);
        if ((r & SwPowerCap) != 0) return new("gpu-throttle", FindingLevel.Info, title, text["HwGpuPowerCap"]);
        if ((r & GpuIdle) != 0) return new("gpu-throttle", FindingLevel.Info, title, text["HwGpuIdle"]);
        return new("gpu-throttle", FindingLevel.Ok, title, text["HwGpuNoLimit"]);
    }

    // Name heuristics only: integrated graphics are Intel UHD/Iris/HD or AMD "Radeon(TM) Graphics" without a discrete model.
    public static bool IsIntegrated(string name) =>
        Regex.IsMatch(name, @"Intel.*(UHD|Iris|HD Graphics|Arc\(TM\) Graphics)|Radeon\(TM\)\s*Graphics$|Radeon Graphics$|Radeon Vega \d+ Graphics", RegexOptions.IgnoreCase);
    public static HardwareFinding GpuAdapters(IReadOnlyList<string> adapters, ITextCatalog text)
    {
        var title = text["HwGpuTitle"];
        var names = adapters.Where(a => !string.IsNullOrWhiteSpace(a) && !a.Contains("Basic", StringComparison.OrdinalIgnoreCase) && !a.Contains("Remote", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0) return new("gpu", FindingLevel.Unavailable, title, text["HwGpuUnavailable"]);
        var integrated = names.Where(IsIntegrated).ToList(); var dedicated = names.Except(integrated).ToList();
        if (integrated.Count > 0 && dedicated.Count > 0)
            return new("gpu", FindingLevel.Info, title, text.Format("HwGpuHybrid", string.Join(", ", dedicated), string.Join(", ", integrated)), "ms-settings:display-advancedgraphics");
        return dedicated.Count > 0 ? new("gpu", FindingLevel.Ok, title, text.Format("HwGpuDedicated", string.Join(", ", dedicated)))
            : new("gpu", FindingLevel.Info, title, text.Format("HwGpuIntegratedOnly", string.Join(", ", integrated)));
    }
}

// Parses SMBIOS type 17 (Memory Device) from the raw firmware table returned by GetSystemFirmwareTable('RSMB').
public static class SmbiosMemoryParser
{
    public static IReadOnlyList<MemoryModuleFact> Parse(ReadOnlySpan<byte> raw)
    {
        var result = new List<MemoryModuleFact>();
        if (raw.Length < 8) return result;
        var length = BitConverter.ToInt32(raw.Slice(4, 4));
        var data = raw.Slice(8, Math.Min(length, raw.Length - 8));
        var offset = 0;
        while (offset + 4 <= data.Length)
        {
            var type = data[offset]; var formatted = data[offset + 1];
            if (formatted < 4 || offset + formatted > data.Length) break;
            var strings = ReadStrings(data, offset + formatted, out var next);
            if (type == 17 && formatted >= 0x15)
            {
                var s = data.Slice(offset, formatted);
                var size = BitConverter.ToUInt16(s.Slice(0x0C, 2));
                long sizeMb = size == 0 || size == 0xFFFF ? 0
                    : size == 0x7FFF && formatted >= 0x20 ? BitConverter.ToUInt32(s.Slice(0x1C, 4)) & 0x7FFFFFFF
                    : (size & 0x8000) != 0 ? (size & 0x7FFF) / 1024 : size;
                string Str(int index) => index > 0 && index <= strings.Count ? strings[index - 1].Trim() : "";
                var rated = formatted >= 0x17 ? BitConverter.ToUInt16(s.Slice(0x15, 2)) : 0;
                var configured = formatted >= 0x22 ? BitConverter.ToUInt16(s.Slice(0x20, 2)) : 0;
                result.Add(new(Str(s[0x10]), Str(s[0x11]), sizeMb, rated is 0xFFFF ? 0 : rated, configured is 0xFFFF ? 0 : configured, s[0x12]));
            }
            if (type == 127) break;
            offset = next;
        }
        return result;
    }

    private static List<string> ReadStrings(ReadOnlySpan<byte> data, int start, out int next)
    {
        var list = new List<string>(); var i = start;
        if (i + 1 < data.Length && data[i] == 0 && data[i + 1] == 0) { next = i + 2; return list; }
        while (i < data.Length)
        {
            var end = i; while (end < data.Length && data[end] != 0) end++;
            list.Add(Encoding.ASCII.GetString(data[i..end]));
            i = end + 1;
            if (i >= data.Length || data[i] == 0) { i++; break; }
        }
        next = i; return list;
    }
}
