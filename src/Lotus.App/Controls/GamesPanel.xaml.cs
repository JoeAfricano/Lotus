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
