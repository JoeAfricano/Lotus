using Lotus.Core.Diary;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App.Controls;

// "O que jogar agora?" card in the diary.
public sealed partial class PlayNextView : UserControl
{
    private PlayNextViewModel? Vm => DataContext as PlayNextViewModel;
    private bool filling;
    public PlayNextView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Fill();
        Loaded += (_, _) => { Vm?.Refresh(); Fill(); };
        SizeChanged += (_, e) => Arrange(e.NewSize.Width);
    }

    private void Fill()
    {
        if (Vm is not { } vm) return;
        filling = true;
        VibeBox.Items.Clear();
        foreach (var v in PlayNext.Vibes) VibeBox.Items.Add(new ComboBoxItem { Content = vm.Text["PnVibe" + v + "Label"], Tag = v });
        VibeBox.SelectedIndex = Array.IndexOf(PlayNext.Vibes, vm.Vibe);
        filling = false;
        PaintTime();
    }

    private void PaintTime()
    {
        if (Vm is not { } vm) return;
        foreach (var b in new[] { TimeShort, TimeEvening, TimeLong })
        {
            var on = (string)b.Tag == vm.Time.ToString();
            b.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[on ? "AccentSoftBrush" : "SubtleBrush"];
            b.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[on ? "RoseBrush" : "CardBorderBrush"];
            b.FontWeight = on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        }
    }

    private void OnTime(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || sender is not Button { Tag: string t } || !Enum.TryParse<PlayTime>(t, out var time)) return;
        vm.Time = time; PaintTime(); Reveal(); UiSound.Play(UiSound.Cue.Toggle);
    }
    private void OnVibe(object sender, SelectionChangedEventArgs e)
    {
        if (filling || Vm is not { } vm || VibeBox.SelectedItem is not ComboBoxItem { Tag: string v }) return;
        vm.Vibe = v; Reveal();
    }
    private void OnAnother(object sender, RoutedEventArgs e) { Vm?.Another(); Reveal(); UiSound.Play(UiSound.Cue.Toggle); }
    private void OnOpen(object sender, RoutedEventArgs e) { if (Vm?.Top is { } p) MainWindow.Shell?.OpenDiaryEditor(p.Entry); }

    // The new pick slides in.
    private void Reveal() { Motion.Pop(PickCover); Motion.Settle(PickText); }

    // Side by side; stacked on narrow windows.
    private void Arrange(double width)
    {
        var narrow = width < 760;
        Root.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        foreach (var el in new FrameworkElement[] { PickArea, (FrameworkElement)Root.Children[2] })
        { Grid.SetColumn(el, narrow ? 0 : 1); Grid.SetRow(el, narrow ? 1 : 0); }
    }
}
