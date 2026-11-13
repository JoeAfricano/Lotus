namespace Lotus.Core.Telemetry;

// "Ajustar automaticamente" (Ajuste da placa): Lotus drives MSI Afterburner through its documented control memory, so
// every value stays inside Afterburner's own limits and Afterburner applies it. Each step is load-tested (temperature,
// driver reset, clock spread) and scored; the original state is journaled before the first change and put back at the
// end, on cancel, or at the next start after a crash.

// A setting for Afterburner. FlatMv/FlatMhz: the whole V/F curve shifted so FlatMv reaches FlatMhz and flat above it
// (the classic curve-editor undervolt). Without them, CoreOffsetMhz shifts the whole curve (plain overclock).
public sealed record GpuSetting(int CoreOffsetMhz, int MemOffsetMhz, int? FlatMv = null, int? FlatMhz = null)
{
    public static readonly GpuSetting Stock = new(0, 0);
    public override string ToString() => FlatMv is { } mv ? $"{mv} mV @ {FlatMhz} MHz, mem +{MemOffsetMhz}" : $"núcleo {CoreOffsetMhz:+0;-0;0} MHz, mem {MemOffsetMhz:+0;-0;0} MHz";
}

public interface IGpuControl
{
    bool Available { get; }                 // Afterburner open with its control memory, NVIDIA card
    (int CoreMaxMhz, int MemMaxMhz) Limits { get; }
    bool SaveOriginal();                    // journal the current state (once per session)
    bool Apply(GpuSetting setting);         // true when Afterburner applied it
    bool RestoreOriginal();                 // back to the journaled state, journal cleared
    bool HasPendingOriginal { get; }        // a journal left by a session that did not finish
}

public interface IGpuLoadTest
{
    // Load for the given time; sensor samples every 2 s, driver reset flag and a performance score (higher = faster).
    Task<(IReadOnlyList<TuneSample> Samples, bool DriverReset, double Score)> RunAsync(TimeSpan length, IProgress<double>? progress, CancellationToken token);
}

public sealed record AutoStep(string Mode, GpuSetting Setting, TuneVerdict Verdict, double Score, double MaxTempC, double AvgCoreMhz, double AvgPowerW)
{
    public bool Ok => Verdict is TuneVerdict.Stable or TuneVerdict.Throttled; // throttled = held back by power/heat, not unstable
}

public sealed record AutoTuneReport(AutoStep? Baseline, AutoStep? Undervolt, AutoStep? Overclock, AutoStep? Combined, IReadOnlyList<AutoStep> Steps, bool Cancelled, string? Stopped = null);

[Flags] public enum AutoModes { Undervolt = 1, Overclock = 2, Combined = 4, All = 7 }

public sealed class GpuAutoTuner(IGpuControl control, IGpuLoadTest test, GpuTuning.CardStart card, Action<AutoStep>? onStep = null, Action<string>? onStatus = null)
{
    public TimeSpan StepLength { get; init; } = TimeSpan.FromMinutes(3);
    public TimeSpan Settle { get; init; } = TimeSpan.FromSeconds(1.5); // clocks settle after each change
    public const int FloorMv = 850, UvStepMv = 25, CoreStepMhz = 50;
    private static readonly int[] MemorySteps = [500, 750, 1000];
    private readonly List<AutoStep> steps = [];

    public async Task<AutoTuneReport> RunAsync(AutoModes modes, CancellationToken token)
    {
        if (!control.Available) return new(null, null, null, null, [], false, "unavailable");
        if (!control.SaveOriginal()) return new(null, null, null, null, [], false, "journal");
        AutoStep? baseline = null, uv = null, oc = null, combo = null;
        try
        {
            onStatus?.Invoke("baseline");
            baseline = await Step("baseline", GpuSetting.Stock, token);
            if (baseline is not { Ok: true }) return new(baseline, null, null, null, steps, false, "baseline");

            if (modes.HasFlag(AutoModes.Undervolt))
            {
                onStatus?.Invoke("undervolt");
                uv = await Undervolt(card.UndervoltMhz, card.UndervoltMv, 0, token);
            }
            if (modes.HasFlag(AutoModes.Overclock) || modes.HasFlag(AutoModes.Combined))
            {
                onStatus?.Invoke("overclock");
                oc = await Overclock(baseline.Score, token);
            }
            if (modes.HasFlag(AutoModes.Combined) && oc is { Ok: true } best && best.AvgCoreMhz > 0)
            {
                // Keep the clock the overclock reached, with the lowest voltage that holds it.
                onStatus?.Invoke("combined");
                combo = await Undervolt((int)Math.Round(best.AvgCoreMhz / 15) * 15, 1000, best.Setting.MemOffsetMhz, token);
            }
            return new(baseline, uv, modes.HasFlag(AutoModes.Overclock) ? oc : null, combo, steps, false);
        }
        catch (OperationCanceledException) { return new(baseline, uv, oc, combo, steps, true); }
        finally { control.RestoreOriginal(); } // the card goes back as it was; the player chooses what to keep
    }

    private async Task<AutoStep?> Step(string mode, GpuSetting setting, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!control.Apply(setting)) return null;
        await Task.Delay(Settle, token);
        var (samples, reset, score) = await test.RunAsync(StepLength, null, token);
        var r = GpuTuning.Judge(new(TuneGoal.Overclock, setting.CoreOffsetMhz, setting.MemOffsetMhz, setting.FlatMv ?? 0, 0), samples, reset, null, card);
        var step = new AutoStep(mode, setting, r.Verdict, score, r.MaxTempC, r.AvgCoreMhz, samples.Count == 0 ? 0 : samples.Average(s => s.PowerW));
        steps.Add(step); onStep?.Invoke(step);
        if (reset) { control.Apply(GpuSetting.Stock); await Task.Delay(Settle * 2, token); } // after a driver reset: back to stock before the next try
        return step;
    }

    // Flat curve at mhz, starting at startMv and going 25 mV lower while it stays stable.
    private async Task<AutoStep?> Undervolt(int mhz, int startMv, int memOffset, CancellationToken token)
    {
        AutoStep? lastGood = null;
        for (var mv = startMv; mv >= FloorMv; mv -= UvStepMv)
        {
            var s = await Step(memOffset == 0 ? "undervolt" : "combined", new(0, memOffset, mv, mhz), token);
            if (s is not { Ok: true }) break;
            lastGood = s;
        }
        return lastGood;
    }

    // Memory first (GDDR7 corrects errors silently: keep a step only when the score goes up), then the core in
    // 50 MHz steps up to the family ceiling; the kept value is 70% of the last stable one.
    private async Task<AutoStep?> Overclock(double baseScore, CancellationToken token)
    {
        int mem = 0; var score = baseScore;
        foreach (var m in MemorySteps.Where(m => m <= Math.Min(card.OcMemMax, control.Limits.MemMaxMhz)))
        {
            var s = await Step("memory", new(0, m), token);
            if (s is not { Ok: true } || s.Score < score * 1.003) break;
            mem = m; score = s.Score;
        }
        AutoStep? lastGood = null;
        for (var core = 100; core <= Math.Min(card.OcCoreMax, control.Limits.CoreMaxMhz); core += CoreStepMhz)
        {
            var s = await Step("core", new(core, mem), token);
            if (s is not { Ok: true }) break;
            lastGood = s;
        }
        if (lastGood is null) return null;
        var kept = new GpuSetting((int)(Math.Floor(lastGood.Setting.CoreOffsetMhz * GpuTuning.SafetyShare / 5) * 5), mem);
        return await Step("overclock", kept, token); // confirm the safer value
    }
}
