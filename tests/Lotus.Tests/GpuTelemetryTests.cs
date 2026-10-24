using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text.Json;
using Lotus.Core;
using Lotus.Core.Telemetry;
using Lotus.Infrastructure.Telemetry;
using Xunit;
using Xunit.Abstractions;

namespace Lotus.Tests;

public sealed class NvmlFactAttribute : FactAttribute
{
    public NvmlFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(Path.Combine(Environment.SystemDirectory, "nvml.dll")))
            Skip = "Requer Windows com NVML instalada pelo driver NVIDIA.";
    }
}

public sealed class GpuTelemetryTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Time = DateTimeOffset.UtcNow;
    private static GpuTelemetrySnapshot Device(string id = "test GPU", double? percent = 20) => new(id, "fake only in tests", "test",
        new ReadOnlyDictionary<GpuSensor, SensorReading>(new Dictionary<GpuSensor, SensorReading>
        {
            [GpuSensor.Utilization] = new(percent, SensorUnit.Percent, percent.HasValue ? TelemetryStatus.Available : TelemetryStatus.Unsupported, "test", Time),
            [GpuSensor.MemoryPercent] = new(50, SensorUnit.Percent, TelemetryStatus.Available, "test", Time),
            [GpuSensor.MemoryTotal] = new(8L << 30, SensorUnit.Bytes, TelemetryStatus.Available, "test", Time),
            [GpuSensor.MemoryUsed] = new(4L << 30, SensorUnit.Bytes, TelemetryStatus.Available, "test", Time)
        }));

    [Theory]
    [InlineData(-1)][InlineData(101)][InlineData(double.NaN)][InlineData(double.PositiveInfinity)]
    public void InvalidSensorsHaveNoNumericValue(double value)
    {
        var reading = SensorReading.Create(value, SensorUnit.Percent, "test", Time, 100);
        Assert.Null(reading.Value); Assert.Equal(TelemetryStatus.Invalid, reading.Status);
        Assert.Equal(Time, reading.CapturedAt); Assert.Equal(SensorUnit.Percent, reading.Unit);
    }
    [Theory]
    [InlineData(0, TelemetryStatus.Available)][InlineData(3, TelemetryStatus.Unsupported)]
    [InlineData(4, TelemetryStatus.AccessDenied)][InlineData(9, TelemetryStatus.Unavailable)]
    [InlineData(15, TelemetryStatus.Error)][InlineData(13, TelemetryStatus.Unsupported)]
    public void NativeStatusesAreExplicit(int code, TelemetryStatus status) => Assert.Equal(status, NvmlGpuTelemetryProvider.Status(code));
    [Fact] public void SnapshotKeepsUnitsTimestampIdentityAndMissingData()
    {
        var device = Device();
        Assert.Equal("test GPU", device.Id); Assert.Null(device.Get(GpuSensor.Temperature));
        Assert.Equal(Time, device.Get(GpuSensor.MemoryUsed)!.CapturedAt);
        var vm = new GpuViewModel(device, new TextCatalog());
        Assert.Equal("8,00 GiB", vm.MemoryTotal); Assert.Equal("4,00 GiB", vm.MemoryUsed); Assert.Equal("4,00 GiB / 8,00 GiB", vm.MemorySummary);
        Assert.Equal("—", vm.MemoryFree);
        Assert.All(vm.Sensors, s => Assert.Equal("—", s.Value));
    }
    [Fact] public void HistoryTracksTimeAvailabilityAndIsBounded()
    {
        var vm = new GpuViewModel(Device(), new TextCatalog());
        for (var i = 0; i < 150; i++) vm.Update(Device(), TimeSpan.FromSeconds(i));
        Assert.Equal(60, vm.UtilizationHistory.Count);
        Assert.Equal(TimeSpan.FromSeconds(90), vm.UtilizationHistory[0].Time);
        vm.Update(Device(percent: null), TimeSpan.FromSeconds(151));
        Assert.Null(vm.UtilizationHistory[^1].Value); Assert.Equal("—", vm.Utilization);
        vm.Update(Device(percent: 150), TimeSpan.FromSeconds(152));
        Assert.Null(vm.UtilizationHistory[^1].Value);
        vm.Update(Device(), TimeSpan.FromSeconds(1000)); Assert.Single(vm.UtilizationHistory);
        vm.Update(Device(), TimeSpan.FromSeconds(999)); Assert.Single(vm.UtilizationHistory);
        for (var i = 1; i < 200; i++) vm.Update(Device(), TimeSpan.FromSeconds(1000) + TimeSpan.FromMilliseconds(i));
        Assert.Equal(60, vm.UtilizationHistory.Count);
    }
    [Fact] public void SensorsAndLanguageRefreshWithoutReplacingIdentity()
    {
        var text = new TextCatalog(); var vm = new GpuViewModel(Device(), text);
        vm.Update(Device(), TimeSpan.FromSeconds(1)); Assert.Equal("20,0 %", vm.Utilization);
        text.SetLanguage("en-US"); vm.RefreshLanguage(); Assert.Equal("20.0 %", vm.Utilization);
        Assert.Contains("temperature", vm.Sensors[0].Label);
    }
    private sealed class SystemProvider : IHardwareTelemetryProvider
    {
        public int Reads; public bool Fail;
        public void Reset() { }
        public ValueTask<HardwareTelemetrySnapshot> ReadAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Fail) throw new InvalidOperationException("system failure only in tests");
            return ValueTask.FromResult(HardwareTelemetrySnapshot.Missing(Time, TimeSpan.FromSeconds(++Reads), TelemetryStatus.Unavailable)
                with { Cpu = new("test", 4, 8, Reading<double>.Available(25, "test"), Reading<double>.Available(1, "test"), Reading<double>.Missing(TelemetryStatus.Unsupported, "test")) });
        }
    }
    private sealed class GpuProvider : IGpuTelemetryProvider
    {
        public int Reads, Stops; public bool Fail;
        public Func<int, GpuTelemetryResult>? Result;
        public GpuTelemetryResult Read(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); var n = Interlocked.Increment(ref Reads);
            if (Fail) throw new InvalidOperationException("GPU failure only in tests");
            return Result?.Invoke(n) ?? new(new[] { Device("A"), Device("B") }, TelemetryStatus.Available, "test");
        }
        public void Stop() => Interlocked.Increment(ref Stops);
    }
    [Fact] public async Task GpuFailureCannotEraseCpuAndNextReadingRecovers()
    {
        var gpu = new GpuProvider { Fail = true }; var diagnostics = 0;
        var provider = new CompositeHardwareTelemetryProvider(new SystemProvider(), gpu, _ => diagnostics++);
        var failed = await provider.ReadAsync(default);
        Assert.Equal(25, failed.Cpu.TotalUtilizationPercent.Value);
        Assert.Equal(TelemetryStatus.Error, failed.Gpu.Status); Assert.Empty(failed.Gpu.Devices);
        await provider.ReadAsync(default); Assert.Equal(1, diagnostics);
        gpu.Fail = false;
        var recovered = await provider.ReadAsync(default);
        Assert.Equal(2, recovered.Gpu.Devices.Count); Assert.Equal("A", recovered.Gpu.Devices[0].Id);
        Assert.Equal("B", recovered.Gpu.Devices[1].Id);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await provider.ReadAsync(canceled.Token));
        Assert.Equal(3, gpu.Reads);
    }
    [Fact] public async Task SystemFailureDoesNotEraseGpu()
    {
        var system = new SystemProvider { Fail = true };
        var gpu = new GpuProvider();
        var provider = new CompositeHardwareTelemetryProvider(system, gpu);
        var result = await provider.ReadAsync(default);
        Assert.Equal(TelemetryStatus.Error, result.Cpu.TotalUtilizationPercent.Status);
        Assert.Equal(2, result.Gpu.Devices.Count);
        system.Fail = false;
        Assert.Equal(25, (await provider.ReadAsync(default)).Cpu.TotalUtilizationPercent.Value);
        provider.Stop();
    }
    [Fact] public void ExplicitThermalAndPowerFlagsRemainSeparateAndZeroFanIsReal()
    {
        var device = Device();
        var sensors = new Dictionary<GpuSensor, SensorReading>(device.Sensors)
        {
            [GpuSensor.ClockEventReasons] = SensorReading.Create(0x24, SensorUnit.Flags, "test", Time),
            [GpuSensor.Fan] = SensorReading.Create(0, SensorUnit.Percent, "test", Time),
            [GpuSensor.Power] = SensorReading.Create(21.03, SensorUnit.Watts, "test", Time),
            [GpuSensor.CoreClock] = SensorReading.Create(2520, SensorUnit.MHz, "test", Time)
        };
        var vm = new GpuViewModel(device with { Sensors = new ReadOnlyDictionary<GpuSensor, SensorReading>(sensors) }, new TextCatalog());
        var reasons = vm.Sensors.Single(s => s.Label.Contains("clock reportados")).Value;
        Assert.Contains("térmico", reasons); Assert.Contains("potência", reasons);
        Assert.Equal("0,0 %", vm.Sensors.Single(s => s.Label.Contains("Ventoinha")).Value);
        Assert.Equal("21,03 W", vm.Sensors.Single(s => s.Label == "Potência reportada pelo driver").Value);
        Assert.Equal("2520 MHz", vm.Sensors.Single(s => s.Label.Contains("Clock gráfico")).Value);
    }
    [NvmlFact] [Trait("Category", "Hardware")]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task NativeGpuContinuousSessionKeepsHistoryBoundedAndReleases()
    {
        var seconds = int.TryParse(Environment.GetEnvironmentVariable("LOTUS_GPU_SOAK_SECONDS"), out var configured)
            ? Math.Clamp(configured, 2, 3600) : 2;
        var gpu = new NvmlGpuTelemetryProvider();
        await using var service = new HardwareMonitoringService(new CompositeHardwareTelemetryProvider(new WindowsHardwareTelemetryProvider(), gpu));
        var history = new TelemetryHistory();
        GpuViewModel? gpuView = null;
        HardwareTelemetrySnapshot? last = null;
        var count = 0; var maxHistory = 0; var errors = 0;
        var started = System.Diagnostics.Stopwatch.StartNew();
        using var lease = service.Subscribe(snapshot =>
        {
            last = snapshot; history.Add(snapshot);
            if (snapshot.Gpu.Status != TelemetryStatus.Available) errors++;
            foreach (var device in snapshot.Gpu.Devices)
            {
                gpuView ??= new(device, new TextCatalog());
                gpuView.Update(device, snapshot.MonotonicTime);
                maxHistory = Math.Max(maxHistory, gpuView.UtilizationHistory.Count);
            }
            Interlocked.Increment(ref count);
        });
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        lease.Dispose(); await service.DisposeAsync();
        Assert.True(count >= Math.Max(1, seconds - 2)); Assert.Equal(0, errors);
        Assert.NotNull(last); Assert.NotEmpty(last.Gpu.Devices);
        Assert.InRange(maxHistory, 1, 60); Assert.InRange(history.Points.Count, 1, 60);
        Assert.False(service.IsRunning); Assert.Equal(0, service.ConsumerCount);
        var stopped = count; await Task.Delay(100); Assert.Equal(stopped, count);
        output.WriteLine(JsonSerializer.Serialize(new { DurationSeconds = started.Elapsed.TotalSeconds, Samples = count,
            Errors = errors, MaxGpuHistory = maxHistory, CpuHistory = history.Points.Count, Last = last }));
    }

    private sealed class Dispatcher : IUiDispatcher
    {
        public readonly ConcurrentQueue<Action> Queue = new();
        public bool Post(Action action) { Queue.Enqueue(action); return true; }
        public void Drain() { while (Queue.TryDequeue(out var action)) action(); }
    }
    [Fact] public async Task ViewModelHandlesMultipleGpusDisappearanceAndRelease()
    {
        var gpu = new GpuProvider(); var dispatcher = new Dispatcher();
        await using var service = new HardwareMonitoringService(new CompositeHardwareTelemetryProvider(new SystemProvider(), gpu), TimeSpan.FromMilliseconds(20));
        var vm = new MonitoringViewModel(service, new TextCatalog(), dispatcher);
        vm.SetActive(true); await Task.Delay(100); dispatcher.Drain();
        Assert.Equal(2, vm.Gpus.Count);
        var original = vm.Gpus[0];
        await Task.Delay(60); dispatcher.Drain(); Assert.Same(original, vm.Gpus[0]);
        gpu.Result = _ => GpuTelemetryResult.Missing(TelemetryStatus.Unavailable);
        await Task.Delay(80); dispatcher.Drain(); Assert.Empty(vm.Gpus); Assert.Equal("25,0 %", vm.CpuPercent);
        vm.SetActive(false); await service.DisposeAsync();
        Assert.True(gpu.Stops >= 1); var reads = gpu.Reads;
        await Task.Delay(50); Assert.Equal(reads, gpu.Reads); Assert.False(service.IsRunning);
    }
    [Fact] public async Task SessionStopRunsExactlyOnceAndRestartReleasesAgain()
    {
        var gpu = new GpuProvider();
        await using var service = new HardwareMonitoringService(new CompositeHardwareTelemetryProvider(new SystemProvider(), gpu), TimeSpan.FromMilliseconds(20));
        var first = service.Subscribe(_ => { }); await Task.Delay(80);
        first.Dispose(); await Task.Delay(80); Assert.Equal(1, gpu.Stops);
        using var second = service.Subscribe(_ => { }); await Task.Delay(80);
        await service.DisposeAsync(); Assert.Equal(2, gpu.Stops);
    }
    [NvmlFact] [Trait("Category", "Hardware")]
    public void InstalledNvmlReturnsRealReadingsAndSupportsStopRestart()
    {
        var provider = new NvmlGpuTelemetryProvider();
        try
        {
            var result = provider.Read(default);
            output.WriteLine(JsonSerializer.Serialize(result));
            Assert.Equal(TelemetryStatus.Available, result.Status); Assert.NotEmpty(result.Devices);
            foreach (var gpu in result.Devices)
            {
                Assert.Equal("NVIDIA", gpu.Vendor); Assert.NotEmpty(gpu.Id);
                Assert.Equal(TelemetryStatus.Available, gpu.Get(GpuSensor.Utilization)!.Status);
                Assert.InRange(gpu.Get(GpuSensor.Utilization)!.Value!.Value, 0, 100);
                Assert.True(gpu.Get(GpuSensor.MemoryTotal)!.Value > 0);
                Assert.NotEqual(default, gpu.Get(GpuSensor.Temperature)!.CapturedAt);
            }
            provider.Stop(); Assert.NotEmpty(provider.Read(default).Devices);
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => provider.Read(canceled.Token));
        }
        finally { provider.Stop(); }
    }
}
