using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;

namespace Lotus.App;

// Small vector surfaces, fixed particle count and compositor-only animation.
// No bitmap frames, UI timer, external font or per-frame allocations.
internal sealed class BrandPulse : IDisposable
{
    private readonly FrameworkElement element;
    private readonly Brush? originalForeground;
    private readonly double originalChildOpacity;
    private readonly ContainerVisual root;
    private readonly ShapeVisual artwork;
    private readonly CompositionVisualSurface surface;
    private readonly CompositionSurfaceBrush surfaceBrush;
    private readonly SpriteVisual image;
    private readonly DropShadow glow;
    private readonly List<CompositionObject> owned = [];
    private readonly List<CompositionColorBrush> inks = [];
    private readonly List<CompositionRectangleGeometry> rectangles = [];
    private readonly List<CompositionSpriteShape> shapes = [];

    private static readonly string[][] Letters =
    [
        ["110000000","110000000","110000000","110000000","110000000","110000000","110000000","110000000","111111111","011111111"],
        ["001111100","011111110","111000111","110000011","110000011","110000011","110000011","111000111","011111110","001111100"],
        ["111111111","111111111","000111000","000111000","000111000","000111000","000111000","000111000","000111000","000111000"],
        ["110000011","110000011","110000011","110000011","110000011","110000011","110000011","111000111","011111110","001111100"],
        ["001111111","011111111","111000000","111000000","011111100","001111110","000000111","000000111","111111110","111111100"]
    ];
    private static readonly Vector2[][] Petals =
    [
        [new(16,1),new(10,8),new(11,18),new(16,27),new(21,18),new(22,8)],
        [new(4,7),new(10,11),new(13,18),new(16,27),new(8,21),new(4,15)],
        [new(28,7),new(22,11),new(19,18),new(16,27),new(24,21),new(28,15)],
        [new(0,14),new(7,15),new(11,21),new(16,27),new(8,27),new(3,23)],
        [new(32,14),new(25,15),new(21,21),new(16,27),new(24,27),new(29,23)]
    ];
    private static bool Inside(Vector2 point, Vector2[] polygon)
    {
        var inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            var a = polygon[i]; var b = polygon[j];
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }
    private static string[] Pattern(bool flower)
    {
        if (!flower) return Enumerable.Range(0, 10).Select(row => string.Join("00", Letters.Select(letter => letter[row]))).ToArray();
        Vector2[] hollow = [new(16,6),new(13,10),new(14,18),new(16,23),new(18,18),new(19,10)];
        return Enumerable.Range(0, 29).Select(y => new string(Enumerable.Range(0, 33).Select(x =>
        {
            var point = new Vector2(x + 0.5f, y + 0.5f);
            return Petals.Any(p => Inside(point, p)) && !Inside(point, hollow) ? '1' : '0';
        }).ToArray())).ToArray();
    }
    public BrandPulse(FrameworkElement element, bool animate, bool flower = false)
    {
        this.element = element;
        var compositor = ElementCompositionPreview.GetElementVisual(element).Compositor;
        var size = new Vector2((float)element.ActualWidth, (float)element.ActualHeight);
        if (element is TextBlock text) { originalForeground = text.Foreground; text.Foreground = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)); }
        if (element is Viewbox { Child: { } child }) { originalChildOpacity = child.Opacity; child.Opacity = 0; }
        root = compositor.CreateContainerVisual(); root.Size = size;
        artwork = compositor.CreateShapeVisual(); artwork.Size = size;
        var pattern = Pattern(flower);
        var cell = Math.Min((size.X - 2) / pattern[0].Length, (size.Y - 2) / pattern.Length);
        var origin = (size - new Vector2(pattern[0].Length, pattern.Length) * cell) / 2;
        var edge = compositor.CreateColorBrush(Color.FromArgb(170, 255, 207, 233)); owned.Add(edge);
        // Adjacent cells in the same row share one rectangle, reducing geometry count.
        for (var y = 0; y < pattern.Length; y++)
        {
            var fraction = y / (float)(pattern.Length - 1);
            var color = fraction < 0.22f ? Color.FromArgb(255,255,175,216)
                : fraction > 0.84f ? Color.FromArgb(255,255,103,180) : Color.FromArgb(255,232,25,126);
            var ink = compositor.CreateColorBrush(color); inks.Add(ink);
            if (animate)
            {
                using var energy = compositor.CreateColorKeyFrameAnimation();
                energy.InsertKeyFrame(0, color); energy.InsertKeyFrame(0.3f, Color.FromArgb(255,255,226,242));
                energy.InsertKeyFrame(0.58f, Color.FromArgb(140,194,14,102)); energy.InsertKeyFrame(1, color);
                energy.Duration = TimeSpan.FromSeconds(6); energy.DelayTime = TimeSpan.FromMilliseconds(y * 35);
                energy.IterationBehavior = AnimationIterationBehavior.Forever;
                ink.StartAnimation("Color", energy);
            }
            for (var x = 0; x < pattern[y].Length; x++)
            {
                if (pattern[y][x] != '1') continue;
                var start = x; while (x + 1 < pattern[y].Length && pattern[y][x + 1] == '1') x++;
                var rectangle = compositor.CreateRectangleGeometry();
                rectangle.Offset = origin + new Vector2(start * cell, y * cell);
                rectangle.Size = new Vector2((x - start + 1) * cell, cell);
                rectangles.Add(rectangle);
                var shape = compositor.CreateSpriteShape(rectangle);
                shape.FillBrush = ink; shape.StrokeBrush = edge; shape.StrokeThickness = Math.Max(0.08f, cell * 0.12f);
                shapes.Add(shape); artwork.Shapes.Add(shape);
            }
        }
        // The surface is only the size of the displayed mark, never a large reference image.
        surface = compositor.CreateVisualSurface(); surface.SourceVisual = artwork; surface.SourceSize = size;
        surfaceBrush = compositor.CreateSurfaceBrush(surface);
        glow = compositor.CreateDropShadow(); glow.Mask = surfaceBrush;
        glow.Color = Color.FromArgb(255,255,20,123); glow.BlurRadius = flower ? 8 : 10; glow.Opacity = 0.65f;
        image = compositor.CreateSpriteVisual(); image.Size = size; image.Brush = surfaceBrush; image.Shadow = glow;
        root.Children.InsertAtBottom(image);
        ElementCompositionPreview.SetElementChildVisual(element, root);
        if (!animate) return;
        using var breathing = compositor.CreateScalarKeyFrameAnimation();
        breathing.InsertKeyFrame(0, 0.95f); breathing.InsertKeyFrame(0.35f, 1);
        breathing.InsertKeyFrame(0.64f, 0.52f); breathing.InsertKeyFrame(1, 0.95f);
        breathing.Duration = TimeSpan.FromSeconds(6); breathing.IterationBehavior = AnimationIterationBehavior.Forever;
        image.StartAnimation("Opacity", breathing);
        using var bloom = compositor.CreateScalarKeyFrameAnimation();
        bloom.InsertKeyFrame(0, 0.4f); bloom.InsertKeyFrame(0.35f, 0.95f); bloom.InsertKeyFrame(0.68f, 0.25f); bloom.InsertKeyFrame(1, 0.4f);
        bloom.Duration = breathing.Duration; bloom.IterationBehavior = AnimationIterationBehavior.Forever;
        glow.StartAnimation("Opacity", bloom);
        for (var i = 0; i < 12; i++)
        {
            var pixel = compositor.CreateSpriteVisual(); owned.Add(pixel);
            var sparkBrush = compositor.CreateColorBrush(i % 4 == 0 ? Color.FromArgb(255,255,235,246) : Color.FromArgb(255,255,42,139)); owned.Add(sparkBrush);
            pixel.Brush = sparkBrush; pixel.Size = new Vector2(Math.Clamp(cell * 0.65f, 1, 2.5f));
            var position = new Vector3(size.X * (i + 0.5f) / 12, size.Y * (i % 3 + 1) / 4, 0);
            pixel.Offset = position; pixel.Opacity = 0;
            using var opacity = compositor.CreateScalarKeyFrameAnimation();
            opacity.InsertKeyFrame(0, 0); opacity.InsertKeyFrame(0.25f, 0);
            opacity.InsertKeyFrame(0.42f, 0.9f); opacity.InsertKeyFrame(0.75f, 0); opacity.InsertKeyFrame(1, 0);
            opacity.Duration = TimeSpan.FromSeconds(6); opacity.DelayTime = TimeSpan.FromMilliseconds(i * 95);
            opacity.IterationBehavior = AnimationIterationBehavior.Forever;
            pixel.StartAnimation("Opacity", opacity);
            using var drift = compositor.CreateVector3KeyFrameAnimation();
            drift.InsertKeyFrame(0, position); drift.InsertKeyFrame(0.75f, position + new Vector3(i % 2 == 0 ? -4 : 4, -5, 0));
            drift.InsertKeyFrame(1, position);
            drift.Duration = opacity.Duration; drift.DelayTime = opacity.DelayTime; drift.IterationBehavior = AnimationIterationBehavior.Forever;
            pixel.StartAnimation("Offset", drift); root.Children.InsertAtTop(pixel);
        }
    }
    public static BrandPulse? TryCreate(FrameworkElement element, bool animate, bool flower = false)
    {
        if (!element.IsLoaded || element.ActualWidth <= 2 || element.ActualHeight <= 2 || element.Visibility != Visibility.Visible) return null;
        var foreground = (element as TextBlock)?.Foreground;
        var childOpacity = (element as Viewbox)?.Child?.Opacity;
        try { return new(element, animate, flower); }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.COMException)
        {
            ElementCompositionPreview.SetElementChildVisual(element, null);
            if (element is TextBlock text) text.Foreground = foreground;
            if (element is Viewbox { Child: { } child } && childOpacity is { } opacity) child.Opacity = opacity;
            Serilog.Log.Warning(ex, "Pixel neon composition unavailable for {Element}", element.Name);
            return null;
        }
    }
    public void Dispose()
    {
        ElementCompositionPreview.SetElementChildVisual(element, null);
        image.StopAnimation("Opacity"); glow.StopAnimation("Opacity");
        root.Children.RemoveAll(); artwork.Shapes.Clear(); image.Brush = null; image.Shadow = null;
        glow.Mask = null; surface.SourceVisual = null;
        foreach (var ink in inks) { ink.StopAnimation("Color"); ink.Dispose(); }
        foreach (var item in owned)
        {
            if (item is SpriteVisual sprite) { sprite.StopAnimation("Opacity"); sprite.StopAnimation("Offset"); sprite.Brush = null; }
            item.Dispose();
        }
        foreach (var shape in shapes) shape.Dispose();
        foreach (var rectangle in rectangles) rectangle.Dispose();
        image.Dispose(); glow.Dispose(); surfaceBrush.Dispose(); surface.Dispose(); artwork.Dispose(); root.Dispose();
        if (element is TextBlock text) text.Foreground = originalForeground;
        if (element is Viewbox { Child: { } child }) child.Opacity = originalChildOpacity;
    }
}
