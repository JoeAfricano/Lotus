using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Lotus.Core.Benchmark;
using Lotus.Infrastructure.Benchmark;
using Serilog;

// Real WPF/D3D presentation workload, not synthetic frame samples. Never part of the Lotus production app.
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var workloadOnly = args.Contains("--workload");
        var cancel = args.Contains("--stop");
        var exitTarget = args.Contains("--exit-target");
        var series = args.Contains("--series");
        if (series && (workloadOnly || cancel || exitTarget)) throw new ArgumentException("--series cannot be combined with lifecycle test modes.");
        var output = System.IO.Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "artifacts/benchmark/real-workload");
        Directory.CreateDirectory(output);
        var application = new Application();
        var canvas = new Canvas { Background = new SolidColorBrush(Color.FromRgb(12, 11, 18)) };
        var shape = new System.Windows.Shapes.Rectangle { Width = 120, Height = 120, Fill = Brushes.HotPink };
        canvas.Children.Add(shape); Canvas.SetTop(shape, 180);
        var translation = new TranslateTransform(); shape.RenderTransform = translation;
        translation.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(20, 620, TimeSpan.FromSeconds(2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        canvas.Children.Add(new TextBlock { Text = "Lotus · workload gráfico real de validação", Foreground = Brushes.White, FontSize = 22, Margin = new Thickness(20) });
        var window = new Window { Title = "Lotus Benchmark — validation workload", Width = 800, Height = 500, Content = canvas };
        if (series) window.ResizeMode = ResizeMode.NoResize;
        window.Loaded += async (_, _) =>
        {
            try
            {
                if (workloadOnly) { await Task.Delay(args.Contains("--short-target") ? TimeSpan.FromSeconds(6) : TimeSpan.FromMinutes(10)); return; }
                await Task.Delay(series ? 10000 : 1500);
                using var logger = new LoggerConfiguration().WriteTo.File(System.IO.Path.Combine(output, "capture.log")).CreateLogger();
                var provider = new PresentMonCaptureProvider(output, logger);
                using var child = exitTarget ? Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { Arguments = "--workload --short-target", UseShellExecute = false }) : null;
                if (child is not null) await Task.Delay(500);
                using var current = child ?? Process.GetCurrentProcess();
                var target = new CaptureTarget(current.Id, current.MainModule!.FileName!, current.StartTime.ToUniversalTime());
                for (var pass = 1; pass <= (series ? 3 : 1); pass++)
                {
                var passOutput = series ? System.IO.Path.Combine(output, $"pass-{pass}") : output;
                Directory.CreateDirectory(passOutput);
                var before = Processes();
                var dimensions = new { window.ActualWidth, window.ActualHeight, Dpi = VisualTreeHelper.GetDpi(window).PixelsPerDip, window.IsActive, window.WindowState, RenderTier = RenderCapability.Tier >> 16 };
                var request = new CaptureRequest(target, series ? 30 : 25, true, "WPF translation animation; 800x500 DIPs; desktop refresh; no game; series=" + series + "; warmup=" + (series ? "10s" : "1.5s"), "{\"Workload\":\"WPF real D3D presentation\"}");
                using var stop = new CancellationTokenSource();
                if (cancel) stop.CancelAfter(TimeSpan.FromSeconds(6));
                var session = await provider.CaptureAsync(request, stop.Token);
                await File.WriteAllTextAsync(System.IO.Path.Combine(passOutput, "session.json"), JsonSerializer.Serialize(session));
                await File.WriteAllTextAsync(System.IO.Path.Combine(passOutput, "conditions.json"), JsonSerializer.Serialize(new { Pass = pass, Before = before, After = Processes(), Dimensions = dimensions, EndActive = window.IsActive, EndState = window.WindowState, EtwIntegrity = "Unknown" }));
                var repo = new SqliteBenchmarkRepository(System.IO.Path.Combine(output, "validation.db"));
                await repo.SaveAsync(session);
                var restored = (await new SqliteBenchmarkRepository(System.IO.Path.Combine(output, "validation.db")).ListAsync()).Single(s => s.Id == session.Id);
                Console.WriteLine(JsonSerializer.Serialize(new { session.Status, session.RawPath, session.Data?.Statistics, Restored = restored.Id == session.Id }));
                if (session.Status != "Completed" && series) break;
                if (series && pass < 3) await Task.Delay(5000);
                }
            }
            catch (Exception ex) { Console.WriteLine(ex); }
            finally { window.Close(); }
        };
        application.Run(window);
    }

    private static object[] Processes() => Process.GetProcesses().Select(p =>
    {
        using (p)
        {
            try { return (object)new { p.Id, p.ProcessName, CpuSeconds = p.TotalProcessorTime.TotalSeconds, WorkingSetBytes = p.WorkingSet64 }; }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return new { p.Id, Unavailable = true }; }
        }
    }).ToArray();
}
