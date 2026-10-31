using Lotus.Core.Backup;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lotus.App.Controls;

// Settings › Backup. Saving writes a file the player picks; restoring shows what the backup holds, asks, keeps it
// for the next start and restarts Lotus (the data is applied before anything loads).
public sealed partial class BackupPanel : UserControl
{
    private BackupViewModel? Vm => DataContext as BackupViewModel;
    public BackupPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (Vm is not { } vm) return;
            vm.PropertyChanged -= OnVm; vm.PropertyChanged += OnVm;
            vm.Notify();
            syncing = true;
            ScheduleBox.Items.Clear();
            foreach (var key in new[] { "BkScheduleManual", "BkScheduleWeekly", "BkScheduleMonthly" }) ScheduleBox.Items.Add(vm.Text[key]);
            ScheduleBox.SelectedIndex = vm.ScheduleIndex;
            syncing = false;
            _ = vm.RefreshCloudAsync();
        };
    }
    private void OnVm(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BackupViewModel.IsError) && Vm is { } vm)
            Status.Foreground = (Brush)Application.Current.Resources[vm.IsError ? "ErrorBrush" : "SuccessBrush"];
    }

    private static nint Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Shell);

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = vm.SuggestedName, SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary };
        picker.FileTypeChoices.Add(vm.Text["BkFileType"], [vm.Extension]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        await using (var stream = File.Create(file.Path)) vm.Save(stream);
        UiSound.Play(vm.IsError ? UiSound.Cue.Fail : UiSound.Cue.Done);
    }

    private bool syncing;
    private void OnSchedule(object sender, SelectionChangedEventArgs e) { if (!syncing && Vm is { } vm && ScheduleBox.SelectedIndex >= 0) vm.ScheduleIndex = ScheduleBox.SelectedIndex; }

    private async void OnLoadCloud(object sender, RoutedEventArgs e) { if (Vm is { } vm) await vm.LoadFromCloudAsync(); }

    private async void OnDeleteCloud(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || sender is not FrameworkElement { DataContext: CloudBackupItem item }) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ThemePalette.Element, Title = vm.Text["BkCloudDeleteTitle"],
            Content = new TextBlock { Text = vm.Text.Format("BkCloudDeleteBody", item.DateText), TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = vm.Text["SpDelete"], CloseButtonText = vm.Text["DlgCancel"], DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await vm.DeleteCloudAsync(item.Id);
    }

    private async void OnImportClient(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var picker = new Windows.Storage.Pickers.FileOpenPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads };
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file is not null) UiSound.Play(vm.ImportClient(file.Path) ? UiSound.Cue.Done : UiSound.Cue.Fail);
    }

    private async void OnRestoreFile(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var picker = new Windows.Storage.Pickers.FileOpenPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(vm.Extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        using var data = new MemoryStream(await File.ReadAllBytesAsync(file.Path));
        await Restore(vm, data);
    }

    private async void OnRestoreCloud(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || sender is not FrameworkElement { DataContext: CloudBackupItem item }) return;
        using var data = await vm.DownloadAsync(item.Id);
        if (data is not null) await Restore(vm, data);
    }

    // Preview, confirmation, then restart into the restored data.
    private async Task Restore(BackupViewModel vm, MemoryStream data)
    {
        data.Position = 0;
        if (vm.Inspect(data) is not { } manifest) { UiSound.Play(UiSound.Cue.Fail); return; }
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ThemePalette.Element, Title = vm.Text["BkRestoreTitle"],
            Content = new TextBlock { Text = vm.Describe(manifest) + "\n\n" + vm.Text["BkRestoreWarning"], TextWrapping = TextWrapping.Wrap, MaxWidth = 460 },
            PrimaryButtonText = vm.Text["BkRestoreConfirm"], CloseButtonText = vm.Text["DyCancel"], DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        data.Position = 0;
        if (vm.Stage(data)) MainWindow.Shell?.Restart();
    }

    private async void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        Directory.CreateDirectory(vm.BackupFolder);
        await Windows.System.Launcher.LaunchFolderPathAsync(vm.BackupFolder);
    }
    private async void OnConnect(object sender, RoutedEventArgs e) { if (Vm is { } vm) await vm.ConnectAsync(); }
    private async void OnUpload(object sender, RoutedEventArgs e) { if (Vm is { } vm) { await vm.UploadAsync(); UiSound.Play(vm.IsError ? UiSound.Cue.Fail : UiSound.Cue.Done); } }
    private async void OnDisconnect(object sender, RoutedEventArgs e) { if (Vm is { } vm) await vm.DisconnectAsync(); }
}
