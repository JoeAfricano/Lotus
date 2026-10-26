using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lotus.Core.Integrations;
using Lotus.Core.Optimization;
using Lotus.Core.Tools;

namespace Lotus.Core;

// First-run guided setup on Home: Lotus analyses the PC, asks one plain question per thing it could improve,
// applies only what the player accepted (every action is one Lotus already offers, reversible or to the Recycle
// Bin), shows what happened and then leaves everything free to edit. Shown once; Settings can run it again.
public enum GuidedPhase { Intro, Analyzing, Questions, Applying, Done }

public sealed partial class GuidedQuestion : ObservableObject
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required string Glyph { get; init; }
    public bool IsInfo { get; init; }          // only advice (a link), nothing to apply
    public string? Url { get; init; }
    public string LinkLabel { get; init; } = "";
    public IReadOnlyList<AppRow> Apps { get; init; } = []; // "close heavy apps": the player ticks which ones
    public bool HasApps => Apps.Count > 0;
    public bool IsAction => !IsInfo;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowApps))] private bool accepted;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsOk), nameof(IsFailed), nameof(IsSkipped), nameof(IsOpen), nameof(IsQueued))] private string state = ""; // "" (answering) | queued | running | ok | failed | skipped
    [ObservableProperty] private string result = "";
    public bool ShowApps => HasApps && Accepted;
    public bool IsOpen => State == "" && IsAction; public bool IsQueued => State == "queued";
    public bool IsRunning => State == "running"; public bool IsOk => State == "ok"; public bool IsFailed => State == "failed"; public bool IsSkipped => State == "skipped";
}

public sealed class GuidedSetupViewModel : ObservableObject
{
    private readonly PreferencesService preferences;
    private readonly PowerProfilesViewModel? power;
    private readonly ToolsViewModel? tools;
    private readonly IntegrationsViewModel? integrations;
    private readonly Action<Exception> log;
    private IReadOnlyList<PlannedChange> profileChanges = [];
    private GuidedPhase phase;
    private string step = "";

    public GuidedSetupViewModel(PreferencesService preferences, ITextCatalog text, Action<Exception> log,
        PowerProfilesViewModel? power = null, ToolsViewModel? tools = null, IntegrationsViewModel? integrations = null)
    {
        this.preferences = preferences; Text = text; this.log = log; this.power = power; this.tools = tools; this.integrations = integrations;
        preferences.PropertyChanged += (_, _) => { OnPropertyChanged(nameof(ShowWizard)); OnPropertyChanged(nameof(ShowReady)); OnPropertyChanged(nameof(ReadySummary)); };
    }

    public ITextCatalog Text { get; }
    public TaskState Progress { get; } = new();
    public ObservableCollection<GuidedQuestion> Questions { get; } = [];
    public IEnumerable<GuidedQuestion> Actions => Questions.Where(q => q.IsAction);
    public GuidedPhase Phase { get => phase; private set { if (SetProperty(ref phase, value)) NotifyPhase(); } }
    public string Step { get => step; private set => SetProperty(ref step, value); }
    public bool IsIntro => Phase == GuidedPhase.Intro; public bool IsAnalyzing => Phase == GuidedPhase.Analyzing;
    public bool IsQuestions => Phase == GuidedPhase.Questions; public bool IsApplying => Phase == GuidedPhase.Applying; public bool IsDone => Phase == GuidedPhase.Done;
    public bool ShowQuestionList => Phase is GuidedPhase.Questions or GuidedPhase.Applying or GuidedPhase.Done;
    public bool NothingToDo => IsQuestions && !Actions.Any();
    public int AcceptedCount => Actions.Count(q => q.Accepted);
    public string ApplyLabel => AcceptedCount == 0 ? Text["GdFinishNoChanges"] : Text.Format("GdApply", AcceptedCount);
    public string Heading => Text[Phase switch { GuidedPhase.Analyzing => "GdAnalyzingTitle", GuidedPhase.Questions => "GdQuestionsTitle", GuidedPhase.Applying => "GdApplyingTitle", GuidedPhase.Done => "GdDoneTitle", _ => "GdIntroTitle" }];
    public string Description => Phase switch
    {
        GuidedPhase.Questions => Text[NothingToDo ? "GdNothingBody" : "GdQuestionsBody"],
        GuidedPhase.Done => Text.Format("GdDoneBody", Actions.Count(q => q.IsOk)),
        GuidedPhase.Analyzing => Text["GdAnalyzingBody"], GuidedPhase.Applying => Text["GdApplyingBody"], _ => Text["GdIntroBody"],
    };

    // Home shows the wizard until it is finished or skipped once, then a short "ready" card.
    public bool ShowWizard => !preferences.Current.GuidedDone || Phase is not GuidedPhase.Intro;
    public bool ShowReady => !ShowWizard;
    public string ReadySummary => preferences.Current.GuidedAt is { } at
        ? Text.Format(preferences.Current.GuidedChanges == 0 ? "GdReadyNone" : "GdReadySummary", preferences.Current.GuidedChanges, at.ToLocalTime().ToString("d", System.Globalization.CultureInfo.GetCultureInfo(Text.Language)))
        : Text["GdReadySkipped"];
    public event EventHandler? Finished;

    public async Task AnalyzeAsync()
    {
        if (Phase is GuidedPhase.Analyzing or GuidedPhase.Applying) return;
        Questions.Clear(); profileChanges = [];
        Phase = GuidedPhase.Analyzing; Progress.Start();
        var found = new List<GuidedQuestion>();
        try
        {
            Step = Text["GdStepPc"];
            if (tools is not null) await Guard(tools.RefreshAsync);
            Progress.Report(15);
            Step = Text["GdStepWindows"];
            if (power is not null) await Guard(async () =>
            {
                await power.RefreshAsync();
                power.SelectProfileCommand.Execute("game");
                profileChanges = [.. (await power.PreviewProfileAsync()).Where(c => !c.AlreadyApplied)];
            });
            Progress.Report(35);
            if (tools?.HasDns == true) { Step = Text["GdStepDns"]; await Guard(tools.BenchmarkDnsAsync); }
            Progress.Report(65);
            if (tools?.CanCheckDrivers == true) { Step = Text["GdStepDrivers"]; await Guard(tools.CheckDriversAsync); }
            Progress.Report(90);
            integrations?.Refresh();
            found.AddRange(BuildQuestions());
        }
        finally
        {
            foreach (var q in found) { q.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(GuidedQuestion.Accepted)) NotifyCounts(); }; Questions.Add(q); }
            Progress.Finish(Text["GdAnalysisDone"]);
            Phase = GuidedPhase.Questions; NotifyCounts();
        }
    }

    // One question per real opportunity, with the recommended answer pre-selected.
    public IReadOnlyList<GuidedQuestion> BuildQuestions()
    {
        var list = new List<GuidedQuestion>();
        if (profileChanges.Count > 0)
            list.Add(new() { Id = "profile", Glyph = "", Title = Text["GdQProfile"], Accepted = true,
                Detail = Text.Format("GdQProfileDetail", string.Join(" · ", profileChanges.Select(c => $"{c.Title}: {c.From} → {c.To}"))) });
        if (tools is { HasTemp: true, TempBytes: >= 50L << 20 })
            list.Add(new() { Id = "temp", Glyph = "", Title = Text.Format("GdQTemp", tools.Size(tools.TempBytes)), Accepted = true,
                Detail = Text.Format("GdQTempDetail", tools.TempItems) });
        if (tools?.Apps.Where(a => a.App.Bytes >= 300L << 20).ToList() is { Count: > 0 } heavy)
        {
            foreach (var a in tools.Apps) a.Selected = false;
            list.Add(new() { Id = "apps", Glyph = "", Title = Text["GdQApps"], Detail = Text["GdQAppsDetail"], Apps = heavy, Accepted = false });
        }
        if (tools?.CanApplyDns == true)
            list.Add(new() { Id = "dns", Glyph = "", Title = Text["GdQDns"], Detail = tools.DnsStatus + " " + Text["GdQDnsDetail"], Accepted = true });
        if (integrations is { RtssInstalled: true, LayoutOn: false })
            list.Add(new() { Id = "overlay", Glyph = "", Title = Text["GdQOverlay"], Detail = Text["GdQOverlayDetail"], Accepted = true });
        if (tools?.GpuUpdateUrl is { } url)
            list.Add(new() { Id = "driver", Glyph = "", Title = Text["GdQDriver"], Detail = tools.DriverUpdateStatus, IsInfo = true, Url = url, LinkLabel = Text["GdOpenDriver"] });
        if (tools?.DiskTip is { Length: > 0 } tip)
            list.Add(new() { Id = "disk", Glyph = "", Title = Text["GdQDisk"], Detail = tip, IsInfo = true });
        return list;
    }

    // Runs the accepted answers one by one; each result is checked, never assumed.
    public async Task ApplyAsync()
    {
        if (Phase != GuidedPhase.Questions) return;
        var chosen = Actions.Where(q => q.Accepted).ToList();
        foreach (var q in Actions.Where(q => !q.Accepted)) { q.State = "skipped"; q.Result = Text["GdSkipped"]; }
        foreach (var q in chosen) q.State = "queued";
        Phase = GuidedPhase.Applying; Progress.Start();
        for (var i = 0; i < chosen.Count; i++)
        {
            var q = chosen[i]; q.State = "running"; Step = q.Title;
            bool ok;
            try { ok = await RunOne(q); }
            catch (Exception ex) { log(ex); ok = false; q.Result = Text["GdFailed"]; }
            q.State = ok ? "ok" : "failed";
            Progress.Report((i + 1) * 100.0 / chosen.Count);
        }
        var applied = chosen.Count(q => q.IsOk);
        if (applied > 0) global::Lotus.Core.Achievements.Activity.Record(global::Lotus.Core.Achievements.Activity.Optimize);
        preferences.Update(preferences.Current with { GuidedDone = true, GuidedAt = DateTimeOffset.Now, GuidedChanges = applied });
        Progress.Finish(Text.Format("GdDoneShort", applied));
        Phase = GuidedPhase.Done;
    }

    private async Task<bool> RunOne(GuidedQuestion q)
    {
        switch (q.Id)
        {
            case "profile" when power is not null:
                await power.ApplyProfileAsync(profileChanges);
                var left = (await power.PreviewProfileAsync()).Count(c => !c.AlreadyApplied); // verify by reading the system again
                q.Result = power.ProfileStatus; return left == 0;
            case "temp" when tools is not null:
                await tools.RecycleTempAsync(); q.Result = tools.TempStatus; return tools.TempTask.Done;
            case "apps" when tools is not null:
                foreach (var a in tools.Apps) a.Selected = q.Apps.Any(x => x.Selected && x.Name == a.Name);
                if (!tools.Apps.Any(a => a.Selected)) { q.Result = Text["GdAppsNone"]; return false; }
                await tools.CloseSelectedAsync(); q.Result = tools.MemoryStatus; return tools.MemTask.Done;
            case "dns" when tools is not null:
                await tools.ApplyDnsAsync(); q.Result = tools.DnsStatus; return tools.DnsStatus == Text["ToolDnsApplied"];
            case "overlay" when integrations is not null:
                await integrations.ActivateLayoutAsync(); q.Result = integrations.Message; return integrations.Message == Text["IntLayoutApplied"];
            default:
                q.Result = Text["GdFailed"]; return false;
        }
    }

    // Leaves the wizard: Home switches to the "ready" card. Skipping also counts as seen.
    public void Close()
    {
        if (!preferences.Current.GuidedDone) preferences.Update(preferences.Current with { GuidedDone = true, GuidedAt = null, GuidedChanges = 0 });
        Questions.Clear(); Phase = GuidedPhase.Intro; NotifyPhase();
        Finished?.Invoke(this, EventArgs.Empty);
    }

    // Settings › "Run the guided setup again".
    public void Restart()
    {
        preferences.Update(preferences.Current with { GuidedDone = false });
        Questions.Clear(); Phase = GuidedPhase.Intro; NotifyPhase();
    }

    private async Task Guard(Func<Task> work) { try { await work(); } catch (Exception ex) { log(ex); } }
    private void NotifyCounts() { foreach (var n in new[] { nameof(AcceptedCount), nameof(ApplyLabel), nameof(NothingToDo), nameof(Description), nameof(Actions) }) OnPropertyChanged(n); }
    private void NotifyPhase()
    {
        foreach (var n in new[] { nameof(IsIntro), nameof(IsAnalyzing), nameof(IsQuestions), nameof(IsApplying), nameof(IsDone), nameof(ShowQuestionList), nameof(Heading), nameof(Description),
                     nameof(ShowWizard), nameof(ShowReady), nameof(ReadySummary), nameof(NothingToDo), nameof(ApplyLabel) }) OnPropertyChanged(n);
    }
}
