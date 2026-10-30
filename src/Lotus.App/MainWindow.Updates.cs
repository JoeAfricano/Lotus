using Lotus.Core.Updates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App;

// Updates from GitHub releases: a quiet check a little after start (and every 12 h while Lotus stays open), a notice
// when there is a new version, a dot on Settings, and — when the player turned it on — download and install by itself
// (never while a game is open; the notice lets the player postpone).
public sealed partial class MainWindow
{
    private UpdateViewModel updates = null!;
    private bool updatePostponed;

    private sealed class SetupInstaller : IUpdateInstaller
    {
        public void Install(string installerPath) =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installerPath, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /relaunch=1") { UseShellExecute = true })?.Dispose();
    }

    private void SetUpUpdates()
    {
        var current = UpdateVersions.Parse(AppVersionText()) ?? new Version(0, 0, 0);
        // Development instance only: pretend to be an older version to try the update screen without installing.
        if (Environment.GetCommandLineArgs().Contains("--dev-instance") && UpdateVersions.Parse(Environment.GetEnvironmentVariable("LOTUS_PRETEND_VERSION")) is { } pretend) current = pretend;
        updates = new UpdateViewModel(new Lotus.Infrastructure.Updates.GitHubUpdateSource(), new SetupInstaller(), current, text, ex => Serilog.Log.Warning(ex, "Updates"))
        {
            AutoEnabled = () => preferences.Current.AutoUpdate,
            SetAuto = on => preferences.Update(preferences.Current with { AutoUpdate = on }),
        };
        updates.Found += (_, release) => DispatcherQueue.TryEnqueue(() => OnUpdateFound(release));
        var timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromSeconds(30);
        timer.Tick += async (_, _) =>
        {
            timer.Interval = TimeSpan.FromHours(12);
            if (!initialized || updates.Busy || updates.IsReady) return;
            await updates.CheckAsync(quiet: true);
        };
        timer.Start();
    }

    private string? notifiedTag;
    private async void OnUpdateFound(UpdateRelease release)
    {
        ShowUpdateDot(true);
        if (notifiedTag == release.Tag) return;
        notifiedTag = release.Tag;
        var version = release.Tag.TrimStart('v');
        Serilog.Log.Information("Update available: {Version}", version);
        if (!preferences.Current.AutoUpdate)
        {
            Notice.Show(new(text["UpdatesCategory"], text.Format("UpNoticeTitle", version), text["UpNoticeBody"], "", null,
                [new(text["UpNoticeOpen"], () => { Notice.Hide(); ViewModel.NavigateCommand.Execute("updates"); }, Primary: true), new(text["GsClose"], () => Notice.Hide())], Seconds: 14));
            return;
        }
        // Automatic: download quietly, then install unless the player says "later" or is playing.
        if (!await updates.DownloadAsync()) return;
        updatePostponed = false;
        Notice.Show(new(text["UpdatesCategory"], text.Format("UpAutoTitle", version), text["UpAutoBody"], "", null,
            [new(text["UpInstallNow"], () => { Notice.Hide(); InstallUpdate(); }, Primary: true), new(text["UpLater"], () => { updatePostponed = true; Notice.Hide(); })], Seconds: 20));
        await Task.Delay(TimeSpan.FromSeconds(20));
        if (updatePostponed || !updates.IsReady) return;
        if (await Task.Run(games.RunningGame) is not null) { Serilog.Log.Information("Update waits: a game is open"); return; }
        InstallUpdate();
    }

    private void ShowUpdateDot(bool on)
    {
        foreach (var item in Navigation.FooterMenuItems.OfType<NavigationViewItem>().Where(i => (string)i.Tag == "settings"))
            item.InfoBadge = on ? new InfoBadge { Style = (Style)Application.Current.Resources["AttentionDotInfoBadgeStyle"] } : null;
    }

    // The installer closes Lotus, updates the files and opens Lotus again.
    public void InstallUpdate()
    {
        // A development instance never replaces the installed Lotus.
        if (Environment.GetCommandLineArgs().Contains("--dev-instance")) { Serilog.Log.Information("Update install skipped (development instance)"); return; }
        if (!updates.Install()) return;
        Serilog.Log.Information("Installing update {Version}", updates.Latest?.Tag);
        Application.Current.Exit();
    }
}
