using Lotus.Core.Score;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App.Controls;

public sealed partial class ScorePanel : UserControl
{
    public ScorePanel()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is not LotusScoreViewModel vm) return;
            vm.Refresh();
            OnlineLinks.Children.Clear();
            foreach (var (name, url) in vm.OnlineLinks())
            {
                var button = new Button { Content = name, Style = (Style)Application.Current.Resources["SoftButtonStyle"] };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "score-online-" + name.Replace(" ", "").ToLowerInvariant());
                button.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
                OnlineLinks.Children.Add(button);
            }
        };
    }

    private async void OnToggle(object sender, RoutedEventArgs e)
    {
        if (DataContext is LotusScoreViewModel vm) await vm.ToggleAsync();
    }
}
