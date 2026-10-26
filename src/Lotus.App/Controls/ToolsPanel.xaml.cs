using Lotus.Core.Tools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lotus.App.Controls;

public sealed partial class ToolsPanel : UserControl
{
    public ToolsPanel()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is not ToolsViewModel vm) return;
            BuildTiles(vm);
            await vm.RefreshAsync();
        };
    }

    // ---- Tiles: one square per tool; clicking opens that tool alone, with a back button ----
    private readonly Dictionary<string, FrameworkElement> cards = [];
    private void BuildTiles(ToolsViewModel vm)
    {
        if (Tiles.Items.Count > 0) return;
        cards["mem"] = MemCard; cards["temp"] = TempCard; cards["unin"] = UninCard; cards["net"] = NetCard; cards["disk"] = DiskCard; cards["drivers"] = DriverCard;
        foreach (var (id, glyph, title, status) in new[]
        {
            ("mem", "", "ToolMemTitle", "MemoryStatus"), ("temp", "", "ToolTempTitle", "TempStatus"), ("unin", "", "UninTitle", "Uninstall.Summary"),
            ("net", "", "ToolNetTile", "NetTileStatus"), ("disk", "", "ToolDiskTitle", "DiskTileStatus"), ("drivers", "", "ToolDriverTitle", "DriverStatus")
        })
        {
            if (id == "unin" && vm.Uninstall is null) continue;
            var badge = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(14), Background = (Brush)Application.Current.Resources["AccentGradientBrush"], HorizontalAlignment = HorizontalAlignment.Left,
                Child = new FontIcon { Glyph = glyph, FontSize = 20, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) } };
            var name = new TextBlock { Text = vm.Text[title], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 15, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
            var line = new TextBlock { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis };
            line.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(status) });
            var content = new StackPanel { Spacing = 10 }; content.Children.Add(badge); content.Children.Add(name); content.Children.Add(line);
            var tile = new Button
            {
                Width = 212, Height = 210, Padding = new Thickness(18), CornerRadius = new CornerRadius(18), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
                Background = (Brush)Application.Current.Resources["CardBrush"], BorderBrush = (Brush)Application.Current.Resources["CardBorderBrush"], BorderThickness = new Thickness(1), Content = content, Tag = id
            };
            Motion.SetHover(tile, 1.04); Motion.SetPress(tile, true);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(tile, "tools-tile-" + id);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tile, vm.Text[title]);
            tile.Click += (_, _) => Open(id);
            Tiles.Items.Add(tile);
        }
    }
    private void Open(string id)
    {
        Tiles.Visibility = Visibility.Collapsed; BackButton.Visibility = Visibility.Visible;
        foreach (var (key, card) in cards) card.Visibility = key == id ? Visibility.Visible : Visibility.Collapsed;
        Motion.EnterPage(cards[id]);
    }
    private void OnBack(object sender, RoutedEventArgs e)
    {
        foreach (var card in cards.Values) card.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Collapsed; Tiles.Visibility = Visibility.Visible;
        Motion.EnterPage(Tiles);
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
    private async void OnCheckDrivers(object sender, RoutedEventArgs e) { if (DataContext is ToolsViewModel vm) await vm.CheckDriversAsync(); }
    // Only NVIDIA's own driver page (validated in the service) and Windows' optional updates page.
    private async void OnOpenGpuUpdate(object sender, RoutedEventArgs e)
    {
        if (DataContext is ToolsViewModel { GpuUpdateUrl: { } url } && Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https" && u.Host.EndsWith("nvidia.com", StringComparison.OrdinalIgnoreCase))
            await Windows.System.Launcher.LaunchUriAsync(u);
    }
    private async void OnOpenWindowsUpdate(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:windowsupdate-optionalupdates"));
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
            Content = RichText.Block(body), CloseButtonText = vm.Text[primary is null ? "GsClose" : "DlgCancel"]
        };
        if (primary is not null) dialog.PrimaryButtonText = primary;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
