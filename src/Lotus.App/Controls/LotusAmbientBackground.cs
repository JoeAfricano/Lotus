using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Lotus.App.Controls;

// A single window-scoped layer. Two gradient quads and fixed vector contours;
// no mask, blur, bitmap/video, frame timer, telemetry subscription or page instance.
public sealed class LotusAmbientBackground : FrameworkElement, IDisposable
{
    private readonly List<CompositionObject> owned = [];
    private readonly List<CompositionRadialGradientBrush> auroras = [];
    private readonly List<Vector2> centers = [];
    private ContainerVisual? root, flowerPlacement, flowerMotion;
    private ShapeVisual? petals;
    private bool windowActive, windowVisible, reduceMotion, running, failed, released;
    private string? lastPolicy;
    private readonly bool proof = Environment.GetCommandLineArgs().Contains("--ambient-motion-proof");
    private readonly bool disabled = Environment.GetCommandLineArgs().Contains("--ambient-motion-disabled");

    public LotusAmbientBackground()
    {
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }
    public void SetWindowState(bool active, bool visible, bool reduced)
    {
        windowActive = active; windowVisible = visible; reduceMotion = reduced;
        Refresh();
    }
    private void OnLoaded(object sender, RoutedEventArgs args) { released = false; failed = false; Refresh(); }
    private void OnUnloaded(object sender, RoutedEventArgs args) => Dispose();
    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (root is null) Refresh(); else Resize();
    }
    private T Own<T>(T value) where T : CompositionObject { owned.Add(value); return value; }
    private void Refresh()
    {
        if (released || !IsLoaded || ActualWidth < 2 || ActualHeight < 2 || failed) return;
        try
        {
            if (root is null) Create();
            var highContrast = new AccessibilitySettings().HighContrast;
            var windowsMotion = new UISettings().AnimationsEnabled;
            root!.Opacity = highContrast ? 0 : 1;
            var animate = windowActive && windowVisible && !reduceMotion && !highContrast && !disabled && (windowsMotion || proof);
            if (animate != running) { if (animate) Start(); else Stop(); }
            var policy = $"{animate}/{windowActive}/{windowVisible}/{reduceMotion}/{windowsMotion}/{proof}/{disabled}/{highContrast}";
            if (policy != lastPolicy)
            {
                lastPolicy = policy;
                Serilog.Log.Information("Ambient motion {Running} Active {Active} Visible {Visible} ReduceMotion {ReduceMotion} WindowsAnimations {WindowsAnimations} Proof {Proof} Disabled {Disabled} HighContrast {HighContrast}",
                    animate, windowActive, windowVisible, reduceMotion, windowsMotion, proof, disabled, highContrast);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.COMException)
        {
            Dispose(); failed = true;
            Serilog.Log.Warning(ex, "Ambient composition unavailable; keeping plain background");
        }
    }
    private void Create()
    {
        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        root = compositor.CreateContainerVisual();
        root.Clip = Own(compositor.CreateInsetClip());
        AddAurora(compositor, new(0.93f, 0.08f), new(0.67f, 0.56f), 0.12f, Color.FromArgb(255, 194, 24, 91));
        AddAurora(compositor, new(0.06f, 0.92f), new(0.59f, 0.66f), 0.10f, Color.FromArgb(255, 232, 55, 138));
        flowerPlacement = Own(compositor.CreateContainerVisual());
        flowerMotion = Own(compositor.CreateContainerVisual()); flowerMotion.Size = new(512); flowerMotion.CenterPoint = new(256, 256, 0); flowerMotion.Opacity = 0.065f;
        petals = Own(compositor.CreateShapeVisual()); petals.Size = new(512);
        var ink = Own(compositor.CreateColorBrush(Color.FromArgb(255, 238, 193, 168)));
        Vector2[][] outlines = [
            [new(16,1),new(10,8),new(11,18),new(16,27),new(21,18),new(22,8)],
            [new(4,7),new(10,11),new(13,18),new(16,27),new(8,21),new(4,15)],
            [new(28,7),new(22,11),new(19,18),new(16,27),new(24,21),new(28,15)],
            [new(0,14),new(7,15),new(11,21),new(16,27),new(8,27),new(3,23)],
            [new(32,14),new(25,15),new(21,21),new(16,27),new(24,27),new(29,23)]
        ];
        foreach (var outline in outlines)
        {
            for (var index = 0; index < outline.Length; index++)
            {
                var from = new Vector2(16, 40) + outline[index] * 15;
                var to = new Vector2(16, 40) + outline[(index + 1) % outline.Length] * 15;
                var delta = to - from;
                var rectangle = Own(compositor.CreateRectangleGeometry()); rectangle.Size = new(delta.Length(), 1.8f);
                var line = Own(compositor.CreateSpriteShape(rectangle)); line.FillBrush = ink;
                line.TransformMatrix = Matrix3x2.CreateRotation(MathF.Atan2(delta.Y, delta.X)) * Matrix3x2.CreateTranslation(from);
                petals.Shapes.Add(line);
            }
        }
        flowerMotion.Children.InsertAtTop(petals); flowerPlacement.Children.InsertAtTop(flowerMotion); root.Children.InsertAtTop(flowerPlacement);
        Resize(); ElementCompositionPreview.SetElementChildVisual(this, root);
        Serilog.Log.Information("Ambient resources created {Objects} {Auroras} {Contours} Viewport {Width}x{Height}", owned.Count + 1, auroras.Count, petals.Shapes.Count, ActualWidth, ActualHeight);
    }
    private void AddAurora(Compositor compositor, Vector2 center, Vector2 radius, float opacity, Color color)
    {
        var brush = Own(compositor.CreateRadialGradientBrush()); brush.MappingMode = CompositionMappingMode.Relative;
        brush.EllipseCenter = center; brush.EllipseRadius = radius;
        brush.ColorStops.Add(Own(compositor.CreateColorGradientStop(0, color)));
        brush.ColorStops.Add(Own(compositor.CreateColorGradientStop(0.55f, Color.FromArgb(220, 120, 14, 58))));
        brush.ColorStops.Add(Own(compositor.CreateColorGradientStop(1, Color.FromArgb(0, 232, 55, 138))));
        var light = Own(compositor.CreateSpriteVisual()); light.Brush = brush; light.Opacity = opacity;
        light.RelativeSizeAdjustment = Vector2.One;
        root!.Children.InsertAtTop(light); auroras.Add(brush); centers.Add(center);
    }
    private void Resize()
    {
        if (root is null) return;
        root.Size = new((float)ActualWidth, (float)ActualHeight);
        if (flowerPlacement is null) return;
        var size = Math.Clamp((float)ActualWidth * 0.5f, 350, 550);
        flowerPlacement.Size = new(512); flowerPlacement.Scale = new(size / 512, size / 512, 1);
        flowerPlacement.Offset = new((float)ActualWidth * 0.76f - size / 2, (float)ActualHeight * 0.57f - size / 2, 0);
    }
    private void Start()
    {
        var compositor = root!.Compositor;
        using var ease = compositor.CreateCubicBezierEasingFunction(new(0.42f, 0), new(0.58f, 1));
        for (var index = 0; index < auroras.Count; index++)
        {
            using var drift = compositor.CreateVector2KeyFrameAnimation();
            var center = centers[index]; var shift = index == 0 ? new Vector2(-0.14f, 0.12f) : new Vector2(0.12f, -0.10f);
            drift.InsertKeyFrame(0, center); drift.InsertKeyFrame(0.5f, center + shift, ease); drift.InsertKeyFrame(1, center, ease);
            drift.Duration = TimeSpan.FromSeconds(index == 0 ? 18 : 23); drift.IterationBehavior = AnimationIterationBehavior.Forever;
            auroras[index].StartAnimation("EllipseCenter", drift);
        }
        using var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0, Vector3.One); scale.InsertKeyFrame(0.5f, new(1.02f, 1.02f, 1), ease); scale.InsertKeyFrame(1, Vector3.One, ease);
        scale.Duration = TimeSpan.FromSeconds(14); scale.IterationBehavior = AnimationIterationBehavior.Forever;
        flowerMotion!.StartAnimation("Scale", scale);
        using var light = compositor.CreateScalarKeyFrameAnimation();
        light.InsertKeyFrame(0, 0.05f); light.InsertKeyFrame(0.5f, 0.07f, ease); light.InsertKeyFrame(1, 0.05f, ease);
        light.Duration = scale.Duration; light.IterationBehavior = AnimationIterationBehavior.Forever;
        flowerMotion.StartAnimation("Opacity", light); running = true;
    }
    private void Stop()
    {
        for (var index = 0; index < auroras.Count; index++) { auroras[index].StopAnimation("EllipseCenter"); auroras[index].EllipseCenter = centers[index]; }
        if (flowerMotion is not null)
        {
            flowerMotion.StopAnimation("Scale"); flowerMotion.StopAnimation("Opacity");
            flowerMotion.Scale = Vector3.One; flowerMotion.Opacity = 0.065f;
        }
        running = false;
    }
    public void Dispose()
    {
        released = true;
        if (root is null) return;
        Stop(); ElementCompositionPreview.SetElementChildVisual(this, null);
        petals?.Shapes.Clear(); flowerMotion?.Children.RemoveAll(); flowerPlacement?.Children.RemoveAll(); root.Children.RemoveAll(); root.Clip = null;
        for (var index = owned.Count - 1; index >= 0; index--) owned[index].Dispose();
        Serilog.Log.Information("Ambient resources disposed {Objects}", owned.Count + 1);
        owned.Clear(); auroras.Clear(); centers.Clear(); root.Dispose(); root = null; petals = null; flowerPlacement = flowerMotion = null; lastPolicy = null;
    }
}
