using System.Numerics;
using Lotus.Core;
using Lotus.Core.Games;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Lotus.App.Controls;

// Home, part 1: "Continue playing" — the last game the player opened, big art, one click to play.
public sealed partial class ContinueCard : UserControl
{
    private readonly Grid root = new() { CornerRadius = new CornerRadius(22), MinHeight = 300 };
    public event EventHandler<GameEntry>? DetailsRequested;
    public ContinueCard() { Content = root; root.Background = (Brush)Application.Current.Resources["HeroBrush"]; RequestedTheme = ElementTheme.Dark; } // photo hero: white text and dark buttons in both themes

    public void Show(SectionViewModel vm)
    {
        root.Children.Clear();
        var t = vm.Text; var games = vm.Games;
        var game = games?.LastPlayed();
        // Art: Steam hero when the game is from Steam, otherwise its cover.
        var url = game is { Store: GameStores.Steam } ? Lotus.Core.Catalog.CatalogText.Hero(game.Id) : game?.CoverPath;
        // Lotus art (design package) under the game art: shown when there is no game, no art or the art fails.
        var lotus = new Image { Stretch = Stretch.UniformToFill, Source = new BitmapImage(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Concept", "lotus-ambient.png"))) { DecodePixelWidth = 1400 } };
        root.Children.Add(lotus);
        if (url is not null && Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri) && (uri.IsAbsoluteUri || File.Exists(url)))
        {
            var img = new Image { Stretch = Stretch.UniformToFill, Opacity = 0, Source = new BitmapImage(uri.IsAbsoluteUri ? uri : new Uri(System.IO.Path.GetFullPath(url))) { DecodePixelWidth = 640 } };
            ((BitmapImage)img.Source).ImageOpened += (_, _) => { HomeMotion.Fade(img, 1, 600); HomeMotion.Drift(img); lotus.Visibility = Visibility.Collapsed; };
            ((BitmapImage)img.Source).ImageFailed += (_, _) => img.Visibility = Visibility.Collapsed;
            root.Children.Add(img);
        }
        root.Children.Add(new Rectangle { Fill = HomeMotion.Gradient(new(0, 0), new(1, 0), (0, Color.FromArgb(0xF0, 18, 9, 15)), (0.6, Color.FromArgb(0x80, 18, 9, 15)), (1, Color.FromArgb(0x10, 18, 9, 15))) });
        root.Children.Add(new Rectangle { Fill = HomeMotion.Gradient(new(0, 0), new(0, 1), (0.45, Color.FromArgb(0, 18, 9, 15)), (1, Color.FromArgb(0xCC, 18, 9, 15))) });

        var text = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(32, 0, 32, 28), MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
        text.Children.Add(new TextBlock { Text = t[game is null ? "HcEmptyEyebrow" : "HcEyebrow"].ToUpperInvariant(), FontSize = 11.5, CharacterSpacing = 160, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromArgb(255, 0xF5, 0x8B, 0xB7)) }); // rose eyebrow on the dark photo
        var title = new TextBlock { Text = game?.Name ?? t["HcEmptyTitle"], FontSize = 42, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = new SolidColorBrush(Colors.White) };
        ToolTipService.SetToolTip(title, title.Text); // two lines at most; the full name on hover
        text.Children.Add(title);
        text.Children.Add(new TextBlock { Text = game is null ? t["HcEmptyBody"] : t["HcTagline"], FontSize = 16, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 255, 240, 246)) });
        if (game is not null) // when it was last played, as a small pill (hidden when unknown)
        {
            var when = When(t, game.LastPlayed);
            if (game.LastPlayed is not null)
                text.Children.Add(new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0),
                    Background = new SolidColorBrush(Color.FromArgb(0x59, 0x6B, 0x22, 0x46)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xF5, 0x8B, 0xB7)), BorderThickness = new Thickness(1),
                    Child = new TextBlock { Text = when, FontSize = 12.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromArgb(255, 0xF8, 0xD6, 0xE5)) } });
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 8, 0, 0) };
        if (game is not null && games is not null)
        {
            buttons.Children.Add(HomeMotion.Button(t["GiPlay"], "", primary: true, () => { UiSound.Play(UiSound.Cue.Open); games.Play(game); }));
            var details = HomeMotion.Button(t["GiDetails"], "", primary: false, () => DetailsRequested?.Invoke(this, game)); Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(details, "continue-details"); buttons.Children.Add(details);
        }
        else if (games is not null)
            buttons.Children.Add(HomeMotion.Button(t["HomeRescan"], "", primary: true, async () => { await games.RefreshAsync(); Show(vm); }));
        text.Children.Add(buttons);
        root.Children.Add(text);
        HomeMotion.Rise(text);
    }

    private static string When(ITextCatalog t, DateTimeOffset? at)
    {
        if (at is not { } d) return t["HcNeverDate"];
        var days = (DateTimeOffset.Now.Date - d.ToLocalTime().Date).Days;
        return days <= 0 ? t["HcToday"] : days == 1 ? t["HcYesterday"] : t.Format("HcDaysAgo", days);
    }
}

// Home, part 2: "PC readiness" — a live 0–100 ring, the one thing holding the PC back, and where to fix it.
public sealed partial class ReadinessCard : UserControl
{
    private readonly Border card;
    private readonly Microsoft.UI.Xaml.Shapes.Path arc = new() { StrokeThickness = 10, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly TextBlock score = new() { FontSize = 34, FontWeight = Microsoft.UI.Text.FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock eyebrow = new() { FontSize = 11, CharacterSpacing = 160, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock headline = new() { FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock advice = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock facts = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock summary = new() { FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
    private readonly Button action = new();
    private SectionViewModel? vm;
    private double shown = -1;
    private static bool scanned; // the Tools scan feeds this card once per session, not on every visit
    private string target = "optimize";
    public event EventHandler<string>? NavigateRequested;

    public ReadinessCard()
    {
        advice.Foreground = facts.Foreground = summary.Foreground = (Brush)Application.Current.Resources["MutedBrush"];
        arc.Stroke = (Brush)Application.Current.Resources["AccentGradientBrush"];
        var ring = new Grid { Width = 110, Height = 110 };
        ring.Children.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)), StrokeThickness = 10 });
        ring.Children.Add(arc); ring.Children.Add(score);
        var head = new Grid { ColumnSpacing = 16 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition());
        var words = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        eyebrow.Foreground = (Brush)Application.Current.Resources["GoldBrush"];
        words.Children.Add(eyebrow);
        words.Children.Add(headline); words.Children.Add(advice);
        Grid.SetColumn(words, 1); head.Children.Add(ring); head.Children.Add(words);
        action.Style = (Style)Application.Current.Resources["SoftButtonStyle"]; action.HorizontalAlignment = HorizontalAlignment.Stretch;
        action.Click += (_, _) => NavigateRequested?.Invoke(this, target);
        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(head); body.Children.Add(facts); body.Children.Add(action); body.Children.Add(summary);
        card = new Border { Style = (Style)Application.Current.Resources["HeroCardStyle"], Padding = new Thickness(22), Child = body };
        Content = card;
        Unloaded += (_, _) => { if (vm?.Monitoring is { } m) m.Updated -= OnUpdated; };
    }

    public async void Attach(SectionViewModel model)
    {
        vm = model;
        eyebrow.Text = model.Text["RdEyebrow"].ToUpperInvariant();
        if (model.Monitoring is { } m) { m.Updated -= OnUpdated; m.Updated += OnUpdated; }
        Render();
        // Heavy apps and disk space come from Ferramentas (read-only scan), once per session.
        if (!scanned && model.Tools is { Busy: false } tools) { scanned = true; try { await tools.RefreshAsync(); } catch (Exception ex) { Serilog.Log.Warning(ex, "Readiness tools scan"); } Render(); }
    }

    private void OnUpdated(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(Render);

    private void Render()
    {
        if (vm is null) return;
        var t = vm.Text;
        var heavy = vm.Tools?.Apps.OrderByDescending(a => a.App.Bytes).Select(a => ((string Name, long Bytes)?)(a.Name, a.App.Bytes)).FirstOrDefault();
        var r = PcReadiness.From(vm.Monitoring?.Snapshot, heavy, vm.Tools?.DiskTip.Length > 0);
        headline.Text = t[r.Score >= 85 ? "RdGreat" : r.Score >= 65 ? "RdGood" : "RdBusy"];
        (advice.Text, target, action.Content) = r.Issue switch
        {
            ReadinessIssue.Memory => (t["RdMemory"], "tools", t["RdFreeMemory"]),
            ReadinessIssue.HeavyApps => (t.Format("RdHeavy", r.HeavyApp ?? "?", vm.Tools?.Size(r.HeavyBytes) ?? ""), "tools", t["RdFreeMemory"]),
            ReadinessIssue.Cpu => (t["RdCpu"], "performance", t["RdSeeLive"]),
            ReadinessIssue.GpuHot => (t["RdGpuHot"], "performance", t["RdSeeLive"]),
            ReadinessIssue.Disk => (t["RdDisk"], "tools", t["RdCleanDisk"]),
            _ => (t["RdAllClear"], "optimize", t["GdEdit"]),
        };
        var parts = new List<string>();
        if (r.MemoryPercent is { } mem) parts.Add(t.Format("RdFactMem", Math.Round(mem)));
        if (r.CpuPercent is { } cpu) parts.Add(t.Format("RdFactCpu", Math.Round(cpu)));
        if (r.GpuTemp is { } temp) parts.Add(t.Format("RdFactGpu", Math.Round(temp)));
        facts.Text = parts.Count == 0 ? t["RdWaiting"] : string.Join("   ·   ", parts);
        summary.Text = vm.Guided?.ReadySummary ?? "";
        Animate(r.Score);
    }

    // The ring sweeps and the number counts to the new score.
    private void Animate(int value)
    {
        if (Math.Abs(shown - value) < 1) return;
        var from = shown < 0 ? 0 : shown; shown = value;
        var start = DateTime.UtcNow; var ms = Motion.Enabled ? 900.0 : 0;
        var timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(16);
        void Step()
        {
            var p = ms == 0 ? 1 : Math.Min(1, (DateTime.UtcNow - start).TotalMilliseconds / ms);
            var v = from + (value - from) * (1 - Math.Pow(1 - p, 3));
            score.Text = Math.Round(v).ToString();
            arc.Data = ArcGeometry(v / 100);
        }
        timer.Tick += (s, _) => { Step(); if ((DateTime.UtcNow - start).TotalMilliseconds >= ms) s.Stop(); };
        Step(); timer.Start();
    }
    private static Geometry ArcGeometry(double fraction)
    {
        fraction = Math.Clamp(fraction, 0.001, 0.999);
        const double r = 50, c = 55; var angle = fraction * 2 * Math.PI - Math.PI / 2;
        var end = new Windows.Foundation.Point(c + r * Math.Cos(angle), c + r * Math.Sin(angle));
        var figure = new PathFigure { StartPoint = new Windows.Foundation.Point(c, c - r), IsClosed = false };
        figure.Segments.Add(new ArcSegment { Point = end, Size = new Windows.Foundation.Size(r, r), IsLargeArc = fraction > 0.5, SweepDirection = SweepDirection.Clockwise });
        var g = new PathGeometry(); g.Figures.Add(figure); return g;
    }
}

// Shared bits for the Home cards (compositor animations, all off with Reduce motion).
internal static class HomeMotion
{
    public static Button Button(string label, string glyph, bool primary, Action onClick)
    {
        var b = new Button { Style = (Style)Application.Current.Resources[primary ? "PrimaryButtonStyle" : "SoftButtonStyle"], MinHeight = 44 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14 }); row.Children.Add(new TextBlock { Text = label });
        b.Content = row; b.Click += (_, _) => onClick();
        return b;
    }
    public static void Fade(UIElement e, double to, int ms)
    {
        if (!Motion.Enabled) { e.Opacity = to; return; }
        var a = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = to, Duration = TimeSpan.FromMilliseconds(ms) };
        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard(); Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(a, e); Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(a, "Opacity"); sb.Children.Add(a); sb.Begin();
    }
    // Slow push-in on the art, origin bound to the real size.
    public static void Drift(UIElement e)
    {
        if (!Motion.Enabled) return;
        var v = ElementCompositionPreview.GetElementVisual(e); var c = v.Compositor;
        v.StartAnimation("CenterPoint", c.CreateExpressionAnimation("Vector3(this.Target.Size.X * 0.65, this.Target.Size.Y * 0.5, 0)"));
        var a = c.CreateVector3KeyFrameAnimation(); a.InsertKeyFrame(0, new(1.0f, 1.0f, 1)); a.InsertKeyFrame(1, new(1.08f, 1.08f, 1));
        a.Duration = TimeSpan.FromSeconds(20); a.Direction = Microsoft.UI.Composition.AnimationDirection.Alternate; a.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;
        v.StartAnimation("Scale", a);
    }
    public static void Rise(UIElement e)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(e, true);
        if (!Motion.Enabled) return;
        var v = ElementCompositionPreview.GetElementVisual(e); var c = v.Compositor;
        var ease = c.CreateCubicBezierEasingFunction(new(0.05f, 0.7f), new(0.1f, 1f));
        var a = c.CreateVector3KeyFrameAnimation(); a.InsertKeyFrame(0, new(0, 18, 0)); a.InsertKeyFrame(1, Vector3.Zero, ease); a.Duration = TimeSpan.FromMilliseconds(600);
        var f = c.CreateScalarKeyFrameAnimation(); f.InsertKeyFrame(0, 0); f.InsertKeyFrame(1, 1, ease); f.Duration = TimeSpan.FromMilliseconds(600);
        v.StartAnimation("Translation", a); v.StartAnimation("Opacity", f);
    }
    public static LinearGradientBrush Gradient(Windows.Foundation.Point from, Windows.Foundation.Point to, params (double Offset, Color Color)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = from, EndPoint = to };
        foreach (var (o, col) in stops) b.GradientStops.Add(new GradientStop { Offset = o, Color = col });
        return b;
    }
}
