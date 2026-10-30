using Lotus.Core.Catalog;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;

namespace Lotus.App.Controls;

// Game page. The art fades in once downloaded (slow zoom while visible), Metacritic gets its usual colors and the
// difficulty shows as four bars that fill one after another.
public sealed partial class GameDetailView : UserControl
{
    public event EventHandler? BackRequested;
    public event EventHandler<Lotus.Core.Games.GameEntry>? PlayRequested; // the host launches it through Meus Jogos
    private GameDetailsViewModel? vm;
    public GameDetailView()
    {
        InitializeComponent();
        // Repaint the diary buttons when the diary changes (saved from the sheet, removed, added elsewhere).
        EventHandler onDiary = (_, _) => DispatcherQueue.TryEnqueue(PaintDiary);
        Loaded += (_, _) => { if (MainWindow.Shell is { } s) { s.Diary.Changed -= onDiary; s.Diary.Changed += onDiary; s.Trophies.Changed -= onDiary; s.Trophies.Changed += onDiary; } PaintDiary(); };
        Unloaded += (_, _) => { if (MainWindow.Shell is { } s) { s.Diary.Changed -= onDiary; s.Trophies.Changed -= onDiary; } };
        DataContextChanged += (_, _) =>
        {
            if (vm is not null) vm.PropertyChanged -= OnVm;
            vm = DataContext as GameDetailsViewModel;
            if (vm is not null) vm.PropertyChanged += OnVm;
            Render();
        };
        Unloaded += (_, _) => { if (vm is not null) vm.PropertyChanged -= OnVm; };
    }

    private void OnVm(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameDetailsViewModel.Art) or nameof(GameDetailsViewModel.DifficultyLevel) or nameof(GameDetailsViewModel.MetacriticKind) or nameof(GameDetailsViewModel.Title)) DispatcherQueue.TryEnqueue(Render);
    }

    private string? shownArt;
    private void Render()
    {
        if (vm is null) return;
        PaintDiary();
        // Art
        var want = vm.HeroArt ?? vm.Art;
        if (want != shownArt)
        {
            shownArt = want; HeroArt.Opacity = 0;
            ShowArt(want, vm.Art);
        }
        // Metacritic colors (green / yellow / red, like the site)
        McBadge.Background = new SolidColorBrush(vm.MetacriticKind switch
        {
            "good" => Windows.UI.Color.FromArgb(255, 102, 204, 51), "mixed" => Windows.UI.Color.FromArgb(255, 255, 204, 51),
            "bad" => Windows.UI.Color.FromArgb(255, 255, 0, 0), _ => Windows.UI.Color.FromArgb(0x33, 255, 255, 255),
        });
        McScore.Foreground = new SolidColorBrush(vm.MetacriticKind == "none" ? Windows.UI.Color.FromArgb(255, 200, 190, 196) : Windows.UI.Color.FromArgb(255, 18, 9, 15));
        // Difficulty bars
        DiffBars.Children.Clear();
        for (var i = 1; i <= 4; i++)
        {
            var on = i <= vm.DifficultyLevel;
            var bar = new Rectangle { Width = 8, Height = 8 + i * 4, RadiusX = 2, RadiusY = 2, VerticalAlignment = VerticalAlignment.Bottom,
                Fill = on ? (Brush)Application.Current.Resources[i >= 3 ? "RoseBrush" : "GoldBrush"] : new SolidColorBrush(Windows.UI.Color.FromArgb(0x26, 255, 255, 255)) };
            DiffBars.Children.Add(bar);
            if (on) { bar.Opacity = 0; Fade(bar, 1, 220, 120 * i); }
        }
    }

    // Key art, then the fallback when that game has none.
    private void ShowArt(string url, string fallback)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) { HeroArt.Source = null; return; }
        var bmp = new BitmapImage { UriSource = uri };
        bmp.ImageOpened += (_, _) => { Fade(HeroArt, 1, 600); Zoom(HeroArt); };
        bmp.ImageFailed += (_, _) => { if (url != fallback && fallback.Length > 0 && shownArt == url) ShowArt(fallback, fallback); };
        HeroArt.Source = bmp;
    }

    private void OnBack(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    // Diary: "Want to play" (one click) and "Log it" (opens the full sheet). The label follows the saved status.
    private void PaintDiary()
    {
        if (vm is null || MainWindow.Shell is not { } shell) return;
        var saved = vm.HasTitle ? shell.Diary.Get(vm.ToDiaryEntry().Key) : null;
        WantText.Text = saved is null ? vm.Text["DyWant"] : "✓ " + vm.Text["DyStatus" + saved.Status];
        WantIcon.Glyph = saved is null ? "" : "";
        LogText.Text = vm.Text[saved is null ? "DyLog" : "DyEditorEdit"];
        PaintTrophies();
    }

    // Steam achievements in this game: progress and the rarest ones the player has.
    private void PaintTrophies()
    {
        if (vm is null || MainWindow.Shell is not { } shell) return;
        var game = vm.HasTitle ? shell.Trophies.Game(vm.ToDiaryEntry().AppId) : null;
        TrophyStrip.Visibility = game is { Total: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        if (game is null) return;
        var culture = System.Globalization.CultureInfo.GetCultureInfo(vm.Text.Language);
        TrophyTitle.Text = vm.Text["TrDetailTitle"]; TrophyAll.Text = vm.Text["TrDetailAll"];
        TrophyCount.Text = vm.Text.Format("TrGameCount", game.Unlocked, game.Total);
        TrophyPercent.Text = (game.Fraction * 100).ToString("0", culture) + "%";
        TrophyBar.Value = game.Fraction * 100;
        TrophyIcons.Children.Clear();
        foreach (var a in game.Items.Where(a => a.Unlocked).OrderBy(a => a.Percent ?? 101).Take(10))
        {
            var url = Lotus.Core.Achievements.SteamGameAchievements.IconUrl(game.AppId, a.Icon);
            var tile = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(9), Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["HeroBrush"] };
            if (Uri.TryCreate(url, UriKind.Absolute, out var u)) tile.Child = new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(u) { DecodePixelWidth = 92 }, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
            ToolTipService.SetToolTip(tile, a.Percent is { } p ? $"{a.Name} · {p.ToString(p < 10 ? "0.0" : "0", culture)}%" : a.Name);
            TrophyIcons.Children.Add(tile);
        }
    }
    private void OnTrophies(object sender, RoutedEventArgs e) { if (vm?.ToDiaryEntry().AppId is { } id) MainWindow.Shell?.ShowSteamAchievements(id); }
    private void OnWant(object sender, RoutedEventArgs e)
    {
        if (vm is null || MainWindow.Shell is not { } shell) return;
        var game = vm.ToDiaryEntry();
        if (shell.Diary.Contains(game.Key)) shell.OpenDiaryEditor(game); else { shell.WantGame(game); Motion.Pop(WantIcon); }
        PaintDiary();
    }
    private void OnLog(object sender, RoutedEventArgs e) { if (vm is not null) MainWindow.Shell?.OpenDiaryEditor(vm.ToDiaryEntry()); }
    private void OnPlay(object sender, RoutedEventArgs e) { if (vm?.Installed is { } g) { UiSound.Play(UiSound.Cue.Open); PlayRequested?.Invoke(this, g); } }
    private async void OnStore(object sender, RoutedEventArgs e) => await Open(vm?.StoreUrl);
    private async void OnMetacriticLink(object sender, RoutedEventArgs e) => await Open(vm?.MetacriticUrl);
    private async void OnMetacritic(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => await Open(vm?.MetacriticUrl);
    private static async Task Open(string? url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) await Windows.System.Launcher.LaunchUriAsync(uri);
    }

    private static void Fade(UIElement e, double to, int ms, int delay = 0)
    {
        var a = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = to, Duration = TimeSpan.FromMilliseconds(Motion.Active ? ms : 0), BeginTime = TimeSpan.FromMilliseconds(Motion.Active ? delay : 0) };
        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard(); Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(a, e); Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(a, "Opacity"); sb.Children.Add(a); sb.Begin();
    }
    // Slow "Ken Burns" push on the hero art.
    private static void Zoom(FrameworkElement e)
    {
        if (!Motion.Active) return;
        var v = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(e); var c = v.Compositor;
        v.StartAnimation("CenterPoint", c.CreateExpressionAnimation("Vector3(this.Target.Size.X * 0.5, this.Target.Size.Y * 0.5, 0)"));
        var a = c.CreateVector3KeyFrameAnimation(); a.InsertKeyFrame(0, new(1.0f, 1.0f, 1)); a.InsertKeyFrame(1, new(1.08f, 1.08f, 1));
        a.Duration = TimeSpan.FromSeconds(18); a.Direction = Microsoft.UI.Composition.AnimationDirection.Alternate; a.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;
        v.StartAnimation("Scale", a);
    }
}
