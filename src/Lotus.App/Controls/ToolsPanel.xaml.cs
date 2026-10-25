using Lotus.Core.Tools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App.Controls;

public sealed partial class ToolsPanel : UserControl
{
    public ToolsPanel()
    {
        InitializeComponent();
        Loaded += async (_, _) => { if (DataContext is ToolsViewModel vm) await vm.RefreshAsync(); };
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) { if (DataContext is ToolsViewModel vm) await vm.RefreshAsync(); }

    // Always asks: lists the chosen apps and explains they close normally (unsaved work prompts stay with them).
    private async void OnCloseApps(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ToolsViewModel vm) return;
        var chosen = vm.Apps.Where(a => a.Selected).Select(a => a.Name).ToList();
        if (chosen.Count == 0) { await Confirm(vm.Text["ToolMemTitle"], vm.Text["ToolMemNoneChosen"], null, vm); return; }
        if (await Confirm(vm.Text["ToolMemConfirmTitle"], vm.Text.Format("ToolMemConfirmBody", string.Join(", ", chosen)), vm.Text["ToolMemClose"], vm))
            await vm.CloseSelectedAsync();
    }
    private async void OnCleanTemp(object sender, RoutedEventArgs e)
    {
        if (DataContext is ToolsViewModel vm && await Confirm(vm.Text["ToolTempTitle"], vm.Text["ToolTempConfirm"], vm.Text["ToolTempClean"], vm))
            await vm.RecycleTempAsync();
    }
    private async void OnTestNet(object sender, RoutedEventArgs e) { if (DataContext is ToolsViewModel vm) await vm.TestConnectionAsync(); }
    private async void OnTestDns(object sender, RoutedEventArgs e) { if (DataContext is ToolsViewModel vm) await vm.BenchmarkDnsAsync(); }
    private async void OnApplyDns(object sender, RoutedEventArgs e)
    {
        if (DataContext is ToolsViewModel vm && await Confirm(vm.RecommendedDnsLabel, vm.Text["ToolDnsConfirm"], vm.Text["FeatConfirm"], vm))
            await vm.ApplyDnsAsync();
    }
    private async void OnRestoreDns(object sender, RoutedEventArgs e)
    {
        if (DataContext is ToolsViewModel vm && await Confirm(vm.Text["ToolDnsRestore"], vm.Text["ToolDnsRestoreConfirm"], vm.Text["FeatConfirm"], vm))
            await vm.RestoreDnsAsync();
    }
    // Opens only the GPU maker's official driver page; Lotus never downloads or installs drivers.
    private async void OnOpenDriverSite(object sender, RoutedEventArgs e)
    {
        if (DataContext is ToolsViewModel { DriverUrl: { } url } && url.StartsWith("https://", StringComparison.Ordinal))
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
    }

    private async Task<bool> Confirm(string title, string body, string? primary, ToolsViewModel vm)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = title, RequestedTheme = ElementTheme.Dark, DefaultButton = ContentDialogButton.Close,
            Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 }, CloseButtonText = vm.Text[primary is null ? "GsClose" : "DlgCancel"]
        };
        if (primary is not null) dialog.PrimaryButtonText = primary;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
