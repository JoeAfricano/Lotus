using Lotus.Core.Tools;

namespace Lotus.App;

// "Saiu driver de vídeo novo": a quiet weekly check a few minutes after start (never with a game open), shown once per
// version as an in-app notice with a link to the maker's own page. Lotus never downloads or installs drivers.
public sealed partial class MainWindow
{
    private void StartDriverNotice()
    {
        if (!preferences.Current.DriverNotices) return;
        var timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMinutes(3); timer.IsRepeating = false;
        timer.Tick += async (_, _) =>
        {
            try
            {
                var p = preferences.Current;
                if (!initialized || !p.DriverNotices || games.RunningGame() is not null) return;
                if (p.DriverCheckedAt is { } at && DateTimeOffset.Now - at < DriverNotice.CheckEvery) return;
                var gpu = await Task.Run(() => new Lotus.Infrastructure.Tools.WindowsDriverInfo().Read());
                LatestDriver? latest = null;
                if (gpu is { Vendor: "NVIDIA" }) latest = await new Lotus.Infrastructure.Tools.TrustedDriverUpdates().NvidiaLatestAsync(gpu.Name, CancellationToken.None);
                var notice = DriverNotice.Decide(gpu, latest, p.DriverNoticeKey, DateTime.Today);
                preferences.Update(preferences.Current with { DriverCheckedAt = DateTimeOffset.Now, DriverNoticeKey = notice.Kind == DriverNoticeKind.None ? preferences.Current.DriverNoticeKey : notice.Key });
                Serilog.Log.Information("Driver check: {Vendor} {Installed} latest {Latest} → {Kind}", gpu?.Vendor, notice.Installed, notice.Latest, notice.Kind);
                if (notice.Kind == DriverNoticeKind.None || gpu is null) return;
                var body = notice.Kind == DriverNoticeKind.Newer ? text.Format("DriverNewBody", notice.Latest!, notice.Installed) : text.Format("DriverOldBody", notice.Installed, gpu.Vendor);
                Notice.Show(new(gpu.Name, text[notice.Kind == DriverNoticeKind.Newer ? "DriverNewTitle" : "DriverOldTitle"], body, "", null,
                [
                    new(text["DriverOpenPage"], () => { Notice.Hide(); _ = Windows.System.Launcher.LaunchUriAsync(new Uri(notice.Url)); }, Primary: true),
                    new(text["DriverLater"], () => Notice.Hide()),
                ], Seconds: 20));
                tray?.Notify(text[notice.Kind == DriverNoticeKind.Newer ? "DriverNewTitle" : "DriverOldTitle"], body);
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Driver check"); } // offline / NVIDIA changed: tried again next start
        };
        timer.Start();
    }
}
