using Lotus.Core.Welcome;
using Microsoft.UI.Xaml;

namespace Lotus.App;

// "Conheça o Lotus" guide: after the first-run profile setup (all pages) and after each update (Novidades), always
// after the opening intro has finished. Settings can open it again at any time.
public sealed partial class MainWindow
{
    private bool tourAfterOnboarding;

    private void StartWelcome()
    {
        var args = Environment.GetCommandLineArgs();
        var p = preferences.Current;
        var opening = WelcomePlan.Decide(p.Onboarded, p.WelcomeSeenVersion, AppVersionText(),
            args.Contains(Lotus.Infrastructure.Setup.WindowsStartup.MinimizedArgument), args.Contains("--dev-instance"),
            args.Contains(WelcomePlan.TourArgument), args.Contains(WelcomePlan.WhatsNewArgument));
        Tour.TryRequested += (_, route) => ViewModel.NavigateCommand.Execute(route);
        Tour.Closed += (_, _) => MarkWelcomeSeen();
        if (onboarding.NeedsWelcome)
        {
            tourAfterOnboarding = opening == WelcomeOpening.Tour;
            AfterIntro(() => OpenOnboarding(edit: false));
            return;
        }
        if (opening != WelcomeOpening.None) AfterIntro(() => OpenTour(opening));
        else AfterIntro(() => { if (AppWindow.IsVisible) UiSound.Play(UiSound.Cue.Startup); }); // soft hello (not when starting in the tray)
    }

    // Called when the first-run profile setup closes: the feature guide follows it.
    private void ContinueAfterOnboarding()
    {
        if (!tourAfterOnboarding) return;
        tourAfterOnboarding = false;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => OpenTour(WelcomeOpening.Tour));
    }

    public void OpenTour(WelcomeOpening opening)
    {
        if (Tour.IsOpen) return;
        WhatsNewNotes? notes = null;
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md");
            if (File.Exists(path)) notes = WhatsNewReader.Read(File.ReadAllText(path), AppVersionText());
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "What's new notes not read"); }
        Tour.MotionEnabled = !preferences.Current.ReduceMotion;
        Tour.Open(new WelcomeTourViewModel(text, notes), opening == WelcomeOpening.None ? WelcomeOpening.Tour : opening);
    }

    // A development instance shares the player's data: it never marks the guide as seen for the installed Lotus.
    private void MarkWelcomeSeen()
    {
        if (Environment.GetCommandLineArgs().Contains("--dev-instance")) return;
        var version = AppVersionText();
        if (version.Length > 0 && preferences.Current.WelcomeSeenVersion != version) preferences.Update(preferences.Current with { WelcomeSeenVersion = version });
    }

    // Runs after the opening intro (right away when there is none), so nothing opens or plays underneath it.
    private void AfterIntro(Action action)
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (Intro.Visibility != Visibility.Visible) { action(); return; }
            EventHandler? handler = null;
            handler = (_, _) => { Intro.Finished -= handler; action(); };
            Intro.Finished += handler;
        });
    }
}
