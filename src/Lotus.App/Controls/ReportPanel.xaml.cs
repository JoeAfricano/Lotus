using Lotus.Core;
using Lotus.Core.Reports;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App.Controls;

// "Relatório": gathers what Lotus already shows (PC, drivers and disks, Score, FPS measurements, games) for the sections the
// player picks, writes one HTML file to Documents\Lotus and opens it.
public sealed partial class ReportPanel : UserControl
{
    private readonly TaskState task = new();
    private string? lastFile;
    public ReportPanel() { InitializeComponent(); Progress.State = task; }

    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Lotus");

    private async void OnGenerate(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SectionViewModel vm) return;
        var t = vm.Text; var culture = System.Globalization.CultureInfo.GetCultureInfo(t.Language);
        task.Start();
        try
        {
            var sections = new List<ReportSection>();
            if (IncPc.IsChecked == true && vm.Experience is { } x)
                sections.Add(new(t["RepPc"], [new(t["UxProcessor"], x.CpuModel), new(t["UxGraphics"], x.GpuModel), new(t["UxMemoryLabel"], x.RamCapacity),
                    new("Windows", Environment.OSVersion.VersionString)]));
            task.Report(15);
            if (IncDrivers.IsChecked == true && vm.Tools is { } tools)
            {
                if (tools.DiskRows.Count == 0) await tools.RefreshAsync();
                var rows = new List<ReportRow> { new(t["ToolDriverTitle"], tools.DriverStatus) };
                rows.AddRange(tools.DriverRows.Select(r => new ReportRow(r.Title, r.Detail)));
                rows.AddRange(tools.DiskRows.Select(r => new ReportRow(r.Title, r.Detail)));
                sections.Add(new(t["RepDrivers"], rows, tools.DiskTip.Length > 0 ? tools.DiskTip : null));
            }
            task.Report(40);
            if (IncScore.IsChecked == true && vm.Score is { } score)
            {
                score.Refresh();
                sections.Add(new(t["RepScore"], [.. score.History.Take(10).Select(h => new ReportRow(h.Date, h.Summary))], score.Verdict.Length > 0 ? score.Verdict : t["ScoreHistoryEmpty"]));
            }
            task.Report(60);
            if (IncFps.IsChecked == true && vm.Benchmark is { } bench)
            {
                if (bench.Sessions.Count == 0) await bench.RefreshAsync();
                var rows = bench.Sessions.Where(s => s.Data?.Statistics is not null).Take(10)
                    .Select(s => { var st = s.Data!.Statistics!; return new ReportRow(s.Label, string.Format(culture, "{0:0} FPS · 1% low {1:0} FPS · P99 {2:0.0} ms", st.AverageFps, st.Low1Fps, st.P99Ms)); }).ToList();
                sections.Add(new(t["RepFps"], rows, rows.Count == 0 ? t["RepFpsEmpty"] : null));
            }
            task.Report(80);
            if (IncGames.IsChecked == true && vm.Games is { } games)
            {
                await games.EnsureLoadedAsync();
                var rows = games.Games.GroupBy(g => g.Store).Select(g => new ReportRow(g.Key, string.Join(", ", g.Select(x => x.Name).Take(12)))).ToList();
                sections.Add(new(t["RepGames"], rows, t.Format("GamesSummary", games.Games.Count, rows.Count)));
            }
            if (IncNotes.IsChecked == true && Notes.Text.Trim() is { Length: > 0 } notes) sections.Add(new(t["RepNotes"], [], notes));
            var title = ReportName.Text.Trim().Length > 0 ? ReportName.Text.Trim() : t["RepDefaultName"];
            var now = DateTime.Now;
            var html = ReportBuilder.Html(title, t.Format("RepSubtitle", now.ToString("f", culture), Environment.MachineName), sections, t["RepFooter"]);
            Directory.CreateDirectory(Folder);
            lastFile = Path.Combine(Folder, ReportBuilder.FileName(title, now));
            await File.WriteAllTextAsync(lastFile, html);
            task.Finish(t.Format("RepDone", Path.GetFileName(lastFile)));
            OpenFolder.Visibility = Visibility.Visible;
            await Windows.System.Launcher.LaunchUriAsync(new Uri(lastFile));
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Report failed"); task.Fail(); }
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        if (lastFile is not null) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", lastFile } })?.Dispose();
    }
}
