using Lotus.Core.Diary;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Lotus.App.Controls;

// Yearly recap: story-style screens (tap or arrows to move, auto-advance), ending on a summary card saved as a PNG.
public sealed partial class DiaryRecapView : UserControl
{
    private DiaryRecapViewModel? vm;
    private int index;
    private DispatcherTimer? auto;
    private DateTime slideStart;
    private static readonly TimeSpan SlideTime = TimeSpan.FromSeconds(6);
    private Storyboard? blobs;
    public bool IsOpen => Visibility == Visibility.Visible;

    public DiaryRecapView()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Escape: e.Handled = true; Close(); break;
                case Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Space: e.Handled = true; Go(index + 1); break;
                case Windows.System.VirtualKey.Left: e.Handled = true; Go(index - 1); break;
            }
        };
    }

    public void Open(DiaryRecapViewModel model)
    {
        vm = model; index = 0;
        Segments.Children.Clear(); Segments.ColumnDefinitions.Clear();
        for (var i = 0; i < model.Slides.Count; i++)
        {
            Segments.ColumnDefinitions.Add(new ColumnDefinition());
            var bar = new ProgressBar { Maximum = 1, MinHeight = 4, CornerRadius = new CornerRadius(2), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xF8, 0xDD, 0xCB)) };
            Grid.SetColumn(bar, i); Segments.Children.Add(bar);
        }
        SummaryEyebrow.Text = model.Text.Format("DyRcEyebrow", model.Year).ToUpperInvariant();
        SummaryName.Text = model.Text.Format("DyRcSummaryName", model.PlayerName, model.Year);
        SummaryLines.ItemsSource = model.Summary;
        SavedText.Text = "";
        Visibility = Visibility.Visible;
        Fade(Backdrop, 0, 1, 220);
        var sb = new Storyboard();
        Add(sb, Card, "Opacity", 0, 1, 320); Add(sb, CardMove, "TranslateY", 30, 0, 420); Add(sb, CardMove, "ScaleX", 0.96, 1, 420); Add(sb, CardMove, "ScaleY", 0.96, 1, 420);
        sb.Begin();
        StartBlobs();
        Show(0, forward: true);
        auto?.Stop();
        auto = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        auto.Tick += (_, _) => Tick();
        auto.Start();
        UiSound.Play(UiSound.Cue.Open);
        NextButton.Focus(FocusState.Programmatic);
    }

    public void Close()
    {
        if (!IsOpen) return;
        auto?.Stop(); blobs?.Stop();
        var sb = new Storyboard();
        Add(sb, Backdrop, "Opacity", 1, 0, 180); Add(sb, Card, "Opacity", 1, 0, 180); Add(sb, CardMove, "TranslateY", 0, 20, 220);
        sb.Completed += (_, _) => Visibility = Visibility.Collapsed;
        sb.Begin();
        UiSound.Play(UiSound.Cue.Close);
    }

    // Progress of the current segment; moves on by itself until the summary.
    private void Tick()
    {
        if (vm is null) return;
        var p = Math.Min(1, (DateTime.UtcNow - slideStart) / SlideTime);
        if (Segments.Children[index] is ProgressBar bar) bar.Value = p;
        if (p >= 1 && index < vm.Slides.Count - 1) Go(index + 1);
    }

    private void Go(int to)
    {
        if (vm is null || to < 0) return;
        if (to >= vm.Slides.Count) { Close(); return; }
        if (to == index) return;
        var forward = to > index;
        index = to;
        Show(to, forward);
        UiSound.Play(UiSound.Cue.Toggle);
    }

    private void Show(int i, bool forward)
    {
        if (vm is null) return;
        var s = vm.Slides[i];
        slideStart = DateTime.UtcNow;
        for (var k = 0; k < Segments.Children.Count; k++) if (Segments.Children[k] is ProgressBar b) b.Value = k < i ? 1 : 0;
        var summary = s.Id == "summary";
        Slide.Visibility = summary ? Visibility.Collapsed : Visibility.Visible;
        SummaryPanel.Visibility = summary ? Visibility.Visible : Visibility.Collapsed;
        SlideGlyph.Glyph = s.Glyph; SlideEyebrow.Text = s.Eyebrow; SlideTitle.Text = s.Title; SlideDetail.Text = s.Detail;
        SlideBig.FontSize = s.Big.Length > 14 ? 40 : s.Big.Length > 8 ? 52 : 72;
        SlideBig.LineHeight = SlideBig.FontSize * 1.1;
        if (s.CountTo is { } n && Motion.Enabled) Motion.CountTo(SlideBig, n, v => v + s.Suffix, TimeSpan.FromMilliseconds(1100)); else SlideBig.Text = s.Big;
        // Art: the game's own art on game screens, the best game's art behind the summary.
        var art = summary ? vm.SummaryArt : s.Art;
        artFallback = summary ? vm.SummaryArtFallback : s.ArtFallback;
        SetArt(art);
        Fade(Art, Art.Opacity, art is null ? 0 : 0.6, 500);
        PrevButton.Visibility = i > 0 ? Visibility.Visible : Visibility.Collapsed;
        NextLabel.Text = vm.Text[summary ? "DyRcSave" : "DyRcNext"];
        NextGlyph.Glyph = summary ? "" : "";
        var target = summary ? (UIElement)SummaryPanel : Slide;
        var sb = new Storyboard();
        Add(sb, target, "Opacity", 0, 1, 380);
        Add(sb, target == Slide ? SlideMove : CardMove, target == Slide ? "TranslateX" : "TranslateY", target == Slide ? (forward ? 40 : -40) : 0, 0, 480);
        sb.Begin();
    }

    // Game art; when the wide store art is missing, the art the diary already has.
    private string? artFallback;
    private void SetArt(string? art) => Art.Source = art is { Length: > 0 } a && Uri.TryCreate(a, UriKind.Absolute, out var u) ? new BitmapImage(u) { DecodePixelWidth = 900 } : null;
    private void OnArtFailed(object sender, ExceptionRoutedEventArgs e) { var next = artFallback; artFallback = null; if (next is not null) SetArt(next); }

    // Two soft petals drifting behind the text.
    private void StartBlobs()
    {
        blobs?.Stop();
        if (!Motion.Enabled) return;
        blobs = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        Add(blobs, BlobAMove, "TranslateX", 0, 90, 7000, ease); Add(blobs, BlobAMove, "TranslateY", 0, 60, 9000, ease);
        Add(blobs, BlobBMove, "TranslateX", 0, -80, 8000, ease); Add(blobs, BlobBMove, "TranslateY", 0, -70, 6500, ease);
        blobs.Begin();
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (vm is null) return;
        if (vm.Slides[index].Id == "summary") _ = SaveAsync(); else Go(index + 1);
    }
    private void OnPrev(object sender, RoutedEventArgs e) => Go(index - 1);
    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnBackdrop(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => Close();
    // Tap on the right two thirds: next; left third: back (buttons keep their own clicks).
    private void OnCardTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement f && IsInButton(f)) return;
        var x = e.GetPosition(Card).X;
        Go(x < Card.ActualWidth / 3 ? index - 1 : index + 1);
    }
    private static bool IsInButton(DependencyObject? d)
    {
        for (; d is not null; d = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(d)) if (d is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase) return true;
        return false;
    }

    // The summary card as a PNG in Pictures\Lotus (the buttons and progress are hidden while it is drawn).
    private async Task SaveAsync()
    {
        if (vm is null) return;
        try
        {
            Segments.Opacity = 0; PrevButton.Opacity = NextButton.Opacity = CloseButton.Opacity = 0; SavedText.Text = vm.Text["DyRcFooter"];
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(Shot);
            var pixels = await bitmap.GetPixelsAsync();
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Lotus");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, $"Lotus-{vm.Text["DyRcFileName"]}-{vm.Year}.png");
            using (var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream())
            {
                var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
                var dpi = XamlRoot?.RasterizationScale * 96 ?? 96;
                encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                    (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, dpi, dpi, System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(pixels));
                await encoder.FlushAsync();
                stream.Seek(0);
                using var output = File.Create(file);
                await System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(stream).CopyToAsync(output);
            }
            SavedText.Text = vm.Text.Format("DyRcSaved", file);
            UiSound.Play(UiSound.Cue.Done);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            SavedText.Text = vm.Text["DyRcSaveFailed"];
        }
        finally { Segments.Opacity = 1; PrevButton.Opacity = NextButton.Opacity = CloseButton.Opacity = 1; }
    }

    private static void Fade(UIElement e, double from, double to, int ms) { var sb = new Storyboard(); Add(sb, e, "Opacity", from, to, ms); sb.Begin(); }
    private static void Add(Storyboard sb, DependencyObject target, string property, double from, double to, int ms, EasingFunctionBase? ease = null)
    {
        var a = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(Motion.Enabled ? ms : 0), EasingFunction = ease ?? new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(a, target); Storyboard.SetTargetProperty(a, property); sb.Children.Add(a);
    }
}
