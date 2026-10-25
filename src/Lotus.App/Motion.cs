using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Animation;

namespace Lotus.App;

// One motion language for the whole app. Everything runs on the compositor (independent of the UI thread),
// uses the same durations and curves, and turns off with Reduce motion (Lotus) or Windows animation settings.
//   Fast 120 ms: press feedback.  Normal 220 ms: hover, status text.  Slow 360 ms: page and section entrance.
//   Curve: "emphasized decelerate" (0.05, 0.7, 0.1, 1): quick start, soft landing.
public static class Motion
{
    public static bool Enabled { get; set; } = true;
    private static bool On => Enabled && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(120), Normal = TimeSpan.FromMilliseconds(220), Slow = TimeSpan.FromMilliseconds(360);

    private static CompositionEasingFunction Ease(Compositor c) => c.CreateCubicBezierEasingFunction(new Vector2(0.05f, 0.7f), new Vector2(0.1f, 1f));

    // ---- Page entrance: content rises 14 px and fades in ----
    public static void EnterPage(UIElement element) => Rise(element, 14, Slow);

    // Fade in while moving up from `distance` px, on the element's Translation (layout is never touched).
    private static void Rise(UIElement element, float distance, TimeSpan duration, float fromOpacity = 0)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var v = ElementCompositionPreview.GetElementVisual(element);
        if (!On) { v.Opacity = 1; v.Properties.InsertVector3("Translation", Vector3.Zero); return; }
        var c = v.Compositor;
        var fade = c.CreateScalarKeyFrameAnimation(); fade.InsertKeyFrame(0, fromOpacity); fade.InsertKeyFrame(1, 1, Ease(c)); fade.Duration = duration;
        var rise = c.CreateVector3KeyFrameAnimation(); rise.InsertKeyFrame(0, new Vector3(0, distance, 0)); rise.InsertKeyFrame(1, Vector3.Zero, Ease(c)); rise.Duration = duration;
        v.StartAnimation("Opacity", fade); v.StartAnimation("Translation", rise);
    }

    // ---- Staggered entrance for a panel's children (cards arrive one after another) ----
    public static readonly DependencyProperty StaggerProperty = DependencyProperty.RegisterAttached("Stagger", typeof(bool), typeof(Motion),
        new PropertyMetadata(false, (d, e) => { if (d is Panel p) p.ChildrenTransitions = e.NewValue is true && On ? [new EntranceThemeTransition { IsStaggeringEnabled = true, FromVerticalOffset = 24 }, new RepositionThemeTransition()] : null; }));
    public static bool GetStagger(DependencyObject d) => (bool)d.GetValue(StaggerProperty);
    public static void SetStagger(DependencyObject d, bool value) => d.SetValue(StaggerProperty, value);

    // ---- Hover lift: cards rise 2 px and grow slightly; implicit animations make every change smooth ----
    public static readonly DependencyProperty HoverProperty = DependencyProperty.RegisterAttached("Hover", typeof(double), typeof(Motion),
        new PropertyMetadata(0.0, (d, e) => { if (d is FrameworkElement f && e.NewValue is double scale && scale > 0) AttachHover(f, scale); }));
    public static double GetHover(DependencyObject d) => (double)d.GetValue(HoverProperty);
    public static void SetHover(DependencyObject d, double value) => d.SetValue(HoverProperty, value);

    private static void AttachHover(FrameworkElement f, double scale)
    {
        f.ScaleTransition = new Vector3Transition { Duration = Normal }; f.TranslationTransition = new Vector3Transition { Duration = Normal };
        f.SizeChanged += (_, _) => f.CenterPoint = new Vector3((float)f.ActualWidth / 2, (float)f.ActualHeight / 2, 0);
        void Rest() { f.Scale = Vector3.One; f.Translation = Vector3.Zero; }
        f.PointerEntered += (_, _) => { if (On) { f.Scale = new Vector3((float)scale, (float)scale, 1); f.Translation = new Vector3(0, -2, 0); } };
        f.PointerExited += (_, _) => Rest(); f.PointerCanceled += (_, _) => Rest(); f.PointerCaptureLost += (_, _) => Rest();
    }

    // ---- Press: buttons dip to 97% while pressed and spring back ----
    public static readonly DependencyProperty PressProperty = DependencyProperty.RegisterAttached("Press", typeof(bool), typeof(Motion),
        new PropertyMetadata(false, (d, e) => { if (d is UIElement u && e.NewValue is true) AttachPress(u); }));
    public static bool GetPress(DependencyObject d) => (bool)d.GetValue(PressProperty);
    public static void SetPress(DependencyObject d, bool value) => d.SetValue(PressProperty, value);

    private static void AttachPress(UIElement u)
    {
        u.ScaleTransition = new Vector3Transition { Duration = Fast };
        if (u is FrameworkElement f) f.SizeChanged += (_, _) => u.CenterPoint = new Vector3((float)f.ActualWidth / 2, (float)f.ActualHeight / 2, 0);
        void Rest() => u.Scale = Vector3.One;
        // Buttons handle pointer presses themselves, so listen to handled events too.
        u.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => { if (On) u.Scale = new Vector3(0.97f, 0.97f, 1); }), true);
        u.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => Rest()), true);
        u.AddHandler(UIElement.PointerExitedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => Rest()), true);
        u.AddHandler(UIElement.PointerCaptureLostEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => Rest()), true);
    }

    // ---- Status text: new messages fade and settle in instead of popping ----
    public static readonly DependencyProperty SettleProperty = DependencyProperty.RegisterAttached("Settle", typeof(bool), typeof(Motion),
        new PropertyMetadata(false, (d, e) => { if (d is TextBlock t && e.NewValue is true) t.RegisterPropertyChangedCallback(TextBlock.TextProperty, (s, _) => Settle((UIElement)s)); }));
    public static bool GetSettle(DependencyObject d) => (bool)d.GetValue(SettleProperty);
    public static void SetSettle(DependencyObject d, bool value) => d.SetValue(SettleProperty, value);

    public static void Settle(UIElement element) => Rise(element, 4, Normal, 0.2f);

    // ---- Numbers count up to their new value (scores) ----
    public static void CountTo(TextBlock block, int to, Func<int, string> format, TimeSpan? duration = null)
    {
        if (!On) { block.Text = format(to); return; }
        var total = (duration ?? TimeSpan.FromMilliseconds(900)).TotalMilliseconds; var start = DateTime.UtcNow;
        var timer = block.DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.Tick += (t, _) =>
        {
            var p = Math.Min(1, (DateTime.UtcNow - start).TotalMilliseconds / total);
            block.Text = format((int)Math.Round(to * (1 - Math.Pow(1 - p, 4)))); // ease-out quart
            if (p >= 1) t.Stop();
        };
        timer.Start();
    }

}
