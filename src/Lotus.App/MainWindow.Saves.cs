using Lotus.Core.Games;
using Lotus.Core.Saves;

namespace Lotus.App;

// "Proteção de progresso": an automatic copy after each session of a protected game (off the UI thread, only when the
// game is really closed), a short notice only when it fails, and a check at start for restores that did not finish.
public sealed partial class MainWindow
{
    private SaveProtection saves = null!;
    public SaveProtection Saves => saves;

    // Unsure counts as running: a restore is never done over an open game.
    public bool IsGameRunning(GameEntry game) =>
        games.GameProcess(game) is not null || string.Equals(games.RunningGame()?.Key, game.Key, StringComparison.OrdinalIgnoreCase);

    private void SetUpSaves()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "SaveBackups");
        saves = new SaveProtection(new Lotus.Infrastructure.Games.JsonSaveSettingsStore(), new SaveVault(root),
            new SaveDiscovery(new Lotus.Infrastructure.Games.WindowsKnownFolders()), ex => Serilog.Log.Warning(ex, "Save protection"));
        if (games.Preparation is { } preparation) preparation.SaveWarning = game => saves.Warning(game, text);
        sessionHub.Stopped += (_, e) => _ = AutoBackupAsync(e.Game);
        var timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromSeconds(20); timer.IsRepeating = false;
        timer.Tick += async (_, _) =>
        {
            try
            {
                var keys = await Task.Run(() => saves.InterruptedGames().ToList());
                foreach (var key in keys)
                {
                    var name = saves.Get(key).GameName is { Length: > 0 } n ? n : key;
                    Serilog.Log.Warning("Save restore left unfinished for {Game}", name);
                    Notice.Show(new(text["SpTitle"], text["SpInterruptedTitle"], text.Format("SpInterruptedBody", name), "", null,
                        [new(text["GsClose"], () => Notice.Hide())], Seconds: 15));
                }
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Save restore check"); }
        };
        timer.Start();
    }

    private async Task AutoBackupAsync(GameEntry game)
    {
        try
        {
            var started = DateTimeOffset.Now;
            var result = await Task.Run(() => saves.AfterSessionAsync(game, () => IsGameRunning(game), TimeSpan.FromSeconds(20)));
            if (result is null) return;
            Serilog.Log.Information("Save backup {Game}: {Status} {Failure} in {Ms} ms", game.Name, result.Status, result.Failure, (int)(DateTimeOffset.Now - started).TotalMilliseconds - 20000);
            if (result.Status != SnapshotStatus.Failed) return;
            var why = new GameSavesViewModel(saves, game, text, IsGameRunning).FailureText(result.Failure);
            DispatcherQueue.TryEnqueue(() => Notice.Show(new(text["SpTitle"], text.Format("SpAutoFailTitle", game.Name), why, "", null,
                [new(text["GsClose"], () => Notice.Hide())], Seconds: 10)));
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Save backup after session"); }
    }
}
