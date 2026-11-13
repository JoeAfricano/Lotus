using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Lotus.Core.Optimization;

public sealed record OperationChoice(OptimizationPlan Plan, string Label);
// One line of the "Apply profile" preview: what changes, from what to what, already planned in the journal.
public sealed record PlannedChange(string Title, string From, string To, bool AlreadyApplied, OptimizationEngine Engine, OptimizationPlan Plan);
public sealed class PowerProfilesViewModel : ObservableObject
{
    private readonly OptimizationEngine engine;
    private readonly IPowerSchemeService power;
    private readonly IProfileStore store;
    private readonly IStartupInventory startup;
    private readonly IGameFeatureInventory? featureSource;
    private IReadOnlyList<GameFeature> features = [];
    // Feature actions keyed by feature id (windowed-optimizations, background-recording, gamemode, display-refresh...).
    private readonly Dictionary<string, OptimizationEngine> featureEngines = new();
    private readonly Dictionary<string, OptimizationPlan?> featureApplied = new();
    private readonly Dictionary<string, IReadOnlyList<OptimizationPlan>> featurePlans = new();
    private readonly IStartupApprovalStore? approval;
    private readonly Func<IOptimizationAction, OptimizationEngine>? engineFactory;
    private readonly Action<Exception> log;
    private readonly Dictionary<string, OptimizationProfile> profiles = new();
    private string profileId = "game", messageKey = "PowerReady", error = "";
    private bool busy, confirmed, enabled = true, ac;
    private Guid? active;
    private PowerScheme? scheme;
    private OperationChoice? operation;
    public ITextCatalog Text { get; }
    public ObservableCollection<PowerScheme> Schemes { get; } = [];
    public ObservableCollection<OperationChoice> Operations { get; } = [];
    public ObservableCollection<StartupRow> StartupEntries { get; } = [];
    public string StartupStatus { get; private set; } = "";
    public ObservableCollection<GameFeatureRow> Features { get; } = [];
    public string FeaturesSummary { get; private set; } = "";
    public string FeatureStatus { get; private set; } = "";
    public string ProfileStatus { get; private set; } = "";
    // Result card shown after applying or undoing (title + detail), and the view animates it on ResultShown.
    public string ResultTitle { get; private set; } = "";
    public bool ResultOk { get; private set; } = true;
    public bool HasResult => ResultTitle.Length > 0 || ProfileStatus.Length > 0;
    public event EventHandler? ResultShown;
    private string ProfileTitle => Text[IsGame ? "PfGameTitle" : IsDaily ? "PfDailyTitle" : "PfCustomTitle"];
    private void ShowResult(string titleKey, bool ok, string detail)
    {
        ResultTitle = Text.Format(titleKey, ProfileTitle); ResultOk = ok; ProfileStatus = detail;
        foreach (var n in new[] { nameof(ResultTitle), nameof(ResultOk), nameof(ProfileStatus), nameof(HasResult) }) OnPropertyChanged(n);
        ResultShown?.Invoke(this, EventArgs.Empty);
    }
    public bool CanUndoProfile => !busy && LastBatch().Count > 0;
    // The latest profile application still in effect: only its plans are undone, never older or individual changes.
    private List<(OptimizationEngine Engine, OptimizationPlan Plan)> LastBatch()
    {
        var all = Operations.Select(o => (Engine: engine, o.Plan))
            .Concat(featurePlans.SelectMany(f => f.Value.Select(p => (Engine: featureEngines[f.Key], Plan: p))))
            .Where(x => x.Plan.Batch is not null && x.Plan.Stage == OperationStage.Applied).ToList();
        if (all.Count == 0) return [];
        var batch = all.MaxBy(x => x.Plan.CreatedUtc).Plan.Batch;
        return all.Where(x => x.Plan.Batch == batch).ToList();
    }
    public bool CanEdit => !busy;
    // Restore tab: what the profiles really changed on this PC (plans that were only previewed stay out).
    public IEnumerable<ActivityRow> Activity => Operations.Where(o => o.Plan.Stage is not (OperationStage.Planned or OperationStage.NoChange))
        .Select(o => new ActivityRow(o.Plan.CreatedUtc.ToLocalTime().ToString("g", System.Globalization.CultureInfo.GetCultureInfo(Text.Language)),
            Text.Format("PfActTitle", Text["PowerProfile" + (o.Plan.ProfileId ?? "custom")]), Text["PfAct" + o.Plan.Stage]));
    public bool HasActivity => Activity.Any();
    public bool NoActivity => !HasActivity;
    public bool IsGame => profileId == "game";
    public bool IsDaily => profileId == "daily";
    public bool IsCustom => profileId == "custom";
    // Text indicator on each card (the selection never depends on color alone).
    public string GameStatus => Text[IsGame ? "PfSelected" : "PfAvailable"];
    public string DailyStatus => Text[IsDaily ? "PfSelected" : "PfAvailable"];
    public string CustomStatus => Text[IsCustom ? "PfSelected" : "PfAvailable"];
    public string ProfileName => Text["PowerProfile" + profileId];
    public string SchemeLabel => PowerEnabled ? SelectedScheme?.Name ?? Text["PowerSelect"] : Text["PfRowPowerOff"];
    public string ActiveName => Schemes.FirstOrDefault(s => s.Id == active)?.Name ?? active?.ToString() ?? "—";
    public PowerScheme? SelectedScheme { get => scheme; set { if (SetProperty(ref scheme, value)) InvalidatePlan(); } }
    public bool PowerEnabled { get => enabled; set { if (SetProperty(ref enabled, value)) InvalidatePlan(); } }
    public bool Confirmed { get => confirmed; set { SetProperty(ref confirmed, value); Notify(); } }
    public OperationChoice? SelectedOperation { get => operation; set { SetProperty(ref operation, value); Confirmed = false; Notify(); } }
    // Idle ("ready") shows nothing: the profile flow no longer has a separate analysis step.
    public string Status => SelectedOperation is { } o && error.Length == 0 ? Text["PowerStage" + o.Plan.Stage] : messageKey == "PowerReady" ? "" : Text[messageKey];
    public string Summary => SelectedOperation is { } o
        ? o.Plan.Stage == OperationStage.Applied
            ? Text.Format("PowerUndoDifference", Name(o.Plan.Desired.Value), Name(o.Plan.Before.Value))
            : Text.Format("PowerDifference", Name(o.Plan.Before.Value), Name(o.Plan.Desired.Value))
        : PowerEnabled ? Text.Format("PowerSelection", ActiveName, SelectedScheme?.Name ?? Text["PowerSelect"])
        : Text["PowerNoActions"];
    public string Details => SelectedOperation is { } o
        ? $"{o.Plan.Id}\n{o.Plan.ActionId}\n{o.Plan.ProfileId}\n{o.Plan.Before.Value} → {o.Plan.Desired.Value}\n{error}"
        : error;
    public IRelayCommand<string> SelectProfileCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand AnalyzeCommand { get; }
    public IAsyncRelayCommand ApplyCommand { get; }
    public IAsyncRelayCommand RevertCommand { get; }
    public IAsyncRelayCommand StartupCommand { get; }
    public PowerProfilesViewModel(OptimizationEngine engine, IPowerSchemeService power, IProfileStore store,
        IStartupInventory startup, ITextCatalog text, Action<Exception> log, IGameFeatureInventory? features = null, OptimizationEngine? windowedEngine = null, IReadOnlyDictionary<string, OptimizationEngine>? featureEngines = null,
        IStartupApprovalStore? approval = null, Func<IOptimizationAction, OptimizationEngine>? engineFactory = null)
    {
        this.engine = engine; this.power = power; this.store = store; this.startup = startup; featureSource = features; Text = text; this.log = log;
        this.approval = approval; this.engineFactory = engineFactory;
        if (windowedEngine is not null) this.featureEngines[WindowedOptimizationsAction.FeatureId] = windowedEngine;
        if (featureEngines is not null) foreach (var (id, e) in featureEngines) this.featureEngines[id] = e;
        SelectProfileCommand = new RelayCommand<string>(SelectProfile, _ => !busy);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !busy);
        SaveCommand = new AsyncRelayCommand(() => Run(async () => { await SaveAsync(); InvalidatePlan(); messageKey = "PowerSaved"; }), () => !busy && (!PowerEnabled || SelectedScheme is not null));
        AnalyzeCommand = new AsyncRelayCommand(() => Run(async () =>
        {
            await SaveAsync(); await engine.RecoverAsync();
            var plan = await engine.AnalyzeAsync(SelectedScheme!.Id.ToString("D"), profileId);
            await LoadOperations(plan.Id);
        }), () => !busy && PowerEnabled && SelectedScheme is not null);
        ApplyCommand = new AsyncRelayCommand(() => Run(async () =>
        {
            var plan = await engine.ApplyAsync(SelectedOperation!.Plan.Id, Confirmed); await LoadOperations(plan.Id);
        }), () => !busy && Confirmed && SelectedOperation?.Plan.Stage == OperationStage.Planned && SelectedOperation.Plan.ProfileId == profileId);
        RevertCommand = new AsyncRelayCommand(() => Run(async () =>
        {
            var plan = await engine.RevertAsync(SelectedOperation!.Plan.Id, Confirmed); await LoadOperations(plan.Id);
        }), () => !busy && Confirmed && SelectedOperation?.Plan.Stage == OperationStage.Applied);
        StartupCommand = new AsyncRelayCommand(() => Run(LoadStartupAsync), () => !busy);
    }
    // Read-only inventory; user-scope items (HKCU) get an optional Disable/Enable action, the rest need admin.
    private async Task LoadStartupAsync()
    {
        var result = await Task.Run(startup.Read); StartupEntries.Clear();
        foreach (var e in result.Entries.OrderBy(e => e.Enabled == false).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var canToggle = approval is not null && engineFactory is not null && e.UserScope && e.Enabled is not null && e.ApprovalKind is not null;
            var location = e.UserScope || e.Enabled is null ? e.Location : e.Location + " · " + Text["StartupNeedsAdmin"];
            StartupEntries.Add(new(e.Name, location, Text[e.Enabled switch { true => "PowerStartupEnabled", false => "PowerStartupDisabled", _ => "PowerStartupUnknown" }],
                e.Enabled == false, e, canToggle, canToggle ? Text[e.Enabled == false ? "StartupEnable" : "StartupDisable"] : null));
        }
        StartupStatus = Text.Format("PowerStartupCountState", result.Entries.Count, result.Entries.Count(e => e.Enabled == true),
            result.Entries.Count(e => e.Enabled == false), result.Entries.Count(e => e.Enabled is null), result.Errors.Count);
        OnPropertyChanged(nameof(StartupStatus));
    }

    // Optional, per item (never part of "Apply profile"). Re-enabling restores the exact bytes when Lotus disabled the item.
    public Task ToggleStartupAsync(StartupRow row) => Run(async () =>
    {
        if (!row.CanToggle || row.Entry is not { ApprovalKind: { } kind, ApprovalName: { } name } entry || approval is null || engineFactory is null)
            throw new InvalidOperationException("Startup item cannot be changed here.");
        var disable = entry.Enabled != false;
        string key;
        var lotusDisable = engineFactory(new StartupToggleAction(approval, kind, name, disable: true));
        // Exact undo of Lotus's own disable first; if the item changed since (e.g. via Task Manager), fall back to a plain enable.
        if (!disable && LastApplied(await lotusDisable.RecoverAsync()) is { } previous
            && (await lotusDisable.RevertAsync(previous.Id, confirmed: true)).Stage == OperationStage.Reverted)
            key = "StartupEnabledOk";
        else
        {
            var toggle = disable ? lotusDisable : engineFactory(new StartupToggleAction(approval, kind, name, disable: false));
            var plan = await toggle.AnalyzeAsync();
            if (plan.Stage == OperationStage.Planned) plan = await toggle.ApplyAsync(plan.Id, confirmed: true);
            key = plan.Stage is OperationStage.Applied or OperationStage.NoChange ? (disable ? "StartupDisabledOk" : "StartupEnabledOk") : "StartupConflict";
        }
        await LoadStartupAsync();
        StartupStatus = Text.Format(key, row.Name) + " " + StartupStatus; OnPropertyChanged(nameof(StartupStatus));
    });

    public Task RefreshAsync() => Run(async () =>
    {
        var data = await Task.Run(() => (Schemes: power.List(), Active: power.Active(), Ac: power.OnAcPower(), Profiles: store.LoadProfiles()));
        Schemes.Clear(); foreach (var s in data.Schemes) Schemes.Add(s);
        active = data.Active; ac = data.Ac == true; profiles.Clear();
        foreach (var p in data.Profiles) profiles[p.Id] = p;
        if (featureSource is not null) features = await Task.Run(featureSource.Read);
        foreach (var (id, featureEngine) in this.featureEngines) await ReadFeature(id, featureEngine);
        scheme = null; enabled = true;
        SelectProfile(profileId); await LoadOperations(null);
    });
    private void SelectProfile(string? id)
    {
        if (id is not ("game" or "daily" or "custom")) return;
        if (scheme is not null || !enabled) profiles[profileId] = new(profileId, scheme?.Id, enabled);
        profileId = id;
        var p = profiles.GetValueOrDefault(id) ?? new(id, ProfileSuggestions.Suggest(id, Schemes, ac));
        scheme = Schemes.FirstOrDefault(s => s.Id == p.PowerSchemeId); enabled = p.PowerEnabled;
        InvalidatePlan(); OnPropertyChanged(nameof(SelectedScheme)); OnPropertyChanged(nameof(PowerEnabled));
        foreach (var name in new[] { nameof(IsGame), nameof(IsDaily), nameof(IsCustom), nameof(ProfileName), nameof(GameStatus), nameof(DailyStatus), nameof(CustomStatus) }) OnPropertyChanged(name);
        // The previous result belonged to the other profile: replace it with a "selected" hint (no animation).
        ResultTitle = ""; ProfileStatus = Text.Format("PfSwitched", ProfileTitle);
        foreach (var n in new[] { nameof(ResultTitle), nameof(ProfileStatus), nameof(HasResult) }) OnPropertyChanged(n);
        // Windows may have changed since the last read (Settings, another app): the summary reflects it now.
        if (featureSource is not null) try { features = featureSource.Read(); } catch (Exception ex) { log(ex); }
        BuildFeatureRows();
    }
    private Task SaveAsync()
    {
        var p = new OptimizationProfile(profileId, SelectedScheme?.Id, PowerEnabled); profiles[profileId] = p;
        return Task.Run(() => store.SaveProfile(p));
    }
    private async Task LoadOperations(Guid? selected)
    {
        var items = await engine.RecoverAsync(); Operations.Clear();
        foreach (var p in items.Take(100)) Operations.Add(new(p, $"{p.CreatedUtc.ToLocalTime():g} · {Text["PowerProfile" + (p.ProfileId ?? "custom")]} · {Text["PowerStage" + p.Stage]}"));
        SelectedOperation = Operations.FirstOrDefault(o => o.Plan.Id == selected);
        active = await Task.Run(power.Active); Notify();
    }
    private string Name(string? value) => Guid.TryParse(value, out var id) ? Schemes.FirstOrDefault(s => s.Id == id)?.Name ?? value! : "—";
    private void InvalidatePlan() { operation = null; confirmed = false; error = ""; messageKey = "PowerReady"; OnPropertyChanged(nameof(SelectedOperation)); OnPropertyChanged(nameof(Confirmed)); Notify(); }
    private async Task Run(Func<Task> task)
    {
        if (busy) return; busy = true; error = ""; Notify();
        try { await task(); }
        catch (Exception ex) { error = ex.Message; messageKey = ex is Win32Exception { NativeErrorCode: 5 } ? "PowerDenied" : "PowerError"; log(ex); }
        finally { busy = false; Confirmed = false; Notify(); }
    }
    public void RefreshLanguage() { BuildFeatureRows(); Notify(); }
    // One-click profile: analyzes every action of the profile; the UI shows the list in one confirmation dialog.
    // A preview that failed (or skipped some actions) must not read as "nothing to change".
    public bool PreviewFailed { get; private set; }
    public int PreviewSkipped { get; private set; }
    public void ReportPreviewFailed() => ShowResult("PfResultPartial", false, Text["PfPreviewFailed"]);
    public async Task<IReadOnlyList<PlannedChange>> PreviewProfileAsync()
    {
        var changes = new List<PlannedChange>();
        PreviewFailed = false; PreviewSkipped = 0; var batch = Guid.NewGuid();
        await Run(async () =>
        {
            await engine.RecoverAsync();
            if (PowerEnabled && SelectedScheme is not null)
            {
                var p = await engine.AnalyzeAsync(SelectedScheme.Id.ToString("D"), profileId, batch);
                changes.Add(new(Text["PfRowPower"], Name(p.Before.Value), Name(p.Desired.Value), p.Stage == OperationStage.NoChange, engine, p));
            }
            // Every feature action the profile recommends (windowed games, background recording, Game Mode, display refresh...).
            foreach (var (id, featureEngine) in featureEngines)
            {
                if (!ProfileWants(id)) continue;
                try
                {
                    await featureEngine.RecoverAsync();
                    var p = await featureEngine.AnalyzeAsync(null, profileId, batch);
                    changes.Add(new(Text["FeatName" + id], FeatureLabel(id, p.Before.Value), FeatureLabel(id, p.Desired.Value), p.Stage == OperationStage.NoChange, featureEngine, p));
                }
                catch (InvalidOperationException ex) { log(ex); PreviewSkipped++; } // Unsupported here or pending recovery: skipped, never forced.
            }
        });
        PreviewFailed = error.Length > 0;
        return changes;
    }
    private bool ProfileWants(string featureId) => GameFeatureRules.Desired(featureId, profileId) is not null;
    // Human labels for stored values: windowed uses its pair string, display shows Hz, DWORD switches show On/Off.
    private string FeatureLabel(string featureId, string? raw) =>
        raw is null or "<absent>" ? Text["FeatStateSystemDefault"]
        : featureId == WindowedOptimizationsAction.FeatureId ? Text[WindowedOptimizationsAction.IsEnabled(raw) ? "FeatStateOn" : "FeatStateOff"]
        : featureId == DisplayRefreshAction.FeatureId ? DisplayRefreshAction.Label(raw)
        : Text[raw == "0" ? "FeatStateOff" : "FeatStateOn"];

    // Applies every planned change (the confirmation came from the dialog), verifying each through its engine.
    public Task ApplyProfileAsync(IReadOnlyList<PlannedChange> changes) => Run(async () =>
    {
        int applied = 0, failed = 0;
        foreach (var c in changes.Where(c => !c.AlreadyApplied))
        {
            // One change that fails (blocked, denied, changed meanwhile) never stops the others nor hides what was done.
            try { if ((await c.Engine.ApplyAsync(c.Plan.Id, confirmed: true)).Stage == OperationStage.Applied) applied++; else failed++; }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException or IOException) { log(ex); failed++; }
        }
        await AfterProfileChange();
        if (failed > 0) ShowResult("PfResultPartial", false, Text.Format("PfAppliedWithConflicts", applied, failed));
        else if (applied == 0) ShowResult("PfResultReady", true, Text["PfPreviewNothing"]);
        else ShowResult("PfResultApplied", true, applied == 1 ? Text["PfApplied1"] : Text.Format("PfApplied", applied));
    });

    // Restores what the latest profile application changed, only where nothing changed it afterwards.
    public Task UndoProfileAsync() => Run(async () =>
    {
        int restored = 0, failed = 0;
        async Task Revert(OptimizationEngine e, Guid id)
        {
            try { if ((await e.RevertAsync(id, confirmed: true)).Stage == OperationStage.Reverted) restored++; else failed++; }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException or IOException) { log(ex); failed++; }
        }
        foreach (var (e, plan) in LastBatch()) await Revert(e, plan.Id);
        await AfterProfileChange();
        if (failed > 0) ShowResult("PfResultUndoPartial", false, Text.Format("PfUndoConflict", restored, failed));
        else if (restored == 0) ShowResult("PfResultNone", true, Text["PfNothingToUndo"]);
        else ShowResult("PfResultUndone", true, restored == 1 ? Text["PfUndone1"] : Text.Format("PfUndone", restored));
    });

    private async Task AfterProfileChange()
    {
        await LoadOperations(null);
        await RefreshFeatureState();
    }
    private async Task RefreshFeatureState()
    {
        foreach (var (id, featureEngine) in featureEngines) await ReadFeature(id, featureEngine);
        if (featureSource is not null) features = await Task.Run(featureSource.Read);
        BuildFeatureRows();
    }

    // For the Tools screen: the same reversible actions and their state (no second implementation).
    public bool HasFeatureAction(string id) => featureEngines.ContainsKey(id);
    public bool IsFeatureApplied(string id) => featureApplied.GetValueOrDefault(id) is not null;
    public async Task RefreshFeaturesAsync()
    {
        foreach (var (id, featureEngine) in featureEngines) await ReadFeature(id, featureEngine);
    }
    public string FeatureMessage => FeatureStatus;

    // Real, reversible feature actions. The UI asks for explicit confirmation before calling these.
    public Task ApplyFeatureAsync(string id, bool confirmed) => FeatureRun(id, async engine =>
    {
        var plan = await engine.AnalyzeAsync(null, profileId);
        if (plan.Stage == OperationStage.Planned) plan = await engine.ApplyAsync(plan.Id, confirmed);
        return plan.Stage switch { OperationStage.Applied => "FeatApplied", OperationStage.NoChange => "FeatNoChange", _ => "FeatConflict" };
    });
    public Task UndoFeatureAsync(string id, bool confirmed) => FeatureRun(id, async engine =>
    {
        if (featureApplied.GetValueOrDefault(id) is not { } applied) return "FeatNothingToUndo";
        var plan = await engine.RevertAsync(applied.Id, confirmed);
        return plan.Stage == OperationStage.Reverted ? "FeatReverted" : "FeatConflict";
    });
    private Task FeatureRun(string id, Func<OptimizationEngine, Task<string>> work) => Run(async () =>
    {
        if (!featureEngines.TryGetValue(id, out var featureEngine)) throw new InvalidOperationException("Unknown feature action.");
        string key;
        try { key = await work(featureEngine); }
        finally { await RefreshFeatureState(); }
        FeatureStatus = Text.Format(key, Text["FeatName" + id]); OnPropertyChanged(nameof(FeatureStatus));
    });
    private async Task ReadFeature(string id, OptimizationEngine featureEngine)
    {
        var plans = await featureEngine.RecoverAsync();
        featurePlans[id] = plans; featureApplied[id] = LastApplied(plans);
    }
    private static OptimizationPlan? LastApplied(IReadOnlyList<OptimizationPlan> plans) =>
        plans.Where(p => p.Stage == OperationStage.Applied).OrderByDescending(p => p.CreatedUtc).FirstOrDefault();
    // Rows reflect the selected profile: what it recommends and whether the current state differs.
    private void BuildFeatureRows()
    {
        Features.Clear();
        foreach (var f in features)
        {
            var desired = GameFeatureRules.Desired(f.Id, profileId);
            var advice = f.State == FeatureState.Unavailable ? Text["FeatUnavailableAdvice"]
                : desired is null ? Text["FeatAdvice" + f.Id] : Text[desired == FeatureState.On ? "FeatRecommendOn" : "FeatRecommendOff"] + " " + Text["FeatAdvice" + f.Id];
            var attention = GameFeatureRules.NeedsAttention(f, profileId);
            var actionable = featureEngines.ContainsKey(f.Id);
            var applied = featureApplied.GetValueOrDefault(f.Id) is not null;
            Features.Add(new(f.Id, Text["FeatName" + f.Id], Text["FeatState" + f.State], advice, attention, f.SettingsUri,
                actionable && !applied && attention, actionable && applied));
        }
        var count = Features.Count(r => r.NeedsAttention);
        FeaturesSummary = Features.Count == 0 ? Text["FeatNotRead"] : count == 0 ? Text["FeatAllGood"] : Text.Format("FeatAttention", count);
        OnPropertyChanged(nameof(FeaturesSummary));
    }
    private void Notify()
    {
        foreach (var p in new[] { nameof(CanEdit), nameof(Status), nameof(Summary), nameof(Details), nameof(ActiveName), nameof(ProfileName), nameof(SchemeLabel), nameof(CanUndoProfile), nameof(Activity), nameof(HasActivity), nameof(NoActivity) }) OnPropertyChanged(p);
        SelectProfileCommand?.NotifyCanExecuteChanged(); RefreshCommand?.NotifyCanExecuteChanged(); SaveCommand?.NotifyCanExecuteChanged();
        AnalyzeCommand?.NotifyCanExecuteChanged(); ApplyCommand?.NotifyCanExecuteChanged(); RevertCommand?.NotifyCanExecuteChanged(); StartupCommand?.NotifyCanExecuteChanged();
    }
}
