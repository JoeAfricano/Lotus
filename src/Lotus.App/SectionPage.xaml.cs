using Lotus.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Lotus.App;

public sealed partial class SectionPage : Page
{
    public SectionPage() => InitializeComponent();
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not Section section) throw new ArgumentException("A section is required.");
        DataContext = section;
        LogLocation.Visibility = section.Id == "logs" ? Visibility.Visible : Visibility.Collapsed;
        LogLocation.Text = "Os logs de inicialização e navegação já são gravados em: " + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "Logs");
    }
}
