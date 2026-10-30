using Lotus.Core.Diary;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App.Controls;

// "Estatísticas" tab of the diary: headline numbers and simple one-hue charts built from the player's entries.
public sealed partial class DiaryStatsView : UserControl
{
    public event EventHandler<int>? RecapRequested;
    private DiaryStatsViewModel? Vm => DataContext as DiaryStatsViewModel;
    private bool syncing;
    public DiaryStatsView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => Arrange(e.NewSize.Width);
        DataContextChanged += (_, _) => { if (Vm is { } vm) { vm.PropertyChanged -= OnVm; vm.PropertyChanged += OnVm; SyncYear(); } };
        Loaded += (_, _) => SyncYear();
    }
    private void OnVm(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName is nameof(DiaryStatsViewModel.Years)) DispatcherQueue.TryEnqueue(SyncYear); }
    private void SyncYear() { if (Vm is { } vm) { syncing = true; YearBox.SelectedItem = vm.Year; syncing = false; } }
    private void OnYear(object sender, SelectionChangedEventArgs e) { if (!syncing && Vm is { } vm && YearBox.SelectedItem is int y) vm.Year = y; }
    private void OnRecap(object sender, RoutedEventArgs e) { if (Vm is { } vm) RecapRequested?.Invoke(this, vm.Year); }

    // Two charts side by side; stacked on narrow windows.
    private void Arrange(double width)
    {
        var narrow = width < 860;
        // Headline tiles: 6, 3 or 2 per row (never a lonely last tile).
        TilesLayout.MaximumRowsOrColumns = width >= 1400 ? 6 : width >= 520 ? 3 : 2;
        foreach (var pair in new[] { Pair1, Pair2, Pair3 })
        {
            pair.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            pair.ColumnSpacing = narrow ? 0 : 18;
            var second = pair.Children[1] as FrameworkElement;
            if (second is null) continue;
            Grid.SetColumn(second, narrow ? 0 : 1); Grid.SetRow(second, narrow ? 1 : 0);
        }
    }
}
