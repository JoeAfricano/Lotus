using Lotus.Core.Brand;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Lotus.App.Controls;

// Plays the opening intro over the whole window while Lotus loads behind it, then fades away and frees the player.
// Never blocks: a missing file, a codec error or a stuck player all just close it (with a time limit as a last resort).
public sealed partial class IntroView : UserControl
{
    private MediaPlayer? media;
    private DispatcherTimer? limit;
    private bool closing;
    public event EventHandler? Finished;

    public IntroView()
    {
        InitializeComponent();
        var esc = new KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape };
        esc.Invoked += (_, e) => { e.Handled = true; Close(); };
        KeyboardAccelerators.Add(esc);
    }

    public async void Play(IntroKind kind, string skipHint, double volume)
    {
        if (kind == IntroKind.None) return;
        var file = Path.Combine(AppContext.BaseDirectory, "Assets", "Intro", kind == IntroKind.Full ? "lotus-intro.mp4" : "lotus-intro-curta.mp4");
        if (!File.Exists(file)) return;
        try
        {
            Visibility = Visibility.Visible; Opacity = 1; SkipHint.Text = skipHint;
            media = new MediaPlayer { Volume = Math.Clamp(volume, 0, 1), IsMuted = volume <= 0, AudioCategory = MediaPlayerAudioCategory.Media };
            media.MediaEnded += (_, _) => DispatcherQueue.TryEnqueue(Close);
            media.MediaFailed += (_, e) => { Serilog.Log.Warning("Intro video failed: {Error}", e.ErrorMessage); DispatcherQueue.TryEnqueue(Close); };
            media.Source = MediaSource.CreateFromStorageFile(await Windows.Storage.StorageFile.GetFileFromPathAsync(file));
            Player.SetMediaPlayer(media);
            media.Play();
            Focus(FocusState.Programmatic);
            limit = new DispatcherTimer { Interval = TimeSpan.FromSeconds(kind == IntroKind.Full ? 11 : 5) };
            limit.Tick += (_, _) => Close(); limit.Start();
            Fade(SkipHint, 0, 1, 400, TimeSpan.FromMilliseconds(700));
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Intro not played"); Close(); }
    }

    private void OnSkip(object sender, TappedRoutedEventArgs e) { e.Handled = true; Close(); }

    private void Close()
    {
        if (closing || Visibility == Visibility.Collapsed) return;
        closing = true; limit?.Stop();
        var story = Fade(this, Opacity, 0, 280, TimeSpan.Zero);
        story.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            try { media?.Pause(); Player.SetMediaPlayer(null); media?.Dispose(); } catch (Exception) { }
            media = null; Finished?.Invoke(this, EventArgs.Empty);
        };
    }

    private static Storyboard Fade(UIElement target, double from, double to, int ms, TimeSpan delay)
    {
        var animation = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(ms), BeginTime = delay, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(animation, target); Storyboard.SetTargetProperty(animation, "Opacity");
        var story = new Storyboard(); story.Children.Add(animation); story.Begin(); return story;
    }
}
