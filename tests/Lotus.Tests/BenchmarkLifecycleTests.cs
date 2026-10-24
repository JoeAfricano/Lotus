using Lotus.Core;
using Lotus.Core.Benchmark;
using Lotus.Core.Telemetry;
using Xunit;

namespace Lotus.Tests;

public sealed class BenchmarkLifecycleTests
{
    private sealed class Frames : IFrameCaptureProvider
    {
        public int Calls;
        public bool Fail;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<BenchmarkSession> CaptureAsync(CaptureRequest request, CancellationToken stop)
        {
            Calls++; Entered.TrySetResult();
            if (Fail) throw new IOException("test failure");
            await Task.Delay(Timeout.Infinite, stop);
            throw new InvalidOperationException();
        }
    }
    private sealed class Repository : IBenchmarkRepository
    {
        public Task SaveAsync(BenchmarkSession session, CancellationToken token = default) => Task.CompletedTask;
        public Task<IReadOnlyList<BenchmarkSession>> ListAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<BenchmarkSession>>([]);
    }
    private sealed class Targets : ICaptureTargetSource
    {
        public Task<IReadOnlyList<CaptureTarget>> ListAsync() => Task.FromResult<IReadOnlyList<CaptureTarget>>([]);
    }
    private sealed class Hardware : IHardwareTelemetryProvider
    {
        public void Reset() { }
        public ValueTask<HardwareTelemetrySnapshot> ReadAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Benchmark must not activate hardware polling.");
    }
    private sealed class Dispatcher : IUiDispatcher { public bool Post(Action action) { action(); return true; } }
    private static BenchmarkViewModel Create(Frames frames, HardwareMonitoringService monitoring, string language = "pt-BR")
    {
        var text = new TextCatalog(language);
        return new(frames, new Repository(), new Targets(), new MonitoringViewModel(monitoring,text,new Dispatcher()),text)
        { SelectedTarget = new(1,"game.exe",DateTimeOffset.UtcNow) };
    }
    [Fact] public async Task StartIsSingleAndShutdownCancelsWithoutHardwarePolling()
    {
        await using var monitoring = new HardwareMonitoringService(new Hardware()); var provider = new Frames(); var vm = Create(provider,monitoring);
        var run = vm.StartAsync(); await provider.Entered.Task;
        Assert.True(vm.Busy); Assert.False(vm.StartCommand.CanExecute(null));
        await vm.StartAsync(); Assert.Equal(1,provider.Calls);
        await vm.ShutdownAsync(); await run; Assert.False(vm.Busy); Assert.Equal("Captura cancelada.",vm.Status);
    }
    [Fact] public async Task ProviderFailureDoesNotEscapeAndAllowsRetry()
    {
        await using var monitoring = new HardwareMonitoringService(new Hardware()); var provider = new Frames { Fail = true }; var vm = Create(provider,monitoring);
        await vm.StartAsync(); Assert.False(vm.Busy); Assert.True(vm.StartCommand.CanExecute(null));
        Assert.Contains("Não foi possível",vm.Status);
        await vm.StartAsync(); Assert.Equal(2,provider.Calls);
    }
    [Fact] public async Task ExplicitStopCancelsAndLanguageRemainsLive()
    {
        await using var monitoring = new HardwareMonitoringService(new Hardware()); var provider = new Frames(); var vm = Create(provider,monitoring,"en-US");
        var run=vm.StartAsync(); await provider.Entered.Task; vm.StopCommand.Execute(null); await run;
        Assert.Equal("Capture cancelled.",vm.Status); vm.Text.SetLanguage("pt-BR"); vm.RefreshLanguage(); Assert.Equal("Captura cancelada.",vm.Status);
    }
}
