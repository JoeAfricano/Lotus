namespace Lotus.Core.Display;

// Driver presets are preferences, not a measured calibration or an ICC replacement.
public sealed record ColorRange(int Current, int Minimum, int Maximum, int Default)
{
    public bool Valid => Minimum >= -4096 && Maximum <= 4096 && Minimum < Maximum &&
        Current >= Minimum && Current <= Maximum && Default >= Minimum && Default < Maximum;
    public int Target(bool vivid) => vivid ? Default + (int)Math.Round((Maximum - Default) * .2) : Default;
    public int Percent(int value) => (int)Math.Round(100d * (value - Minimum) / (Maximum - Minimum));
}

public sealed record MonitorColorState(string Identity, string Device, string Name, bool? AdvancedColor, ColorRange? Vibrance)
{
    public string Label => $"{Name} · {Device}";
    public bool CanAdjust => Identity.Length > 0 && AdvancedColor == false && Vibrance is { Valid: true };
    public bool SameSetting(MonitorColorState other) => Identity == other.Identity && Device == other.Device &&
        AdvancedColor == other.AdvancedColor && Vibrance == other.Vibrance;
}

public interface IMonitorColors
{
    IReadOnlyList<MonitorColorState> Displays();
    MonitorColorState? Read(string identity);
    // Re-read identity, capabilities and current value before writing; verify after writing.
    bool TrySet(MonitorColorState expected, int value);
}

public sealed record MonitorColorChange(string Identity, int Original, int Applied, int Revert, int? PriorApplied, bool Preview);
public interface IMonitorColorStore
{
    IReadOnlyList<MonitorColorChange> Load();
    void Save(IReadOnlyList<MonitorColorChange> changes);
}

public enum MonitorColorResult { Preview, Kept, Restored, NoChange, Unsupported, AdvancedColor, Conflict, StorageError, DriverError, Pending, Invalid }

// One bounded preview at a time, durable rollback BEFORE the first driver write. Confirmed settings are never
// silently reapplied at startup. Original values remain available across application restarts.
public sealed class MonitorColorController : IDisposable
{
    private readonly IMonitorColors driver;
    private readonly IMonitorColorStore store;
    private readonly object gate = new();
    private readonly Action<Exception>? log;
    private readonly Func<Action, IDisposable> schedule;
    private List<MonitorColorChange> changes = [];
    private bool healthy = true, disposed;
    private IDisposable? timer;
    public event EventHandler? Changed;
    public MonitorColorResult LastResult { get; private set; } = MonitorColorResult.NoChange;
    public DateTimeOffset? PreviewUntil { get; private set; }
    public bool HasPreview { get { lock (gate) return changes.Any(c => c.Preview); } }
    public bool HasBackup(string identity) { lock (gate) return changes.Any(c => c.Identity == identity); }
    public MonitorColorController(IMonitorColors driver, IMonitorColorStore store, Action<Exception>? log = null,
        Func<Action, IDisposable>? schedule = null)
    {
        this.driver = driver; this.store = store; this.log = log;
        this.schedule = schedule ?? (action => new Timer(_ => action(), null, TimeSpan.FromSeconds(15), Timeout.InfiniteTimeSpan));
        try
        {
            changes = store.Load().ToList();
            if (changes.Count > 16 || changes.Any(c => c is null) || changes.Select(c => c.Identity).Distinct().Count() != changes.Count ||
                changes.Count(c => c.Preview) > 1 ||
                changes.Any(c => string.IsNullOrWhiteSpace(c.Identity) || c.Identity.Length > 1024 || Math.Abs((long)c.Original) > 4096 ||
                    Math.Abs((long)c.Applied) > 4096 || Math.Abs((long)c.Revert) > 4096 ||
                    (c.PriorApplied is int prior && Math.Abs((long)prior) > 4096))) throw new IOException("Invalid monitor color recovery data.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { changes = []; healthy = false; LastResult = MonitorColorResult.StorageError; log?.Invoke(ex); }
    }
    private MonitorColorResult Run(Func<MonitorColorResult> operation)
    {
        MonitorColorResult result;
        lock (gate)
        {
            try { result = !healthy ? MonitorColorResult.StorageError : operation(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { log?.Invoke(ex); result = MonitorColorResult.StorageError; }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.Runtime.InteropServices.ExternalException or InvalidOperationException)
            { log?.Invoke(ex); result = MonitorColorResult.DriverError; }
            LastResult = result;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }
    private void Save(List<MonitorColorChange> value) { store.Save(value); changes = value; }
    private static MonitorColorResult? Block(MonitorColorState? state) => state is null ? MonitorColorResult.Pending :
        state.AdvancedColor != false ? MonitorColorResult.AdvancedColor : !state.CanAdjust ? MonitorColorResult.Unsupported : null;

    public MonitorColorResult Begin(MonitorColorState expected, bool vivid) => BeginCustom(expected, expected.Vibrance?.Target(vivid) ?? int.MinValue);
    public MonitorColorResult BeginCustom(MonitorColorState expected, int target) => Run(() =>
    {
        if (disposed || HasPreview) return MonitorColorResult.Invalid;
        var current = driver.Read(expected.Identity);
        if (Block(current) is { } blocked) return blocked;
        if (!current!.SameSetting(expected)) return MonitorColorResult.Conflict;
        var prior = changes.SingleOrDefault(c => c.Identity == expected.Identity);
        if (prior is not null && current.Vibrance!.Current != prior.Applied) return MonitorColorResult.Conflict;
        if (target < current.Vibrance!.Minimum || target > current.Vibrance.Maximum) return MonitorColorResult.Invalid;
        if (target == current.Vibrance.Current) return MonitorColorResult.NoChange;
        if (changes.Count >= 16 && prior is null) return MonitorColorResult.Invalid;
        var entry = new MonitorColorChange(expected.Identity, prior?.Original ?? current.Vibrance.Current,
            target, current.Vibrance.Current, prior?.Applied, true);
        Save(changes.Where(c => c.Identity != entry.Identity).Append(entry).ToList());
        // Schedule before driver invocation, so a failed driver call cannot leave an unbounded preview.
        PreviewUntil = DateTimeOffset.UtcNow.AddSeconds(15);
        timer = schedule(() => CancelPreview());
        if (!driver.TrySet(current, target))
        {
            var restored = EndPreview(entry);
            return restored is MonitorColorResult.Restored ? MonitorColorResult.DriverError : restored;
        }
        return MonitorColorResult.Preview;
    });
    public MonitorColorResult Keep() => Run(() =>
    {
        var entry = changes.SingleOrDefault(c => c.Preview);
        if (entry is null) return MonitorColorResult.NoChange;
        var current = driver.Read(entry.Identity);
        if (Block(current) is { } blocked) return blocked;
        if (current!.Vibrance!.Current != entry.Applied) return MonitorColorResult.Conflict;
        Save(changes.Select(c => c == entry ? c with { Preview = false, Revert = c.Original, PriorApplied = null } : c).ToList());
        StopTimer(); return MonitorColorResult.Kept;
    });
    public MonitorColorResult CancelPreview() => Run(() =>
    {
        var entries = changes.Where(c => c.Preview).ToArray();
        if (entries.Length == 0) return MonitorColorResult.NoChange;
        foreach (var entry in entries)
        {
            var result = EndPreview(entry);
            if (result != MonitorColorResult.Restored) return result;
        }
        return MonitorColorResult.Restored;
    });
    private MonitorColorResult EndPreview(MonitorColorChange entry)
    {
        var state = driver.Read(entry.Identity);
        if (Block(state) is { } blocked) return blocked;
        var value = state!.Vibrance!.Current;
        if (value != entry.Applied && value != entry.Revert) return MonitorColorResult.Conflict;
        if (value != entry.Revert && !driver.TrySet(state, entry.Revert)) return MonitorColorResult.DriverError;
        var next = changes.Where(c => c.Identity != entry.Identity).ToList();
        if (entry.PriorApplied is int prior) next.Add(entry with { Applied = prior, Revert = entry.Original, PriorApplied = null, Preview = false });
        Save(next); StopTimer(); return MonitorColorResult.Restored;
    }
    public MonitorColorResult Restore(string identity, MonitorColorState? authorizedCurrent = null) => Run(() =>
    {
        if (HasPreview && authorizedCurrent is null) return MonitorColorResult.Invalid;
        if (changes.Any(c => c.Preview && c.Identity != identity)) return MonitorColorResult.Invalid;
        var entry = changes.SingleOrDefault(c => c.Identity == identity);
        if (entry is null) return MonitorColorResult.NoChange;
        var state = driver.Read(identity);
        if (Block(state) is { } blocked) return blocked;
        var currentValue = state!.Vibrance!.Current;
        if (authorizedCurrent is not null && !state!.SameSetting(authorizedCurrent)) return MonitorColorResult.Conflict;
        if (authorizedCurrent is null && currentValue != entry.Applied && currentValue != entry.Original) return MonitorColorResult.Conflict;
        if (currentValue != entry.Original && !driver.TrySet(state, entry.Original)) return MonitorColorResult.DriverError;
        Save(changes.Where(c => c != entry).ToList()); StopTimer(); return MonitorColorResult.Restored;
    });
    private void StopTimer() { timer?.Dispose(); timer = null; PreviewUntil = null; }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; }
        CancelPreview(); lock (gate) StopTimer();
    }
}
