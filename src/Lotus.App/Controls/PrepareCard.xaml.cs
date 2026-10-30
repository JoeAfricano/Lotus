using Lotus.Core.Catalog;
using Lotus.Core.Games;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App.Controls;

// Shown on the page of an installed game (Meus Jogos). Its own view model is built from the page's game.
public sealed partial class PrepareCard : UserControl
{
    private GameDetailsViewModel? page;
    private GamePrepareViewModel? Vm => Box.DataContext as GamePrepareViewModel;

    public PrepareCard()
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
        if (page?.Installed is not { } game || MainWindow.Shell is not { } shell) { Visibility = Visibility.Collapsed; return; }
        var t = page.Text;
        Box.DataContext = new GamePrepareViewModel(shell.PlayPrefs, game, t, () => shell.PreparePlayDefault);
        Title.Text = t["PrepCardTitle"];
        PrepareSwitch.Header = t["PrepCardSwitch"];
        ProfileBox.Content = t["PrepCardProfile"]; ControllerBox.Content = t["PrepCardController"]; SystemBox.Content = t["PrepCardSystem"]; QuietBox.Content = t["PrepCardQuiet"];
        Note.Text = t["PrepCardNote"];
        Visibility = Visibility.Visible;
    }

    // Authorizing the Game profile for this game: show exactly what would change now, then store the choice.
    private async void OnProfile(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || MainWindow.Shell is not { } shell) return;
        if (vm.UseProfile) { vm.UseProfile = false; return; } // turning it off needs no question
        ProfileBox.IsChecked = false;
        var t = vm.Text;
        var body = new StackPanel { Spacing = 8, MaxWidth = 440 };
        body.Children.Add(new TextBlock { Text = t["PrepProfileAskBody"], TextWrapping = TextWrapping.Wrap });
        try
        {
            if (shell.GameProfilePreview is { } preview)
                foreach (var s in (await preview()).Where(s => !s.AlreadyApplied))
                    body.Children.Add(new TextBlock { Text = $"{s.Title}: {s.From}  →  {s.To}", TextWrapping = TextWrapping.Wrap, Opacity = 0.8, FontSize = 12.5 });
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Profile preview"); }
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ThemePalette.Element, Title = t.Format("PrepProfileAskTitle", vm.Game.Name), Content = body,
            PrimaryButtonText = t["PrepProfileAllow"], CloseButtonText = t["DlgCancel"], DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) vm.UseProfile = true;
        ProfileBox.IsChecked = vm.UseProfile;
    }
}
