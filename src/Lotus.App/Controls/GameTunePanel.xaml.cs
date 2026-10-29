using Lotus.Core.Games;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Lotus.App.Controls;

// Actions that live in "Meus Jogos" (they need its dialogs); the panel only asks for them.
public sealed record TuneActions(Func<Task> Analyze, Func<Task> Precompile, Func<Task> Measure, Action OpenShaderTool, Func<Task> Remove);

// "Otimizar este jogo": one place for everything Lotus knows about a game and can do for it.
public sealed partial class GameTunePanel : UserControl
{
    private GamesViewModel? vm;
    private GameCard? card;
    private TuneActions? actions;
    private GamesViewModel.GameTune? tune;
    private Control? returnFocus;
    public bool IsOpen => Visibility == Visibility.Visible;

    public GameTunePanel()
    {
        InitializeComponent();
        KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Escape) { Close(); e.Handled = true; } };
    }

    public async void Open(GamesViewModel model, GameCard game, TuneActions acts)
    {
        vm = model; card = game; actions = acts;
        returnFocus = XamlRoot is { } root ? Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root) as Control : null;
        Eyebrow.Text = model.Text["TuneOpen"].ToUpperInvariant(); GameName.Text = game.Name;
        Chips.Children.Clear(); Body.Children.Clear(); Body.Children.Add(Loading); Loading.IsActive = true;
        PrimaryAction.Visibility = SecondaryAction.Visibility = Footer.Visibility = Visibility.Collapsed;
        LoadArt(game);
        Visibility = Visibility.Visible;
        Animate(Backdrop, "Opacity", 0, 1, 220); Animate(SheetShift, "X", 560, 0, 380, new CubicEase { EasingMode = EasingMode.EaseOut });
        DispatcherQueue.TryEnqueue(() => CloseButton.Focus(FocusState.Programmatic));
        if (MainWindow.Shell?.AdaptiveSync is null && XamlRoot is { } xamlRoot) await AskAdaptiveSyncAsync(xamlRoot, model);
        await Reload();
    }

    public void Close()
    {
        if (!IsOpen) return;
        var sb = new Storyboard();
        Add(sb, Backdrop, "Opacity", 1, 0, 180); Add(sb, SheetShift, "X", 0, 560, 260, new CubicEase { EasingMode = EasingMode.EaseIn });
        sb.Completed += (_, _) => { Visibility = Visibility.Collapsed; HeaderArt.Source = null; returnFocus?.Focus(FocusState.Programmatic); };
        sb.Begin();
    }

    private async Task Reload()
    {
        if (vm is null || card is null) return;
        tune = await vm.TuneAsync(card.Game);
        Body.Children.Clear();
        if (tune is null) { Body.Children.Add(Caption(vm.Text["GsFailed"])); return; }
        BuildChips(tune);
        Body.Children.Add(Section(vm.Text["TuneRecommended"], RecommendedTiles(tune)));
        Body.Children.Add(Section(vm.Text["TuneTips"], TipList(tune)));
        Body.Children.Add(Section(vm.Text["LlTitle"], LowLatency(tune)));
        Body.Children.Add(Section(vm.Text["TuneLast"], LastCheck(tune)));
        Body.Children.Add(Links());
        BuildFooter(tune);
        Motion.Settle(Body);
    }

    private void BuildChips(GamesViewModel.GameTune t)
    {
        Chips.Children.Clear();
        if (t.TechChips.Count == 0) { Chips.Children.Add(new TextBlock { Text = vm!.Text["TuneNothingDetected"], FontSize = 12, Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0xE6, 0xD6, 0xDE)), TextWrapping = TextWrapping.Wrap, MaxWidth = 460 }); return; }
        foreach (var chip in t.TechChips)
            Chips.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 3, 10, 3), BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x66, 0x2A, 0x14, 0x24)), BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x88, 0xEE, 0xC1, 0xA8)),
                Child = new TextBlock { Text = chip, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0xEE, 0xC1, 0xA8)) },
            });
    }

    private UIElement RecommendedTiles(GamesViewModel.GameTune t)
    {
        var r = t.Settings.Recommendation; var text = vm!.Text;
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var upscale = r.Upscaler == Upscaler.None ? text["GsLineNoUpscale"] : $"{text["GsUpscaler" + r.Upscaler]} · {text["GsMode" + r.Mode]}";
        var tiles = new[] { (text["TuneQuality"], text["GsTier" + r.Tier]), (text["TuneTextures"], text["GsTier" + r.TextureTier]), (text["TuneUpscaler"], upscale), (text["TuneCap"], $"{r.FpsCap} FPS") };
        for (var i = 0; i < tiles.Length; i++)
        {
            var tile = new Border { Padding = new Thickness(14, 12, 14, 12), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Background = Res("CardBrush"), BorderBrush = Res("CardBorderBrush") };
            var s = new StackPanel { Spacing = 4 };
            s.Children.Add(new TextBlock { Text = tiles[i].Item1.ToUpperInvariant(), FontSize = 10.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, CharacterSpacing = 80, Foreground = Res("RoseBrush") });
            s.Children.Add(new TextBlock { Text = tiles[i].Item2, FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            tile.Child = s; Grid.SetColumn(tile, i % 2); Grid.SetRow(tile, i / 2); grid.Children.Add(tile);
        }
        // Lotus Score: the expected FPS of this game on this PC (from the player's own test), or an invitation to run it.
        var score = new Border { Padding = new Thickness(14, 10, 14, 10), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Background = Res("AccentSoftBrush"), BorderBrush = Res("RoseBrush") };
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        line.Children.Add(new FontIcon { Glyph = "", FontSize = 16, Foreground = Res("RoseBrush"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) });
        line.Children.Add(new TextBlock { Text = t.ScoreLine ?? t.ScoreHint ?? text["TuneScoreHint"], FontSize = 12.5, FontWeight = t.ScoreLine is null ? Microsoft.UI.Text.FontWeights.Normal : Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxWidth = 430 });
        score.Child = line;
        var hwPanel = new StackPanel { Spacing = 8 }; hwPanel.Children.Add(score); hwPanel.Children.Add(Caption(t.Settings.Hardware));
        // The FreeSync/G-Sync answer the settings are based on, with a way to change it.
        var sync = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var noVrr = vm!.MonitorSync() is { Source: "nvapi", VrrPossible: false };
        sync.Children.Add(new TextBlock { Text = text[noVrr ? "SyncLineNone" : r.AdaptiveSync ? "SyncLineOn" : "SyncLineOff"], FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center });
        var change = new HyperlinkButton { Content = text["SyncChange"], Padding = new Thickness(6, 2, 6, 2), FontSize = 12 };
        change.Click += async (_, _) => { if (XamlRoot is { } root && await AskAdaptiveSyncAsync(root, vm!)) await Reload(); };
        if (!noVrr) sync.Children.Add(change);
        hwPanel.Children.Add(sync);
        Grid.SetRow(hwPanel, 2); Grid.SetColumnSpan(hwPanel, 2); grid.Children.Add(hwPanel);
        return grid;
    }

    private UIElement TipList(GamesViewModel.GameTune t)
    {
        var list = new StackPanel { Spacing = 10 };
        var n = 1;
        foreach (var (title, body) in t.Tips)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition());
            var badge = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), VerticalAlignment = VerticalAlignment.Top, Background = n == 1 ? Res("AccentGradientBrush") : Res("SubtleBrush"),
                Child = new TextBlock { Text = n.ToString(), FontSize = 12.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = n == 1 ? new SolidColorBrush(Microsoft.UI.Colors.White) : Res("MainTextBrush") } };
            var s = new StackPanel { Spacing = 3 }; Grid.SetColumn(s, 1);
            s.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            s.Children.Add(new TextBlock { Text = body, FontSize = 12.5, Opacity = 0.82, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(badge); row.Children.Add(s);
            var card = new Border { Padding = new Thickness(14, 12, 14, 12), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Background = Res("CardBrush"), BorderBrush = n == 1 ? Res("RoseBrush") : Res("CardBorderBrush"), Child = row };
            list.Children.Add(card); n++;
        }
        var support = t.Settings.Support is { CanApply: true } sp ? vm!.Text.Format("TuneCanApply", sp.EngineLabel) : vm!.Text["TuneManual"];
        list.Children.Add(Caption(support));
        return list;
    }

    // Frame pacing: RivaTuner's limiter for this game only, at the exact paced cap. "Fluid" (front edge sync) keeps every
    // frame on screen for the same time, like a console; "Responsive" (NVIDIA Reflex) trades that for less delay.
    private UIElement LowLatency(GamesViewModel.GameTune t)
    {
        var text = vm!.Text; var info = t.LowLatency; var r = t.Settings.Recommendation;
        var culture = System.Globalization.CultureInfo.GetCultureInfo(text.Language);
        var cap = r.PacedCap > 0 ? r.PacedCap : r.FpsCap;
        var capText = cap.ToString(Math.Abs(cap - Math.Round(cap)) < 0.001 ? "0" : "0.0", culture);
        var box = new Border { Padding = new Thickness(16, 14, 16, 14), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Background = Res("CardBrush"), BorderBrush = info is { On: true } ? Res("SuccessBrush") : Res("CardBorderBrush") };
        var s = new StackPanel { Spacing = 8 }; box.Child = s;
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        head.Children.Add(new FontIcon { Glyph = "", FontSize = 18, Foreground = Res(info is { On: true } ? "SuccessBrush" : "RoseBrush") });
        // The state shows the cap actually written in RivaTuner (it may differ from today's recommendation).
        var current = info?.Cap is { } written ? written.ToString(Math.Abs(written - Math.Round(written)) < 0.001 ? "0" : "0.0", culture) : capText;
        var state = info?.Mode switch { "fluid" => text.Format("LlStateFluid", current), "responsive" => text.Format("LlStateOn", current), _ => text["LlStateOff"] };
        if (info is { On: true, Cap: { } w } && Math.Abs(w - cap) > 0.01) state += " · " + text.Format("LlCapUpdate", capText);
        head.Children.Add(new TextBlock { Text = state, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        s.Children.Add(head);
        s.Children.Add(new TextBlock { Text = text[r.AdaptiveSync ? "LlBodyVrr" : "LlBodyFixed"], FontSize = 12.5, Opacity = 0.85, TextWrapping = TextWrapping.Wrap });
        if (info?.State != "ok") { s.Children.Add(Caption(text[info?.State == "noRtss" ? "LlNoRtss" : "LlNoExe"])); return box; }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        Button Make(string label, string? mode, bool primary, string id)
        {
            var b = new Button { Content = label, Style = (Style)Application.Current.Resources[primary ? "PrimaryButtonStyle" : "SoftButtonStyle"], MinHeight = 40 };
            AutomationProperties.SetAutomationId(b, id);
            b.Click += async (_, _) =>
            {
                foreach (var other in buttons.Children.OfType<Button>()) other.IsEnabled = false;
                await vm.SetLimiterAsync(info.Exe!, mode, cap);
                await Reload();
                if (vm.Status is { Length: > 0 } status) Body.Children.Insert(0, new TextBlock { Text = status, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Res("GoldBrush"), TextWrapping = TextWrapping.Wrap });
            };
            return b;
        }
        if (info.Mode != "fluid") buttons.Children.Add(Make(text["LlEnableFluid"], "fluid", info.Mode is null, "tune-fluid"));
        if (info.Mode != "responsive" && info.Nvidia) buttons.Children.Add(Make(text["LlEnable"], "responsive", false, "tune-lowlatency"));
        if (info.On) buttons.Children.Add(Make(text["LlDisable"], null, false, "tune-limiter-off"));
        s.Children.Add(buttons);
        s.Children.Add(Caption(text.Format(r.AdaptiveSync ? "LlNoteVrr" : "LlNoteFixed", info.Exe!)));
        if (!info.Nvidia) s.Children.Add(Caption(text["LlAmd"]));
        return box;
    }

    // Asked before the first analysis (and from "Change" next to the monitor line): is FreeSync/G-Sync on?
    // The monitor probe only adds a recommendation (e.g. "supported but off: turn it on"); the answer decides.
    public static async Task<bool> AskAdaptiveSyncAsync(XamlRoot root, GamesViewModel model)
    {
        if (MainWindow.Shell is not { } shell) return false;
        var text = model.Text; var probe = await Task.Run(model.MonitorSync);
        // The NVIDIA driver says this monitor has no VRR: nothing to ask, nothing to offer.
        if (probe is { Source: "nvapi", VrrPossible: false }) { shell.AdaptiveSync = "off"; return true; }
        var body = new StackPanel { Spacing = 10, MaxWidth = 460 };
        body.Children.Add(RichText.Block(text["SyncAskBody"]));
        var hint = probe switch
        {
            { Source: "nvapi", VrrEnabled: true } => text["SyncHintOn"],
            { Source: "nvapi", VrrPossible: false } => text["SyncHintNone"],
            { VrrAvailableButOff: true } => text["SyncHintAvailable"],
            { Source: "edid", VrrPossible: true } => text["SyncHintFreeSync"],
            { Source: "edid", VrrPossible: false } => text["SyncHintEdidNone"],
            _ => text["SyncHintUnknown"],
        };
        body.Children.Add(new Border
        {
            Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(10), Background = Res("AccentSoftBrush"),
            Child = RichText.Block(probe.MonitorName is { Length: > 0 } name ? $"{name}: {hint}" : hint, 12.5),
        });
        // Three explicit, full-width choices (dialog buttons would cut the long labels); the probe pre-selects one.
        var dialog = new ContentDialog { XamlRoot = root, RequestedTheme = ThemePalette.Element, Title = text["SyncAskTitle"], Content = body, CloseButtonText = text["DlgCancel"] };
        string? choice = null;
        var suggested = probe is { VrrEnabled: true } ? "on" : "off";
        Serilog.Log.Information("Adaptive sync question: probe {Source} possible {Possible} enabled {Enabled}", probe.Source, probe.VrrPossible, probe.VrrEnabled);
        foreach (var (id, label) in new[] { ("on", text["SyncYes"]), ("off", text["SyncNo"]), ("unknown", text["SyncDontKnow"]) })
        {
            var option = new Button
            {
                Content = label, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, MinHeight = 44, Padding = new Thickness(16, 8, 16, 8),
                Style = (Style)Application.Current.Resources[id == suggested ? "PrimaryButtonStyle" : "SoftButtonStyle"],
            };
            AutomationProperties.SetAutomationId(option, "sync-" + id);
            option.Click += (_, _) => { choice = id; Serilog.Log.Information("Adaptive sync answer {Choice}", id); dialog.Hide(); };
            body.Children.Add(option);
        }
        await dialog.ShowAsync();
        if (choice is null) return false;                                // closed without answering: ask again next time
        shell.AdaptiveSync = choice == "on" ? "on" : "off";              // "don't know" plays safe: fixed-rate pacing
        return true;
    }

    private UIElement LastCheck(GamesViewModel.GameTune t)
    {
        var text = vm!.Text;
        var box = new Border { Padding = new Thickness(16, 14, 16, 14), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Background = Res("CardHoverBrush"), BorderBrush = Res("CardBorderBrush") };
        var s = new StackPanel { Spacing = 6 }; box.Child = s;
        if (t.Last is not { } last)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            row.Children.Add(new FontIcon { Glyph = "", FontSize = 20, Foreground = Res("RoseBrush"), VerticalAlignment = VerticalAlignment.Top });
            row.Children.Add(new TextBlock { Text = text["TuneNoLast"], FontSize = 12.5, Opacity = 0.85, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 });
            s.Children.Add(row);
            return box;
        }
        var top = last.Findings[0];
        var culture = System.Globalization.CultureInfo.GetCultureInfo(text.Language);
        var numbers = new Grid { ColumnSpacing = 10 };
        foreach (var _ in new[] { 0, 1, 2 }) numbers.ColumnDefinitions.Add(new ColumnDefinition());
        var values = new[] { (last.AverageFps.ToString("0", culture), "FPS"), (last.Low1Fps.ToString("0", culture), "1% low"), (last.Spikes.ToString(culture), text["IgSpikes"]) };
        for (var i = 0; i < 3; i++)
        {
            var v = new StackPanel(); Grid.SetColumn(v, i);
            v.Children.Add(new TextBlock { Text = values[i].Item1, FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Res("GoldBrush") });
            v.Children.Add(new TextBlock { Text = values[i].Item2, FontSize = 11.5, Opacity = 0.7 });
            numbers.Children.Add(v);
        }
        s.Children.Add(numbers);
        s.Children.Add(new TextBlock { Text = text[top.Key + "Title"], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        s.Children.Add(RichText.Block(text.Format(top.Key + "Body", top.Detail ?? ""), 12.5, 0.85));
        if (last.When is { } when) s.Children.Add(Caption(text.Format("TuneLastWhen", when.ToLocalTime().ToString("g", culture))));
        return box;
    }

    // Secondary tools that used to sit in the card menu.
    private UIElement Links()
    {
        var text = vm!.Text; var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        HyperlinkButton Link(string label, Func<Task> run) { var b = new HyperlinkButton { Content = label, Padding = new Thickness(6, 2, 6, 2) }; b.Click += async (_, _) => { Close(); await run(); }; return b; }
        row.Children.Add(Link(card!.MeasureLabel, actions!.Measure));
        if (card.ShaderLabel is not null) { var open = new HyperlinkButton { Content = card.OpenShaderLabel, Padding = new Thickness(6, 2, 6, 2) }; open.Click += (_, _) => actions.OpenShaderTool(); row.Children.Add(open); }
        row.Children.Add(Link(card.RemoveLabel, actions.Remove));
        return row;
    }

    private enum Act { None, Analyze, Apply, Step, Restore, Precompile }
    private Act primary, secondary;
    private void BuildFooter(GamesViewModel.GameTune t)
    {
        var text = vm!.Text;
        var canApply = t.Settings.Support is { CanApply: true };
        primary = t.Running && vm.CanMeasure ? Act.Analyze : canApply ? (t.Settings.PendingStep is not null ? Act.Step : Act.Apply) : card!.CanPrecompile ? Act.Precompile : Act.None;
        secondary = t.Settings.HasOriginal ? Act.Restore : primary != Act.Precompile && card!.CanPrecompile ? Act.Precompile : Act.None;
        Set(PrimaryAction, primary); Set(SecondaryAction, secondary);
        Footer.Visibility = primary == Act.None && secondary == Act.None ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumnSpan(SecondaryAction, primary == Act.None ? 2 : 1); Grid.SetColumn(PrimaryAction, secondary == Act.None ? 0 : 1); Grid.SetColumnSpan(PrimaryAction, secondary == Act.None ? 2 : 1);
        void Set(Button b, Act a)
        {
            b.Visibility = a == Act.None ? Visibility.Collapsed : Visibility.Visible;
            b.Content = a switch { Act.Analyze => text["TuneAnalyze"], Act.Apply => text["TuneApply"], Act.Step => text["GsStepApply"], Act.Restore => text["TuneRestore"], Act.Precompile => text["TunePrecompile"], _ => "" };
        }
    }
    private async void OnPrimary(object sender, RoutedEventArgs e) => await Run(primary);
    private async void OnSecondary(object sender, RoutedEventArgs e) => await Run(secondary);
    private async Task Run(Act a)
    {
        if (vm is null || card is null || tune is null) return;
        switch (a)
        {
            case Act.Analyze: Close(); await actions!.Analyze(); return;
            case Act.Precompile: Close(); await actions!.Precompile(); return;
            case Act.Apply: await vm.ApplySettingsAsync(card.Game, tune.Settings.Recommendation); break;
            case Act.Step: await vm.ApplyStepAsync(card.Game); break;
            case Act.Restore: await vm.RestoreSettingsAsync(card.Game); break;
            default: return;
        }
        UiSound.Play(UiSound.Cue.Done);
        await Reload();
        if (vm.Status is { Length: > 0 } status) Body.Children.Insert(0, new TextBlock { Text = status, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Res("GoldBrush"), TextWrapping = TextWrapping.Wrap });
    }

    private UIElement Section(string title, UIElement content)
    {
        var s = new StackPanel { Spacing = 10 };
        s.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        s.Children.Add(content);
        return s;
    }
    private static TextBlock Caption(string text) => new() { Text = text, FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    private void LoadArt(GameCard game)
    {
        HeaderArt.Opacity = 0;
        var url = game.Game.Store == GameStores.Steam ? Lotus.Core.Catalog.CatalogText.Hero(game.Game.Id) : game.CoverPath;
        if (url is not null && Uri.TryCreate(url, UriKind.Absolute, out var u))
        {
            var bmp = new BitmapImage(u) { DecodePixelWidth = 1080 };
            bmp.ImageOpened += (_, _) => Animate(HeaderArt, "Opacity", 0, 1, 400);
            HeaderArt.Source = bmp;
        }
    }
    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnBackdrop(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => Close();

    private static void Animate(DependencyObject target, string property, double from, double to, int ms, EasingFunctionBase? ease = null)
    {
        var sb = new Storyboard(); Add(sb, target, property, from, to, ms, ease); sb.Begin();
    }
    private static void Add(Storyboard sb, DependencyObject target, string property, double from, double to, int ms, EasingFunctionBase? ease = null)
    {
        var a = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(Motion.Enabled ? ms : 0), EasingFunction = ease ?? new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(a, target); Storyboard.SetTargetProperty(a, property); sb.Children.Add(a);
    }
}
