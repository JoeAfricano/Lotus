using Lotus.Core.Saves;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Lotus.App.Controls;

// One game's saves on the "Backup de saves" page. Nothing changes without the player's confirmation (turning on,
// restoring, deleting); the view model does the disk work off the UI thread.
public sealed partial class SaveManagerView : UserControl
{
    private GameSavesViewModel? vm;
    private bool syncing;

    public SaveManagerView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (vm is not null) { vm.PropertyChanged -= OnVm; vm.Backups.CollectionChanged -= OnBackups; }
            vm = DataContext as GameSavesViewModel;
            if (vm is null) return;
            vm.PropertyChanged += OnVm; vm.Backups.CollectionChanged += OnBackups;
            syncing = true;
            KeepBox.Items.Clear();
            foreach (var k in SaveProtection.KeepChoices) KeepBox.Items.Add(new ComboBoxItem { Content = vm.Text.Format("SpKeepItem", k), Tag = k });
            syncing = false;
            Sync();
        };
        Unloaded += (_, _) => { if (vm is not null) { vm.PropertyChanged -= OnVm; vm.Backups.CollectionChanged -= OnBackups; } };
    }

    private void OnVm(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Sync();
    private void OnBackups(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Sync();
    private void Sync()
    {
        if (vm is null) return;
        syncing = true;
        AutoSwitch.IsOn = vm.Auto;
        KeepBox.SelectedItem = KeepBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is int k && k == vm.Keep);
        syncing = false;
        NoBackups.Visibility = vm.Backups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = vm.Backups.Count == 0 ? "" : vm.Backups.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
        StateIcon.Glyph = vm.State switch
        {
            ProtectionState.Active => "",
            ProtectionState.Attention or ProtectionState.Unavailable => "",
            ProtectionState.Unsupported => "",
            ProtectionState.NotFound => "",
            _ => "",
        };
    }

    private async Task<bool> Confirm(string title, UIElement content, string yes, bool allowed = true)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ThemePalette.Element, Title = title, Content = content,
            PrimaryButtonText = yes, CloseButtonText = vm!.Text["DlgCancel"], DefaultButton = ContentDialogButton.Close, IsPrimaryButtonEnabled = allowed,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    private static TextBlock Line(string text, double opacity = 1, double size = 14) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = opacity, FontSize = size };

    private StackPanel EnableBody(string folder)
    {
        var body = new StackPanel { Spacing = 8, MaxWidth = 460 };
        body.Children.Add(Line(vm!.Text["SpEnableBody"]));
        body.Children.Add(Line(folder, 0.8, 12.5));
        return body;
    }

    private async void OnEnable(object sender, RoutedEventArgs e)
    {
        if (vm is not { } v) return;
        if (await Confirm(v.Text.Format("SpEnableTitle", v.Game.Name), EnableBody(v.FolderPath), v.Text["SpEnable"])) await v.EnableAsync();
    }

    private async void OnPick(object sender, RoutedEventArgs e)
    {
        if (vm is not { } v) return;
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        if (await Confirm(v.Text.Format("SpEnableTitle", v.Game.Name), EnableBody(folder.Path), v.Text["SpEnable"])) await v.UseFolderAsync(folder.Path);
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if (vm is not { FolderPath.Length: > 0 } v || !Directory.Exists(v.FolderPath)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { ArgumentList = { v.FolderPath } })?.Dispose(); }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Open save folder"); }
    }

    private async void OnBackup(object sender, RoutedEventArgs e) { if (vm is { } v) await v.BackupNowAsync(); }
    private async void OnOff(object sender, RoutedEventArgs e) { if (vm is { } v) await v.DisableAsync(); }
    private async void OnRecover(object sender, RoutedEventArgs e) { if (vm is { } v) await v.RecoverAsync(); }
    private void OnAuto(object sender, RoutedEventArgs e) { if (!syncing && vm is { } v) v.SetAuto(AutoSwitch.IsOn); }
    private void OnKeep(object sender, SelectionChangedEventArgs e) { if (!syncing && vm is { } v && KeepBox.SelectedItem is ComboBoxItem { Tag: int k }) v.SetKeep(k); }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (vm is not { } v || (sender as FrameworkElement)?.Tag is not string id) return;
        if (await Confirm(v.Text["SpDeleteTitle"], Line(v.Text["SpDeleteBody"]), v.Text["SpDelete"])) await v.DeleteAsync(id);
    }

    // The validated plan is shown file by file; the game must be closed; then the vault restores with a safety copy.
    private async void OnRestore(object sender, RoutedEventArgs e)
    {
        if (vm is not { } v || (sender as FrameworkElement)?.Tag is not string id) return;
        var t = v.Text;
        var plan = await v.PlanAsync(id);
        if (plan is null) { await Confirm(t["SpTitle"], Line(t["SpFailInvalid"]), t["SpRestoreConfirm"], allowed: false); await v.LoadAsync(); return; }
        var running = await Task.Run(v.GameIsRunning);
        var body = new StackPanel { Spacing = 8, MaxWidth = 480 };
        body.Children.Add(Line(t["SpRestoreBody"]));
        void Section(string key, IReadOnlyList<string> files)
        {
            if (files.Count == 0) return;
            body.Children.Add(new TextBlock { Text = t.Format(key, files.Count), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
            foreach (var f in files.Take(6)) body.Children.Add(Line(f, 0.8, 12.5));
            if (files.Count > 6) body.Children.Add(Line(t.Format("SpRestoreMore", files.Count - 6), 0.6, 12.5));
        }
        Section("SpRestoreReplace", plan.Replace); Section("SpRestoreCreate", plan.Create); Section("SpRestoreKeep", plan.KeepUntouched);
        if (v.CloudSynced) body.Children.Add(Line(t["SpCloudNote"], 0.85, 12.5));
        if (running) body.Children.Add(new TextBlock { Text = t["SpRestoreRunning"], TextWrapping = TextWrapping.Wrap, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["WarningBrush"] });
        var when = plan.Snapshot.Created.ToLocalTime().ToString("g", System.Globalization.CultureInfo.GetCultureInfo(t.Language));
        if (!await Confirm(t.Format("SpRestoreTitle", when), new ScrollViewer { Content = body, MaxHeight = 420 }, t["SpRestoreConfirm"], allowed: !running)) return;
        var result = await v.RestoreAsync(id);
        Serilog.Log.Information("Save restore {Game}: {Restored} {Failure}", v.Game.Name, result.Restored, result.Failure);
    }
}
