using System.ComponentModel;
using Lotus.Core.Benchmark;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App.Controls;

public sealed partial class BenchmarkPanel : UserControl
{
    private BenchmarkViewModel? model;
    public BenchmarkPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => { if (model is not null) model.PropertyChanged -= Changed; model = null; };
    }
    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (model is not null) model.PropertyChanged -= Changed;
        model = DataContext as BenchmarkViewModel;
        if (model is null) return;
        model.PropertyChanged += Changed;
        Chart.SetData(model.SelectedSession?.Data?.Frames);
        await model.RefreshAsync();
    }
    private void Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(BenchmarkViewModel.SelectedSession)) Chart.SetData(model?.SelectedSession?.Data?.Frames);
    }

    // Side by side when there is room for both columns; otherwise the result goes below the preparation.
    private void OnColumnsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < 900;
        Columns.ColumnDefinitions[0].Width = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(416);
        Columns.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(ResultCard, stacked ? 0 : 1); Grid.SetRow(ResultCard, stacked ? 1 : 0);
        Columns.ColumnSpacing = stacked ? 0 : 20;
    }
}
