using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App.Controls;

public sealed partial class PowerProfilesPanel : UserControl
{
    public PowerProfilesPanel()
    {
        InitializeComponent();
        // Loads power plans, saved profiles and journal history (read-only queries).
        Loaded += async (_, _) => { if (DataContext is Lotus.Core.Optimization.PowerProfilesViewModel model) await model.RefreshAsync(); };
    }

    // Opens only Windows Settings pages (ms-settings:); nothing is changed by Lotus.
    private async void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string uri } && uri.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
            await Windows.System.Launcher.LaunchUriAsync(new Uri(uri));
    }

    // Real feature actions always pass through an explicit confirmation dialog.
    private async void OnApplyFeature(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id } && DataContext is Lotus.Core.Optimization.PowerProfilesViewModel vm
            && await ConfirmAsync(vm.Text["FeatConfirmTitle"], vm.Text["FeatConfirmBody"], vm.Text))
            await vm.ApplyFeatureAsync(id, confirmed: true);
    }
    private async void OnUndoFeature(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id } && DataContext is Lotus.Core.Optimization.PowerProfilesViewModel vm
            && await ConfirmAsync(vm.Text["FeatConfirmUndoTitle"], vm.Text["FeatConfirmUndoBody"], vm.Text))
            await vm.UndoFeatureAsync(id, confirmed: true);
    }
    private async Task<bool> ConfirmAsync(string title, string body, Lotus.Core.ITextCatalog text)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = title, Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 },
            PrimaryButtonText = text["FeatConfirm"], CloseButtonText = text["UxCancel"], DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ElementTheme.Dark
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

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
