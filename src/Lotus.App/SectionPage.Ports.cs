using Lotus.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Lotus.App;

// Home: a small invitation to Lotus Ports (the sibling app). Not installed: the click explains what it is and installs it
// only after the player confirms (official GitHub release, SHA-256 checked, Lotus Ports' own setup wizard). Installed:
// the card opens it.
public sealed partial class SectionPage
{
    private readonly Lotus.Infrastructure.PortsLink.PortsOffer ports = new();
    // Development build only: LOTUS_PRETEND_NO_PORTS=1 shows the invitation even with Lotus Ports installed (to review it).
    private bool PortsInstalled => ports.IsInstalled && !(Environment.GetCommandLineArgs().Contains("--dev-instance") && Environment.GetEnvironmentVariable("LOTUS_PRETEND_NO_PORTS") == "1");
    private static ImageSource PortsIcon(int pixels) =>
        new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Concept", "LotusPorts.png"))) { DecodePixelWidth = pixels };

    private void OnHomePortsLoaded(object sender, RoutedEventArgs e)
    {
        HomePortsIcon.Source ??= PortsIcon(96);
        RefreshPortsCard();
    }

    private void RefreshPortsCard()
    {
        if (DataContext is not SectionViewModel vm) return;
        var installed = PortsInstalled;
        HomePortsHint.Text = vm.Text[installed ? "PoOpenHint" : "PoHint"];
        HomePortsBadge.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnHomePorts(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SectionViewModel vm) return;
        if (PortsInstalled) { if (!ports.Open()) RefreshPortsCard(); return; }
        await ShowPortsOfferAsync(vm);
        RefreshPortsCard();
    }

    private async Task ShowPortsOfferAsync(SectionViewModel vm)
    {
        var text = vm.Text;
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Foreground = (Brush)Application.Current.Resources["GoldBrush"], Visibility = Visibility.Collapsed };
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 0) };
        StackPanel Point(string glyph, string key)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 15, Foreground = (Brush)Application.Current.Resources["RoseBrush"], VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) });
            var body = RichText.Block(text[key]); body.TextWrapping = TextWrapping.Wrap; body.MaxWidth = 400; body.FontSize = 13.5;
            row.Children.Add(body);
            return row;
        }
        var header = new Grid { ColumnSpacing = 16 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new ColumnDefinition());
        header.Children.Add(new Image { Source = PortsIcon(160), Width = 72, Height = 72 });
        var titles = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "Lotus Ports", FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        titles.Children.Add(new TextBlock { Text = text["PoTagline"], FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["MutedBrush"] });
        Grid.SetColumn(titles, 1); header.Children.Add(titles);
        var content = new StackPanel { Spacing = 14, MaxWidth = 460 };
        content.Children.Add(header);
        content.Children.Add(Point("", "PoWhat"));
        content.Children.Add(Point("", "PoHow"));
        content.Children.Add(Point("", "PoFiles"));
        content.Children.Add(Point("", "PoLotus"));
        var note = RichText.Block(text["PoInstallNote"]); note.TextWrapping = TextWrapping.Wrap; note.FontSize = 12; note.Opacity = 0.8;
        content.Children.Add(note);
        content.Children.Add(status); content.Children.Add(bar);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ThemePalette.Element, Content = content, DefaultButton = ContentDialogButton.Primary,
            PrimaryButtonText = text["PoInstall"], SecondaryButtonText = text["PoGitHub"], CloseButtonText = text["PoLater"],
        };
        dialog.SecondaryButtonClick += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(new Uri(Lotus.Infrastructure.PortsLink.PortsOffer.RepositoryUrl));
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true; // the dialog stays open and shows each step
            var deferral = args.GetDeferral();
            dialog.IsPrimaryButtonEnabled = false; dialog.IsSecondaryButtonEnabled = false;
            void Say(string key, bool progress = false) { status.Text = text[key]; status.Visibility = Visibility.Visible; bar.Visibility = progress ? Visibility.Visible : Visibility.Collapsed; }
            try
            {
                Say("PoChecking", progress: true); bar.IsIndeterminate = true;
                var release = await ports.LatestAsync();
                if (release is null) { Say("PoNoRelease"); return; }
                Say("PoDownloading", progress: true); bar.IsIndeterminate = false;
                var setupFile = await ports.DownloadAsync(release, new Progress<double>(p => bar.Value = p));
                if (setupFile is null) { Say("PoBadFile"); Serilog.Log.Warning("Lotus Ports setup {Tag} refused: SHA-256 missing or different", release.Tag); return; }
                Say("PoRunning");
                Serilog.Log.Information("Lotus Ports setup {Tag} checked and opened", release.Tag);
                var installed = await ports.RunSetupAsync(setupFile);
                if (installed) { dialog.Hide(); return; }
                Say("PoNotInstalled");
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or InvalidOperationException or UnauthorizedAccessException)
            {
                Serilog.Log.Warning(ex, "Lotus Ports install");
                Say("PoOffline");
            }
            finally
            {
                dialog.IsPrimaryButtonEnabled = !ports.IsInstalled; dialog.IsSecondaryButtonEnabled = true;
                deferral.Complete();
            }
        };
        await dialog.ShowAsync();
    }
}
