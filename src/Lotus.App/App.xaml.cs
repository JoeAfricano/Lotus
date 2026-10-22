using Lotus.Core;
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
    public App() => InitializeComponent();
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
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
            .AddSingleton<IHardwareTelemetryProvider, WindowsHardwareTelemetryProvider>()
            .AddSingleton(provider => new HardwareMonitoringService(provider.GetRequiredService<IHardwareTelemetryProvider>(),
                diagnostic: (message, error) => { if (error is null) Log.Information("{MonitoringEvent}", message); else Log.Warning(error, "{MonitoringEvent}", message); }))
            .AddSingleton<IUiDispatcher>(_ => new UiDispatcher(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()))
            .AddSingleton<MonitoringViewModel>()
            .AddSingleton<IAppEvents, AppEvents>().AddSingleton<ShellViewModel>().AddSingleton<MainWindow>().BuildServiceProvider();
        Text = services.GetRequiredService<ITextCatalog>();
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo(Text.Language);
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(Text.Language);
        var preferences = services.GetRequiredService<PreferencesService>();
        services.GetRequiredService<ThemePalette>().Apply(preferences.Current.Theme);
        Log.Information("Preferences initialized {Status} {Language} {Theme}", preferences.LoadStatus, Text.Language, preferences.Current.Theme);
        UnhandledException += (_, e) => Log.Fatal(e.Exception, "Unhandled UI exception");
        window = services.GetRequiredService<MainWindow>();
        var readyToClose = false;
        var closeRequested = false;
        window.AppWindow.Closing += async (_, e) =>
        {
            if (readyToClose) return;
            e.Cancel = true;
            if (closeRequested) return;
            closeRequested = true;
            ((MainWindow)window).PrepareForClose();
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
