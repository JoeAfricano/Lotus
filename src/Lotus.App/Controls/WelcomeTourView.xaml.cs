using Lotus.Core.Profile;
using Lotus.Core.Welcome;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Lotus.App.Controls;

// "Conheça o Lotus" (pattern adapted from the PowerToys OOBE: one page per feature, "try it" button, "what's new" after
// updates; no PowerToys code copied). Each page has a small scene drawn with shapes (no video or images to load) that
// loops only while the guide is open; with Reduce motion the scene is shown still, in its final state.
public sealed partial class WelcomeTourView : UserControl
{
    private const string ReleasesUrl = "https://github.com/JoeAfricano/Lotus/releases";
    private const double Cycle = 5.6; // seconds per scene loop
    private WelcomeTourViewModel? vm;
    private OnboardingViewModel? setup; // first run: profile and look are chosen inside the guide and saved when it closes
    private Storyboard? loop, art;
    private int lastIndex;
    private bool closing;
    private readonly List<Button> railItems = [];

    public bool MotionEnabled { get; set; } = true;
    public bool IsOpen => Visibility == Visibility.Visible && !closing;
    public event EventHandler? Closed;
    public event EventHandler<string>? TryRequested; // route of the feature the player wants to try

    public WelcomeTourView()
    {
        InitializeComponent();
        var esc = new KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape };
        esc.Invoked += (_, e) => { e.Handled = true; Close(); };
        KeyboardAccelerators.Add(esc);
        foreach (var (key, step) in new[] { (Windows.System.VirtualKey.Right, 1), (Windows.System.VirtualKey.Left, -1) })
        {
            var arrow = new KeyboardAccelerator { Key = key };
            arrow.Invoked += (_, e) => { e.Handled = true; if (step > 0) vm?.Next(); else vm?.Back(); Render(true); };
            KeyboardAccelerators.Add(arrow);
        }
    }

    public void Open(WelcomeTourViewModel model, WelcomeOpening opening, OnboardingViewModel? firstRun = null)
    {
        vm = model; closing = false; setup = firstRun;
        setup?.Start(edit: false);
        vm.Open(opening); lastIndex = vm.Index;
        RailKicker.Text = vm.Text["WtKicker"]; CloseButton.Content = vm.Text["WtClose"]; BackButton.Content = vm.Text["WtBack"];
        SceneTag.Text = vm.Text["WtScExample"].ToUpperInvariant();
        BuildRail();
        Visibility = Visibility.Visible;
        Render(animate: false);
        Animate(Backdrop, "Opacity", 0, 1, 260);
        Animate(Card, "Opacity", 0, 1, 320);
        Animate(CardTransform, "ScaleX", 0.96, 1, 420, Spring()); Animate(CardTransform, "ScaleY", 0.96, 1, 420, Spring());
        Animate(CardTransform, "TranslateY", 16, 0, 420, Spring());
        UiSound.Play(UiSound.Cue.Open);
        NextButton.Focus(FocusState.Programmatic);
    }

    public void Close()
    {
        if (closing || Visibility == Visibility.Collapsed) return;
        closing = true;
        steamWatch?.Stop(); steamWatch = null;
        if (setup is { } chosen) { setup = null; try { chosen.Skip(); } catch (Exception ex) { Serilog.Log.Warning(ex, "Welcome profile not saved"); } } // saves profile + look, marks the first run done
        loop?.Stop(); loop = null; art?.Stop(); art = null;
        var ms = MotionEnabled ? 200 : 0;
        var sb = new Storyboard();
        Add(sb, Backdrop, "Opacity", 1, 0, ms); Add(sb, Card, "Opacity", 1, 0, ms); Add(sb, CardTransform, "TranslateY", 0, 10, ms);
        sb.Completed += (_, _) => { Visibility = Visibility.Collapsed; Scene.Children.Clear(); closing = false; Closed?.Invoke(this, EventArgs.Empty); };
        sb.Begin();
    }

    // ---- rail ----
    private void BuildRail()
    {
        Rail.Children.Clear(); railItems.Clear();
        if (vm is null) return;
        for (var i = 0; i < vm.Pages.Count; i++)
        {
            var page = vm.Pages[i];
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new FontIcon { Glyph = page.Glyph, FontSize = 15 });
            var label = new TextBlock { Text = page.Title, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 1); row.Children.Add(label);
            var item = new Button
            {
                Content = row, Tag = i, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                MinHeight = 40, Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            };
            Describe(item, page);
            item.Click += (s, _) => { if (vm is not null && s is Button { Tag: int index }) { vm.Index = index; Render(true); } };
            railItems.Add(item); Rail.Children.Add(item);
        }
    }

    private static void Describe(Button item, WelcomePage page)
    {
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, page.Title);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, "wt-page-" + page.Id);
    }

    private void SelectRail()
    {
        if (vm is null) return;
        for (var i = 0; i < railItems.Count; i++)
        {
            var on = i == vm.Index;
            railItems[i].Background = on ? (Brush)Application.Current.Resources["BadgeBrush"] : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            if (railItems[i].Content is Grid { Children: [FontIcon icon, TextBlock label] })
            {
                icon.Foreground = on ? (Brush)Application.Current.Resources["RoseBrush"] : (Brush)Application.Current.Resources["MutedBrush"];
                label.FontWeight = on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            }
        }
    }

    // ---- page ----
    private void Render(bool animate)
    {
        if (vm is null || Visibility != Visibility.Visible) return;
        var page = vm.Current;
        var forward = vm.Index >= lastIndex; lastIndex = vm.Index;
        SelectRail();
        StepLabel.Text = vm.StepText;
        PageKicker.Text = page.Title.ToUpperInvariant();
        PageTitle.Text = page.Headline;
        PageGrid.RowDefinitions[0].Height = new GridLength(page.IsSetup ? 150 : 236);
        PageLead.Text = page.Lead;
        BuildDetails(page);
        BackButton.Visibility = vm.CanBack ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = vm.NextLabel;
        var canTry = page.Route is not null || vm.IsWhatsNew;
        TryButton.Visibility = canTry ? Visibility.Visible : Visibility.Collapsed;
        TryText.Text = page.TryLabel; TryIcon.Glyph = vm.IsWhatsNew ? "" : "";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(TryButton, page.TryLabel);
        BuildScene(page);
        if (animate && MotionEnabled)
        {
            Animate(BodyShift, "X", forward ? 28 : -28, 0, 340, Spring());
            Animate(Body, "Opacity", 0, 1, 240);
            UiSound.Play(UiSound.Cue.Navigate);
        }
    }

    private void BuildDetails(WelcomePage page)
    {
        Details.Children.Clear();
        if (vm is null) return;
        if (vm.IsWhatsNew) { BuildNotes(); return; }
        if (page.Id == WelcomeTourViewModel.ProfileId) { BuildProfile(); return; }
        if (page.Id == WelcomeTourViewModel.ThemeId) { BuildTheme(); return; }
        Details.Children.Add(new TextBlock { Text = vm.Text["WtHow"].ToUpperInvariant(), Style = (Style)Application.Current.Resources["EyebrowStyle"], Margin = new Thickness(0, 4, 0, 2) });
        foreach (var point in page.Points) Details.Children.Add(PointRow("", point));
        if (page.Route is not null) Details.Children.Add(new TextBlock { Text = vm.Text["WtTryHint"], Style = (Style)Application.Current.Resources["CaptionStyle"], Margin = new Thickness(0, 6, 0, 0) });
    }

    private void BuildNotes()
    {
        if (vm is null) return;
        Details.Children.Add(new TextBlock { Text = vm.NotesTitle, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (vm.Notes is { } notes)
        {
            if (notes.Date.Length > 0) Details.Children.Add(new TextBlock { Text = notes.Date, Style = (Style)Application.Current.Resources["CaptionStyle"] });
            foreach (var group in notes.Groups)
            {
                if (group.Title.Length > 0) Details.Children.Add(new TextBlock { Text = group.Title.ToUpperInvariant(), Style = (Style)Application.Current.Resources["EyebrowStyle"], Margin = new Thickness(0, 8, 0, 0) });
                foreach (var item in group.Items) Details.Children.Add(PointRow("", item, small: true));
            }
        }
        Details.Children.Add(new TextBlock { Text = vm.Current.Points[0], Style = (Style)Application.Current.Resources["CaptionStyle"], Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
    }

    private static Grid PointRow(string glyph, string text, bool small = false)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition());
        var badge = new Border
        {
            Width = small ? 20 : 26, Height = small ? 20 : 26, CornerRadius = new CornerRadius(small ? 10 : 13), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0),
            Background = (Brush)Application.Current.Resources["BadgeBrush"],
            Child = new FontIcon { Glyph = glyph, FontSize = small ? 8 : 12, Foreground = (Brush)Application.Current.Resources["RoseBrush"] },
        };
        var body = RichText.Block(text, small ? 13 : 14);
        body.TextWrapping = TextWrapping.Wrap; body.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(body, 1); row.Children.Add(badge); row.Children.Add(body);
        return row;
    }

    // ---- buttons ----
    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (vm is null) return;
        if (vm.IsLast) { UiSound.Play(UiSound.Cue.Done); Close(); return; }
        vm.Next(); Render(true);
    }
    private void OnBack(object sender, RoutedEventArgs e) { vm?.Back(); Render(true); }
    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnBackdropTapped(object sender, TappedRoutedEventArgs e) { e.Handled = true; }
    private async void OnTry(object sender, RoutedEventArgs e)
    {
        if (vm is null) return;
        if (vm.IsWhatsNew) { await Windows.System.Launcher.LaunchUriAsync(new Uri(ReleasesUrl)); return; }
        if (vm.Current.Route is not { } route) return;
        Close();
        TryRequested?.Invoke(this, route);
    }

    // ---- scenes ----
    private void BuildScene(WelcomePage page)
    {
        loop?.Stop(); loop = null; art?.Stop(); art = null;
        Scene.Children.Clear();
        var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        var root = page.Id switch
        {
            "welcome" => WelcomeScene(sb),
            "optimize" => OptimizeScene(sb),
            "games" => GamesScene(sb),
            "score" => ScoreScene(sb),
            "tools" => ToolsScene(sb),
            "saves" => SavesScene(sb),
            "diary" => DiaryScene(sb),
            WelcomeTourViewModel.ProfileId => ProfileScene(sb),
            WelcomeTourViewModel.ThemeId => ThemeScene(sb),
            _ => NewsScene(sb),
        };
        Scene.Children.Add(root);
        ShowArt(page.Id == "welcome");
        SceneTag.Visibility = page.Id is "welcome" or WelcomeTourViewModel.WhatsNewId || page.IsSetup ? Visibility.Collapsed : Visibility.Visible;
        if (!MotionEnabled) return;
        Keys(sb, root, "Opacity", (0, 0), (0.35, 1), (Cycle - 0.45, 1), (Cycle, 0));
        loop = sb; sb.Begin();
    }

    private bool artLoaded;
    private void ShowArt(bool on)
    {
        if (on && !artLoaded)
        {
            artLoaded = true;
            StageArt.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Concept", "lotus-sculpture.png"))) { DecodePixelWidth = 820 };
        }
        StageArt.Opacity = on ? 0.9 : 0;
        if (!on || !MotionEnabled) return;
        art = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        Add(art, ArtTransform, "ScaleX", 1, 1.05, 4200, ease); Add(art, ArtTransform, "ScaleY", 1, 1.05, 4200, ease);
        art.Begin();
    }

    private Grid WelcomeScene(Storyboard sb)
    {
        var grid = new Grid();
        grid.Children.Add(new Border { Background = Gradient("#E6160C14", "#00160C14", horizontal: true) , Margin = new Thickness(-28, -22, 0, -22), Width = 420, HorizontalAlignment = HorizontalAlignment.Left });
        var stack = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(Label("PLAY BETTER.", 12, "#F07A9C", bold: true, spacing: 160));
        var chips = new[] { ("", "WtScConfirm", "#F07A9C"), ("", "WtScUndo", "#F07A9C"), ("", "WtScHonest", "#EEC1A8") };
        for (var i = 0; i < chips.Length; i++)
        {
            var chip = Chip(chips[i].Item1, Text(chips[i].Item2), chips[i].Item3);
            stack.Children.Add(chip); Appear(sb, chip, 0.4 + i * 0.45);
        }
        grid.Children.Add(stack);
        return grid;
    }

    private Grid OptimizeScene(Storyboard sb)
    {
        var grid = Columns();
        var rows = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Width = 330, HorizontalAlignment = HorizontalAlignment.Left };
        var items = new[] { ("", "WtScPower"), ("", "WtScGameMode"), ("", "WtScRefresh") };
        for (var i = 0; i < items.Length; i++)
        {
            var line = new Grid { ColumnSpacing = 12 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); line.ColumnDefinitions.Add(new ColumnDefinition()); line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.Children.Add(Icon(items[i].Item1, 15, "#F07A9C"));
            var label = Label(Text(items[i].Item2), 13, "#FFFFFF"); Grid.SetColumn(label, 1); line.Children.Add(label);
            var toggle = Toggle(sb, 0.9 + i * 0.5); Grid.SetColumn(toggle, 2); line.Children.Add(toggle);
            var card = MockCard(line, new Thickness(14, 10, 14, 10));
            rows.Children.Add(card); Appear(sb, card, 0.35 + i * 0.3);
        }
        grid.Children.Add(rows);
        var side = new StackPanel { Spacing = 14, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var orb = Orb("", 30); side.Children.Add(orb); Pop(sb, orb, 2.2);
        var undo = Chip("", Text("WtScUndo"), "#EEC1A8"); undo.HorizontalAlignment = HorizontalAlignment.Center; side.Children.Add(undo); Appear(sb, undo, 2.7);
        Grid.SetColumn(side, 1); grid.Children.Add(side);
        return grid;
    }

    private Grid GamesScene(Storyboard sb)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var palettes = new[] { ("#AD245E", "#3A1430"), ("#6D43B5", "#221436"), ("#B02236", "#2C0F18"), ("#8F5E16", "#2A1A0C"), ("#28715A", "#0F2620") };
        Grid? first = null;
        for (var i = 0; i < palettes.Length; i++)
        {
            var tile = new Grid { Width = 104, Height = 146, CornerRadius = new CornerRadius(12), Background = Gradient(palettes[i].Item1, palettes[i].Item2), BorderBrush = Solid("#33FFFFFF"), BorderThickness = new Thickness(1) };
            tile.Children.Add(new FontIcon { Glyph = "", FontSize = 30, Foreground = Solid("#59FFFFFF") });
            row.Children.Add(tile); Appear(sb, tile, 0.3 + i * 0.22, rise: 22);
            first ??= tile;
        }
        var play = new Border
        {
            CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 5, 14, 5), Background = Gradient("#F07A9C", "#AD245E", horizontal: true),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 10),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Icon("", 11, "#FFFFFF"), Label(Text("WtScPlay"), 12, "#FFFFFF", bold: true) } },
        };
        first!.Children.Add(play); Pop(sb, play, 1.9);
        var tf = Tf(play);
        Keys(sb, tf, "ScaleX", (0, 0.6), (1.9, 0.6), (2.25, 1), (2.9, 1), (3.15, 1.1), (3.45, 1));
        Keys(sb, tf, "ScaleY", (0, 0.6), (1.9, 0.6), (2.25, 1), (2.9, 1), (3.15, 1.1), (3.45, 1));
        return new Grid { Children = { row } };
    }

    private Grid ScoreScene(Storyboard sb)
    {
        var grid = Columns();
        var bars = new StackPanel { Spacing = 18, VerticalAlignment = VerticalAlignment.Center, Width = 340, HorizontalAlignment = HorizontalAlignment.Left };
        var items = new[] { ("", "WtScCpu", 0.58), ("", "WtScGpu", 0.86) };
        for (var i = 0; i < items.Length; i++)
        {
            var block = new StackPanel { Spacing = 8 };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            head.Children.Add(Icon(items[i].Item1, 14, "#EEC1A8")); head.Children.Add(Label(Text(items[i].Item2), 13, "#FFFFFF"));
            if (i == 0) { var tag = Chip("", Text("WtScBottleneck"), "#F07A9C", small: true); tag.Margin = new Thickness(6, -4, 0, -4); head.Children.Add(tag); Pop(sb, tag, 2.5); }
            block.Children.Add(head);
            var track = new Grid { Height = 12, CornerRadius = new CornerRadius(6), Background = Solid("#33FFFFFF") };
            var fill = new Border { CornerRadius = new CornerRadius(6), Background = Gradient("#F07A9C", "#EEC1A8", horizontal: true), HorizontalAlignment = HorizontalAlignment.Left, Width = 340 * items[i].Item3 };
            var tf = Tf(fill); fill.RenderTransformOrigin = new Point(0, 0.5);
            Keys(sb, tf, "ScaleX", (0, 0), (0.6 + i * 0.35, 0), (1.9 + i * 0.35, 1));
            track.Children.Add(fill); block.Children.Add(track);
            bars.Children.Add(block); Appear(sb, block, 0.3 + i * 0.3);
        }
        grid.Children.Add(bars);
        var orb = Orb("", 32); orb.HorizontalAlignment = HorizontalAlignment.Center; orb.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(orb, 1); grid.Children.Add(orb); Pop(sb, orb, 0.4);
        var spin = Tf(orb.Children[^1]);
        Keys(sb, spin, "Rotation", (0, -30), (0.4, -30), (1.6, 0));
        return grid;
    }

    private Grid ToolsScene(Storyboard sb)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 560 };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition());
        var items = new[] { ("", "WtScCleanup"), ("", "WtScStartup"), ("", "WtScDisk"), ("", "WtScDrivers") };
        for (var i = 0; i < items.Length; i++)
        {
            var line = new Grid { ColumnSpacing = 12 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); line.ColumnDefinitions.Add(new ColumnDefinition()); line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.Children.Add(Icon(items[i].Item1, 15, "#EEC1A8"));
            var label = Label(Text(items[i].Item2), 13, "#FFFFFF"); Grid.SetColumn(label, 1); line.Children.Add(label);
            var status = new Grid { Width = 22, Height = 22 };
            var dot = new Ellipse { Width = 8, Height = 8, Fill = Solid("#66FFFFFF") };
            var check = new Border { CornerRadius = new CornerRadius(11), Background = Solid("#338AD6B3"), Child = Icon("", 11, "#8AD6B3") };
            status.Children.Add(dot); status.Children.Add(check);
            var at = 1.1 + i * 0.45;
            Keys(sb, dot, "Opacity", (0, 1), (at, 1), (at + 0.2, 0));
            Keys(sb, check, "Opacity", (0, 0), (at, 0), (at + 0.25, 1));
            var pop = Tf(check);
            Keys(sb, pop, "ScaleX", (0, 0.5), (at, 0.5), (at + 0.3, 1)); Keys(sb, pop, "ScaleY", (0, 0.5), (at, 0.5), (at + 0.3, 1));
            dot.Opacity = 0;
            Grid.SetColumn(status, 2); line.Children.Add(status);
            var card = MockCard(line, new Thickness(14, 12, 14, 12));
            Grid.SetColumn(card, i % 2); Grid.SetRow(card, i / 2); grid.Children.Add(card); Appear(sb, card, 0.3 + i * 0.18);
        }
        return new Grid { Children = { grid } };
    }

    private Grid SavesScene(Storyboard sb)
    {
        var stack = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = 420 };
        var flow = new Grid { ColumnSpacing = 18, HorizontalAlignment = HorizontalAlignment.Center };
        for (var c = 0; c < 3; c++) flow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var folder = MockCard(Icon("", 30, "#EEC1A8"), new Thickness(18)); flow.Children.Add(folder); Appear(sb, folder, 0.3);
        var arrow = Icon("", 18, "#F07A9C"); arrow.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(arrow, 1); flow.Children.Add(arrow);
        var move = Tf(arrow);
        Keys(sb, arrow, "Opacity", (0, 0), (0.7, 0), (0.9, 1), (2.3, 1), (2.5, 0.35));
        Keys(sb, move, "TranslateX", (0, -10), (0.7, -10), (1.3, 6), (1.8, -4), (2.3, 4));
        var shield = new Grid { Width = 72, Height = 72 };
        shield.Children.Add(new Ellipse { Fill = Gradient("#AD245E", "#3A1430") });
        shield.Children.Add(Icon("", 30, "#FFFFFF"));
        var cloud = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Solid("#FF21141E"), BorderBrush = Solid("#88EEC1A8"), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -6, -8, 0), Child = Icon("", 12, "#EEC1A8") };
        shield.Children.Add(cloud); Pop(sb, cloud, 3.0);
        Grid.SetColumn(shield, 2); flow.Children.Add(shield); Pop(sb, shield, 0.6);
        stack.Children.Add(flow);
        var track = new Grid { Height = 8, CornerRadius = new CornerRadius(4), Background = Solid("#33FFFFFF"), Width = 300 };
        var fill = new Border { CornerRadius = new CornerRadius(4), Background = Gradient("#F07A9C", "#EEC1A8", horizontal: true) };
        var tf = Tf(fill); fill.RenderTransformOrigin = new Point(0, 0.5);
        Keys(sb, tf, "ScaleX", (0, 0), (0.8, 0), (2.4, 1));
        track.Children.Add(fill); stack.Children.Add(track);
        var done = Chip("", Text("WtScSaveCopy"), "#8AD6B3"); done.HorizontalAlignment = HorizontalAlignment.Center; stack.Children.Add(done); Appear(sb, done, 2.55);
        return new Grid { Children = { stack } };
    }

    private Grid DiaryScene(Storyboard sb)
    {
        var grid = Columns();
        var rows = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Width = 330, HorizontalAlignment = HorizontalAlignment.Left };
        var widths = new[] { 120.0, 84, 150 };
        for (var i = 0; i < widths.Length; i++)
        {
            var line = new Grid { ColumnSpacing = 12 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); line.ColumnDefinitions.Add(new ColumnDefinition());
            line.Children.Add(new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Background = Solid("#4DF07A9C"), Child = Icon("", 14, "#FFFFFF") });
            var text = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Label(Text("WtScSession"), 12.5, "#FFFFFF"));
            text.Children.Add(new Border { Height = 6, Width = widths[i], CornerRadius = new CornerRadius(3), Background = Solid("#40EEC1A8"), HorizontalAlignment = HorizontalAlignment.Left });
            Grid.SetColumn(text, 1); line.Children.Add(text);
            var card = MockCard(line, new Thickness(12, 10, 12, 10));
            rows.Children.Add(card); Appear(sb, card, 0.35 + i * 0.4, slideX: -18);
        }
        grid.Children.Add(rows);
        var side = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var medal = new Grid { Width = 84, Height = 84, HorizontalAlignment = HorizontalAlignment.Center };
        medal.Children.Add(new Ellipse { Fill = Gradient("#F5D7C4", "#8F5E16") });
        medal.Children.Add(new Ellipse { Margin = new Thickness(7), Stroke = Solid("#AAFFFFFF"), StrokeThickness = 1.5 });
        medal.Children.Add(Icon("", 30, "#3A1430"));
        side.Children.Add(medal); Pop(sb, medal, 1.9, overshoot: true);
        var chip = Chip("", Text("WtScMedal"), "#EEC1A8"); chip.HorizontalAlignment = HorizontalAlignment.Center; side.Children.Add(chip); Appear(sb, chip, 2.4);
        Grid.SetColumn(side, 1); grid.Children.Add(side);
        return grid;
    }

    private Grid NewsScene(Storyboard sb)
    {
        var grid = new Grid();
        var stars = new[] { (-210.0, -60.0, 14.0, 0.2), (190, -70, 18, 0.9), (-150, 64, 12, 1.5), (230, 50, 12, 0.6), (-260, 10, 10, 2.1), (120, 78, 10, 1.2) };
        foreach (var (x, y, size, at) in stars)
        {
            var star = Icon("", size, "#EEC1A8");
            star.HorizontalAlignment = HorizontalAlignment.Center; star.VerticalAlignment = VerticalAlignment.Center;
            star.RenderTransform = new TranslateTransform { X = x, Y = y };
            grid.Children.Add(star);
            Keys(sb, star, "Opacity", (0, 0.15), (at, 0.15), (at + 0.5, 1), (at + 1.3, 0.25), (Cycle, 0.15));
        }
        var center = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        center.Children.Add(Label((vm?.Text["WtNews"] ?? "").ToUpperInvariant(), 12, "#F07A9C", bold: true, spacing: 160, center: true));
        var version = Label(vm?.Notes?.Version ?? "Lotus", 40, "#FFFFFF", bold: true, center: true);
        version.Foreground = (Brush)Application.Current.Resources["BrandGoldGradientBrush"];
        center.Children.Add(version); Pop(sb, version, 0.35);
        if (vm?.Notes is { Name.Length: > 0 } notes) { var name = Label("“" + notes.Name + "”", 14, "#E6D6DE", center: true); center.Children.Add(name); Appear(sb, name, 0.9); }
        grid.Children.Add(center);
        return grid;
    }

    // ---- first-run choices (account and look), same rules as the profile setup ----
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? steamWatch;

    private void BuildProfile()
    {
        if (setup is not { } s || vm is null) return;
        var caption = (Style)Application.Current.Resources["CaptionStyle"];
        if (s.SteamFound)
        {
            var steam = Choice(s.UseSteam, vm.Text["WtProfileSteam"], s.SteamName, s.SteamPicture, s.SteamName);
            steam.Click += (_, _) => { s.UseSteam = true; RefreshSetup(); };
            Details.Children.Add(steam);
        }
        else
        {
            var missing = new Grid { ColumnSpacing = 12 };
            missing.ColumnDefinitions.Add(new ColumnDefinition()); missing.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            missing.Children.Add(new TextBlock { Text = vm.Text["WtProfileNoSteam"], Style = caption, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            var signIn = new Button { Content = s.SteamSignInLabel, Style = (Style)Application.Current.Resources["SoftButtonStyle"] };
            signIn.Click += async (_, _) => { await Windows.System.Launcher.LaunchUriAsync(new Uri(s.SteamSignInUrl)); WatchSteam(); };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(signIn, "wt-steam-signin");
            Grid.SetColumn(signIn, 1); missing.Children.Add(signIn);
            Details.Children.Add(missing);
            WatchSteam();
        }
        var own = Choice(s.UseCustom, vm.Text["WtProfileOwn"], s.CustomName.Trim().Length > 0 ? s.CustomName.Trim() : vm.Text["ObDefaultName"], s.CustomPicture, s.PreviewName);
        own.Click += (_, _) => { s.UseCustom = true; RefreshSetup(); };
        Details.Children.Add(own);
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBox { PlaceholderText = vm.Text["WtProfileName"], Text = s.CustomName, MaxLength = 32 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(name, "wt-profile-name");
        name.GotFocus += (_, _) => { if (!s.UseCustom) { s.UseCustom = true; } };
        name.TextChanged += (_, _) => { s.CustomName = name.Text; UpdateProfileScene(); };
        var photo = new Button { Content = vm.Text["WtProfilePhoto"], Style = (Style)Application.Current.Resources["SoftButtonStyle"] };
        photo.Click += OnPickPicture;
        Grid.SetColumn(photo, 1); row.Children.Add(name); row.Children.Add(photo);
        Details.Children.Add(row);
        Details.Children.Add(new TextBlock { Text = vm.Current.Points[0], Style = caption, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
    }

    // A selectable card: avatar (picture or initial), title and the name that will be used.
    private static Button Choice(bool selected, string title, string detail, string? picture, string initialFrom)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Avatar(picture, initialFrom, 38));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        text.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = detail, Style = (Style)Application.Current.Resources["CaptionStyle"], TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(text, 1); grid.Children.Add(text);
        var mark = new FontIcon { Glyph = selected ? "" : "", FontSize = 14, Foreground = (Brush)Application.Current.Resources["RoseBrush"], VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(mark, 2); grid.Children.Add(mark);
        return new Button
        {
            Content = grid, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 8, 14, 8), CornerRadius = new CornerRadius(12),
            Background = (Brush)Application.Current.Resources[selected ? "BadgeBrush" : "CardBrush"],
            BorderBrush = (Brush)Application.Current.Resources[selected ? "RoseBrush" : "CardBorderBrush"], BorderThickness = new Thickness(1),
        };
    }

    private static Grid Avatar(string? picture, string name, double size)
    {
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(new Ellipse { Fill = (Brush)Application.Current.Resources["AccentGradientBrush"] });
        grid.Children.Add(new TextBlock { Text = name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "P", FontSize = size * 0.45, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = (Brush)Application.Current.Resources["OnRoseBrush"], HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        if (picture is { Length: > 0 } && File.Exists(picture))
        {
            try { grid.Children.Add(new Ellipse { Fill = new ImageBrush { ImageSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(picture)) { DecodePixelWidth = (int)(size * 2) }, Stretch = Stretch.UniformToFill } }); }
            catch (Exception) { }
        }
        return grid;
    }

    private async void OnPickPicture(object sender, RoutedEventArgs e)
    {
        if (setup is not { } s) return;
        var picker = new Windows.Storage.Pickers.FileOpenPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary, ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail };
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" }) picker.FileTypeFilter.Add(ext);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        if (await picker.PickSingleFileAsync() is { } file) { s.ChoosePicture(file.Path); RefreshSetup(); }
    }

    // No Steam account yet: re-check every few seconds while the profile page is open (stops when it closes).
    private void WatchSteam()
    {
        if (steamWatch is not null || setup is not { SteamFound: false }) return;
        steamWatch = DispatcherQueue.CreateTimer(); steamWatch.Interval = TimeSpan.FromSeconds(3);
        steamWatch.Tick += (t, _) =>
        {
            if (setup is not { } s || vm?.Current.Id != WelcomeTourViewModel.ProfileId || !IsOpen) { t.Stop(); steamWatch = null; return; }
            if (s.RecheckSteam()) { t.Stop(); steamWatch = null; RefreshSetup(); }
        };
        steamWatch.Start();
    }

    private void BuildTheme()
    {
        if (setup is not { } s || vm is null) return;
        Details.Children.Add(Heading(vm.Text["WtThemeMode"]));
        var modes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var t in s.Themes)
        {
            var b = Pill(t.Label, t.Selected, swatch: Solid(t.Swatch));
            b.Click += (_, _) => { s.Theme = t.Id; UiSound.Play(UiSound.Cue.Toggle); RefreshSetup(); };
            modes.Children.Add(b);
        }
        Details.Children.Add(modes);
        Details.Children.Add(Heading(vm.Text["WtThemeReady"]));
        var ready = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var f in s.Featured)
        {
            var b = Pill(f.Label, f.Selected, swatch: Swatch(f.Colors));
            b.Click += (_, _) => { s.PickFeatured(f.Id); UiSound.Play(UiSound.Cue.Toggle); RefreshSetup(); };
            ready.Children.Add(b);
        }
        Details.Children.Add(new ScrollViewer { Content = ready, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 0, 6) });
        Details.Children.Add(Heading(vm.Text["WtThemeAccent"]));
        var accents = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        foreach (var a in s.Accents)
        {
            var dot = new Button
            {
                Width = 34, Height = 34, Padding = new Thickness(0), CornerRadius = new CornerRadius(17), Background = Solid(a.Hex),
                BorderBrush = (Brush)Application.Current.Resources[a.Selected ? "MainTextBrush" : "CardBorderBrush"], BorderThickness = new Thickness(a.Selected ? 3 : 1),
                Content = a.Selected ? new FontIcon { Glyph = "", FontSize = 12, Foreground = Solid("#FFFFFF") } : null,
            };
            ToolTipService.SetToolTip(dot, a.Label);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dot, a.Label);
            dot.Click += (_, _) => { s.Accent = a.Id; UiSound.Play(UiSound.Cue.Toggle); RefreshSetup(); };
            accents.Children.Add(dot);
        }
        Details.Children.Add(accents);
        Details.Children.Add(new TextBlock { Text = vm.Current.Points[0], Style = (Style)Application.Current.Resources["CaptionStyle"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
    }

    private static TextBlock Heading(string text) => new() { Text = text.ToUpperInvariant(), Style = (Style)Application.Current.Resources["EyebrowStyle"], Margin = new Thickness(0, 4, 0, 0) };
    private static Button Pill(string label, bool selected, Brush swatch)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = swatch, BorderBrush = Solid("#55FFFFFF"), BorderThickness = new Thickness(1) });
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        return new Button
        {
            Content = content, MinHeight = 36, Padding = new Thickness(12, 6, 14, 6), CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1),
            Background = (Brush)Application.Current.Resources[selected ? "BadgeBrush" : "CardBrush"],
            BorderBrush = (Brush)Application.Current.Resources[selected ? "RoseBrush" : "CardBorderBrush"],
        };
    }
    private static Brush Swatch(string colors)
    {
        var list = colors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        try { return list.Length >= 2 ? Gradient(list[0], list[^1]) : Solid(list.Length == 1 ? list[0] : "#888888"); }
        catch (Exception) { return Solid("#888888"); }
    }

    // A choice was made: redraw the controls and the live preview in the scene (no page animation).
    private void RefreshSetup()
    {
        if (vm is null) return;
        BuildDetails(vm.Current);
        if (vm.Current.Id == WelcomeTourViewModel.ProfileId) UpdateProfileScene(); else BuildScene(vm.Current);
    }

    private TextBlock? sceneName;
    private Grid? sceneAvatar;
    private void UpdateProfileScene()
    {
        if (setup is not { } s || sceneName is null || sceneAvatar?.Parent is not Panel holder) return;
        sceneName.Text = s.PreviewName;
        var index = holder.Children.IndexOf(sceneAvatar);
        var fresh = Avatar(s.PreviewPicture, s.PreviewName, 64);
        holder.Children[index] = fresh; sceneAvatar = fresh;
    }

    private Grid ProfileScene(Storyboard sb)
    {
        var card = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        sceneAvatar = Avatar(setup?.PreviewPicture, setup?.PreviewName ?? "", 64);
        card.Children.Add(sceneAvatar);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 6 };
        sceneName = Label(setup?.PreviewName ?? "", 22, "#FFFFFF", bold: true);
        text.Children.Add(sceneName);
        text.Children.Add(Chip("", Text("ObStageLevel"), "#EEC1A8", small: true));
        card.Children.Add(text);
        var panel = MockCard(card, new Thickness(22, 16, 28, 16)); panel.HorizontalAlignment = HorizontalAlignment.Center; panel.VerticalAlignment = VerticalAlignment.Center;
        Pop(sb, panel, 0.3);
        return new Grid { Children = { panel } };
    }

    private Grid ThemeScene(Storyboard sb)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var picks = setup?.Featured.Take(4).ToList() ?? [];
        for (var i = 0; i < picks.Count; i++)
        {
            var tile = new Border { Width = 92, Height = 64, CornerRadius = new CornerRadius(12), Background = Swatch(picks[i].Colors), BorderBrush = Solid(picks[i].Selected ? "#FFF07A9C" : "#44FFFFFF"), BorderThickness = new Thickness(picks[i].Selected ? 2 : 1) };
            row.Children.Add(tile); Appear(sb, tile, 0.3 + i * 0.2, rise: 14);
        }
        var accent = new Ellipse { Width = 44, Height = 44, Fill = (Brush)Application.Current.Resources["AccentGradientBrush"], VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(accent); Pop(sb, accent, 1.3, overshoot: true);
        return new Grid { Children = { row } };
    }

    // ---- scene building blocks ----
    private string Text(string key) => vm?.Text[key] ?? key;
    private static Grid Columns()
    {
        var grid = new Grid { ColumnSpacing = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        return grid;
    }
    private static Border MockCard(UIElement child, Thickness padding) => new()
    {
        Background = Solid("#CC21141E"), BorderBrush = Solid("#55EEC1A8"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = padding, Child = child,
    };
    private static Border Chip(string glyph, string text, string color, bool small = false) => new()
    {
        CornerRadius = new CornerRadius(small ? 10 : 14), Padding = small ? new Thickness(8, 3, 10, 3) : new Thickness(12, 6, 14, 6), HorizontalAlignment = HorizontalAlignment.Left,
        Background = Solid("#4D2A1424"), BorderBrush = Solid("#66" + color.TrimStart('#')), BorderThickness = new Thickness(1),
        Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Icon(glyph, small ? 10 : 12, color), Label(text, small ? 11 : 12.5, "#FFFFFF") } },
    };
    private static Grid Orb(string glyph, double size)
    {
        var orb = new Grid { Width = size * 2.6, Height = size * 2.6 };
        orb.Children.Add(new Ellipse { Fill = Gradient("#F07A9C", "#6E1A44") });
        orb.Children.Add(new Ellipse { Margin = new Thickness(-10), Stroke = Solid("#40F07A9C"), StrokeThickness = 1 });
        orb.Children.Add(Icon(glyph, size, "#FFFFFF"));
        return orb;
    }
    private static Grid Toggle(Storyboard sb, double at)
    {
        var pill = new Grid { Width = 40, Height = 22 };
        pill.Children.Add(new Border { CornerRadius = new CornerRadius(11), BorderBrush = Solid("#99FFFFFF"), BorderThickness = new Thickness(1.5) });
        var on = new Border { CornerRadius = new CornerRadius(11), Background = Gradient("#F07A9C", "#AD245E", horizontal: true) };
        pill.Children.Add(on);
        var knob = new Ellipse { Width = 12, Height = 12, Fill = Solid("#FFFFFFFF"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(5, 0, 0, 0) };
        var move = new TranslateTransform { X = 18 }; knob.RenderTransform = move;
        pill.Children.Add(knob);
        Keys(sb, on, "Opacity", (0, 0), (at, 0), (at + 0.25, 1));
        Keys(sb, move, "X", (0, 0), (at, 0), (at + 0.3, 18));
        return pill;
    }
    private static FontIcon Icon(string glyph, double size, string color) => new() { Glyph = glyph, FontSize = size, Foreground = Solid(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private static TextBlock Label(string text, double size, string color, bool bold = false, int spacing = 0, bool center = false) => new()
    {
        Text = text, FontSize = size, Foreground = Solid(color), CharacterSpacing = spacing, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal, HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
    };
    private static SolidColorBrush Solid(string hex) => new(Hex(hex));
    private static LinearGradientBrush Gradient(string from, string to, bool horizontal = false) => new()
    {
        StartPoint = new Point(0, 0), EndPoint = horizontal ? new Point(1, 0) : new Point(1, 1),
        GradientStops = { new GradientStop { Color = Hex(from), Offset = 0 }, new GradientStop { Color = Hex(to), Offset = 1 } },
    };
    private static Windows.UI.Color Hex(string hex)
    {
        hex = hex.TrimStart('#');
        byte a = 255; var i = 0;
        if (hex.Length == 8) { a = Convert.ToByte(hex[..2], 16); i = 2; }
        return Windows.UI.Color.FromArgb(a, Convert.ToByte(hex.Substring(i, 2), 16), Convert.ToByte(hex.Substring(i + 2, 2), 16), Convert.ToByte(hex.Substring(i + 4, 2), 16));
    }
    private static CompositeTransform Tf(UIElement element)
    {
        if (element.RenderTransform is CompositeTransform existing) return existing;
        var t = new CompositeTransform(); element.RenderTransform = t; element.RenderTransformOrigin = new Point(0.5, 0.5); return t;
    }

    // Elements are built in their final state; the loop hides them at the start of each cycle and brings them in.
    private static void Appear(Storyboard sb, UIElement element, double at, double rise = 10, double slideX = 0)
    {
        var tf = Tf(element);
        Keys(sb, element, "Opacity", (0, 0), (at, 0), (at + 0.35, 1));
        if (slideX != 0) Keys(sb, tf, "TranslateX", (0, slideX), (at, slideX), (at + 0.45, 0));
        else Keys(sb, tf, "TranslateY", (0, rise), (at, rise), (at + 0.45, 0));
    }
    private static void Pop(Storyboard sb, UIElement element, double at, bool overshoot = false)
    {
        var tf = Tf(element);
        Keys(sb, element, "Opacity", (0, 0), (at, 0), (at + 0.25, 1));
        var peak = overshoot ? 1.12 : 1.04;
        Keys(sb, tf, "ScaleX", (0, 0.7), (at, 0.7), (at + 0.3, peak), (at + 0.5, 1));
        Keys(sb, tf, "ScaleY", (0, 0.7), (at, 0.7), (at + 0.3, peak), (at + 0.5, 1));
    }
    private static void Keys(Storyboard sb, DependencyObject target, string property, params (double At, double Value)[] keys)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(Cycle) };
        foreach (var (at, value) in keys)
            animation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(Math.Min(at, Cycle))), Value = value, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        Storyboard.SetTarget(animation, target); Storyboard.SetTargetProperty(animation, property); sb.Children.Add(animation);
    }

    // ---- small animation helpers (same feel as the profile welcome) ----
    private static EasingFunctionBase Spring() => new BackEase { Amplitude = 0.18, EasingMode = EasingMode.EaseOut };
    private void Animate(DependencyObject target, string property, double from, double to, int ms, EasingFunctionBase? ease = null)
    {
        var sb = new Storyboard(); Add(sb, target, property, from, to, MotionEnabled ? ms : 0, ease); sb.Begin();
    }
    private static void Add(Storyboard sb, DependencyObject target, string property, double from, double to, int ms, EasingFunctionBase? ease = null)
    {
        var a = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(ms), EasingFunction = ease ?? Motion.Decelerate() };
        Storyboard.SetTarget(a, target); Storyboard.SetTargetProperty(a, property); sb.Children.Add(a);
    }
}
