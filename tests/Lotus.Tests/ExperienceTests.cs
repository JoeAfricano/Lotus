using System.Collections.Concurrent;
using Lotus.Core;
using Lotus.Core.Diagnostics;
using Lotus.Core.Telemetry;
using Xunit;

namespace Lotus.Tests;

public sealed class ExperienceTests
{
    private sealed class Events : IAppEvents { public void Navigated(string id) { } }
    [Theory]
    [InlineData("dashboard", "home", "home")]
    [InlineData("analysis", "optimize", "analysis")]
    [InlineData("optimizations", "optimize", "analysis")]
    [InlineData("restore", "optimize", "restore")]
    [InlineData("monitoring", "performance", "monitoring")]
    [InlineData("benchmark", "performance", "benchmark")]
    [InlineData("history", "performance", "history")]
    [InlineData("games", "games", "games")]
    [InlineData("settings", "settings", "settings")]
    [InlineData("logs", "settings", "logs")]
    [InlineData("faq", "settings", "faq")]
    public void LegacyDestinationsMapToFiveGroups(string requested, string group, string route)
    {
        var shell = new ShellViewModel(new Events()); shell.NavigateCommand.Execute(requested);
        Assert.Equal(group, shell.SelectedSection.Id); Assert.Equal(route, shell.CurrentRoute); Assert.Equal(9, shell.Items.Count); // incl. Game list and diary (opened from Home, title bar and profile)
    }
    [Fact] public void InternalTabsAndLanguageKeepExactRoute()
    {
        var text = new TextCatalog(); var shell = new ShellViewModel(new Events(), text);
        shell.NavigateCommand.Execute("history"); Assert.Equal(2, shell.PerformanceTabIndex);
        text.SetLanguage("en-US"); shell.RefreshLanguage(); Assert.Equal("history", shell.CurrentRoute);
        shell.NavigateCommand.Execute("restore"); Assert.Equal(1, shell.OptimizeTabIndex);
        shell.SettingsCategoryIndex = 3; Assert.Equal("logs", shell.CurrentRoute);
        Assert.Equal("Advanced", shell.SettingsCategories[3]); Assert.Equal(5, shell.SettingsCategories.Count);
        shell.SettingsCategoryIndex = -1; Assert.Equal("logs", shell.CurrentRoute);
    }
    [Theory]
    [InlineData("home", true, false, true, false)]  // live readiness card
    [InlineData("analysis", true, false, false, false)]
    [InlineData("monitoring", true, false, true, false)]
    [InlineData("benchmark", true, false, false, false)]
    [InlineData("history", true, false, false, false)]
    [InlineData("home", true, true, true, true)]
    [InlineData("monitoring", false, true, false, false)]
    [InlineData("logs", true, false, false, false)]
    public void OnlyLiveViewsOrExplicitAnalysisDemandSensors(string route, bool visible, bool request, bool collect, bool analyze)
        => Assert.Equal(new MonitoringDemand(collect, analyze), MonitoringDemand.For(route, visible, request));

    private sealed class Dispatcher : IUiDispatcher
    {
        public readonly ConcurrentQueue<Action> Queue = new();
        public bool Post(Action action) { Queue.Enqueue(action); return true; }
        public void Drain() { while (Queue.TryDequeue(out var action)) action(); }
    }
    private sealed class Provider(ulong available = 4UL << 30, bool missing = false) : IHardwareTelemetryProvider
    {
        private int reads;
        public int Reads => Volatile.Read(ref reads);
        public void Reset() => reads = 0;
        public ValueTask<HardwareTelemetrySnapshot> ReadAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); var n = Interlocked.Increment(ref reads);
            var snapshot = HardwareTelemetrySnapshot.Missing(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(n), TelemetryStatus.Unavailable);
            if (!missing) snapshot = snapshot with { Cpu = snapshot.Cpu with { Model = "test CPU" }, Memory = snapshot.Memory with {
                TotalPhysicalBytes = Reading<ulong>.Available(16UL << 30, "test"), AvailablePhysicalBytes = Reading<ulong>.Available(available, "test") } };
            return ValueTask.FromResult(snapshot);
        }
    }
    private static async Task PumpUntil(Dispatcher dispatcher, Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) { dispatcher.Drain(); await Task.Delay(5, timeout.Token); }
        dispatcher.Drain();
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task AnalysisUsesRealRuleAndReleasesCollectorKeepingResult(bool low)
    {
        var dispatcher = new Dispatcher(); var text = new TextCatalog(); var shell = new ShellViewModel(new Events(), text);
        var provider = new Provider(low ? 200UL << 20 : 4UL << 30);
        await using var service = new HardwareMonitoringService(provider, TimeSpan.FromMilliseconds(20));
        var monitoring = new MonitoringViewModel(service, text, dispatcher);
        using var memory = new MemoryAnalysisViewModel(monitoring, text);
        using var vm = new ExperienceViewModel(monitoring, memory, text, shell);
        vm.DemandChanged += (_, _) => { memory.SetActive(vm.IsAnalyzing); monitoring.SetActive(vm.IsAnalyzing); };
        Assert.Equal(AnalysisExperienceState.NotAnalyzed, vm.State); Assert.False(service.IsRunning); Assert.Contains("disponível", vm.CpuModel);
        var run = vm.AnalyzeCommand.ExecuteAsync(null);
        Assert.True(vm.IsAnalyzing); Assert.False(vm.AnalyzeCommand.CanExecute(null)); Assert.Equal(1, service.ConsumerCount);
        await PumpUntil(dispatcher, () => run.IsCompleted); await run;
        Assert.Equal(AnalysisExperienceState.Completed, vm.State); Assert.Equal(low, vm.NeedsAttention);
        Assert.Equal("analysis", shell.CurrentRoute); Assert.InRange(vm.ObservationSeconds, 30, 30);
        Assert.True(vm.Assessment.ObservedDuration >= TimeSpan.FromSeconds(30));
        Assert.Equal(0, service.ConsumerCount); Assert.True(vm.HasResult); Assert.Equal("test CPU", vm.CpuModel);
        Assert.NotEqual(MemoryFinding.Unavailable, vm.Assessment.Finding); Assert.Equal(MemoryFinding.Unavailable, memory.Assessment.Finding);
        vm.ViewResultCommand.Execute(null); Assert.Equal("analysis", shell.CurrentRoute);
        text.SetLanguage("en-US"); vm.RefreshLanguage(); Assert.Contains("GiB", vm.Capacity);
        var saved = vm.Assessment; await Task.Delay(50); dispatcher.Drain(); Assert.Same(saved, vm.Assessment);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task TimeoutAndCancellationArePartialAndNeverSuccess(bool timeout)
    {
        var dispatcher = new Dispatcher(); var text = new TextCatalog();
        await using var service = new HardwareMonitoringService(new Provider(missing: true), TimeSpan.FromMilliseconds(20));
        var monitoring = new MonitoringViewModel(service, text, dispatcher);
        using var memory = new MemoryAnalysisViewModel(monitoring, text);
        using var vm = new ExperienceViewModel(monitoring, memory, text, new ShellViewModel(new Events()), TimeSpan.FromMilliseconds(100));
        vm.DemandChanged += (_, _) => { memory.SetActive(vm.IsAnalyzing); monitoring.SetActive(vm.IsAnalyzing); };
        var run = vm.AnalyzeCommand.ExecuteAsync(null); if (!timeout) vm.Interrupt();
        await PumpUntil(dispatcher, () => run.IsCompleted); await run;
        Assert.Equal(AnalysisExperienceState.Partial, vm.State); Assert.Null(vm.Assessment.AvailableBytes);
        Assert.Equal(0, service.ConsumerCount); Assert.Contains("tentar", vm.Heading);
        Assert.True(vm.AnalyzeCommand.CanExecute(null)); Assert.False(vm.IsAnalyzing);
    }
    private sealed class Store : IPreferencesStore
    {
        public PreferencesLoadResult Load() => new(new InterfacePreferences { ReduceMotion = true }, "Loaded");
        public bool TrySave(InterfacePreferences value) => true;
    }
    [Fact] public async Task ClockStopsWithOperationAndMissingDataDoesNotBecomeProgress()
    {
        var dispatcher = new Dispatcher(); var text = new TextCatalog();
        await using var service = new HardwareMonitoringService(new Provider(missing: true), TimeSpan.FromMilliseconds(20));
        var monitoring = new MonitoringViewModel(service, text, dispatcher);
        using var memory = new MemoryAnalysisViewModel(monitoring, text);
        using var vm = new ExperienceViewModel(monitoring, memory, text, new ShellViewModel(new Events()), TimeSpan.FromSeconds(5), new PreferencesService(new Store()));
        vm.DemandChanged += (_, _) => { memory.SetActive(vm.IsAnalyzing); monitoring.SetActive(vm.IsAnalyzing); };
        var ticks = 0; vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.ElapsedTime)) ticks++; };
        var run = vm.AnalyzeCommand.ExecuteAsync(null);
        await PumpUntil(dispatcher, () => ticks > 0);
        Assert.DoesNotContain("00:00", vm.ElapsedTime); Assert.Equal(0, vm.ObservationSeconds);
        Assert.False(vm.WaitingAnimation); // user's reduced-motion preference
        vm.CancelCommand.Execute(null); await run;
        var stoppedTicks = ticks; var stoppedTime = vm.ElapsedTime;
        await Task.Delay(1100); dispatcher.Drain();
        Assert.Equal(stoppedTicks, ticks); Assert.Equal(stoppedTime, vm.ElapsedTime);
        Assert.Equal(AnalysisExperienceState.Partial, vm.State); Assert.Equal(0, service.ConsumerCount);
    }

}
