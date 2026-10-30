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
        sessionHub.Observe(running, DateTimeOffset.Now); // shared with "Preparar e jogar" (no second polling loop)
        if (sessions.Update(running, DateTimeOffset.Now) is { } done) OnSessionFinished(done);
        if (running is null) ShowPendingResumePrompt();
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

    // One place reacts to the end of a session; each part handles its own failures so one never blocks another
    // (a failed note prompt never stops the diary, and so on).
    private void OnSessionFinished(FinishedSession s)
    {
        RecordSession(s);
        try { OfferResumeNote(s); } catch (Exception ex) { Serilog.Log.Warning(ex, "Resume prompt"); }
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
        if (sessions.Flush() is { } s) OnSessionFinished(s); // Lotus closing: recorded, never prompted
        if (priorityActive) { priorityActive = false; try { priority.Restore(); } catch (Exception ex) { Serilog.Log.Warning(ex, "Game priority restore"); } }
    }
}

// "Onde eu parei?" after a session: a discreet notice, only after a normal close of a real session (2+ minutes), once
// per session, never while Windows is in quiet mode (focus assist, presentation, full-screen app) or Lotus is hidden:
// then it waits until the player looks at Lotus again (within a few hours).
public sealed partial class MainWindow
{
    private Lotus.Core.Diary.ResumeNotes resume = null!;
    public Lotus.Core.Diary.ResumeNotes Resume => resume;
    private FinishedSession? pendingPrompt;

    private void OfferResumeNote(FinishedSession s)
    {
        if (!s.EndedNormally || !preferences.Current.ResumePrompts) return;
        var key = DiaryGames.KeyFor(s.Game);
        if (!resume.ShouldPrompt(key, s.Start)) return;
        var visible = AppWindow.IsVisible && AppWindow.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized };
        if (!visible || QuietMode.Active) { pendingPrompt = s; return; }
        ShowResumePrompt(s);
    }

    private void ShowPendingResumePrompt()
    {
        if (pendingPrompt is not { } s || !initialized) return;
        var visible = AppWindow.IsVisible && AppWindow.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized };
        if (!visible || QuietMode.Active) return;
        pendingPrompt = null;
        if (DateTimeOffset.Now - s.Start.AddMinutes(s.Minutes) > TimeSpan.FromHours(3)) return; // too late to be useful
        if (preferences.Current.ResumePrompts && resume.ShouldPrompt(DiaryGames.KeyFor(s.Game), s.Start)) ShowResumePrompt(s);
    }

    private void ShowResumePrompt(FinishedSession s)
    {
        var key = DiaryGames.KeyFor(s.Game);
        var art = s.Game.Store == Lotus.Core.Games.GameStores.Steam ? Lotus.Core.Catalog.CatalogText.Portrait(s.Game.Id) : s.Game.CoverPath;
        Controls.GameNotice.CancelLabel = text["DlgCancel"];
        Controls.GameNotice.Request? request = null;
        request = new(text["RnPromptEyebrow"], text.Format("RnPromptTitle", s.Game.Name), text.Format("RnPromptBody", DiaryText.Hours(s.Minutes)), "", art,
        [
            new(text["RnPromptWrite"], () => Notice.StartWriting(), Primary: true),
            new(text["RnPromptLater"], () => Notice.Hide()),
            new(text["RnPromptNever"], () => { preferencesViewModel.ResumePrompts = false; Notice.Hide(); }),
        ], Seconds: 25, NotePlaceholder: text["RnPlaceholder"], SaveNote: note => resume.Set(key, note, s.Start), SaveLabel: text["RnSave"]);
        Notice.Show(request);
        if (preferences.Current.UiSounds) UiSound.Play(UiSound.Cue.Notify);
    }
}

// Windows "quiet" states (Do not disturb / focus assist, presentation mode, a full-screen game or app): Lotus keeps
// its notices for later instead of interrupting.
internal static class QuietMode
{
    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);
    private const int AcceptsNotifications = 5;
    public static bool Active
    {
        get
        {
            try { return SHQueryUserNotificationState(out var s) == 0 && s != AcceptsNotifications; }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
        }
    }
}
