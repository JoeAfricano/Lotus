using Lotus.Core.Diary;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Lotus.App.Controls;

// "Meu diário": the player's profile, progress, badges and every logged game. A click on a game edits it.
public sealed partial class DiaryPanel : UserControl
{
    public event EventHandler? GameListRequested;
    private DiaryViewModel? Vm => DataContext as DiaryViewModel;
    public DiaryPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => Render();
        DataContextChanged += (_, _) => { if (Vm is { } vm) { vm.PropertyChanged -= OnVm; vm.PropertyChanged += OnVm; Render(); } };
        Unloaded += (_, _) => { if (Vm is { } vm) vm.PropertyChanged -= OnVm; };
    }

    private void OnVm(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DiaryViewModel.LevelPercent) or nameof(DiaryViewModel.Progress)) DispatcherQueue.TryEnqueue(Render);
    }

    private void Render()
    {
        if (Vm is not { } vm) return;
        Initial.Text = vm.Name.Length > 0 ? char.ToUpperInvariant(vm.Name[0]).ToString() : "P";
        Picture.Visibility = Visibility.Collapsed;
        if (vm.Avatar is { Length: > 0 } a && File.Exists(a)) { PictureBrush.ImageSource = new BitmapImage(new Uri(a)); Picture.Visibility = Visibility.Visible; }
        // XP bar glides to its value.
        var target = vm.LevelPercent; var from = XpBar.Value; var start = DateTime.UtcNow;
        if (!Motion.Enabled) { XpBar.Value = target; }
        else
        {
            var timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(16);
            timer.Tick += (s, _) => { var p = Math.Min(1, (DateTime.UtcNow - start).TotalMilliseconds / 700); XpBar.Value = from + (target - from) * (1 - Math.Pow(1 - p, 3)); if (p >= 1) s.Stop(); };
            timer.Start();
        }
        PaintTabs();
    }

    // Selected tab: gold underline and full text; others muted.
    private void PaintTabs()
    {
        if (Vm is not { } vm) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            for (var i = 0; i < TabBar.Items.Count; i++)
                if (TabBar.ContainerFromIndex(i) is ContentPresenter { Content: DiaryTab tab } cp && Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(cp) > 0 && Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(cp, 0) is Button b)
                {
                    var on = tab.Id == vm.Tab;
                    b.BorderBrush = on ? (Brush)Application.Current.Resources["GoldGradientBrush"] : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    b.Opacity = on ? 1 : 0.65;
                }
        });
    }

    private void OnTab(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || sender is not FrameworkElement { Tag: string id } f) return;
        vm.Tab = id; Motion.Pop(f); UiSound.Play(UiSound.Cue.Toggle); PaintTabs();
    }
    private void OnSearch(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) { if (Vm is { } vm && args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) vm.Query = sender.Text; }
    private void OnEntry(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { DataContext: DiaryCard card }) MainWindow.Shell?.OpenDiaryEditor(card.Entry); }
    private void OnOpenGameList(object sender, RoutedEventArgs e) => GameListRequested?.Invoke(this, EventArgs.Empty);
}
