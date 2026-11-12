namespace Lotus.Core.Games;

// Is the processor slowing itself down (heat or power limit) while a game runs? Read without administrator rights from
// Windows' own counters: "% Performance Limit" (100 = free to run at full speed; less = held back by the firmware)
// and the ACPI thermal zones (the motherboard's temperature reading, close to the CPU on most laptops; many desktops
// do not expose one). Lotus never claims a precise CPU core temperature from these.
public sealed record CpuHeatSample(double? PerformanceLimit, double? ZoneCelsius);
public sealed record CpuHeat(double? AveragePerformanceLimit, double? MaxZoneCelsius)
{
    public const double LimitedBelow = 85, HotZone = 90;
    public bool Limited => AveragePerformanceLimit is < LimitedBelow;
    public bool Hot => MaxZoneCelsius is >= HotZone;

    public static CpuHeat? From(IReadOnlyList<CpuHeatSample> samples)
    {
        var limits = samples.Select(s => s.PerformanceLimit).OfType<double>().ToList();
        var zones = samples.Select(s => s.ZoneCelsius).OfType<double>().ToList();
        if (limits.Count == 0 && zones.Count == 0) return null;
        return new(limits.Count > 0 ? limits.Average() : null, zones.Count > 0 ? zones.Max() : null);
    }
}

public interface ICpuHeatSampler
{
    CpuHeatSample? Sample();                                              // one reading now (null = counters not available)
    Task<CpuHeat?> MeasureAsync(TimeSpan duration, CancellationToken token);
}

// While playing: one quiet warning per session when the processor stays held back for minutes, or the thermal zone
// stays very hot. Short dips (loading screens, a burst) never warn.
public sealed class CpuHeatWatch
{
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(15);
    public const int LimitedSamples = 8, HotSamples = 4;   // about 2 minutes held back / 1 minute very hot
    public const double AlertLimit = 75, AlertZone = 95;
    private int limitedRun, hotRun;
    public bool Warned { get; private set; }
    public CpuHeat? Last { get; private set; }

    public void Reset() { limitedRun = hotRun = 0; Warned = false; Last = null; }

    // True once, when the session first crosses the line.
    public bool Feed(CpuHeatSample? s)
    {
        if (s is null || Warned) return false;
        limitedRun = s.PerformanceLimit is < AlertLimit ? limitedRun + 1 : 0;
        hotRun = s.ZoneCelsius is >= AlertZone ? hotRun + 1 : 0;
        if (limitedRun < LimitedSamples && hotRun < HotSamples) return false;
        Warned = true; Last = new(s.PerformanceLimit, s.ZoneCelsius);
        return true;
    }
}
