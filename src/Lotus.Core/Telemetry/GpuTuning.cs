namespace Lotus.Core.Telemetry;

// "Ajuste da placa" (Desempenho): Lotus guides an undervolt or overclock that MSI Afterburner applies, tests it and
// checks it while playing. Lotus never writes clocks or voltages itself (that needs administrator rights and NVIDIA's
// undocumented interfaces); Afterburner does it within the card's factory limits.
public enum TuneGoal { Undervolt, Overclock, Stock }

// What the assistant needs before it starts. Afterburner's automatic 2D/3D profiles depend on RivaTuner being installed.
public sealed record TuneReadiness(bool Nvidia, bool Afterburner, bool RivaTuner, bool VoltageUnlocked, bool AutoProfiles)
{
    public bool CanStart => Nvidia && Afterburner;
    // The first thing missing, in the order the player should fix it (null = ready).
    public string? Missing => !Nvidia ? "nvidia" : !Afterburner ? "afterburner" : !RivaTuner ? "rivatuner" : !VoltageUnlocked ? "voltage" : !AutoProfiles ? "auto-profiles" : null;
}

// One value to set in Afterburner for the next try. Undervolt: the curve point (mV, MHz). Overclock: offsets (MHz).
public sealed record TuneStep(TuneGoal Goal, int CoreMhz, int MemoryMhz, int VoltageMv, int PowerLimitPct);

// A few seconds of sensor readings during the stability test.
public sealed record TuneSample(double TempC, double CoreMhz, double PowerW);

public enum TuneVerdict { Stable, Crashed, TooHot, Throttled, NoData }
public sealed record TuneResult(TuneVerdict Verdict, double MaxTempC, double AvgCoreMhz, double ClockSpreadMhz, TuneStep? Next);

public static class GpuTuning
{
    public const double StopTempC = 83;   // the test stops above this
    public const double WarnTempC = 80;   // stable, but hotter than we like
    public const double SafetyShare = 0.7; // keep 70% of the highest overclock that passed (margin for long sessions)

    // Conservative first tries per card family (what owners and MSI's guides report as a safe start; every chip is
    // different, so the test decides). Undervolt = flat curve from this voltage point at this clock.
    // OcCoreMax / OcMemMax: the highest offsets reviews report as stable for that family, where the overclock ladder stops.
    public sealed record CardStart(string Family, int UndervoltMv, int UndervoltMhz, int StepMhz, string Note, int OcCoreMax = 400, int OcMemMax = 1000);
    private static readonly (System.Text.RegularExpressions.Regex Match, CardStart Start)[] Cards =
    [
        (new(@"RTX\s*50(9|8)0", System.Text.RegularExpressions.RegexOptions.IgnoreCase), new("RTX 5080/5090", 950, 2600, 15, "voltage-cap")),
        (new(@"RTX\s*507\d", System.Text.RegularExpressions.RegexOptions.IgnoreCase), new("RTX 5070", 950, 2600, 15, "")),
        (new(@"RTX\s*5060\s*Ti", System.Text.RegularExpressions.RegexOptions.IgnoreCase), new("RTX 5060 Ti", 950, 2700, 15, "", 375, 1000)), // TechPowerUp / FPS Review: core +200..+375, memory up to +1000
        (new(@"RTX\s*506\d", System.Text.RegularExpressions.RegexOptions.IgnoreCase), new("RTX 5060", 950, 2700, 15, "", 450, 1000)),       // MSI 5060 Gaming OC: core +450, memory +1000 stable
        (new(@"RTX\s*4090", System.Text.RegularExpressions.RegexOptions.IgnoreCase), new("RTX 4090", 985, 2700, 15, "voltage-cap")),
        (new(@"RTX\s*40(7|8)\d", System.Text.RegularExpressions.RegexOptions.IgnoreCase), new("RTX 4070/4080", 950, 2700, 15, "")),
        (new(@"RTX\s*406\d", System.Text.RegularExpressions.RegexOptions.IgnoreCase), new("RTX 4060", 925, 2650, 15, "")),
        (new(@"RTX\s*306\d", System.Text.RegularExpressions.RegexOptions.IgnoreCase), new("RTX 3060", 900, 1850, 25, "")),
        (new(@"RTX\s*30[789]\d", System.Text.RegularExpressions.RegexOptions.IgnoreCase), new("RTX 3070/3080/3090", 875, 1800, 25, "")),
        (new(@"RTX\s*20\d\d|GTX\s*16\d\d", System.Text.RegularExpressions.RegexOptions.IgnoreCase), new("RTX 20/GTX 16", 900, 1800, 15, "")),
    ];
    // Unknown NVIDIA cards get the most common start; AMD Radeon is tuned in AMD Adrenalin (negative voltage offset).
    public static CardStart For(string? gpuName) =>
        Cards.FirstOrDefault(c => gpuName is not null && c.Match.IsMatch(gpuName)).Start ?? new("NVIDIA", 950, 2600, 15, "");
    public static bool IsRadeon(string? gpuName) => gpuName?.Contains("Radeon", StringComparison.OrdinalIgnoreCase) == true;
    // AMD Adrenalin: start at -40 mV, 25 mV deeper per test; most RX 9070 XT owners end between -70 and -100 mV.
    public const int AmdStartOffsetMv = -40, AmdStepMv = -25, AmdTypicalMinMv = -100;

    // Starting points: undervolt at the card's point; overclock from stock with the power limit at its maximum,
    // small steps (core +50, memory +100 MHz).
    public static TuneStep Start(TuneGoal goal, string? gpuName = null)
    {
        var card = For(gpuName);
        return goal switch
        {
            TuneGoal.Undervolt => new(goal, card.UndervoltMhz, 0, card.UndervoltMv, 100),
            TuneGoal.Overclock => new(goal, 100, 200, 0, 0), // 0 = power limit slider to its maximum
            _ => new(goal, 0, 0, 0, 100)
        };
    }

    // The next try after a result: undervolt walks the voltage down at the same clock until it fails, then goes back
    // one step; overclock walks the offsets up until it fails, then keeps 70% of the last one that passed.
    public static TuneStep? Next(TuneStep tried, TuneVerdict verdict, TuneStep? lastStable, CardStart? card = null) => (tried.Goal, verdict) switch
    {
        (TuneGoal.Stock, _) => null,
        (_, TuneVerdict.TooHot) => tried.Goal == TuneGoal.Overclock
            ? (lastStable is null ? null : Keep(lastStable))
            : tried with { CoreMhz = tried.CoreMhz - 100 },             // undervolt too hot: lower clock at the same voltage
        (TuneGoal.Undervolt, TuneVerdict.Stable) => tried.VoltageMv - 25 >= 850 ? tried with { VoltageMv = tried.VoltageMv - 25 } : null,
        (TuneGoal.Undervolt, TuneVerdict.Crashed or TuneVerdict.Throttled) => lastStable ?? tried with { CoreMhz = tried.CoreMhz - 15 },
        (TuneGoal.Overclock, TuneVerdict.Stable) => tried.CoreMhz + 50 <= (card?.OcCoreMax ?? 400) ? tried with { CoreMhz = tried.CoreMhz + 50, MemoryMhz = Math.Min(tried.MemoryMhz + 100, card?.OcMemMax ?? 1000) } : Keep(tried),
        (TuneGoal.Overclock, TuneVerdict.Crashed or TuneVerdict.Throttled) => lastStable is null ? Start(TuneGoal.Stock) : Keep(lastStable),
        _ => null
    };

    // The value to keep after the search: 70% of the offsets (rounded to 5 MHz). An undervolt is kept as found.
    public static TuneStep Keep(TuneStep best) => best.Goal != TuneGoal.Overclock ? best
        : best with { CoreMhz = Round5(best.CoreMhz * SafetyShare), MemoryMhz = Round5(best.MemoryMhz * SafetyShare) };

    private static int Round5(double v) => (int)(Math.Floor(v / 5) * 5);

    // Verdict of one test run. A GPU driver reset during the test is a crash; above 83 °C the test was stopped;
    // a clock that keeps falling far below its average means the card is hitting its power or heat limit.
    public static TuneResult Judge(TuneStep tried, IReadOnlyList<TuneSample> samples, bool driverReset, TuneStep? lastStable, CardStart? card = null)
    {
        if (driverReset) return new(TuneVerdict.Crashed, Max(samples, s => s.TempC), Avg(samples), Spread(samples), Next(tried, TuneVerdict.Crashed, lastStable, card));
        if (samples.Count < 5) return new(TuneVerdict.NoData, 0, 0, 0, null);
        var maxTemp = Max(samples, s => s.TempC);
        if (maxTemp > StopTempC) return new(TuneVerdict.TooHot, maxTemp, Avg(samples), Spread(samples), Next(tried, TuneVerdict.TooHot, lastStable, card));
        var spread = Spread(samples);
        var verdict = spread > 150 ? TuneVerdict.Throttled : TuneVerdict.Stable;
        return new(verdict, maxTemp, Avg(samples), spread, Next(tried, verdict, verdict == TuneVerdict.Stable ? tried : lastStable, card));
    }

    private static double Max(IReadOnlyList<TuneSample> s, Func<TuneSample, double> f) => s.Count == 0 ? 0 : s.Max(f);
    private static double Avg(IReadOnlyList<TuneSample> s) => s.Count == 0 ? 0 : s.Average(x => x.CoreMhz);
    // Spread of the core clock under load, ignoring the first 10% (ramp-up): p95 - p5.
    private static double Spread(IReadOnlyList<TuneSample> s)
    {
        if (s.Count < 5) return 0;
        var clocks = s.Skip(s.Count / 10).Select(x => x.CoreMhz).Order().ToArray();
        return clocks[(int)(clocks.Length * 0.95) - 1 < 0 ? 0 : (int)(clocks.Length * 0.95) - 1] - clocks[(int)(clocks.Length * 0.05)];
    }
}
