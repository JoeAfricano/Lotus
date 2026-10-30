using Lotus.Core.Diary;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Lotus.App.Controls;

// "Log this game" sheet over the whole window. The big score takes the review-site colours as you slide.
public sealed partial class DiaryEditorView : UserControl
{
    public event EventHandler<DiaryProgressChange>? Saved;
    private DiaryEditorViewModel? Vm => DataContext as DiaryEditorViewModel;
    public bool IsOpen => Visibility == Visibility.Visible;

    public DiaryEditorView()
    {
        InitializeComponent();
        KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; _ = RequestCloseAsync(); } };
    }

    public void Open(DiaryEditorViewModel vm)
    {
        DataContext = vm;
        Eyebrow.Text = vm.Text[vm.IsNew ? "DyEditorTitle" : "DyEditorEdit"].ToUpperInvariant();
        SaveLabel.Text = vm.Text[vm.IsNew ? "DyAdd" : "DySave"];
        Load(CoverArt, vm.Cover ?? vm.Wide); Load(HeaderArt, vm.Wide ?? vm.Cover, 1100); _ = LoadBannerAsync(vm);
        syncing = true; StartPicker.Date = vm.Started; EndPicker.Date = vm.Finished; syncing = false; // null shows an empty picker
        vm.PropertyChanged -= OnVmDates; vm.PropertyChanged += OnVmDates;
        PaintScore(); PaintStatus();
        Visibility = Visibility.Visible;
        StartDrift();
        StatusList.UpdateLayout(); PaintStatus(); // containers exist only after layout
        Fade(Backdrop, 0, 1, 200); Fade(Card, 0, 1, 260);
        Animate(CardMove, "TranslateY", 24, 0, 360, Motion.Decelerate());
        UiSound.Play(UiSound.Cue.Open);
        ScoreSlider.Focus(FocusState.Programmatic);
    }

    public void Close()
    {
        if (!IsOpen) return;
        var sb = new Storyboard();
        Add(sb, Backdrop, "Opacity", 1, 0, 160); Add(sb, Card, "Opacity", 1, 0, 160); Add(sb, CardMove, "TranslateY", 0, 16, 200);
        sb.Completed += (_, _) => { Visibility = Visibility.Collapsed; drift?.Stop(); };
        sb.Begin();
        UiSound.Play(UiSound.Cue.Close);
    }

    private void OnStatus(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || sender is not FrameworkElement { DataContext: StatusOption s } f) return;
        vm.SelectStatus(s.Status); Motion.Pop(f); UiSound.Play(UiSound.Cue.Toggle); PaintStatus();
    }
    private void OnFeat(object sender, RoutedEventArgs e) { if (sender is FrameworkElement f) { Motion.Pop(f); UiSound.Play(UiSound.Cue.Toggle); } }
    // Dates are set in code: a bound null shows 1926 in CalendarDatePicker.
    private bool syncing;
    private void OnStartChanged(CalendarDatePicker s, CalendarDatePickerDateChangedEventArgs e) { if (!syncing && Vm is { } vm) vm.Started = e.NewDate; }
    private void OnEndChanged(CalendarDatePicker s, CalendarDatePickerDateChangedEventArgs e) { if (!syncing && Vm is { } vm) vm.Finished = e.NewDate; }
    private void OnVmDates(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (Vm is not { } vm) return;
        syncing = true;
        if (e.PropertyName == nameof(DiaryEditorViewModel.Finished)) EndPicker.Date = vm.Finished;
        if (e.PropertyName == nameof(DiaryEditorViewModel.Started)) StartPicker.Date = vm.Started;
        syncing = false;
    }
    private void OnSteamHours(object sender, RoutedEventArgs e) => Vm?.UseSteamHours();
    private void OnScore(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) => PaintScore();
    private void OnBackdrop(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => _ = RequestCloseAsync();
    private void OnCancel(object sender, RoutedEventArgs e) => _ = RequestCloseAsync();

    // Escape, X, Cancel and the backdrop never drop typed changes silently: a dialog asks first.
    private bool asking, saving;
    private async Task RequestCloseAsync()
    {
        if (asking || Vm is not { } vm) return;
        FocusHelper.Commit(this);
        if (!vm.IsDirty) { Close(); return; }
        asking = true;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = vm.Text["DyDiscardTitle"], Content = vm.Text["DyDiscardBody"], PrimaryButtonText = vm.Text["DyDiscard"],
            CloseButtonText = vm.Text["DyKeepEditing"], DefaultButton = ContentDialogButton.Close, RequestedTheme = ThemePalette.Element,
        };
        try { if (await dialog.ShowAsync() == ContentDialogResult.Primary) Close(); } finally { asking = false; }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        FocusHelper.Commit(this); // commits the text being typed
        if (saving || !vm.CanSave) return;
        saving = true; SaveLabel.Text = vm.Text["DySaving"];
        Lotus.Core.Diary.DiaryProgressChange change;
        try { change = vm.Save(); }
        finally { saving = false; SaveLabel.Text = vm.Text[vm.IsNew ? "DyAdd" : "DySave"]; }
        Close();
        Saved?.Invoke(this, change);
    }

    private async void OnRemove(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = vm.Text["DyRemove"], Content = vm.Text.Format("DyRemoveConfirm", vm.Name),
            PrimaryButtonText = vm.Text["DyRemove"], CloseButtonText = vm.Text["DyCancel"], DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        vm.Remove(); Close();
    }

    // Big score colour: green / yellow / red like review sites, muted when unrated.
    // Score: the number, the slider and the ring take the review-site color; the ring glides to the new value.
    private double ringFrom;
    private void PaintScore()
    {
        if (Vm is not { } vm) return;
        Brush brush = vm.RatingKind switch
        {
            "good" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 102, 214, 140)),
            "mixed" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 204, 80)),
            "bad" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 110, 110)),
            _ => (Brush)Application.Current.Resources["MutedBrush"],
        };
        ScoreBig.FontSize = vm.RatingKind == "none" ? 16 : 32;
        ScoreArc.Stroke = brush; ScoreSlider.Foreground = brush;
        var target = vm.Rating / 10; var from = ringFrom; ringFrom = target;
        Motion.Tween(ScoreArc, TimeSpan.FromMilliseconds(380), p => DrawArc(from + (target - from) * p), Motion.EaseOutQuart);
    }
    // The score ring: an arc from the top, clockwise, for 0..1 of the circle (104 px, 7 px stroke).
    private void DrawArc(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 0.9999);
        if (fraction <= 0) { ScoreArc.Data = null; return; }
        const double size = 104, r = (size - 7) / 2, c = size / 2;
        var angle = fraction * 2 * Math.PI;
        var end = new Windows.Foundation.Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = new Windows.Foundation.Point(c, c - r), IsClosed = false };
        figure.Segments.Add(new ArcSegment { Point = end, Size = new Windows.Foundation.Size(r, r), SweepDirection = SweepDirection.Clockwise, IsLargeArc = fraction > 0.5 });
        var geometry = new PathGeometry(); geometry.Figures.Add(figure); ScoreArc.Data = geometry;
    }
    // Status cards: the chosen one takes its own color (green completed, blue playing...), the others stay plain.
    private void PaintStatus()
    {
        if (Vm is not { } vm) return;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            var colors = (IValueConverter)Application.Current.Resources["DiaryBrush"];
            for (var i = 0; i < StatusList.Items.Count; i++)
            {
                if (StatusList.ContainerFromIndex(i) is not ContentPresenter { Content: StatusOption s } cp || VisualTreeHelper.GetChildrenCount(cp) == 0 || VisualTreeHelper.GetChild(cp, 0) is not Button b) continue;
                var color = (Brush)colors.Convert(s.Status.ToString(), typeof(Brush), null!, "");
                b.BorderBrush = s.Selected ? color : (Brush)Application.Current.Resources["CardBorderBrush"];
                b.Background = s.Selected && color is SolidColorBrush sc ? new SolidColorBrush(sc.Color) { Opacity = 0.16 } : (Brush)Application.Current.Resources["CardBrush"];
                if (b.Content is StackPanel { Children: [Grid icon, ..] } && icon.Children is [Ellipse dot, FontIcon glyph]) { dot.Fill = s.Selected ? color : (Brush)Application.Current.Resources["SubtleBrush"]; glyph.Foreground = s.Selected ? new SolidColorBrush(Microsoft.UI.Colors.Black) : (Brush)Application.Current.Resources["MainTextBrush"]; }
            }
        });
    }
    // Cinematic header: the wide art drifts slowly (off with Reduce motion).
    private Storyboard? drift;
    private void StartDrift()
    {
        drift?.Stop(); HeaderZoom.ScaleX = HeaderZoom.ScaleY = 1;
        if (!Motion.Active) return;
        drift = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        Add(drift, HeaderZoom, "ScaleX", 1, 1.08, 16000, ease); Add(drift, HeaderZoom, "ScaleY", 1, 1.08, 16000, ease);
        drift.Begin();
    }
    // Two columns on wide windows, one under the other on narrow ones.
    private void OnColumnsSize(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < 820;
        Columns.ColumnDefinitions[0].Width = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(340);
        Columns.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(RightColumn, stacked ? 0 : 1); Grid.SetRow(RightColumn, stacked ? 1 : 0);
    }
    private void OnCategory(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) => Vm?.NotifyCategories();
    private void OnUseAverage(object sender, RoutedEventArgs e) { Vm?.UseCategoryAverage(); UiSound.Play(UiSound.Cue.Toggle); }
    private void OnResumeWrite(object sender, RoutedEventArgs e) { Vm?.EditResume(); UiSound.Play(UiSound.Cue.Toggle); }
    private void OnResumeSave(object sender, RoutedEventArgs e) { if (Vm?.SaveResume() == true) UiSound.Play(UiSound.Cue.Done); }
    private void OnResumeCancel(object sender, RoutedEventArgs e) => Vm?.CancelResume();
    private void OnResumeClear(object sender, RoutedEventArgs e) { if (Vm?.ClearResume() == true) UiSound.Play(UiSound.Cue.Toggle); }
    private void OnAddRun(object sender, RoutedEventArgs e) { Vm?.AddRun(); UiSound.Play(UiSound.Cue.Toggle); }
    private void OnRemoveRun(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { DataContext: RunRow run }) Vm?.RemoveRun(run); }

    // Store banner: tries each candidate (best first); the first one that opens fades in over the fallback.
    private Queue<string> banners = new();
    private int bannerOpen;
    private async Task LoadBannerAsync(DiaryEditorViewModel vm)
    {
        var ticket = ++bannerOpen;
        BannerArt.Opacity = 0; BannerArt.Source = null;
        IReadOnlyList<string> list;
        try { list = await Lotus.Infrastructure.Games.StoreArt.BannerAsync(vm.EntryKey, vm.AppId); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        if (ticket != bannerOpen || DataContext != vm) return;
        banners = new(list.Where(u => u != (vm.Wide ?? vm.Cover)));
        NextBanner();
    }
    private void NextBanner() { if (banners.Count > 0) Load(BannerArt, banners.Dequeue(), 1600); }
    private void OnStoreArtOpened(object sender, RoutedEventArgs e) { banners.Clear(); Fade(BannerArt, 0, 0.9, 500); }
    private void OnStoreArtFailed(object sender, ExceptionRoutedEventArgs e) => NextBanner();
    private static void Load(Image target, string? url, int width = 320)
    {
        target.Source = null;
        if (url is null) return;
        if (Uri.TryCreate(url, UriKind.Absolute, out var u)) target.Source = new BitmapImage(u) { DecodePixelWidth = width };
    }
    private static void Fade(UIElement e, double from, double to, int ms) { var sb = new Storyboard(); Add(sb, e, "Opacity", from, to, ms); sb.Begin(); }
    private static void Animate(DependencyObject target, string property, double from, double to, int ms, EasingFunctionBase ease) { var sb = new Storyboard(); Add(sb, target, property, from, to, ms, ease); sb.Begin(); }
    private static void Add(Storyboard sb, DependencyObject target, string property, double from, double to, int ms, EasingFunctionBase? ease = null)
    {
        var a = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(Motion.Active ? ms : 0), EasingFunction = ease ?? Motion.Decelerate() };
        Storyboard.SetTarget(a, target); Storyboard.SetTargetProperty(a, property); sb.Children.Add(a);
    }
}
