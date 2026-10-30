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
        BuildTour(vm); ShowStageArt(vm, animate: false);
        Visibility = Visibility.Visible;
        UpdateProgress(animate: false);
        Animate(Backdrop, "Opacity", 0, 1, 260);
        Animate(Card, "Opacity", 0, 1, 320);
        Animate(CardTransform, "ScaleX", 0.96, 1, 420, Spring()); Animate(CardTransform, "ScaleY", 0.96, 1, 420, Spring());
        Animate(CardTransform, "TranslateY", 16, 0, 420, Spring());
        if (vm.IsWelcome) UiSound.Play(UiSound.Cue.Welcome);
        StartBreathing(true);
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
        StartBreathing(true);
        ShowStageArt(vm, animate: true);
        if (vm.IsTour) StaggerTour();
        if (vm.IsProfile) WatchSteam();
    }

    private void UpdateProgress(bool animate)
    {
        if (Vm is not { } vm) return;
        var target = vm.StepNumber * 100.0 / vm.StepCount;
        if (!animate || !MotionEnabled) { StepProgress.Value = target; return; }
        // ProgressBar.Value is not an independent property: a frame-synced glide keeps it smooth.
        var from = StepProgress.Value;
        Motion.Tween(StepProgress, TimeSpan.FromMilliseconds(420), p => StepProgress.Value = from + (target - from) * p, Motion.EaseOutQuart);
    }

    // The stage art breathes slowly on every step (off with Reduce motion).
    private void StartBreathing(bool on)
    {
        breathing?.Stop(); breathing = null;
        if (!on || !MotionEnabled) return;
        breathing = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        Add(breathing, FlowerTransform, "ScaleX", 1, 1.045, 4200, ease); Add(breathing, FlowerTransform, "ScaleY", 1, 1.045, 4200, ease);
        Add(breathing, FlowerTransform, "TranslateY", 0, -6, 4200, ease);
        breathing.Begin();
    }

    // Tour: six cards in two columns, each with what it is and a concrete example.
    private void BuildTour(OnboardingViewModel vm)
    {
        TourList.Children.Clear(); TourList.ColumnDefinitions.Clear(); TourList.RowDefinitions.Clear();
        TourList.ColumnDefinitions.Add(new ColumnDefinition()); TourList.ColumnDefinitions.Add(new ColumnDefinition());
        var items = new[] { ("", "Optimize"), ("", "Games"), ("", "List"), ("", "Why"), ("", "Score"), ("\uE8F1", "Diary") };
        for (var r = 0; r < items.Length / 2; r++) TourList.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < items.Length; i++)
        {
            var (glyph, key) = items[i];
            var card = new Border
            {
                Padding = new Thickness(14, 12, 14, 12), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1),
                Background = (Brush)Application.Current.Resources["CardBrush"], BorderBrush = (Brush)Application.Current.Resources["CardBorderBrush"],
                Opacity = MotionEnabled ? 0 : 1, RenderTransform = new TranslateTransform(),
            };
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition());
            var badge = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(11), VerticalAlignment = VerticalAlignment.Top, Background = (Brush)Application.Current.Resources["BadgeBrush"],
                Child = new FontIcon { Glyph = glyph, FontSize = 15, Foreground = (Brush)Application.Current.Resources["RoseBrush"] } };
            var text = new StackPanel { Spacing = 3 };
            text.Children.Add(new TextBlock { Text = vm.Text["ObTour" + key], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            text.Children.Add(RichText.Block(vm.Text["ObTour" + key + "Detail"], 12, 0.8));
            text.Children.Add(new TextBlock { Text = vm.Text["ObTour" + key + "Ex"], FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), Foreground = (Brush)Application.Current.Resources["GoldBrush"] });
            Grid.SetColumn(text, 1); row.Children.Add(badge); row.Children.Add(text);
            card.Child = row;
            Grid.SetColumn(card, i % 2); Grid.SetRow(card, i / 2);
            TourList.Children.Add(card);
        }
    }

    // Stage art: the crystal lotus on every step; full on the welcome and the last step, softer behind the examples.
    private bool artLoaded;
    private void ShowStageArt(OnboardingViewModel vm, bool animate)
    {
        if (!artLoaded)
        {
            artLoaded = true;
            StageArt.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Concept", "lotus-sculpture.png"))) { DecodePixelWidth = 900 };
        }
        var target = vm.IsWelcome || vm.IsTour ? 1.0 : 0.42;
        if (animate) Animate(StageArt, "Opacity", StageArt.Opacity, target, 520); else StageArt.Opacity = target;
        if (animate) { Animate(StageShift, "Y", 18, 0, 420, Spring()); Animate(StageExamples, "Opacity", 0, 1, 300); }
    }
    private void OnPickTheme(object sender, RoutedEventArgs e) { if (Vm is { } vm && sender is FrameworkElement { Tag: string id }) vm.Theme = id; }
    private void OnPickFeatured(object sender, RoutedEventArgs e) { if (Vm is { } vm && sender is FrameworkElement { Tag: string id } f) { vm.PickFeatured(id); Motion.Pop(f); UiSound.Play(UiSound.Cue.Toggle); } }
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
        if (last) { UiSound.Play(UiSound.Cue.Done); Close(); }
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
    // Recommended program: confirm first, then install through winget (or open the official page when there is no package).
    private async void OnInstallApp(object sender, RoutedEventArgs e)
    {
        if (Vm?.Apps is not { } apps || sender is not FrameworkElement { Tag: RecommendedAppRow row }) return;
        if (!apps.InstallsDirectly(row)) { await Windows.System.Launcher.LaunchUriAsync(new Uri(row.App.Page)); return; }
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = apps.Text.Format("RecConfirmTitle", row.Name), Content = new TextBlock { Text = apps.Text.Format("RecConfirmBody", row.Name), TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = apps.Text["RecInstall"], CloseButtonText = apps.Text["RecCancel"], DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await apps.InstallAsync(row);
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
        var a = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(ms), EasingFunction = ease ?? Motion.Decelerate() };
        Storyboard.SetTarget(a, target); Storyboard.SetTargetProperty(a, property); sb.Children.Add(a);
    }
}
