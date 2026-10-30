using Lotus.Core.Games;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App;

// "Preparar e Jogar" in the window: the question (only for real problems), discreet notices, and putting the Game
// profile back after the session — or at the next start when Lotus or the game closed unexpectedly.
public sealed partial class MainWindow
{
    private readonly SessionHub sessionHub = new();
    private GamePrefsBook playPrefs = null!;
    public GamePrefsBook PlayPrefs => playPrefs;

    private void SetUpPreparation()
    {
        playPrefs = new GamePrefsBook(new Lotus.Infrastructure.Games.JsonGamePrefsStore(), ex => Serilog.Log.Warning(ex, "Game preferences"));
        GameControllers.Start();
        var preparation = new PlayPreparation(new Lotus.Infrastructure.Games.WindowsPrepareProbes(games.Memory, GameControllers.Count), text, ex => Serilog.Log.Warning(ex, "Play preparation"))
        {
            Profile = games.GameProfile,
        };
        games.Preparation = preparation;
        games.PlayPrefs = playPrefs;
        games.PrepareByDefault = () => preferences.Current.PreparePlay;
        games.AskBeforePlay = AskBeforePlayAsync;
        games.Trace = m => Serilog.Log.Information(m);
        games.Prepared += (_, e) => DispatcherQueue.TryEnqueue(() => ShowPrepared(e.Game, e.Result, e.Quiet));
        sessionHub.Stopped += (_, e) => DispatcherQueue.TryEnqueue(() => _ = RestoreAfterAsync(e.Game.Key, e.Game.Name));
        // Restores left from a crash of Lotus (or of the game) are finished a little after start, if the game is closed.
        var timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromSeconds(15); timer.IsRepeating = false;
        timer.Tick += async (_, _) =>
        {
            if (!initialized) return;
            var running = await Task.Run(games.RunningGame);
            foreach (var key in playPrefs.PendingRestore.Keys.ToList())
                if (running is null || !string.Equals(running.Key, key, StringComparison.OrdinalIgnoreCase))
                    await RestoreAfterAsync(key, games.InstalledGames().FirstOrDefault(g => string.Equals(g.Key, key, StringComparison.OrdinalIgnoreCase))?.Name ?? key);
        };
        timer.Start();
    }

    private async Task RestoreAfterAsync(string key, string name)
    {
        if (!playPrefs.PendingRestore.ContainsKey(key)) return;
        var ok = await games.RestoreAfterSessionAsync(key);
        if (ok is null) return;
        Serilog.Log.Information("Play preparation restore {Game}: {Result}", name, ok.Value ? "restored" : "changed meanwhile");
        Notice.Show(new(text["PrepRestoreEyebrow"], text.Format(ok.Value ? "PrepRestored" : "PrepRestoreConflict", name), text[ok.Value ? "PrepRestoredBody" : "PrepRestoreConflictBody"],
            ok.Value ? "" : "", null, [new(text["GsClose"], () => Notice.Hide())], Seconds: 10));
    }

    // Only warnings and blocking problems get here. Short, plain words; the game can always be opened anyway.
    private async Task<PrepDecision> AskBeforePlayAsync(GameEntry game, PrepResult result)
    {
        if (ShellRoot.XamlRoot is null) return PrepDecision.Play;
        var body = new StackPanel { Spacing = 12, MaxWidth = 440 };
        foreach (var f in result.Findings.Where(f => f.Level >= PrepLevel.Warning))
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new FontIcon { Glyph = f.Level == PrepLevel.Blocking ? "" : "", FontSize = 16, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0),
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[f.Level == PrepLevel.Blocking ? "ErrorBrush" : "WarningBrush"] });
            var text_ = new StackPanel { Spacing = 2 };
            text_.Children.Add(new TextBlock { Text = f.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            text_.Children.Add(new TextBlock { Text = f.Detail, TextWrapping = TextWrapping.Wrap, Opacity = 0.8, FontSize = 13 });
            Grid.SetColumn(text_, 1); row.Children.Add(text_);
            body.Children.Add(row);
        }
        var blocked = result.Blocked;
        var dialog = new ContentDialog
        {
            XamlRoot = ShellRoot.XamlRoot, RequestedTheme = ThemePalette.Element, Title = text.Format("PrepAskTitle", game.Name), Content = body,
            PrimaryButtonText = text[blocked ? "PrepRescan" : "PrepPlayAnyway"], SecondaryButtonText = blocked ? text["PrepOpenAnyway"] : "",
            CloseButtonText = text["DlgCancel"], DefaultButton = ContentDialogButton.Primary,
        };
        var answer = await dialog.ShowAsync();
        return answer switch
        {
            ContentDialogResult.Primary => blocked ? PrepDecision.Rescan : PrepDecision.Play,
            ContentDialogResult.Secondary => PrepDecision.Play,
            _ => PrepDecision.Cancel,
        };
    }

    private void ShowPrepared(GameEntry game, PrepResult result, bool quiet)
    {
        var notes = result.Findings.Where(f => f.Level == PrepLevel.Info || f.Id == "profile").ToList();
        if (notes.Count == 0 && quiet) return; // nothing worth saying: the game simply opened
        var title = notes.Count == 0 ? text.Format("PrepAllGood", game.Name) : notes[0].Title;
        var body = notes.Count == 0 ? text["PrepAllGoodBody"] : string.Join("\n", notes.Select(n => n.Detail));
        Notice.Show(new(text["PrepEyebrow"], title, body, "", null, [new(text["GsClose"], () => Notice.Hide())], Seconds: 8));
    }
}

// Game controllers through Windows.Gaming.Input. The list fills asynchronously after the first use, so it is started
// once at launch and kept current by its own add/remove events (no polling).
internal static class GameControllers
{
    private static bool started;
    public static void Start()
    {
        if (started) return;
        started = true;
        try
        {
            Windows.Gaming.Input.RawGameController.RawGameControllerAdded += (_, _) => { };
            Windows.Gaming.Input.RawGameController.RawGameControllerRemoved += (_, _) => { };
            _ = Windows.Gaming.Input.RawGameController.RawGameControllers.Count;
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Game controllers"); started = false; }
    }
    public static int? Count()
    {
        if (!started) return null;
        try { return Windows.Gaming.Input.RawGameController.RawGameControllers.Count; }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Game controllers"); return null; }
    }
}

public sealed partial class MainWindow
{
    public bool PreparePlayDefault => preferences.Current.PreparePlay;
    // What the Game profile would change now (for the per-game authorization question).
    public Func<Task<IReadOnlyList<ProfileStep>>>? GameProfilePreview => games.GameProfile is { } p ? p.PreviewAsync : null;
}
