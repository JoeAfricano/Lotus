using Lotus.Core.Tools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App.Controls;

public sealed partial class UninstallerCard : UserControl
{
    private bool loaded;
    public UninstallerCard()
    {
        InitializeComponent();
        Loaded += async (_, _) => { if (!loaded && DataContext is UninstallerViewModel vm) { loaded = true; await vm.LoadAsync(); } };
    }

    // Always asks first; then the program's own uninstaller takes over (it may show its own windows).
    private async void OnUninstall(object sender, RoutedEventArgs e)
    {
        if (DataContext is not UninstallerViewModel vm || sender is not FrameworkElement { Tag: AppRowView row }) return;
        if (!await Confirm(vm.Text.Format("UninConfirmTitle", row.App.Name), vm.Text["UninConfirmBody"], vm.Text["UninButton"], vm)) return;
        await vm.UninstallAsync(row.App);
    }

    private void OnSelectionClick(object sender, RoutedEventArgs e) => (DataContext as UninstallerViewModel)?.SelectionChanged();
    private async void OnUninstallSelected(object sender, RoutedEventArgs e)
    {
        if (DataContext is not UninstallerViewModel vm || vm.SelectedCount == 0) return;
        var names = string.Join(", ", vm.Apps.Where(a => a.Selected).Select(a => a.App.Name));
        if (!await Confirm(vm.Text.Format("UninBatchConfirmTitle", vm.SelectedCount), vm.Text.Format("UninBatchConfirmBody", names), vm.Text["UninButton"], vm)) return;
        await vm.UninstallSelectedAsync();
    }

    private async void OnRecycleLeftovers(object sender, RoutedEventArgs e)
    {
        if (DataContext is not UninstallerViewModel vm) return;
        var count = vm.Leftovers.Count(l => l.Selected && l.CanRecycle);
        if (count == 0) return;
        if (await Confirm(vm.Text["UninRecycle"], vm.Text.Format("UninRecycleConfirm", count), vm.Text["ToolTempClean"], vm))
            await vm.RecycleLeftoversAsync();
    }

    private async Task<bool> Confirm(string title, string body, string primary, UninstallerViewModel vm)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = title, RequestedTheme = ElementTheme.Dark, DefaultButton = ContentDialogButton.Close,
            Content = RichText.Block(body), PrimaryButtonText = primary, CloseButtonText = vm.Text["DlgCancel"]
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
