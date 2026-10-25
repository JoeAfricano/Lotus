using Lotus.Core.Integrations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App.Controls;

public sealed partial class RtssCard : UserControl
{
    public RtssCard()
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
    private async void OnActivateLayout(object sender, RoutedEventArgs e)
    {
        if (DataContext is IntegrationsViewModel vm && await Confirm(vm.Text["IntLayoutConfirmTitle"], vm.Text["IntLayoutConfirmBody"], vm))
            await vm.ActivateLayoutAsync();
    }
    private async void OnRestoreLayout(object sender, RoutedEventArgs e)
    {
        if (DataContext is IntegrationsViewModel vm && await Confirm(vm.Text["IntLayoutRestoreTitle"], vm.Text["IntRestoreConfirmBody"], vm))
            await vm.RestoreLayoutAsync();
    }
    // Opens the 3D preview window so RivaTuner shows the overlay on it.
    private void OnPreviewOverlay(object sender, RoutedEventArgs e) => (DataContext as IntegrationsViewModel)?.PreviewOverlay();

    // Saves Lotus.ovl (no admin) and opens its folder for manual import in OverlayEditor.
    private void OnExportLayout(object sender, RoutedEventArgs e)
    {
        if (DataContext is not IntegrationsViewModel vm || vm.ExportLayout() is not { } path) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", path } })?.Dispose();
    }

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
