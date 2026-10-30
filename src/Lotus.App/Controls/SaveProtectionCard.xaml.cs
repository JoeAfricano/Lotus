using Lotus.Core.Catalog;
using Lotus.Core.Saves;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Lotus.App.Controls;

// "Proteção de progresso" on the page of an installed game. Nothing is read from disk until the card is opened, and
// nothing is changed without the player's confirmation (turning on, restoring, deleting).
public sealed partial class SaveProtectionCard : UserControl
{
    private GameDetailsViewModel? page;
    private bool syncing;
    private GameSavesViewModel? Vm => Box.DataContext as GameSavesViewModel;

    public SaveProtectionCard()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (page is not null) page.PropertyChanged -= OnPage;
            page = DataContext as GameDetailsViewModel;
            if (page is not null) page.PropertyChanged += OnPage;
            Build();
        };
        Unloaded += (_, _) => { if (page is not null) page.PropertyChanged -= OnPage; };
    }
    private void OnPage(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName is nameof(GameDetailsViewModel.Installed) or null or "") DispatcherQueue.TryEnqueue(Build); }

    private void Build()
    {
        if (Vm is { } old) old.PropertyChanged -= OnVm;
        if (page?.Installed is not { } game || MainWindow.Shell is not { } shell) { Visibility = Visibility.Collapsed; return; }
        var t = page.Text;
        var vm = new GameSavesViewModel(shell.Saves, game, t, shell.IsGameRunning);
        vm.PropertyChanged += OnVm;
        vm.Backups.CollectionChanged += (_, _) => Sync();
        Box.DataContext = vm;
        Box.IsExpanded = false;
        Title.Text = t["SpTitle"];
        FolderLabel.Text = t["SpFolderLabel"]; CloudNote.Text = t["SpCloudNote"]; WhereNote.Text = t["SpWhere"];
        OpenButton.Content = t["SpOpen"]; PickButton.Content = t["SpPickFolder"]; EnableButton.Content = t["SpEnable"]; RecoverButton.Content = t["SpRecover"];
        BackupButton.Content = t["SpBackupNow"]; OffButton.Content = t["SpTurnOff"]; AutoSwitch.Header = t["SpAuto"];
        KeepBox.Header = t["SpKeep"]; ManageLabel.Text = t["SpManage"]; NoBackups.Text = t["SpNoBackups"];
        syncing = true;
        KeepBox.Items.Clear();
        foreach (var k in SaveProtection.KeepChoices) KeepBox.Items.Add(new ComboBoxItem { Content = t.Format("SpKeepItem", k), Tag = k });
        syncing = false;
        Visibility = Visibility.Visible;
        // The closed card only reads the settings file (no folder scan) to say whether protection is on.
        Summary.Text = shell.Saves.Get(game.Key).Enabled ? t["SpStateActive"] : t["SpTitleHint"];
    }

    private void OnVm(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Sync();
    private void Sync()
    {
        if (Vm is not { } vm) return;
        if (vm.Loaded) Summary.Text = vm.Summary;
        syncing = true;
        AutoSwitch.IsOn = vm.Auto;
        KeepBox.SelectedItem = KeepBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is int k && k == vm.Keep);
        syncing = false;
        NoBackups.Visibility = vm.Backups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnExpanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        if (Vm is { Loaded: false } vm) { await vm.LoadAsync(); Sync(); }
    }

    private async Task<bool> Confirm(string title, UIElement content, string yes, bool allowed = true)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ThemePalette.Element, Title = title, Content = content,
            PrimaryButtonText = yes, CloseButtonText = Vm!.Text["DlgCancel"], DefaultButton = ContentDialogButton.Close, IsPrimaryButtonEnabled = allowed,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    private static TextBlock Line(string text, double opacity = 1, double size = 14) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = opacity, FontSize = size };

    private StackPanel EnableBody(string folder)
    {
        var body = new StackPanel { Spacing = 8, MaxWidth = 460 };
        body.Children.Add(Line(Vm!.Text["SpEnableBody"]));
        body.Children.Add(Line(folder, 0.8, 12.5));
        return body;
    }

    private async void OnEnable(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (await Confirm(vm.Text.Format("SpEnableTitle", vm.Game.Name), EnableBody(vm.FolderPath), vm.Text["SpEnable"])) await vm.EnableAsync();
    }

    private async void OnPick(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        if (await Confirm(vm.Text.Format("SpEnableTitle", vm.Game.Name), EnableBody(folder.Path), vm.Text["SpEnable"])) await vm.UseFolderAsync(folder.Path);
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if (Vm is not { FolderPath.Length: > 0 } vm || !Directory.Exists(vm.FolderPath)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { ArgumentList = { vm.FolderPath } })?.Dispose(); }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Open save folder"); }
    }

    private async void OnBackup(object sender, RoutedEventArgs e) { if (Vm is { } vm) await vm.BackupNowAsync(); }
    private async void OnOff(object sender, RoutedEventArgs e) { if (Vm is { } vm) await vm.DisableAsync(); }
    private async void OnRecover(object sender, RoutedEventArgs e) { if (Vm is { } vm) await vm.RecoverAsync(); }
    private void OnAuto(object sender, RoutedEventArgs e) { if (!syncing && Vm is { } vm) vm.SetAuto(AutoSwitch.IsOn); }
    private void OnKeep(object sender, SelectionChangedEventArgs e) { if (!syncing && Vm is { } vm && KeepBox.SelectedItem is ComboBoxItem { Tag: int k }) vm.SetKeep(k); }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || (sender as FrameworkElement)?.Tag is not string id) return;
        if (await Confirm(vm.Text["SpDeleteTitle"], Line(vm.Text["SpDeleteBody"]), vm.Text["SpDelete"])) await vm.DeleteAsync(id);
    }

    // The validated plan is shown file by file; the game must be closed; then the vault restores with a safety copy.
    private async void OnRestore(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || (sender as FrameworkElement)?.Tag is not string id) return;
        var t = vm.Text;
        var plan = await vm.PlanAsync(id);
        if (plan is null) { await Confirm(t["SpTitle"], Line(t["SpFailInvalid"]), t["SpRestoreConfirm"], allowed: false); await vm.LoadAsync(); return; }
        var running = await Task.Run(vm.GameIsRunning);
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
        if (vm.CloudSynced) body.Children.Add(Line(t["SpCloudNote"], 0.85, 12.5));
        if (running) body.Children.Add(new TextBlock { Text = t["SpRestoreRunning"], TextWrapping = TextWrapping.Wrap, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["WarningBrush"] });
        var when = plan.Snapshot.Created.ToLocalTime().ToString("g", System.Globalization.CultureInfo.GetCultureInfo(t.Language));
        if (!await Confirm(t.Format("SpRestoreTitle", when), new ScrollViewer { Content = body, MaxHeight = 420 }, t["SpRestoreConfirm"], allowed: !running)) return;
        var result = await vm.RestoreAsync(id);
        Serilog.Log.Information("Save restore {Game}: {Restored} {Failure}", vm.Game.Name, result.Restored, result.Failure);
    }
}
