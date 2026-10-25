using Lotus.Core.Games;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Lotus.App.Controls;

public sealed partial class GamesPanel : UserControl
{
    public GamesPanel()
    {
        InitializeComponent();
        // The first visit scans launchers in the background; later visits reuse the result.
        Loaded += async (_, _) => { if (DataContext is GamesViewModel vm) await vm.EnsureLoadedAsync(); };
    }

    private async void OnAddGame(object sender, RoutedEventArgs e)
    {
        if (DataContext is not GamesViewModel vm) return;
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder, ViewMode = PickerViewMode.List };
        picker.FileTypeFilter.Add(".exe");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var file = await picker.PickSingleFileAsync();
        if (file is not null) await vm.AddManualAsync(file.Path);
    }

    // Scans every platform again; installed games that were removed from the list can be put back.
    private async void OnRescan(object sender, RoutedEventArgs e)
    {
        if (DataContext is not GamesViewModel vm) return;
        var removed = await vm.RescanAsync();
        if (removed.Count == 0) return;
        var list = new ListView { SelectionMode = ListViewSelectionMode.Multiple, MaxHeight = 380, ItemsSource = removed };
        list.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><StackPanel Padding=\"0,6\"><TextBlock Text=\"{Binding Name}\" FontWeight=\"SemiBold\"/>"
            + "<TextBlock Text=\"{Binding Store}\" FontSize=\"11.5\" Opacity=\"0.7\"/></StackPanel></DataTemplate>");
        list.Loaded += (_, _) => list.SelectAll();
        var content = new StackPanel { Spacing = 10, MinWidth = 420 };
        content.Children.Add(new TextBlock { Text = vm.Text["GamesRestoreHint"], TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
        content.Children.Add(list);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = vm.Text["GamesRestoreTitle"], Content = content, RequestedTheme = ElementTheme.Dark,
            PrimaryButtonText = vm.Text["GamesRestoreSelected"], CloseButtonText = vm.Text["UxCancel"], DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && list.SelectedItems.Count > 0)
            vm.Restore(list.SelectedItems.Cast<GameEntry>().ToList());
    }

    // Recommendation first; "Aplicar no jogo" only when a recipe can edit the settings safely.
    private async void OnOptimizeSettings(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GameCard card } || DataContext is not GamesViewModel vm) return;
        var analysis = await vm.AnalyzeSettingsAsync(card.Game);
        if (analysis is null) return;
        var body = new StackPanel { Spacing = 8, MaxWidth = 480 };
        body.Children.Add(new TextBlock { Text = analysis.Hardware, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        foreach (var line in analysis.Lines)
            body.Children.Add(new TextBlock { Text = "•  " + line, TextWrapping = TextWrapping.Wrap });
        var support = analysis.Support is { CanApply: true } s ? vm.Text.Format("GsSupportApply", s.EngineLabel)
            : analysis.Support is { } u ? $"{u.EngineLabel}: {vm.Text[u.Detail ?? "GsSupportManual"]}" : vm.Text["GsSupportManual"];
        body.Children.Add(new TextBlock { Text = support, TextWrapping = TextWrapping.Wrap, Opacity = 0.8, FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0) });
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = vm.Text.Format("GsTitle", card.Name), RequestedTheme = ElementTheme.Dark,
            Content = new ScrollViewer { Content = body, MaxHeight = 460 }, CloseButtonText = vm.Text["GsClose"], DefaultButton = ContentDialogButton.Close
        };
        // A measured adjustment takes precedence over the hardware-based starting point.
        var pending = analysis.PendingStep is { } step && analysis.Support is { CanApply: true };
        if (pending) body.Children.Insert(1, new TextBlock { Text = vm.Text.Format("GsStepPending", vm.Text[analysis.PendingStep > 0 ? "GsStepUp" : "GsStepDown"]), TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        if (analysis.Support is { CanApply: true }) dialog.PrimaryButtonText = vm.Text[pending ? "GsStepApply" : "GsApply"];
        if (analysis.HasOriginal) dialog.SecondaryButtonText = vm.Text["GsRestore"];
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary) { if (pending) await vm.ApplyStepAsync(card.Game); else await vm.ApplySettingsAsync(card.Game, analysis.Recommendation); }
        else if (result == ContentDialogResult.Secondary) await vm.RestoreSettingsAsync(card.Game);
    }

    // Measure while the game runs, then explain the bottleneck and offer one quality step.
    private async void OnMeasureGame(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GameCard card } || DataContext is not GamesViewModel vm || !vm.CanMeasure) return;
        if (!vm.IsGameRunning(card.Game))
        {
            await Show(vm.Text["GsMeasureMenu"], new TextBlock { Text = vm.Text.Format("GsMeasureNotRunning", card.Name), TextWrapping = TextWrapping.Wrap, MaxWidth = 440 }, null, vm.Text["GsClose"]);
            return;
        }
        var start = await Show(vm.Text.Format("GsMeasureConfirmTitle", card.Name), new TextBlock { Text = vm.Text["GsMeasureConfirmBody"], TextWrapping = TextWrapping.Wrap, MaxWidth = 460 }, vm.Text["GsMeasureStart"], vm.Text["UxCancel"]);
        if (!start) return;
        var result = await vm.MeasureAsync(card.Game);
        if (result is null) return;
        var body = new StackPanel { Spacing = 10, MaxWidth = 480 };
        body.Children.Add(new TextBlock { Text = result.Summary, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = result.DecisionText, TextWrapping = TextWrapping.Wrap });
        if (result.CanApplyStep) body.Children.Add(new TextBlock { Text = vm.Text.Format("GsStepPending", vm.Text[result.Decision.Step > 0 ? "GsStepUp" : "GsStepDown"]), TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
        if (await Show(vm.Text["GsMeasureResultTitle"], body, result.CanApplyStep ? vm.Text["GsStepApply"] : null, vm.Text["GsClose"]))
            await vm.ApplyStepAsync(card.Game);
    }

    // Pre-compilation runs through the user's SCSKiller, with the game closed.
    private async void OnPrecompile(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GameCard card } || DataContext is not GamesViewModel vm) return;
        if (vm.IsGameRunning(card.Game))
        {
            await Show(vm.Text["ScsPrecompile"], new TextBlock { Text = vm.Text.Format("GsCloseGame", card.Name), TextWrapping = TextWrapping.Wrap, MaxWidth = 440 }, null, vm.Text["GsClose"]);
            return;
        }
        if (await Show(vm.Text.Format("ScsConfirmTitle", card.Name), new TextBlock { Text = vm.Text["ScsConfirmBody"], TextWrapping = TextWrapping.Wrap, MaxWidth = 460 }, vm.Text["ScsPrecompile"], vm.Text["UxCancel"]))
            await vm.PrecompileAsync(card);
    }

    // Opens only the official SCSKiller releases page; Lotus never downloads it.
    private async void OnScsOffer(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri(GamesViewModel.ScsKillerReleases));

    private async Task<bool> Show(string title, UIElement content, string? primary, string close)
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = content, CloseButtonText = close, RequestedTheme = ElementTheme.Dark, DefaultButton = ContentDialogButton.Close };
        if (primary is not null) dialog.PrimaryButtonText = primary;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async void OnRemoveGame(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GameCard card } || DataContext is not GamesViewModel vm) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = vm.Text.Format("GamesRemoveConfirmTitle", card.Name), RequestedTheme = ElementTheme.Dark,
            Content = new TextBlock { Text = vm.Text["GamesRemoveConfirmBody"], TextWrapping = TextWrapping.Wrap, MaxWidth = 420 },
            PrimaryButtonText = vm.Text["GamesRemoveConfirm"], CloseButtonText = vm.Text["UxCancel"], DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) card.Remove.Execute(card);
    }
}
