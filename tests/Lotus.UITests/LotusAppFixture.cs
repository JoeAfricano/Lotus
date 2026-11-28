using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Xunit;

namespace Lotus.UITests;

/// <summary>Runs only when LOTUS_UI_TESTS=1, so a plain `dotnet test` never opens windows.</summary>
public sealed class UiFactAttribute : FactAttribute
{
    public UiFactAttribute() { if (!LotusAppFixture.Enabled) Skip = "Screen tests are off (run tools\\Run-UiTests.ps1 or set LOTUS_UI_TESTS=1)."; }
}

public sealed class UiTheoryAttribute : TheoryAttribute
{
    public UiTheoryAttribute() { if (!LotusAppFixture.Enabled) Skip = "Screen tests are off (run tools\\Run-UiTests.ps1 or set LOTUS_UI_TESTS=1)."; }
}

[CollectionDefinition(Name)]
public sealed class LotusAppCollection : ICollectionFixture<LotusAppFixture> { public const string Name = "Lotus app"; }

/// <summary>
/// Opens one development instance of an already built Lotus (--dev-instance: no update install, no play sessions,
/// no "start with Windows", no first-run welcome) for the whole run, and closes it at the end.
/// It never builds the app and never clicks anything that changes Windows: tests only navigate and look.
/// The player's preferences.json is copied before and put back after, because opening screens can remember
/// the last tab or window size.
/// </summary>
public sealed class LotusAppFixture : IDisposable
{
    public static bool Enabled => Environment.GetEnvironmentVariable("LOTUS_UI_TESTS") == "1";

    public UIA3Automation Automation { get; } = new();
    public Application App { get; private set; } = null!;
    public Window Window { get; private set; } = null!;
    public string ScreenshotFolder { get; }
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;

    private readonly string dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus");
    private readonly string? preferencesBackup;

    public LotusAppFixture()
    {
        var root = RepositoryRoot();
        ScreenshotFolder = Environment.GetEnvironmentVariable("LOTUS_UI_SHOTS") is { Length: > 0 } shots
            ? shots : Path.Combine(root, "artifacts", "ui-tests", DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));
        if (!Enabled) return;
        Directory.CreateDirectory(ScreenshotFolder);

        var exe = Environment.GetEnvironmentVariable("LOTUS_UI_APP") is { Length: > 0 } custom ? custom
            : Path.Combine(root, "src", "Lotus.App", "bin", "x64", "Debug", "net10.0-windows10.0.26100.0", "win-x64", "Lotus.App.exe");
        if (!File.Exists(exe)) throw new InvalidOperationException($"Lotus build not found: {exe}. Build it first (dotnet build Lotus.slnx -c Debug -p:Platform=x64) or set LOTUS_UI_APP.");
        // Only one development instance can run: a second launch just wakes the first and exits.
        var running = Process.GetProcessesByName("Lotus.App").Where(p => SamePath(p, exe)).ToList();
        if (running.Count > 0) throw new InvalidOperationException($"This Lotus build is already open (PID {running[0].Id}). Close it before the screen tests.");

        var preferences = Path.Combine(dataFolder, "preferences.json");
        if (File.Exists(preferences))
        {
            preferencesBackup = Path.Combine(ScreenshotFolder, "preferences.before.json");
            File.Copy(preferences, preferencesBackup, overwrite: true);
        }

        App = Application.Launch(new ProcessStartInfo(exe, "--dev-instance") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
        Window = Retry.WhileNull(() => App.GetMainWindow(Automation, TimeSpan.FromSeconds(2)), TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(500),
            throwOnTimeout: true, timeoutMessage: "Lotus window did not appear in 60 s.").Result!;
        // Wait for the side menu: it is filled after the window shows.
        Retry.WhileNull(() => Window.FindFirstDescendant(cf => cf.ByAutomationId("nav-home")), TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true, timeoutMessage: "Side menu (nav-home) did not appear in 30 s.");
        Window.Patterns.Window.PatternOrDefault?.SetWindowVisualState(WindowVisualState.Maximized);
        Thread.Sleep(1500); // opening animation
    }

    /// <summary>Clicks a side menu item (nav-&lt;id&gt;) and waits until it is the selected one.</summary>
    public AutomationElement OpenSection(string id)
    {
        var item = Retry.WhileNull(() => Window.FindFirstDescendant(cf => cf.ByAutomationId("nav-" + id)), TimeSpan.FromSeconds(10),
            throwOnTimeout: true, timeoutMessage: $"Menu item nav-{id} not found.").Result!;
        if (item.Patterns.SelectionItem.TryGetPattern(out var selection)) selection.Select();
        else item.Click();
        Retry.WhileFalse(() => item.Patterns.SelectionItem.PatternOrDefault?.IsSelected.ValueOrDefault ?? true, TimeSpan.FromSeconds(10),
            throwOnTimeout: true, timeoutMessage: $"Section {id} was not selected.");
        return item;
    }

    /// <summary>Saves a picture of the Lotus window (proof print) and returns its path.</summary>
    public string Screenshot(string name)
    {
        Window.SetForeground();
        // Park the mouse on the empty title bar so no hover tooltip covers the screen.
        var bounds = Window.BoundingRectangle;
        FlaUI.Core.Input.Mouse.Position = new System.Drawing.Point(bounds.Left + bounds.Width / 2, bounds.Top + 12);
        Thread.Sleep(800);
        var path = Path.Combine(ScreenshotFolder, name + ".png");
        Capture.Element(Window).ToFile(path);
        return path;
    }

    public bool IsRunning => App is { HasExited: false };

    /// <summary>Lines Lotus wrote to its own log during this run that point to a screen problem.</summary>
    public IReadOnlyList<string> ScreenErrorsInLog()
    {
        var logs = Path.Combine(dataFolder, "Logs");
        if (!Directory.Exists(logs)) return [];
        var found = new List<string>();
        foreach (var file in Directory.GetFiles(logs, "lotus-*.jsonl").Where(f => File.GetLastWriteTime(f) >= StartedAt.LocalDateTime))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                if (!(line.Contains("UI error handled") || line.Contains("XAML/composition error"))) continue;
                using var json = System.Text.Json.JsonDocument.Parse(line);
                if (json.RootElement.TryGetProperty("Timestamp", out var stamp) && stamp.GetDateTimeOffset() >= StartedAt) found.Add(line);
            }
        }
        return found;
    }

    public void Dispose()
    {
        if (!Enabled) { Automation.Dispose(); return; }
        try
        {
            if (App is { HasExited: false })
            {
                // Lotus may keep running in the tray after the window closes: close, then end the test instance.
                try { App.Close(); } catch (Exception) { }
                if (!App.HasExited) { Thread.Sleep(3000); if (!App.HasExited) App.Kill(); }
            }
        }
        finally
        {
            App?.Dispose();
            Automation.Dispose();
            if (preferencesBackup is not null)
            {
                Thread.Sleep(500);
                File.Copy(preferencesBackup, Path.Combine(dataFolder, "preferences.json"), overwrite: true);
            }
        }
    }

    private static bool SamePath(Process process, string exe)
    {
        try { return string.Equals(process.MainModule?.FileName, Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase); }
        catch (Exception) { return false; }
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Lotus.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Lotus.slnx not found above the test folder.");
    }
}
