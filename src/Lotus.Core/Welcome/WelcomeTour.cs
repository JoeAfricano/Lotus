using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Welcome;

// "Conheça o Lotus": one page per feature (short animated scene, plain text, a "try it" button) and a "Novidades" page
// shown after each update. Pattern adapted from the PowerToys OOBE (one page per module + "what's new" after updates);
// written for Lotus, no PowerToys code copied.
public enum WelcomeOpening { None, Tour, WhatsNew }

public static class WelcomePlan
{
    public const string TourArgument = "--welcome";      // opens the tour (also in a development instance, to test)
    public const string WhatsNewArgument = "--whats-new"; // opens straight on Novidades
    public const string FirstRunArgument = "--first-run"; // test: the first-run guide with account and look pages

    // First run: the tour after the profile setup. After an update: Novidades. Seen version is the last one shown.
    public static WelcomeOpening Decide(bool onboarded, string? seenVersion, string currentVersion, bool minimized, bool devInstance, bool tourArgument, bool whatsNewArgument)
    {
        if (whatsNewArgument) return WelcomeOpening.WhatsNew;
        if (tourArgument) return WelcomeOpening.Tour;
        if (minimized || devInstance) return WelcomeOpening.None;        // tray start and script-driven test instances open straight away
        if (!onboarded) return WelcomeOpening.Tour;                      // shown right after the first-run profile setup
        if (string.IsNullOrWhiteSpace(currentVersion)) return WelcomeOpening.None;
        return string.Equals(seenVersion, currentVersion, StringComparison.OrdinalIgnoreCase) ? WelcomeOpening.None : WelcomeOpening.WhatsNew;
    }
}

// One page of the tour. Scene names the animated illustration the view draws; Route is where "Experimentar" goes.
public sealed record WelcomePage(string Id, string Glyph, string Title, string Headline, string Lead, IReadOnlyList<string> Points, string TryLabel, string? Route)
{
    public bool IsSetup => Id is WelcomeTourViewModel.ProfileId or WelcomeTourViewModel.ThemeId; // interactive first-run choices
}

public sealed record WhatsNewGroup(string Title, IReadOnlyList<string> Items);
public sealed record WhatsNewNotes(string Version, string Name, string Date, IReadOnlyList<WhatsNewGroup> Groups);

// Reads the current version's section of the CHANGELOG shipped with the app (no internet).
public static class WhatsNewReader
{
    // Changelog files to try for a language, best first (CHANGELOG.en-US.md for English, then CHANGELOG.md).
    public static IReadOnlyList<string> Files(string? language) =>
        language is { } l && l.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? ["CHANGELOG.en-US.md", "CHANGELOG.md"] : ["CHANGELOG.md"];

    public static WhatsNewNotes? Read(string? markdown, string version)
    {
        if (string.IsNullOrWhiteSpace(markdown) || string.IsNullOrWhiteSpace(version)) return null;
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var core = version.Split('-', '+')[0].Trim();
        int start = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("## ", StringComparison.Ordinal)) continue;
            var head = lines[i][3..].Trim();
            var tag = head.Split(' ', 2)[0];
            if (string.Equals(tag, version, StringComparison.OrdinalIgnoreCase) || string.Equals(tag.Split('-')[0], core, StringComparison.OrdinalIgnoreCase)) { start = i; break; }
        }
        if (start < 0) return null;
        var header = lines[start][3..].Trim();
        var versionTag = header.Split(' ', 2)[0];
        var name = Between(header, '"', '"');
        var date = Between(header, '(', ')');
        var groups = new List<WhatsNewGroup>();
        string? title = null; var items = new List<string>();
        void Flush() { if (items.Count > 0) groups.Add(new WhatsNewGroup(title ?? "", [.. items])); items = []; }
        for (var i = start + 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            if (line.StartsWith("## ", StringComparison.Ordinal)) break;
            if (line.StartsWith("### ", StringComparison.Ordinal)) { Flush(); title = line[4..].Trim(); continue; }
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal)) items.Add(trimmed[2..].Trim());
            else if (trimmed.Length > 0 && items.Count > 0 && line.StartsWith(' ')) items[^1] += " " + trimmed; // wrapped bullet
        }
        Flush();
        return groups.Count == 0 ? null : new WhatsNewNotes(versionTag, name, date, groups);
    }

    private static string Between(string text, char open, char close)
    {
        var a = text.IndexOf(open); if (a < 0) return "";
        var b = text.IndexOf(close, a + 1); return b > a ? text[(a + 1)..b] : "";
    }
}

public sealed class WelcomeTourViewModel : ObservableObject
{
    public const string WhatsNewId = "news", ProfileId = "profile", ThemeId = "theme";
    // First run: account and look come right after the welcome (they used to be the separate profile setup).
    private static readonly (string Id, string Glyph, string? Route)[] Definitions =
    [
        ("welcome", "", null), (ProfileId, "\uE77B", null), (ThemeId, "\uE790", null), ("optimize", "", "optimize"), ("games", "", "games"), ("score", "", "lotusscore"),
        ("tools", "", "tools"), ("saves", "", "saves"), ("diary", "", "diary"), (WhatsNewId, "", null),
    ];
    private int index;

    public WelcomeTourViewModel(ITextCatalog text, WhatsNewNotes? notes, bool includeSetup = false)
    {
        Text = text; Notes = notes;
        Pages = [.. Definitions.Where(d => includeSetup || d.Id is not (ProfileId or ThemeId)).Select(d => new WelcomePage(d.Id, d.Glyph, text["Wt" + Key(d.Id)], text["Wt" + Key(d.Id) + "Headline"], text["Wt" + Key(d.Id) + "Lead"],
            [text["Wt" + Key(d.Id) + "P1"], text["Wt" + Key(d.Id) + "P2"], text["Wt" + Key(d.Id) + "P3"]], text["Wt" + Key(d.Id) + "Try"], d.Route))];
    }

    private static string Key(string id) => char.ToUpperInvariant(id[0]) + id[1..];

    public ITextCatalog Text { get; }
    public WhatsNewNotes? Notes { get; }
    public IReadOnlyList<WelcomePage> Pages { get; }
    public int Index { get => index; set { value = Math.Clamp(value, 0, Pages.Count - 1); if (SetProperty(ref index, value)) Notify(); } }
    public WelcomePage Current => Pages[index];
    public bool IsWhatsNew => Current.Id == WhatsNewId;
    public bool HasNotes => Notes is not null;
    public bool CanBack => index > 0;
    public bool IsLast => index == Pages.Count - 1;
    public string NextLabel => Text[IsLast ? "WtFinish" : index == 0 ? "WtStart" : "WtNext"];
    public string StepText => Text.Format("ObStepOf", index + 1, Pages.Count);
    public string NotesTitle => Notes is null ? Text["WtNewsNone"] : Text.Format("WtNewsVersion", Notes.Name.Length > 0 && Notes.Name != "Lotus" ? Notes.Name : "Lotus " + Lotus.Core.Updates.UpdateVersions.Display(Notes.Version));

    public void Open(WelcomeOpening opening) { index = opening == WelcomeOpening.WhatsNew ? Pages.Count - 1 : 0; Notify(); }
    public void Next() { if (!IsLast) Index = index + 1; }
    public void Back() { if (CanBack) Index = index - 1; }
    public void Go(string id) { var i = Pages.ToList().FindIndex(p => p.Id == id); if (i >= 0) Index = i; }

    private void Notify()
    {
        foreach (var n in new[] { nameof(Index), nameof(Current), nameof(IsWhatsNew), nameof(CanBack), nameof(IsLast), nameof(NextLabel), nameof(StepText) }) OnPropertyChanged(n);
    }
}
