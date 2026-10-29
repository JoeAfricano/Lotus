using Lotus.Core;
using Lotus.Core.Achievements;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Lotus.App.Controls;

// Bottom-right notice for new achievements: one at a time from a queue, a spinning medal, a chime and a short timer
// (paused while the pointer is over it). "Ver conquistas" opens the diary on the achievements.
public sealed partial class TrophyToast : UserControl
{
    private sealed record Item(string Eyebrow, string Title, string Body, string Xp, string Glyph, string Tier, string? Art, string View, bool Steam);
    private double seconds = 7;
    private readonly Queue<Item> queue = new();
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? timer;
    private bool visible, held;
    private double left;
    public event EventHandler<bool>? ViewRequested; // true: Steam section

    public TrophyToast() => InitializeComponent();

    public void Show(TrophyNews news, ITextCatalog t)
    {
        string N(int n) => n.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo(t.Language));
        var xp = t.Format("TrToastXp", N(news.XpGained));
        if (news.FirstImport)
        {
            var body = t.Format("TrToastImportBody", N(news.SteamTotal), N(news.SteamGames));
            if (news.Trophies.Count > 0) body += " " + t.Format("TrToastMany", news.Trophies.Count) + ".";
            queue.Enqueue(new("STEAM", t["TrToastImport"], body, xp, "", "gold", null, t["TrToastView"], news.Trophies.Count == 0));
        }
        else
        {
            if (news.Trophies.Count == 1)
            {
                var s = news.Trophies[0];
                queue.Enqueue(new(t["TrToastOne"].ToUpperInvariant(), t[s.Def.NameKey], t[s.Def.HintKey], "+" + s.Def.Xp + " XP", s.Def.Glyph,
                    s.Def.Tier.ToString().ToLowerInvariant(), null, t["TrToastView"], false));
            }
            else if (news.Trophies.Count > 1)
            {
                var best = news.Trophies.OrderByDescending(s => s.Def.Tier).First();
                queue.Enqueue(new(t.Format("TrToastMany", news.Trophies.Count).ToUpperInvariant(), string.Join(" · ", news.Trophies.OrderByDescending(s => s.Def.Tier).Take(3).Select(s => t[s.Def.NameKey])),
                    news.Trophies.Count > 3 ? "+" + (news.Trophies.Count - 3) : "", "+" + news.Trophies.Sum(s => s.Def.Xp) + " XP", best.Def.Glyph, best.Def.Tier.ToString().ToLowerInvariant(), null, t["TrToastView"], false));
            }
            if (news.NewSteam > 0)
            {
                var title = news.SteamGame is { } g ? t.Format("TrToastSteamIn", news.NewSteam, g) : t.Format("TrToastSteam", news.NewSteam);
                queue.Enqueue(new("STEAM", title, "", news.Trophies.Count == 0 ? xp : "", "", "gold", null, t["TrToastView"], true));
            }
        }
        if (!visible) Next();
    }

    private void Next()
    {
        if (queue.Count == 0) { visible = false; Visibility = Visibility.Collapsed; return; }
        var item = queue.Dequeue();
        visible = true;
        Eyebrow.Text = item.Eyebrow; Title.Text = item.Title; Body.Text = item.Body; Body.Visibility = item.Body.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        XpText.Text = item.Xp; XpPill.Visibility = item.Xp.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ViewLabel.Text = item.View; ViewButton.Tag = item.Steam;
        MedalGlyph.Glyph = item.Glyph;
        MedalFill.Fill = (Microsoft.UI.Xaml.Media.Brush)new TierFillConverter().Convert(item.Tier, typeof(object), null!, "");
        ArtFrame.Visibility = Visibility.Collapsed;
        if (item.Art is { } art && Uri.TryCreate(art, UriKind.Absolute, out var u)) { Art.ImageSource = new BitmapImage(u) { DecodePixelWidth = 160 }; ArtFrame.Visibility = Visibility.Visible; }
        Visibility = Visibility.Visible;
        var sb = new Storyboard();
        Add(sb, Card, "Opacity", 0, 1, 260);
        Add(sb, Move, "TranslateX", 40, 0, 420, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 });
        Add(sb, MedalSpin, "Rotation", -200, 0, 900, new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 4 });
        sb.Begin();
        UiSound.Play(UiSound.Cue.Score);
        seconds = item.Eyebrow == "STEAM" && item.Body.Length > 0 ? 12 : 7; // the first import gets more time
        left = seconds; held = false; TimerScale.ScaleX = 1;
        timer ??= DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(50);
        timer.Tick -= OnTick; timer.Tick += OnTick;
        timer.Start();
    }

    private void OnTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (held) return;
        left -= 0.05;
        TimerScale.ScaleX = Math.Max(0, left / seconds);
        if (left <= 0) Hide();
    }

    private void Hide()
    {
        timer?.Stop();
        var sb = new Storyboard();
        Add(sb, Card, "Opacity", 1, 0, 200);
        Add(sb, Move, "TranslateX", 0, 30, 220);
        sb.Completed += (_, _) => Next();
        sb.Begin();
    }

    private void OnHold(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => held = true;
    private void OnRelease(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => held = false;
    private void OnClose(object sender, RoutedEventArgs e) => Hide();
    private void OnView(object sender, RoutedEventArgs e)
    {
        var steam = ViewButton.Tag is true;
        queue.Clear();
        Hide();
        ViewRequested?.Invoke(this, steam);
    }

    private static void Add(Storyboard sb, DependencyObject target, string property, double from, double to, int ms, EasingFunctionBase? ease = null)
    {
        var a = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(Motion.Enabled ? ms : 0), EasingFunction = ease ?? new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(a, target); Storyboard.SetTargetProperty(a, property); sb.Children.Add(a);
    }
}
