using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App.Controls;

public sealed partial class PowerProfilesPanel : UserControl
{
    public PowerProfilesPanel() => InitializeComponent();

    // Three columns on wide windows, one stacked column on narrow ones.
    private void OnCardsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < 820;
        foreach (var (card, index) in new FrameworkElement[] { GameCard, DailyCard, CustomCard }.Select((c, i) => (c, i)))
        {
            Grid.SetColumn(card, stacked ? 0 : index); Grid.SetRow(card, stacked ? index : 0); Grid.SetColumnSpan(card, stacked ? 3 : 1);
        }
    }

    // Actions move below the change list when there is no room for a side column.
    private void OnChangesSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < 700;
        ChangesGrid.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(260);
        Grid.SetColumn(ActionsColumn, stacked ? 0 : 1); Grid.SetRow(ActionsColumn, stacked ? 1 : 0);
    }
}
