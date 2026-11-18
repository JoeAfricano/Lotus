namespace Lotus.Core.Display;

public sealed record MonitorLevel(int Current, int Minimum, int Maximum)
{
    public bool Valid => Minimum >= 0 && Maximum <= 65535 && Minimum < Maximum && Current >= Minimum && Current <= Maximum;
    public double Percent => 100d * (Current - Minimum) / (Maximum - Minimum);
    public MonitorLevel At(double percent) => this with { Current = Minimum + (int)Math.Round((Maximum - Minimum) * percent / 100) };
}
public sealed record CalibrationState(string Identity, string Device, string Name, bool? AdvancedColor,
    MonitorLevel? Brightness, MonitorLevel? Contrast, ushort[]? Ramp, ColorRange? Vibrance = null)
{
    public string Label => $"{Name} · {Device}";
    public bool Valid => Identity?.Length is > 0 and <= 1024 && Device?.Length is > 0 and <= 64 && (Brightness is null or { Valid: true }) &&
        (Contrast is null or { Valid: true }) && (Vibrance is null or { Valid: true }) && (Ramp is null || ValidRamp(Ramp));
    public static bool ValidRamp(ushort[] ramp)
    {
        if (ramp.Length != 768) return false;
        for (var c = 0; c < 3; c++)
        {
            if (ramp[c * 256 + 255] - ramp[c * 256] < 16384) return false;
            for (var i = 1; i < 256; i++) if (ramp[c * 256 + i] < ramp[c * 256 + i - 1]) return false;
        }
        return true;
    }
    public bool CanAdjust => Valid && AdvancedColor == false && (Brightness is not null || Contrast is not null || Ramp is not null || Vibrance is not null);
    public bool Same(CalibrationState other, int rampTolerance = 0) => Identity == other.Identity && Device == other.Device &&
        AdvancedColor == other.AdvancedColor && Brightness == other.Brightness && Contrast == other.Contrast && Vibrance == other.Vibrance && RampEqual(Ramp, other.Ramp, rampTolerance);
    public static bool RampEqual(ushort[]? a, ushort[]? b, int tolerance = 0) => a is null ? b is null :
        b is { Length: 768 } && a.Length == 768 && a.Zip(b).All(p => Math.Abs((int)p.First - p.Second) <= tolerance);
    // Accept a partially applied transaction only when EVERY component is one of our two recorded states.
    public bool Between(CalibrationState a, CalibrationState b, CalibrationState? c = null) => Identity == a.Identity && Identity == b.Identity &&
        Device == a.Device && Device == b.Device && AdvancedColor == false &&
        (c is null || c.Identity == Identity && c.Device == Device) &&
        (Brightness == a.Brightness || Brightness == b.Brightness || c is not null && Brightness == c.Brightness) &&
        (Contrast == a.Contrast || Contrast == b.Contrast || c is not null && Contrast == c.Contrast) &&
        (Vibrance == a.Vibrance || Vibrance == b.Vibrance || c is not null && Vibrance == c.Vibrance) &&
        (RampEqual(Ramp, a.Ramp, 256) || RampEqual(Ramp, b.Ramp, 256) || c is not null && RampEqual(Ramp, c.Ramp, 256));
}
public sealed record CalibrationProfile(double? Brightness = null, double? Contrast = null, double Gamma = 1,
    double DigitalBrightness = 0, double DigitalContrast = 1, double Warmth = 0, double Tint = 0, double? Saturation = null)
{
    public bool Valid => (Brightness is null || double.IsFinite(Brightness.Value) && Brightness is >= 0 and <= 100) &&
        (Contrast is null || double.IsFinite(Contrast.Value) && Contrast is >= 0 and <= 100) &&
        double.IsFinite(Gamma) && Gamma is >= .75 and <= 1.3 && double.IsFinite(DigitalBrightness) && DigitalBrightness is >= -.1 and <= .1 &&
        double.IsFinite(DigitalContrast) && DigitalContrast is >= .8 and <= 1.2 && double.IsFinite(Warmth) && Warmth is >= -20 and <= 20 &&
        double.IsFinite(Tint) && Tint is >= -10 and <= 10 &&
        (Saturation is null || double.IsFinite(Saturation.Value) && Saturation is >= 0 and <= 100);
    public CalibrationState Build(CalibrationState baseline)
    {
        if (!Valid || !baseline.CanAdjust) throw new ArgumentException("Unsupported display calibration profile.");
        if (baseline.Ramp is null && (Gamma != 1 || DigitalBrightness != 0 || DigitalContrast != 1 || Warmth != 0 || Tint != 0))
            throw new ArgumentException("Gamma controls unavailable.");
        if (Brightness is not null && baseline.Brightness is null || Contrast is not null && baseline.Contrast is null)
            throw new ArgumentException("Physical controls unavailable.");
        if (Saturation is not null && baseline.Vibrance is null) throw new ArgumentException("Driver saturation unavailable.");
        ushort[]? ramp = baseline.Ramp?.ToArray();
        if (ramp is not null)
        {
            double[] gains = [1 + Warmth * .003, 1 + Tint * .004, 1 - Warmth * .003];
            for (var channel = 0; channel < 3; channel++)
            for (var i = 0; i < 256; i++)
            {
                var x = Math.Clamp((Math.Pow(i / 255d, 1 / Gamma) - .5) * DigitalContrast + .5 + DigitalBrightness, 0, 1) * 255;
                var lo = (int)Math.Floor(x); var hi = Math.Min(255, lo + 1); var f = x - lo;
                var y = baseline.Ramp![channel * 256 + lo] * (1 - f) + baseline.Ramp[channel * 256 + hi] * f;
                ramp[channel * 256 + i] = (ushort)Math.Clamp(Math.Round(y * gains[channel]), 0, 65535);
            }
        }
        return baseline with { Brightness = Brightness is double br ? baseline.Brightness!.At(br) : baseline.Brightness,
            Contrast = Contrast is double co ? baseline.Contrast!.At(co) : baseline.Contrast, Ramp = ramp,
            Vibrance = Saturation is double sat && baseline.Vibrance is { } range
                ? range with { Current = range.Minimum + (int)Math.Round((range.Maximum - range.Minimum) * sat / 100) } : baseline.Vibrance };
    }
}
public interface IDisplayCalibration
{
    IReadOnlyList<CalibrationState> Displays();
    CalibrationState? Read(string identity);
    bool TryApply(CalibrationState expected, CalibrationState target);
}
public sealed record CalibrationBackup(CalibrationState Original, CalibrationState Applied, CalibrationState Revert,
    CalibrationState? PriorApplied, CalibrationProfile Profile, CalibrationProfile? PriorProfile, bool Preview, CalibrationState? TransitionFrom = null);
public interface ICalibrationStore
{
    IReadOnlyList<CalibrationBackup> Load();
    void Save(IReadOnlyList<CalibrationBackup> entries);
}

public sealed class DisplayCalibrationController : IDisposable
{
    private readonly IDisplayCalibration driver;
    private readonly ICalibrationStore store;
    private readonly object gate = new();
    private readonly Func<TimeSpan, Action, IDisposable> schedule;
    private readonly Action<Exception>? log;
    private readonly Func<DateTimeOffset> now;
    private List<CalibrationBackup> entries = [];
    // UI reads must never wait for the transaction lock while a slow DDC/CI request is running.
    private CalibrationBackup[] publishedEntries = [];
    private IDisposable? timer;
    private long timerRevision;
    private bool healthy = true, disposed;
    private DateTimeOffset sessionEnd;
    public DateTimeOffset? PreviewUntil { get; private set; }
    public MonitorColorResult LastResult { get; private set; } = MonitorColorResult.NoChange;
    public event EventHandler? Changed;
    public bool HasPreview => Volatile.Read(ref publishedEntries).Any(e => e.Preview);
    public bool PreviewMatchesOriginal => Volatile.Read(ref publishedEntries).FirstOrDefault(e => e.Preview) is { } entry && entry.Applied.Same(entry.Original);
    public bool HasBackup(string id) => Volatile.Read(ref publishedEntries).Any(e => e.Original.Identity == id);
    public CalibrationProfile? ProfileFor(string id) => Volatile.Read(ref publishedEntries).FirstOrDefault(e => e.Original.Identity == id)?.Profile;
    public CalibrationState? OriginalFor(string id) => Volatile.Read(ref publishedEntries).FirstOrDefault(e => e.Original.Identity == id)?.Original;
    public DisplayCalibrationController(IDisplayCalibration driver, ICalibrationStore store, Action<Exception>? log = null,
        Func<TimeSpan, Action, IDisposable>? schedule = null, Func<DateTimeOffset>? utcNow = null)
    {
        this.driver = driver; this.store = store; this.log = log;
        now = utcNow ?? (() => DateTimeOffset.UtcNow);
        this.schedule = schedule ?? ((delay, action) => new Timer(_ => action(), null, delay, Timeout.InfiniteTimeSpan));
        try
        {
            entries = store.Load().ToList();
            if (entries.Count > 8 || entries.Any(e => e is null || e.Original is null || e.Applied is null || e.Revert is null ||
                e.Profile is null || !e.Profile.Valid || !e.Original.Valid || !e.Applied.Valid || !e.Revert.Valid ||
                e.Profile.Saturation is not null && e.Original.Vibrance is null ||
                e.Original.Identity != e.Applied.Identity || e.Original.Identity != e.Revert.Identity ||
                e.PriorProfile is { Valid: false } || e.TransitionFrom is { } transition && (!transition.Valid || transition.Identity != e.Original.Identity) ||
                e.PriorApplied is { } prior && (!prior.Valid || prior.Identity != e.Original.Identity)) ||
                entries.Count(e => e.Preview) > 1 || entries.Select(e => e.Original.Identity).Distinct().Count() != entries.Count)
                throw new IOException("Invalid display calibration recovery data.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { healthy = false; entries = []; LastResult = MonitorColorResult.StorageError; log?.Invoke(ex); }
        Volatile.Write(ref publishedEntries, entries.ToArray());
    }
    private MonitorColorResult Run(Func<MonitorColorResult> action)
    {
        MonitorColorResult result;
        lock (gate)
        {
            try { result = !healthy ? MonitorColorResult.StorageError : action(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { log?.Invoke(ex); result = MonitorColorResult.StorageError; }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
            { log?.Invoke(ex); result = MonitorColorResult.DriverError; }
            LastResult = result;
        }
        Changed?.Invoke(this, EventArgs.Empty); return result;
    }
    private void Save(List<CalibrationBackup> value)
    { store.Save(value); entries = value; Volatile.Write(ref publishedEntries, value.ToArray()); }
    private CalibrationBackup UpgradeLegacy(CalibrationBackup entry, CalibrationState current)
    {
        // Old journals did not record driver vibrance. Preserve the newly observed value in ALL
        // rollback states; never guess an old saturation value or erase an external setting.
        if (current.Vibrance is null || entry.Original.Vibrance is not null || entry.Applied.Vibrance is not null || entry.Revert.Vibrance is not null ||
            entry.Profile.Saturation is not null || entry.PriorProfile?.Saturation is not null || entry.PriorApplied?.Vibrance is not null || entry.TransitionFrom?.Vibrance is not null) return entry;
        CalibrationState Extend(CalibrationState state) => state with { Vibrance = current.Vibrance };
        var updated = entry with { Original = Extend(entry.Original), Applied = Extend(entry.Applied), Revert = Extend(entry.Revert),
            PriorApplied = entry.PriorApplied is { } prior ? Extend(prior) : null, TransitionFrom = entry.TransitionFrom is { } transition ? Extend(transition) : null };
        Save(entries.Select(e => e == entry ? updated : e).ToList()); return updated;
    }
    private static MonitorColorResult? Block(CalibrationState? state) => state is null ? MonitorColorResult.Pending :
        state.AdvancedColor != false ? MonitorColorResult.AdvancedColor : !state.CanAdjust ? MonitorColorResult.Unsupported : null;
    private void Arm()
    {
        timer?.Dispose(); var revision = ++timerRevision;
        PreviewUntil = now().AddMinutes(2);
        if (PreviewUntil > sessionEnd) PreviewUntil = sessionEnd;
        var delay = PreviewUntil.Value - now();
        timer = schedule(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, () =>
        { lock (gate) { if (revision == timerRevision) CancelPreview(); } });
    }
    private void StopTimer() { ++timerRevision; timer?.Dispose(); timer = null; PreviewUntil = null; }
    public MonitorColorResult Begin(CalibrationState expected, CalibrationProfile profile) => Run(() =>
    {
        if (disposed || HasPreview || !profile.Valid) return MonitorColorResult.Invalid;
        var current = driver.Read(expected.Identity); if (Block(current) is { } block) return block;
        if (!current!.Same(expected)) return MonitorColorResult.Conflict;
        var prior = entries.SingleOrDefault(e => e.Original.Identity == expected.Identity);
        if (prior is not null) prior = UpgradeLegacy(prior, current);
        if (prior is not null && !current.Same(prior.Applied, 256)) return MonitorColorResult.Conflict;
        if (entries.Count >= 8 && prior is null) return MonitorColorResult.Invalid;
        var original = prior is not null ? prior.Original with { Device = current.Device } : current;
        CalibrationState target; try { target = profile.Build(original); } catch (ArgumentException) { return MonitorColorResult.Unsupported; }
        var next = new CalibrationBackup(original, target, current, prior?.Applied, profile, prior?.Profile, true);
        Save(entries.Where(e => e.Original.Identity != expected.Identity).Append(next).ToList());
        sessionEnd = now().AddMinutes(10); Arm();
        if (!current.Same(target) && !driver.TryApply(current, target))
        { var recovery = End(next); return recovery == MonitorColorResult.Restored ? MonitorColorResult.DriverError : recovery; }
        return MonitorColorResult.Preview;
    });
    public MonitorColorResult Update(CalibrationProfile profile, bool compareOriginal = false) => Run(() =>
    {
        var previous = entries.SingleOrDefault(e => e.Preview);
        if (previous is null || disposed) return MonitorColorResult.NoChange;
        if (!profile.Valid) return MonitorColorResult.Invalid;
        var current = driver.Read(previous.Original.Identity); if (Block(current) is { } block) return block;
        if (!current!.Same(previous.Applied, 256)) return MonitorColorResult.Conflict;
        CalibrationState target;
        try { target = compareOriginal ? previous.Original : profile.Build(previous.Original); } catch (ArgumentException) { return MonitorColorResult.Unsupported; }
        if (previous.Profile == profile && previous.Applied.Same(target) && current.Same(target, 256))
        { Arm(); return MonitorColorResult.Preview; }
        var next = previous with { Applied = target, Profile = profile, TransitionFrom = previous.Applied };
        Save(entries.Select(e => e == previous ? next : e).ToList()); Arm();
        if (!current.Same(target) && !driver.TryApply(current, target))
        {
            // Include the immediately preceding state when recovering a partially applied live update.
            var partial = driver.Read(next.Original.Identity);
            if (partial is not null && partial.Between(previous.Applied, target) && partial.CanAdjust &&
                driver.TryApply(partial, next.Revert)) return Finish(next);
            return MonitorColorResult.DriverError;
        }
        return MonitorColorResult.Preview;
    });
    public MonitorColorResult Keep() => Run(() =>
    {
        var entry = entries.SingleOrDefault(e => e.Preview); if (entry is null) return MonitorColorResult.NoChange;
        var current = driver.Read(entry.Original.Identity); if (Block(current) is { } block) return block;
        if (!current!.Same(entry.Applied, 256)) return MonitorColorResult.Conflict;
        Save(entries.Select(e => e == entry ? e with { Preview = false, Revert = e.Original, PriorApplied = null, PriorProfile = null, TransitionFrom = null } : e).ToList());
        StopTimer(); return MonitorColorResult.Kept;
    });
    public MonitorColorResult CancelPreview() => Run(() => entries.FirstOrDefault(e => e.Preview) is { } entry ? End(entry) : MonitorColorResult.NoChange);
    private MonitorColorResult End(CalibrationBackup entry)
    {
        var current = driver.Read(entry.Original.Identity); if (Block(current) is { } block) return block;
        entry = UpgradeLegacy(entry, current!);
        if (!current!.Between(entry.Applied, entry.Revert, entry.TransitionFrom)) return MonitorColorResult.Conflict;
        if (!current.Same(entry.Revert, 256) && !driver.TryApply(current, entry.Revert)) return MonitorColorResult.DriverError;
        return Finish(entry);
    }
    private MonitorColorResult Finish(CalibrationBackup entry)
    {
        var value = entries.Where(e => e != entry).ToList();
        if (entry.PriorApplied is { } prior) value.Add(entry with { Applied = prior, Revert = entry.Original, PriorApplied = null,
            Profile = entry.PriorProfile ?? new(), PriorProfile = null, Preview = false, TransitionFrom = null });
        Save(value); StopTimer(); return MonitorColorResult.Restored;
    }
    public MonitorColorResult Restore(CalibrationState authorized) => Run(() =>
    {
        var entry = entries.SingleOrDefault(e => e.Original.Identity == authorized.Identity); if (entry is null) return MonitorColorResult.NoChange;
        if (entries.Any(e => e.Preview && e != entry)) return MonitorColorResult.Invalid;
        var current = driver.Read(authorized.Identity); if (Block(current) is { } block) return block;
        if (!current!.Same(authorized)) return MonitorColorResult.Conflict;
        entry = UpgradeLegacy(entry, current);
        var target = entry.Original with { Device = current.Device };
        if (!current.Same(target, 256) && !driver.TryApply(current, target)) return MonitorColorResult.DriverError;
        Save(entries.Where(e => e != entry).ToList()); StopTimer(); return MonitorColorResult.Restored;
    });
    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; } CancelPreview(); lock (gate) StopTimer(); }
}

/// <summary>
/// DDC/CI monitors often keep reporting the previous brightness/contrast for tens to hundreds of
/// milliseconds after a write, and a single read can fail transiently. Poll a bounded number of
/// times instead of treating the first stale read as a failed (and then rolled back) transaction.
/// </summary>
public static class DisplaySettle
{
    public static readonly TimeSpan WriteGap = TimeSpan.FromMilliseconds(60);
    public static readonly TimeSpan ReadStep = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan VerifyBudget = TimeSpan.FromMilliseconds(1500);
    public static readonly TimeSpan ExpectBudget = TimeSpan.FromMilliseconds(450);
    /// <summary>Reads until <paramref name="accept"/> holds or the budget is spent; returns the last read.</summary>
    public static CalibrationState? Until(Func<CalibrationState?> read, Func<CalibrationState, bool> accept, Action<TimeSpan> sleep, TimeSpan budget, TimeSpan step)
    {
        if (step <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(step));
        var attempts = Math.Max(1, (int)Math.Ceiling(budget / step) + 1);
        CalibrationState? last = null;
        for (var i = 0; i < attempts; i++)
        {
            if (i > 0) sleep(step);
            last = read();
            if (last is not null && accept(last)) return last;
        }
        return last;
    }
}
