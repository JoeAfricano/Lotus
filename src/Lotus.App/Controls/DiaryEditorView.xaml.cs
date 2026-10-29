using Lotus.Core.Diary;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        Load(CoverArt, vm.Cover ?? vm.Wide); Load(HeaderArt, vm.Wide ?? vm.Cover, 1100);
        syncing = true; StartPicker.Date = vm.Started; EndPicker.Date = vm.Finished; syncing = false; // null shows an empty picker
        vm.PropertyChanged -= OnVmDates; vm.PropertyChanged += OnVmDates;
        PaintScore();
        Visibility = Visibility.Visible;
        Fade(Backdrop, 0, 1, 200); Fade(Card, 0, 1, 260);
        Animate(CardMove, "TranslateY", 24, 0, 360, new CubicEase { EasingMode = EasingMode.EaseOut });
        UiSound.Play(UiSound.Cue.Open);
        ScoreSlider.Focus(FocusState.Programmatic);
    }

    public void Close()
    {
        if (!IsOpen) return;
        var sb = new Storyboard();
        Add(sb, Backdrop, "Opacity", 1, 0, 160); Add(sb, Card, "Opacity", 1, 0, 160); Add(sb, CardMove, "TranslateY", 0, 16, 200);
        sb.Completed += (_, _) => Visibility = Visibility.Collapsed;
        sb.Begin();
        UiSound.Play(UiSound.Cue.Close);
    }

    private void OnStatus(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || sender is not FrameworkElement { DataContext: StatusOption s } f) return;
        vm.SelectStatus(s.Status); Motion.Pop(f); UiSound.Play(UiSound.Cue.Toggle);
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
    private void PaintScore()
    {
        if (Vm is not { } vm) return;
        ScoreBig.Foreground = vm.RatingKind switch
        {
            "good" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 102, 214, 140)),
            "mixed" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 204, 80)),
            "bad" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 110, 110)),
            _ => (Brush)Application.Current.Resources["MutedBrush"],
        };
        ScoreBig.FontSize = vm.RatingKind == "none" ? 22 : 44;
    }

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
        var a = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(Motion.Enabled ? ms : 0), EasingFunction = ease ?? new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(a, target); Storyboard.SetTargetProperty(a, property); sb.Children.Add(a);
    }
}
