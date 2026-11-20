using Lotus.Core.Brand;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Lotus.App.Controls;

// Opening intro drawn with shapes and storyboards (no video file): two rings of petals open from a closed bud, a gold
// halo pulses, sparks rise and the name appears, then everything fades into the app. The short version runs the same
// scene faster. Lotus loads behind it; once closed, the shapes are removed and nothing keeps running.
public sealed partial class IntroView : UserControl
{
    private Storyboard? story;
    private DispatcherTimer? limit;
    private bool closing;
    private double speed = 1; // scene times below are divided by this (see Play)
    public event EventHandler? Finished;

    public IntroView()
    {
        InitializeComponent();
        var esc = new KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape };
        esc.Invoked += (_, e) => { e.Handled = true; Close(); };
        KeyboardAccelerators.Add(esc);
    }

    public void Play(IntroKind kind, string skipHint, double volume)
    {
        if (kind == IntroKind.None) return;
        try
        {
            speed = kind == IntroKind.Full ? 1.55 : 3.3; // about 3 s full, 1.4 s short
            Visibility = Visibility.Visible; Opacity = 1; SkipHint.Text = skipHint;
            Title.Foreground = (Brush)Application.Current.Resources["BrandGoldGradientBrush"];
            story = new Storyboard();
            BuildFlower(story);
            BuildSparks(story);
            Anim(story, Glow, "Opacity", 0, 1, 0, 1.2);
            Anim(story, GlowTransform, "ScaleX", 0.7, 1.05, 0, 4.8, Soft()); Anim(story, GlowTransform, "ScaleY", 0.7, 1.05, 0, 4.8, Soft());
            Anim(story, FlowerTransform, "Rotation", -14, 6, 0, 5.2, Soft());
            Anim(story, Title, "Opacity", 0, 1, 1.7, 0.7); Anim(story, TitleShift, "Y", 18, 0, 1.7, 0.8, Spring());
            Anim(story, Tagline, "Opacity", 0, 1, 2.3, 0.7);
            Anim(story, SkipHint, "Opacity", 0, 1, 0.6, 0.4);
            story.Begin();
            if (volume > 0) UiSound.Play(UiSound.Cue.Welcome);
            Focus(FocusState.Programmatic);
            limit = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.6 / speed) };
            limit.Tick += (_, _) => Close(); limit.Start();
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Intro not played"); Close(); }
    }

    // Back ring (deeper rose, longer) and front ring (rose to gold), each petal turning out of the closed bud.
    private void BuildFlower(Storyboard sb)
    {
        var rings = new[]
        {
            (Count: 8, Length: 150.0, Width: 50.0, Offset: 0.0, From: "#6E1A44", To: "#AD245E", Start: 0.25, Opacity: 0.9),
            (Count: 8, Length: 118.0, Width: 42.0, Offset: 22.5, From: "#AD245E", To: "#F07A9C", Start: 0.55, Opacity: 1.0),
            (Count: 5, Length: 64.0, Width: 28.0, Offset: 36.0, From: "#F07A9C", To: "#EEC1A8", Start: 0.95, Opacity: 1.0),
        };
        foreach (var ring in rings)
            for (var i = 0; i < ring.Count; i++)
            {
                var angle = ring.Offset + i * 360.0 / ring.Count;
                var petal = Petal(ring.Length, ring.Width, ring.From, ring.To);
                petal.Opacity = 0;
                var tf = (CompositeTransform)petal.RenderTransform;
                Flower.Children.Add(petal);
                var at = ring.Start + i * 0.06;
                Anim(sb, petal, "Opacity", 0, ring.Opacity, at, 0.35);
                Anim(sb, tf, "Rotation", angle - 70, angle, at, 1.1, Spring());
                Anim(sb, tf, "ScaleX", 0.2, 1, at, 1.0, Spring()); Anim(sb, tf, "ScaleY", 0.2, 1, at, 1.0, Spring());
            }

        // Heart of the flower and a halo that rings out once the bloom is complete.
        var halo = new Ellipse { Width = 300, Height = 300, Stroke = Gradient("#F5D7C4", "#8F5E16"), StrokeThickness = 1.5, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new CompositeTransform() };
        Place(halo, 300); Flower.Children.Insert(0, halo);
        Anim(sb, halo, "Opacity", 0.9, 0, 1.45, 1.3);
        Anim(sb, halo.RenderTransform, "ScaleX", 0.4, 1.35, 1.45, 1.3, Soft()); Anim(sb, halo.RenderTransform, "ScaleY", 0.4, 1.35, 1.45, 1.3, Soft());
        var heart = new Ellipse { Width = 30, Height = 30, Fill = Gradient("#FFF1E6", "#C98B3A"), Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new CompositeTransform() };
        Place(heart, 30); Flower.Children.Add(heart);
        Anim(sb, heart, "Opacity", 0, 1, 1.25, 0.3);
        Anim(sb, heart.RenderTransform, "ScaleX", 0, 1, 1.25, 0.5, Spring()); Anim(sb, heart.RenderTransform, "ScaleY", 0, 1, 1.25, 0.5, Spring());
    }

    // Small gold and rose sparks that drift up around the flower (fixed seed: the same scene every time).
    private void BuildSparks(Storyboard sb)
    {
        var random = new Random(7);
        for (var i = 0; i < 22; i++)
        {
            var size = 2.5 + random.NextDouble() * 3.5;
            var spark = new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(i % 3 == 0 ? Windows.UI.Color.FromArgb(255, 240, 122, 156) : Windows.UI.Color.FromArgb(255, 245, 215, 196)), Opacity = 0 };
            var shift = new TranslateTransform(); spark.RenderTransform = shift;
            var x = (random.NextDouble() - 0.5) * 760; var y = (random.NextDouble() - 0.3) * 420;
            Canvas.SetLeft(spark, x); Canvas.SetTop(spark, y);
            Sparks.Children.Add(spark);
            var at = 0.4 + random.NextDouble() * 2.2; var life = 1.6 + random.NextDouble() * 1.4;
            Anim(sb, shift, "Y", 0, -60 - random.NextDouble() * 70, at, life, Soft());
            // One animation per property: fade in, hold, fade out.
            var glow = new DoubleAnimationUsingKeyFrames { BeginTime = Time(at / speed) };
            foreach (var (t, v) in new[] { (0.0, 0.0), (life * 0.3, 0.9), (life * 0.55, 0.9), (life, 0.0) })
                glow.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(Time(t / speed)), Value = v });
            Storyboard.SetTarget(glow, spark); Storyboard.SetTargetProperty(glow, "Opacity"); sb.Children.Add(glow);
        }
    }

    private static Microsoft.UI.Xaml.Shapes.Path Petal(double length, double width, string from, string to)
    {
        var figure = new PathFigure { StartPoint = new Point(0, 0), IsClosed = true };
        figure.Segments.Add(new BezierSegment { Point1 = new Point(width, -length * 0.35), Point2 = new Point(width * 0.6, -length * 0.85), Point3 = new Point(0, -length) });
        figure.Segments.Add(new BezierSegment { Point1 = new Point(-width * 0.6, -length * 0.85), Point2 = new Point(-width, -length * 0.35), Point3 = new Point(0, 0) });
        var geometry = new PathGeometry(); geometry.Figures.Add(figure);
        return new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = geometry,
            Fill = new LinearGradientBrush { StartPoint = new Point(0.5, 1), EndPoint = new Point(0.5, 0), GradientStops = { new GradientStop { Color = Hex(from), Offset = 0 }, new GradientStop { Color = Hex(to), Offset = 1 } } },
            Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(70, 255, 241, 230)), StrokeThickness = 0.8,
            RenderTransform = new CompositeTransform(), // rotates and scales around the petal's base (0,0), the flower's center
        };
    }

    private static void Place(FrameworkElement element, double size) { Canvas.SetLeft(element, -size / 2); Canvas.SetTop(element, -size / 2); }

    private void OnSkip(object sender, TappedRoutedEventArgs e) { e.Handled = true; Close(); }

    private void Close()
    {
        if (closing || Visibility == Visibility.Collapsed) return;
        closing = true; limit?.Stop();
        var fade = new Storyboard();
        Add(fade, this, "Opacity", Opacity, 0, 0, 0.4, Soft());
        Add(fade, FlowerTransform, "ScaleX", 1, 1.08, 0, 0.4, Soft()); Add(fade, FlowerTransform, "ScaleY", 1, 1.08, 0, 0.4, Soft());
        fade.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            story?.Stop(); story = null;
            Flower.Children.Clear(); Sparks.Children.Clear();
            Finished?.Invoke(this, EventArgs.Empty);
        };
        fade.Begin();
    }

    // Scene times are written for the full intro; the short one divides them by its speed.
    private void Anim(Storyboard sb, DependencyObject target, string property, double from, double to, double at, double duration, EasingFunctionBase? ease = null)
        => Add(sb, target, property, from, to, at / speed, duration / speed, ease);
    private static void Add(Storyboard sb, DependencyObject target, string property, double from, double to, double at, double duration, EasingFunctionBase? ease = null)
    {
        var a = new DoubleAnimation { From = from, To = to, BeginTime = Time(at), Duration = new Duration(Time(duration)), EasingFunction = ease ?? new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(a, target); Storyboard.SetTargetProperty(a, property); sb.Children.Add(a);
    }
    private static TimeSpan Time(double seconds) => TimeSpan.FromSeconds(seconds);
    private static EasingFunctionBase Spring() => new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
    private static EasingFunctionBase Soft() => new SineEase { EasingMode = EasingMode.EaseInOut };
    private static LinearGradientBrush Gradient(string from, string to) => new()
    {
        StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
        GradientStops = { new GradientStop { Color = Hex(from), Offset = 0 }, new GradientStop { Color = Hex(to), Offset = 1 } },
    };
    private static Windows.UI.Color Hex(string hex)
    {
        hex = hex.TrimStart('#');
        return Windows.UI.Color.FromArgb(255, Convert.ToByte(hex[..2], 16), Convert.ToByte(hex.Substring(2, 2), 16), Convert.ToByte(hex.Substring(4, 2), 16));
    }
}
