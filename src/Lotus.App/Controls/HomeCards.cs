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
    private readonly Grid root = new() { CornerRadius = new CornerRadius(22), MinHeight = 200 };
    public event EventHandler<GameEntry>? DetailsRequested;
    // Bleed: Home's edge-to-edge hero (no corners, art fades into the page, greeting on top).
    // Compact: the "Último jogado" band at the top of Meus Jogos (smaller title, no note).
    public bool Bleed { get; set; }
    public bool Compact { get; set; }
    public string? Greeting { get; set; }
    // Tight: narrow Home, where the readiness card sits right under the art; less empty band under the buttons.
    public bool Tight { get => tight; set { tight = value; if (heroText is not null) heroText.Margin = TextMargin(); } }
    private bool tight;
    private StackPanel? heroText;
    private Thickness TextMargin() => Bleed ? new Thickness(40, 0, 40, tight ? 28 : 64) : new Thickness(32, 0, 32, 28);
    public ContinueCard() { Content = root; root.Background = (Brush)Application.Current.Resources["HeroBrush"]; RequestedTheme = ElementTheme.Dark; } // photo hero: white text and dark buttons in both themes

    public void Show(SectionViewModel vm)
    {
        // A note written elsewhere (diary, end-of-session notice, player panel) shows up here right away.
        if (!watching && MainWindow.Shell?.Resume is { } notes) { watching = true; notes.Changed += (_, _) => DispatcherQueue.TryEnqueue(() => { if (shown is { } v && IsLoaded) Show(v); }); }
        shown = vm;
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
        root.CornerRadius = new CornerRadius(Bleed ? 0 : 16);
        root.Children.Add(new Rectangle { Fill = HomeMotion.Gradient(new(0, 0), new(1, 0), (0, Color.FromArgb(0xF2, 18, 9, 15)), (0.38, Color.FromArgb(0xB3, 18, 9, 15)), (0.62, Color.FromArgb(0x1A, 18, 9, 15)), (1, Color.FromArgb(Bleed ? (byte)0x8C : (byte)0x33, 18, 9, 15))) }); // dark behind the text, the art bright in the middle
        // Bleed: the art melts into the page background instead of ending on a hard edge.
        var page = PageColor(); // Home paints this same solid color behind the page, so the fade has no edge
        root.Children.Add(new Rectangle { Fill = Bleed
            ? HomeMotion.Gradient(new(0, 0), new(0, 1), (0.45, Color.FromArgb(0, page.R, page.G, page.B)), (0.8, Color.FromArgb((byte)(page.A * 0.85), page.R, page.G, page.B)), (1, page))
            : HomeMotion.Gradient(new(0, 0), new(0, 1), (0.5, Color.FromArgb(0, 18, 9, 15)), (1, Color.FromArgb(0xB3, 18, 9, 15))) });
        if (Bleed && !string.IsNullOrWhiteSpace(Greeting))
            root.Children.Add(new TextBlock { Text = Greeting, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(40, 28, 40, 0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Foreground = new SolidColorBrush(Color.FromArgb(0xF2, 255, 244, 248)) });

        var text = heroText = new StackPanel { Spacing = Compact ? 8 : 12, VerticalAlignment = VerticalAlignment.Bottom, Margin = TextMargin(), MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
        var eyebrowRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        eyebrowRow.Children.Add(new Rectangle { Width = 24, Height = 2, VerticalAlignment = VerticalAlignment.Center, Fill = new SolidColorBrush(Color.FromArgb(255, 0xF5, 0x8B, 0xB7)) });
        var eyebrowText = t[game is null ? "HcEmptyEyebrow" : Compact ? "GmSpotlight" : "HcEyebrow"].ToUpperInvariant();
        if (Compact && game?.LastPlayed is not null) eyebrowText += " · " + When(t, game.LastPlayed).ToUpperInvariant();
        eyebrowRow.Children.Add(new TextBlock { Text = eyebrowText, FontSize = 12, CharacterSpacing = 160, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromArgb(255, 0xF5, 0x8B, 0xB7)) }); // rose eyebrow on the dark photo
        text.Children.Add(eyebrowRow);
        var big = Compact ? 44 : ActualWidth is > 0 and < 760 ? 48 : 72;
        var title = new TextBlock { Text = game?.Name ?? t["HcEmptyTitle"], FontSize = big, LineHeight = big + 4, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, CharacterSpacing = -30, FontWeight = Microsoft.UI.Text.FontWeights.Bold, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = new SolidColorBrush(Colors.White) };
        title.IsTextTrimmedChanged += (_, _) => ToolTipService.SetToolTip(title, title.IsTextTrimmed ? title.Text : null); // full name on hover only when cut
        text.Children.Add(title);
        // "Onde você parou": the player's own note for this game replaces the tagline (no empty space without a note).
        var note = game is null ? null : MainWindow.Shell?.Resume.Get(Lotus.Core.Diary.DiaryGames.KeyFor(game));
        if (Compact) { }
        else if (note is not null) text.Children.Add(ResumeBlock(t, note));
        else text.Children.Add(new TextBlock { Text = game is null ? t["HcEmptyBody"] : t["HcTagline"], FontSize = 18, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 255, 240, 246)) });
        if (game is not null && !Compact) // when it was last played, as a small pill (hidden when unknown)
        {
            var when = When(t, game.LastPlayed);
            if (game.LastPlayed is not null)
                text.Children.Add(new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0),
                    Background = new SolidColorBrush(Color.FromArgb(0x59, 0x6B, 0x22, 0x46)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xF5, 0x8B, 0xB7)), BorderThickness = new Thickness(1),
                    Child = new TextBlock { Text = when, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromArgb(255, 0xF8, 0xD6, 0xE5)) } });
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, Compact ? 4 : 10, 0, 0) };
        if (game is not null && games is not null)
        {
            buttons.Children.Add(HomeMotion.Big(HomeMotion.Button(t[note is null || Compact ? "GiPlay" : "RnContinue"], "", primary: true, () => { UiSound.Play(UiSound.Cue.Open); games.Play(game); }), primary: true));
            var details = HomeMotion.Button(t["GiDetails"], "", primary: false, () => DetailsRequested?.Invoke(this, game)); HomeMotion.Big(details, primary: false); HomeMotion.OnPhoto(details); Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(details, "continue-details"); buttons.Children.Add(details);
            if (note is not null && !Compact)
            {
                Button? view = null;
                view = HomeMotion.Button(t["RnView"], "\uE8A4", primary: false, () => ShowNote(view!, t, game, note));
                HomeMotion.Big(view, primary: false); HomeMotion.OnPhoto(view);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(view, "continue-resume");
                buttons.Children.Add(view);
            }
        }
        else if (games is not null)
            buttons.Children.Add(HomeMotion.Button(t["HomeRescan"], "", primary: true, async () => { await games.RefreshAsync(); Show(vm); }));
        text.Children.Add(buttons);
        root.Children.Add(text);
        HomeMotion.Rise(text);
    }

    // The opaque page color under Home (the theme canvas); SectionPage uses it as Home's background.
    public static Color PageColor() => Application.Current.Resources["WindowBrush"] is SolidColorBrush b ? Color.FromArgb(255, b.Color.R, b.Color.G, b.Color.B) : Color.FromArgb(255, 18, 9, 15);
    private bool watching;
    private SectionViewModel? shown;

    // Compact "Onde você parou" block on the photo hero: two lines at most, the full note in "Ver anotação".
    private static FrameworkElement ResumeBlock(ITextCatalog t, Lotus.Core.Diary.ResumeNote note)
    {
        var box = new StackPanel { Spacing = 3 };
        box.Children.Add(new TextBlock { Text = t["RnHomeEyebrow"].ToUpperInvariant(), FontSize = 12, CharacterSpacing = 120, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromArgb(255, 0xF5, 0x8B, 0xB7)) });
        box.Children.Add(new TextBlock { Text = "\u201C" + note.Text.ReplaceLineEndings(" ") + "\u201D", FontSize = 18, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = new SolidColorBrush(Color.FromArgb(0xF2, 255, 244, 248)) });
        return new Border { Child = box, Padding = new Thickness(14, 8, 14, 10), CornerRadius = new CornerRadius(12), HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.FromArgb(0x66, 18, 9, 15)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xF5, 0x8B, 0xB7)), BorderThickness = new Thickness(1.5, 0, 0, 0) };
    }

    // The whole note without leaving Home, with a shortcut to edit it in the diary.
    private static void ShowNote(FrameworkElement anchor, ITextCatalog t, GameEntry game, Lotus.Core.Diary.ResumeNote note)
    {
        var panel = new StackPanel { Spacing = 8, MaxWidth = 380, Padding = new Thickness(4) };
        panel.Children.Add(new TextBlock { Text = t["RnTitle"], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 14 });
        panel.Children.Add(new TextBlock { Text = note.Text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontSize = 14 });
        panel.Children.Add(new TextBlock { Text = t.Format("RnUpdated", note.Updated.LocalDateTime.ToString("g", System.Globalization.CultureInfo.GetCultureInfo(t.Language))), FontSize = 12, Opacity = 0.7 });
        var flyout = new Flyout { Content = panel };
        var edit = new Button { Content = t["RnEdit"], Style = (Style)Application.Current.Resources["SoftButtonStyle"], Margin = new Thickness(0, 4, 0, 0) };
        edit.Click += (_, _) => { flyout.Hide(); if (MainWindow.Shell is { } shell) shell.OpenDiaryEditor(shell.Diary.Get(Lotus.Core.Diary.DiaryGames.KeyFor(game)) ?? Lotus.Core.Diary.DiaryGames.FromGame(game)); };
        panel.Children.Add(edit);
        flyout.ShowAt(anchor);
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
    private readonly Microsoft.UI.Xaml.Shapes.Path arc = new() { StrokeThickness = Stroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly TextBlock score = new() { FontSize = 72, FontWeight = Microsoft.UI.Text.FontWeights.Bold, CharacterSpacing = -40, HorizontalAlignment = HorizontalAlignment.Center, LineHeight = 74, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
    private readonly TextBlock eyebrow = new() { FontSize = 12, CharacterSpacing = 160, FontWeight = Microsoft.UI.Text.FontWeights.Bold };
    private readonly TextBlock headline = new() { FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 116, MaxLines = 2 };
    private readonly TextBlock advice = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel factRows = new() { Spacing = 10 };
    private readonly TextBlock facts = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock summary = new() { FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
    private readonly Button action = new();
    private SectionViewModel? vm;
    private double shown = -1;
    private static bool scanned; // the Tools scan feeds this card once per session, not on every visit
    private string target = "optimize";
    public event EventHandler<string>? NavigateRequested;
    private const double Ring = 196, Stroke = 14;

    // Visual 3.0: the readiness number is the centerpiece of Home — a big glowing ring on a dark glass card
    // that floats over the game art, with the live facts behind the score as short rows under it.
    public ReadinessCard()
    {
        RequestedTheme = ElementTheme.Dark; // it sits on the photo hero in every theme
        var res = Application.Current.Resources;
        var rose = res["RoseBrush"] is SolidColorBrush rb ? rb.Color : Color.FromArgb(255, 0xF2, 0x7B, 0xA8);
        var gold = res["GoldBrush"] is SolidColorBrush gb ? gb.Color : Color.FromArgb(255, 0xEE, 0xC1, 0xA8);
        var accent = res["AccentFillColorDefaultBrush"] is SolidColorBrush ab ? ab.Color : Color.FromArgb(255, 0xD6, 0x30, 0x6F);
        advice.Foreground = facts.Foreground = summary.Foreground = new SolidColorBrush(Color.FromArgb(255, 0xC9, 0xB6, 0xC0));
        arc.Stroke = HomeMotion.Gradient(new(0, 0), new(1, 1), (0, accent), (0.55, rose), (1, gold));
        var ring = new Grid { Width = Ring, Height = Ring, HorizontalAlignment = HorizontalAlignment.Center };
        var glow = new RadialGradientBrush(); glow.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0x66, accent.R, accent.G, accent.B), Offset = 0 }); glow.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, accent.R, accent.G, accent.B), Offset = 1 });
        ring.Children.Add(new Ellipse { Fill = glow, Margin = new Thickness(18) });
        ring.Children.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255)), StrokeThickness = Stroke });
        ring.Children.Add(arc);
        var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 2 };
        headline.Foreground = new SolidColorBrush(Color.FromArgb(255, 0x9F, 0xE3, 0xC1));
        center.Children.Add(score); center.Children.Add(headline);
        ring.Children.Add(center);
        eyebrow.Foreground = new SolidColorBrush(gold);
        action.Style = (Style)res["PrimaryButtonStyle"]; action.HorizontalAlignment = HorizontalAlignment.Stretch; action.MinHeight = 44;
        action.Click += (_, _) => NavigateRequested?.Invoke(this, target);
        // The ring sits in a Viewbox so the compact layout (narrow window) can shrink it whole, text included.
        ringBox = new Viewbox { Child = ring, Width = Ring, Height = Ring, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        details.Children.Add(advice); details.Children.Add(factRows); details.Children.Add(action);
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); body.ColumnDefinitions.Add(new ColumnDefinition());
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.Children.Add(eyebrow); body.Children.Add(ringBox); body.Children.Add(details);
        SetCompact(false);
        card = new Border
        {
            Padding = new Thickness(24), CornerRadius = new CornerRadius(22), BorderThickness = new Thickness(1), Child = body,
            Background = new SolidColorBrush(Color.FromArgb(0xD1, 0x16, 0x0A, 0x13)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x29, 0xFF, 0xE4, 0xEE)),
        };
        Motion.SetHover(card, 1.01);
        Content = card;
        Unloaded += (_, _) => { if (vm?.Monitoring is { } m) m.Updated -= OnUpdated; };
    }

    private readonly Grid body = new() { RowSpacing = 16, ColumnSpacing = 20 };
    private readonly StackPanel details = new() { Spacing = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly Viewbox ringBox;
    // Compact: a smaller ring beside the facts (narrow window, card under the hero); otherwise the ring on top.
    public void SetCompact(bool compact)
    {
        ringBox.Width = ringBox.Height = compact ? 132 : Ring;
        headline.Visibility = compact ? Visibility.Collapsed : Visibility.Visible; // too small to read in the small ring; the advice beside it says it
        Grid.SetColumnSpan(eyebrow, 2);
        Grid.SetRow(ringBox, 1); Grid.SetColumn(ringBox, 0); Grid.SetColumnSpan(ringBox, compact ? 1 : 2);
        Grid.SetRow(details, compact ? 1 : 2); Grid.SetColumn(details, compact ? 1 : 0); Grid.SetColumnSpan(details, compact ? 1 : 2);
    }

    // One live fact as a row: a status dot (green / amber) and the text.
    private static FrameworkElement FactRow(string text, bool warn)
    {
        var color = warn ? Color.FromArgb(255, 0xF0, 0xC2, 0x7D) : Color.FromArgb(255, 0x7F, 0xD1, 0xA8);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = text, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        return row;
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
        factRows.Children.Clear();
        if (r.MemoryPercent is { } m2) factRows.Children.Add(FactRow(t.Format("RdFactMem", Math.Round(m2)), m2 >= 80));
        if (r.CpuPercent is { } c2) factRows.Children.Add(FactRow(t.Format("RdFactCpu", Math.Round(c2)), c2 >= 70));
        if (r.GpuTemp is { } g2) factRows.Children.Add(FactRow(t.Format("RdFactGpu", Math.Round(g2)), g2 >= 80));
        if (factRows.Children.Count == 0) factRows.Children.Add(new TextBlock { Text = t["RdWaiting"], FontSize = 12, Foreground = advice.Foreground });
        summary.Text = vm.Guided?.ReadySummary ?? "";
        var more = string.Join("\n", new[] { facts.Text, summary.Text }.Where(x => x.Length > 0));
        ToolTipService.SetToolTip(card, more.Length > 0 ? more : null);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(card, more);
        Animate(r.Score);
    }

    // The ring sweeps and the number counts to the new score.
    private void Animate(int value)
    {
        if (Math.Abs(shown - value) < 1) return;
        var from = shown < 0 ? 0 : shown; shown = value;
        void Step(double p)
        {
            var v = from + (value - from) * p;
            score.Text = Math.Round(v).ToString();
            arc.Data = ArcGeometry(v / 100);
        }
        Step(0);
        Motion.Tween(arc, TimeSpan.FromMilliseconds(1000), Step, Motion.EaseOutQuart);
    }
    private static Geometry ArcGeometry(double fraction)
    {
        fraction = Math.Clamp(fraction, 0.001, 0.999);
        const double r = (Ring - Stroke) / 2, c = Ring / 2; var angle = fraction * 2 * Math.PI - Math.PI / 2;
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
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, label); // screen readers read the label, not the icon
        return b;
    }
    // Secondary buttons over the dark cover art: the same dark glass in every theme (a light theme's soft
    // button would put white-on-pale or dark-on-dark text on the photo).
    public static void OnPhoto(Button b)
    {
        static SolidColorBrush B(byte a, byte r, byte g, byte bl) => new(Color.FromArgb(a, r, g, bl));
        b.Resources["ButtonBackground"] = B(0x8C, 0x14, 0x0C, 0x12);
        b.Resources["ButtonBackgroundPointerOver"] = B(0xB3, 0x2A, 0x18, 0x24);
        b.Resources["ButtonBackgroundPressed"] = B(0xCC, 0x0E, 0x08, 0x0C);
        b.Resources["ButtonBackgroundDisabled"] = B(0x59, 0x14, 0x0C, 0x12);
        b.Resources["ButtonForeground"] = b.Resources["ButtonForegroundPointerOver"] = b.Resources["ButtonForegroundPressed"] = B(0xFF, 0xFF, 0xF4, 0xF8);
        b.Resources["ButtonForegroundDisabled"] = B(0x99, 0xFF, 0xF4, 0xF8);
        b.Resources["ButtonBorderBrush"] = b.Resources["ButtonBorderBrushPointerOver"] = b.Resources["ButtonBorderBrushPressed"] = B(0x40, 0xFF, 0xFF, 0xFF);
        b.Foreground = (SolidColorBrush)b.Resources["ButtonForeground"];
    }
    // Hero buttons: taller, rounder; the primary one a little larger.
    public static Button Big(Button b, bool primary)
    {
        b.MinHeight = 52; b.CornerRadius = new CornerRadius(14);
        if (primary) { b.Padding = new Thickness(28, 10, 28, 10); b.FontSize = 16; }
        return b;
    }
    public static void Fade(UIElement e, double to, int ms)
    {
        if (!Motion.Active) { e.Opacity = to; return; }
        var a = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = to, Duration = TimeSpan.FromMilliseconds(ms) };
        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard(); Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(a, e); Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(a, "Opacity"); sb.Children.Add(a); sb.Begin();
    }
    // Slow push-in on the art, origin bound to the real size.
    public static void Drift(UIElement e)
    {
        if (!Motion.Active) return;
        var v = ElementCompositionPreview.GetElementVisual(e); var c = v.Compositor;
        v.StartAnimation("CenterPoint", c.CreateExpressionAnimation("Vector3(this.Target.Size.X * 0.65, this.Target.Size.Y * 0.5, 0)"));
        var a = c.CreateVector3KeyFrameAnimation(); a.InsertKeyFrame(0, new(1.0f, 1.0f, 1)); a.InsertKeyFrame(1, new(1.08f, 1.08f, 1));
        a.Duration = TimeSpan.FromSeconds(20); a.Direction = Microsoft.UI.Composition.AnimationDirection.Alternate; a.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;
        v.StartAnimation("Scale", a);
    }
    public static void Rise(UIElement e)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(e, true);
        if (!Motion.Active) return;
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
