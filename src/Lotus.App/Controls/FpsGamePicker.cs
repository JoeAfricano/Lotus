using Lotus.Core.Score;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lotus.App.Controls;

// Lotus Score › "Escolher jogo": a centered panel over a dimmed window with a search box and game cards (cover, name,
// 1080p estimate per quality, colored by range). Typing filters the list (short pause); Enter also searches Steam for
// games outside the list. The grid virtualizes, so only the visible cards exist.
public static class FpsGamePicker
{
    private const string CardTemplate = """
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
         <Grid Padding="10" RowSpacing="8" HorizontalAlignment="Stretch" CornerRadius="14" Background="{ThemeResource CardBrush}" BorderBrush="{ThemeResource CardBorderBrush}" BorderThickness="1">
          <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
          <Grid Height="170" CornerRadius="10" Background="{ThemeResource HeroBrush}" BorderBrush="{ThemeResource CardBorderBrush}" BorderThickness="1">
           <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="6">
            <FontIcon Glyph="&#xE7FC;" FontSize="26" Foreground="#CCFFF6F9"/>
            <TextBlock Text="{Binding Initials}" FontSize="30" FontWeight="Bold" Foreground="#FFF6F9" HorizontalAlignment="Center"/>
           </StackPanel>
           <Image Source="{Binding Cover, Converter={StaticResource CoverImage}}" Stretch="UniformToFill"/>
           <Border Visibility="{Binding HasCap, Converter={StaticResource BooleanVisibility}}" Margin="6" Padding="8,3" CornerRadius="8" HorizontalAlignment="Left" VerticalAlignment="Bottom" Background="#D912090F"><TextBlock Text="{Binding CapText}" FontSize="12" FontWeight="SemiBold" Foreground="#EEC1A8"/></Border>
          </Grid>
          <StackPanel Grid.Row="1" Spacing="1"><TextBlock Text="{Binding Name}" FontWeight="SemiBold" FontSize="14" TextTrimming="CharacterEllipsis"/><TextBlock Text="{Binding Note}" FontSize="12" Foreground="{ThemeResource MutedBrush}" TextTrimming="CharacterEllipsis"/></StackPanel>
          <Grid Grid.Row="2" ColumnSpacing="4">
           <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/></Grid.ColumnDefinitions>
           <StackPanel><TextBlock Text="L" FontSize="12" Foreground="{ThemeResource MutedBrush}"/><TextBlock Text="{Binding Low}" FontSize="16" FontWeight="Bold" Foreground="{Binding LowKind, Converter={StaticResource DiaryBrush}}"/></StackPanel>
           <StackPanel Grid.Column="1"><TextBlock Text="M" FontSize="12" Foreground="{ThemeResource MutedBrush}"/><TextBlock Text="{Binding Medium}" FontSize="16" FontWeight="Bold" Foreground="{Binding MediumKind, Converter={StaticResource DiaryBrush}}"/></StackPanel>
           <StackPanel Grid.Column="2"><TextBlock Text="H" FontSize="12" Foreground="{ThemeResource MutedBrush}"/><TextBlock Text="{Binding High}" FontSize="16" FontWeight="Bold" Foreground="{Binding HighKind, Converter={StaticResource DiaryBrush}}"/></StackPanel>
           <StackPanel Grid.Column="3"><TextBlock Text="U" FontSize="12" Foreground="{ThemeResource MutedBrush}"/><TextBlock Text="{Binding Ultra}" FontSize="16" FontWeight="Bold" Foreground="{Binding UltraKind, Converter={StaticResource DiaryBrush}}"/></StackPanel>
          </Grid>
         </Grid>
        </DataTemplate>
        """;

    private static Image? FindImage(DependencyObject d)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) { var c = VisualTreeHelper.GetChild(d, i); if (c is Image im) return im; if (FindImage(c) is { } f) return f; }
        return null;
    }
    public static void Show(XamlRoot root, LotusScoreViewModel vm)
    {
        var t = vm.Text;
        var res = Application.Current.Resources;
        var popup = new Popup { XamlRoot = root, IsLightDismissEnabled = false };
        var host = new Grid { Width = root.Size.Width, Height = root.Size.Height, RequestedTheme = ThemePalette.Element };
        var backdrop = new Border { Background = new SolidColorBrush(Color.FromArgb(0x99, 0x0C, 0x06, 0x0A)) };
        backdrop.Tapped += (_, _) => popup.IsOpen = false;
        host.Children.Add(backdrop);

        var sheet = new Grid { MaxWidth = 1100, Margin = new Thickness(40, 32, 40, 32), CornerRadius = new CornerRadius(22), BorderThickness = new Thickness(1),
            Background = (Brush)res["WindowBrush"], BorderBrush = (Brush)res["GoldGradientBrush"], Padding = new Thickness(28, 24, 28, 24), RowSpacing = 16 };
        sheet.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); sheet.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sheet.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); sheet.RowDefinitions.Add(new RowDefinition()); sheet.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new Grid { ColumnSpacing = 12 };
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var words = new StackPanel { Spacing = 2 };
        words.Children.Add(new TextBlock { Text = t["ScorePickTitle"], FontSize = 26, FontWeight = FontWeights.Bold });
        words.Children.Add(new TextBlock { Text = t["ScorePickHint"], FontSize = 14, Foreground = (Brush)res["MutedBrush"], TextWrapping = TextWrapping.Wrap });
        head.Children.Add(words);
        var close = new Button { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = (Brush)res["SubtleBrush"], Content = new FontIcon { Glyph = "", FontSize = 13 } };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(close, t["PpClose"]);
        close.Click += (_, _) => popup.IsOpen = false;
        Grid.SetColumn(close, 1); head.Children.Add(close);
        sheet.Children.Add(head);

        var box = new AutoSuggestBox { PlaceholderText = t["ScoreFpsSearch"], QueryIcon = new SymbolIcon(Symbol.Find), VerticalContentAlignment = VerticalAlignment.Center };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(box, "score-pick-search");
        Grid.SetRow(box, 1); sheet.Children.Add(box);
        var status = new TextBlock { FontSize = 12, Foreground = (Brush)res["GoldBrush"], TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(status, 2); sheet.Children.Add(status);

        var grid = new GridView { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true, ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(CardTemplate
            .Replace("Text=\"L\"", $"Text=\"{t["ScorePresetLow"]}\"").Replace("Text=\"M\"", $"Text=\"{t["ScorePresetMedium"]}\"").Replace("Text=\"H\"", $"Text=\"{t["ScorePresetHigh"]}\"").Replace("Text=\"U\"", $"Text=\"{t["ScorePresetUltra"]}\"")) };
        grid.ItemContainerStyle = new Style(typeof(GridViewItem)) { Setters = { new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 12, 12)), new Setter(Control.PaddingProperty, new Thickness(0)), new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch) } };
        // Columns stretch to fill the width (about 210 px each); the grid keeps virtualizing.
        grid.SizeChanged += (_, e) => { if (grid.ItemsPanelRoot is ItemsWrapGrid wrap && e.NewSize.Width > 0) { var cols = Math.Max(1, (int)((e.NewSize.Width - 8) / 210)); wrap.ItemWidth = Math.Floor((e.NewSize.Width - 8) / cols); } };
        // Games outside Steam: their box art from Wikipedia (cached on disk), filled in as each card appears.
        grid.ContainerContentChanging += async (_, e) =>
        {
            if (e.InRecycleQueue) return;
            if (e.Item is LotusScoreViewModel.FpsPick { Cover: not null } && e.ItemContainer.ContentTemplateRoot is FrameworkElement reused && FindImage(reused) is { } steamImg)
            { steamImg.Stretch = Stretch.UniformToFill; steamImg.Margin = new Thickness(0); } // a recycled card back to a Steam cover
            if (e.Item is not LotusScoreViewModel.FpsPick { Cover: null } pick) return;
            var container = e.ItemContainer;
            if (container.ContentTemplateRoot is FrameworkElement early && FindImage(early) is { } old) old.Source = null;
            // The first cards may not have their template yet: look for the image after the download.
            if (await WikiCover.GetAsync(pick.Name) is not { } file || container.Content != (object)pick || container.ContentTemplateRoot is not FrameworkElement rootEl || FindImage(rootEl) is not { } img) return;
            img.Stretch = Stretch.Uniform; img.Margin = new Thickness(14); // logos and box art shown whole, centered on the brand card
            img.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(file)) { DecodePixelWidth = 360 };
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(grid, "score-pick-grid");
        Grid.SetRow(grid, 3); sheet.Children.Add(grid);
        // Footer: when the numbers are from, their sources, and a manual check for a newer table.
        var foot = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var dated = new TextBlock { FontSize = 12, Foreground = (Brush)res["MutedBrush"], VerticalAlignment = VerticalAlignment.Center };
        var sourcesFlyout = new MenuFlyout();
        void Sources() { sourcesFlyout.Items.Clear(); foreach (var s in vm.ReferenceSources.Values) { var item = new MenuFlyoutItem { Text = s.Label }; var url = s.Url; item.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(new Uri(url)); sourcesFlyout.Items.Add(item); } }
        var sourcesButton = new HyperlinkButton { Content = t["ScorePickSources"], Padding = new Thickness(4, 2, 4, 2) };
        sourcesButton.Click += (_, _) => sourcesFlyout.ShowAt(sourcesButton);
        var refresh = new HyperlinkButton { Content = t["ScorePickRefresh"], Padding = new Thickness(4, 2, 4, 2) };
        foot.Children.Add(dated); foot.Children.Add(sourcesButton); foot.Children.Add(refresh);
        void Foot() { dated.Text = t.Format("ScorePickDate", vm.ReferenceDate, ScoreInsights.Games.Count); Sources(); }
        refresh.Click += async (_, _) =>
        {
            refresh.IsEnabled = false; status.Text = t["ScorePickChecking"];
            var newer = vm.CheckReference is { } check && await check(true);
            await vm.ResolvePendingAsync();
            Fill(box.Text); Foot(); status.Text = t[newer ? "ScorePickUpdated" : "ScorePickCurrent"]; refresh.IsEnabled = true;
        };
        Grid.SetRow(foot, 4); sheet.Children.Add(foot);
        Foot();
        host.Children.Add(sheet);
        // Popular games listed by name are worked out from Steam requirements in the background (cached 30 days).
        async void Resolve() { try { await vm.ResolvePendingAsync(); if (popup.IsOpen) { Fill(box.Text); Foot(); } } catch (Exception ex) { Serilog.Log.Warning(ex, "FPS requirements"); } }
        Resolve(); // network waits only; the view model stays on the UI thread

        void Fill(string text) { grid.ItemsSource = vm.Picks(text); status.Text = vm.FpsSearchStatus; }
        Fill("");
        var debounce = root.Content.DispatcherQueue.CreateTimer(); debounce.Interval = TimeSpan.FromMilliseconds(220); debounce.IsRepeating = false;
        debounce.Tick += (_, _) => Fill(box.Text);
        box.TextChanged += (_, a) => { if (a.Reason == AutoSuggestionBoxTextChangeReason.UserInput) { debounce.Stop(); debounce.Start(); } };
        // Enter: a game outside the list is searched on Steam and appears first.
        box.QuerySubmitted += async (_, a) =>
        {
            var q = a.QueryText.Trim();
            if (q.Length < 2 || vm.Picks(q).Count > 0) return;
            status.Text = t["ScoreFpsSearching"];
            await vm.SearchGameAsync(q);
            Fill(q.Length > 0 && vm.Picks(q).Count > 0 ? q : "");
        };
        grid.ItemClick += (_, e) => { if (e.ClickedItem is LotusScoreViewModel.FpsPick p) { vm.GameIndex = p.Index; popup.IsOpen = false; } };

        void Resize(XamlRoot r, XamlRootChangedEventArgs _) { host.Width = r.Size.Width; host.Height = r.Size.Height; }
        root.Changed += Resize;
        popup.Closed += (_, _) => root.Changed -= Resize;
        host.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Escape) popup.IsOpen = false; };
        popup.Child = host;
        popup.IsOpen = true;
        sheet.Opacity = 0; HomeMotion.Fade(sheet, 1, 220);
        box.Focus(FocusState.Programmatic);
    }
}
