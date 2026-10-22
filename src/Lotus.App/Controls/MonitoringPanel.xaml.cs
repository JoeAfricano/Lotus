using Lotus.Core.Telemetry;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App.Controls;

public sealed partial class MonitoringPanel : UserControl
{
    private MonitoringViewModel? viewModel;
    public MonitoringPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => { viewModel = DataContext as MonitoringViewModel; if (viewModel is not null) viewModel.Updated += OnUpdated; Draw(); };
        Unloaded += (_, _) => { if (viewModel is not null) viewModel.Updated -= OnUpdated; viewModel = null; };
    }
    private void OnUpdated(object? sender, EventArgs args) => Draw();
    private void Draw()
    {
        if (viewModel is null) return;
        CpuChart.SetPoints(viewModel.History.Points, cpu: true);
        MemoryChart.SetPoints(viewModel.History.Points, cpu: false);
    }
    private void OnCardsSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var stacked = args.NewSize.Width < 900;
        Grid.SetColumnSpan(CpuCard, stacked ? 2 : 1);
        Grid.SetColumn(MemoryCard, stacked ? 0 : 1);
        Grid.SetRow(MemoryCard, stacked ? 1 : 0);
        Grid.SetColumnSpan(MemoryCard, stacked ? 2 : 1);
    }
}
