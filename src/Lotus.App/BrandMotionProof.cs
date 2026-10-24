using System.Numerics;
using Lotus.Core;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace Lotus.App;

// Opt-in diagnostic only (--brand-motion-proof). Does not change Windows or preferences.
// Reuses the actual pixel flower and isolates Scale from all other animated effects.
internal sealed class BrandMotionProof : IDisposable
{
    private readonly ContentDialog dialog;
    private readonly Viewbox preview;
    private readonly FrameworkElement sidebarFlower;
    private readonly Func<bool> allowed;
    private readonly TextBlock status;
    private readonly ITextCatalog text;
    private ScalePulse? largePulse, smallPulse;
    private BrandPulse? artwork;
    private bool disposed;

    public BrandMotionProof(XamlRoot root, Viewbox sidebarFlower, Func<bool> allowed, ITextCatalog text)
    {
        this.sidebarFlower = sidebarFlower; this.allowed = allowed; this.text = text;
        // The existing BrandPulse factory draws the same pixel geometry below.
        // XAML Geometry objects cannot be reparented between two Path controls.
        var canvas = new Canvas { Width = 64, Height = 64 };
        preview = new Viewbox { Name = "BrandScalePreview", Width = 228, Height = 228, Child = canvas,
            HorizontalAlignment = HorizontalAlignment.Center };
        status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 18 };
        content.Children.Add(new TextBlock { Text = text["BrandProofDescription"], TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new Border { Width = 248, Height = 248, BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["MutedBrush"], Child = preview });
        content.Children.Add(new TextBlock { Text = "1.000 ↔ 1.035 · 4.4 s · 228 ↔ 236 px", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(status);
        dialog = new ContentDialog { XamlRoot = root, Title = text["BrandProofTitle"], Content = content,
            CloseButtonText = text["BrandProofClose"], RequestedTheme = ElementTheme.Dark };
        preview.Loaded += (_, _) => Refresh();
        dialog.Opened += (_, _) => Refresh();
    }
    public async Task ShowAsync() { try { await dialog.ShowAsync(); } finally { Dispose(); } }
    public void Refresh()
    {
        if (disposed || !preview.IsLoaded || preview.ActualWidth <= 0) return;
        artwork ??= BrandPulse.TryCreate(preview, animate: false, flower: true);
        if (artwork is null) { status.Text = text["BrandProofFailed"]; return; }
        largePulse ??= new(preview);
        if (sidebarFlower.IsLoaded && sidebarFlower.ActualWidth > 0) smallPulse ??= new(sidebarFlower);
        var enabled = allowed();
        largePulse.SetEnabled(enabled);
        smallPulse?.SetEnabled(enabled && sidebarFlower.Parent is FrameworkElement { Visibility: Visibility.Visible });
        status.Text = text[enabled ? "BrandProofRunning" : "BrandProofStopped"];
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; dialog.Hide();
        largePulse?.Dispose(); smallPulse?.Dispose(); artwork?.Dispose();
    }

    private sealed class ScalePulse : IDisposable
    {
        private readonly FrameworkElement target;
        private readonly Visual visual;
        private readonly Vector3KeyFrameAnimation animation;
        private readonly Vector3 previousCenter, previousScale;
        private bool running;
        public ScalePulse(FrameworkElement target)
        {
            this.target = target; visual = ElementCompositionPreview.GetElementVisual(target);
            previousCenter = visual.CenterPoint; previousScale = visual.Scale;
            visual.CenterPoint = new Vector3((float)target.ActualWidth / 2, (float)target.ActualHeight / 2, 0);
            var compositor = visual.Compositor;
            animation = compositor.CreateVector3KeyFrameAnimation(); animation.Target = "Scale";
            using var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.42f, 0), new Vector2(0.58f, 1));
            animation.InsertKeyFrame(0, Vector3.One);
            animation.InsertKeyFrame(0.5f, new Vector3(1.035f, 1.035f, 1), easing);
            animation.InsertKeyFrame(1, Vector3.One, easing);
            animation.Duration = TimeSpan.FromSeconds(4.4);
            animation.IterationBehavior = AnimationIterationBehavior.Forever;
        }
        public void SetEnabled(bool enabled)
        {
            if (enabled == running) return;
            running = enabled;
            if (enabled) visual.StartAnimation("Scale", animation);
            else { visual.StopAnimation("Scale"); visual.Scale = previousScale; }
            Serilog.Log.Information("Brand scale proof {Running} {Element} {Width} {Height}", enabled, target.Name, target.ActualWidth, target.ActualHeight);
        }
        public void Dispose()
        {
            SetEnabled(false); visual.CenterPoint = previousCenter; animation.Dispose();
        }
    }
}
