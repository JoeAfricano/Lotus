using Lotus.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Lotus.App;

public sealed partial class MainWindow : Window
{
    public ShellViewModel ViewModel { get; }
    public MainWindow(ShellViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        AppWindow.TitleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(255, 16, 16, 22);
        AppWindow.TitleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 240, 232, 238);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 820));
        foreach (var section in ViewModel.Items)
            Navigation.MenuItems.Add(new NavigationViewItem { Content = section.Title, Tag = section.Id, Icon = new FontIcon { Glyph = section.Glyph } });
        Navigation.SelectedItem = Navigation.MenuItems[0];
    }
    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: string id }) return;
        ViewModel.NavigateCommand.Execute(id);
        ContentFrame.Navigate(typeof(SectionPage), ViewModel.SelectedSection, new EntranceNavigationTransitionInfo());
        ContentFrame.BackStack.Clear();
    }
}
