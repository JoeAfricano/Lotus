using Lotus.Core;
using Lotus.Core.Diary;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;

namespace Lotus.App.Controls;

// The reward after logging a game: confetti, the cover flips in, XP counts up, the level bar fills (and rolls over
// on a level up) and new badges pop in one by one. With Reduce motion it shows the end state at once.
public sealed partial class CelebrationView : UserControl
{
    public event EventHandler? DiaryRequested;
    private readonly Random random = new();
    public CelebrationView()
    {
        InitializeComponent();
        KeyDown += (_, e) => { if (e.Key is Windows.System.VirtualKey.Escape or Windows.System.VirtualKey.Enter) { Close(); e.Handled = true; } };
    }

    public void Show(DiaryProgressChange change, ITextCatalog t)
    {
        var e = change.Entry;
        Title.Text = t[change.JustCompleted ? "DyCelebrateDone" : "DyCelebrateSaved"];
        GameName.Text = e.Name;
        Cover.Source = e.Cover is { } c && Uri.TryCreate(c, UriKind.Absolute, out var u) ? new BitmapImage(u) { DecodePixelWidth = 320 } : null;
        ToDiary.Content = t["DyCelebrateShare"]; Continue.Content = t["DyCelebrateContinue"];
        var after = Progression.LevelStart(change.LevelAfter); var next = Progression.LevelStart(change.LevelAfter + 1);
        LevelLabel.Text = t.Format("DyLevel", change.LevelAfter) + " · " + t[Progression.TitleKey(change.LevelAfter)];
        XpLabel.Text = t.Format("DyXp", change.XpAfter - after, next - after);
        LevelUp.Visibility = change.LevelUp ? Visibility.Visible : Visibility.Collapsed;
        LevelUp.Text = t.Format("DyCelebrateLevel", change.LevelAfter, t[Progression.TitleKey(change.LevelAfter)]);
        BadgeArea.Visibility = change.NewBadges.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BadgeTitle.Text = t["DyCelebrateBadge"].ToUpperInvariant();
        BadgeRow.Children.Clear();
        foreach (var b in change.NewBadges) BadgeRow.Children.Add(BadgeTile(b, t));

        Visibility = Visibility.Visible;
        Focus(FocusState.Programmatic);
        var sb = new Storyboard();
        Add(sb, Backdrop, "Opacity", 0, 1, 250); Add(sb, Card, "Opacity", 0, 1, 300);
        Add(sb, CardScale, "ScaleX", 0.9, 1, 520, new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut });
        Add(sb, CardScale, "ScaleY", 0.9, 1, 520, new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut });
        Add(sb, CoverFlip, "RotationY", 90, 0, 700, new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut }, 150);
        sb.Begin();

        UiSound.Play(change.JustCompleted || change.LevelUp ? UiSound.Cue.Score : UiSound.Cue.Done);
        if (change.JustCompleted || change.LevelUp) Burst();
        CountXp(change.XpGained, t);
        FillBar(change, t);
        for (var i = 0; i < BadgeRow.Children.Count; i++) PopLater((FrameworkElement)BadgeRow.Children[i], 900 + i * 220);
    }

    public void Close()
    {
        if (Visibility != Visibility.Visible) return;
        var sb = new Storyboard();
        Add(sb, Backdrop, "Opacity", 1, 0, 180); Add(sb, Card, "Opacity", 1, 0, 180);
        sb.Completed += (_, _) => { Visibility = Visibility.Collapsed; Confetti.Children.Clear(); };
        sb.Begin();
    }

    private void OnContinue(object sender, object e) => Close();
    private void OnDiary(object sender, RoutedEventArgs e) { Close(); DiaryRequested?.Invoke(this, EventArgs.Empty); }

    private void CountXp(int gained, ITextCatalog t)
    {
        if (!Motion.Enabled || gained <= 0) { Xp.Text = t.Format("DyCelebrateXp", Math.Max(0, gained)); return; }
        var start = DateTime.UtcNow; var timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.Tick += (s, _) =>
        {
            var p = Math.Min(1, (DateTime.UtcNow - start).TotalMilliseconds / 900);
            Xp.Text = t.Format("DyCelebrateXp", (int)Math.Round(gained * (1 - Math.Pow(1 - p, 3))));
            if (p >= 1) s.Stop();
        };
        timer.Start();
    }

    // Before → after; on a level up the bar fills to the end, flashes and starts again from the new level.
    private void FillBar(DiaryProgressChange c, ITextCatalog t)
    {
        double Pct(int xp, int level) { var a = Progression.LevelStart(level); var b = Progression.LevelStart(level + 1); return Math.Clamp((xp - a) * 100.0 / Math.Max(1, b - a), 0, 100); }
        var target = Pct(c.XpAfter, c.LevelAfter);
        if (!Motion.Enabled) { LevelBar.Value = target; return; }
        var phases = c.LevelUp ? new[] { (Pct(c.XpBefore, c.LevelBefore), 100.0), (0.0, target) } : [(Pct(c.XpBefore, c.LevelBefore), target)];
        var phase = 0; var start = DateTime.UtcNow.AddMilliseconds(350); var timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.Tick += (s, _) =>
        {
            var ms = (DateTime.UtcNow - start).TotalMilliseconds; if (ms < 0) return;
            var (from, to) = phases[phase]; var p = Math.Min(1, ms / 700);
            LevelBar.Value = from + (to - from) * (1 - Math.Pow(1 - p, 3));
            if (p < 1) return;
            if (++phase >= phases.Length) { s.Stop(); return; }
            start = DateTime.UtcNow; Motion.Pop(LevelUp); UiSound.Play(UiSound.Cue.Notify);
        };
        timer.Start();
    }

    private FrameworkElement BadgeTile(Badge b, ITextCatalog t)
    {
        var panel = new StackPanel { Spacing = 4, Width = 96, Opacity = Motion.Enabled ? 0 : 1 };
        var circle = new Border { Width = 54, Height = 54, CornerRadius = new CornerRadius(27), HorizontalAlignment = HorizontalAlignment.Center,
            Background = (Brush)Application.Current.Resources["GoldGradientBrush"], Child = new FontIcon { Glyph = b.Glyph, FontSize = 22, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 58, 15, 36)) } };
        panel.Children.Add(circle);
        panel.Children.Add(new TextBlock { Text = t["DyBadge" + b.Id], FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center });
        ToolTipService.SetToolTip(panel, t["DyBadge" + b.Id + "Hint"]);
        return panel;
    }
    private void PopLater(FrameworkElement f, int delayMs)
    {
        if (!Motion.Enabled) return;
        var timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(delayMs); timer.IsRepeating = false;
        timer.Tick += (_, _) => { f.Opacity = 1; Motion.Pop(f); UiSound.Play(UiSound.Cue.Toggle); };
        timer.Start();
    }

    // ~80 petals and sparks in the Lotus colours fall, drift and spin.
    private void Burst()
    {
        if (!Motion.Enabled) return;
        Confetti.Children.Clear();
        // Measured from the window (this control was collapsed until now, so its own size is still 0).
        var w = Math.Max(400, XamlRoot?.Size.Width ?? ActualWidth); var h = Math.Max(400, XamlRoot?.Size.Height ?? ActualHeight);
        var colors = new[] { (255, 242, 123, 168), (255, 238, 193, 168), (255, 214, 48, 111), (255, 248, 221, 203), (255, 156, 92, 240), (255, 255, 255, 255) };
        var sb = new Storyboard();
        for (var i = 0; i < 80; i++)
        {
            var (a, r, g, b) = colors[random.Next(colors.Length)];
            var piece = new Rectangle { Width = 6 + random.NextDouble() * 6, Height = 10 + random.NextDouble() * 8, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(Windows.UI.Color.FromArgb((byte)a, (byte)r, (byte)g, (byte)b)) };
            var turn = new CompositeTransform { CenterX = 5, CenterY = 7 }; piece.RenderTransform = turn;
            var x = w / 2 + (random.NextDouble() - 0.5) * w * 0.5; Canvas.SetLeft(piece, x); Canvas.SetTop(piece, -20);
            Confetti.Children.Add(piece);
            var delay = random.Next(0, 500); var ms = 1800 + random.Next(0, 1400);
            Add(sb, turn, "TranslateY", 0, h * (0.7 + random.NextDouble() * 0.4), ms, new QuadraticEase { EasingMode = EasingMode.EaseIn }, delay);
            Add(sb, turn, "TranslateX", 0, (random.NextDouble() - 0.5) * w * 0.7, ms, new SineEase { EasingMode = EasingMode.EaseOut }, delay);
            Add(sb, turn, "Rotation", 0, random.Next(-720, 720), ms, null, delay);
            Add(sb, piece, "Opacity", 1, 0, 600, null, delay + ms - 600);
        }
        sb.Begin();
    }

    private static void Add(Storyboard sb, DependencyObject target, string property, double from, double to, int ms, EasingFunctionBase? ease = null, int delay = 0)
    {
        var a = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(Motion.Enabled ? ms : 0), BeginTime = TimeSpan.FromMilliseconds(Motion.Enabled ? delay : 0), EasingFunction = ease ?? new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(a, target); Storyboard.SetTargetProperty(a, property); sb.Children.Add(a);
    }
}
