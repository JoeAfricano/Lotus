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
}
