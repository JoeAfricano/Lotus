using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Lotus.App;

// Two compositor animations; no UI timer, layout animation or per-frame application code.
internal sealed class BrandPulse : IDisposable
{
    private readonly FrameworkElement element;
    private readonly ScalarKeyFrameAnimation opacity;
    private readonly Vector3KeyFrameAnimation scale;
    public BrandPulse(FrameworkElement element)
    {
        this.element = element;
        var compositor = ElementCompositionPreview.GetElementVisual(element).Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0), new Vector2(0.6f, 1));
        element.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.Target = "Opacity";
        opacity.InsertKeyFrame(0, 0.8f); opacity.InsertKeyFrame(0.5f, 1, easing); opacity.InsertKeyFrame(1, 0.8f, easing);
        opacity.Duration = TimeSpan.FromSeconds(4.4);
        opacity.IterationBehavior = AnimationIterationBehavior.Forever;
        scale = compositor.CreateVector3KeyFrameAnimation();
        scale.Target = "Scale";
        scale.InsertKeyFrame(0, Vector3.One);
        scale.InsertKeyFrame(0.5f, new Vector3(1.015f, 1.015f, 1), easing);
        scale.InsertKeyFrame(1, Vector3.One, easing);
        scale.Duration = opacity.Duration;
        scale.IterationBehavior = AnimationIterationBehavior.Forever;
        element.StartAnimation(opacity);
        element.StartAnimation(scale);
    }
    public void Dispose()
    {
        element.StopAnimation(opacity); element.StopAnimation(scale);
        element.Opacity = 1; element.Scale = Vector3.One;
        opacity.Dispose(); scale.Dispose();
    }
}
