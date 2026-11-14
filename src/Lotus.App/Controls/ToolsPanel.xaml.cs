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
            // Arrived from Home or the PC X-ray with a tool in mind: open it; "checkup" also starts the Check-up right away.
            if (PendingOpen is { } id) { PendingOpen = null; await OpenPendingAsync(vm, id); }
            await vm.RefreshAsync();
        };
    }
    public static string? PendingOpen { get; set; }
    private async Task OpenPendingAsync(ToolsViewModel vm, string id)
    {
        if (!cards.ContainsKey(id)) return;
        Open(id);
        if (id == "checkup" && vm.Checkup is { Busy: false } c) { try { await c.RunAsync(); } catch (Exception ex) { Serilog.Log.Warning(ex, "Check-up from Home"); } }
    }

    // ---- Tiles (visual 2.0): compact squares in four groups, filtered by the search box ----
    private readonly Dictionary<string, FrameworkElement> cards = [];
    private readonly List<(Button Tile, string Name, TextBlock Line, VariableSizedWrapGrid Wrap, FrameworkElement Group)> tiles = [];
    private readonly List<VariableSizedWrapGrid> wraps = [];
    private static readonly (string Title, string[] Ids)[] Groups =
    [
        ("ToolGroupClean", ["clean", "temp", "store", "unin"]),
        ("ToolGroupSpeed", ["proc", "mem", "startup", "monitor", "features", "overlays"]),
        ("ToolGroupDiagnose", ["crash", "health", "disk", "drivers"]),
        ("ToolGroupSystem", ["windows", "net"]),
    ];
    private void BuildTiles(ToolsViewModel vm)
    {
        if (TileGroups.Children.Count > 0) return;
        cards["mem"] = MemCard; cards["temp"] = TempCard; cards["clean"] = CleanCard; cards["proc"] = ProcCard; cards["store"] = StoreCard; cards["startup"] = StartCard; cards["health"] = HealthCard; cards["monitor"] = MonitorCard; cards["features"] = FeaturesCard; cards["crash"] = CrashCard; cards["windows"] = UpkeepCard; cards["checkup"] = CheckupCard; cards["unin"] = UninCard; cards["net"] = NetCard; cards["disk"] = DiskCard; cards["drivers"] = DriverCard; cards["overlays"] = OverlayCard;
        var defs = new Dictionary<string, (string Glyph, string Title, string Status)>
        {
            ["clean"] = ("\uE74D", "ClTitle", "Clean.TileStatus"), ["proc"] = ("\uE964", "PrTitle", "Processes.TileStatus"), ["store"] = ("\uE8B7", "StTitle", "Storage.TileStatus"), ["startup"] = ("\uE7E8", "SuTitle", "Startup.TileStatus"),
            ["monitor"] = ("\uE7F4", "MnTitle", "Gaming.MonitorTile"), ["features"] = ("\uE7FC", "GfTitle", "Gaming.FeaturesTile"), ["crash"] = ("\uE9D9", "CrashTitle", "Crashes.TileStatus"), ["windows"] = ("\uE895", "WuTitle", "Upkeep.TileStatus"),
            ["mem"] = ("\uE964", "ToolMemTitle", "MemoryStatus"), ["temp"] = ("\uE74D", "ToolTempTitle", "TempStatus"), ["unin"] = ("\uE71D", "UninTitle", "Uninstall.Summary"), ["net"] = ("\uE701", "ToolNetTile", "NetTileStatus"),
            ["health"] = ("\uEDA2", "DkTitle", "DiskTool.TileStatus"), ["disk"] = ("\uEDA2", "ToolDiskTitle", "DiskTileStatus"), ["drivers"] = ("\uE7F8", "ToolDriverTitle", "DriverStatus"), ["overlays"] = ("\uE7F4", "OvTitle", "Overlays.Summary"),
        };
        foreach (var (groupTitle, ids) in Groups)
        {
            var wrap = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 272, ItemHeight = 156, Margin = new Thickness(0, 0, -12, 0) }; // the last tile's 12 px gap hangs outside, so tiles end flush wraps.Add(wrap);
            var group = new StackPanel { Spacing = 12 };
            group.Children.Add(new TextBlock { Text = vm.Text[groupTitle], Style = (Style)Application.Current.Resources["SubtitleStyle"] });
            group.Children.Add(wrap);
            foreach (var id in ids)
            {
                if (!Available(vm, id)) continue;
                var (glyph, title, status) = defs[id];
                var badge = new FontIcon { Glyph = glyph, FontSize = 20, Foreground = (Brush)Application.Current.Resources["RoseBrush"], HorizontalAlignment = HorizontalAlignment.Left };
                var name = new TextBlock { Text = vm.Text[title], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
                var line = new TextBlock { FontSize = 12, LineHeight = 16, Foreground = (Brush)Application.Current.Resources["MutedBrush"], TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis };
                line.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(status) });
                var info = new StackPanel { Spacing = 8 }; info.Children.Add(badge); info.Children.Add(name); info.Children.Add(line);
                var tile = new Button
                {
                    Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(16), CornerRadius = (CornerRadius)Application.Current.Resources["CardRadius"],
                    HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
                    Background = (Brush)Application.Current.Resources["CardBrush"], BorderBrush = (Brush)Application.Current.Resources["CardBorderBrush"], BorderThickness = new Thickness(1), Content = info, Tag = id
                };
                Motion.SetHover(tile, 1.02); Motion.SetPress(tile, true);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(tile, "tools-tile-" + id);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tile, vm.Text[title]);
                ToolTipService.SetToolTip(tile, vm.Text["ToolOpen"]);
                tile.Click += (_, _) => Open(id);
                wrap.Children.Add(tile); tiles.Add((tile, vm.Text[title], line, wrap, group));
            }
            if (wrap.Children.Count > 0) TileGroups.Children.Add(group);
        }
        TileGroups.SizeChanged += (_, e) => FitColumns(e.NewSize.Width);
        FitColumns(TileGroups.ActualWidth);
    }
    // The tiles fill the whole width: as many ~270 px columns as fit, then stretched to the edge.
    private void FitColumns(double width)
    {
        if (width < 100) return;
        width += 12; // the wraps reach 12 px past the edge (their last gap)
        var columns = Math.Max(1, (int)(width / 272)); // each cell holds the tile and its 12 px gap, so the row never overflows
        var item = Math.Floor(width / columns);
        foreach (var wrap in wraps) wrap.ItemWidth = item;
    }
    private static bool Available(ToolsViewModel vm, string id) => id switch
    {
        "unin" => vm.Uninstall is not null, "clean" => vm.Clean is not null, "proc" => vm.Processes is not null, "store" => vm.Storage is not null,
        "startup" => vm.Startup is not null, "health" => vm.DiskTool is not null, "monitor" or "features" => vm.Gaming is not null, "crash" => vm.Crashes is not null,
        "windows" => vm.Upkeep is not null, "overlays" => vm.Overlays is not null,
        "disk" => vm.DiskTool is null, // Saúde do Disco replaces the short list
        "mem" => vm.Processes is null, // Memória e Processos replaces the simple list
        "temp" => vm.Clean is null, // Lotus Clean covers the temporary files (and much more)
        _ => true,
    };
    // Search: hides the tiles (and empty groups) whose name does not contain the typed text; accents and case ignored.
    private void OnToolSearch(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        // Matches the name and the tile's own line (e.g. "disco" also finds Arquivos e Armazenamento).
        // Matching tiles are re-added in order, so the results start at the first column without gaps.
        var query = Fold(sender.Text.Trim());
        foreach (var wrap in wraps) wrap.Children.Clear();
        foreach (var (tile, name, line, wrap, _) in tiles)
            if (query.Length == 0 || Fold(name).Contains(query) || Fold(line.Text ?? "").Contains(query)) wrap.Children.Add(tile);
        foreach (var (_, _, _, wrap, group) in tiles) group.Visibility = wrap.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ToolSearchEmpty.Visibility = wraps.Any(w => w.Children.Count > 0) ? Visibility.Collapsed : Visibility.Visible;
    }
    private static string Fold(string text)
    {
        var d = text.Normalize(System.Text.NormalizationForm.FormD);
        return new string(d.Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
    }
    private void Open(string id)
    {
        Tiles.Visibility = Visibility.Collapsed; CheckupHero.Visibility = Visibility.Collapsed; BackButton.Visibility = Visibility.Visible;
        foreach (var (key, card) in cards) card.Visibility = key == id ? Visibility.Visible : Visibility.Collapsed;
        if (id == "store" && DataContext is ToolsViewModel { Storage: { } st }) st.Load(); // drive space only; reading, no scan
        if (id == "startup" && DataContext is ToolsViewModel { Startup: { Rows.Count: 0 } su }) _ = su.LoadAsync(); // reading only
        if (id == "health" && DataContext is ToolsViewModel { DiskTool: { Rows.Count: 0 } dk }) _ = dk.LoadAsync(); // reading only
        if (id is "monitor" or "features" && DataContext is ToolsViewModel { Gaming: { Monitor: null, Rows.Count: 0 } gm }) _ = gm.LoadAsync(); // reading only
        if (id == "crash" && DataContext is ToolsViewModel { Crashes: { Loaded: false } cr }) _ = cr.LoadAsync(); // reading only
        if (id == "windows" && DataContext is ToolsViewModel { Upkeep: { Loaded: false } up }) _ = up.LoadAsync(); // reading only
        Motion.EnterPage(cards[id]);
    }
    private void OnBack(object sender, RoutedEventArgs e)
    {
        foreach (var card in cards.Values) card.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Collapsed; Tiles.Visibility = Visibility.Visible; CheckupHero.Visibility = DataContext is ToolsViewModel { Checkup: not null } ? Visibility.Visible : Visibility.Collapsed; Motion.EnterPage(CheckupHero);
        Motion.EnterPage(Tiles);
    }

    private async void OnScanOverlays(object sender, RoutedEventArgs e) { if (DataContext is ToolsViewModel { Overlays: { } o }) { try { await o.ScanAsync(); } catch (Exception ex) { Serilog.Log.Warning(ex, "Overlay check"); } } }
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
    // ---- Check-up: runs every read-only analysis, then shows the summary; each line opens its tool ----
    private async void OnCheckupRun(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ToolsViewModel { Checkup: { } c }) return;
        await c.RunAsync();
        if (c.Done && CheckupCard.Visibility != Visibility.Visible) Open("checkup");
    }
    private void OnCheckupOpen(object sender, RoutedEventArgs e) => Open("checkup");
    private void OnCheckupGo(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is string id && cards.ContainsKey(id)) Open(id); }

    // ---- Monitor and Windows gaming features (the same reversible actions as Otimizar) ----
    private async void OnMonitorRaise(object sender, RoutedEventArgs e)
    {
        if (DataContext is ToolsViewModel { Gaming: { Monitor: { } m } g } vm
            && await Confirm(vm.Text.Format("MnConfirmTitle", m.MaxHz), vm.Text["MnConfirmBody"], g.RaiseLabel, vm))
            await g.ApplyAsync(Lotus.Core.Optimization.DisplayRefreshAction.FeatureId);
    }
    private async void OnMonitorUndo(object sender, RoutedEventArgs e)
    {
        if (DataContext is ToolsViewModel { Gaming: { } g } vm && await Confirm(vm.Text["FeatConfirmUndoTitle"], vm.Text["FeatConfirmUndoBody"], vm.Text["MnUndo"], vm))
            await g.UndoAsync(Lotus.Core.Optimization.DisplayRefreshAction.FeatureId);
    }
    private async void OnMonitorSettings(object sender, RoutedEventArgs e) => await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:display-advanced"));
    private async void OnFeatureApply(object sender, RoutedEventArgs e)
    {
        if (DataContext is ToolsViewModel { Gaming: { } g } vm && (sender as FrameworkElement)?.Tag is string id
            && await Confirm(vm.Text.Format("FeatConfirmTitle", vm.Text["FeatName" + id]), vm.Text["FeatConfirmBody"], vm.Text["GfApply"], vm))
            await g.ApplyAsync(id);
    }
    private async void OnFeatureUndo(object sender, RoutedEventArgs e)
    {
        if (DataContext is ToolsViewModel { Gaming: { } g } vm && (sender as FrameworkElement)?.Tag is string id
            && await Confirm(vm.Text["FeatConfirmUndoTitle"], vm.Text["FeatConfirmUndoBody"], vm.Text["MnUndo"], vm))
            await g.UndoAsync(id);
    }
    // Only Windows Settings pages (ms-settings:) are opened from here.
    private async void OnFeatureSettings(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string uri && uri.StartsWith("ms-settings:", StringComparison.Ordinal)) await Windows.System.Launcher.LaunchUriAsync(new Uri(uri));
    }

    // ---- Game crashes and Windows upkeep ----
    private async void OnCrashRefresh(object sender, RoutedEventArgs e) { if (DataContext is ToolsViewModel { Crashes: { } c }) await c.LoadAsync(); }
    private void OnOpenWindowsUpdateSettings(object sender, RoutedEventArgs e) { if (DataContext is ToolsViewModel { Upkeep: { } u }) u.OpenWindowsUpdate(); }
    private void OnRestoreOpen(object sender, RoutedEventArgs e) { if (DataContext is ToolsViewModel { Upkeep: { } u }) u.OpenSystemRestore(); }
    private async void OnRestoreCreate(object sender, RoutedEventArgs e)
    {
        if (DataContext is ToolsViewModel { Upkeep: { } u } vm && await Confirm(vm.Text["WuRestoreConfirmTitle"], vm.Text["WuRestoreConfirmBody"], vm.Text["WuRestoreCreate"], vm))
            await u.CreateRestorePointAsync();
    }

    // ---- Disk health and system ----
    private async void OnDiskRefresh(object sender, RoutedEventArgs e) { if (DataContext is ToolsViewModel { DiskTool: { } d }) await d.LoadAsync(); }
    private async void OnDiskScan(object sender, RoutedEventArgs e)
    {
        if (DataContext is ToolsViewModel { DiskTool: { } d } vm && (sender as FrameworkElement)?.Tag is string letter
            && await Confirm(vm.Text.Format("DkScanTitle", letter), vm.Text["DkScanBody"], vm.Text["DkScanGo"], vm)) d.StartScan(letter);
    }
    private async void OnSystemLoad(object sender, RoutedEventArgs e) { if (DataContext is ToolsViewModel { SystemTool: { } s }) await s.LoadAsync(); }
    private async void OnSystemSfc(object sender, RoutedEventArgs e) => await Repair("sfc");
    private async void OnSystemDism(object sender, RoutedEventArgs e) => await Repair("dism");
    private async Task Repair(string tool)
    {
        if (DataContext is ToolsViewModel { SystemTool: { } s } vm && await Confirm(vm.Text[tool == "sfc" ? "SySfc" : "SyDism"], vm.Text[tool == "sfc" ? "SySfcBody" : "SyDismBody"], vm.Text["SyRepairGo"], vm)) s.StartRepair(tool);
    }

    // ---- Windows startup ----
    private async void OnStartupRefresh(object sender, RoutedEventArgs e) { if (DataContext is ToolsViewModel { Startup: { } s }) await s.LoadAsync(); }
    private async void OnStartupToggle(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ToolsViewModel { Startup: { } s } vm || (sender as FrameworkElement)?.DataContext is not Lotus.Core.Maintenance.StartupToolRow row) return;
        var on = row.IsDisabled;
        if (await Confirm(vm.Text.Format(on ? "SuConfirmOnTitle" : "SuConfirmOffTitle", row.Title), vm.Text[on ? "SuConfirmOnBody" : "SuConfirmOffBody"], row.ToggleLabel, vm)) await s.ToggleAsync(row);
    }

    // ---- Files and storage ----
    private async void OnStoreScan(object sender, RoutedEventArgs e) { if (DataContext is ToolsViewModel { Storage: { } s }) await s.ScanAsync(); }
    private void OnStoreCancel(object sender, RoutedEventArgs e) { if (DataContext is ToolsViewModel { Storage: { } s }) s.Cancel(); }
    private void OnStoreReveal(object sender, RoutedEventArgs e) { if (DataContext is ToolsViewModel { Storage: { } s } && (sender as FrameworkElement)?.DataContext is Lotus.Core.Maintenance.StorageRow row) s.Reveal(row); }
    // Lists what goes to the Recycle Bin; for duplicates one copy of each content must stay.
    private async void OnStoreRecycle(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ToolsViewModel { Storage: { } s } vm) return;
        var chosen = s.Chosen;
        if (chosen.Count == 0) { await Confirm(vm.Text["StTitle"], vm.Text["StNoneChosen"], null, vm); return; }
        if (!s.KeepsOneCopyOfEach()) { await Confirm(vm.Text["StTitle"], vm.Text["StKeepOne"], null, vm); return; }
        var names = string.Join(", ", chosen.Take(8).Select(r => r.Title)) + (chosen.Count > 8 ? vm.Text.Format("StAndMore", chosen.Count - 8) : "");
        if (await Confirm(vm.Text["StConfirmTitle"], vm.Text.Format("StConfirmBody", chosen.Count, s.Size(s.ChosenBytes), names), vm.Text["StRecycle"], vm)) await s.RecycleSelectedAsync();
    }

    // ---- Memory and processes ----
    private async void OnProcRefresh(object sender, RoutedEventArgs e) { if (DataContext is ToolsViewModel { Processes: { } p }) await p.RefreshAsync(); }
    private void OnProcToggleList(object sender, RoutedEventArgs e) { if (DataContext is ToolsViewModel { Processes: { } p } && (sender as FrameworkElement)?.DataContext is Lotus.Core.Maintenance.ProcessRow row) p.ToggleInList(row); }
    private async void OnProcPickList(object sender, RoutedEventArgs e)
    {
        if (DataContext is ToolsViewModel { Processes: { } p } vm && p.SelectCloseList() == 0) await Confirm(vm.Text["PrTitle"], vm.Text["PrListEmpty"], null, vm);
    }
    private async void OnProcClose(object sender, RoutedEventArgs e) => await CloseProcesses(false);
    private async void OnProcForce(object sender, RoutedEventArgs e) => await CloseProcesses(true);
    // Always asks, naming each program; forcing gets its own, stronger warning (unsaved work is lost).
    private async Task CloseProcesses(bool force)
    {
        if (DataContext is not ToolsViewModel { Processes: { } p } vm) return;
        var chosen = p.Chosen;
        if (chosen.Count == 0) { await Confirm(vm.Text["PrTitle"], vm.Text["PrNoneChosen"], null, vm); return; }
        var names = string.Join(", ", chosen.Select(r => r.Name));
        if (await Confirm(vm.Text[force ? "PrForceTitle" : "PrConfirmTitle"], vm.Text.Format(force ? "PrForceBody" : "PrConfirmBody", names), vm.Text[force ? "PrForceGo" : "PrClose"], vm))
            await p.CloseSelectedAsync(force);
    }

    // ---- Lotus Clean ----
    private async void OnCleanAnalyze(object sender, RoutedEventArgs e) { if (DataContext is ToolsViewModel { Clean: { } c }) await c.AnalyzeAsync(); }
    private void OnCleanCancel(object sender, RoutedEventArgs e) { if (DataContext is ToolsViewModel { Clean: { } c }) c.Cancel(); }
    private void OnCleanPickSafe(object sender, RoutedEventArgs e) { if (DataContext is ToolsViewModel { Clean: { } c }) c.SelectRecommended(); }
    private void OnCleanPickNone(object sender, RoutedEventArgs e) { if (DataContext is ToolsViewModel { Clean: { } c }) c.SelectNone(); }
    // Always asks, listing what will go; the Recycle Bin gets its own warning (emptying it cannot be undone).
    private async void OnCleanRun(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ToolsViewModel { Clean: { } c } vm) return;
        var chosen = c.Rows.Where(r => r.Selected && r.CanSelect).ToList();
        if (chosen.Count == 0) return;
        var body = vm.Text.Format("ClConfirmBody", c.Size(c.SelectedBytes), string.Join(", ", chosen.Select(r => r.Title)));
        if (chosen.Any(r => r.Id == "RecycleBin")) body += "\n\n" + vm.Text["ClConfirmBin"];
        if (await Confirm(vm.Text["ClConfirmTitle"], body, vm.Text["ClClean"], vm)) await c.CleanAsync();
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
            XamlRoot = XamlRoot, Title = title, RequestedTheme = ThemePalette.Element, DefaultButton = ContentDialogButton.Close,
            Content = RichText.Block(body), CloseButtonText = vm.Text[primary is null ? "GsClose" : "DlgCancel"]
        };
        if (primary is not null) dialog.PrimaryButtonText = primary;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
