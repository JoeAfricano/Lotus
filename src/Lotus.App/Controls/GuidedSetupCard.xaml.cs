using Lotus.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App.Controls;

// Home's first-run guided setup. The view model does the work; this only forwards clicks, opens links and adds motion.
public sealed partial class GuidedSetupCard : UserControl
{
    public event EventHandler<string>? NavigateRequested; // section id
    public GuidedSetupCard() => InitializeComponent();
    private GuidedSetupViewModel? Vm => DataContext as GuidedSetupViewModel;

    private async void OnAnalyze(object sender, RoutedEventArgs e) { if (Vm is { } vm) { Motion.Pop(BadgeIcon); await vm.AnalyzeAsync(); } }
    private async void OnApply(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (vm.AcceptedCount == 0) { vm.Close(); return; } // nothing accepted: just finish
        await vm.ApplyAsync();
        UiSound.Play(UiSound.Cue.Score); // the whole setup is done
    }
    private void OnClose(object sender, RoutedEventArgs e) => Vm?.Close();
    private void OnGo(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id }) return;
        if (Vm is { IsDone: true } vm) vm.Close();
        NavigateRequested?.Invoke(this, id);
    }
    private async void OnOpenLink(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            await Windows.System.Launcher.LaunchUriAsync(uri);
    }
    // Each check pops in as its step finishes.
    private void OnResultIconLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement f) f.RegisterPropertyChangedCallback(VisibilityProperty, (s, _) => { if (((UIElement)s).Visibility == Visibility.Visible) Motion.Pop((UIElement)s); });
    }
}
