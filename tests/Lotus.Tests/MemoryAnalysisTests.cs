using System.Collections.Concurrent;
using System.Text.Json;
using Lotus.Core;
using Lotus.Core.Diagnostics;
using Lotus.Core.Telemetry;
using Lotus.Infrastructure.Telemetry;
using Xunit;
using Xunit.Abstractions;

namespace Lotus.Tests;

public sealed class MemoryAnalysisTests(ITestOutputHelper output)
{
    private static HardwareTelemetrySnapshot Sample(double second, ulong available = 4UL << 30, ulong total = 16UL << 30)
    {
        var snapshot = HardwareTelemetrySnapshot.Missing(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(second), TelemetryStatus.Unavailable);
        return snapshot with { Memory = snapshot.Memory with {
            TotalPhysicalBytes = Reading<ulong>.Available(total, "GlobalMemoryStatusEx"),
            AvailablePhysicalBytes = Reading<ulong>.Available(available, "GlobalMemoryStatusEx") } };
    }
    private static MemoryAssessment Feed(MemoryAvailabilityAnalyzer analyzer, ulong available, int start = 0, int end = 30, ulong total = 16UL << 30)
    {
        for (var i = start; i <= end; i++) analyzer.Add(Sample(i, available, total));
        return analyzer.Current;
    }
    [Fact] public void FirstSampleObservesAndThirtySecondsAreRequired()
    {
        var rule = new MemoryAvailabilityAnalyzer();
        Assert.Equal(MemoryFinding.Unavailable, rule.Current.Finding);
        Assert.Equal(MemoryFinding.Observing, Feed(rule, 768UL << 20, end: 29).Finding);
        Assert.Equal(MemoryFinding.LowAvailability, rule.Add(Sample(30, 768UL << 20)).Finding);
        Assert.Equal(TimeSpan.FromSeconds(30), rule.Current.LowDuration);
    }
    [Theory]
    [InlineData(16, 1024, MemoryFinding.LowAvailability)]
    [InlineData(16, 512, MemoryFinding.VeryLowAvailability)]
    [InlineData(4, 512, MemoryFinding.NoSustainedLowAvailability)]
    [InlineData(64, 2048, MemoryFinding.NoSustainedLowAvailability)]
    public void PercentageAndAbsoluteBoundsMustBothHold(ulong totalGiB, ulong availableMiB, MemoryFinding expected)
        => Assert.Equal(expected, Feed(new(), availableMiB << 20, total: totalGiB << 30).Finding);
    [Fact] public void ShortDipAndRecoveryDoNotBecomeSustainedPressure()
    {
        var rule = new MemoryAvailabilityAnalyzer(); Feed(rule, 4UL << 30);
        Feed(rule, 200UL << 20, 31, 40);
        Assert.Equal(MemoryFinding.NoSustainedLowAvailability, rule.Current.Finding);
        rule.Add(Sample(41)); Assert.Equal(TimeSpan.Zero, rule.Current.LowDuration);
        Assert.Equal(MemoryFinding.NoSustainedLowAvailability, rule.Current.Finding);
        Assert.Equal(200UL << 20, rule.Current.MinimumAvailableBytes);
    }
    [Fact] public void VeryLowNeedsItsOwnThirtySecondWindow()
    {
        var rule = new MemoryAvailabilityAnalyzer(); Feed(rule, 768UL << 20);
        Assert.Equal(MemoryFinding.LowAvailability, Feed(rule, 200UL << 20, 31, 60).Finding);
        Assert.Equal(MemoryFinding.VeryLowAvailability, rule.Add(Sample(61, 200UL << 20)).Finding);
    }
    [Fact] public void FailureClearsEvidenceAndDoesNotConvertMissingToZero()
    {
        var rule = new MemoryAvailabilityAnalyzer(); Feed(rule, 200UL << 20);
        var failed = Sample(31) with { Memory = Sample(31).Memory with {
            AvailablePhysicalBytes = Reading<ulong>.Missing(TelemetryStatus.AccessDenied, "GlobalMemoryStatusEx") } };
        Assert.Equal(MemoryFinding.Unavailable, rule.Add(failed).Finding);
        Assert.Null(rule.Current.AvailableBytes); Assert.Equal(0, rule.SampleCount);
        Assert.Equal(MemoryFinding.Observing, rule.Add(Sample(32, 200UL << 20)).Finding);
    }
    [Theory] [InlineData(0, 0)] [InlineData(10, 11)]
    public void InvalidCapacityAndAvailabilityAreRejected(ulong total, ulong available)
    {
        var rule = new MemoryAvailabilityAnalyzer();
        Assert.Equal(MemoryFinding.Unavailable, rule.Add(Sample(0, available, total)).Finding);
    }
    [Fact] public void MismatchedSourcesAreRejected()
    {
        var sample = Sample(0);
        sample = sample with { Memory = sample.Memory with { AvailablePhysicalBytes = Reading<ulong>.Available(100, "Other") } };
        Assert.Equal(MemoryFinding.Unavailable, new MemoryAvailabilityAnalyzer().Add(sample).Finding);
    }
    [Fact] public void GapsClockRegressionAndCapacityChangeRestartEvidence()
    {
        var rule = new MemoryAvailabilityAnalyzer(); Feed(rule, 200UL << 20, end: 29);
        Assert.Equal(MemoryFinding.Observing, rule.Add(Sample(35, 200UL << 20)).Finding);
        Assert.Equal(TimeSpan.Zero, rule.Current.LowDuration);
        Assert.Equal(MemoryFinding.Unavailable, rule.Add(Sample(34)).Finding);
        Feed(rule, 200UL << 20, 40, 70);
        Assert.Equal(MemoryFinding.Observing, rule.Add(Sample(71, 200UL << 20, 8UL << 30)).Finding);
    }
    [Fact] public void DuplicateSamplesDoNotExtendEvidenceAndWindowIsBounded()
    {
        var rule = new MemoryAvailabilityAnalyzer(); Feed(rule, 4UL << 30, end: 120);
        Assert.InRange(rule.SampleCount, 1, 61); Assert.Equal(TimeSpan.FromSeconds(60), rule.Current.ObservedDuration);
        var before = rule.Current; Assert.Same(before, rule.Add(Sample(120, 0)));
        rule.Add(Sample(121)); Assert.Equal(4UL << 30, rule.Current.MinimumAvailableBytes);
        rule.Reset(); Assert.Equal(MemoryAssessment.Empty, rule.Current);
    }
    private sealed class Dispatcher : IUiDispatcher
    {
        public readonly ConcurrentQueue<Action> Queue = new();
        public bool Post(Action action) { Queue.Enqueue(action); return true; }
        public void Drain() { while (Queue.TryDequeue(out var action)) action(); }
    }
    private sealed class Provider : IHardwareTelemetryProvider
    {
        private int sample;
        public void Reset() => sample = 0;
        public ValueTask<HardwareTelemetrySnapshot> ReadAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(Sample(Interlocked.Increment(ref sample))); }
    }
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(5, timeout.Token);
    }
    [Fact] public async Task ViewModelSharesExistingStreamLocalizesAndDropsEvidenceOnPause()
    {
        var dispatcher = new Dispatcher(); var text = new TextCatalog();
        await using var service = new HardwareMonitoringService(new Provider(), TimeSpan.FromMilliseconds(20));
        var monitoring = new MonitoringViewModel(service, text, dispatcher);
        using var analysis = new MemoryAnalysisViewModel(monitoring, text);
        analysis.SetActive(true); monitoring.SetActive(true);
        await Until(() => !dispatcher.Queue.IsEmpty); dispatcher.Drain();
        Assert.Equal(1, service.ConsumerCount); Assert.Equal("4,00 GiB", analysis.Available);
        text.SetLanguage("en-US"); monitoring.RefreshLanguage();
        Assert.Equal("4.00 GiB", analysis.Available); Assert.Equal("Observing availability", analysis.Status);
        Assert.Contains("insufficient", text["RamConfidenceObserved"]);
        analysis.SetActive(false); monitoring.SetActive(false); dispatcher.Drain();
        Assert.Equal("—", analysis.Available); Assert.Equal(MemoryFinding.Unavailable, analysis.Assessment.Finding);
        await Until(() => !service.IsRunning); Assert.Equal(0, service.ConsumerCount);
        analysis.SetActive(true); monitoring.SetActive(true);
        Assert.Null(analysis.Assessment.AvailableBytes); // old monitoring snapshot is not reused
        await Until(() => !dispatcher.Queue.IsEmpty); dispatcher.Drain();
        Assert.Equal(MemoryFinding.Observing, analysis.Assessment.Finding);
        analysis.SetActive(false); monitoring.SetActive(false);
    }
    [WindowsMemoryFact] [Trait("Category", "Hardware")]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task NativeWindowsMemoryFeedsRuleWithoutInventedReadings()
    {
        var seconds = int.TryParse(Environment.GetEnvironmentVariable("LOTUS_RAM_DIAGNOSTIC_SECONDS"), out var duration) ? Math.Clamp(duration, 2, 120) : 2;
        var rule = new MemoryAvailabilityAnalyzer();
        var states = new List<MemoryAssessment>();
        await using var service = new HardwareMonitoringService(new WindowsHardwareTelemetryProvider());
        using var lease = service.Subscribe(snapshot => { lock (states) states.Add(rule.Add(snapshot)); });
        await Task.Delay(TimeSpan.FromSeconds(seconds + 0.2));
        lease.Dispose(); await service.DisposeAsync();
        Assert.NotEmpty(states); Assert.All(states, state => {
            Assert.NotEqual(MemoryFinding.Unavailable, state.Finding);
            Assert.True(state.TotalBytes > 0); Assert.True(state.AvailableBytes <= state.TotalBytes);
            Assert.Equal("GlobalMemoryStatusEx", state.Source);
        });
        Assert.False(service.IsRunning); Assert.Equal(0, service.ConsumerCount);
        if (seconds >= 32) Assert.NotEqual(MemoryFinding.Observing, states[^1].Finding);
        output.WriteLine(JsonSerializer.Serialize(new { DurationSeconds = seconds, Samples = states.Count, First = states[0], Last = states[^1] }));
    }
}

public sealed class WindowsMemoryFactAttribute : FactAttribute
{
    public WindowsMemoryFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Requires actual Windows memory APIs."; }
}
