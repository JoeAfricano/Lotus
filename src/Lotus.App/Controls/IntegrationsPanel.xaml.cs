using Lotus.Core.Integrations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App.Controls;

public sealed partial class IntegrationsPanel : UserControl
{
    public IntegrationsPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => (DataContext as IntegrationsViewModel)?.Refresh();
    }

    private async void OnApplyOverlay(object sender, RoutedEventArgs e)
    {
        if (DataContext is IntegrationsViewModel vm && await Confirm(vm.Text["IntOverlayConfirmTitle"], vm.Text["IntOverlayConfirmBody"], vm))
            await vm.ApplyOverlayAsync();
    }
    private async void OnRestoreOverlay(object sender, RoutedEventArgs e)
    {
        if (DataContext is IntegrationsViewModel vm && await Confirm(vm.Text["IntRestoreConfirmTitle"], vm.Text["IntRestoreConfirmBody"], vm))
            await vm.RestoreOverlayAsync();
    }
    // Opens only the official SCSKiller releases page.
    private async void OnScsLink(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri(Lotus.Core.Games.GamesViewModel.ScsKillerReleases));

    private async Task<bool> Confirm(string title, string body, IntegrationsViewModel vm)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = title, RequestedTheme = ElementTheme.Dark, DefaultButton = ContentDialogButton.Close,
            Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 },
            PrimaryButtonText = vm.Text["FeatConfirm"], CloseButtonText = vm.Text["UxCancel"]
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
