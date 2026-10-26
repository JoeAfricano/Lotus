using Lotus.Core.Games;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Lotus.App.Controls;

// Profile side sheet: slides in from the right over a dimmed backdrop; Esc, the X or a click outside closes it.
public sealed partial class PlayerPanel : UserControl
{
    public event EventHandler? EditProfileRequested;
    public event EventHandler? DiaryRequested;
    public PlayerPanel()
    {
        InitializeComponent();
        KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Escape) { Close(); e.Handled = true; } };
    }
    private PlayerPanelViewModel? Vm => DataContext as PlayerPanelViewModel;
    public bool IsOpen => Visibility == Visibility.Visible;

    public void Open(PlayerPanelViewModel vm, string name, string? picture)
    {
        DataContext = vm; vm.Refresh();
        PlayerName.Text = name; Initial.Text = name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "P";
        Picture.Visibility = Visibility.Collapsed;
        if (picture is { Length: > 0 } && File.Exists(picture)) { PictureBrush.ImageSource = new BitmapImage(new Uri(picture)); Picture.Visibility = Visibility.Visible; }
        LastArt.Opacity = 0;
        if (vm.Last?.Game is { } g)
        {
            var url = g.Store == GameStores.Steam ? Lotus.Core.Catalog.CatalogText.Hero(g.Id) : g.CoverPath;
            if (url is not null && Uri.TryCreate(url, UriKind.Absolute, out var u))
            {
                var bmp = new BitmapImage(u); bmp.ImageOpened += (_, _) => Animate(LastArt, "Opacity", 0, 1, 400);
                LastArt.Source = bmp;
            }
        }
        Visibility = Visibility.Visible;
        Animate(Backdrop, "Opacity", 0, 1, 220);
        Animate(SheetShift, "X", 460, 0, 380, new CubicEase { EasingMode = EasingMode.EaseOut });
        UiSound.Play(UiSound.Cue.Open);
    }

    public void Close()
    {
        if (!IsOpen) return;
        UiSound.Play(UiSound.Cue.Close);
        FocusHelper.Commit(this); // commits a note being typed
        var sb = new Storyboard();
        Add(sb, Backdrop, "Opacity", 1, 0, 180); Add(sb, SheetShift, "X", 0, 460, 260, new CubicEase { EasingMode = EasingMode.EaseIn });
        sb.Completed += (_, _) => Visibility = Visibility.Collapsed;
        sb.Begin();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnBackdrop(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => Close();
    private void OnPlayLast(object sender, RoutedEventArgs e) { if (Vm is { Last: { } row } vm) { vm.Play(row); Close(); } }
    private void OnOpenDiary(object sender, RoutedEventArgs e) { Close(); DiaryRequested?.Invoke(this, EventArgs.Empty); }

    // Fills the diary miniature (called on open): level ring, XP bar, three numbers and the shelf covers.
    public void ShowDiary(Lotus.Core.Diary.DiaryViewModel d)
    {
        var p = d.Progress; var t = d.Text;
        MiniLevel.Text = p.Level.ToString(); MiniTitle.Text = d.LevelText + " · " + d.TitleText; MiniXp.Text = d.XpText;
        MiniBar.Value = 0; GlideBar(d.LevelPercent);
        MiniDone.Text = d.StatCompleted; MiniDoneLabel.Text = t["DyStatCompleted"];
        MiniHours.Text = d.StatHours; MiniHoursLabel.Text = t["DyStatHours"];
        MiniBadges.Text = p.UnlockedBadges + "/" + p.Badges.Count; MiniBadgesLabel.Text = t["DyBadges"].ToLowerInvariant();
        MiniOpen.Content = t["DyOpen"];
        MiniShelf.Children.Clear();
        foreach (var card in d.Shelf(6))
        {
            var cover = new Border { Width = 52, Height = 70, CornerRadius = new CornerRadius(8), Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["HeroBrush"] };
            if (card.Cover is { } c && Uri.TryCreate(c, UriKind.Absolute, out var u)) cover.Child = new Image { Source = new BitmapImage(u), Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
            var b = new Button { Content = cover, Padding = new Thickness(0), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) };
            ToolTipService.SetToolTip(b, card.Name + " · " + card.Status);
            var entry = card.Entry; b.Click += (_, _) => { Close(); MainWindow.Shell?.OpenDiaryEditor(entry); };
            MiniShelf.Children.Add(b);
        }
        MiniShelf.Visibility = MiniShelf.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void GlideBar(double target)
    {
        if (!Motion.Enabled) { MiniBar.Value = target; return; }
        var start = DateTime.UtcNow.AddMilliseconds(250); var timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.Tick += (s, _) => { var ms = (DateTime.UtcNow - start).TotalMilliseconds; if (ms < 0) return; var p = Math.Min(1, ms / 700); MiniBar.Value = target * (1 - Math.Pow(1 - p, 3)); if (p >= 1) s.Stop(); };
        timer.Start();
    }

    private void OnEditProfile(object sender, RoutedEventArgs e) { Close(); EditProfileRequested?.Invoke(this, EventArgs.Empty); }
    private void OnStar(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PlayerGameRow row } f) return;
        row.Favorite = !row.Favorite; Motion.Pop(f); UiSound.Play(UiSound.Cue.Toggle); Vm?.NotifyStats();
    }

    // The pencil shows the note box under the game; Enter or leaving the box saves it.
    private void OnNoteToggle(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement f || Find<TextBox>(f) is not { } box) return;
        box.Visibility = box.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (box.Visibility == Visibility.Visible) box.Focus(FocusState.Programmatic);
    }
    private void OnNoteKey(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && sender is TextBox box) { box.Visibility = Visibility.Collapsed; e.Handled = true; }
    }
    private void OnNoteDone(object sender, RoutedEventArgs e) { if (sender is TextBox box) box.Visibility = Visibility.Collapsed; }
    // Walks up to the row and back down to its note box.
    private static T? Find<T>(DependencyObject from) where T : DependencyObject
    {
        var row = from;
        while (row is not null && row is not Border { Tag: "noterow" }) row = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(row);
        return row is null ? null : Down<T>(row);
        static T? Down<TX>(DependencyObject d) where TX : DependencyObject
        {
            for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var c = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(d, i);
                if (c is T t) return t;
                if (Down<T>(c) is { } found) return found;
            }
            return null;
        }
    }

    private void Animate(DependencyObject target, string property, double from, double to, int ms, EasingFunctionBase? ease = null)
    {
        var sb = new Storyboard(); Add(sb, target, property, from, to, ms, ease); sb.Begin();
    }
    private static void Add(Storyboard sb, DependencyObject target, string property, double from, double to, int ms, EasingFunctionBase? ease = null)
    {
        var a = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(Motion.Enabled ? ms : 0), EasingFunction = ease ?? new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(a, target); Storyboard.SetTargetProperty(a, property); sb.Children.Add(a);
    }
}
