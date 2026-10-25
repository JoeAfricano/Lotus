using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Lotus.Core.Optimization;

public sealed record OperationChoice(OptimizationPlan Plan, string Label);
public sealed class PowerProfilesViewModel : ObservableObject
{
    private readonly OptimizationEngine engine;
    private readonly IPowerSchemeService power;
    private readonly IProfileStore store;
    private readonly IStartupInventory startup;
    private readonly IGameFeatureInventory? featureSource;
    private IReadOnlyList<GameFeature> features = [];
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
    public bool CanEdit => !busy;
    public bool IsGame => profileId == "game";
    public bool IsDaily => profileId == "daily";
    public bool IsCustom => profileId == "custom";
    public string ProfileName => Text["PowerProfile" + profileId];
    public string SchemeLabel => PowerEnabled ? SelectedScheme?.Name ?? Text["PowerSelect"] : Text["PfRowPowerOff"];
    public string ActiveName => Schemes.FirstOrDefault(s => s.Id == active)?.Name ?? active?.ToString() ?? "—";
    public PowerScheme? SelectedScheme { get => scheme; set { if (SetProperty(ref scheme, value)) InvalidatePlan(); } }
    public bool PowerEnabled { get => enabled; set { if (SetProperty(ref enabled, value)) InvalidatePlan(); } }
    public bool Confirmed { get => confirmed; set { SetProperty(ref confirmed, value); Notify(); } }
    public OperationChoice? SelectedOperation { get => operation; set { SetProperty(ref operation, value); Confirmed = false; Notify(); } }
    public string Status => SelectedOperation is { } o && error.Length == 0 ? Text["PowerStage" + o.Plan.Stage] : Text[messageKey];
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
        IStartupInventory startup, ITextCatalog text, Action<Exception> log, IGameFeatureInventory? features = null)
    {
        this.engine = engine; this.power = power; this.store = store; this.startup = startup; featureSource = features; Text = text; this.log = log;
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
        StartupCommand = new AsyncRelayCommand(() => Run(async () =>
        {
            var result = await Task.Run(startup.Read); StartupEntries.Clear();
            foreach (var e in result.Entries.OrderBy(e => e.Enabled == false).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
                StartupEntries.Add(new(e.Name, e.Location, Text[e.Enabled switch { true => "PowerStartupEnabled", false => "PowerStartupDisabled", _ => "PowerStartupUnknown" }], e.Enabled == false));
            StartupStatus = Text.Format("PowerStartupCountState", result.Entries.Count, result.Entries.Count(e => e.Enabled == true),
                result.Entries.Count(e => e.Enabled == false), result.Entries.Count(e => e.Enabled is null), result.Errors.Count);
            OnPropertyChanged(nameof(StartupStatus));
        }), () => !busy);
    }
    public Task RefreshAsync() => Run(async () =>
    {
        var data = await Task.Run(() => (Schemes: power.List(), Active: power.Active(), Ac: power.OnAcPower(), Profiles: store.LoadProfiles()));
        Schemes.Clear(); foreach (var s in data.Schemes) Schemes.Add(s);
        active = data.Active; ac = data.Ac == true; profiles.Clear();
        foreach (var p in data.Profiles) profiles[p.Id] = p;
        if (featureSource is not null) features = await Task.Run(featureSource.Read);
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
        foreach (var name in new[] { nameof(IsGame), nameof(IsDaily), nameof(IsCustom), nameof(ProfileName) }) OnPropertyChanged(name);
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
    // Rows reflect the selected profile: what it recommends and whether the current state differs.
    private void BuildFeatureRows()
    {
        Features.Clear();
        foreach (var f in features)
        {
            var desired = GameFeatureRules.Desired(f.Id, profileId);
            var advice = f.State == FeatureState.Unavailable ? Text["FeatUnavailableAdvice"]
                : desired is null ? Text["FeatAdvice" + f.Id] : Text[desired == FeatureState.On ? "FeatRecommendOn" : "FeatRecommendOff"] + " " + Text["FeatAdvice" + f.Id];
            Features.Add(new(f.Id, Text["FeatName" + f.Id], Text["FeatState" + f.State], advice, GameFeatureRules.NeedsAttention(f, profileId), f.SettingsUri));
        }
        var attention = Features.Count(r => r.NeedsAttention);
        FeaturesSummary = Features.Count == 0 ? Text["FeatNotRead"] : attention == 0 ? Text["FeatAllGood"] : Text.Format("FeatAttention", attention);
        OnPropertyChanged(nameof(FeaturesSummary));
    }
    private void Notify()
    {
        foreach (var p in new[] { nameof(CanEdit), nameof(Status), nameof(Summary), nameof(Details), nameof(ActiveName), nameof(ProfileName), nameof(SchemeLabel) }) OnPropertyChanged(p);
        SelectProfileCommand?.NotifyCanExecuteChanged(); RefreshCommand?.NotifyCanExecuteChanged(); SaveCommand?.NotifyCanExecuteChanged();
        AnalyzeCommand?.NotifyCanExecuteChanged(); ApplyCommand?.NotifyCanExecuteChanged(); RevertCommand?.NotifyCanExecuteChanged(); StartupCommand?.NotifyCanExecuteChanged();
    }
}
