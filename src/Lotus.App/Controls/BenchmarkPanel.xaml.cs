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
}
