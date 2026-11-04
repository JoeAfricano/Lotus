using Microsoft.UI.Windowing;

namespace Lotus.App;

// A game opened: Lotus gets out of the way (minimized). It closed: Lotus comes back as it was. Only a window that Lotus
// itself minimized is brought back; a window the player hid to the tray or minimized stays where it is.
public sealed partial class MainWindow
{
    private bool minimizedForGame;

    private void SetUpGameWindow()
    {
        // Clicked Jogar in Lotus: out of the way right away (the game check below also catches games opened elsewhere).
        games.Launched += (_, game) => DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(1200); // the launcher window comes up first
            MinimizeForGame(game.Name);
            // The game never appeared (closed launcher, store page, a process Lotus does not recognize): come back.
            TimeSpan? played;
            try { played = await sessionHub.WaitForSessionAsync(game, TimeSpan.FromMinutes(3), CancellationToken.None); } catch (OperationCanceledException) { return; }
            if (played is null) RestoreAfterGame(game.Name);
        });
        sessionHub.Started += (_, game) => DispatcherQueue.TryEnqueue(() => MinimizeForGame(game.Name));
        sessionHub.Stopped += (_, e) => DispatcherQueue.TryEnqueue(() => RestoreAfterGame(e.Game.Name));
    }

    private void RestoreAfterGame(string name)
    {
        if (!minimizedForGame) return;
        minimizedForGame = false;
        if (!preferences.Current.MinimizeForGames || !AppWindow.IsVisible) return;
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore(); // back to how it was (maximized stays maximized)
            Activate();
            Serilog.Log.Information("Lotus restored: {Game} closed or never started", name);
        }
    }

    private void MinimizeForGame(string name)
    {
        if (!preferences.Current.MinimizeForGames || !AppWindow.IsVisible) return;
        if (AppWindow.Presenter is OverlappedPresenter { State: not OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Minimize();
            minimizedForGame = true;
            Serilog.Log.Information("Lotus minimized: {Game} opened", name);
        }
    }
}
