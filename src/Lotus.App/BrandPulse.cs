using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;

namespace Lotus.App;

// A masked neon highlight sweeps through the letters on the compositor, not a UI timer.
internal sealed class BrandPulse : IDisposable
{
    private readonly TextBlock element;
    private readonly SpriteVisual neon;
    private readonly CompositionLinearGradientBrush gradient;
    private readonly Brush originalForeground;
    private readonly NeonBrush foreground;
    private readonly DropShadow glow;
    public BrandPulse(TextBlock element, bool animate)
    {
        this.element = element;
        var compositor = ElementCompositionPreview.GetElementVisual(element).Compositor;
        originalForeground = element.Foreground;
        gradient = compositor.CreateLinearGradientBrush();
        gradient.StartPoint = Vector2.Zero; gradient.EndPoint = Vector2.UnitX;
        gradient.ColorStops.Add(compositor.CreateColorGradientStop(0, Color.FromArgb(255, 255, 53, 144)));
        gradient.ColorStops.Add(compositor.CreateColorGradientStop(0.35f, Color.FromArgb(255, 255, 107, 178)));
        gradient.ColorStops.Add(compositor.CreateColorGradientStop(0.5f, Color.FromArgb(255, 255, 246, 253)));
        gradient.ColorStops.Add(compositor.CreateColorGradientStop(0.65f, Color.FromArgb(255, 255, 107, 178)));
        gradient.ColorStops.Add(compositor.CreateColorGradientStop(1, Color.FromArgb(255, 255, 53, 144)));
        // Let XAML mask Foreground to glyphs. GetAlphaMask can return a nine-grid
        // brush after resize, which CompositionMaskBrush does not support.
        foreground = new NeonBrush(gradient); element.Foreground = foreground;
        glow = compositor.CreateDropShadow(); glow.Mask = element.GetAlphaMask();
        glow.Color = Color.FromArgb(255, 255, 37, 129); glow.BlurRadius = 14; glow.Opacity = 0.8f;
        neon = compositor.CreateSpriteVisual(); neon.RelativeSizeAdjustment = Vector2.One;
        neon.Shadow = glow;
        ElementCompositionPreview.SetElementChildVisual(element, neon);
        if (!animate) return;
        var sweep = compositor.CreateVector2KeyFrameAnimation();
        sweep.InsertKeyFrame(0, new Vector2(-1, 0)); sweep.InsertKeyFrame(1, new Vector2(1, 0));
        sweep.Duration = TimeSpan.FromSeconds(3.2); sweep.IterationBehavior = AnimationIterationBehavior.Forever;
        gradient.StartAnimation("StartPoint", sweep);
        var end = compositor.CreateVector2KeyFrameAnimation();
        end.InsertKeyFrame(0, Vector2.Zero); end.InsertKeyFrame(1, new Vector2(2, 0));
        end.Duration = sweep.Duration; end.IterationBehavior = AnimationIterationBehavior.Forever;
        gradient.StartAnimation("EndPoint", end);
        var pulse = compositor.CreateScalarKeyFrameAnimation();
        pulse.InsertKeyFrame(0, 0.4f); pulse.InsertKeyFrame(0.5f, 1); pulse.InsertKeyFrame(1, 0.4f);
        pulse.Duration = TimeSpan.FromSeconds(3.2); pulse.IterationBehavior = AnimationIterationBehavior.Forever;
        glow.StartAnimation("Opacity", pulse);
        sweep.Dispose(); end.Dispose(); pulse.Dispose();
    }
    public static BrandPulse? TryCreate(TextBlock element, bool animate)
    {
        if (!element.IsLoaded || element.ActualWidth <= 0 || element.ActualHeight <= 0 || element.Visibility != Visibility.Visible) return null;
        var original = element.Foreground;
        try { return new BrandPulse(element, animate); }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.COMException)
        {
            // A decorative effect must never bring down navigation or monitoring.
            ElementCompositionPreview.SetElementChildVisual(element, null);
            element.Foreground = original;
            Serilog.Log.Warning(ex, "Neon composition unavailable for {Element}", element.Name);
            return null;
        }
    }

    private sealed class NeonBrush : XamlCompositionBrushBase
    {
        public NeonBrush(CompositionBrush source) => CompositionBrush = source;
        public void Dispose() => CompositionBrush = null;
    }

    public void Dispose()
    {
        gradient.StopAnimation("StartPoint"); gradient.StopAnimation("EndPoint"); glow.StopAnimation("Opacity");
        ElementCompositionPreview.SetElementChildVisual(element, null);
        element.Foreground = originalForeground;
        neon.Dispose(); glow.Dispose(); foreground.Dispose(); gradient.Dispose();
        // GetAlphaMask is owned by XAML; do not dispose a borrowed cached brush.
    }
}
