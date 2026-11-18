using Lotus.Core.Display;
using Lotus.Infrastructure.Display;
using Xunit;

namespace Lotus.Tests;

public sealed class MonitorColorTests
{
    private static readonly MonitorColorState Original = new("monitor-stable-id", "display1", "Monitor", false, new(50, 0, 100, 50));
    private sealed class Driver : IMonitorColors
    {
        public MonitorColorState? State = Original;
        public readonly List<int> Writes = [];
        public bool Fail, Partial;
        public Action? BeforeWrite;
        public IReadOnlyList<MonitorColorState> Displays() => State is { } value ? [value] : [];
        public MonitorColorState? Read(string identity) => State?.Identity == identity ? State : null;
        public bool TrySet(MonitorColorState expected, int value)
        {
            BeforeWrite?.Invoke();
            Assert.NotNull(State); Assert.True(State.SameSetting(expected));
            Writes.Add(value);
            if (!Fail || Partial) State = State with { Vibrance = State.Vibrance! with { Current = value } };
            if (Partial) { Partial = false; Fail = false; return false; }
            return !Fail;
        }
    }
    private sealed class Store : IMonitorColorStore
    {
        public IReadOnlyList<MonitorColorChange> Value = [];
        public bool FailRead, FailWrite;
        public IReadOnlyList<MonitorColorChange> Load() => FailRead ? throw new IOException("read blocked") : Value;
        public void Save(IReadOnlyList<MonitorColorChange> changes) { if (FailWrite) throw new IOException("write blocked"); Value = changes.ToArray(); }
    }
    private sealed class Clock : IDisposable
    {
        public Action? Callback;
        public bool Disposed;
        public IDisposable Schedule(Action callback) { Callback = callback; Disposed = false; return this; }
        public void Dispose() => Disposed = true;
    }
    private static MonitorColorController Controller(Driver driver, Store store, Clock? clock = null) =>
        new(driver, store, schedule: (clock ?? new()).Schedule);

    [Theory]
    [InlineData(false, 50)] [InlineData(true, 60)]
    public void ProfilesUseReportedNeutralAndModerateIncrease(bool vivid, int expected) => Assert.Equal(expected, Original.Vibrance!.Target(vivid));
    [Fact] public void RangeComesFromDriver_NotAssumedToBe0To100()
    {
        var range = new ColorRange(0, -50, 50, 0);
        Assert.Equal(10, range.Target(true)); Assert.Equal(50, range.Percent(range.Default));
    }
    [Fact] public void JournalIsDurableBeforeTheFirstWrite()
    {
        var driver = new Driver(); var store = new Store(); using var session = Controller(driver, store);
        driver.BeforeWrite = () => Assert.Single(store.Value, c => c.Preview);
        Assert.Equal(MonitorColorResult.Preview, session.Begin(Original, true));
        Assert.Equal(60, driver.State!.Vibrance!.Current);
    }
    [Fact] public void PreviewTimeoutRestoresAndClearsJournal()
    {
        var driver = new Driver(); var store = new Store(); var clock = new Clock(); using var session = Controller(driver, store, clock);
        session.Begin(Original, true); clock.Callback!();
        Assert.Equal(50, driver.State!.Vibrance!.Current); Assert.Empty(store.Value); Assert.False(session.HasPreview); Assert.True(clock.Disposed);
    }
    [Fact] public void KeepCancelsTimeoutAndOriginalCanBeRestored()
    {
        var driver = new Driver(); var store = new Store(); var clock = new Clock(); using var session = Controller(driver, store, clock);
        session.Begin(Original, true); Assert.Equal(MonitorColorResult.Kept, session.Keep());
        clock.Callback!(); Assert.Equal(60, driver.State!.Vibrance!.Current); Assert.False(store.Value.Single().Preview);
        Assert.Equal(MonitorColorResult.Restored, session.Restore(Original.Identity)); Assert.Equal(50, driver.State.Vibrance.Current); Assert.Empty(store.Value);
    }
    [Fact] public void SecondPreviewRestoresKeptValueOnCancel_OriginalSurvives()
    {
        var driver = new Driver { State = Original with { Vibrance = Original.Vibrance! with { Current = 55 } } }; var store = new Store(); using var session = Controller(driver, store);
        session.Begin(driver.State, true); session.Keep();
        session.Begin(driver.State!, false); session.CancelPreview();
        Assert.Equal(60, driver.State!.Vibrance!.Current); Assert.Equal(55, store.Value.Single().Original);
        session.Restore(Original.Identity); Assert.Equal(55, driver.State.Vibrance.Current);
    }
    [Fact] public void RepeatedConfirmedAdjustmentsRetainFirstOriginal()
    {
        var driver = new Driver { State = Original with { Vibrance = Original.Vibrance! with { Current = 70 } } }; var store = new Store(); using var session = Controller(driver, store);
        session.Begin(driver.State, false); session.Keep(); session.Begin(driver.State!, true); session.Keep();
        Assert.Equal(70, store.Value.Single().Original); session.Restore(Original.Identity); Assert.Equal(70, driver.State!.Vibrance!.Current);
    }
    [Fact] public void BackupWriteFailureNeverTouchesDriver()
    {
        var driver = new Driver(); var store = new Store { FailWrite = true }; using var session = Controller(driver, store);
        Assert.Equal(MonitorColorResult.StorageError, session.Begin(Original, true)); Assert.Empty(driver.Writes);
    }
    [Fact] public void BackupReadFailureNeverTouchesDriver()
    {
        var driver = new Driver(); using var session = Controller(driver, new Store { FailRead = true });
        Assert.Equal(MonitorColorResult.StorageError, session.Begin(Original, true)); Assert.Empty(driver.Writes);
    }
    [Theory] [InlineData(true)] [InlineData(null)]
    public void HdrOrUnknownColorPipelineBlocksMutation(bool? advanced)
    {
        var driver = new Driver { State = Original with { AdvancedColor = advanced } }; using var session = Controller(driver, new());
        Assert.Equal(MonitorColorResult.AdvancedColor, session.Begin(driver.State, true)); Assert.Empty(driver.Writes);
    }
    [Fact] public void MissingNvidiaControlBlocksMutation()
    {
        var driver = new Driver { State = Original with { Vibrance = null } }; using var session = Controller(driver, new());
        Assert.Equal(MonitorColorResult.Unsupported, session.Begin(driver.State, true)); Assert.Empty(driver.Writes);
    }
    [Fact] public void StaleSelectionCannotOverrideExternalChange()
    {
        var driver = new Driver { State = Original with { Vibrance = Original.Vibrance! with { Current = 57 } } }; using var session = Controller(driver, new());
        Assert.Equal(MonitorColorResult.Conflict, session.Begin(Original, true)); Assert.Empty(driver.Writes);
    }
    [Fact] public void ExternalChangeDuringPreviewIsNotOverwritten()
    {
        var driver = new Driver(); var store = new Store(); using var session = Controller(driver, store);
        session.Begin(Original, true); driver.State = driver.State! with { Vibrance = Original.Vibrance! with { Current = 80 } };
        Assert.Equal(MonitorColorResult.Conflict, session.CancelPreview()); Assert.Single(store.Value); Assert.Equal(new[] { 60 }, driver.Writes);
    }
    [Fact] public void DisconnectPreservesBackupForReconnectRecovery()
    {
        var driver = new Driver(); var store = new Store(); using var session = Controller(driver, store);
        session.Begin(Original, true); var applied = driver.State; driver.State = null;
        Assert.Equal(MonitorColorResult.Pending, session.CancelPreview()); Assert.Single(store.Value);
        driver.State = applied; session.CancelPreview(); Assert.Equal(50, driver.State!.Vibrance!.Current); Assert.Empty(store.Value);
    }
    [Fact] public void ReassignedDisplayNameDoesNotRedirectToAnotherMonitor()
    {
        var driver = new Driver(); var store = new Store(); using var session = Controller(driver, store);
        session.Begin(Original, true); driver.State = Original with { Identity = "different-monitor" };
        Assert.Equal(MonitorColorResult.Pending, session.CancelPreview()); Assert.Equal(new[] { 60 }, driver.Writes);
    }
    [Fact] public void DriverFailureWithoutMutationLeavesNoFalseSuccessOrBackup()
    {
        var driver = new Driver { Fail = true }; var store = new Store(); using var session = Controller(driver, store);
        Assert.Equal(MonitorColorResult.DriverError, session.Begin(Original, true)); Assert.Empty(store.Value); Assert.Equal(50, driver.State!.Vibrance!.Current);
    }
    [Fact] public void PartialDriverFailureRollsBack()
    {
        var driver = new Driver { Fail = true, Partial = true }; var store = new Store(); using var session = Controller(driver, store);
        Assert.Equal(MonitorColorResult.DriverError, session.Begin(Original, true)); Assert.Equal(new[] { 60, 50 }, driver.Writes); Assert.Empty(store.Value);
    }
    [Fact] public void FailedRestorationRetainsRecoveryEntry()
    {
        var driver = new Driver(); var store = new Store(); using var session = Controller(driver, store);
        session.Begin(Original, true); driver.Fail = true;
        Assert.Equal(MonitorColorResult.DriverError, session.CancelPreview()); Assert.Single(store.Value);
        driver.Fail = false;
    }
    [Fact] public void CommitFailureRemainsPreviewAndTimeoutCanRestore()
    {
        var driver = new Driver(); var store = new Store(); var clock = new Clock(); using var session = Controller(driver, store, clock);
        session.Begin(Original, true); store.FailWrite = true;
        Assert.Equal(MonitorColorResult.StorageError, session.Keep()); Assert.True(session.HasPreview); Assert.False(clock.Disposed);
        store.FailWrite = false; clock.Callback!(); Assert.Equal(50, driver.State!.Vibrance!.Current);
    }
    [Fact] public void StartupRollsBackOnlyAnUnfinishedPreview()
    {
        var driver = new Driver { State = Original with { Vibrance = Original.Vibrance! with { Current = 60 } } };
        var store = new Store { Value = [new(Original.Identity, 50, 60, 50, null, true)] }; using var session = Controller(driver, store);
        Assert.Empty(driver.Writes); session.CancelPreview(); Assert.Equal(50, driver.State!.Vibrance!.Current);
    }
    [Fact] public void ConfirmedSettingsPersistAcrossSessionsWithoutReapply()
    {
        var driver = new Driver(); var store = new Store();
        using (var first = Controller(driver, store)) { first.Begin(Original, true); first.Keep(); }
        driver.Writes.Clear(); using var second = Controller(driver, store);
        Assert.False(second.HasPreview); Assert.True(second.HasBackup(Original.Identity)); Assert.Empty(driver.Writes);
        second.Restore(Original.Identity); Assert.Equal(50, driver.State!.Vibrance!.Current);
    }
    [Fact] public void DisposingUndoesPendingPreview()
    {
        var driver = new Driver(); var store = new Store(); var session = Controller(driver, store);
        session.Begin(Original, true); session.Dispose(); Assert.Equal(50, driver.State!.Vibrance!.Current); Assert.Empty(store.Value);
    }
    [Fact] public void NeutralNoOpDoesNotCreateBackupOrTouchDriver()
    {
        var driver = new Driver(); var store = new Store(); using var session = Controller(driver, store);
        Assert.Equal(MonitorColorResult.NoChange, session.Begin(Original, false)); Assert.Empty(store.Value); Assert.Empty(driver.Writes);
    }
    [Fact] public void ASecondPreviewIsBlocked()
    {
        var driver = new Driver(); using var session = Controller(driver, new());
        session.Begin(Original, true); Assert.Equal(MonitorColorResult.Invalid, session.Begin(driver.State!, false)); Assert.Equal(new[] { 60 }, driver.Writes);
    }
    [Theory] [InlineData(0, 0, 0)] [InlineData(0, 100, 101)] [InlineData(-5000, 100, 0)]
    public void InvalidRangesAreRejected(int min, int max, int neutral) => Assert.False(new ColorRange(50, min, max, neutral).Valid);
    [Fact] public void NullJournalEntriesBlockWritesInsteadOfCrashing()
    {
        var driver = new Driver(); var store = new Store { Value = [null!] }; using var session = Controller(driver, store);
        Assert.Equal(MonitorColorResult.StorageError, session.Begin(Original, true)); Assert.Empty(driver.Writes);
    }
    [Fact] public void RealJsonStoreRoundTripsBackupWithoutTouchingUserData()
    {
        var folder = Path.Combine(Path.GetTempPath(), "LotusColorTest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(folder, "colors.json"); var store = new JsonMonitorColorStore(path);
            var entry = new MonitorColorChange(Original.Identity, 55, 60, 55, null, true);
            Assert.Empty(store.Load()); store.Save([entry]); Assert.Equal(entry, store.Load().Single());
            store.Save([]); Assert.Empty(store.Load()); Assert.Single(Directory.GetFiles(folder));
            File.WriteAllText(path, "null"); Assert.Throws<IOException>(() => store.Load());
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact] public void CustomIntensityUsesTheSameVerifiedPreviewAndRollback()
    {
        var driver = new Driver(); var store = new Store(); using var session = Controller(driver, store);
        Assert.Equal(MonitorColorResult.Preview, session.BeginCustom(Original, 57));
        Assert.Equal(57, driver.State!.Vibrance!.Current); session.CancelPreview(); Assert.Equal(50, driver.State.Vibrance.Current);
    }
    [Theory] [InlineData(-1)] [InlineData(101)]
    public void InvalidCustomIntensityNeverWrites(int value)
    {
        var driver = new Driver(); var store = new Store(); using var session = Controller(driver, store);
        Assert.Equal(MonitorColorResult.Invalid, session.BeginCustom(Original, value)); Assert.Empty(driver.Writes); Assert.Empty(store.Value);
    }
    [Fact] public void ExplicitRestoreCanResolveExternalConflict_OnlyWithFreshAuthorization()
    {
        var driver = new Driver(); var store = new Store(); using var session = Controller(driver, store);
        session.Begin(Original, true); session.Keep(); driver.State = driver.State! with { Vibrance = Original.Vibrance! with { Current = 80 } };
        Assert.Equal(MonitorColorResult.Conflict, session.Restore(Original.Identity));
        var approved = driver.State; driver.State = driver.State with { Vibrance = Original.Vibrance! with { Current = 81 } };
        Assert.Equal(MonitorColorResult.Conflict, session.Restore(Original.Identity, approved));
        Assert.Equal(MonitorColorResult.Restored, session.Restore(Original.Identity, driver.State)); Assert.Equal(50, driver.State.Vibrance.Current);
    }
    [Fact] public void ExplicitRestoreDuringPreviewRestoresFirstOriginalAndStopsTimer()
    {
        var driver = new Driver(); var clock = new Clock(); using var session = Controller(driver, new(), clock);
        session.Begin(Original, true);
        Assert.Equal(MonitorColorResult.Restored, session.Restore(Original.Identity, driver.State)); Assert.False(session.HasPreview); Assert.True(clock.Disposed);
    }
}
