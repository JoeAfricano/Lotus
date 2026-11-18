using Lotus.Core.Telemetry;
using Xunit;

namespace Lotus.Tests;

public sealed class GpuAutoTunerTests
{
    [Theory]
    [InlineData(GpuTestDepth.Quick, 45, 2, 1, 2)]
    [InlineData(GpuTestDepth.Balanced, 75, 4, 2, 4)]
    public async Task Short_modes_limit_the_search_but_keep_confirmation_and_restore(GpuTestDepth depth, int seconds, int uv, int memory, int core)
    {
        var budget = GpuTestBudget.For(depth);
        Assert.Equal((seconds,uv,memory,core), (budget.Seconds,budget.UndervoltSteps,budget.MemorySteps,budget.CoreSteps));
        var card = new FakeCard();
        var report = await new GpuAutoTuner(card,card,GpuTuning.For("RTX 5060"))
        { StepLength=TimeSpan.Zero, Settle=TimeSpan.Zero, MaxUndervoltSteps=budget.UndervoltSteps, MaxMemorySteps=budget.MemorySteps, MaxCoreSteps=budget.CoreSteps }
            .RunAsync(AutoModes.Undervolt | AutoModes.Overclock,CancellationToken.None);
        Assert.InRange(report.Steps.Count(s=>s.Mode=="undervolt"),1,uv);
        Assert.InRange(report.Steps.Count(s=>s.Mode=="memory"),1,memory);
        Assert.InRange(report.Steps.Count(s=>s.Mode=="core"),1,core);
        Assert.Equal("overclock",report.Steps.Last().Mode);
        Assert.NotNull(report.Undervolt); Assert.NotNull(report.Overclock);
        Assert.True(card.Restored); Assert.Equal(GpuSetting.Stock,card.Applied);
    }
    // A card that is stable down to 900 mV and up to +250 MHz core; memory helps up to +750 and then slows down (GDDR7).
    private sealed class FakeCard : IGpuControl, IGpuLoadTest
    {
        public GpuSetting Applied { get; private set; } = GpuSetting.Stock;
        public bool Saved, Restored;
        public bool RestoreSucceeds = true;
        public double Temperature = 70;
        public bool Available => true;
        public (int CoreMaxMhz, int MemMaxMhz) Limits => (1000, 3000);
        public bool SaveOriginal() { Saved = true; return true; }
        public bool Apply(GpuSetting setting) { Applied = setting; return true; }
        public bool RestoreOriginal() { Restored = RestoreSucceeds; if (Restored) Applied = GpuSetting.Stock; return Restored; }
        public bool HasPendingOriginal => Saved && !Restored;
        public Task<(IReadOnlyList<TuneSample> Samples, bool DriverReset, double Score)> RunAsync(TimeSpan length, IProgress<double>? progress, CancellationToken token)
        {
            var s = Applied;
            var crash = (s.FlatMv is { } mv && mv < 900) || s.CoreOffsetMhz > 250;
            var clock = s.FlatMhz ?? 2700 + s.CoreOffsetMhz;
            var memGain = s.MemOffsetMhz switch { 500 => 1.02, 750 => 1.03, 1000 => 1.025, _ => 1.0 };
            var samples = Enumerable.Range(0, 10).Select(_ => new TuneSample(s.FlatMv is null ? Temperature : 62, clock, s.FlatMv is null ? 140 : 110)).ToList();
            return Task.FromResult<(IReadOnlyList<TuneSample>, bool, double)>((samples, crash, 100 * memGain * (1 + s.CoreOffsetMhz / 3000.0)));
        }
    }

    [Fact] public async Task Finds_the_lowest_stable_voltage_the_best_memory_and_keeps_70_percent_of_the_core()
    {
        var card = new FakeCard();
        var tuner = new GpuAutoTuner(card, card, GpuTuning.For("NVIDIA GeForce RTX 5060")) { StepLength = TimeSpan.Zero, Settle = TimeSpan.Zero };
        var r = await tuner.RunAsync(AutoModes.All, CancellationToken.None);
        Assert.Equal(900, r.Undervolt!.Setting.FlatMv);           // 950 → 925 → 900 stable, 875 crashed
        Assert.Equal(750, r.Overclock!.Setting.MemOffsetMhz);     // +1000 was slower than +750
        Assert.Equal(175, r.Overclock.Setting.CoreOffsetMhz);     // last stable +250, kept at 70%
        Assert.NotNull(r.Combined);
        Assert.NotNull(r.Current);                                  // the player's own state was measured first
        Assert.True(card.Restored);                               // the card goes back as it was
        Assert.Equal(GpuSetting.Stock, card.Applied);
    }

    [Fact] public async Task Cancelling_restores_the_original_state()
    {
        var card = new FakeCard();
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var r = await new GpuAutoTuner(card, card, GpuTuning.For("RTX 5060")) { StepLength = TimeSpan.Zero, Settle = TimeSpan.Zero }.RunAsync(AutoModes.All, cts.Token);
        Assert.True(r.Cancelled); Assert.True(card.Restored);
    }

    [Fact] public async Task Restore_failure_is_reported_and_keeps_recovery_pending()
    {
        var card = new FakeCard { RestoreSucceeds = false };
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new GpuAutoTuner(card, card, GpuTuning.For("RTX 5060"))
            { StepLength = TimeSpan.Zero, Settle = TimeSpan.Zero }.RunAsync(AutoModes.All, cts.Token));
        Assert.True(card.HasPendingOriginal);
    }

    [Theory] [InlineData(90,"hot")] [InlineData(0,"sensors")]
    public async Task Unsafe_or_missing_temperature_stops_before_any_new_candidate(double temp,string reason)
    {
        var card = new FakeCard { Temperature=temp };
        var report = await new GpuAutoTuner(card, card, GpuTuning.For("RTX 5060"))
            { StepLength=TimeSpan.Zero,Settle=TimeSpan.Zero }.RunAsync(AutoModes.All,CancellationToken.None);
        Assert.Equal(reason,report.Stopped); Assert.Single(report.Steps); Assert.True(card.Restored);
        Assert.Null(report.Undervolt); Assert.Null(report.Overclock);
    }

    // The first real run stopped when the load test threw (window state read off the UI thread): whatever the
    // failure, the card must go back as it was.
    private sealed class ThrowingTest : IGpuLoadTest
    {
        public Task<(IReadOnlyList<TuneSample> Samples, bool DriverReset, double Score)> RunAsync(TimeSpan length, IProgress<double>? progress, CancellationToken token) =>
            throw new System.Runtime.InteropServices.COMException("wrong thread", unchecked((int)0x8001010E));
    }
    [Fact] public async Task A_failing_load_test_still_restores_the_card()
    {
        var card = new FakeCard();
        var tuner = new GpuAutoTuner(card, new ThrowingTest(), GpuTuning.For("RTX 5060")) { StepLength = TimeSpan.Zero, Settle = TimeSpan.Zero };
        await Assert.ThrowsAsync<System.Runtime.InteropServices.COMException>(() => tuner.RunAsync(AutoModes.All, CancellationToken.None));
        Assert.True(card.Restored);
    }
}
