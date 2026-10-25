using Lotus.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Lotus.App.Controls;

// Shows a TaskState: bar and percentage while running; on completion the check pops in (scale with a small
// overshoot) and the result fades in. Bind with State="{Binding SomeTask}".
public sealed partial class TaskProgressView : UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(TaskState), typeof(TaskProgressView),
        new PropertyMetadata(null, (d, e) => ((TaskProgressView)d).Attach(e.OldValue as TaskState, e.NewValue as TaskState)));
    public TaskState? State { get => (TaskState?)GetValue(StateProperty); set => SetValue(StateProperty, value); }

    public TaskProgressView() => InitializeComponent();

    private void Attach(TaskState? old, TaskState? state)
    {
        if (old is not null) old.PropertyChanged -= OnChanged;
        if (state is not null) state.PropertyChanged += OnChanged;
        Render(animate: false);
    }

    private void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TaskState.Done) && State is { Done: true }) { Render(animate: true); return; }
        Render(animate: false);
    }

    private void Render(bool animate)
    {
        var s = State;
        RunningPart.Visibility = s is { Running: true } ? Visibility.Visible : Visibility.Collapsed;
        DonePart.Visibility = s is { Done: true } ? Visibility.Visible : Visibility.Collapsed;
        if (s is null) return;
        Bar.Value = s.Percent; PercentLabel.Text = s.PercentText; DoneLabel.Text = s.DoneText;
        if (animate && Motion.Enabled && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        {
            var sb = new Storyboard();
            Add(sb, CheckScale, "ScaleX", 0.3, 1, 420, new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut });
            Add(sb, CheckScale, "ScaleY", 0.3, 1, 420, new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut });
            Add(sb, Check, "Opacity", 0, 1, 180, new CubicEase { EasingMode = EasingMode.EaseOut });
            var label = new DoubleAnimation { From = 0, To = 1, BeginTime = TimeSpan.FromMilliseconds(120), Duration = TimeSpan.FromMilliseconds(260), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(label, DoneLabel); Storyboard.SetTargetProperty(label, "Opacity"); sb.Children.Add(label);
            sb.Begin();
        }
    }

    private static void Add(Storyboard sb, DependencyObject target, string property, double from, double to, int ms, EasingFunctionBase ease)
    {
        var a = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(ms), EasingFunction = ease };
        Storyboard.SetTarget(a, target); Storyboard.SetTargetProperty(a, property); sb.Children.Add(a);
    }
}
