using Lotus.Core.Telemetry;
using Xunit;

namespace Lotus.Tests;

public sealed class GpuAutoTunerTests
{
    // A card that is stable down to 900 mV and up to +250 MHz core; memory helps up to +750 and then slows down (GDDR7).
    private sealed class FakeCard : IGpuControl, IGpuLoadTest
    {
        public GpuSetting Applied { get; private set; } = GpuSetting.Stock;
        public bool Saved, Restored;
        public bool Available => true;
        public (int CoreMaxMhz, int MemMaxMhz) Limits => (1000, 3000);
        public bool SaveOriginal() { Saved = true; return true; }
        public bool Apply(GpuSetting setting) { Applied = setting; return true; }
        public bool RestoreOriginal() { Restored = true; Applied = GpuSetting.Stock; return true; }
        public bool HasPendingOriginal => Saved && !Restored;
        public Task<(IReadOnlyList<TuneSample> Samples, bool DriverReset, double Score)> RunAsync(TimeSpan length, IProgress<double>? progress, CancellationToken token)
        {
            var s = Applied;
            var crash = (s.FlatMv is { } mv && mv < 900) || s.CoreOffsetMhz > 250;
            var clock = s.FlatMhz ?? 2700 + s.CoreOffsetMhz;
            var memGain = s.MemOffsetMhz switch { 500 => 1.02, 750 => 1.03, 1000 => 1.025, _ => 1.0 };
            var samples = Enumerable.Range(0, 10).Select(_ => new TuneSample(s.FlatMv is null ? 70 : 62, clock, s.FlatMv is null ? 140 : 110)).ToList();
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
}
