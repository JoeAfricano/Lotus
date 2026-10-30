using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Lotus.App.Controls;

// Discreet game notices (end of session, save backups, preparation): one at a time, bottom right, inside Lotus's
// window (never a window over the game, never stealing focus). Hovering or typing holds the timer.
public sealed partial class GameNotice : UserControl
{
    public sealed record Action(string Label, System.Action Run, bool Primary = false);
    public sealed record Request(string Eyebrow, string Title, string Body, string Glyph, string? Art, IReadOnlyList<Action> Actions, double Seconds = 12,
        string? NotePlaceholder = null, Func<string, bool>? SaveNote = null, string? SaveLabel = null);

    private readonly Queue<Request> queue = new();
    private Request? current;
    private double left, seconds;
    private bool held, writing;
    public bool IsShowing => current is not null;

    public GameNotice() => InitializeComponent();

    public void Show(Request request)
    {
        queue.Enqueue(request);
        if (current is null) Next();
    }

    private void Next()
    {
        if (queue.Count == 0) { current = null; Visibility = Visibility.Collapsed; return; }
        current = queue.Dequeue();
        writing = false; held = false;
        Eyebrow.Text = current.Eyebrow.ToUpperInvariant(); Title.Text = current.Title; Body.Text = current.Body; Body.Visibility = current.Body.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Glyph.Glyph = current.Glyph;
        ArtFrame.Visibility = Visibility.Collapsed;
        if (current.Art is { Length: > 0 } a && Uri.TryCreate(a, UriKind.Absolute, out var u)) { Art.ImageSource = new BitmapImage(u) { DecodePixelWidth = 104 }; ArtFrame.Visibility = Visibility.Visible; }
        Note.Text = ""; Note.PlaceholderText = current.NotePlaceholder ?? ""; Note.Visibility = Visibility.Collapsed;
        BuildActions(current.Actions);
        Visibility = Visibility.Visible;
        var sb = new Storyboard();
        Add(sb, Card, "Opacity", 0, 1, 260); Add(sb, Move, "TranslateX", 40, 0, 380);
        sb.Begin();
        seconds = left = current.Seconds; TimerScale.ScaleX = 1;
        long last = -1;
        Motion.OnFrames(this, ms => { var step = last < 0 ? 0 : (ms - last) / 1000.0; last = (long)ms; return Tick(step); });
    }

    private void BuildActions(IReadOnlyList<Action> actions)
    {
        Actions.Children.Clear();
        foreach (var a in actions)
        {
            var b = new Button { Content = a.Label, Padding = new Thickness(12, 5, 12, 5), Style = (Style)Application.Current.Resources[a.Primary ? "PrimaryButtonStyle" : "SoftButtonStyle"], MinHeight = 32 };
            var run = a.Run;
            b.Click += (_, _) => { run(); };
            Actions.Children.Add(b);
        }
    }

    // "Anotar": the note box opens in the notice and the buttons become Salvar / Cancelar. The timer stops while writing.
    public void StartWriting()
    {
        if (current?.SaveNote is not { } save) return;
        writing = true; held = true; TimerScale.ScaleX = 1;
        Note.Visibility = Visibility.Visible;
        BuildActions([new(current.SaveLabel ?? "OK", () => { if (save(Note.Text)) Hide(); else Body.Text = current?.Body ?? ""; }, Primary: true), new(Lotus.App.Controls.GameNotice.CancelLabel, Hide)]);
        Note.Focus(FocusState.Programmatic);
    }
    public static string CancelLabel { get; set; } = "Cancelar";

    private bool Tick(double elapsed)
    {
        if (current is null) return false;
        if (held || writing) return true;
        left -= elapsed;
        TimerScale.ScaleX = Math.Max(0, left / seconds);
        if (left > 0) return true;
        Hide();
        return false;
    }

    public void Hide()
    {
        Motion.StopFrames(this);
        var sb = new Storyboard();
        Add(sb, Card, "Opacity", 1, 0, 180); Add(sb, Move, "TranslateX", 0, 30, 200);
        sb.Completed += (_, _) => Next();
        sb.Begin();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Hide();
    private void OnHold(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => held = true;
    private void OnRelease(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) { if (!writing) held = false; }
    private void OnNoteFocus(object sender, RoutedEventArgs e) => held = true;

    private static void Add(Storyboard sb, DependencyObject target, string property, double from, double to, int ms)
    {
        var a = new DoubleAnimation { From = from, To = to, Duration = Motion.Ms(ms), EasingFunction = to > from ? Motion.Decelerate() : Motion.Accelerate() };
        Storyboard.SetTarget(a, target); Storyboard.SetTargetProperty(a, property); sb.Children.Add(a);
    }
}
