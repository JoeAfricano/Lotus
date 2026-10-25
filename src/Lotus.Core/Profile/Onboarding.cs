using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Profile;

// The player's identity in Lotus: read from the Steam account already signed in on this PC (local files only,
// no login, no internet) or chosen by the player (name + picture copied into Lotus's own folder).
public sealed record SteamProfile(string Name, string? AvatarPath);
public interface IProfileSource
{
    SteamProfile? ReadSteam();
    bool SteamInstalled();
    string ImportPicture(string sourcePath); // copies the picture into Lotus's folder and returns the copy
}

public sealed record AccentOption(string Id, string Label, string Hex, bool Selected);
public sealed record GoalOption(string Id, string Title, string Detail, string Glyph, bool Selected);

// First-run welcome in steps (and later "Edit profile", which starts at the profile step and skips the tour).
public sealed class OnboardingViewModel : ObservableObject
{
    public const int WelcomeStep = 0, ProfileStep = 1, GoalStep = 2, AccentStep = 3, TourStep = 4;
    private readonly PreferencesService preferences;
    private readonly IProfileSource source;
    private readonly Action<Exception> log;
    private int step;
    private bool editing;
    private string source_ = "none", customName = "";
    private string? customPicture;
    private string goal, accent;
    private SteamProfile? steam;

    public ITextCatalog Text { get; }
    public event EventHandler? Finished;
    public event EventHandler<string>? AccentPreview; // live preview while choosing a color

    public OnboardingViewModel(PreferencesService preferences, IProfileSource source, ITextCatalog text, Action<Exception> log)
    {
        this.preferences = preferences; this.source = source; Text = text; this.log = log;
        var p = preferences.Current;
        goal = p.Goal; accent = p.Accent; source_ = p.ProfileSource;
        customName = p.ProfileSource == "custom" ? p.DisplayName : ""; customPicture = p.ProfileSource == "custom" ? p.AvatarPath : null;
    }

    public bool NeedsWelcome => !preferences.Current.Onboarded;
    public int Step => step;
    public int StepCount => editing ? 3 : 5;
    public int StepNumber => editing ? step : step + 1;
    public bool IsWelcome => step == WelcomeStep; public bool IsProfile => step == ProfileStep; public bool IsGoal => step == GoalStep;
    public bool IsAccent => step == AccentStep; public bool IsTour => step == TourStep;
    public bool CanBack => editing ? step > ProfileStep : step > WelcomeStep;
    public bool IsLast => editing ? step == AccentStep : step == TourStep;
    public string NextLabel => Text[IsLast ? (editing ? "ObSave" : "ObStart") : step == WelcomeStep ? "ObBegin" : "ObNext"];
    public string StepText => Text.Format("ObStepOf", StepNumber, StepCount);

    // Profile step
    public bool SteamFound => steam is not null;
    public string SteamName => steam?.Name ?? "";
    public string? SteamPicture => steam?.AvatarPath;
    public bool UseSteam { get => source_ == "steam"; set { if (value) { source_ = "steam"; NotifyProfile(); } } }
    public bool UseCustom { get => source_ == "custom"; set { if (value) { source_ = "custom"; NotifyProfile(); } } }
    public string CustomName { get => customName; set { if (SetProperty(ref customName, value ?? "")) NotifyProfile(); } }
    public string? CustomPicture => customPicture;
    public string PreviewName => source_ == "steam" ? SteamName : customName.Trim().Length > 0 ? customName.Trim() : Text["ObDefaultName"];
    public string? PreviewPicture => source_ == "steam" ? SteamPicture : customPicture;
    public string PreviewInitial => PreviewName.Length > 0 ? char.ToUpperInvariant(PreviewName[0]).ToString() : "P";

    // Goal and accent
    public IReadOnlyList<GoalOption> Goals => [.. InterfacePreferences.Goals.Select(g => new GoalOption(g, Text["ObGoal" + g], Text["ObGoal" + g + "Detail"], g switch { "fps" => "", "competitive" => "", _ => "" }, g == goal))];
    public string Goal { get => goal; set { if (value is not null && SetProperty(ref goal, value)) { OnPropertyChanged(nameof(GoalTitle)); OnPropertyChanged(nameof(Goals)); } } }
    public string GoalTitle => Text["ObGoal" + goal];
    public IReadOnlyList<AccentOption> Accents => [.. new[] { ("rose", "AccentRose", "#E8378A"), ("crimson", "AccentCrimson", "#E0384C"), ("gold", "AccentGold", "#E3A350"), ("violet", "AccentViolet", "#9C5CF0"), ("dark", "AccentDark", "#3A2E35") }
        .Select(a => new AccentOption(a.Item1, Text[a.Item2], a.Item3, a.Item1 == accent))];
    public string Accent { get => accent; set { if (value is not null && SetProperty(ref accent, value)) { AccentPreview?.Invoke(this, value); OnPropertyChanged(nameof(Accents)); } } }

    public void Start(bool edit)
    {
        editing = edit;
        try { steam = source.ReadSteam(); } catch (Exception ex) { log(ex); steam = null; }
        if (source_ == "none") source_ = steam is not null ? "steam" : "custom";
        if (source_ == "steam" && steam is null) source_ = "custom";
        step = edit ? ProfileStep : WelcomeStep;
        NotifyAll();
    }

    // No Steam account yet: the player can sign in to Steam now; the view re-checks every few seconds.
    public bool SteamInstalled { get { try { return source.SteamInstalled(); } catch (Exception ex) { log(ex); return false; } } }
    public string SteamSignInUrl => SteamInstalled ? "steam://open/main" : "https://store.steampowered.com/about/";
    public string SteamSignInLabel => Text[SteamInstalled ? "ObSteamSignIn" : "ObSteamGet"];
    public bool RecheckSteam()
    {
        if (steam is not null) return true;
        try { steam = source.ReadSteam(); } catch (Exception ex) { log(ex); }
        if (steam is null) return false;
        source_ = "steam";
        foreach (var n in new[] { nameof(SteamFound), nameof(SteamName), nameof(SteamPicture) }) OnPropertyChanged(n);
        NotifyProfile();
        return true;
    }

    public void ChoosePicture(string path)
    {
        try { customPicture = source.ImportPicture(path); source_ = "custom"; }
        catch (Exception ex) { log(ex); }
        NotifyProfile();
    }

    public void Next()
    {
        if (IsLast) { Finish(); return; }
        step++; NotifyAll();
    }
    public void Back() { if (CanBack) { step--; NotifyAll(); } }
    public void Skip() => Finish(); // keeps defaults; the welcome does not show again

    private void Finish()
    {
        var p = preferences.Current;
        var name = PreviewName == Text["ObDefaultName"] && source_ != "steam" ? "" : PreviewName;
        preferences.Update(p with { Onboarded = true, ProfileSource = source_, DisplayName = name, AvatarPath = PreviewPicture, Goal = goal, Accent = accent });
        Finished?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyProfile()
    {
        foreach (var n in new[] { nameof(UseSteam), nameof(UseCustom), nameof(CustomName), nameof(CustomPicture), nameof(PreviewName), nameof(PreviewPicture), nameof(PreviewInitial) }) OnPropertyChanged(n);
    }
    private void NotifyAll()
    {
        foreach (var n in new[] { nameof(Step), nameof(StepCount), nameof(StepNumber), nameof(IsWelcome), nameof(IsProfile), nameof(IsGoal), nameof(IsAccent), nameof(IsTour),
                     nameof(CanBack), nameof(IsLast), nameof(NextLabel), nameof(StepText), nameof(SteamFound), nameof(SteamName), nameof(SteamPicture), nameof(Goal), nameof(Accent), nameof(GoalTitle) })
            OnPropertyChanged(n);
        NotifyProfile();
    }
}
