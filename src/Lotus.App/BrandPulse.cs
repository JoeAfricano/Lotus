using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using Windows.UI;

namespace Lotus.App;

// Brand motion on top of the real vector mark (text or flower path), so it stays crisp at any size and DPI.
// Glow and shimmer use the element's own alpha mask; everything runs on the compositor (no UI timer, no bitmaps).
// - Text: soft neon glow that breathes slowly, plus a light sweep every few seconds with a long pause.
// - Flower: one spring "bloom" when it appears, then a gentle breathing scale and glow.
// With animate = false only the static glow is drawn.
internal sealed class BrandPulse : IDisposable
{
    private static readonly Color Neon = Color.FromArgb(255, 232, 55, 138);
    private readonly UIElement host;
    private readonly Visual? scaled;
    private readonly ContainerVisual root;
    private readonly DropShadow glow;
    private readonly List<CompositionObject> owned = [];
    private readonly List<(CompositionObject Target, string Property)> running = [];

    public static bool LightTheme { get; set; } // set by the window when the light palette is on
    private BrandPulse(FrameworkElement element, bool animate, bool flower)
    {
        var compositor = ElementCompositionPreview.GetElementVisual(element).Compositor;
        CompositionBrush mask;
        if (flower)
        {
            var path = (element as Viewbox)?.Child is Canvas canvas ? canvas.Children.OfType<Path>().LastOrDefault() : null;
            if (path is null) throw new ArgumentException("Flower mark requires a Viewbox > Canvas > Path.");
            host = path; mask = path.GetAlphaMask();
        }
        else
        {
            if (element is not TextBlock text) throw new ArgumentException("Text mark requires a TextBlock.");
            host = text; mask = text.GetAlphaMask();
        }


        root = Own(compositor.CreateContainerVisual()); root.RelativeSizeAdjustment = Vector2.One;
        // A brushless sprite draws only its shadow: a blurred halo shaped like the mark.
        glow = Own(compositor.CreateDropShadow()); glow.Mask = mask; glow.Color = Neon; glow.Offset = Vector3.Zero;
        glow.BlurRadius = flower ? 9 : 10; glow.Opacity = (flower ? 0.35f : 0.55f) * (LightTheme ? 0.6f : 1f);
        var halo = Own(compositor.CreateSpriteVisual()); halo.RelativeSizeAdjustment = Vector2.One; halo.Shadow = glow;
        root.Children.InsertAtBottom(halo);
        if (!flower)
        {
            // Redraw the text crisp above its halo so the blur never softens the letters.
            var ink = Own(compositor.CreateMaskBrush()); ink.Mask = mask;
            // Rose-gold vertical gradient, matching the Lotus logo lettering.
            var gold = Own(compositor.CreateLinearGradientBrush());
            gold.StartPoint = Vector2.Zero; gold.EndPoint = new Vector2(0, 1);
            // Light theme: a deeper gold so the name keeps its contrast on rosy porcelain; same effects.
            var (g0, g1, g2) = LightTheme ? (Color.FromArgb(255, 214, 150, 84), Color.FromArgb(255, 176, 108, 52), Color.FromArgb(255, 128, 72, 30))
                : (Color.FromArgb(255, 248, 221, 203), Color.FromArgb(255, 231, 174, 147), Color.FromArgb(255, 201, 132, 104));
            gold.ColorStops.Add(Own(compositor.CreateColorGradientStop(0, g0)));
            gold.ColorStops.Add(Own(compositor.CreateColorGradientStop(0.6f, g1)));
            gold.ColorStops.Add(Own(compositor.CreateColorGradientStop(1, g2)));
            ink.Source = gold;
            var crisp = Own(compositor.CreateSpriteVisual()); crisp.RelativeSizeAdjustment = Vector2.One; crisp.Brush = ink;
            root.Children.InsertAtTop(crisp);
        }
        ElementCompositionPreview.SetElementChildVisual(host, root);
        if (!animate) return;

        var ease = Own(compositor.CreateCubicBezierEasingFunction(new Vector2(0.45f, 0), new Vector2(0.55f, 1)));
        var breathe = Own(compositor.CreateScalarKeyFrameAnimation());
        breathe.InsertKeyFrame(0, glow.Opacity * 0.7f); breathe.InsertKeyFrame(0.5f, glow.Opacity * 1.25f, ease); breathe.InsertKeyFrame(1, glow.Opacity * 0.7f, ease);
        breathe.Duration = TimeSpan.FromSeconds(4); breathe.IterationBehavior = AnimationIterationBehavior.Forever;
        Start(glow, "Opacity", breathe);

        if (flower) StartBloom(compositor, element, ease, out scaled);
        else StartShimmer(compositor, (TextBlock)element, mask);
        Serilog.Log.Information("Brand animation started {Element} {Kind}", element.Name, flower ? "Bloom" : "Shimmer");
    }

    private void StartShimmer(Compositor compositor, TextBlock text, CompositionBrush mask)
    {
        var width = (float)Math.Max(text.ActualWidth, 40);
        var light = Own(compositor.CreateLinearGradientBrush());
        light.MappingMode = CompositionMappingMode.Absolute;
        light.StartPoint = Vector2.Zero; light.EndPoint = new Vector2(width * 0.35f, 0);
        light.ColorStops.Add(Own(compositor.CreateColorGradientStop(0, Color.FromArgb(0, 255, 255, 255))));
        light.ColorStops.Add(Own(compositor.CreateColorGradientStop(0.5f, Color.FromArgb(210, 255, 236, 246))));
        light.ColorStops.Add(Own(compositor.CreateColorGradientStop(1, Color.FromArgb(0, 255, 255, 255))));
        var sweep = Own(compositor.CreateMaskBrush()); sweep.Source = light; sweep.Mask = mask;
        var sprite = Own(compositor.CreateSpriteVisual()); sprite.RelativeSizeAdjustment = Vector2.One; sprite.Brush = sweep;
        root.Children.InsertAtTop(sprite);
        // Sweep across in ~1.1 s, then rest; light.Offset moves the gradient band over the letters.
        var move = Own(compositor.CreateVector2KeyFrameAnimation());
        var smooth = Own(compositor.CreateCubicBezierEasingFunction(new Vector2(0.3f, 0), new Vector2(0.2f, 1)));
        move.InsertKeyFrame(0, new Vector2(-width * 0.45f, 0));
        move.InsertKeyFrame(0.16f, new Vector2(width * 1.1f, 0), smooth);
        move.InsertKeyFrame(1, new Vector2(width * 1.1f, 0));
        move.Duration = TimeSpan.FromSeconds(7); move.DelayTime = TimeSpan.FromMilliseconds(900);
        move.IterationBehavior = AnimationIterationBehavior.Forever;
        light.Offset = new Vector2(-width * 0.45f, 0);
        Start(light, "Offset", move);
    }

    private void StartBloom(Compositor compositor, FrameworkElement element, CompositionEasingFunction ease, out Visual visual)
    {
        visual = ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        // One spring bloom on appearance, then a slow breathing loop chained after it.
        var bloom = Own(compositor.CreateSpringVector3Animation());
        bloom.InitialValue = new Vector3(0.82f, 0.82f, 1); bloom.FinalValue = Vector3.One;
        bloom.DampingRatio = 0.45f; bloom.Period = TimeSpan.FromMilliseconds(70);
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        Start(visual, "Scale", bloom);
        batch.End();
        var target = visual;
        batch.Completed += (_, _) =>
        {
            if (disposed) return;
            var loop = Own(compositor.CreateVector3KeyFrameAnimation());
            loop.InsertKeyFrame(0, Vector3.One); loop.InsertKeyFrame(0.5f, new Vector3(1.04f, 1.04f, 1), ease); loop.InsertKeyFrame(1, Vector3.One, ease);
            loop.Duration = TimeSpan.FromSeconds(4); loop.IterationBehavior = AnimationIterationBehavior.Forever;
            Start(target, "Scale", loop);
        };
        owned.Add(batch);
    }

    private bool disposed;
    private T Own<T>(T item) where T : CompositionObject { owned.Add(item); return item; }
    private void Start(CompositionObject target, string property, CompositionAnimation animation)
    {
        target.StartAnimation(property, animation); running.Add((target, property));
    }

    public static BrandPulse? TryCreate(FrameworkElement element, bool animate, bool flower = false)
    {
        if (!element.IsLoaded || element.ActualWidth <= 2 || element.ActualHeight <= 2 || element.Visibility != Visibility.Visible) return null;
        try { return new(element, animate, flower); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            Serilog.Log.Warning(ex, "Brand glow unavailable for {Element}", element.Name);
            return null;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var (target, property) in running) target.StopAnimation(property);
        if (scaled is not null) scaled.Scale = Vector3.One;
        ElementCompositionPreview.SetElementChildVisual(host, null);
        root.Children.RemoveAll(); glow.Mask = null;
        foreach (var item in owned) item.Dispose();
    }
}
