using System.Runtime.InteropServices.WindowsRuntime;
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
    private readonly StackPanel root = new() { Spacing = 14 };
    private readonly ComboBox monitors = new() { HorizontalAlignment = HorizontalAlignment.Stretch, DisplayMemberPath = nameof(CalibrationState.Label) };
    private readonly Grid workspace = new() { ColumnSpacing = 20, RowSpacing = 16 };
    private readonly StackPanel mission = new() { Spacing = 12 };
    private readonly StackPanel scene = new() { Spacing = 10 };
    private readonly Grid missionNav = new() { ColumnSpacing = 8, Margin = new(0, 6, 0, 0) };
    private readonly Button finish = new();
    private readonly HyperlinkButton skipToEnd = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly DisplayComparisonPreview image = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock objective = new() { TextWrapping = TextWrapping.Wrap, FontSize = 20, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock detail = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock clock = new() { TextWrapping = TextWrapping.Wrap };
    private readonly InfoBar status = new() { IsClosable = false };
    private readonly Button start = new(), keep = new(), undo = new(), restore = new(), refresh = new(), next = new(), previous = new();
    private readonly ToggleButton beforeButton = new(), adjustedButton = new();
    private readonly StackPanel chooser = new() { Spacing = 16 }, adjustment = new() { Spacing = 14 };
    private readonly Grid purposeCards = new() { ColumnSpacing = 10, RowSpacing = 10 }, ambientCards = new() { ColumnSpacing = 10, RowSpacing = 10 };
    private readonly List<ToggleButton> purposes = [], ambients = [];
    private readonly TextBlock suggestion = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock applying = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Grid progress = new() { ColumnSpacing = 8 };
    private readonly List<Button> steps = [];
    private readonly List<(Slider Control, bool Supported)> sliders = [];
    private Grid? clueGrid;
    // Premium chrome: hero art, capability/suggestion tiles, step header and icon labels. Visual only.
    private readonly Image heroArt = new() { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border sceneHost = new(), missionHost = new(), clockChip = new();
    private readonly TextBlock[] capabilityValues = new TextBlock[4], capabilityStates = new TextBlock[4], suggestedValues = new TextBlock[4], suggestedDeltas = new TextBlock[4];
    private readonly Border[] capabilityTiles = new Border[4];
    private readonly Dictionary<ToggleButton, (Border Badge, FontIcon Icon, TextBlock Title, TextBlock Hint)> choiceParts = [];
    private readonly FontIcon missionIcon = new() { FontSize = 20 }, keepIcon = new() { FontSize = 16 }, undoIcon = new() { FontSize = 16 };
    private readonly TextBlock missionEyebrow = new(), keepLabel = new() { TextWrapping = TextWrapping.Wrap }, undoLabel = new() { TextWrapping = TextWrapping.Wrap };
    private int heroLoad;
    private readonly List<Grid> tileRows = [];
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
            ++revision; ++heroLoad; ticker.Stop(); heroArt.Source = null;
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
        keep.Click += async (_, _) => await FinishAsync();
        finish.Click += async (_, _) => await FinishAsync();
        skipToEnd.Click += (_, _) => { completed[step] = true; step = 3; RenderMission(); };
        WireControls();
    }
    private async Task FinishAsync()
    {
        if (controller?.HasPreview != true) { await TestMonitorAsync(); return; }
        requestedBefore = false;
        if (showingOriginal || verifiedProfile != profile) await QueueImmediateAsync();
        else { ++revision; await pending; }
        if (controller?.HasPreview != true || controller.LastResult != MonitorColorResult.Preview) return;
        await Act(() => controller!.Keep());
        if (controller.LastResult == MonitorColorResult.Kept) { drafting = false; if (MainWindow.Shell is { } shell) shell.CalibrationDraftOpen = false; }
        await ReloadAsync();
    }
    private void WireControls()
    {
        beforeButton.Click += (_, _) => RequestComparison(true);
        adjustedButton.Click += (_, _) => RequestComparison(false);
        restore.Click += async (_, _) => await RestoreAsync();
        refresh.Click += async (_, _) => await ReloadAsync();
        previous.Click += (_, _) => { if (step > 0) { --step; RenderMission(); } };
        // Finishing a step is the confirmation itself; the stepper above stays free to jump around.
        next.Click += (_, _) => { completed[step] = true; if (step < 3) ++step; RenderMission(); };
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
            _ = LoadHeroAsync();
            await ReloadAsync();
            if (controller.LastResult != MonitorColorResult.NoChange) Show(controller.LastResult);
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Display studio load"); Show(MonitorColorResult.DriverError); }
    }
    private static Style StyleOf(string key) => (Style)Application.Current.Resources[key];
    private readonly Brush OnArt = new SolidColorBrush(Color.FromArgb(255, 248, 240, 243));
    private readonly Brush OnArtMuted = new SolidColorBrush(Color.FromArgb(230, 230, 210, 198));
    private static StackPanel IconLabel(FontIcon icon, TextBlock label)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        icon.VerticalAlignment = label.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(icon); panel.Children.Add(label); return panel;
    }
    private static Border Badge(string glyph, double size, string accent, out FontIcon icon)
    {
        icon = new FontIcon { Glyph = glyph, FontSize = size * .46, Foreground = Brush(accent), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        return new Border { Width = size, Height = size, CornerRadius = new(size / 2), Background = Brush("AccentSoftBrush"), Child = icon,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    }
    private static FrameworkElement SectionHeader(string glyph, string title, string? caption)
    {
        var row = new Grid { ColumnSpacing = 14 };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new());
        row.Children.Add(Badge(glyph, 34, "RoseBrush", out _));
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, Style = StyleOf("SubtitleStyle") });
        if (caption is not null) text.Children.Add(Caption(caption));
        Grid.SetColumn(text, 1); row.Children.Add(text); return row;
    }
    private static Border Tile(string name, TextBlock value, TextBlock? state, out Border tile)
    {
        var content = new StackPanel { Spacing = 2 };
        content.Children.Add(new TextBlock { Text = name.ToUpperInvariant(), Style = StyleOf("EyebrowStyle"), Foreground = Brush("MutedBrush"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
        value.FontSize = 20; value.FontWeight = FontWeights.SemiBold; content.Children.Add(value);
        if (state is not null) { state.Style = StyleOf("CaptionStyle"); content.Children.Add(state); }
        tile = new Border { Background = Brush("SubtleBrush"), BorderBrush = Brush("CardBorderBrush"), BorderThickness = new(1), CornerRadius = new(12), Padding = new(12, 10, 12, 10), Child = content };
        return tile;
    }
    private Grid TileRow(IEnumerable<FrameworkElement> tiles)
    {
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        foreach (var tile in tiles) grid.Children.Add(tile);
        ChoiceLayout(grid, 4); tileRows.Add(grid); return grid;
    }
    private FrameworkElement Hero()
    {
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var (glyph, key) in new[] { ("\uE7B3", "VsChipLocal"), ("\uE777", "VsChipSafe"), ("\uE916", "VsChipTimer") })
        {
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            chip.Children.Add(new FontIcon { Glyph = glyph, FontSize = 13, Foreground = Brush("BrandGoldBrush"), VerticalAlignment = VerticalAlignment.Center });
            chip.Children.Add(new TextBlock { Text = Text[key], Style = StyleOf("ChipTextStyle"), Foreground = OnArt });
            chips.Children.Add(new Border { Style = StyleOf("ChipStyle"), CornerRadius = new(14), Padding = new(12, 6, 12, 6),
                Background = new SolidColorBrush(Color.FromArgb(110, 20, 8, 14)), BorderBrush = new SolidColorBrush(Color.FromArgb(70, 238, 193, 168)), BorderThickness = new(1), Child = chip });
        }
        var text = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 680, HorizontalAlignment = HorizontalAlignment.Left, Padding = new(22, 16, 22, 16) };
        text.Children.Add(new Border { Background = Brush("AccentGradientBrush"), CornerRadius = new(10), Padding = new(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = Text["VsBadge"], Foreground = OnArt, FontSize = 11, FontWeight = FontWeights.SemiBold, CharacterSpacing = 60, TextWrapping = TextWrapping.Wrap } });
        text.Children.Add(new TextBlock { Text = Text["VsTitle"], FontSize = 24, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Foreground = OnArt });
        text.Children.Add(new TextBlock { Text = Text["VsIntro"], FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = OnArtMuted });
        text.Children.Add(new ScrollViewer { Content = chips, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled, Margin = new(0, 4, 0, 0) });
        var shade = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 0.4) };
        shade.GradientStops.Add(new GradientStop { Color = Color.FromArgb(242, 18, 7, 13), Offset = 0 });
        shade.GradientStops.Add(new GradientStop { Color = Color.FromArgb(190, 40, 10, 26), Offset = 0.5 });
        shade.GradientStops.Add(new GradientStop { Color = Color.FromArgb(40, 60, 12, 36), Offset = 1 });
        var art = new Grid { Height = 172, Background = new SolidColorBrush(Color.FromArgb(255, 26, 12, 20)) };
        art.Children.Add(heroArt); art.Children.Add(new Border { Background = shade }); art.Children.Add(text);
        return new Border { CornerRadius = new(20), BorderBrush = Brush("CardBorderBrush"), BorderThickness = new(1), Child = art };
    }
    private async Task LoadHeroAsync()
    {
        if (heroArt.Source is not null) return;
        var ticket = ++heroLoad;
        try
        {
            using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", "ColorCalibration", "medieval-comparison.png"));
            using var stream = file.AsRandomAccessStream();
            var bitmap = new BitmapImage { DecodePixelWidth = 1280 };
            await bitmap.SetSourceAsync(stream);
            if (ticket == heroLoad && IsLoaded) heroArt.Source = bitmap;
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Display studio hero art"); }
    }
    private void Build()
    {
        root.Children.Clear(); steps.Clear(); progress.Children.Clear(); progress.ColumnDefinitions.Clear(); tileRows.Clear();
        root.Children.Add(Hero());
        for (var i = 0; i < 4; i++)
        {
            var index = i; progress.ColumnDefinitions.Add(new());
            var button = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new(10, 8, 10, 8), CornerRadius = new(12), MinHeight = 48 };
            AutomationProperties.SetName(button, Text["VsStep" + i]);
            button.Click += (_, _) => { step = index; RenderMission(); };
            Grid.SetColumn(button, i); progress.Children.Add(button); steps.Add(button);
        }
        chooser.Children.Clear(); adjustment.Children.Clear();
        root.Children.Add(status);
        root.Children.Add(chooser);

        // Monitor card: which screen, and what Lotus can actually control on it.
        var monitorCard = new StackPanel { Spacing = 14 };
        monitorCard.Children.Add(SectionHeader("\uE7F4", Text["VsMonitorTitle"], null));
        AutomationProperties.SetName(monitors, Text["McMonitor"]); monitorCard.Children.Add(monitors);
        string[] tileKeys = ["VsTileBrightness", "VsTileContrast", "VsTileCurve", "VsTileIntensity"];
        var tiles = new List<FrameworkElement>();
        for (var i = 0; i < 4; i++)
        {
            capabilityValues[i] = new TextBlock { Text = "—" }; capabilityStates[i] = new TextBlock();
            tiles.Add(Tile(Text[tileKeys[i]], capabilityValues[i], capabilityStates[i], out capabilityTiles[i]));
        }
        monitorCard.Children.Add(TileRow(tiles));
        detail.Style = StyleOf("CaptionStyle"); monitorCard.Children.Add(detail);
        chooser.Children.Add(new Border { Style = StyleOf("CardStyle"), Child = monitorCard });

        var recommendations = new StackPanel { Spacing = 14 };
        recommendations.Children.Add(SectionHeader("\uE771", Text["VsSuggestTitle"], Text["VsSuggestIntro"]));
        recommendations.Children.Add(new TextBlock { Text = Text["VsPurpose"], Style = StyleOf("BodyStrongStyle"), Margin = new(0, 4, 0, 0) });
        MakeChoices(purposeCards, purposes, ["VsCardEveryday", "VsCardGaming", "VsCardEditing", "VsCardMovies"],
            ["\uE7F4", "\uE7FC", "\uE790", "\uE714"], true);
        recommendations.Children.Add(purposeCards);
        recommendations.Children.Add(new TextBlock { Text = Text["VsAmbient"], Style = StyleOf("BodyStrongStyle"), Margin = new(0, 4, 0, 0) });
        MakeChoices(ambientCards, ambients, ["VsAmbientNormal", "VsAmbientDim", "VsAmbientBright"],
            ["\uE706", "\uE708", "\uE793"], false);
        recommendations.Children.Add(ambientCards);

        // Suggested starting point as value tiles instead of a single line of text.
        var suggested = new StackPanel { Spacing = 10 };
        var suggestedHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        suggestedHead.Children.Add(new FontIcon { Glyph = "\uE734", FontSize = 14, Foreground = Brush("GoldBrush"), VerticalAlignment = VerticalAlignment.Center });
        suggestedHead.Children.Add(new TextBlock { Text = Text["VsSuggestedTitle"], Style = StyleOf("BodyStrongStyle"), VerticalAlignment = VerticalAlignment.Center });
        suggested.Children.Add(suggestedHead);
        suggestion.Style = StyleOf("CaptionStyle"); suggested.Children.Add(suggestion);
        string[] valueKeys = ["VsTileBrightness", "VsTileContrast", "VsTileGamma", "VsTileIntensity"];
        var valueTiles = new List<FrameworkElement>();
        for (var i = 0; i < 4; i++)
        {
            suggestedValues[i] = new TextBlock { Text = "—", Foreground = Brush("RoseBrush") }; suggestedDeltas[i] = new TextBlock();
            valueTiles.Add(Tile(Text[valueKeys[i]], suggestedValues[i], suggestedDeltas[i], out _));
        }
        suggested.Children.Add(TileRow(valueTiles));
        recommendations.Children.Add(new Border { Background = Brush("HeroBrush"), BorderBrush = Brush("CardBorderBrush"), BorderThickness = new(1), CornerRadius = new(16), Padding = new(16), Margin = new(0, 6, 0, 0), Child = suggested });

        start.Content = IconLabel(new FontIcon { Glyph = "\uE768", FontSize = 16 }, new TextBlock { Text = Text["VsStart"], TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
        start.Style = StyleOf("PrimaryButtonStyle"); start.MinHeight = 44;
        AutomationProperties.SetName(start, Text["VsStart"]);
        start.HorizontalAlignment = HorizontalAlignment.Stretch; recommendations.Children.Add(start);
        chooser.Children.Add(new Border { Style = StyleOf("CardStyle"), Child = recommendations });

        applying.Style = StyleOf("CaptionStyle"); applying.Foreground = Brush("GoldBrush");
        root.Children.Add(applying);
        root.Children.Add(adjustment);

        var head = new Grid { ColumnSpacing = 12 };
        head.ColumnDefinitions.Add(new()); head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        head.Children.Add(SectionHeader("\uE790", Text["VsRefineTitle"], Text["VsRefineHint"]));
        var clockRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        clockRow.Children.Add(new FontIcon { Glyph = "\uE916", FontSize = 13, Foreground = Brush("GoldBrush"), VerticalAlignment = VerticalAlignment.Center });
        clock.Style = StyleOf("ChipTextStyle"); clock.MaxWidth = 360; clockRow.Children.Add(clock);
        clockChip.Style = StyleOf("ChipStyle"); clockChip.CornerRadius = new(14); clockChip.Padding = new(12, 6, 12, 6);
        clockChip.BorderBrush = Brush("GoldBrush"); clockChip.BorderThickness = new(1); clockChip.VerticalAlignment = VerticalAlignment.Center; clockChip.Child = clockRow;
        Grid.SetColumn(clockChip, 1); head.Children.Add(clockChip);
        adjustment.Children.Add(head);
        var comparison = new Grid { ColumnSpacing = 10 }; comparison.ColumnDefinitions.Add(new()); comparison.ColumnDefinitions.Add(new());
        beforeButton.Content = IconLabel(new FontIcon { Glyph = "\uE7A7", FontSize = 14 }, new TextBlock { Text = Text["VsBeforeButton"] });
        adjustedButton.Content = IconLabel(new FontIcon { Glyph = "\uE790", FontSize = 14 }, new TextBlock { Text = Text["VsAdjustedButton"] });
        AutomationProperties.SetName(beforeButton, Text["VsBeforeButton"]); AutomationProperties.SetName(adjustedButton, Text["VsAdjustedButton"]);
        beforeButton.HorizontalAlignment = adjustedButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        beforeButton.MinHeight = adjustedButton.MinHeight = 40;
        comparison.Children.Add(beforeButton); comparison.Children.Add(adjustedButton); Grid.SetColumn(adjustedButton, 1);
        adjustment.Children.Add(comparison);
        adjustment.Children.Add(progress);
        image.SetLabels(Text["VsSplitBefore"], Text["VsSplitAfter"], Text["VsSplitHelp"]);
        scene.Children.Clear(); scene.Children.Add(image);
        var limits = new Grid { ColumnSpacing = 8 };
        limits.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); limits.ColumnDefinitions.Add(new());
        limits.Children.Add(new FontIcon { Glyph = "\uE946", FontSize = 13, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 2, 0, 0) });
        var limitsText = Caption(Text["VsLocalLimits"]); Grid.SetColumn(limitsText, 1); limits.Children.Add(limitsText);
        scene.Children.Add(limits);
        sceneHost.Style = StyleOf("CardStyle"); sceneHost.Padding = new(14); sceneHost.VerticalAlignment = VerticalAlignment.Top; sceneHost.Child = scene;
        missionHost.Style = StyleOf("CardStyle"); missionHost.Child = mission;
        workspace.Children.Clear(); workspace.ColumnDefinitions.Clear(); workspace.RowDefinitions.Clear();
        workspace.ColumnDefinitions.Add(new() { Width = new GridLength(1.6, GridUnitType.Star) }); workspace.ColumnDefinitions.Add(new());
        workspace.RowDefinitions.Add(new()); workspace.RowDefinitions.Add(new() { Height = GridLength.Auto });
        workspace.Children.Add(sceneHost); workspace.Children.Add(missionHost);
        adjustment.Children.Add(workspace); Layout(ActualWidth);
        var actions = new Grid { ColumnSpacing = 10, RowSpacing = 8 };
        actions.ColumnDefinitions.Add(new()); actions.ColumnDefinitions.Add(new());
        keep.Content = IconLabel(keepIcon, keepLabel); undo.Content = IconLabel(undoIcon, undoLabel);
        keepLabel.FontWeight = FontWeights.SemiBold;
        keep.Style = StyleOf("PrimaryButtonStyle");
        foreach (var button in new[] { keep, undo }) { button.HorizontalAlignment = HorizontalAlignment.Stretch; button.MinHeight = 44; actions.Children.Add(button); }
        Grid.SetColumn(undo, 1); adjustment.Children.Add(actions);
        var safety = new StackPanel { Spacing = 10 };
        restore.Content = IconLabel(new FontIcon { Glyph = "\uE777", FontSize = 14 }, new TextBlock { Text = Text["VsRestore"] });
        refresh.Content = IconLabel(new FontIcon { Glyph = "\uE72C", FontSize = 14 }, new TextBlock { Text = Text["McRefresh"] });
        AutomationProperties.SetName(restore, Text["VsRestore"]); AutomationProperties.SetName(refresh, Text["McRefresh"]);
        safety.Children.Add(restore); safety.Children.Add(refresh);
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
        var safetyHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        safetyHeader.Children.Add(new FontIcon { Glyph = "\uEA18", FontSize = 16, Foreground = Brush("GoldBrush"), VerticalAlignment = VerticalAlignment.Center });
        safetyHeader.Children.Add(new TextBlock { Text = Text["McLimitsTitle"], VerticalAlignment = VerticalAlignment.Center });
        var expander = new Expander { Header = safetyHeader, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = safety };
        AutomationProperties.SetName(expander, Text["McLimitsTitle"]);
        root.Children.Add(expander);
        RenderMission();
    }
    private void MakeChoices(Grid grid, List<ToggleButton> buttons, string[] keys, string[] glyphs, bool use)
    {
        foreach (var old in buttons) choiceParts.Remove(old);
        grid.Children.Clear(); grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear(); buttons.Clear();
        for (var i = 0; i < keys.Length; i++)
        {
            var index = i;
            var content = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left };
            var badge = Badge(glyphs[i], use ? 38 : 32, use ? "RoseBrush" : "GoldBrush", out var icon);
            content.Children.Add(badge);
            var title = new TextBlock { Text = Text[keys[i]], TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, FontSize = use ? 15 : 14 };
            var hint = new TextBlock { Text = Text[keys[i] + "Hint"], TextWrapping = TextWrapping.Wrap, FontSize = 12, LineHeight = 16 };
            content.Children.Add(title); content.Children.Add(hint);
            var button = new ToggleButton { Content = content, MinHeight = use ? 112 : 88, Padding = new Thickness(16, 14, 16, 14), CornerRadius = new(16),
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Top };
            AutomationProperties.SetName(button, Text[keys[i]]);
            AutomationProperties.SetHelpText(button, Text[keys[i] + "Hint"]);
            button.Click += (_, _) => { if (use) purposeIndex = index; else ambientIndex = index; usingSavedProfile = false; UpdateChoiceStates(); PrepareSuggestion(); };
            grid.Children.Add(button); buttons.Add(button); choiceParts[button] = (badge, icon, title, hint);
        }
        UpdateChoiceStates();
    }
    private void UpdateChoiceStates()
    {
        void Paint(ToggleButton button, bool selected, string accent)
        {
            button.IsChecked = selected; button.BorderBrush = Brush(selected ? accent : "CardBorderBrush");
            // Checked template uses a filled rose surface: explicit contrast for icon, badge, label and hint.
            if (!choiceParts.TryGetValue(button, out var parts)) return;
            parts.Icon.Foreground = Brush(selected ? "OnRoseBrush" : accent);
            parts.Badge.Background = selected ? new SolidColorBrush(Color.FromArgb(56, 255, 255, 255)) : Brush("AccentSoftBrush");
            parts.Title.Foreground = Brush(selected ? "OnRoseBrush" : "MainTextBrush");
            parts.Hint.Foreground = Brush(selected ? "OnRoseBrush" : "MutedBrush");
            parts.Hint.Opacity = selected ? .86 : 1;
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
        Grid.SetColumn(missionHost, wide ? 1 : 0); Grid.SetRow(missionHost, wide ? 0 : 1);
        ChoiceLayout(purposeCards, width >= 720 ? 4 : width >= 340 ? 2 : 1);
        foreach (var row in tileRows) ChoiceLayout(row, width >= 640 ? 4 : 2);
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
        suggestion.Text = Text[usingSavedProfile ? "VsUsingSaved" : purposeIndex == (int)DisplayUse.Editing ? "VsSuggestEditing" : "VsSuggestPreference"];
        string[] values = [profile.Brightness is { } br ? br.ToString("0") + "%" : "—", profile.Contrast is { } co ? co.ToString("0") + "%" : "—",
            profile.Gamma.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture) + "×", saturation is { } sat ? sat.ToString("0") + "%" : "—"];
        // Each tile compares the suggestion with what the monitor shows now, so the two rows never read as duplicates.
        var state = Selected;
        string[] now = [state?.Brightness is { } nb ? nb.Percent.ToString("0") + "%" : "—", state?.Contrast is { } nc ? nc.Percent.ToString("0") + "%" : "—",
            (1d).ToString("0.00", System.Globalization.CultureInfo.CurrentCulture) + "×", state?.Vibrance is { } nv ? nv.Percent(nv.Current).ToString("0") + "%" : "—"];
        for (var i = 0; i < suggestedValues.Length; i++)
        {
            if (suggestedValues[i] is not { } tile || suggestedDeltas[i] is not { } delta) continue;
            tile.Text = values[i];
            delta.Text = values[i] == "—" ? Text["VsTileMissing"] : values[i] == now[i] ? Text["VsTileSame"] : Text.Format("VsTileNow", now[i]);
        }
        AutomationProperties.SetHelpText(suggestion, Text.Format("VsSuggestValues", profile.Brightness?.ToString("0") ?? "—", profile.Contrast?.ToString("0") ?? "—", profile.Gamma.ToString("0.00"), saturation?.ToString("0") ?? "—"));
    }
    private void RenderMission()
    {
        rendering = true;
        try
        {
            for (var i = 0; i < steps.Count; i++)
            {
                steps[i].Content = StepContent(i);
                steps[i].BorderBrush = Brush(i == step ? "RoseBrush" : "CardBorderBrush");
                steps[i].Background = i == step ? Brush("AccentSoftBrush") : Brush("CardBrush");
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
            mission.Children.Add(MissionHeader());
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
            previous.Content = Text["VsPrevious"]; next.Content = Text["VsNext"]; previous.IsEnabled = step > 0; next.IsEnabled = step < 3;
            next.Visibility = step < 3 ? Visibility.Visible : Visibility.Collapsed;
            // Last step: "Concluir" runs the same action as the main button below (test, then save).
            finish.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
            finish.Content = Text["VsFinish"]; finish.Style = StyleOf("PrimaryButtonStyle"); finish.HorizontalAlignment = HorizontalAlignment.Stretch;
            finish.IsEnabled = keep.IsEnabled; AutomationProperties.SetName(finish, Text["VsFinish"]);
            skipToEnd.Content = Text["VsSkipToEnd"]; skipToEnd.Visibility = step < 3 ? Visibility.Visible : Visibility.Collapsed;
            if (missionNav.ColumnDefinitions.Count == 0) { missionNav.ColumnDefinitions.Add(new()); missionNav.ColumnDefinitions.Add(new()); }
            previous.HorizontalAlignment = next.HorizontalAlignment = HorizontalAlignment.Stretch;
            previous.Style = StyleOf("SoftButtonStyle"); next.Style = StyleOf("PrimaryButtonStyle");
            // On the last step only "Voltar" remains, spanning the row.
            Grid.SetColumn(next, 1); Grid.SetColumn(finish, 1);
            missionNav.Children.Add(previous); missionNav.Children.Add(next); missionNav.Children.Add(finish); mission.Children.Add(missionNav);
            mission.Children.Add(skipToEnd);
            UpdateClues();
        }
        finally { rendering = false; }
    }
    private void AddSlider(string key, double value, double min, double max, double increment, bool supported, Action<double> changed)
    {
        var format = increment < 1 ? "0.00" : "0";
        var label = new TextBlock { Text = value.ToString(format, System.Globalization.CultureInfo.CurrentCulture), FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = Brush(supported ? "RoseBrush" : "MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center };
        var labelRow = new Grid { Margin = new(0, 8, 0, 0) };
        labelRow.ColumnDefinitions.Add(new()); labelRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        labelRow.Children.Add(new TextBlock { Text = Text[key], Style = StyleOf("BodyStrongStyle"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        var valueChip = new Border { Background = Brush("AccentSoftBrush"), CornerRadius = new(10), Padding = new(10, 3, 10, 3), MinWidth = 52, Child = label };
        Grid.SetColumn(valueChip, 1); labelRow.Children.Add(valueChip);
        var slider = new Slider { Minimum = min, Maximum = max, StepFrequency = increment, Value = Math.Clamp(value, min, max),
            IsEnabled = supported && drafting && controller?.HasPreview != true && !busy };
        AutomationProperties.SetName(slider, Text[key]);
        sliders.Add((slider, supported));
        slider.ValueChanged += (_, e) =>
        {
            if (rendering) return;
            label.Text = e.NewValue.ToString(format, System.Globalization.CultureInfo.CurrentCulture);
            changed(e.NewValue); DescribeSuggestion(); completed[step] = false;
            RequestLocalPreview();
        };
        mission.Children.Add(labelRow); mission.Children.Add(slider);
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
        keepLabel.Text = Text[active ? "VsKeep" : "VsTestMonitor"]; keepIcon.Glyph = active ? "\uE74E" : "\uE7F4";
        undoLabel.Text = Text[active ? "VsReturnDraft" : "VsUndo"]; undoIcon.Glyph = active ? "\uE7A7" : "\uE711";
        AutomationProperties.SetName(keep, keepLabel.Text); AutomationProperties.SetName(undo, undoLabel.Text);
        keep.IsEnabled = finish.IsEnabled = !busy && !updating && (drafting || active);
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
        clockChip.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        image.SetPhysicalPreview(active || physicalOperation);
        UpdateClues();
        // The capability tiles already show brightness/contrast/curve; the caption only explains blocked monitors.
        detail.Text = Selected switch
        {
            { AdvancedColor: not false } => Text["McAdvancedColor"],
            { CanAdjust: true } => "",
            _ => Text["VsUnsupported"]
        };
        detail.Visibility = string.IsNullOrEmpty(detail.Text) ? Visibility.Collapsed : Visibility.Visible;
        UpdateCapabilities();
        if (active) ticker.Start(); else ticker.Stop();
    }
    private void UpdateCapabilities()
    {
        if (capabilityValues[0] is null) return;
        var state = Selected is { CanAdjust: true } usable ? usable : null;
        (string Value, bool Ready)[] tiles =
        [
            (state?.Brightness is { } br ? br.Percent.ToString("0") + "%" : "—", state?.Brightness is not null),
            (state?.Contrast is { } co ? co.Percent.ToString("0") + "%" : "—", state?.Contrast is not null),
            (state?.Ramp is not null ? "SDR" : "—", state?.Ramp is not null),
            (state?.Vibrance is { } vi ? vi.Percent(vi.Current).ToString("0") + "%" : "—", state?.Vibrance is not null)
        ];
        for (var i = 0; i < tiles.Length; i++)
        {
            capabilityValues[i].Text = tiles[i].Value;
            capabilityValues[i].Foreground = Brush(tiles[i].Ready ? "MainTextBrush" : "MutedBrush");
            // The value already says it is available; only call out what this monitor cannot do.
            capabilityStates[i].Text = Text["VsTileMissing"];
            capabilityStates[i].Visibility = tiles[i].Ready ? Visibility.Collapsed : Visibility.Visible;
            capabilityTiles[i].Opacity = tiles[i].Ready ? 1 : .7;
        }
    }
    private FrameworkElement StepContent(int index)
    {
        var current = index == step; var done = completed[index];
        var mark = new Border { Width = 26, Height = 26, CornerRadius = new(13), VerticalAlignment = VerticalAlignment.Center,
            Background = current ? Brush("AccentGradientBrush") : done ? Brush("SuccessBrush") : Brush("SubtleBrush") };
        mark.Child = done && !current
            ? new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = Brush("OnRoseBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            : new TextBlock { Text = (index + 1).ToString(), FontSize = 12, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush(current ? "OnRoseBrush" : "MainTextBrush") };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(mark);
        row.Children.Add(new TextBlock { Text = Text["VsStep" + index], VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Brush(current || done ? "MainTextBrush" : "MutedBrush") });
        return row;
    }
    private FrameworkElement MissionHeader()
    {
        missionIcon.Glyph = step switch { 0 => "\uE708", 1 => "\uE706", 2 => "\uE790", _ => "\uE73E" };
        missionIcon.Foreground = Brush("RoseBrush");
        missionIcon.HorizontalAlignment = HorizontalAlignment.Center; missionIcon.VerticalAlignment = VerticalAlignment.Center;
        missionEyebrow.Text = Text.Format("VsStepOf", step + 1).ToUpperInvariant(); missionEyebrow.Style = StyleOf("EyebrowStyle");
        missionEyebrow.VerticalAlignment = VerticalAlignment.Center;
        // Reused elements must leave their previous parent before joining the new header row.
        if (missionIcon.Parent is Border previousBadge) previousBadge.Child = null;
        if (missionEyebrow.Parent is Panel previousRow) previousRow.Children.Remove(missionEyebrow);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(new Border { Width = 40, Height = 40, CornerRadius = new(20), Background = Brush("AccentSoftBrush"), Child = missionIcon });
        row.Children.Add(missionEyebrow);
        return row;
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
