using System.ComponentModel;
using Lotus.Core.Integrations;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Lotus.App.Controls;

// Meus Jogos › Overlay: pick the overlay style by looking at it. Two styles (top bar, Neon card) as tiles with a mini
// drawing; the big preview follows the tile chosen, and one button turns that style on (with the usual confirmation).
// The preview is an illustration with example numbers, labeled as such; the layout files themselves are never rewritten.
public sealed class OverlayStylePicker : UserControl
{
    private IntegrationsViewModel? vm;
    private string selected = "top";
    private bool picked; // the player chose a tile; until then the selection follows the active layout
    private readonly StackPanel tileRow = new() { Orientation = Orientation.Horizontal, Spacing = 14 };
    private readonly Grid stage = new() { Height = 300, CornerRadius = new CornerRadius(16) };
    private readonly TextBlock status = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock title = new() { FontSize = 20, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock what = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock hint = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button use = new() { MinHeight = 44 };
    private readonly Button preview = new() { MinHeight = 44 };
    private readonly Button restore = new() { MinHeight = 44 };
    private readonly TextBlock message = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar busy = new() { IsIndeterminate = true };
    private readonly Border statusPill = new() { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private static readonly Color Ok = Color.FromArgb(255, 0x7F, 0xD1, 0xA8);
    private readonly Color rose, gold, accent;

    public OverlayStylePicker()
    {
        var res = Application.Current.Resources;
        rose = res["RoseBrush"] is SolidColorBrush rb ? rb.Color : Color.FromArgb(255, 0xF2, 0x7B, 0xA8);
        gold = res["GoldBrush"] is SolidColorBrush gb ? gb.Color : Color.FromArgb(255, 0xEE, 0xC1, 0xA8);
        accent = res["AccentFillColorDefaultBrush"] is SolidColorBrush ab ? ab.Color : Color.FromArgb(255, 0xD6, 0x30, 0x6F);
        what.Foreground = message.Foreground = (Brush)res["MutedBrush"];
        message.Foreground = (Brush)res["GoldBrush"];
        use.Style = (Style)res["PrimaryButtonStyle"]; preview.Style = restore.Style = (Style)res["SoftButtonStyle"];
        Motion.SetPress(use, true);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(use, "overlay-use-style");
        use.Click += async (_, _) => { if (vm is not null && await Confirm(vm.Text["IntLayoutConfirmTitle"], vm.Text["IntLayoutConfirmBody"])) { await vm.ActivateLayoutAsync(selected); picked = false; Render(); } };
        preview.Click += (_, _) => vm?.PreviewOverlay();
        restore.Click += async (_, _) => { if (vm is not null && await Confirm(vm.Text["IntLayoutRestoreTitle"], vm.Text["IntRestoreConfirmBody"])) { await vm.RestoreLayoutAsync(); Render(); } };
        statusPill.Child = status;

        var head = new Grid { ColumnSpacing = 16 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new Border { Style = (Style)res["IconBadgeStyle"], Child = new FontIcon { Glyph = "", Style = (Style)res["BadgeIconStyle"] } });
        var words = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(title);
        hint.Style = (Style)res["CaptionStyle"];
        words.Children.Add(hint);
        Grid.SetColumn(words, 1); head.Children.Add(words);
        Grid.SetColumn(statusPill, 2); head.Children.Add(statusPill);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(use); actions.Children.Add(preview); actions.Children.Add(restore);
        var body = new StackPanel { Spacing = 16 };
        body.Children.Add(head);
        body.Children.Add(tileRow);
        body.Children.Add(stage);
        body.Children.Add(what);
        body.Children.Add(actions);
        body.Children.Add(busy);
        body.Children.Add(message);
        Content = new Border { Style = (Style)res["CardStyle"], Child = body };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(this, "overlay-style-picker");
        DataContextChanged += (_, _) => Attach(DataContext as IntegrationsViewModel);
        Loaded += (_, _) => { vm?.Refresh(); Render(); };
        Unloaded += (_, _) => { if (vm is not null) vm.PropertyChanged -= OnChanged; };
    }

    private void Attach(IntegrationsViewModel? model)
    {
        if (vm is not null) vm.PropertyChanged -= OnChanged;
        vm = model;
        if (vm is not null) vm.PropertyChanged += OnChanged;
        Render();
    }
    private void OnChanged(object? sender, PropertyChangedEventArgs e) => DispatcherQueue.TryEnqueue(Render);

    private void Render()
    {
        if (vm is null) return;
        var t = vm.Text;
        if (!picked && vm.ActiveVariant is { } active) selected = active;
        title.Text = t["GmOverlayTitle"];
        hint.Text = t["OsPickHint"];
        var on = vm.ActiveVariant;
        status.Text = on is null ? t["OsNoneActive"] : t.Format("OsActive", t[on == "neon" ? "OsNeon" : "OsTop"]);
        var c = on is null ? gold : Ok;
        statusPill.Background = new SolidColorBrush(Color.FromArgb(0x24, c.R, c.G, c.B));
        status.Foreground = new SolidColorBrush(c);

        tileRow.Children.Clear();
        tileRow.Children.Add(Tile("top", t["OsTop"], t["OsTopShort"], on == "top"));
        tileRow.Children.Add(Tile("neon", t["OsNeon"], t["OsNeonShort"], on == "neon"));

        stage.Children.Clear();
        stage.Children.Add(GameScene());
        stage.Children.Add(selected == "neon" ? NeonCard(1.0) : TopBar(1.0));
        stage.Children.Add(new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(14), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(Color.FromArgb(0xB3, 18, 9, 15)),
            Child = new TextBlock { Text = t["OsExample"], FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 255, 255, 255)) } });

        what.Inlines.Clear();
        what.Text = t[selected == "neon" ? "OsNeonWhat" : "OsTopWhat"];
        var isOn = on == selected;
        use.Content = Label(isOn ? "" : "", t.Format(isOn ? "OsInUse" : "OsUse", t[selected == "neon" ? "OsNeon" : "OsTop"]));
        use.IsEnabled = !isOn && vm.RtssInstalled && !vm.Busy;
        preview.Content = Label("", t["IntPreview"]);
        preview.Visibility = vm.RtssInstalled && vm.CanPreview ? Visibility.Visible : Visibility.Collapsed;
        restore.Content = t["IntLayoutRestore"];
        restore.Visibility = vm.CanRestoreLayout ? Visibility.Visible : Visibility.Collapsed;
        busy.Visibility = vm.Busy ? Visibility.Visible : Visibility.Collapsed;
        // "Ver o overlay agora" shows what RivaTuner has loaded, not the tile being looked at: say so when they differ.
        message.Text = !vm.RtssInstalled ? t["OsNeedRtss"] : vm.Message.Length > 0 ? vm.Message : !isOn ? t.Format("OsPreviewActive", t[on == "neon" ? "OsNeon" : on == "top" ? "OsTop" : "OsNoneShort"]) : "";
        message.Visibility = message.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static StackPanel Label(string glyph, string text)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        p.Children.Add(new FontIcon { Glyph = glyph, FontSize = 13 });
        p.Children.Add(new TextBlock { Text = text });
        return p;
    }

    // A style tile: a small drawing of the style, its name and one line; gold border when chosen, green badge when active.
    private Button Tile(string id, string name, string line, bool active)
    {
        var chosen = id == selected;
        var mini = new Grid { Height = 96, CornerRadius = new CornerRadius(10) };
        mini.Children.Add(GameScene());
        mini.Children.Add(id == "neon" ? NeonCard(0.42) : TopBar(0.42));
        var info = new StackPanel { Spacing = 2 };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        nameRow.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, FontSize = 14 });
        if (active) nameRow.Children.Add(new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 1, 8, 1), Background = new SolidColorBrush(Color.FromArgb(0x2E, Ok.R, Ok.G, Ok.B)), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = vm!.Text["OsActiveBadge"], FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Ok) } });
        info.Children.Add(nameRow);
        info.Children.Add(new TextBlock { Text = line, FontSize = 12, Foreground = (Brush)Application.Current.Resources["MutedBrush"], TextWrapping = TextWrapping.Wrap });
        var stack = new StackPanel { Spacing = 10 }; stack.Children.Add(mini); stack.Children.Add(info);
        var tile = new Button
        {
            Width = 260, Padding = new Thickness(12), CornerRadius = new CornerRadius(14), HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = stack,
            Background = (Brush)Application.Current.Resources[chosen ? "CardHoverBrush" : "CardBrush"],
            BorderThickness = new Thickness(chosen ? 2 : 1),
            BorderBrush = chosen ? HomeMotion.Gradient(new(0, 0), new(1, 1), (0, rose), (1, gold)) : (Brush)Application.Current.Resources["CardBorderBrush"],
        };
        Motion.SetHover(tile, 1.02); Motion.SetPress(tile, true);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(tile, "overlay-style-" + id);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tile, name);
        tile.Click += (_, _) => { selected = id; picked = true; Render(); };
        return tile;
    }

    // A dark "game" backdrop so the overlay reads like it would over a game.
    private Grid GameScene()
    {
        var g = new Grid { Background = HomeMotion.Gradient(new(0, 0), new(1, 1), (0, Color.FromArgb(255, 0x24, 0x10, 0x2A)), (0.55, Color.FromArgb(255, 0x12, 0x0A, 0x1C)), (1, Color.FromArgb(255, 0x3A, 0x14, 0x22))) };
        g.Children.Add(new Ellipse { Width = 420, Height = 420, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -260), Fill = new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B)) });
        return g;
    }

    // Lotus Top: one slim bar across the top with every metric.
    private FrameworkElement TopBar(double scale)
    {
        var bar = new Border { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12 * scale, 12 * scale, 12 * scale, 0), CornerRadius = new CornerRadius(8 * scale), Padding = new Thickness(12 * scale, 6 * scale, 12 * scale, 6 * scale),
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x12, 0x09, 0x0F)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, rose.R, rose.G, rose.B)), BorderThickness = new Thickness(1) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 * scale };
        foreach (var (k, v, hi) in new[] { ("FPS", "144", true), ("1% LOW", "118", false), ("0.1%", "96", false), ("MS", "6.9", false), ("GPU", "71% 64°", false), ("CPU", "38% 58°", false), ("RAM", "9.8 GB", false) })
        {
            var cell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 * scale };
            cell.Children.Add(new TextBlock { Text = k, FontSize = Math.Max(5, 11 * scale), FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(gold), VerticalAlignment = VerticalAlignment.Center });
            cell.Children.Add(new TextBlock { Text = v, FontSize = Math.Max(6, (hi ? 18 : 13) * scale), FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(hi ? rose : Colors.White), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(cell);
        }
        bar.Child = row;
        return bar;
    }

    // Lotus Neon: a card in the corner with the big FPS, the lows and usage bars.
    private FrameworkElement NeonCard(double scale)
    {
        var card = new Border { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(14 * scale), Width = 220 * scale, CornerRadius = new CornerRadius(12 * scale), Padding = new Thickness(14 * scale),
            Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x12, 0x09, 0x0F)), BorderBrush = HomeMotion.Gradient(new(0, 0), new(1, 1), (0, rose), (1, gold)), BorderThickness = new Thickness(Math.Max(1, 1.5 * scale)) };
        var p = new StackPanel { Spacing = 6 * scale };
        var fps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 * scale };
        fps.Children.Add(new TextBlock { Text = "144", FontSize = Math.Max(10, 40 * scale), FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(rose) });
        fps.Children.Add(new TextBlock { Text = "FPS", FontSize = Math.Max(5, 12 * scale), FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(gold), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6 * scale) });
        p.Children.Add(fps);
        p.Children.Add(new TextBlock { Text = "1% 118  ·  0.1% 96  ·  6.9 ms", FontSize = Math.Max(5, 11 * scale), Foreground = new SolidColorBrush(Colors.White) });
        foreach (var (k, share) in new[] { ("GPU", 0.71), ("CPU", 0.38), ("RAM", 0.61) })
        {
            var line = new Grid { ColumnSpacing = 8 * scale };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30 * scale) }); line.ColumnDefinitions.Add(new ColumnDefinition());
            line.Children.Add(new TextBlock { Text = k, FontSize = Math.Max(5, 10 * scale), FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(gold) });
            var track = new Grid { Height = Math.Max(2, 6 * scale), CornerRadius = new CornerRadius(3 * scale), VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)) };
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) }); track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - share, GridUnitType.Star) });
            track.Children.Add(new Rectangle { Fill = HomeMotion.Gradient(new(0, 0), new(1, 0), (0, accent), (1, rose)) });
            Grid.SetColumn(track, 1); line.Children.Add(track);
            p.Children.Add(line);
        }
        card.Child = p;
        return card;
    }

    private async Task<bool> Confirm(string head, string body)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = head, RequestedTheme = ThemePalette.Element, DefaultButton = ContentDialogButton.Close,
            Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 },
            PrimaryButtonText = vm!.Text["FeatConfirm"], CloseButtonText = vm.Text["DlgCancel"]
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
