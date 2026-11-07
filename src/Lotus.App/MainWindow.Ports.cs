using Lotus.Core.Diary;
using Lotus.Core.Games;

namespace Lotus.App;

// Games opened by Lotus Ports behave like library games: Lotus goes to the tray while playing and comes back after,
// and the session goes to the diary (Settings > "Registrar minhas sessões"), keyed by the game's Lotus key.
public sealed partial class MainWindow
{
    public void AttachPortsSessions(Lotus.Infrastructure.PortsLink.LotusIntegrationHandler handler)
    {
        handler.SessionStarted += s => DispatcherQueue.TryEnqueue(() => PutAwayForGame(s.GameTitle));
        handler.SessionEnded += (s, start, seconds) => DispatcherQueue.TryEnqueue(() =>
        {
            RestoreAfterGame(s.GameTitle);
            RecordPortsSession(s.LotusGameKey, s.GameTitle, start, seconds);
        });
    }

    private void RecordPortsSession(string key, string title, DateTimeOffset start, double seconds)
    {
        var minutes = (int)Math.Round(seconds / 60);
        if (!preferences.Current.AutoSessions || minutes < PlaySessionTracker.MinMinutes || string.IsNullOrWhiteSpace(key)) return;
        try
        {
            diary.RecordSession(DiaryGames.FromGame(new GameEntry(GameStores.LotusPorts, key, title)), start.LocalDateTime, minutes);
            Serilog.Log.Information("Play session (Lotus Ports): {Game} {Minutes} min", title, minutes);
            tray?.Notify(text["SessionSavedTitle"], text.Format("SessionSaved", DiaryText.Hours(minutes), title));
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Lotus Ports play session"); }
    }
}
