using Lotus.Core.Integrations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App.Controls;

public sealed partial class IntegrationsPanel : UserControl
{
    public IntegrationsPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => (DataContext as IntegrationsViewModel)?.Refresh();
    }

    // Opens only the official SCSKiller releases page.
    private async void OnScsLink(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri(Lotus.Core.Games.GamesViewModel.ScsKillerReleases));
}
