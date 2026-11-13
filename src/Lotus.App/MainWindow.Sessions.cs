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
    // Perfil Jogo: Windows tweaks only while a game is open, undone when it closes (or at the next start after a crash).
    private Lotus.Core.Games.GameBoost? boost;
    private bool boostRecovered;
    private Lotus.Core.Games.GameBoost Boost => boost ??= new(
    [
        new Lotus.Core.Games.PowerBoostStep(new Lotus.Infrastructure.Optimization.WindowsPowerSchemes()),
        new Lotus.Infrastructure.Games.NotificationsBoostStep(),
        new Lotus.Infrastructure.Games.RecordingBoostStep(),
        new Lotus.Infrastructure.Games.CloseAppsBoostStep(() => preferences.Current.BoostAppList),
    ], new Lotus.Infrastructure.Games.FileBoostJournal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "game-boost.json")));

    private async Task OnGameCheckAsync(GameEntry? running)
    {
        if (!initialized) return;
        sessionHub.Observe(running, DateTimeOffset.Now); // shared with "Preparar e jogar" (no second polling loop)
        if (sessions.Update(running, DateTimeOffset.Now) is { } done) OnSessionFinished(done);
        if (running is null) ShowPendingResumePrompt();
        await UpdateBoostAsync(running);
        var wantPriority = preferences.Current.GamePriority && running is not null && !DevBesideInstalled();
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

    private async Task UpdateBoostAsync(GameEntry? running)
    {
        try
        {
            if (!boostRecovered)
            {
                boostRecovered = true;
                var undone = await Task.Run(Boost.Recover);
                if (undone.Count > 0) Serilog.Log.Information("Perfil Jogo: {Steps} undone after an unfinished session", string.Join(", ", undone));
            }
            var want = preferences.Current.GameBoost && running is not null && !DevBesideInstalled();
            if (want && !Boost.IsActive)
            {
                var process = await Task.Run(() => games.GameProcess(running!));
                if (process is null) return;
                var options = preferences.Current.BoostOptions;
                var result = await Task.Run(() => Boost.Start(process, options));
                Serilog.Log.Information("Perfil Jogo on for {Game}: {Applied} (failed: {Failed})", running!.Name, string.Join(", ", result.Applied), string.Join(", ", result.Failed));
            }
            else if (!want && Boost.IsActive)
            {
                var back = await Task.Run(Boost.Stop);
                Serilog.Log.Information("Perfil Jogo off: {Steps} restored", string.Join(", ", back));
            }
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Perfil Jogo"); }
    }

    // A development build (--dev-instance) next to a running installed Lotus leaves the game to the installed one:
    // both changed the same processes' priority and wrote the same diary.
    private static bool DevBesideInstalled()
    {
        if (!Environment.GetCommandLineArgs().Contains("--dev-instance")) return false;
        try { if (!Mutex.TryOpenExisting(@"Local\Lotus.App.Main", out var m)) return false; m.Dispose(); return true; }
        catch (Exception) { return false; }
    }

    // One place reacts to the end of a session; each part handles its own failures so one never blocks another
    // (a failed note prompt never stops the diary, and so on).
    private void OnSessionFinished(FinishedSession s)
    {
        RecordSession(s);
        if (Environment.GetCommandLineArgs().Contains("--dev-instance") && Environment.GetEnvironmentVariable("LOTUS_DEV_SESSIONS") != "1") return; // nothing recorded, nothing to ask
        try { OfferResumeNote(s); } catch (Exception ex) { Serilog.Log.Warning(ex, "Resume prompt"); }
    }

    private void RecordSession(FinishedSession s)
    {
        if (!preferences.Current.AutoSessions) return;
        // A development build runs next to the installed Lotus, which already records the same session.
        if (Environment.GetCommandLineArgs().Contains("--dev-instance") && Environment.GetEnvironmentVariable("LOTUS_DEV_SESSIONS") != "1") { Serilog.Log.Information("Play session not recorded (development instance)"); return; }
        try
        {
            diary.RecordSession(DiaryGames.FromGame(s.Game), s.Start.LocalDateTime, s.Minutes);
            Serilog.Log.Information("Play session: {Game} {Minutes} min", s.Game.Name, s.Minutes);
            tray?.Notify(text["SessionSavedTitle"], text.Format("SessionSaved", DiaryText.Hours(s.Minutes), s.Game.Name));
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Play session"); }
    }

    // One-shot jobs a little after start. The timers are kept here: a timer nothing refers to can be collected before
    // it fires (the cover repair at 8 s never ran on a real PC).
    private readonly List<Microsoft.UI.Dispatching.DispatcherQueueTimer> startupTimers = [];
    private Microsoft.UI.Dispatching.DispatcherQueueTimer StartupTimer(TimeSpan delay)
    {
        var timer = DispatcherQueue.CreateTimer(); timer.Interval = delay; timer.IsRepeating = false;
        startupTimers.Add(timer);
        return timer;
    }

    // Diary covers that never showed (other launchers, games added by hand, newer Steam art): repaired once, quietly,
    // a little after start. Only the pictures change; Home, the diary and the player panel repaint through Changed.
    private Lotus.Core.Diary.DiaryArt? diaryArt;
    public Lotus.Core.Diary.DiaryArt? DiaryArt => diaryArt;
    private void StartArtRepair()
    {
        diaryArt = new Lotus.Core.Diary.DiaryArt(diary, catalog, ex => Serilog.Log.Warning(ex, "Diary art"))
        {
            LibraryCover = key => portsCovers.GetValueOrDefault(key)
                ?? games.InstalledGames().FirstOrDefault(g => string.Equals(Lotus.Core.Diary.DiaryGames.KeyFor(g), key, StringComparison.OrdinalIgnoreCase))?.CoverPath,
        };
        var timer = StartupTimer(TimeSpan.FromSeconds(8));
        timer.Tick += async (_, _) =>
        {
            if (!initialized || diaryArt is not { } art) return;
            await LoadPortsCoversAsync(); // games played through Lotus Ports reach the diary with a title only
            try { var count = await art.RepairAllAsync(); if (count > 0) Serilog.Log.Information("Diary art: {Count} covers repaired", count); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Diary art"); }
        };
        timer.Start();
    }

    // "Tempo para zerar" (HowLongToBeat) for the games being played or wanted: a few each start, after things settle.
    private readonly Lotus.Infrastructure.Catalog.HowLongToBeatClient gameLengths = new();
    private Lotus.Core.Diary.DiaryLengths? diaryLengths;
    private void StartGameLengths()
    {
        diaryLengths = new Lotus.Core.Diary.DiaryLengths(diary, gameLengths, ex => Serilog.Log.Warning(ex, "Game length"));
        var timer = StartupTimer(TimeSpan.FromSeconds(45));
        timer.Tick += async (_, _) =>
        {
            if (!initialized || diaryLengths is not { } lengths) return;
            if (await Task.Run(games.RunningGame) is not null) { timer.Interval = TimeSpan.FromMinutes(10); timer.Start(); return; } // never while a game runs: later
            try { var found = await lengths.RefreshAsync(); if (found > 0) Serilog.Log.Information("Game length: {Count} games", found); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Game length"); }
        };
        timer.Start();
    }

    // Covers of the Lotus Ports games by diary key ("Lotus Ports:<key>"): read from the catalog Lotus Ports ships (works
    // with it closed), then asked from Lotus Ports itself when it is open (its answer wins).
    private readonly Dictionary<string, string> portsCovers = new(StringComparer.OrdinalIgnoreCase);
    private async Task LoadPortsCoversAsync()
    {
        try
        {
            foreach (var (key, url) in await Task.Run(() => Lotus.Infrastructure.PortsLink.PortsCatalogCovers.Read()))
                if (CoverAddress(url) is { } cover) portsCovers[new GameEntry(GameStores.LotusPorts, key, key).Key] = cover;
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Diary art (Lotus Ports catalog)"); }
        if (!Lotus.Infrastructure.PortsLink.PortsClient.IsInstalled) { if (portsCovers.Count > 0) Serilog.Log.Information("Diary art: {Count} Lotus Ports covers (catalog)", portsCovers.Count); return; }
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result = await new Lotus.Infrastructure.PortsLink.PortsClient(Serilog.Log.Logger).ListGamesAsync(timeout.Token);
            foreach (var game in result?.Games ?? [])
                if (CoverAddress(game.CoverUrl) is { } cover)
                    portsCovers[new GameEntry(GameStores.LotusPorts, game.LotusGameKey, game.Title).Key] = cover;
            Serilog.Log.Information("Diary art: {Count} Lotus Ports covers", portsCovers.Count);
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Diary art (Lotus Ports)"); }
    }
    // https or a local picture (file URI or plain path) that exists; anything else is not shown.
    public static string? CoverAddress(string? cover)
    {
        if (cover is not { Length: > 0 }) return null;
        if (Uri.TryCreate(cover, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme == "https") return cover;
            if (uri.IsFile) return File.Exists(uri.LocalPath) ? uri.LocalPath : null;
            return null;
        }
        return Path.IsPathFullyQualified(cover) && File.Exists(cover) ? cover : null;
    }

    // "Quero jogar" deals: checked a little after start (prices are cached for hours); new ones get a quiet tray note.
    private void StartDealsCheck()
    {
        var timer = StartupTimer(TimeSpan.FromSeconds(25));
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
        if (boost?.IsActive == true) { try { boost.Stop(); } catch (Exception ex) { Serilog.Log.Warning(ex, "Perfil Jogo restore"); } }
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
