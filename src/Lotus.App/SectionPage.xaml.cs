using Lotus.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Hosting;
using System.Numerics;
using Windows.UI.ViewManagement;

namespace Lotus.App;

public sealed partial class SectionPage : Page
{
    public bool ShowDashboard { get; private set; }
    public bool ShowSettings { get; private set; }
    public bool ShowModule { get; private set; }
    public SectionPage()
    {
        InitializeComponent();
        Loaded += (_, _) => AnimateEntrance();
    }
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not SectionViewModel viewModel) throw new ArgumentException("A section view model is required.");
        DataContext = viewModel;
        Language = viewModel.Text.Language;
        ShowDashboard = viewModel.Section.Id == "dashboard";
        ShowSettings = viewModel.Section.Id == "settings";
        ShowModule = !ShowDashboard && !ShowSettings;
        Bindings.Update();
        if (LogLocation is not null)
        {
            LogLocation.Visibility = viewModel.Section.Id == "logs" ? Visibility.Visible : Visibility.Collapsed;
            LogLocation.Text = viewModel.Text.Format("LogLocation", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "Logs"));
        }
    }
    private void AnimateEntrance()
    {
        if (DataContext is not SectionViewModel { MotionEnabled: true } || !new UISettings().AnimationsEnabled) return;
        var compositor = ElementCompositionPreview.GetElementVisual(PageContent).Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.8f), new Vector2(0.2f, 1));
        var movement = compositor.CreateVector3KeyFrameAnimation();
        movement.Target = "Translation";
        movement.InsertKeyFrame(0, new Vector3(0, 12, 0));
        movement.InsertKeyFrame(1, Vector3.Zero, easing);
        movement.Duration = TimeSpan.FromMilliseconds(220);
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Target = "Opacity";
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, easing);
        fade.Duration = TimeSpan.FromMilliseconds(180);
        PageContent.StartAnimation(movement);
        PageContent.StartAnimation(fade);
    }
    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Keep the scroll content at its natural constrained width. Binding Width to the
        // viewport while centering a MaxWidth element can retain a stale clip on restore.
        var inset = Math.Max(0, (e.NewSize.Width - 1200) / 2);
        PageContent.Margin = new Thickness(inset, 0, inset, 0);
    }
    private void OnContentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // View-only adaptation uses available content width, including the sidebar state.
        var stacked = e.NewSize.Width < 790;
        if (ScoreCard is not null && TelemetryCard is not null)
        {
            Grid.SetColumnSpan(ScoreCard, stacked ? 2 : 1);
            Grid.SetColumn(TelemetryCard, stacked ? 0 : 1);
            Grid.SetRow(TelemetryCard, stacked ? 1 : 0);
            Grid.SetColumnSpan(TelemetryCard, stacked ? 2 : 1);
        }
        if (HeroArt is not null) HeroArt.Visibility = e.NewSize.Width < 780 ? Visibility.Collapsed : Visibility.Visible;
        PageContent.Padding = new Thickness(e.NewSize.Width < 700 ? 16 : 24);
    }
}
