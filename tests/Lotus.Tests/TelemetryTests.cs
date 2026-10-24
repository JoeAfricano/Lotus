using System.Collections.Concurrent;
using Lotus.Core;
using Lotus.Core.Telemetry;
using Lotus.Infrastructure.Telemetry;
using Xunit;
using Xunit.Abstractions;

namespace Lotus.Tests;

public sealed class TelemetryTests(ITestOutputHelper output)
{
    private static HardwareTelemetrySnapshot Sample(int second, double? cpu = 25, double? memory = 60)
    {
        var sample = HardwareTelemetrySnapshot.Missing(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(second), TelemetryStatus.Unavailable);
        return sample with
        {
            Cpu = sample.Cpu with { TotalUtilizationPercent = Read(cpu), Model = "test only", PhysicalCores = 4, LogicalProcessors = 8 },
            Memory = sample.Memory with { UsedPhysicalPercent = Read(memory), TotalPhysicalBytes = Reading<ulong>.Available(16UL << 30, "test"), LotusWorkingSetBytes = Reading<ulong>.Available(128UL << 20, "test") }
        };
    }
    private static Reading<double> Read(double? n) => n is { } value ? Reading<double>.Available(value, "test") : Reading<double>.Missing(TelemetryStatus.Unavailable, "test");

    [Theory]
    [InlineData(0, 100, 0)][InlineData(25, 100, 25)][InlineData(100, 100, 100)]
    public void PercentUsesExplicitCapacity(double part, double total, double expected) => Assert.Equal(expected, TelemetryMath.Percent(part, total));
    [Theory]
    [InlineData(-1, 100)][InlineData(101, 100)][InlineData(1, 0)][InlineData(double.NaN, 100)][InlineData(1, double.PositiveInfinity)]
    public void InvalidValuesDoNotBecomeZero(double part, double total) => Assert.Null(TelemetryMath.Percent(part, total));
    [Fact] public void BinaryUnitsAndPhysicalMemoryDefinitionsAreExact()
    {
        Assert.Equal(1, TelemetryMath.GiB(1UL << 30)); Assert.Equal(1, TelemetryMath.MiB(1UL << 20));
        Assert.Equal(12UL << 30, TelemetryMath.UsedPhysicalBytes(16UL << 30, 4UL << 30));
        Assert.Null(TelemetryMath.UsedPhysicalBytes(0, 0)); Assert.Null(TelemetryMath.UsedPhysicalBytes(10, 11));
    }
    [Fact] public void CpuFirstSampleWarmsUpAndKernelAlreadyIncludesIdle()
    {
        var calculator = new CpuUsageCalculator();
        var first = calculator.Calculate(new(50, 100, 100, 0, TimeSpan.Zero), 2);
        Assert.Null(first.Total.Value); Assert.Equal(TelemetryStatus.WarmingUp, first.Total.Status);
        var second = calculator.Calculate(new(150, 250, 150, 2_000_000, TimeSpan.FromSeconds(1)), 2);
        Assert.Equal(50, second.Total.Value); Assert.Equal(10, second.Process.Value);
        calculator.Reset(); Assert.Equal(TelemetryStatus.WarmingUp, calculator.Calculate(new(0, 0, 0, 0, TimeSpan.Zero), 2).Total.Status);
    }
    [Fact] public void CounterRegressionZeroIntervalAndMissingProcessRemainUnavailable()
    {
        var calculator = new CpuUsageCalculator();
        calculator.Calculate(new(100, 100, 100, 100, TimeSpan.FromSeconds(2)), 8);
        var result = calculator.Calculate(new(99, 99, 99, null, TimeSpan.FromSeconds(1)), 8);
        Assert.Null(result.Total.Value); Assert.Null(result.Process.Value);
        Assert.Equal(TelemetryStatus.Invalid, result.Total.Status);
        Assert.Null(calculator.Calculate(new(99, 99, 99, null, TimeSpan.FromSeconds(1)), 0).Total.Value);
    }
    [Fact] public void HistoryIsBoundedByRealTimeAndCountAndPreservesGaps()
    {
        var history = new TelemetryHistory();
        for (var i = 0; i < 100; i++) history.Add(Sample(i));
        Assert.Equal(60, history.Points.Count); Assert.Equal(TimeSpan.FromSeconds(40), history.Points[0].Time);
        history.Add(Sample(101, null, double.NaN)); Assert.Null(history.Points[^1].CpuPercent); Assert.Null(history.Points[^1].MemoryPercent);
        history.Add(Sample(1000)); Assert.Single(history.Points);
        history.Add(Sample(999)); Assert.Single(history.Points);
        history.Clear(); Assert.Empty(history.Points);
        for (var i = 0; i < 100; i++) history.Add(Sample(i) with { MonotonicTime = TimeSpan.FromMilliseconds(i) });
        Assert.Equal(60, history.Points.Count);
    }

    private sealed class Provider : IHardwareTelemetryProvider
    {
        public int Reads, Resets, Active, Peak;
        public Func<int, CancellationToken, ValueTask<HardwareTelemetrySnapshot>>? OnRead;
        public void Reset() => Interlocked.Increment(ref Resets);
        public async ValueTask<HardwareTelemetrySnapshot> ReadAsync(CancellationToken token)
        {
            var active = Interlocked.Increment(ref Active); Peak = Math.Max(Peak, active);
            try
            {
                var count = Interlocked.Increment(ref Reads);
                return OnRead is null ? Sample(count) : await OnRead(count, token);
            }
            finally { Interlocked.Decrement(ref Active); }
        }
    }
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(5, timeout.Token);
    }
    [Fact] public async Task ConsumersShareOneWorkerAndReleaseStopsCollection()
    {
        var provider = new Provider();
        await using var service = new HardwareMonitoringService(provider, TimeSpan.FromMilliseconds(20));
        var a = 0; var b = 0;
        var first = service.Subscribe(_ => Interlocked.Increment(ref a));
        using var second = service.Subscribe(_ => Interlocked.Increment(ref b));
        await Until(() => Volatile.Read(ref b) >= 3);
        Assert.Equal(2, service.ConsumerCount); Assert.Equal(1, provider.Resets); Assert.Equal(1, provider.Peak);
        first.Dispose(); var stopped = a;
        await Until(() => Volatile.Read(ref b) >= 5); Assert.Equal(stopped, a);
        second.Dispose(); await Until(() => !service.IsRunning);
        var reads = provider.Reads; await Task.Delay(50); Assert.Equal(reads, provider.Reads); Assert.Equal(0, service.ConsumerCount);
    }
    [Fact] public async Task CancellationInterruptsReadAndReacquisitionNeverOverlaps()
    {
        var provider = new Provider { OnRead = async (n, token) => { await Task.Delay(100, token); return Sample(n); } };
        await using var service = new HardwareMonitoringService(provider, TimeSpan.FromMilliseconds(20));
        var lease = service.Subscribe(_ => { }); await Until(() => provider.Active == 1);
        lease.Dispose();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var next = service.Subscribe(_ => received.TrySetResult());
        await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, provider.Peak);
        await service.DisposeAsync(); Assert.Equal(0, provider.Active); Assert.False(service.IsRunning);
        Assert.Throws<ObjectDisposedException>(() => service.Subscribe(_ => { }));
    }
    [Fact] public async Task ProviderFailureIsReportedAndNextSampleCanRecover()
    {
        var provider = new Provider { OnRead = (n, _) => n == 1 ? throw new InvalidOperationException("test failure") : ValueTask.FromResult(Sample(n)) };
        var observed = new ConcurrentQueue<HardwareTelemetrySnapshot>();
        var messages = new ConcurrentQueue<string>();
        await using var service = new HardwareMonitoringService(provider, TimeSpan.FromMilliseconds(20), (m, _) => messages.Enqueue(m));
        using var lease = service.Subscribe(observed.Enqueue);
        await Until(() => observed.Count >= 2);
        Assert.Equal(TelemetryStatus.Error, observed.First().Cpu.TotalUtilizationPercent.Status);
        Assert.Equal(TelemetryStatus.Available, observed.ElementAt(1).Memory.UsedPhysicalPercent.Status);
        Assert.Contains("Monitoring provider failed", messages);
    }
    private sealed class QueueDispatcher : IUiDispatcher
    {
        public readonly ConcurrentQueue<Action> Actions = new();
        public bool Post(Action action) { Actions.Enqueue(action); return true; }
        public void Drain() { while (Actions.TryDequeue(out var action)) action(); }
    }
    [Fact] public async Task ViewModelAppliesLatestSnapshotAndIgnoresQueuedWorkAfterDeactivation()
    {
        var dispatcher = new QueueDispatcher(); var provider = new Provider(); var text = new TextCatalog();
        await using var service = new HardwareMonitoringService(provider, TimeSpan.FromMilliseconds(20));
        var vm = new MonitoringViewModel(service, text, dispatcher);
        Assert.Equal("—", vm.CpuPercent); Assert.Empty(vm.History.Points);
        vm.SetActive(true); vm.SetActive(true); Assert.Equal(1, service.ConsumerCount);
        await Until(() => service.SampleCount >= 3); dispatcher.Drain();
        Assert.NotNull(vm.Snapshot); Assert.Equal("25,0 %", vm.CpuPercent); Assert.Equal("16,00 GiB", vm.TotalMemory); Assert.Equal("128,0 MiB", vm.WorkingSet);
        text.SetLanguage("en-US"); vm.RefreshLanguage(); Assert.Equal("25.0 %", vm.CpuPercent);
        await Until(() => dispatcher.Actions.Count > 0);
        var snapshot = vm.Snapshot; vm.SetActive(false); dispatcher.Drain(); Assert.Same(snapshot, vm.Snapshot);
        await Until(() => !service.IsRunning); Assert.Equal(0, service.ConsumerCount);
    }
    [Fact] public async Task MissingSnapshotsNeverDisplayInventedValues()
    {
        var dispatcher = new QueueDispatcher(); var provider = new Provider { OnRead = (_, _) => ValueTask.FromResult(HardwareTelemetrySnapshot.Missing(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1), TelemetryStatus.AccessDenied)) };
        await using var service = new HardwareMonitoringService(provider, TimeSpan.FromMilliseconds(20));
        var vm = new MonitoringViewModel(service, new TextCatalog(), dispatcher); vm.SetActive(true);
        // A completed read is counted before delivery. Wait for the UI work itself,
        // otherwise this assertion races the collector's notification.
        await Until(() => !dispatcher.Actions.IsEmpty); dispatcher.Drain();
        Assert.Equal("—", vm.CpuPercent); Assert.Equal("—", vm.TotalMemory); Assert.Equal("Acesso negado", vm.CpuStatus); Assert.Null(vm.History.Points[0].CpuPercent);
        vm.SetActive(false);
    }
    [Fact] public async Task NativeWindowsSourcesReturnActualDataAndResetCpuBaseline()
    {
        if (!OperatingSystem.IsWindows()) return;
        var provider = new WindowsHardwareTelemetryProvider();
        var first = await provider.ReadAsync(CancellationToken.None);
        Assert.Null(first.Cpu.TotalUtilizationPercent.Value);
        Assert.True(first.Memory.TotalPhysicalBytes.Value > 0);
        Assert.True(first.Cpu.LogicalProcessors > 0); Assert.True(first.Cpu.PhysicalCores > 0);
        Assert.Null(first.Cpu.EffectiveFrequencyMHz.Value);
        await Task.Delay(120);
        var second = await provider.ReadAsync(CancellationToken.None);
        if (second.Cpu.TotalUtilizationPercent.Status == TelemetryStatus.Available) Assert.InRange(second.Cpu.TotalUtilizationPercent.Value!.Value, 0, 100);
        Assert.True(second.Memory.LotusWorkingSetBytes.Value > 0);
        output.WriteLine(System.Text.Json.JsonSerializer.Serialize(second));
        provider.Reset(); Assert.Null((await provider.ReadAsync(CancellationToken.None)).Cpu.TotalUtilizationPercent.Value);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        OperationCanceledException? failure = null;
        try { await provider.ReadAsync(canceled.Token); }
        catch (OperationCanceledException ex) { failure = ex; }
        Assert.NotNull(failure);
    }
}
