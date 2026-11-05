using Microsoft.UI.Windowing;

namespace Lotus.App;

// A game opened: Lotus gets out of the way into the tray (no taskbar button). It closed: Lotus comes back as it was.
// Only a window that Lotus itself put away is brought back; a window the player minimized or hid stays where it is,
// and a window the player reopened during the game is left alone. Without a tray icon it only minimizes (never lost).
public sealed partial class MainWindow
{
    private enum PutAway { None, Tray, Minimized }
    private PutAway awayForGame;

    private void SetUpGameWindow()
    {
        // Clicked Jogar in Lotus: out of the way right away (the game check below also catches games opened elsewhere).
        games.Launched += (_, game) => DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(1200); // the launcher window comes up first
            PutAwayForGame(game.Name);
            // The game never appeared (closed launcher, store page, a process Lotus does not recognize): come back.
            TimeSpan? played;
            try { played = await sessionHub.WaitForSessionAsync(game, TimeSpan.FromMinutes(3), CancellationToken.None); } catch (OperationCanceledException) { return; }
            if (played is null) RestoreAfterGame(game.Name);
        });
        sessionHub.Started += (_, game) => DispatcherQueue.TryEnqueue(() => PutAwayForGame(game.Name));
        sessionHub.Stopped += (_, e) => DispatcherQueue.TryEnqueue(() => RestoreAfterGame(e.Game.Name));
    }

    private void RestoreAfterGame(string name)
    {
        var how = awayForGame;
        awayForGame = PutAway.None;
        if (how == PutAway.None || !preferences.Current.MinimizeForGames) return;
        if (how == PutAway.Tray && !AppWindow.IsVisible)
        {
            ShowFromTray(); // back from the tray as it was (maximized stays maximized); shows a waiting report too
            Serilog.Log.Information("Lotus back from the tray: {Game} closed or never started", name);
        }
        else if (how == PutAway.Minimized && AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
            Activate();
            Serilog.Log.Information("Lotus restored: {Game} closed or never started", name);
        }
        // Otherwise the player already brought Lotus back during the game: nothing to do.
    }

    private void PutAwayForGame(string name)
    {
        if (!preferences.Current.MinimizeForGames || !AppWindow.IsVisible || awayForGame != PutAway.None) return;
        if (AppWindow.Presenter is not OverlappedPresenter { State: not OverlappedPresenterState.Minimized } presenter) return; // the player minimized it: leave it
        if (tray is not null)
        {
            AppWindow.Hide(); // tray only: no taskbar button while playing
            awayForGame = PutAway.Tray;
            Serilog.Log.Information("Lotus moved to the tray: {Game} opened", name);
        }
        else
        {
            presenter.Minimize();
            awayForGame = PutAway.Minimized;
            Serilog.Log.Information("Lotus minimized: {Game} opened", name);
        }
    }
}
