using Lotus.Core.Diary;
using Lotus.Core.Games;

namespace Lotus.App;

// While a library game is open: play sessions go to the diary (Settings > "Registrar minhas sessões") and, when the
// player turned it on, the game gets priority over known background apps (put back when it closes or Lotus exits).
public sealed partial class MainWindow
{
    private readonly PlaySessionTracker sessions = new();
    private readonly Lotus.Infrastructure.Games.WindowsGamePriority priority = new();
    private bool priorityActive;

    private async Task OnGameCheckAsync(GameEntry? running)
    {
        if (!initialized) return;
        if (sessions.Update(running, DateTimeOffset.Now) is { } done) RecordSession(done);
        var wantPriority = preferences.Current.GamePriority && running is not null;
        try
        {
            if (wantPriority && running is not null)
            {
                var process = await Task.Run(() => games.GameProcess(running));
                if (process is not null) { var changed = await Task.Run(() => priority.Apply(process)); if (changed > 0) Serilog.Log.Information("Game priority: {Count} processes adjusted for {Game}", changed, running.Name); priorityActive = true; }
            }
            else if (priorityActive) { priorityActive = false; var back = await Task.Run(priority.Restore); Serilog.Log.Information("Game priority: {Count} processes restored", back); }
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Game priority"); }
    }

    private void RecordSession(FinishedSession s)
    {
        if (!preferences.Current.AutoSessions) return;
        try
        {
            diary.RecordSession(DiaryGames.FromGame(s.Game), s.Start.LocalDateTime, s.Minutes);
            Serilog.Log.Information("Play session: {Game} {Minutes} min", s.Game.Name, s.Minutes);
            tray?.Notify(text["SessionSavedTitle"], text.Format("SessionSaved", DiaryText.Hours(s.Minutes), s.Game.Name));
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Play session"); }
    }

    // "Quero jogar" deals: checked a little after start (prices are cached for hours); new ones get a quiet tray note.
    private void StartDealsCheck()
    {
        var timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromSeconds(25); timer.IsRepeating = false;
        timer.Tick += async (_, _) =>
        {
            if (!initialized || diaryPage.Deals is not { } deals) return;
            var fresh = await deals.RefreshAsync();
            if (fresh.Count == 1) tray?.Notify(text["DlNotifyTitle"], text.Format("DlNotifyOne", fresh[0].Name, fresh[0].DiscountText, fresh[0].Final));
            else if (fresh.Count > 1) tray?.Notify(text["DlNotifyTitle"], text.Format("DlNotifyMany", fresh.Count, fresh[0].Name, fresh[0].DiscountText));
        };
        timer.Start();
    }

    // Lotus is closing: the running session still counts and priorities go back.
    private void EndGameWatch()
    {
        if (sessions.Flush() is { } s) RecordSession(s);
        if (priorityActive) { priorityActive = false; try { priority.Restore(); } catch (Exception ex) { Serilog.Log.Warning(ex, "Game priority restore"); } }
    }
}
