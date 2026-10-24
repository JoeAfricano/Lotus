using Lotus.Core.Telemetry;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App.Controls;
public sealed partial class GpuPanel : UserControl
{
    private GpuViewModel? viewModel;
    public GpuPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => { viewModel = DataContext as GpuViewModel; if (viewModel is not null) viewModel.Updated += OnUpdated; Draw(); };
        Unloaded += (_, _) => { if (viewModel is not null) viewModel.Updated -= OnUpdated; viewModel = null; };
    }
    private void OnUpdated(object? sender, EventArgs args) => Draw();
    private void Draw()
    {
        if (viewModel is null) return;
        GpuChart.SetPoints(viewModel.UtilizationHistory); VramChart.SetPoints(viewModel.MemoryHistory);
    }
    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var stacked = args.NewSize.Width < 900;
        Grid.SetColumnSpan(GpuCard, stacked ? 2 : 1);
        Grid.SetColumn(VramCard, stacked ? 0 : 1); Grid.SetRow(VramCard, stacked ? 1 : 0);
        Grid.SetColumnSpan(VramCard, stacked ? 2 : 1);
    }
}
