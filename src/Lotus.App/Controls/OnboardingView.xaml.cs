using Lotus.Core.Profile;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Storage.Pickers;

namespace Lotus.App.Controls;

// First-run welcome. Motion: backdrop fade, card rises and scales in with a soft ease, each step slides in from the
// direction of travel, the progress bar glides, and the lotus "breathes" on the welcome step. Respects Reduce motion.
public sealed partial class OnboardingView : UserControl
{
    private int lastStep;
    private Storyboard? breathing;
    public bool MotionEnabled { get; set; } = true;
    public event EventHandler? Closed;

    public OnboardingView()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    private OnboardingViewModel? Vm => DataContext as OnboardingViewModel;

    public void Open(OnboardingViewModel vm, bool edit)
    {
        DataContext = vm;
        vm.PropertyChanged -= OnVmChanged; vm.PropertyChanged += OnVmChanged;
        vm.Start(edit);
        lastStep = vm.Step;
        BuildTour(vm);
        Visibility = Visibility.Visible;
        UpdateProgress(animate: false);
        Animate(Backdrop, "Opacity", 0, 1, 260);
        Animate(Card, "Opacity", 0, 1, 320);
        Animate(CardTransform, "ScaleX", 0.96, 1, 420, Spring()); Animate(CardTransform, "ScaleY", 0.96, 1, 420, Spring());
        Animate(CardTransform, "TranslateY", 16, 0, 420, Spring());
        StartBreathing(vm.IsWelcome);
    }

    private void Close()
    {
        if (Vm is { } vm) vm.PropertyChanged -= OnVmChanged;
        breathing?.Stop();
        var ms = MotionEnabled ? 200 : 0;
        var sb = new Storyboard();
        Add(sb, Backdrop, "Opacity", 1, 0, ms); Add(sb, Card, "Opacity", 1, 0, ms); Add(sb, CardTransform, "TranslateY", 0, 10, ms);
        sb.Completed += (_, _) => { Visibility = Visibility.Collapsed; Closed?.Invoke(this, EventArgs.Empty); };
        sb.Begin();
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OnboardingViewModel.Step) || Vm is not { } vm) return;
        var forward = vm.Step >= lastStep; lastStep = vm.Step;
        // New step slides in from the side we are moving to, with a short fade.
        Animate(StepShift, "X", forward ? 36 : -36, 0, 340, Spring());
        Animate(StepHost, "Opacity", 0, 1, 240);
        UpdateProgress(animate: true);
        StartBreathing(vm.IsWelcome);
        if (vm.IsTour) StaggerTour();
        if (vm.IsProfile) WatchSteam();
    }

    private void UpdateProgress(bool animate)
    {
        if (Vm is not { } vm) return;
        var target = vm.StepNumber * 100.0 / vm.StepCount;
        if (!animate || !MotionEnabled) { StepProgress.Value = target; return; }
        // ProgressBar.Value is not an independent property; a short timer-driven glide keeps it smooth.
        var from = StepProgress.Value; var start = DateTime.UtcNow;
        var timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.Tick += (t, _) =>
        {
            var p = Math.Min(1, (DateTime.UtcNow - start).TotalMilliseconds / 360);
            StepProgress.Value = from + (target - from) * (1 - Math.Pow(1 - p, 3));
            if (p >= 1) t.Stop();
        };
        timer.Start();
    }

    // Gentle scale/opacity loop on the welcome flower.
    private void StartBreathing(bool on)
    {
        breathing?.Stop(); breathing = null;
        if (!on || !MotionEnabled) return;
        breathing = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        Add(breathing, FlowerTransform, "ScaleX", 1, 1.06, 2200, ease); Add(breathing, FlowerTransform, "ScaleY", 1, 1.06, 2200, ease);
        Add(breathing, FlowerTransform, "TranslateY", 0, -4, 2200, ease);
        breathing.Begin();
    }

    private void BuildTour(OnboardingViewModel vm)
    {
        TourList.Children.Clear();
        foreach (var (glyph, key) in new[] { ("", "Optimize"), ("", "Games"), ("", "Why"), ("", "Score"), ("", "Tools") })
        {
            var row = new Grid { ColumnSpacing = 14, Opacity = MotionEnabled ? 0 : 1, RenderTransform = new TranslateTransform() };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition());
            var badge = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(12), Background = (Brush)Application.Current.Resources["BadgeBrush"],
                Child = new FontIcon { Glyph = glyph, FontSize = 17, Foreground = (Brush)Application.Current.Resources["RoseBrush"] } };
            var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = vm.Text["ObTour" + key], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            text.Children.Add(RichText.Block(vm.Text["ObTour" + key + "Detail"], 12.5, 0.8));
            Grid.SetColumn(text, 1); row.Children.Add(badge); row.Children.Add(text);
            TourList.Children.Add(row);
        }
    }
    // Rows arrive one after another (60 ms apart).
    private void StaggerTour()
    {
        if (!MotionEnabled) return;
        var i = 0;
        foreach (FrameworkElement row in TourList.Children)
        {
            var sb = new Storyboard { BeginTime = TimeSpan.FromMilliseconds(80 + i++ * 60) };
            Add(sb, row, "Opacity", 0, 1, 260); Add(sb, (TranslateTransform)row.RenderTransform, "Y", 10, 0, 320, Spring());
            sb.Begin();
        }
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var last = vm.IsLast; vm.Next();
        if (last) Close();
    }
    private void OnBack(object sender, RoutedEventArgs e) => Vm?.Back();
    private void OnSkip(object sender, RoutedEventArgs e) { Vm?.Skip(); Close(); }
    private void OnPickGoal(object sender, RoutedEventArgs e) { if (Vm is { } vm && sender is FrameworkElement { Tag: string id }) vm.Goal = id; }
    private void OnPickAccent(object sender, RoutedEventArgs e) { if (Vm is { } vm && sender is FrameworkElement { Tag: string id }) vm.Accent = id; }
    // Opens Steam (or its official download page); the account is picked up automatically once it is there.
    private async void OnSteamSignIn(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        await Windows.System.Launcher.LaunchUriAsync(new Uri(vm.SteamSignInUrl));
        WatchSteam();
    }
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? steamWatch;
    private void WatchSteam()
    {
        if (steamWatch is not null || Vm is not { SteamFound: false }) return;
        steamWatch = DispatcherQueue.CreateTimer(); steamWatch.Interval = TimeSpan.FromSeconds(3);
        steamWatch.Tick += (t, _) =>
        {
            if (Vm is not { } vm || Visibility != Visibility.Visible) { t.Stop(); steamWatch = null; return; }
            if (vm.RecheckSteam()) { t.Stop(); steamWatch = null; Animate(SteamChoice, "Opacity", 0, 1, 360); }
        };
        steamWatch.Start();
    }
    private void OnUseSteam(object sender, RoutedEventArgs e) { if (Vm is { } vm) vm.UseSteam = true; }
    private void OnUseCustom(object sender, RoutedEventArgs e) { if (Vm is { } vm) vm.UseCustom = true; }
    private void OnCustomFocus(object sender, RoutedEventArgs e) { if (Vm is { } vm) vm.UseCustom = true; }
    private async void OnPickPicture(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, ViewMode = PickerViewMode.Thumbnail };
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" }) picker.FileTypeFilter.Add(ext);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        if (await picker.PickSingleFileAsync() is { } file) vm.ChoosePicture(file.Path);
    }

    // ---- small animation helpers ----
    private static EasingFunctionBase Spring() => new BackEase { Amplitude = 0.18, EasingMode = EasingMode.EaseOut };
    private void Animate(DependencyObject target, string property, double from, double to, int ms, EasingFunctionBase? ease = null)
    {
        var sb = new Storyboard(); Add(sb, target, property, from, to, MotionEnabled ? ms : 0, ease); sb.Begin();
    }
    private static void Add(Storyboard sb, DependencyObject target, string property, double from, double to, int ms, EasingFunctionBase? ease = null)
    {
        var a = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(ms), EasingFunction = ease ?? new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(a, target); Storyboard.SetTargetProperty(a, property); sb.Children.Add(a);
    }
}
