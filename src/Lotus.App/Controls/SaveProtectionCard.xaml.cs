using Lotus.Core.Catalog;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App.Controls;

// On an installed game's page: one line saying whether its saves are protected, and a shortcut to "Backup de saves".
public sealed partial class SaveProtectionCard : UserControl
{
    private GameDetailsViewModel? page;

    public SaveProtectionCard()
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
        var on = shell.Saves.Get(game.Key).Enabled;
        Title.Text = t["savesTitle"];
        Summary.Text = on ? t["SpStateActive"] : t["SpTitleHint"];
        Icon.Glyph = on ? "" : "";
        OpenText.Text = t["SlManage"];
        Visibility = Visibility.Visible;
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if (page?.Installed is not { } game || MainWindow.Shell is not { } shell || shell.SaveLibrary is not { } library) return;
        library.PendingOpen = game.Key;
        UiSound.Play(UiSound.Cue.Navigate);
        shell.ViewModel.NavigateCommand.Execute("saves");
    }
}
