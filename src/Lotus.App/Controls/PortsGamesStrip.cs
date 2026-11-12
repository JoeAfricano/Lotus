using Lotus.Infrastructure.PortsLink;
using Lotus.Integration.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Lotus.App.Controls;

/// <summary>
/// Meus Jogos: the games Lotus Ports installed or the player favorited there, with Jogar (Lotus Ports opens the game,
/// with its own checks) and Abrir no Lotus Ports. Shown only when Lotus Ports is installed and answers; asked once
/// each time Meus Jogos opens, never polled.
/// </summary>
public sealed partial class PortsGamesStrip : UserControl
{
    private readonly PortsClient client = new(Serilog.Log.Logger);
    private readonly PortsOffer offer = new();
    private readonly StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 14 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };

    public PortsGamesStrip()
    {
        Visibility = Visibility.Collapsed;
        var text = App.Text;
        status.Style = (Style)Application.Current.Resources["CaptionStyle"];
        status.Foreground = (Brush)Application.Current.Resources["RoseBrush"];
        var header = new StackPanel { Spacing = 2 };
        header.Children.Add(new TextBlock { Text = text["PortsGamesTitle"], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 16 });
        header.Children.Add(new TextBlock { Text = text["PortsGamesHint"], Style = (Style)Application.Current.Resources["CaptionStyle"] });
        var open = new HyperlinkButton { Content = text["PortsOpen"], HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        open.Click += async (_, _) =>
        {
            if (PortsClient.IsInstalled) { await client.OpenAsync(null); return; }
            // Installed but closed: open it, then list its games as soon as it answers (a few tries, no polling after).
            if (!offer.Open()) return;
            Show(App.Text["PortsOpening"]);
            for (var i = 0; i < 10 && !PortsClient.IsInstalled; i++) await Task.Delay(1500);
            await RefreshAsync();
        };
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(header);
        Grid.SetColumn(open, 1);
        top.Children.Add(open);
        Content = new Border
        {
            Style = (Style)Application.Current.Resources["CardStyle"],
            Child = new StackPanel
            {
                Spacing = 14,
                Children = { top, new ScrollViewer { Content = row, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled }, status },
            },
        };
        AutomationProperties.SetAutomationId(this, "games-lotusports");
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (!PortsClient.IsInstalled)
        {
            // Installed but closed: a short note with "Abrir o Lotus Ports"; not installed: nothing here (Home has the invitation).
            row.Children.Clear();
            if (offer.IsInstalled) { Show(App.Text["PortsClosed"]); Visibility = Visibility.Visible; } else Visibility = Visibility.Collapsed;
            return;
        }
        Show("");
        var result = await client.ListGamesAsync();
        row.Children.Clear();
        if (result is null || result.Games.Count == 0) { Visibility = Visibility.Collapsed; return; }
        foreach (var game in result.Games.OrderByDescending(g => g.LastPlayed ?? DateTimeOffset.MinValue).Take(20))
            row.Children.Add(Card(game));
        Visibility = Visibility.Visible;
        // Sessions from Lotus Ports reach the diary with a title only: its cover comes from here, for entries still without one.
        if (MainWindow.Shell?.Diary is { } diary)
            foreach (var game in result.Games)
                if (game.CoverUrl is { Length: > 0 } cover && Uri.TryCreate(cover, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "file"
                    && diary.Get(new Lotus.Core.Games.GameEntry(Lotus.Core.Games.GameStores.LotusPorts, game.LotusGameKey, game.Title).Key) is { Cover: null or "" } entry)
                    diary.SetArt(entry.Key, cover);
    }

    private UIElement Card(PortsGame game)
    {
        var text = App.Text;
        var cover = new Border { Width = 132, Height = 176, CornerRadius = new CornerRadius(10), Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"] };
        if (game.CoverUrl is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "file")
            cover.Child = new Image { Source = new BitmapImage(uri) { DecodePixelWidth = 264 }, Stretch = Stretch.UniformToFill };
        var state = game.State switch
        {
            PortsGameState.Ready => text["PortsStateReady"],
            PortsGameState.UpdateAvailable => text["PortsStateUpdate"],
            PortsGameState.Problem => text["PortsStateProblem"],
            _ => text["PortsStateNotInstalled"],
        };
        var play = new Button { Content = text["PortsPlay"], Style = (Style)Application.Current.Resources["PrimaryButtonStyle"], HorizontalAlignment = HorizontalAlignment.Stretch };
        play.Click += async (_, _) =>
        {
            play.IsEnabled = false;
            Show(text["PortsLaunching"]);
            var launched = await client.LaunchAsync(game.GameId);
            Show(launched is null ? text["PortsUnavailable"] : launched.Started ? "" : string.Format(System.Globalization.CultureInfo.CurrentCulture, text["PortsLaunchNotStarted"], launched.ReasonPtBr ?? game.Title));
            play.IsEnabled = true;
        };
        AutomationProperties.SetName(play, $"{text["PortsPlay"]} {game.Title}");
        return new StackPanel
        {
            Width = 132,
            Spacing = 6,
            Children =
            {
                cover,
                new TextBlock { Text = game.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = state, Style = (Style)Application.Current.Resources["CaptionStyle"], TextTrimming = TextTrimming.CharacterEllipsis },
                play,
            },
        };
    }

    private void Show(string message)
    {
        status.Text = message;
        status.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
