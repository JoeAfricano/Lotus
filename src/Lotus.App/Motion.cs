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
    // Lotus "Reduce motion" off AND Windows animations on (read once a second at most: the setting rarely changes).
    private static bool On
    {
        get
        {
            if (!Enabled) return false;
            var now = Environment.TickCount64;
            if (now - systemCheckedAt > 1000) { systemAnimations = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled; systemCheckedAt = now; }
            return systemAnimations;
        }
    }
    private static bool systemAnimations = true;
    private static long systemCheckedAt = long.MinValue / 2;
    public static bool Active => On;
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(120), Normal = TimeSpan.FromMilliseconds(220), Slow = TimeSpan.FromMilliseconds(360);

    private static CompositionEasingFunction Ease(Compositor c) => c.CreateCubicBezierEasingFunction(new Vector2(0.05f, 0.7f), new Vector2(0.1f, 1f));

    // ---- Storyboard curves and durations shared by every panel (sheets, dialogs, overlays) ----
    // Arrivals: quick start and a long soft landing. Exits: a short acceleration out (exits never linger).
    public static EasingFunctionBase Decelerate() => new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 };
    public static EasingFunctionBase Accelerate() => new CubicEase { EasingMode = EasingMode.EaseIn };
    public static EasingFunctionBase Gentle() => new SineEase { EasingMode = EasingMode.EaseInOut };
    // A duration that collapses to zero with Reduce motion / Windows animations off.
    public static TimeSpan Ms(double milliseconds) => TimeSpan.FromMilliseconds(On ? milliseconds : 0);

    // ---- Frame-synced tweens: values that are not independent properties (progress bars, arcs, counters) ----
    // Driven by CompositionTarget.Rendering, so every step lands on a real frame (smooth on 60/120/144 Hz screens),
    // instead of a 16 ms timer that drifts against the display. One tween per key: a new one replaces the old.
    public static double EaseOutCubic(double p) => 1 - Math.Pow(1 - p, 3);
    public static double EaseOutQuart(double p) => 1 - Math.Pow(1 - p, 4);
    public static double EaseOutExpo(double p) => p >= 1 ? 1 : 1 - Math.Pow(2, -10 * p);
    private sealed class Frame(Func<double, bool> step, long start) { public Func<double, bool> Step = step; public long Start = start; }
    private static readonly Dictionary<object, Frame> frames = [];
    private static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private static bool hooked;

    // Calls `step(elapsed ms)` once per frame until it returns false (or another call reuses the key).
    public static void OnFrames(object key, Func<double, bool> step, TimeSpan? delay = null)
    {
        frames[key] = new Frame(step, clock.ElapsedMilliseconds + (long)(delay?.TotalMilliseconds ?? 0));
        if (!hooked) { Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnRendering; hooked = true; }
    }
    public static void StopFrames(object key) => frames.Remove(key);

    // Animates 0 → 1 over `duration` (eased) and calls `apply` every frame; `done` runs once at the end.
    public static void Tween(object key, TimeSpan duration, Action<double> apply, Func<double, double>? ease = null, Action? done = null, TimeSpan? delay = null)
    {
        if (!On || duration <= TimeSpan.Zero) { StopFrames(key); apply(1); done?.Invoke(); return; }
        var curve = ease ?? EaseOutCubic; var total = duration.TotalMilliseconds;
        OnFrames(key, ms =>
        {
            var p = Math.Min(1, ms / total);
            apply(curve(p));
            if (p < 1) return true;
            done?.Invoke();
            return false;
        }, delay);
    }

    private static void OnRendering(object? sender, object e)
    {
        var now = clock.ElapsedMilliseconds;
        foreach (var (key, f) in frames.ToArray())
        {
            var elapsed = now - f.Start;
            if (elapsed < 0) continue;
            bool more;
            try { more = f.Step(elapsed); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Animation step"); more = false; } // a broken animation must never break the app
            if (!more && frames.TryGetValue(key, out var current) && ReferenceEquals(current, f)) frames.Remove(key);
        }
        if (frames.Count == 0) { Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering; hooked = false; } // no cost while idle
    }

    // ---- Page entrance: content rises 14 px and fades in ----
    public static void EnterPage(UIElement element) => Rise(element, 16, TimeSpan.FromMilliseconds(420), fromScale: 0.985f);

    // Fade in while moving up from `distance` px, on the element's Translation (layout is never touched).
    private static void Rise(UIElement element, float distance, TimeSpan duration, float fromOpacity = 0, float fromScale = 1)
    {
        // Elements with the hover lift use XAML's own Scale/Translation transitions; the compositor visual is off
        // limits for them (Windows throws), so they rise through those transitions instead.
        if (element.ScaleTransition is not null || element.TranslationTransition is not null) { RiseWithTransitions(element, distance, duration, fromOpacity); return; }
        try { RiseOnVisual(element, distance, duration, fromOpacity, fromScale); }
        catch (UnauthorizedAccessException) { RiseWithTransitions(element, distance, duration, fromOpacity); } // never let an animation crash the app
    }

    private static void RiseWithTransitions(UIElement element, float distance, TimeSpan duration, float fromOpacity)
    {
        var translation = element.TranslationTransition;
        element.TranslationTransition = null; element.OpacityTransition = null;
        if (!On) { element.Translation = Vector3.Zero; element.Opacity = 1; element.TranslationTransition = translation; return; }
        element.Translation = new Vector3(0, distance, 0); element.Opacity = fromOpacity;
        element.DispatcherQueue.TryEnqueue(() =>
        {
            element.TranslationTransition = new Vector3Transition { Duration = duration };
            element.OpacityTransition = new ScalarTransition { Duration = duration };
            element.Translation = Vector3.Zero; element.Opacity = 1;
            // Hand the hover lift its own (shorter) transition back once the entrance is over.
            var timer = element.DispatcherQueue.CreateTimer(); timer.Interval = duration + TimeSpan.FromMilliseconds(40); timer.IsRepeating = false;
            timer.Tick += (_, _) => { element.TranslationTransition = translation; element.OpacityTransition = null; };
            timer.Start();
        });
    }

    private static void RiseOnVisual(UIElement element, float distance, TimeSpan duration, float fromOpacity, float fromScale = 1)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var v = ElementCompositionPreview.GetElementVisual(element);
        if (!On) { v.Opacity = 1; v.Properties.InsertVector3("Translation", Vector3.Zero); v.Scale = Vector3.One; return; }
        var c = v.Compositor;
        var fade = c.CreateScalarKeyFrameAnimation(); fade.InsertKeyFrame(0, fromOpacity); fade.InsertKeyFrame(1, 1, Ease(c)); fade.Duration = duration;
        var rise = c.CreateVector3KeyFrameAnimation(); rise.InsertKeyFrame(0, new Vector3(0, distance, 0)); rise.InsertKeyFrame(1, Vector3.Zero, Ease(c)); rise.Duration = duration;
        v.StartAnimation("Opacity", fade); v.StartAnimation("Translation", rise);
        // Pages also grow from 98.5 % around their top centre: depth without any blur or layout work.
        if (fromScale < 1 && element is FrameworkElement fe)
        {
            v.CenterPoint = new Vector3((float)fe.ActualWidth / 2, 0, 0);
            var grow = c.CreateVector3KeyFrameAnimation(); grow.InsertKeyFrame(0, new Vector3(fromScale, fromScale, 1)); grow.InsertKeyFrame(1, Vector3.One, Ease(c)); grow.Duration = duration;
            v.StartAnimation("Scale", grow);
        }
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
        // Cards (Borders) also light their edge in the accent while the pointer is over them.
        Microsoft.UI.Xaml.Media.Brush? restBorder = null;
        void Glow(bool on)
        {
            if (f is not Border b || Application.Current.Resources["CardHoverBorderBrush"] is not Microsoft.UI.Xaml.Media.Brush hover) return;
            if (on) { if (!ReferenceEquals(b.BorderBrush, hover)) restBorder = b.BorderBrush; b.BorderBrush = hover; }
            else if (restBorder is not null) { b.BorderBrush = restBorder; restBorder = null; }
        }
        void Rest() { f.Scale = Vector3.One; f.Translation = Vector3.Zero; Glow(false); }
        f.PointerEntered += (_, _) => { Glow(true); if (On) { f.Scale = new Vector3((float)scale, (float)scale, 1); f.Translation = new Vector3(0, -2, 0); } };
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
        // Ease-out quart: the number runs fast, then settles on the final value.
        Tween(block, duration ?? TimeSpan.FromMilliseconds(900), p => block.Text = format((int)Math.Round(to * p)), EaseOutQuart);
    }

    // ---- Pop: an icon springs up (1 → 1.22 → 1) to confirm a choice, e.g. the selected menu item ----
    public static void Pop(UIElement element)
    {
        if (!On || element is not FrameworkElement f) return;
        if (element.ScaleTransition is not null)
        {
            // Press/hover elements: grow, then settle through their own ScaleTransition (compositor is off limits).
            f.CenterPoint = new Vector3((float)f.ActualWidth / 2, (float)f.ActualHeight / 2, 0);
            element.Scale = new Vector3(1.18f, 1.18f, 1);
            var back = element.DispatcherQueue.CreateTimer(); back.Interval = TimeSpan.FromMilliseconds(140); back.IsRepeating = false;
            back.Tick += (_, _) => element.Scale = Vector3.One; back.Start();
            return;
        }
        try { PopOnVisual(f); } catch (UnauthorizedAccessException) { } // an animation must never crash the app
    }
    private static void PopOnVisual(FrameworkElement f)
    {
        UIElement element = f;
        var v = ElementCompositionPreview.GetElementVisual(element); var c = v.Compositor;
        v.CenterPoint = new Vector3((float)f.ActualWidth / 2, (float)f.ActualHeight / 2, 0);
        var a = c.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(0.35f, new Vector3(1.22f, 1.22f, 1), Ease(c));
        a.InsertKeyFrame(1, Vector3.One, c.CreateCubicBezierEasingFunction(new Vector2(0.34f, 1.56f), new Vector2(0.64f, 1f))); // slight overshoot
        a.Duration = TimeSpan.FromMilliseconds(420);
        v.StartAnimation("Scale", a);
    }

    // ---- Sheen: a faint diagonal light sweeps across a hero surface every few seconds ----
    public static readonly DependencyProperty SheenProperty = DependencyProperty.RegisterAttached("Sheen", typeof(bool), typeof(Motion),
        new PropertyMetadata(false, (d, e) => { if (d is FrameworkElement f && e.NewValue is true) AttachSheen(f); }));
    public static bool GetSheen(DependencyObject d) => (bool)d.GetValue(SheenProperty);
    public static void SetSheen(DependencyObject d, bool value) => d.SetValue(SheenProperty, value);

    private static void AttachSheen(FrameworkElement f)
    {
        // Not GetElementVisual: that would clash with the hover lift (implicit Scale/Translation) on the same element.
        var c = Microsoft.UI.Xaml.Media.CompositionTarget.GetCompositorForCurrentThread();
        var brush = c.CreateLinearGradientBrush(); brush.StartPoint = new Vector2(0, 0); brush.EndPoint = new Vector2(1, 0);
        brush.ColorStops.Add(c.CreateColorGradientStop(0, Windows.UI.Color.FromArgb(0, 255, 255, 255)));
        brush.ColorStops.Add(c.CreateColorGradientStop(0.5f, Windows.UI.Color.FromArgb(22, 255, 236, 244)));
        brush.ColorStops.Add(c.CreateColorGradientStop(1, Windows.UI.Color.FromArgb(0, 255, 255, 255)));
        var band = c.CreateSpriteVisual(); band.Brush = brush; band.RotationAngleInDegrees = 18;
        var clip = c.CreateContainerVisual(); clip.Children.InsertAtTop(band);
        ElementCompositionPreview.SetElementChildVisual(f, clip); // drawn above the content; composition visuals are not hit-tested
        void Layout()
        {
            var w = (float)f.ActualWidth; var h = (float)f.ActualHeight;
            band.StopAnimation("Offset.X");
            if (w <= 0 || h <= 0) return;
            clip.Size = new Vector2(w, h);
            var radius = f is Border b ? (float)b.CornerRadius.TopLeft : 16f;
            var shape = c.CreateRoundedRectangleGeometry(); shape.Size = new Vector2(w, h); shape.CornerRadius = new Vector2(radius, radius);
            clip.Clip = c.CreateGeometricClip(shape);
            band.Size = new Vector2(Math.Max(160, w * 0.22f), h * 2.2f); band.Offset = new Vector3(-band.Size.X * 2, -h * 0.6f, 0);
            band.IsVisible = On;
            if (!On) return;
            var sweep = c.CreateScalarKeyFrameAnimation();
            sweep.InsertKeyFrame(0, -band.Size.X * 2); sweep.InsertKeyFrame(0.32f, w + band.Size.X, Ease(c)); sweep.InsertKeyFrame(1, w + band.Size.X);
            sweep.Duration = TimeSpan.FromSeconds(7); sweep.DelayTime = TimeSpan.FromMilliseconds(900); sweep.IterationBehavior = AnimationIterationBehavior.Forever;
            band.StartAnimation("Offset.X", sweep);
        }
        f.SizeChanged += (_, _) => Layout();
        f.Loaded += (_, _) => Layout();
        f.Unloaded += (_, _) => band.StopAnimation("Offset.X");
    }
}

// Moves keyboard focus off the current text box so its binding commits (WinUI desktop needs a search root).
public static class FocusHelper
{
    public static void Commit(Microsoft.UI.Xaml.FrameworkElement root)
    {
        if (root.XamlRoot?.Content is not DependencyObject top) return;
        try { Microsoft.UI.Xaml.Input.FocusManager.TryMoveFocus(Microsoft.UI.Xaml.Input.FocusNavigationDirection.Next, new Microsoft.UI.Xaml.Input.FindNextElementOptions { SearchRoot = top }); }
        catch (System.Runtime.InteropServices.COMException) { } // nothing focusable: nothing to commit
    }
}
