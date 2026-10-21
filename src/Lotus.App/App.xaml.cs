using Lotus.Core;
using Lotus.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Serilog;
using Serilog.Formatting.Json;

namespace Lotus.App;

public partial class App : Application
{
    private ServiceProvider? services;
    private Window? window;
    public App() => InitializeComponent();
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "Logs", "lotus-.jsonl");
        Log.Logger = new LoggerConfiguration().WriteTo.File(new JsonFormatter(), logPath,
            rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7, fileSizeLimitBytes: 5_000_000, rollOnFileSizeLimit: true).CreateLogger();
        services = new ServiceCollection().AddSingleton<Serilog.ILogger>(Log.Logger)
            .AddSingleton<IAppEvents, AppEvents>().AddSingleton<ShellViewModel>().AddSingleton<MainWindow>().BuildServiceProvider();
        UnhandledException += (_, e) => Log.Fatal(e.Exception, "Unhandled UI exception");
        window = services.GetRequiredService<MainWindow>();
        window.Closed += (_, _) => { Log.Information("Application closed"); services.Dispose(); Log.CloseAndFlush(); };
        Log.Information("Application started {Version}", "0.1.0");
        window.Activate();
    }
}
