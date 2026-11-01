using Microsoft.UI.Windowing;

namespace Lotus.App;

// A game opened: Lotus gets out of the way (minimized). It closed: Lotus comes back as it was. Only a window that Lotus
// itself minimized is brought back; a window the player hid to the tray or minimized stays where it is.
public sealed partial class MainWindow
{
    private bool minimizedForGame;

    private void SetUpGameWindow()
    {
        sessionHub.Started += (_, game) => DispatcherQueue.TryEnqueue(() =>
        {
            if (!preferences.Current.MinimizeForGames || !AppWindow.IsVisible) return;
            if (AppWindow.Presenter is OverlappedPresenter { State: not OverlappedPresenterState.Minimized } presenter)
            {
                presenter.Minimize();
                minimizedForGame = true;
                Serilog.Log.Information("Lotus minimized: {Game} opened", game.Name);
            }
        });
        sessionHub.Stopped += (_, e) => DispatcherQueue.TryEnqueue(() =>
        {
            if (!minimizedForGame) return;
            minimizedForGame = false;
            if (!preferences.Current.MinimizeForGames || !AppWindow.IsVisible) return;
            if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            {
                presenter.Restore(); // back to how it was (maximized stays maximized)
                Activate();
                Serilog.Log.Information("Lotus restored: {Game} closed", e.Game.Name);
            }
        });
    }
}
