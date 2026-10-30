using Lotus.Core.Saves;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lotus.App.Controls;

// "Backup de saves": the library of games with their save protection, and one game's backups when a cover is clicked.
public sealed partial class SavesPanel : UserControl
{
    private SaveLibraryViewModel? vm;
    private bool syncing;
    private static readonly CoverImageConverter Covers = new();

    public SavesPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as SaveLibraryViewModel);
        Loaded += async (_, _) => { if (vm is not null && !vm.Loading) { await vm.LoadAsync(); Refresh(); } };
        Unloaded += (_, _) => Attach(null);
    }

    private void Attach(SaveLibraryViewModel? next)
    {
        if (vm is not null) vm.PropertyChanged -= OnVm;
        vm = next;
        if (vm is null) return;
        vm.PropertyChanged += OnVm;
        Refresh();
    }

    private void OnVm(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SaveLibraryViewModel.ProtectedCount) or nameof(SaveLibraryViewModel.Loaded)) Refresh();
        if (e.PropertyName == nameof(SaveLibraryViewModel.Selected)) ShowSelection();
    }

    private void Refresh()
    {
        if (vm is null) return;
        var t = vm.Text;
        StatProtected.Text = vm.Loaded ? vm.ProtectedCount.ToString() : "—";
        StatBackups.Text = vm.Loaded ? vm.BackupCount.ToString() : "—";
        // Filters show how many games each one has.
        syncing = true;
        var current = vm.Filter;
        FilterBar.Items.Clear();
        foreach (var (id, key) in new[] { ("all", "SlFilterAll"), ("protected", "SlFilterProtected"), ("found", "SlFilterFound"), ("none", "SlFilterNone") })
        {
            var item = new SelectorBarItem { Text = vm.Loaded ? $"{t[key]} ({vm.CountFor(id)})" : t[key], Tag = id };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, "saves-filter-" + id);
            FilterBar.Items.Add(item);
            if (id == current) FilterBar.SelectedItem = item;
        }
        syncing = false;
        BuildHeroCovers();
        ShowSelection();
    }

    // Up to three covers fanned out on the right of the hero: protected games first.
    private void BuildHeroCovers()
    {
        HeroCovers.Children.Clear();
        if (vm is null) return;
        var picks = vm.Items.Where(i => i.CoverPath is not null).DistinctBy(i => i.Game.Key)
            .OrderBy(i => i.IsProtected ? 0 : 1).Take(3).Reverse().ToList();
        var angles = new[] { -9.0, 6.0, -2.0 };
        var lefts = new[] { 40.0, 210.0, 118.0 };
        for (var i = 0; i < picks.Count; i++)
        {
            var slot = picks.Count - 1 - i; // front card in the middle
            var card = new Border
            {
                Width = 128, Height = 172, CornerRadius = new CornerRadius(12), BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x55, 0xEE, 0xC1, 0xA8)), BorderThickness = new Thickness(1),
                Background = new ImageBrush { ImageSource = Covers.Convert(picks[i].CoverPath!, typeof(ImageSource), null!, "") as ImageSource, Stretch = Stretch.UniformToFill },
                RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5), RenderTransform = new RotateTransform { Angle = angles[slot] }, Opacity = slot == 2 ? 1 : 0.8,
            };
            Canvas.SetLeft(card, lefts[slot]); Canvas.SetTop(card, slot == 2 ? 30 : 40);
            HeroCovers.Children.Add(card);
        }
    }

    private void OnFilter(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (!syncing && vm is not null && sender.SelectedItem?.Tag is string id) vm.Filter = id;
    }

    private async void OnTileClick(object sender, ItemClickEventArgs e)
    {
        if (vm is null || e.ClickedItem is not SaveGameTile tile) return;
        UiSound.Play(UiSound.Cue.Navigate);
        await vm.OpenAsync(tile);
    }

    private async void OnBack(object sender, RoutedEventArgs e)
    {
        if (vm is null) return;
        await vm.CloseAsync();
        Refresh();
    }

    private void ShowSelection()
    {
        var open = vm?.Selected is not null;
        ListArea.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        DetailArea.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (!open || vm?.SelectedTile is not { } tile) { Manager.DataContext = null; return; }
        var fresh = !ReferenceEquals(Manager.DataContext, vm.Selected);
        Manager.DataContext = vm.Selected;
        DetailName.Text = tile.Name;
        DetailLine.Text = tile.Detail.Length > 0 ? $"{tile.Store} · {tile.Detail}" : tile.Store;
        var art = tile.CoverPath is { } p ? Covers.Convert(p, typeof(ImageSource), null!, "") as ImageSource : null;
        DetailArt.Background = art is null ? null : new ImageBrush { ImageSource = art, Stretch = Stretch.UniformToFill };
        DetailCover.Background = art is null ? (Brush)Application.Current.Resources["HeroBrush"] : new ImageBrush { ImageSource = art, Stretch = Stretch.UniformToFill };
        if (fresh) Motion.EnterPage(DetailArea);
    }
}
