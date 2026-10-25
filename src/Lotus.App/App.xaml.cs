using Lotus.Core;
using Lotus.Core.Benchmark;
using Lotus.Infrastructure.Benchmark;
using Lotus.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Serilog;
using Serilog.Formatting.Json;
using System.Globalization;
using Lotus.Core.Telemetry;
using Lotus.Infrastructure.Telemetry;

namespace Lotus.App;

public partial class App : Application
{
    public static ITextCatalog Text { get; private set; } = new TextCatalog();
    private ServiceProvider? services;
    private Window? window;
    // Needed by WinUI pickers in an unpackaged app.
    public static nint WindowHandle { get; private set; }
    public App() => InitializeComponent();
    // Reversible per-user feature actions offered by the profiles (same journal as the power plan).
    private static Dictionary<string, Lotus.Core.Optimization.OptimizationEngine> FeatureEngines(Lotus.Infrastructure.Optimization.SqliteOptimizationJournal journal)
    {
        var registry = new Lotus.Infrastructure.Optimization.WindowsUserRegistry();
        return new()
        {
            ["background-recording"] = new(journal, Lotus.Core.Optimization.RegistryDwordAction.BackgroundRecording(registry)),
            ["gamemode"] = new(journal, Lotus.Core.Optimization.RegistryDwordAction.GameMode(registry)),
            [Lotus.Core.Optimization.DisplayRefreshAction.FeatureId] = new(journal, new Lotus.Core.Optimization.DisplayRefreshAction(new Lotus.Infrastructure.Optimization.WindowsDisplayModes()))
        };
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Short-lived elevated helper for RivaTuner profile edits: no window, no services, fixed allow-list.
        if (Lotus.Infrastructure.Integrations.RtssElevatedHelper.TryRun(Environment.GetCommandLineArgs()) is { } helperExit) { Environment.Exit(helperExit); return; }
        // Short-lived elevated helper for the DNS switch (fixed provider list, backup in the user's Lotus folder).
        if (Lotus.Infrastructure.Tools.DnsElevatedHelper.TryRun(Environment.GetCommandLineArgs()) is { } dnsExit) { Environment.Exit(dnsExit); return; }
        // Overlay preview: a separate, unelevated process that only shows the 3D scene so RivaTuner draws its overlay on it.
        if (Environment.GetCommandLineArgs().Contains(Lotus.Infrastructure.Score.D3D11GpuBenchmark.PreviewArgument))
        {
            var ok = false;
            try { ok = Lotus.Infrastructure.Score.D3D11GpuBenchmark.Preview(); } catch (Exception) { }
            Environment.Exit(ok ? 0 : 2); return;
        }
        var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "Logs", "lotus-.jsonl");
        Log.Logger = new LoggerConfiguration().WriteTo.File(new JsonFormatter(), logPath,
            rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7, fileSizeLimitBytes: 5_000_000, rollOnFileSizeLimit: true).CreateLogger();
        services = new ServiceCollection().AddSingleton<Serilog.ILogger>(Log.Logger)
            .AddSingleton<IPreferencesStore>(_ => new JsonPreferencesStore(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "preferences.json"), Log.Logger))
            .AddSingleton<PreferencesService>()
            .AddSingleton(_ => new ThemePalette(Resources))
            .AddSingleton<ITextCatalog>(provider => new TextCatalog(provider.GetRequiredService<PreferencesService>().Current.Language))
            .AddSingleton<PreferencesViewModel>()
            .AddSingleton<WindowsHardwareTelemetryProvider>()
            .AddSingleton<IGpuTelemetryProvider, NvmlGpuTelemetryProvider>()
            .AddSingleton<IHardwareTelemetryProvider>(provider => new CompositeHardwareTelemetryProvider(
                provider.GetRequiredService<WindowsHardwareTelemetryProvider>(), provider.GetRequiredService<IGpuTelemetryProvider>(),
                error => Log.Warning(error, "Telemetry source failed")))
            .AddSingleton(provider => new HardwareMonitoringService(provider.GetRequiredService<IHardwareTelemetryProvider>(),
                diagnostic: (message, error) => { if (error is null) Log.Information("{MonitoringEvent}", message); else Log.Warning(error, "{MonitoringEvent}", message); }))
            .AddSingleton<IUiDispatcher>(_ => new UiDispatcher(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()))
            .AddSingleton<MonitoringViewModel>()
            .AddSingleton<Lotus.Core.Diagnostics.MemoryAnalysisViewModel>()
            .AddSingleton<ExperienceViewModel>()
            .AddSingleton<ICaptureTargetSource, WindowsCaptureTargets>()
            .AddSingleton<IFrameCaptureProvider>(_ => new PresentMonCaptureProvider(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "Benchmarks"), Log.Logger))
            .AddSingleton<IBenchmarkRepository>(_ => new SqliteBenchmarkRepository(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "benchmarks.db")))
            .AddSingleton(p => new BenchmarkViewModel(p.GetRequiredService<IFrameCaptureProvider>(),
                p.GetRequiredService<IBenchmarkRepository>(), p.GetRequiredService<ICaptureTargetSource>(),
                p.GetRequiredService<MonitoringViewModel>(), p.GetRequiredService<ITextCatalog>(), ex => Log.Warning(ex, "Benchmark operation failed")))
            .AddSingleton(_ => new Lotus.Infrastructure.Optimization.DisposableOptimizationStore(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "OptimizerSandbox", "journal.db")))
            .AddSingleton(p => new Lotus.Core.Optimization.OptimizationEngine(
                p.GetRequiredService<Lotus.Infrastructure.Optimization.DisposableOptimizationStore>(),
                p.GetRequiredService<Lotus.Infrastructure.Optimization.DisposableOptimizationStore>()))
            .AddSingleton(p => new Lotus.Core.Optimization.OptimizationViewModel(
                p.GetRequiredService<Lotus.Core.Optimization.OptimizationEngine>(), p.GetRequiredService<ITextCatalog>(),
                ex => Log.Warning(ex, "Optimizer test operation failed")))
            .AddSingleton<Lotus.Core.Optimization.IPowerSchemeService, Lotus.Infrastructure.Optimization.WindowsPowerSchemes>()
            .AddSingleton<Lotus.Core.Optimization.IStartupInventory, Lotus.Infrastructure.Optimization.WindowsStartupInventory>()
            .AddSingleton(_ => new Lotus.Infrastructure.Optimization.SqliteOptimizationJournal(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "Optimizer", "journal.db")))
            .AddSingleton(p => new Lotus.Core.Optimization.PowerProfilesViewModel(
                new Lotus.Core.Optimization.OptimizationEngine(p.GetRequiredService<Lotus.Infrastructure.Optimization.SqliteOptimizationJournal>(),
                    new Lotus.Core.Optimization.PowerSchemeAction(p.GetRequiredService<Lotus.Core.Optimization.IPowerSchemeService>())),
                p.GetRequiredService<Lotus.Core.Optimization.IPowerSchemeService>(), p.GetRequiredService<Lotus.Infrastructure.Optimization.SqliteOptimizationJournal>(),
                p.GetRequiredService<Lotus.Core.Optimization.IStartupInventory>(), p.GetRequiredService<ITextCatalog>(), ex => Log.Warning(ex, "Power profile operation failed"), new Lotus.Infrastructure.Optimization.WindowsGameFeatures(new Lotus.Infrastructure.Optimization.WindowsDisplayModes()),
                new Lotus.Core.Optimization.OptimizationEngine(p.GetRequiredService<Lotus.Infrastructure.Optimization.SqliteOptimizationJournal>(),
                    new Lotus.Core.Optimization.WindowedOptimizationsAction(new Lotus.Infrastructure.Optimization.WindowsDirectXUserSettings())),
                FeatureEngines(p.GetRequiredService<Lotus.Infrastructure.Optimization.SqliteOptimizationJournal>()),
                new Lotus.Infrastructure.Optimization.WindowsStartupApprovalStore(),
                action => new Lotus.Core.Optimization.OptimizationEngine(p.GetRequiredService<Lotus.Infrastructure.Optimization.SqliteOptimizationJournal>(), action)))
            .AddSingleton(p => new Lotus.Core.Diagnostics.HardwareDiagnosticsViewModel(new Lotus.Infrastructure.Diagnostics.WindowsHardwareFacts(),
                p.GetRequiredService<MonitoringViewModel>(), p.GetRequiredService<ITextCatalog>(), ex => Log.Warning(ex, "Hardware diagnostics failed")))
            .AddSingleton(p => new Lotus.Core.Games.GamesViewModel(new Lotus.Infrastructure.Games.WindowsGameLibrary(),
                new Lotus.Infrastructure.Games.JsonManualGameStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "games.json")),
                new Lotus.Infrastructure.Games.WindowsGameLauncher(), p.GetRequiredService<ITextCatalog>(), ex => Log.Warning(ex, "Games operation failed"),
                new Lotus.Infrastructure.Games.WindowsSystemProfile(new Lotus.Infrastructure.Optimization.WindowsDisplayModes()),
                [new Lotus.Infrastructure.Games.UnrealEngineRecipe(), new Lotus.Infrastructure.Games.NoRestForTheWickedRecipe()],
                new Lotus.Infrastructure.Games.FileGameSettingsStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "GameSettingsBackups")),
                new Lotus.Infrastructure.Games.WindowsGameProcessCheck(),
                new Lotus.Infrastructure.Games.RtssPreferredCaptureProvider(p.GetRequiredService<IFrameCaptureProvider>()), new Lotus.Infrastructure.Games.PdhGpuLoadSampler(), new Lotus.Infrastructure.Games.ScsKillerCli()))
            .AddSingleton(p => new Lotus.Core.Integrations.IntegrationsViewModel(new Lotus.Infrastructure.Integrations.WindowsRtssIntegration(),
                new Lotus.Infrastructure.Integrations.WindowsToolDetector(), p.GetRequiredService<ITextCatalog>(), ex => Log.Warning(ex, "Integration operation failed"),
                () => { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, ArgumentList = { Lotus.Infrastructure.Score.D3D11GpuBenchmark.PreviewArgument } })?.Dispose(); return true; }))
            .AddSingleton(p => { var gpu = new Lotus.Infrastructure.Score.D3D11GpuBenchmark(); return new Lotus.Core.Score.LotusScoreViewModel(new Lotus.Infrastructure.Score.CpuRayBenchmark(), gpu,
                new Lotus.Infrastructure.Score.JsonScoreHistory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "score-history.json")),
                new Lotus.Infrastructure.Score.WindowsScoreMachine(gpu), p.GetRequiredService<ITextCatalog>(), ex => Log.Warning(ex, "Lotus Score failed")); })
            .AddSingleton(p => new Lotus.Core.Tools.ToolsViewModel(new Lotus.Infrastructure.Tools.WindowsAppCloser(), new Lotus.Infrastructure.Tools.WindowsTempCleaner(),
                new Lotus.Infrastructure.Tools.PingConnectionTester(), new Lotus.Infrastructure.Tools.WindowsDiskHealth(), new Lotus.Infrastructure.Tools.WindowsDriverInfo(),
                p.GetRequiredService<ITextCatalog>(), ex => Log.Warning(ex, "Tools operation failed"), new Lotus.Infrastructure.Tools.WindowsDnsService(), new Lotus.Infrastructure.Tools.TrustedDriverUpdates(),
                new Lotus.Core.Tools.UninstallerViewModel(new Lotus.Infrastructure.Tools.WindowsUninstaller(), p.GetRequiredService<ITextCatalog>(), ex => Log.Warning(ex, "Uninstaller failed"))))
            .AddSingleton<IAppEvents, AppEvents>().AddSingleton<ShellViewModel>().AddSingleton<MainWindow>().BuildServiceProvider();
        Text = services.GetRequiredService<ITextCatalog>();
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo(Text.Language);
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(Text.Language);
        var preferences = services.GetRequiredService<PreferencesService>();
        services.GetRequiredService<ThemePalette>().Apply(preferences.Current.Theme);
        Log.Information("Preferences initialized {Status} {Language} {Theme}", preferences.LoadStatus, Text.Language, preferences.Current.Theme);
        UnhandledException += (_, e) => Log.Fatal(e.Exception, "Unhandled UI exception");
        window = services.GetRequiredService<MainWindow>();
        WindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var readyToClose = false;
        var closeRequested = false;
        window.AppWindow.Closing += async (_, e) =>
        {
            if (readyToClose) return;
            e.Cancel = true;
            if (closeRequested) return;
            closeRequested = true;
            ((MainWindow)window).PrepareForClose();
            await services.GetRequiredService<BenchmarkViewModel>().ShutdownAsync();
            await services.GetRequiredService<HardwareMonitoringService>().DisposeAsync();
            readyToClose = true;
            window.Close();
        };
        window.Closed += async (_, _) =>
        {
            await services.GetRequiredService<HardwareMonitoringService>().DisposeAsync();
            Log.Information("Application closed"); await services.DisposeAsync(); Log.CloseAndFlush();
        };
        Log.Information("Application started {Version}", "0.1.0");
        window.Activate();
    }
}
