using Lotus.Core.Themes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace Lotus.App.Controls;

// The animated background of custom themes: a slow gradient of 2–5 colors, or a Lotus scene (petals, aurora,
// particles, neon waves) in the theme's colors, with optional film grain. Only transform/opacity animations, which run
// on the composition thread; everything pauses while the window is hidden or inactive, and stays still with
// "reduce motion". It sits behind the pages, so it shows between cards and through translucent ones.
public sealed class ThemeBackdrop : Grid
{
    private readonly Canvas scene = new() { IsHitTestVisible = false }; // a Canvas never clips its children (a Grid would cut big shapes before they move)
    private readonly Canvas grain = new() { IsHitTestVisible = false };
    private readonly List<Storyboard> boards = [];
    private readonly Random random = new(7);
    private BackdropSpec? spec;
    private bool light, running, reduced;
    private Size built;
    private DispatcherTimer? resize;
    private static WriteableBitmap? noise;

    public ThemeBackdrop()
    {
        IsHitTestVisible = false;
        Children.Add(scene); Children.Add(grain);
        SizeChanged += (_, e) =>
        {
            if (spec is null) return;
            resize ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            resize.Stop(); resize.Tick -= OnResize; resize.Tick += OnResize; resize.Start();
        };
        Unloaded += (_, _) => Clear();
    }
    private void OnResize(object? sender, object e) { resize?.Stop(); if (Math.Abs(ActualWidth - built.Width) > 40 || Math.Abs(ActualHeight - built.Height) > 40) Build(); }

    // null = no animated background (built-in modes, "none" or the Lotus flower, which has its own control).
    public void Apply(BackdropSpec? backdrop, bool lightTheme)
    {
        if (Same(backdrop, spec) && lightTheme == light && (spec is null || scene.Children.Count > 0)) return;
        spec = backdrop; light = lightTheme;
        Build();
    }

    // By value: preferences are read again on any change (a new list each time), which must not restart the animation.
    private static bool Same(BackdropSpec? a, BackdropSpec? b) =>
        ReferenceEquals(a, b) || (a is not null && b is not null && a.Kind == b.Kind && a.Speed == b.Speed && a.Intensity == b.Intensity && a.Grain == b.Grain && a.Colors.SequenceEqual(b.Colors));

    public void SetState(bool run, bool reduceMotion)
    {
        var wasReduced = reduced;
        running = run; reduced = reduceMotion;
        if (wasReduced != reduced) { Build(); return; }
        foreach (var b in boards) { if (running && !reduced) b.Resume(); else b.Pause(); }
    }

    private void Clear()
    {
        foreach (var b in boards) b.Stop();
        boards.Clear(); offsets.Clear(); reverseOf.Clear(); scene.Children.Clear(); scene.Background = null; grain.Children.Clear();
    }

    private void Build()
    {
        Clear();
        if (spec is null || spec.Kind is "none" or "lotus") { Visibility = Visibility.Collapsed; return; }
        Visibility = Visibility.Visible;
        if (ActualWidth < 10 || ActualHeight < 10) { built = default; return; } // not laid out yet: SizeChanged builds it
        built = new(ActualWidth, ActualHeight);
        var colors = spec.Colors.Select(h => ThemeColor.TryParse(h, out var c) ? Color.FromArgb(255, c.R, c.G, c.B) : Color.FromArgb(255, 128, 128, 128)).ToList();
        // Light themes: softer, so text keeps its contrast (the engine checks against the brightest color too).
        Opacity = spec.Intensity * (light ? 0.75 : 1);
        var speed = Math.Clamp(spec.Speed, 0.25, 2);
        switch (spec.Kind)
        {
            case "gradient": Gradient(colors, speed); break;
            case "aurora": Aurora(colors, speed); break;
            case "particles": Particles(colors, speed); break;
            case "petals": Petals(colors, speed); break;
            case "waves": Waves(colors, speed); break;
        }
        if (spec.Grain) Grain();
        foreach (var b in boards) b.Begin();
        foreach (var (b, at) in offsets) b.Seek(at);
        if (!running || reduced) foreach (var b in boards) b.Pause();
    }

    private double W => built.Width; private double H => built.Height;
    private TimeSpan Dur(double seconds, double speed) => TimeSpan.FromSeconds(seconds / speed);
    private double Rand(double min, double max) => min + random.NextDouble() * (max - min);
    private static Color Alpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    // Soft color blobs drifting slowly (the "Discord / Arc" gradient).
    private void Gradient(List<Color> colors, double speed)
    {
        scene.Background = new LinearGradientBrush(new GradientStopCollection
        {
            new() { Color = Alpha(colors[0], 0x55), Offset = 0 }, new() { Color = Alpha(colors[^1], 0x55), Offset = 1 },
        }, 35);
        var size = Math.Max(W, H) * 0.95;
        var spots = new (double X, double Y)[] { (-0.25, -0.3), (0.55, -0.25), (0.2, 0.45), (-0.2, 0.55), (0.6, 0.5) };
        for (var i = 0; i < Math.Max(3, colors.Count); i++)
        {
            var c = colors[i % colors.Count];
            var blob = new Ellipse
            {
                Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Fill = new RadialGradientBrush { GradientStops = { new() { Color = Alpha(c, 0xC0), Offset = 0 }, new() { Color = Alpha(c, 0x50), Offset = 0.45 }, new() { Color = Alpha(c, 0), Offset = 1 } } },
            };
            var move = new CompositeTransform { TranslateX = spots[i % spots.Length].X * W, TranslateY = spots[i % spots.Length].Y * H, CenterX = size / 2, CenterY = size / 2 };
            blob.RenderTransform = move;
            scene.Children.Add(blob);
            var sb = Loop(true);
            Animate(sb, move, "TranslateX", move.TranslateX, move.TranslateX + Rand(-0.22, 0.22) * W, Dur(Rand(16, 26), speed));
            Animate(sb, move, "TranslateY", move.TranslateY, move.TranslateY + Rand(-0.2, 0.2) * H, Dur(Rand(18, 30), speed));
            Animate(sb, move, "ScaleX", 1, Rand(1.05, 1.25), Dur(Rand(14, 22), speed)); Animate(sb, move, "ScaleY", 1, Rand(1.05, 1.25), Dur(Rand(14, 22), speed));
        }
    }

    // Curtains of light: wide translucent bands that sway and breathe.
    private void Aurora(List<Color> colors, double speed)
    {
        for (var i = 0; i < 4; i++)
        {
            var c = colors[i % colors.Count];
            // An elongated ellipse with a radial gradient: soft on every edge (a rectangle would show a hard cut).
            var band = new Ellipse
            {
                Width = W * 1.5, Height = H * Rand(0.35, 0.5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Fill = new RadialGradientBrush { GradientStops = { new() { Color = Alpha(c, 0xB8), Offset = 0 }, new() { Color = Alpha(c, 0x60), Offset = 0.45 }, new() { Color = Alpha(c, 0), Offset = 1 } } },
            };
            var t = new CompositeTransform { TranslateX = -W * 0.3, TranslateY = H * (-0.05 + i * 0.17), Rotation = Rand(-14, 14), CenterX = W * 0.75, CenterY = band.Height / 2 };
            band.RenderTransform = t;
            scene.Children.Add(band);
            var sb = Loop(true);
            Animate(sb, t, "TranslateX", -W * 0.4, -W * 0.05, Dur(Rand(20, 32), speed));
            Animate(sb, t, "Rotation", t.Rotation, t.Rotation + Rand(-10, 10), Dur(Rand(14, 22), speed));
            Animate(sb, t, "ScaleY", 0.7, 1.35, Dur(Rand(9, 15), speed));
        }
    }

    // Tiny lights rising and twinkling.
    private void Particles(List<Color> colors, double speed)
    {
        var count = (int)Math.Clamp(W * H / 26000, 24, 70);
        for (var i = 0; i < count; i++)
        {
            var c = colors[i % colors.Count]; var size = Rand(2, 6.5);
            var dot = new Ellipse
            {
                Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Fill = new RadialGradientBrush { GradientStops = { new() { Color = Alpha(c, 0xFF), Offset = 0 }, new() { Color = Alpha(c, 0), Offset = 1 } } },
            };
            var x = Rand(0, W);
            var t = new CompositeTransform { TranslateX = x, TranslateY = H + 10 };
            dot.RenderTransform = t; scene.Children.Add(dot);
            var rise = Dur(Rand(14, 30), speed);
            var sb = Loop(false);
            Animate(sb, t, "TranslateY", H + 10, -20, rise);
            var drift = Loop(true);
            Animate(drift, t, "TranslateX", x - Rand(6, 22), x + Rand(6, 22), Dur(Rand(4, 8), speed));
            Animate(drift, dot, "Opacity", 0.25, 1, Dur(Rand(1.5, 4), speed));
            Offset(sb, rise);
        }
    }

    // Petals falling and turning (a Lotus touch).
    private void Petals(List<Color> colors, double speed)
    {
        var count = (int)Math.Clamp(W * H / 55000, 14, 34);
        for (var i = 0; i < count; i++)
        {
            var c = colors[i % colors.Count]; var size = Rand(10, 22);
            var petal = new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = PetalGeometry(size), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Fill = new LinearGradientBrush(new GradientStopCollection { new() { Color = Alpha(c, 0xF0), Offset = 0 }, new() { Color = Alpha(c, 0x70), Offset = 1 } }, 60),
            };
            var x = Rand(-0.1, 0.95) * W;
            var t = new CompositeTransform { TranslateX = x, TranslateY = -40, CenterX = size / 2, CenterY = size / 2 };
            petal.RenderTransform = t; scene.Children.Add(petal);
            var fall = Dur(Rand(16, 30), speed);
            var sb = Loop(false);
            Animate(sb, t, "TranslateY", -40, H + 40, fall);
            Animate(sb, t, "TranslateX", x, x + Rand(0.08, 0.25) * W, fall);
            Animate(sb, t, "Rotation", Rand(0, 90), Rand(300, 620), fall);
            var sway = Loop(true);
            Animate(sway, t, "ScaleX", 1, Rand(0.35, 0.7), Dur(Rand(1.6, 3.2), speed)); // the petal turning over
            Offset(sb, fall);
        }
    }
    private static Geometry PetalGeometry(double s)
    {
        var fig = new PathFigure { StartPoint = new(s / 2, 0), IsClosed = true };
        fig.Segments.Add(new BezierSegment { Point1 = new(s * 1.05, s * 0.25), Point2 = new(s * 0.9, s * 0.85), Point3 = new(s / 2, s) });
        fig.Segments.Add(new BezierSegment { Point1 = new(s * 0.1, s * 0.85), Point2 = new(-s * 0.05, s * 0.25), Point3 = new(s / 2, 0) });
        var g = new PathGeometry(); g.Figures.Add(fig); return g;
    }

    // Neon sine waves gliding sideways (seamless: they move exactly one wavelength per loop).
    private void Waves(List<Color> colors, double speed)
    {
        for (var i = 0; i < 3; i++)
        {
            var c = colors[i % colors.Count];
            var wave = Math.Max(240, W / Rand(1.6, 2.6)); var amp = Rand(18, 46); var y = H * (0.5 + i * 0.13);
            foreach (var (thickness, alpha) in new[] { (12.0, (byte)0x26), (2.2, (byte)0xE6) }) // soft glow under a sharp line
            {
                var fig = new PathFigure { StartPoint = new(0, y) };
                var line = new PolyLineSegment();
                for (double x = 0; x <= W + wave * 2; x += 6) line.Points.Add(new(x, y + Math.Sin(x / wave * Math.PI * 2 + i) * amp));
                fig.Segments.Add(line);
                var geo = new PathGeometry(); geo.Figures.Add(fig);
                var path = new Microsoft.UI.Xaml.Shapes.Path { Data = geo, Stroke = new SolidColorBrush(Alpha(c, alpha)), StrokeThickness = thickness, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                var t = new CompositeTransform();
                path.RenderTransform = t; scene.Children.Add(path);
                var sb = Loop(false);
                Animate(sb, t, "TranslateX", 0, -wave, Dur(Rand(7, 13) * (1 + i * 0.25), speed), linear: true);
            }
        }
        // A faint horizon glow behind the lines.
        scene.Background = new LinearGradientBrush(new GradientStopCollection
        {
            new() { Color = Alpha(colors[0], 0), Offset = 0.35 }, new() { Color = Alpha(colors[^1], 0x40), Offset = 0.75 }, new() { Color = Alpha(colors[0], 0x18), Offset = 1 },
        }, 90);
    }

    // Film grain: one small noise tile, shared by every copy (memory stays tiny at any window size).
    private void Grain()
    {
        if (noise is null)
        {
            const int n = 192;
            noise = new WriteableBitmap(n, n);
            var px = new byte[n * n * 4]; var r = new Random(42);
            for (var i = 0; i < px.Length; i += 4) { var v = (byte)r.Next(256); var a = (byte)r.Next(0, 70); px[i] = px[i + 1] = px[i + 2] = (byte)(v * a / 255); px[i + 3] = a; } // premultiplied
            using (var stream = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsStream(noise.PixelBuffer)) stream.Write(px);
            noise.Invalidate();
        }
        grain.Opacity = light ? 0.35 : 0.5;
        for (double y = 0; y < H; y += 192)
            for (double x = 0; x < W; x += 192)
            {
                var img = new Image { Source = noise, Width = 192, Height = 192, Stretch = Stretch.None };
                Canvas.SetLeft(img, x); Canvas.SetTop(img, y); grain.Children.Add(img);
            }
    }

    // Each animation repeats on its own (different durations never make the scene jump).
    private readonly Dictionary<Storyboard, bool> reverseOf = [];
    private Storyboard Loop(bool reverse)
    {
        var sb = new Storyboard();
        boards.Add(sb); reverseOf[sb] = reverse; return sb;
    }
    private void Animate(Storyboard sb, DependencyObject target, string property, double from, double to, TimeSpan duration, bool linear = false)
    {
        var a = new DoubleAnimation { From = from, To = to, Duration = duration, RepeatBehavior = RepeatBehavior.Forever, AutoReverse = reverseOf.GetValueOrDefault(sb) };
        if (!linear) a.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
        Storyboard.SetTarget(a, target); Storyboard.SetTargetProperty(a, property); sb.Children.Add(a);
    }
    // Falling/rising things start at a random point of their path, so the screen is full from the first frame.
    private readonly List<(Storyboard Board, TimeSpan At)> offsets = [];
    private void Offset(Storyboard sb, TimeSpan duration) => offsets.Add((sb, TimeSpan.FromSeconds(random.NextDouble() * duration.TotalSeconds)));
}
