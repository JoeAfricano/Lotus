using Lotus.Core;
using Lotus.Core.Display;
using Lotus.Core.Tools;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace Lotus.App.Controls;

// Local illustrative draft first; physical DDC, SDR ramp and compatible driver intensity only on explicit test.
public sealed class MonitorColorsPanel : UserControl
{
    private readonly StackPanel root = new() { Spacing = 18 };
    private readonly ComboBox monitors = new() { HorizontalAlignment = HorizontalAlignment.Stretch, DisplayMemberPath = nameof(CalibrationState.Label) };
    private readonly Grid workspace = new() { ColumnSpacing = 20, RowSpacing = 16 };
    private readonly StackPanel mission = new() { Spacing = 12 };
    private readonly StackPanel scene = new() { Spacing = 10 };
    private readonly StackPanel missionNav = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly DisplayComparisonPreview image = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock objective = new() { TextWrapping = TextWrapping.Wrap, FontSize = 20, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock detail = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock clock = new() { TextWrapping = TextWrapping.Wrap };
    private readonly InfoBar status = new() { IsClosable = false };
    private readonly Button start = new(), keep = new(), undo = new(), restore = new(), refresh = new(), next = new(), previous = new();
    private readonly ToggleButton beforeButton = new(), adjustedButton = new();
    private readonly CheckBox checkedByPlayer = new();
    private readonly StackPanel chooser = new() { Spacing = 16 }, adjustment = new() { Spacing = 14 };
    private readonly Grid purposeCards = new() { ColumnSpacing = 10, RowSpacing = 10 }, ambientCards = new() { ColumnSpacing = 10, RowSpacing = 10 };
    private readonly List<ToggleButton> purposes = [], ambients = [];
    private readonly TextBlock suggestion = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock applying = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Grid progress = new() { ColumnSpacing = 8 };
    private readonly List<Button> steps = [];
    private readonly List<(Slider Control, bool Supported)> sliders = [];
    private Grid? clueGrid;
    private readonly bool[] completed = new bool[4];
    private readonly DispatcherTimer ticker = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly SemaphoreSlim operations = new(1, 1);
    private DisplayCalibrationController? controller;
    private CalibrationProfile profile = new();
    private int step, revision, purposeIndex, ambientIndex;
    private bool rendering, busy, subscribed, updating, requestedBefore, showingOriginal, built, drafting, physicalOperation, usingSavedProfile;
    private CalibrationProfile? verifiedProfile;
    private Task pending = Task.CompletedTask;
    private ITextCatalog Text => DataContext switch { ToolsViewModel vm => vm.Text, SectionViewModel vm => vm.Text, _ => new TextCatalog() };
    private CalibrationState? Selected => monitors.SelectedItem as CalibrationState;
    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    private static TextBlock Caption(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap,
        Style = (Style)Application.Current.Resources["CaptionStyle"] };

    public MonitorColorsPanel()
    {
        Content = root; AutomationProperties.SetAutomationId(this, "monitor-colors");
        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            ++revision; ticker.Stop();
            if (subscribed && controller is not null) controller.Changed -= OnChanged;
            subscribed = false;
            if (MainWindow.Shell is { } shell) shell.CalibrationDraftOpen = false;
            if (controller is { HasPreview: true } service) _ = Task.Run(() => service.CancelPreview());
        };
        SizeChanged += (_, e) => Layout(e.NewSize.Width);
        monitors.SelectionChanged += (_, _) => { if (!rendering) LoadSelection(); };
        start.Click += async (_, _) => await StartAsync();
        undo.Click += async (_, _) =>
        {
            ++revision;
            if (controller?.HasPreview == true) { await Act(() => controller.CancelPreview()); RequestLocalPreview(); }
            else { drafting = false; if (MainWindow.Shell is { } shell) shell.CalibrationDraftOpen = false; }
            UpdateStatus();
        };
        keep.Click += async (_, _) =>
        {
            if (controller?.HasPreview != true) { await TestMonitorAsync(); return; }
            requestedBefore = false;
            if (showingOriginal || verifiedProfile != profile) await QueueImmediateAsync();
            else { ++revision; await pending; }
            if (controller?.HasPreview != true || controller.LastResult != MonitorColorResult.Preview) return;
            await Act(() => controller!.Keep());
            if (controller.LastResult == MonitorColorResult.Kept) { drafting = false; if (MainWindow.Shell is { } shell) shell.CalibrationDraftOpen = false; }
            await ReloadAsync();
        };
        beforeButton.Click += (_, _) => RequestComparison(true);
        adjustedButton.Click += (_, _) => RequestComparison(false);
        restore.Click += async (_, _) => await RestoreAsync();
        refresh.Click += async (_, _) => await ReloadAsync();
        previous.Click += (_, _) => { if (step > 0) { --step; RenderMission(); } };
        next.Click += (_, _) => { completed[step] = checkedByPlayer.IsChecked == true; if (step < 3) ++step; RenderMission(); };
        checkedByPlayer.Checked += (_, _) => next.IsEnabled = step < 3;
        checkedByPlayer.Unchecked += (_, _) => next.IsEnabled = false;
        ticker.Tick += (_, _) => UpdateStatus();
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (MainWindow.Shell is not { } shell) return;
        try
        {
            await shell.MonitorColorsReady;
            if (!IsLoaded || shell.Calibration is not { } service) return;
            controller = service;
            if (!subscribed) { controller.Changed += OnChanged; subscribed = true; }
            if (!built) { Build(); built = true; }
            await ReloadAsync();
            if (controller.LastResult != MonitorColorResult.NoChange) Show(controller.LastResult);
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Display studio load"); Show(MonitorColorResult.DriverError); }
    }
    private void Build()
    {
        root.Children.Clear(); steps.Clear(); progress.Children.Clear(); progress.ColumnDefinitions.Clear();
        var hero = new StackPanel { Spacing = 8 };
        hero.Children.Add(new TextBlock { Text = Text["VsBadge"], Foreground = Brush("GoldBrush"), FontSize = 12, FontWeight = FontWeights.SemiBold });
        hero.Children.Add(new TextBlock { Text = Text["VsTitle"], FontSize = 30, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        hero.Children.Add(Caption(Text["VsIntro"]));
        root.Children.Add(new Border { Style = (Style)Application.Current.Resources["CardStyle"], Background = Brush("HeroBrush"), Child = hero });
        for (var i = 0; i < 4; i++)
        {
            var index = i; progress.ColumnDefinitions.Add(new());
            var button = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            button.Click += (_, _) => { step = index; RenderMission(); };
            Grid.SetColumn(button, i); progress.Children.Add(button); steps.Add(button);
        }
        chooser.Children.Clear(); adjustment.Children.Clear();
        root.Children.Add(status);
        root.Children.Add(chooser);
        monitors.Header = Text["McMonitor"]; chooser.Children.Add(monitors);
        detail.Style = (Style)Application.Current.Resources["CaptionStyle"]; chooser.Children.Add(detail);
        var recommendations = new StackPanel { Spacing = 10 };
        recommendations.Children.Add(new TextBlock { Text = Text["VsSuggestTitle"], FontSize = 20, FontWeight = FontWeights.SemiBold });
        recommendations.Children.Add(Caption(Text["VsSuggestIntro"]));
        recommendations.Children.Add(Caption(Text["VsPurpose"]));
        MakeChoices(purposeCards, purposes, ["VsCardEveryday", "VsCardGaming", "VsCardEditing", "VsCardMovies"],
            ["\uE7F4", "\uE7FC", "\uE790", "\uE714"], true);
        recommendations.Children.Add(purposeCards);
        recommendations.Children.Add(Caption(Text["VsAmbient"]));
        MakeChoices(ambientCards, ambients, ["VsAmbientNormal", "VsAmbientDim", "VsAmbientBright"],
            ["\uE706", "\uE708", "\uE793"], false);
        recommendations.Children.Add(ambientCards);
        suggestion.Style = (Style)Application.Current.Resources["CaptionStyle"]; recommendations.Children.Add(suggestion);
        var applyLabel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        applyLabel.Children.Add(new FontIcon { Glyph = "\uE73E", FontSize = 16 }); applyLabel.Children.Add(new TextBlock { Text = Text["VsStart"], TextWrapping = TextWrapping.Wrap });
        start.Content = applyLabel; start.Style = (Style)Application.Current.Resources["PrimaryButtonStyle"];
        AutomationProperties.SetName(start, Text["VsStart"]);
        start.HorizontalAlignment = HorizontalAlignment.Stretch; recommendations.Children.Add(start);
        chooser.Children.Add(new Border { Style = (Style)Application.Current.Resources["CardStyle"], Child = recommendations });
        applying.Style = (Style)Application.Current.Resources["CaptionStyle"]; applying.Foreground = Brush("GoldBrush");
        root.Children.Add(applying);
        root.Children.Add(adjustment);
        adjustment.Children.Add(new TextBlock { Text = Text["VsRefineTitle"], FontSize = 22, FontWeight = FontWeights.SemiBold });
        adjustment.Children.Add(Caption(Text["VsRefineHint"]));
        var comparison = new Grid { ColumnSpacing = 10 }; comparison.ColumnDefinitions.Add(new()); comparison.ColumnDefinitions.Add(new());
        beforeButton.Content = Text["VsBeforeButton"]; adjustedButton.Content = Text["VsAdjustedButton"];
        beforeButton.HorizontalAlignment = adjustedButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        comparison.Children.Add(beforeButton); comparison.Children.Add(adjustedButton); Grid.SetColumn(adjustedButton, 1);
        adjustment.Children.Add(comparison); adjustment.Children.Add(clock);
        adjustment.Children.Add(progress);
        image.SetLabels(Text["VsSplitBefore"], Text["VsSplitAfter"], Text["VsSplitHelp"]);
        scene.Children.Clear(); scene.Children.Add(image);
        scene.Children.Add(Caption(Text["VsLocalLimits"]));
        workspace.Children.Clear(); workspace.ColumnDefinitions.Clear(); workspace.RowDefinitions.Clear();
        workspace.ColumnDefinitions.Add(new() { Width = new GridLength(1.6, GridUnitType.Star) }); workspace.ColumnDefinitions.Add(new());
        workspace.RowDefinitions.Add(new()); workspace.RowDefinitions.Add(new() { Height = GridLength.Auto });
        workspace.Children.Add(scene); workspace.Children.Add(mission);
        adjustment.Children.Add(workspace); Layout(ActualWidth);
        var actions = new Grid { ColumnSpacing = 10, RowSpacing = 8 };
        actions.ColumnDefinitions.Add(new()); actions.ColumnDefinitions.Add(new());
        keep.Content = Text["VsKeep"]; undo.Content = Text["VsUndo"];
        keep.Style = (Style)Application.Current.Resources["PrimaryButtonStyle"];
        foreach (var button in new[] { keep, undo }) { button.HorizontalAlignment = HorizontalAlignment.Stretch; actions.Children.Add(button); }
        Grid.SetColumn(undo, 1); adjustment.Children.Add(actions);
        var safety = new StackPanel { Spacing = 10 };
        restore.Content = Text["VsRestore"]; refresh.Content = Text["McRefresh"]; safety.Children.Add(restore); safety.Children.Add(refresh);
        var legacy = new Button { Content = Text["VsLegacyRestore"] };
        legacy.Click += async (_, _) =>
        {
            if (Selected is not { } selected || MainWindow.Shell is not { MonitorColors: { } old } shell) return;
            if (!await Confirm(Text["VsLegacyRestore"], Text["McRestoreConfirm"])) return;
            var current = await Task.Run(() => shell.ColorDriver.Read(selected.Identity));
            if (current is not null) Show(await Task.Run(() => old.Restore(selected.Identity, current)));
        };
        safety.Children.Add(legacy);
        safety.Children.Add(Caption(Text["VsLimits"]));
        root.Children.Add(new Expander { Header = Text["McLimitsTitle"], HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = safety });
        RenderMission();
    }
    private void MakeChoices(Grid grid, List<ToggleButton> buttons, string[] keys, string[] glyphs, bool use)
    {
        grid.Children.Clear(); grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear(); buttons.Clear();
        for (var i = 0; i < keys.Length; i++)
        {
            var index = i;
            var content = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(new FontIcon { Glyph = glyphs[i], FontSize = use ? 27 : 20, Foreground = Brush(use ? "RoseBrush" : "GoldBrush") });
            content.Children.Add(new TextBlock { Text = Text[keys[i]], TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontWeight = FontWeights.SemiBold });
            var button = new ToggleButton { Content = content, MinHeight = use ? 118 : 86, Padding = new Thickness(12), CornerRadius = new(14),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(button, Text[keys[i]]);
            button.Click += (_, _) => { if (use) purposeIndex = index; else ambientIndex = index; usingSavedProfile = false; UpdateChoiceStates(); PrepareSuggestion(); };
            grid.Children.Add(button); buttons.Add(button);
        }
        UpdateChoiceStates();
    }
    private void UpdateChoiceStates()
    {
        void Paint(ToggleButton button, bool selected, string accent)
        {
            button.IsChecked = selected; button.BorderBrush = Brush(selected ? accent : "CardBorderBrush");
            // Checked template uses a filled rose surface: explicit contrast for BOTH icon and label.
            if (button.Content is StackPanel content)
                foreach (var item in content.Children)
                {
                    if (item is FontIcon icon) icon.Foreground = Brush(selected ? "OnRoseBrush" : accent);
                    if (item is TextBlock label) label.Foreground = Brush(selected ? "OnRoseBrush" : "MainTextBrush");
                }
        }
        for (var i = 0; i < purposes.Count; i++) Paint(purposes[i], !usingSavedProfile && i == purposeIndex, "RoseBrush");
        for (var i = 0; i < ambients.Count; i++) Paint(ambients[i], !usingSavedProfile && i == ambientIndex, "GoldBrush");
    }
    private static void ChoiceLayout(Grid grid, int columns)
    {
        grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
        for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new());
        for (var i = 0; i < (grid.Children.Count + columns - 1) / columns; i++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var i = 0; i < grid.Children.Count; i++) { Grid.SetColumn((FrameworkElement)grid.Children[i], i % columns); Grid.SetRow((FrameworkElement)grid.Children[i], i / columns); }
    }
    private void Layout(double width)
    {
        if (workspace.ColumnDefinitions.Count != 2) return;
        var wide = width >= 850; workspace.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(mission, wide ? 1 : 0); Grid.SetRow(mission, wide ? 0 : 1);
        ChoiceLayout(purposeCards, width >= 720 ? 4 : width >= 340 ? 2 : 1);
        ChoiceLayout(ambientCards, width >= 450 ? 3 : 1);
    }
    private async Task ReloadAsync()
    {
        if (controller is null || busy) return;
        busy = true; UpdateStatus();
        try
        {
            var id = Selected?.Identity; var values = await Task.Run(() => MainWindow.Shell!.CalibrationDriver.Displays());
            if (!IsLoaded) return;
            rendering = true; monitors.ItemsSource = values;
            monitors.SelectedItem = values.FirstOrDefault(s => s.Identity == id) ?? values.FirstOrDefault(s => s.CanAdjust) ?? values.FirstOrDefault();
            rendering = false; LoadSelection();
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Display studio refresh"); Show(MonitorColorResult.DriverError); }
        finally { rendering = false; busy = false; UpdateStatus(); }
    }
    private void LoadSelection()
    {
        if (Selected is { } state)
        {
            var saved = controller?.ProfileFor(state.Identity);
            usingSavedProfile = saved is not null;
            profile = saved ?? (state.CanAdjust
                ? DisplayRecommendations.Suggest(state, (DisplayUse)purposeIndex, (DisplayAmbient)ambientIndex)
                : new(state.Brightness?.Percent, state.Contrast?.Percent));
            DescribeSuggestion();
            UpdateChoiceStates();
        }
        RenderMission(); UpdateStatus();
    }
    private void PrepareSuggestion()
    {
        if (Selected is not { CanAdjust: true } state) return;
        var original = controller?.OriginalFor(state.Identity) ?? state;
        // Older saved profiles predate driver intensity support. The current driver readback
        // supplies that previously unknown field without modifying the durable original.
        original = original with { Vibrance = original.Vibrance ?? state.Vibrance };
        profile = DisplayRecommendations.Suggest(original,
            (DisplayUse)purposeIndex, (DisplayAmbient)ambientIndex);
        Array.Clear(completed); DescribeSuggestion(); RenderMission();
        if (drafting) RequestLocalPreview();
    }
    private void DescribeSuggestion()
    {
        var saturation = profile.Saturation ?? (Selected?.Vibrance is { } range ? range.Percent(range.Current) : (double?)null);
        suggestion.Text = Text[usingSavedProfile ? "VsUsingSaved" : purposeIndex == (int)DisplayUse.Editing ? "VsSuggestEditing" : "VsSuggestPreference"] + "\n" +
            Text.Format("VsSuggestValues", profile.Brightness?.ToString("0") ?? "—", profile.Contrast?.ToString("0") ?? "—", profile.Gamma.ToString("0.00"), saturation?.ToString("0") ?? "—");
    }
    private void RenderMission()
    {
        rendering = true;
        try
        {
            for (var i = 0; i < steps.Count; i++)
            {
                steps[i].Content = (completed[i] ? "✓ " : $"{i + 1} · ") + Text["VsStep" + i];
                steps[i].BorderBrush = Brush(i == step ? "RoseBrush" : "CardBorderBrush");
            }
            var sceneAsset = step switch
            {
                0 => "medieval-shadows.png",
                1 => "medieval-highlights.png",
                2 => "medieval-daylight.png",
                _ => "medieval-comparison.png"
            };
            if (drafting) { _ = image.SetSceneAsync(sceneAsset); RequestLocalPreview(); }
            missionNav.Children.Clear();
            mission.Children.Clear();
            sliders.Clear();
            clueGrid = null;
            objective.Text = Text["VsMission" + step]; mission.Children.Add(objective); mission.Children.Add(Caption(Text["VsHint" + step]));
            if (step is 0 or 1) mission.Children.Add(Clues(step == 1));
            if (step == 0)
            {
                var physical = Selected?.Brightness is not null;
                AddSlider(physical ? "VsBrightness" : "VsDigitalBrightness", physical ? profile.Brightness ?? 50 : profile.DigitalBrightness * 100,
                    physical ? 0 : -10, physical ? 100 : 10, 1, physical || Selected?.Ramp is not null,
                    value => profile = physical ? profile with { Brightness = value } : profile with { DigitalBrightness = value / 100 });
                AddSlider("VsGamma", profile.Gamma, .75, 1.3, .01, Selected?.Ramp is not null, value => profile = profile with { Gamma = value });
            }
            else if (step == 1)
            {
                var physical = Selected?.Contrast is not null;
                AddSlider(physical ? "VsContrast" : "VsDigitalContrast", physical ? profile.Contrast ?? 50 : profile.DigitalContrast * 100,
                    physical ? 0 : 80, physical ? 100 : 120, 1, physical || Selected?.Ramp is not null,
                    value => profile = physical ? profile with { Contrast = value } : profile with { DigitalContrast = value / 100 });
            }
            else if (step == 2)
            {
                AddSlider("VsWarmth", profile.Warmth, -20, 20, 1, Selected?.Ramp is not null, value => profile = profile with { Warmth = value });
                AddSlider("VsTint", profile.Tint, -10, 10, 1, Selected?.Ramp is not null, value => profile = profile with { Tint = value });
                AddSlider("VsSaturation", profile.Saturation ?? (Selected?.Vibrance is { } range ? range.Percent(range.Current) : 50), 0, 100, 1,
                    Selected?.Vibrance is not null, value => profile = profile with { Saturation = value });
            }
            else mission.Children.Add(Caption(Text["VsReview"]));
            checkedByPlayer.Content = Text["VsCheck" + step]; checkedByPlayer.IsChecked = completed[step];
            mission.Children.Add(checkedByPlayer);
            previous.Content = Text["VsPrevious"]; next.Content = Text["VsNext"]; previous.IsEnabled = step > 0; next.IsEnabled = step < 3 && checkedByPlayer.IsChecked == true;
            next.Visibility = step < 3 ? Visibility.Visible : Visibility.Collapsed;
            missionNav.Children.Add(previous); missionNav.Children.Add(next); mission.Children.Add(missionNav);
            UpdateClues();
        }
        finally { rendering = false; }
    }
    private void AddSlider(string key, double value, double min, double max, double increment, bool supported, Action<double> changed)
    {
        var label = Caption(Text[key] + " · " + value.ToString(increment < 1 ? "0.00" : "0", System.Globalization.CultureInfo.CurrentCulture));
        var slider = new Slider { Minimum = min, Maximum = max, StepFrequency = increment, Value = Math.Clamp(value, min, max),
            IsEnabled = supported && drafting && controller?.HasPreview != true && !busy };
        AutomationProperties.SetName(slider, Text[key]);
        sliders.Add((slider, supported));
        slider.ValueChanged += (_, e) =>
        {
            if (rendering) return;
            label.Text = Text[key] + " · " + e.NewValue.ToString(increment < 1 ? "0.00" : "0", System.Globalization.CultureInfo.CurrentCulture);
            changed(e.NewValue); DescribeSuggestion(); completed[step] = false; checkedByPlayer.IsChecked = false;
            RequestLocalPreview();
        };
        mission.Children.Add(label); mission.Children.Add(slider);
        mission.Children.Add(Caption(Text[key + "Help"]));
        if (!supported) mission.Children.Add(Caption(Text["VsUnavailable"]));
    }
    private FrameworkElement Clues(bool highlights)
    {
        var grid = new Grid { Height = 64, Background = new SolidColorBrush(highlights ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 0, 0, 0)) };
        clueGrid = grid;
        byte[] levels = highlights ? [231, 235, 239, 243, 247, 251] : [4, 8, 12, 16, 20, 24];
        for (var i = 0; i < levels.Length; i++)
        {
            grid.ColumnDefinitions.Add(new()); var v = levels[i];
            var rune = new TextBlock { Text = "✦", FontSize = 32, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromArgb(255, v, v, v)) };
            Grid.SetColumn(rune, i); grid.Children.Add(rune);
        }
        return new Border { CornerRadius = new(12), Child = grid };
    }
    private void UpdateClues()
    {
        if (clueGrid is null || Selected is not { } state) return;
        byte[] levels = step == 1 ? [255, 231, 235, 239, 243, 247, 251] : [0, 4, 8, 12, 16, 20, 24];
        var raw = levels.SelectMany(v => new byte[] { v, v, v, 255 }).ToArray();
        var output = controller?.HasPreview == true || physicalOperation ? raw :
            DisplayImagePreview.Render(raw, profile, state, controller?.ProfileFor(state.Identity));
        Color At(int index) => Color.FromArgb(255, output[index * 4 + 2], output[index * 4 + 1], output[index * 4]);
        clueGrid.Background = new SolidColorBrush(At(0));
        for (var i = 0; i < clueGrid.Children.Count; i++) ((TextBlock)clueGrid.Children[i]).Foreground = new SolidColorBrush(At(i + 1));
    }
    private void UpdateStatus()
    {
        var active = controller?.HasPreview == true;
        monitors.IsEnabled = refresh.IsEnabled = !busy && !active && !drafting;
        start.IsEnabled = !busy && !active && Selected?.CanAdjust == true;
        chooser.Visibility = drafting || active ? Visibility.Collapsed : Visibility.Visible;
        adjustment.Visibility = drafting || active ? Visibility.Visible : Visibility.Collapsed;
        keep.Content = Text[active ? "VsKeep" : "VsTestMonitor"];
        undo.Content = Text[active ? "VsReturnDraft" : "VsUndo"];
        keep.IsEnabled = !busy && !updating && (drafting || active);
        undo.IsEnabled = !busy && (drafting || active);
        beforeButton.Visibility = adjustedButton.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        beforeButton.IsEnabled = adjustedButton.IsEnabled = active && !busy;
        beforeButton.IsChecked = requestedBefore; adjustedButton.IsChecked = !requestedBefore;
        foreach (var button in purposes.Concat(ambients)) button.IsEnabled = !busy;
        applying.Text = updating || physicalOperation ? Text[requestedBefore ? "VsApplyingOriginal" : "VsApplying"] : busy ? Text["VsDetecting"] : "";
        foreach (var (control, supported) in sliders) control.IsEnabled = supported && drafting && !active && !busy && !updating;
        restore.IsEnabled = !busy && !updating && Selected is { } current && controller?.HasBackup(current.Identity) == true;
        var seconds = Math.Max(0, (int)Math.Ceiling(((controller?.PreviewUntil ?? DateTimeOffset.UtcNow) - DateTimeOffset.UtcNow).TotalSeconds));
        clock.Text = active ? Text.Format("VsCountdown", seconds) : Text["VsLocalOnly"];
        clock.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        image.SetPhysicalPreview(active || physicalOperation);
        UpdateClues();
        detail.Text = Selected switch
        {
            { AdvancedColor: not false } => Text["McAdvancedColor"],
            { CanAdjust: true } state => Text.Format("VsCapabilities", state.Brightness is null ? "—" : state.Brightness.Percent.ToString("0"),
                state.Contrast is null ? "—" : state.Contrast.Percent.ToString("0"), Text[state.Ramp is null ? "VsGammaUnavailable" : "VsGammaReady"]),
            _ => Text["VsUnsupported"]
        };
        if (active) ticker.Start(); else ticker.Stop();
    }
    private async Task<bool> Confirm(string title, string message)
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = message, PrimaryButtonText = Text["VsContinue"],
            CloseButtonText = Text["McCancel"], DefaultButton = ContentDialogButton.Close };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    private Task StartAsync()
    {
        if (Selected is not { CanAdjust: true } || controller is null) return Task.CompletedTask;
        drafting = true; MainWindow.Shell!.CalibrationDraftOpen = true;
        requestedBefore = showingOriginal = false; step = 0; Array.Clear(completed);
        status.IsOpen = true; status.Severity = InfoBarSeverity.Informational; status.Message = Text["VsLocalOnly"];
        RenderMission(); UpdateStatus(); RevealStatus(); return Task.CompletedTask;
    }
    private void RequestLocalPreview()
    {
        if (!drafting || controller?.HasPreview == true || Selected is not { } state) return;
        // The monitor may already carry a confirmed Lotus profile. Preview only the relative draft
        // difference, rather than applying its correction twice to an image on that monitor.
        var current = controller?.ProfileFor(state.Identity) ?? new CalibrationProfile();
        image.Request(profile, state, current);
        UpdateClues();
    }
    private async Task TestMonitorAsync()
    {
        if (controller is null || Selected is not { } state || !await Confirm(Text["VsTestMonitor"], Text["VsConfirm"])) return;
        var chosen = profile;
        // One explicit physical transaction after the local draft, never while dragging a slider.
        await Act(() =>
        {
            var fresh = MainWindow.Shell!.CalibrationDriver.Read(state.Identity);
            return fresh is null ? MonitorColorResult.Pending : controller.Begin(fresh, chosen);
        });
        if (controller.LastResult == MonitorColorResult.Preview) { verifiedProfile = chosen; Show(MonitorColorResult.Preview); }
        if (!IsLoaded && controller.HasPreview) await Task.Run(() => controller.CancelPreview());
        RenderMission(); UpdateStatus();
    }
    private async Task QueueImmediateAsync()
    {
        if (controller?.HasPreview == true) pending = UpdateAsync(++revision, profile, false);
        await pending;
    }
    private void RequestComparison(bool before)
    {
        if (controller?.HasPreview != true || busy) return;
        requestedBefore = before;
        // No slider debounce or wait for an older queued request; only the latest request survives.
        pending = UpdateAsync(++revision, profile, before);
        RenderMission(); UpdateStatus();
    }
    private async Task UpdateAsync(int ticket, CalibrationProfile value, bool before)
    {
        if (ticket != revision || !IsLoaded || controller is null) return;
        await operations.WaitAsync();
        var shell = MainWindow.Shell;
        try
        {
            if (ticket != revision || !IsLoaded || !controller.HasPreview) return;
            shell?.BeginCalibrationWork();
            updating = true; UpdateStatus();
            var result = await Task.Run(() => controller.Update(value, before));
            if (result == MonitorColorResult.Preview) { showingOriginal = before; verifiedProfile = value; }
            Show(result);
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Live display calibration"); Show(MonitorColorResult.DriverError); }
        finally { if (updating) shell?.EndCalibrationWork(); updating = false; operations.Release(); if (IsLoaded) UpdateStatus(); }
    }
    private async Task Act(Func<MonitorColorResult> action)
    {
        var shell = MainWindow.Shell; shell?.BeginCalibrationWork();
        await operations.WaitAsync(); busy = physicalOperation = true; UpdateStatus();
        try { Show(await Task.Run(action)); RevealStatus(); }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Display calibration action"); Show(MonitorColorResult.DriverError); }
        finally { busy = physicalOperation = false; operations.Release(); shell?.EndCalibrationWork(); if (IsLoaded) UpdateStatus(); }
    }
    private void RevealStatus() => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
    {
        if (IsLoaded && status.IsOpen) status.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0 });
    });
    private async Task RestoreAsync()
    {
        if (Selected is not { } selected || controller is null || !await Confirm(Text["VsRestore"], Text["VsRestoreConfirm"])) return;
        ++revision;
        await pending;
        var current = await Task.Run(() => MainWindow.Shell!.CalibrationDriver.Read(selected.Identity));
        if (current is not null) await Act(() => controller.Restore(current));
        await ReloadAsync(); RenderMission();
    }
    private void Show(MonitorColorResult result)
    {
        status.IsOpen = true; status.Severity = result == MonitorColorResult.Preview ? InfoBarSeverity.Success :
            result is MonitorColorResult.Restored or MonitorColorResult.Kept or MonitorColorResult.NoChange ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        var unchanged = !showingOriginal && controller?.PreviewMatchesOriginal == true;
        status.Message = result == MonitorColorResult.Preview ? Text[showingOriginal ? "VsShowingOriginal" : unchanged ? "VsShowingUnchanged" : "VsShowingAdjusted"] : Text["VsResult" + result];
    }
    private void OnChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!IsLoaded || controller is null) return;
        if (!busy && !updating) Show(controller.LastResult);
        UpdateStatus();
        if (!controller.HasPreview) { ++revision; RenderMission(); }
    });
}
