using Lotus.Core.Display;
using Lotus.Infrastructure.Display;
using Xunit;

namespace Lotus.Tests;

public sealed class DisplayCalibrationTests
{
    private static ushort[] Ramp() => Enumerable.Range(0, 768).Select(i => (ushort)(i % 256 * 257)).ToArray();
    private static CalibrationState Original() => new("stable-monitor", "display1", "Monitor", false, new(65, 0, 100), new(60, 0, 100), Ramp());
    private static CalibrationProfile Profile(double gamma = 1) => new(65, 60, gamma);
    private sealed class Driver : IDisplayCalibration
    {
        public CalibrationState? State = Original();
        public readonly List<CalibrationState> Writes = [];
        public bool Fail, Partial, FailRecovery, PartialVibrance;
        public Action? BeforeWrite;
        public IReadOnlyList<CalibrationState> Displays() => State is { } s ? [s] : [];
        public CalibrationState? Read(string id) => State?.Identity == id ? State : null;
        public bool TryApply(CalibrationState expected, CalibrationState target)
        {
            BeforeWrite?.Invoke(); Assert.NotNull(State); Assert.True(State.Same(expected)); Writes.Add(target);
            if (Partial || PartialVibrance)
            {
                State = PartialVibrance ? State with { Vibrance = target.Vibrance } : State with { Brightness = target.Brightness };
                Partial = PartialVibrance = false; Fail = FailRecovery; return false;
            }
            if (Fail) return false;
            State = target; return true;
        }
    }
    private sealed class Store : ICalibrationStore
    {
        public IReadOnlyList<CalibrationBackup> Entries = [];
        public bool Fail, FailRead;
        public int Saves;
        public IReadOnlyList<CalibrationBackup> Load() => FailRead ? throw new IOException("blocked") : Entries;
        public void Save(IReadOnlyList<CalibrationBackup> values) { if (Fail) throw new IOException("blocked"); Entries = values.ToArray(); Saves++; }
    }
    private sealed class Clock : IDisposable
    {
        public DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        public readonly List<Action> Callbacks = [];
        public TimeSpan LastDelay;
        public IDisposable Schedule(TimeSpan delay, Action callback) { LastDelay = delay; Callbacks.Add(callback); return this; }
        public void Dispose() { }
    }
    private static DisplayCalibrationController Controller(Driver driver, Store store, Clock? time = null)
    { var clock = time ?? new(); return new(driver, store, schedule: clock.Schedule, utcNow: () => clock.Now); }
    [Fact] public void SaturationChangesAreBackedUpVerifiedAndReversedWithOtherControls()
    {
        var original = Original() with { Vibrance = new(50, 0, 100, 50) }; var driver = new Driver { State = original }; var store = new Store();
        using var controller = Controller(driver, store);
        Assert.Equal(MonitorColorResult.Preview, controller.Begin(original, Profile(1.04) with { Saturation = 55 }));
        Assert.Equal(55, driver.State!.Vibrance!.Current); Assert.Equal(50, store.Entries.Single().Original.Vibrance!.Current);
        Assert.Equal(MonitorColorResult.Preview, controller.Update(Profile(1.04) with { Saturation = 20 })); Assert.Equal(20, driver.State.Vibrance.Current);
        Assert.Equal(MonitorColorResult.Restored, controller.CancelPreview()); Assert.True(driver.State.Same(original));
    }
    [Fact] public void PartialSaturationFailureRestoresItsOriginalAlongsideGamma()
    {
        var original = Original() with { Vibrance = new(50, 0, 100, 50) }; var driver = new Driver { State = original, PartialVibrance = true };
        using var controller = Controller(driver, new());
        Assert.Equal(MonitorColorResult.DriverError, controller.Begin(original, Profile(1.04) with { Saturation = 55 }));
        Assert.True(driver.State!.Same(original)); Assert.False(controller.HasPreview);
    }
    [Fact] public void LegacyBackupPreservesNewlyDetectedSaturationInsteadOfGuessingItsOriginal()
    {
        var old = Original(); var applied = Profile(1.1).Build(old); var driver = new Driver { State = applied with { Vibrance = new(57, 0, 100, 50) } };
        var store = new Store { Entries = [new(old, applied, old, null, Profile(1.1), null, false)] };
        using var controller = Controller(driver, store);
        Assert.Equal(MonitorColorResult.Restored, controller.Restore(driver.State)); Assert.Equal(57, driver.State!.Vibrance!.Current);
        Assert.True(CalibrationState.RampEqual(old.Ramp, driver.State.Ramp)); Assert.Empty(store.Entries);
    }
    [Fact] public void UnsupportedSaturationAndNonFiniteValuesNeverProduceHardwareTargets()
    {
        Assert.Throws<ArgumentException>(() => (Profile() with { Saturation = 55 }).Build(Original()));
        Assert.False((Profile() with { Saturation = double.NaN }).Valid);
        Assert.False((Profile() with { Saturation = 101 }).Valid);
        var original = Original() with { Vibrance = new(25, -50, 50, 0) };
        Assert.Equal(-50, (Profile() with { Saturation = 0 }).Build(original).Vibrance!.Current);
        Assert.Equal(50, (Profile() with { Saturation = 100 }).Build(original).Vibrance!.Current);
    }
    [Fact] public void RecommendationsKeepNeutralDriverSaturationForAccuracyAndOnlyOfferOptionalGamingBoost()
    {
        var original = Original() with { Vibrance = new(60, 0, 100, 50) };
        foreach (var use in new[] { DisplayUse.Everyday, DisplayUse.Editing, DisplayUse.Movies })
            Assert.Equal(50, DisplayRecommendations.Suggest(original, use, DisplayAmbient.Normal).Saturation);
        Assert.Equal(55, DisplayRecommendations.Suggest(original, DisplayUse.Gaming, DisplayAmbient.Normal).Saturation);
    }
    [Fact]
    public async Task UiStatusReadsNeverWaitForSlowHardwareTransaction()
    {
        var driver = new Driver(); var store = new Store(); using var controller = Controller(driver, store);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        driver.BeforeWrite = () => { entered.TrySetResult(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); };
        var write = Task.Run(() => controller.Begin(driver.State!, Profile(1.04)));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var uiRead = Task.Run(() => (controller.HasPreview, controller.HasBackup("stable-monitor"),
                controller.ProfileFor("stable-monitor"), controller.OriginalFor("stable-monitor")));
            var result = await uiRead.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(result.Item1); Assert.True(result.Item2); Assert.NotNull(result.Item3); Assert.NotNull(result.Item4);
        }
        finally { release.Set(); await write; }
        Assert.Equal(MonitorColorResult.Preview, controller.LastResult);
    }
    [Theory]
    [InlineData(DisplayUse.Everyday, DisplayAmbient.Normal, 65, 1)]
    [InlineData(DisplayUse.Everyday, DisplayAmbient.Dim, 57, 1)]
    [InlineData(DisplayUse.Gaming, DisplayAmbient.Bright, 70, 1.04)]
    [InlineData(DisplayUse.Editing, DisplayAmbient.Dim, 65, 1)]
    [InlineData(DisplayUse.Movies, DisplayAmbient.Dim, 57, 1)]
    public void RecommendationsRespectUseAmbientAndPreserveContrastAndWhite(DisplayUse use, DisplayAmbient ambient, double brightness, double gamma)
    {
        var original = Original(); var suggested = DisplayRecommendations.Suggest(original, use, ambient);
        Assert.Equal(brightness, suggested.Brightness); Assert.Equal(60, suggested.Contrast);
        Assert.Equal(gamma, suggested.Gamma); Assert.Equal(0, suggested.Warmth); Assert.Equal(0, suggested.Tint);
        Assert.True(suggested.Build(original).Valid);
    }
    [Fact] public void RecommendationsDoNotInventUnavailableControlsOrOverrideHdr()
    {
        var original = Original() with { Brightness = null, Ramp = null };
        var suggested = DisplayRecommendations.Suggest(original, DisplayUse.Gaming, DisplayAmbient.Dim);
        Assert.Null(suggested.Brightness); Assert.Equal(1, suggested.Gamma); Assert.True(suggested.Build(original).Valid);
        Assert.Throws<ArgumentException>(() => DisplayRecommendations.Suggest(original with { AdvancedColor = true }, DisplayUse.Gaming, DisplayAmbient.Normal));
    }
    [Fact] public void RecommendationBrightnessIsBoundedAndNotCumulative()
    {
        var original = Original() with { Brightness = new(3, 0, 100) };
        Assert.Equal(0, DisplayRecommendations.Suggest(original, DisplayUse.Everyday, DisplayAmbient.Dim).Brightness);
        original = original with { Brightness = new(99, 0, 100) };
        Assert.Equal(100, DisplayRecommendations.Suggest(original, DisplayUse.Everyday, DisplayAmbient.Bright).Brightness);
    }
    [Fact] public void NeutralProfilePreservesEveryOriginalGammaEntry()
    {
        var baseline = Original(); baseline.Ramp![120] = baseline.Ramp[119];
        var target = Profile().Build(baseline);
        Assert.True(target.Same(baseline)); Assert.NotSame(target.Ramp, baseline.Ramp);
    }
    [Fact] public void HardwareLevelsUseReportedRanges()
    {
        var baseline = Original() with { Brightness = new(100, 50, 150) };
        var target = new CalibrationProfile(20, 75).Build(baseline);
        Assert.Equal(70, target.Brightness!.Current); Assert.Equal(75, target.Contrast!.Current); Assert.True(CalibrationState.RampEqual(baseline.Ramp, target.Ramp));
    }
    [Fact] public void GammaChangesMidtonesAndPreservesEndpoints()
    {
        var target = Profile(1.2).Build(Original());
        Assert.True(target.Ramp![128] > Original().Ramp![128]); Assert.Equal((ushort)0, target.Ramp[0]); Assert.Equal(ushort.MaxValue, target.Ramp[255]); Assert.True(target.Valid);
    }
    [Fact] public void WhiteBalanceChangesChannelsWithoutSaturationMatrix()
    {
        var target = (Profile() with { Warmth = 20, Tint = 10 }).Build(Original());
        Assert.True(target.Ramp![128] > Original().Ramp![128]); Assert.True(target.Ramp[640] < Original().Ramp![640]); Assert.True(target.Valid);
    }
    [Theory] [InlineData(.75, -.1, .8, -20, -10)] [InlineData(1.3, .1, 1.2, 20, 10)]
    public void ControlBoundsProduceReadableMonotonicRamps(double gamma, double brightness, double contrast, double warmth, double tint)
    {
        var value = new CalibrationProfile(65, 60, gamma, brightness, contrast, warmth, tint).Build(Original()); Assert.True(value.Valid);
    }
    [Theory] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(.1)] [InlineData(4)]
    public void InvalidGammaNeverProducesAPlan(double value) => Assert.False(Profile(value).Valid);
    [Fact] public void NoGammaSupportStillAllowsPhysicalBrightness()
    {
        var baseline = Original() with { Ramp = null }; Assert.True(baseline.CanAdjust);
        Assert.Equal(70, new CalibrationProfile(70, 60).Build(baseline).Brightness!.Current);
        Assert.Throws<ArgumentException>(() => Profile(1.1).Build(baseline));
    }
    [Fact] public void FlatOrInvertedRampsAreNotAdvertisedAsSupported()
    {
        Assert.False(CalibrationState.ValidRamp(new ushort[768])); var ramp = Ramp(); ramp[10] = 0; Assert.False(CalibrationState.ValidRamp(ramp));
    }
    [Fact] public void PreviewCanArmBeforeAnySliderChange()
    {
        var driver = new Driver(); var store = new Store(); using var controller = Controller(driver, store);
        Assert.Equal(MonitorColorResult.Preview, controller.Begin(driver.State!, Profile())); Assert.Empty(driver.Writes); Assert.True(controller.HasPreview);
    }
    [Fact] public void DurableBackupPrecedesAllHardwareWrites()
    {
        var driver = new Driver(); var store = new Store(); using var controller = Controller(driver, store);
        driver.BeforeWrite = () => Assert.True(store.Entries.Single().Preview);
        Assert.Equal(MonitorColorResult.Preview, controller.Begin(driver.State!, Profile(1.1)));
    }
    [Fact] public void LiveUpdatesAreRelativeToOriginal_NotCumulative()
    {
        var driver = new Driver(); using var controller = Controller(driver, new());
        controller.Begin(driver.State!, Profile()); controller.Update(Profile(1.1)); controller.Update(Profile(1.2));
        Assert.True(driver.State!.Same(Profile(1.2).Build(Original())));
        controller.CancelPreview(); Assert.True(driver.State.Same(Original()));
    }
    [Fact] public void OldTimerCannotCancelANewerLiveUpdate()
    {
        var driver = new Driver(); var clock = new Clock(); using var controller = Controller(driver, new(), clock);
        controller.Begin(driver.State!, Profile()); var stale = clock.Callbacks[0];
        controller.Update(Profile(1.1)); stale(); Assert.True(controller.HasPreview); Assert.True(driver.State!.Same(Profile(1.1).Build(Original())));
        clock.Callbacks[^1](); Assert.False(controller.HasPreview); Assert.True(driver.State.Same(Original()));
    }
    [Fact] public void UpdatesCannotExtendPreviewPastTenMinutes()
    {
        var driver = new Driver(); var clock = new Clock(); using var controller = Controller(driver, new(), clock);
        controller.Begin(driver.State!, Profile()); clock.Now = clock.Now.AddSeconds(595); controller.Update(Profile(1.1));
        Assert.Equal(TimeSpan.FromSeconds(5), clock.LastDelay); clock.Callbacks[^1](); Assert.True(driver.State!.Same(Original()));
    }
    [Fact] public void RepeatedComparisonAvoidsDuplicateWritesAndPersistenceButStillChecksConflicts()
    {
        var driver = new Driver(); var store = new Store(); var clock = new Clock(); using var controller = Controller(driver, store, clock);
        controller.Begin(driver.State!, Profile(1.04)); controller.Update(Profile(1.04), true);
        var saves = store.Saves; var writes = driver.Writes.Count;
        Assert.Equal(MonitorColorResult.Preview, controller.Update(Profile(1.04), true));
        Assert.Equal(saves, store.Saves); Assert.Equal(writes, driver.Writes.Count); Assert.Equal(TimeSpan.FromMinutes(2), clock.LastDelay);
        driver.State = driver.State! with { Brightness = new(20, 0, 100) };
        Assert.Equal(MonitorColorResult.Conflict, controller.Update(Profile(1.04), true));
    }
    [Fact] public void BeforeAfterComparisonDoesNotLoseDesiredProfile()
    {
        var driver = new Driver(); var store = new Store(); using var controller = Controller(driver, store);
        controller.Begin(driver.State!, Profile(1.1)); Assert.False(controller.PreviewMatchesOriginal);
        controller.Update(Profile(1.2), true); Assert.True(driver.State!.Same(Original())); Assert.True(controller.PreviewMatchesOriginal);
        Assert.Equal(1.2, store.Entries.Single().Profile.Gamma); controller.Update(Profile(1.2), false); Assert.False(controller.PreviewMatchesOriginal); controller.Keep();
        Assert.True(driver.State.Same(Profile(1.2).Build(Original())));
    }
    [Fact] public void KeepRetainsOriginalAcrossMultipleSessions()
    {
        var driver = new Driver(); var store = new Store();
        using (var first = Controller(driver, store)) { first.Begin(driver.State!, Profile(1.1)); first.Keep(); }
        using var second = Controller(driver, store); second.Begin(driver.State!, Profile(1.2)); second.Keep();
        Assert.True(store.Entries.Single().Original.Same(Original())); second.Restore(driver.State!); Assert.True(driver.State!.Same(Original()));
    }
    [Fact] public void CancelNewPreviewReturnsToLastConfirmedSettings()
    {
        var driver = new Driver(); var store = new Store(); using var controller = Controller(driver, store);
        controller.Begin(driver.State!, Profile(1.1)); controller.Keep(); var kept = driver.State!;
        controller.Begin(kept, Profile(1.2)); controller.CancelPreview(); Assert.True(driver.State!.Same(kept)); Assert.Equal(1.1, controller.ProfileFor(kept.Identity)!.Gamma);
    }
    [Fact] public void StorageFailureBeforeBeginNeverWritesHardware()
    {
        var driver = new Driver(); using var controller = Controller(driver, new() { Fail = true });
        Assert.Equal(MonitorColorResult.StorageError, controller.Begin(driver.State!, Profile(1.1))); Assert.Empty(driver.Writes);
    }
    [Fact] public void StorageFailureDuringUpdatePreservesPriorRecoveryAndTimer()
    {
        var driver = new Driver(); var store = new Store(); var clock = new Clock(); using var controller = Controller(driver, store, clock);
        controller.Begin(driver.State!, Profile(1.1)); store.Fail = true;
        Assert.Equal(MonitorColorResult.StorageError, controller.Update(Profile(1.2))); Assert.True(driver.State!.Same(Profile(1.1).Build(Original())));
        store.Fail = false; clock.Callbacks[^1](); Assert.True(driver.State.Same(Original()));
    }
    [Fact] public void PartialInitialApplyRestoresAllComponents()
    {
        var driver = new Driver { Partial = true }; var store = new Store(); using var controller = Controller(driver, store);
        var plan = Profile(1.1) with { Brightness = 75, Contrast = 70 };
        Assert.Equal(MonitorColorResult.DriverError, controller.Begin(driver.State!, plan)); Assert.True(driver.State!.Same(Original())); Assert.Empty(store.Entries);
    }
    [Fact] public void FailedLiveUpdateRecoveryRetainsTransitionalStateForLaterRollback()
    {
        var driver = new Driver(); var store = new Store(); using var controller = Controller(driver, store);
        controller.Begin(driver.State!, Profile(1.1)); driver.Partial = driver.FailRecovery = true;
        Assert.Equal(MonitorColorResult.DriverError, controller.Update(Profile(1.2) with { Brightness = 75 })); Assert.NotNull(store.Entries.Single().TransitionFrom);
        driver.Fail = false; Assert.Equal(MonitorColorResult.Restored, controller.CancelPreview()); Assert.True(driver.State!.Same(Original()));
    }
    [Fact] public void ExternalChangesAreNotOverwrittenByTimeout()
    {
        var driver = new Driver(); var store = new Store(); using var controller = Controller(driver, store);
        controller.Begin(driver.State!, Profile(1.1)); driver.State = driver.State! with { Brightness = new(95, 0, 100) };
        Assert.Equal(MonitorColorResult.Conflict, controller.CancelPreview()); Assert.Equal(95, driver.State.Brightness!.Current); Assert.Single(store.Entries);
    }
    [Fact] public void DisconnectKeepsRecoveryUntilMonitorReconnects()
    {
        var driver = new Driver(); var store = new Store(); using var controller = Controller(driver, store);
        controller.Begin(driver.State!, Profile(1.1)); var applied = driver.State; driver.State = null;
        Assert.Equal(MonitorColorResult.Pending, controller.CancelPreview()); Assert.Single(store.Entries);
        driver.State = applied; controller.CancelPreview(); Assert.True(driver.State!.Same(Original()));
    }
    [Theory] [InlineData(true)] [InlineData(null)]
    public void HdrOrUnknownBlocksAllWrites(bool? advanced)
    {
        var driver = new Driver { State = Original() with { AdvancedColor = advanced } }; using var controller = Controller(driver, new());
        Assert.Equal(MonitorColorResult.AdvancedColor, controller.Begin(driver.State, Profile(1.1))); Assert.Empty(driver.Writes);
    }
    [Fact] public void StaleExpectedStateAndSecondPreviewAreRejected()
    {
        var driver = new Driver(); using var controller = Controller(driver, new());
        Assert.Equal(MonitorColorResult.Conflict, controller.Begin(Original() with { Brightness = new(10, 0, 100) }, Profile()));
        controller.Begin(driver.State!, Profile()); Assert.Equal(MonitorColorResult.Invalid, controller.Begin(driver.State!, Profile()));
    }
    [Fact] public void ExplicitRestoreStillRequiresAFreshSnapshot()
    {
        var driver = new Driver(); using var controller = Controller(driver, new()); controller.Begin(driver.State!, Profile(1.1)); controller.Keep();
        var stale = driver.State!; driver.State = driver.State! with { Contrast = new(75, 0, 100) };
        Assert.Equal(MonitorColorResult.Conflict, controller.Restore(stale)); Assert.Equal(MonitorColorResult.Restored, controller.Restore(driver.State)); Assert.True(driver.State.Same(Original()));
    }
    [Fact] public void CrashJournalRestoresOnNextControllerWithoutReapplying()
    {
        var original = Original(); var applied = Profile(1.1).Build(original); var driver = new Driver { State = applied };
        var store = new Store { Entries = [new(original, applied, original, null, Profile(1.1), null, true)] };
        using var controller = Controller(driver, store); Assert.Empty(driver.Writes); controller.CancelPreview(); Assert.True(driver.State!.Same(original));
    }
    [Fact] public void DisposeRestoresOnlyUnconfirmedChanges()
    {
        var driver = new Driver(); var controller = Controller(driver, new()); controller.Begin(driver.State!, Profile(1.1)); controller.Dispose(); Assert.True(driver.State!.Same(Original()));
    }
    [Fact] public void BadJournalBlocksInsteadOfDiscardingOriginalOnDisk()
    {
        var driver = new Driver(); var store = new Store { Entries = [null!] }; using var controller = Controller(driver, store);
        Assert.Equal(MonitorColorResult.StorageError, controller.Begin(driver.State!, Profile())); Assert.Empty(driver.Writes); Assert.Single(store.Entries);
    }
    [Fact] public void UnreadableJournalPreventsHardwareChanges()
    {
        var driver = new Driver(); using var controller = Controller(driver, new() { FailRead = true });
        Assert.Equal(MonitorColorResult.StorageError, controller.Begin(driver.State!, Profile(1.1))); Assert.Empty(driver.Writes);
    }
    [Fact] public void JsonStorePreservesAllThreeRgbTablesAndProfiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LotusCalibrationTest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonCalibrationStore(Path.Combine(directory, "recovery.json")); var original = Original(); var target = Profile(1.1).Build(original);
            store.Save([new(original, target, original, null, Profile(1.1), null, true)]);
            var restored = store.Load().Single(); Assert.True(original.Same(restored.Original)); Assert.True(target.Same(restored.Applied)); Assert.Equal(1.1, restored.Profile.Gamma);
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact] public void SettleWaitsForDelayedDdcReadbackInsteadOfFailingOnTheFirstStaleRead()
    {
        // Monitor accepted 66/61 but keeps reporting 65/60 for two reads, then one read glitches (null).
        var target = Original() with { Brightness = new(66, 0, 100), Contrast = new(61, 0, 100) };
        var reads = new Queue<CalibrationState?>([Original(), Original(), null, target]);
        var sleeps = new List<TimeSpan>();
        var result = DisplaySettle.Until(() => reads.Dequeue(), s => s.Same(target, 256), sleeps.Add, TimeSpan.FromMilliseconds(1500), TimeSpan.FromMilliseconds(150));
        Assert.True(result!.Same(target)); Assert.Equal(3, sleeps.Count); Assert.Empty(reads);
    }
    [Fact] public void SettleIsBoundedAndReturnsTheLastReadWhenTheMonitorNeverConfirms()
    {
        var target = Original() with { Brightness = new(66, 0, 100) }; var count = 0; var slept = TimeSpan.Zero;
        var result = DisplaySettle.Until(() => { count++; return Original(); }, s => s.Same(target, 256), d => slept += d, TimeSpan.FromMilliseconds(1500), TimeSpan.FromMilliseconds(150));
        Assert.False(result!.Same(target)); Assert.Equal(11, count); Assert.Equal(TimeSpan.FromMilliseconds(1500), slept);
    }
    [Fact] public void SettleReturnsImmediatelyWhenTheFirstReadAlreadyMatches()
    {
        var sleeps = 0; var result = DisplaySettle.Until(Original, s => s.Same(Original()), _ => sleeps++, DisplaySettle.ExpectBudget, DisplaySettle.ReadStep);
        Assert.NotNull(result); Assert.Equal(0, sleeps);
        Assert.Throws<ArgumentOutOfRangeException>(() => DisplaySettle.Until(Original, _ => true, _ => { }, TimeSpan.FromSeconds(1), TimeSpan.Zero));
    }
}
