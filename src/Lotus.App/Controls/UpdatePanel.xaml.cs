using Lotus.Core.Updates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Lotus.App.Controls;

// Settings › Atualizações. While a new version downloads, the ring fills, a spark runs at its tip and the flower
// breathes; the animation only runs during the download (and never with "Reduzir animações").
public sealed partial class UpdatePanel : UserControl
{
    private UpdateViewModel? vm;
    private const double Center = 84, Radius = 75;
    private double shown; // eased progress drawn on screen

    public UpdatePanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as UpdateViewModel);
        Unloaded += (_, _) => { Attach(null); Motion.StopFrames(this); };
        Loaded += async (_, _) => { if (vm is { State: UpdateState.Idle }) await vm.CheckAsync(quiet: true); };
    }

    private void Attach(UpdateViewModel? next)
    {
        if (vm is not null) vm.PropertyChanged -= OnVm;
        vm = next;
        if (vm is null) return;
        vm.PropertyChanged += OnVm;
        shown = vm.IsReady ? 1 : vm.Progress;
        Sync();
    }

    private void OnVm(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UpdateViewModel.State) or nameof(UpdateViewModel.Latest)) Sync();
        if (e.PropertyName == nameof(UpdateViewModel.Progress) && vm is not null && !Motion.Active) { shown = vm.Progress; Draw(); }
    }

    private void Sync()
    {
        if (vm is null) return;
        var t = vm.Text;
        Headline.Text = vm.State switch
        {
            UpdateState.Checking => t["UpHeadChecking"],
            UpdateState.Downloading or UpdateState.Verifying => t["UpHeadDownloading"],
            UpdateState.Ready => t["UpHeadReady"],
            UpdateState.Installing => t["UpHeadInstalling"],
            _ when vm.HasUpdate => vm.LatestTitle,
            UpdateState.UpToDate => t["UpHeadUpToDate"],
            _ => t["UpHeadIdle"],
        };
        InstallText.Text = vm.IsReady ? t["UpInstallNow"] : t["UpDownloadInstall"];
        Notes.Text = Clean(vm.LatestNotes);
        if (vm.State == UpdateState.Checking) Spin();
        if (vm.IsDownloading) StartAnimation();
        else
        {
            Motion.StopFrames(this);
            shown = vm.IsReady ? 1 : 0;
            Draw();
            GlowScale.ScaleX = GlowScale.ScaleY = 1; FlowerMove.ScaleX = FlowerMove.ScaleY = 1; FlowerMove.TranslateY = 0;
            Spark.Opacity = 0;
            Percent.Opacity = 0;
            DoneIcon.Opacity = vm.IsReady ? 1 : 0;
            if (vm.IsReady) Motion.Pop(DoneIcon);
        }
    }

    // Release notes come as Markdown: shown as plain, readable text.
    private static string Clean(string markdown) => string.Join('\n', markdown.Replace("\r", "").Split('\n')
        .Select(l => l.TrimEnd())
        .Select(l => l.StartsWith('#') ? l.TrimStart('#', ' ').ToUpperInvariant() : l)
        .Select(l => l.Replace("**", "").Replace("`", ""))
        .Select(l => l.TrimStart().StartsWith("- ") ? new string(' ', (l.Length - l.TrimStart().Length)) + "• " + l.TrimStart()[2..] : l)).Trim()
        .Replace("\n\n\n", "\n\n");

    private void Spin() => Motion.Tween(CheckIcon, TimeSpan.FromMilliseconds(700), p => CheckIcon.RenderTransform = new RotateTransform { Angle = 360 * p, CenterX = 6.5, CenterY = 6.5 });

    private void StartAnimation()
    {
        Percent.Opacity = 1; DoneIcon.Opacity = 0;
        if (!Motion.Active) { shown = vm?.Progress ?? 0; Draw(); return; }
        Motion.OnFrames(this, ms =>
        {
            if (vm is null || !vm.IsDownloading) return false;
            shown += (vm.Progress - shown) * 0.12;                       // eases toward the real value
            var breath = (Math.Sin(ms / 520.0) + 1) / 2;                 // 0..1
            GlowScale.ScaleX = GlowScale.ScaleY = 0.92 + 0.16 * breath;
            Glow.Opacity = 0.25 + 0.3 * breath;
            FlowerMove.ScaleX = FlowerMove.ScaleY = 0.96 + 0.06 * breath;
            FlowerMove.TranslateY = -10 - 3 * breath;
            Spark.Opacity = shown > 0.01 && shown < 0.995 ? 0.6 + 0.4 * breath : 0;
            Draw();
            return true;
        });
    }

    // The arc from 12 o'clock, clockwise, with the spark at its tip.
    private void Draw()
    {
        var p = Math.Clamp(shown, 0, 0.9999);
        Percent.Text = $"{(int)Math.Round(shown * 100)}%";
        if (p <= 0.001) { Arc.Data = null; return; }
        var angle = p * 2 * Math.PI;
        var end = new Point(Center + Radius * Math.Sin(angle), Center - Radius * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = new Point(Center, Center - Radius), IsClosed = false };
        figure.Segments.Add(new ArcSegment { Point = end, Size = new Size(Radius, Radius), IsLargeArc = p > 0.5, SweepDirection = SweepDirection.Clockwise });
        var geometry = new PathGeometry(); geometry.Figures.Add(figure);
        Arc.Data = geometry;
        Canvas.SetLeft(Spark, end.X - 6); Canvas.SetTop(Spark, end.Y - 6);
    }

    private async void OnCheck(object sender, RoutedEventArgs e)
    {
        if (vm is null) return;
        var found = await vm.CheckAsync();
        UiSound.Play(found ? UiSound.Cue.Notify : UiSound.Cue.Done);
    }

    private async void OnInstall(object sender, RoutedEventArgs e)
    {
        if (vm is null) return;
        if (!vm.IsReady && !await vm.DownloadAsync()) { UiSound.Play(UiSound.Cue.Fail); return; }
        UiSound.Play(UiSound.Cue.Done);
        await Task.Delay(Motion.Ms(900)); // let the finished ring be seen
        if (MainWindow.Shell is { } shell) shell.InstallUpdate();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => vm?.Cancel();

    private async void OnOpenPage(object sender, RoutedEventArgs e)
    {
        if (vm?.Latest?.PageUrl is { Length: > 0 } url && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase))
            await Windows.System.Launcher.LaunchUriAsync(uri);
    }
}
