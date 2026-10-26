using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Lotus.App.Controls;

// Home's rotating banner. Two kinds of slides: Lotus art drawn here (gradients, a big lotus, drifting light orbs)
// and game art (installed games, what's trending). Crossfade every 7 s, slow zoom on photos, pauses under the
// pointer, dots to jump. Everything stops with Reduce motion (slides then change only by click).
public sealed partial class HomeBanner : UserControl
{
    public sealed record Slide(string Eyebrow, string Title, string Subtitle, string? Image, int Art, string? Action, Action? OnAction, string Glyph = "");

    private readonly List<Slide> slides = [];
    private int index = -1;
    private bool showingA, hovering;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? timer;

    public HomeBanner()
    {
        InitializeComponent();
        // Hard clip with rounded corners: zoomed photos never spill past the card.
        Root.SizeChanged += (_, e) =>
        {
            var v = V(Root); var c = v.Compositor;
            var shape = c.CreateRoundedRectangleGeometry(); shape.Size = new Vector2((float)e.NewSize.Width, (float)e.NewSize.Height); shape.CornerRadius = new Vector2(22, 22);
            v.Clip = c.CreateGeometricClip(shape);
        };
        Loaded += (_, _) => { timer?.Start(); };
        Unloaded += (_, _) => timer?.Stop();
    }

    public void SetSlides(IEnumerable<Slide> list)
    {
        slides.Clear(); slides.AddRange(list);
        foreach (var s in slides) if (s.Image is { } u) Photo(u); // start downloading every photo now
        Dots.Children.Clear();
        for (var i = 0; i < slides.Count; i++)
        {
            var n = i;
            var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromArgb(0x66, 255, 255, 255)) };
            var hit = new Button { Content = dot, Padding = new Thickness(3), MinWidth = 0, MinHeight = 0, Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(hit, slides[i].Title);
            hit.Click += (_, _) => { Show(n); Restart(); };
            Dots.Children.Add(hit);
        }
        index = -1; Show(0);
        timer ??= DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(10); // calm pace: one slide every 10 s
        timer.Tick -= OnTick; timer.Tick += OnTick;
        Restart();
    }

    private void OnTick(Microsoft.UI.Dispatching.DispatcherQueueTimer t, object o) { if (!hovering && Motion.Enabled && slides.Count > 1) Show((index + 1) % slides.Count); }
    private void Restart() { timer?.Stop(); if (Motion.Enabled) timer?.Start(); }
    private void OnPrev(object s, RoutedEventArgs e) { if (slides.Count > 0) { Show((index - 1 + slides.Count) % slides.Count); Restart(); } }
    private void OnNext(object s, RoutedEventArgs e) { if (slides.Count > 0) { Show((index + 1) % slides.Count); Restart(); } }
    private void OnEnter(object s, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) { hovering = true; Fade(Prev, 1, 200); Fade(Next, 1, 200); }
    private void OnLeave(object s, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) { hovering = false; Fade(Prev, 0, 200); Fade(Next, 0, 200); }

    private void Show(int i)
    {
        if (i == index || i < 0 || i >= slides.Count) return;
        index = i;
        var incoming = showingA ? LayerB : LayerA; var outgoing = showingA ? LayerA : LayerB; showingA = !showingA;
        incoming.Children.Clear(); incoming.Children.Add(Build(slides[i]));
        Canvas.SetZIndex(incoming, 1); Canvas.SetZIndex(outgoing, 0);
        Fade(incoming, 1, 700); Fade(outgoing, 0, 700);
        for (var d = 0; d < Dots.Children.Count; d++)
            if (Dots.Children[d] is Button { Content: Border dot })
            {
                dot.Width = d == i ? 26 : 8;
                dot.Background = d == i ? (Brush)Application.Current.Resources["GoldGradientBrush"] : new SolidColorBrush(Color.FromArgb(0x66, 255, 255, 255));
            }
    }

    // One BitmapImage per URL for the whole session: decoded once, reused by every slide change and visit.
    private static readonly Dictionary<string, BitmapImage> photos = [];
    private static BitmapImage? Photo(string url)
    {
        if (photos.TryGetValue(url, out var b)) return b;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        return photos[url] = new BitmapImage(uri);
    }

    // ---- Slide drawing ----
    private FrameworkElement Build(Slide s)
    {
        var g = new Grid();
        if (s.Image is { } url && Photo(url) is { } bmp)
        {
            // Preloaded photos show at once (no flash of the Lotus art); others fade in when they arrive.
            var ready = bmp.PixelWidth > 0;
            var img = new Image { Stretch = Stretch.UniformToFill, Opacity = ready ? 1 : 0, Source = bmp, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            if (ready) KenBurns(img);
            else bmp.ImageOpened += (_, _) => { Fade(img, 1, 500); KenBurns(img); };
            bmp.ImageFailed += (_, _) => img.Visibility = Visibility.Collapsed; // the Lotus art underneath stays
            g.Children.Add(Art(s.Art)); // shows while the photo downloads (or if it fails)
            g.Children.Add(img);
        }
        else g.Children.Add(Art(s.Art));
        // Readability: plum fade from the left and the bottom.
        g.Children.Add(new Rectangle { Fill = Gradient(new(0, 0), new(1, 0), (0, Color.FromArgb(0xE6, 18, 9, 15)), (0.55, Color.FromArgb(0x66, 18, 9, 15)), (1, Color.FromArgb(0, 18, 9, 15))) });
        g.Children.Add(new Rectangle { Fill = Gradient(new(0, 0), new(0, 1), (0.5, Color.FromArgb(0, 18, 9, 15)), (1, Color.FromArgb(0xB3, 18, 9, 15))) });

        var text = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(40, 0, 40, 0), MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
        text.Children.Add(new TextBlock { Text = s.Eyebrow.ToUpperInvariant(), FontSize = 11.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, CharacterSpacing = 160, Foreground = (Brush)Application.Current.Resources["GoldBrush"] });
        text.Children.Add(new TextBlock { Text = s.Title, FontSize = 36, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = new SolidColorBrush(Colors.White) });
        text.Children.Add(new TextBlock { Text = s.Subtitle, FontSize = 15, TextWrapping = TextWrapping.Wrap, MaxLines = 3, Foreground = new SolidColorBrush(Color.FromArgb(0xD9, 255, 240, 246)) });
        if (s.Action is { } label && s.OnAction is { } act)
        {
            var b = new Button { Style = (Style)Application.Current.Resources["PrimaryButtonStyle"], MinHeight = 44, Margin = new Thickness(0, 10, 0, 0) };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new FontIcon { Glyph = s.Glyph, FontSize = 14 }); row.Children.Add(new TextBlock { Text = label });
            b.Content = row; b.Click += (_, _) => act();
            text.Children.Add(b);
        }
        g.Children.Add(text);
        Rise(text);
        return g;
    }

    // Four Lotus art styles: wine dusk, rose-gold dawn, violet night, crimson ember.
    private static readonly (Color A, Color B, Color C, Color Glow)[] Palettes =
    [
        (Color.FromArgb(255, 74, 14, 46), Color.FromArgb(255, 150, 22, 82), Color.FromArgb(255, 24, 8, 18), Color.FromArgb(255, 242, 123, 168)),
        (Color.FromArgb(255, 96, 40, 40), Color.FromArgb(255, 201, 132, 104), Color.FromArgb(255, 28, 12, 16), Color.FromArgb(255, 248, 221, 203)),
        (Color.FromArgb(255, 40, 16, 70), Color.FromArgb(255, 120, 60, 190), Color.FromArgb(255, 14, 8, 24), Color.FromArgb(255, 190, 150, 255)),
        (Color.FromArgb(255, 80, 10, 20), Color.FromArgb(255, 200, 40, 60), Color.FromArgb(255, 20, 6, 10), Color.FromArgb(255, 255, 150, 120)),
    ];
    private const string Petals = "M31,50 C18,50 7,43 2,31 C14,30 25,37 31,50 Z M33,50 C46,50 57,43 62,31 C50,30 39,37 33,50 Z M30,48 C19,42 13,30 16,17 C25,23 30,34 30,48 Z M34,48 C45,42 51,30 48,17 C39,23 34,34 34,48 Z M32,7 C41,17 44,32 32,48 C20,32 23,17 32,7 Z";

    private FrameworkElement Art(int style)
    {
        var p = Palettes[Math.Abs(style) % Palettes.Length];
        var g = new Grid { Background = Gradient(new(0, 0), new(1, 1), (0, p.C), (0.45, p.A), (1, p.B)) };
        // Soft glows
        g.Children.Add(Orb(p.Glow, 420, 0.30, HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, -160, -80, 0), 0));
        g.Children.Add(Orb(p.B, 360, 0.35, HorizontalAlignment.Center, VerticalAlignment.Bottom, new Thickness(0, 0, 0, -220), 1));
        // Big lotus on the right, slowly breathing
        var flower = new Viewbox { Width = 300, Height = 300, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 10, 60, 0), Opacity = 0.9 };
        var canvas = new Canvas { Width = 64, Height = 64 };
        canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = Geometry(Petals), Fill = Gradient(new(0.5, 0), new(0.5, 1), (0, Color.FromArgb(0x99, p.Glow.R, p.Glow.G, p.Glow.B)), (1, Color.FromArgb(0x22, p.B.R, p.B.G, p.B.B))) });
        canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = Geometry(Petals), Stroke = (Brush)Application.Current.Resources["GoldGradientBrush"], StrokeThickness = 0.9, StrokeLineJoin = PenLineJoin.Round });
        flower.Child = canvas;
        g.Children.Add(flower);
        Breathe(flower);
        // Sparkles
        var rnd = new Random(style * 7919 + 13);
        var sparks = new Canvas { IsHitTestVisible = false };
        for (var i = 0; i < 18; i++)
        {
            var size = 2 + rnd.NextDouble() * 3;
            var dot = new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(Color.FromArgb((byte)(90 + rnd.Next(120)), 255, 236, 244)) };
            Canvas.SetLeft(dot, 380 + rnd.NextDouble() * 700); Canvas.SetTop(dot, rnd.NextDouble() * 300);
            sparks.Children.Add(dot); Twinkle(dot, rnd.Next(1200, 3200), rnd.Next(0, 2000));
        }
        g.Children.Add(sparks);
        return g;
    }

    private static Ellipse Orb(Color c, double size, double opacity, HorizontalAlignment h, VerticalAlignment v, Thickness m, int seed)
    {
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb((byte)(255 * opacity), c.R, c.G, c.B), Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, c.R, c.G, c.B), Offset = 1 });
        var e = new Ellipse { Width = size, Height = size, Fill = brush, HorizontalAlignment = h, VerticalAlignment = v, Margin = m, IsHitTestVisible = false };
        Drift(e, seed);
        return e;
    }

    // ---- Motion helpers (compositor; all off with Reduce motion) ----
    private static Visual V(UIElement e) => ElementCompositionPreview.GetElementVisual(e);
    private static void Fade(UIElement e, double to, int ms)
    {
        if (!Motion.Enabled) { e.Opacity = to; return; }
        var a = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = to, Duration = TimeSpan.FromMilliseconds(ms), EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseInOut } };
        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard(); Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(a, e); Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(a, "Opacity"); sb.Children.Add(a); sb.Begin();
    }
    private static void KenBurns(FrameworkElement e)
    {
        if (!Motion.Enabled) return;
        var v = V(e); var c = v.Compositor;
        // Zoom origin follows the element's real size (it can change after the photo loads).
        var center = c.CreateExpressionAnimation("Vector3(this.Target.Size.X * 0.6, this.Target.Size.Y * 0.5, 0)");
        v.StartAnimation("CenterPoint", center);
        var a = c.CreateVector3KeyFrameAnimation(); a.InsertKeyFrame(0, new(1.02f, 1.02f, 1)); a.InsertKeyFrame(1, new(1.12f, 1.12f, 1), c.CreateLinearEasingFunction());
        a.Duration = TimeSpan.FromSeconds(9);
        v.StartAnimation("Scale", a);
    }
    private static void Rise(UIElement e)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(e, true);
        if (!Motion.Enabled) return;
        var v = V(e); var c = v.Compositor;
        var ease = c.CreateCubicBezierEasingFunction(new(0.05f, 0.7f), new(0.1f, 1f));
        var a = c.CreateVector3KeyFrameAnimation(); a.InsertKeyFrame(0, new(-24, 0, 0)); a.InsertKeyFrame(1, Vector3.Zero, ease); a.Duration = TimeSpan.FromMilliseconds(700); a.DelayTime = TimeSpan.FromMilliseconds(150);
        var f = c.CreateScalarKeyFrameAnimation(); f.InsertKeyFrame(0, 0); f.InsertKeyFrame(1, 1, ease); f.Duration = TimeSpan.FromMilliseconds(700); f.DelayTime = TimeSpan.FromMilliseconds(150);
        a.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay; f.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        v.StartAnimation("Translation", a); v.StartAnimation("Opacity", f);
    }
    private static void Breathe(FrameworkElement e)
    {
        if (!Motion.Enabled) return;
        e.SizeChanged += (_, _) =>
        {
            var v = V(e); var c = v.Compositor; v.CenterPoint = new Vector3((float)e.ActualWidth / 2, (float)e.ActualHeight * 0.7f, 0);
            var a = c.CreateVector3KeyFrameAnimation(); a.InsertKeyFrame(0, Vector3.One); a.InsertKeyFrame(1, new(1.05f, 1.05f, 1));
            a.Duration = TimeSpan.FromSeconds(4); a.Direction = AnimationDirection.Alternate; a.IterationBehavior = AnimationIterationBehavior.Forever;
            v.StartAnimation("Scale", a);
        };
    }
    private static void Drift(UIElement e, int seed)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(e, true);
        if (!Motion.Enabled) return;
        var v = V(e); var c = v.Compositor;
        var a = c.CreateVector3KeyFrameAnimation(); a.InsertKeyFrame(0, Vector3.Zero); a.InsertKeyFrame(1, seed == 0 ? new(-60, 30, 0) : new(70, -20, 0));
        a.Duration = TimeSpan.FromSeconds(seed == 0 ? 9 : 11); a.Direction = AnimationDirection.Alternate; a.IterationBehavior = AnimationIterationBehavior.Forever;
        v.StartAnimation("Translation", a);
    }
    private static void Twinkle(UIElement e, int ms, int delay)
    {
        if (!Motion.Enabled) return;
        var v = V(e); var c = v.Compositor;
        var a = c.CreateScalarKeyFrameAnimation(); a.InsertKeyFrame(0, 0.15f); a.InsertKeyFrame(0.5f, 1f); a.InsertKeyFrame(1, 0.15f);
        a.Duration = TimeSpan.FromMilliseconds(ms); a.DelayTime = TimeSpan.FromMilliseconds(delay); a.IterationBehavior = AnimationIterationBehavior.Forever;
        v.StartAnimation("Opacity", a);
    }

    private static LinearGradientBrush Gradient(Windows.Foundation.Point from, Windows.Foundation.Point to, params (double Offset, Color Color)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = from, EndPoint = to };
        foreach (var (o, c) in stops) b.GradientStops.Add(new GradientStop { Offset = o, Color = c });
        return b;
    }
    private static Geometry Geometry(string data) => (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), data);
}
