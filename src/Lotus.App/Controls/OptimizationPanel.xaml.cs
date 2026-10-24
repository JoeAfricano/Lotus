using Lotus.Core.Optimization;
using Microsoft.UI.Xaml.Controls;
namespace Lotus.App.Controls;
public sealed partial class OptimizationPanel : UserControl
{
    public OptimizationPanel()
    {
        InitializeComponent();
        Loaded += async (_, _) => { if (DataContext is OptimizationViewModel model) await model.RefreshAsync(); };
    }
}
