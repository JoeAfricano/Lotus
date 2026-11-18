using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Telemetry;

// "Ajuste da placa": automatic and manual workflows share GpuTuning, with exclusive operations and explicit
// application/export actions. Hardware writes belong to the injected MSI Afterburner integration.
public sealed partial class GpuTuneViewModel(ITextCatalog text, Func<TuneReadiness> readiness, Func<string?> gpuName,
    Func<TimeSpan, IProgress<double>, CancellationToken, Task<(IReadOnlyList<TuneSample> Samples, bool DriverReset)>>? runTest = null,
    Action<Exception>? log = null) : ObservableObject
{
    public static readonly TimeSpan TestLength = TimeSpan.FromMinutes(5);
    public ITextCatalog Text { get; } = text;
    [ObservableProperty] private TuneGoal goal = TuneGoal.Undervolt;
    [ObservableProperty] private TuneReadiness ready = new(false, false, false, false, false);
    [ObservableProperty] private TuneStep current = GpuTuning.Start(TuneGoal.Undervolt);
    [ObservableProperty] private TuneStep? lastStable;
    [ObservableProperty] private TuneResult? result;
    [ObservableProperty] private bool testing;
    [ObservableProperty] private double progress;
    [ObservableProperty] private GpuTestDepth testDepth = GpuTestDepth.Quick;
    public GpuTestDepth? MeasuredDepth { get; private set; }
    private CancellationTokenSource? cts;
    public string GpuName => gpuName() ?? Text["UxUnavailable"];
    public bool IsRadeon => GpuTuning.IsRadeon(gpuName());
    public bool CanTest => runTest is not null && Ready.CanStart && !Testing && !Saving && Goal != TuneGoal.Stock;

    private bool? lastConnected;
    private string? restoredIdentity;
    public Func<string?>? GpuIdentity { get; init; }
    public Func<GpuTuneCalibration?>? LoadCalibration { get; init; }
    public Func<GpuTuneCalibration, bool>? SaveCalibration { get; init; }
    public DateTimeOffset? TestedAt { get; private set; }
    private string? Identity => GpuIdentity is { } identity ? identity() : gpuName();
    private void ReadReady() => RefreshStatus();
    // A visible panel may refresh this without resetting the selected test or rebuilding unchanged UI.
    public void RefreshStatus()
    {
        try
        {
            Ready = readiness();
            if (!Testing && !Saving && Identity is { Length: > 0 } id && id != restoredIdentity)
            {
                restoredIdentity = id;
                if (LoadCalibration?.Invoke() is { Schema: 1, Report: { Cancelled: false, Stopped: null, Baseline: { } baseline } } saved
                    && saved.GpuIdentity == id && saved.Report.Steps is { Count: <= 128 } && Enum.IsDefined(saved.Depth) && Usable(baseline))
                {
                    reportGpu = id; Report = saved.Report; TestedAt = saved.TestedAt; MeasuredDepth = saved.Depth;
                    AutoStatus = Text.Format("GtSavedMeasurement", saved.TestedAt.ToLocalTime().ToString("g"));
                    OnPropertyChanged(nameof(TestedAt)); OnPropertyChanged(nameof(CanApplySelected));
                }
            }
            var connected = Connected;
            if (lastConnected != connected)
            {
                lastConnected = connected;
                OnPropertyChanged(nameof(Connected)); OnPropertyChanged(nameof(CanCompare)); OnPropertyChanged(nameof(CanApplySelected)); OnPropertyChanged(nameof(CanReset)); OnPropertyChanged(nameof(CanAuto)); OnPropertyChanged(nameof(CanSave));
                OnPropertyChanged(nameof(ReadinessSummaryKey)); OnPropertyChanged(nameof(ReadinessItems));
            }
        }
        catch (Exception ex) { log?.Invoke(ex); }
    }
    public void Refresh()
    {
        RefreshStatus();
        if (!Testing && Result is null && LastStable is null) Current = GpuTuning.Start(Goal, gpuName());
        OnPropertyChanged(nameof(GpuName)); OnPropertyChanged(nameof(CanTest)); OnPropertyChanged(nameof(CanAuto)); OnPropertyChanged(nameof(Connected));
    }
    partial void OnGoalChanged(TuneGoal value) { OnPropertyChanged(nameof(CanCompare)); OnPropertyChanged(nameof(CanApplySelected)); OnPropertyChanged(nameof(CanReset)); OnPropertyChanged(nameof(SelectedRecommendation)); Current = GpuTuning.Start(value, gpuName()); OnPropertyChanged(nameof(ReadinessItems)); OnPropertyChanged(nameof(ReadinessSummaryKey)); if (value != TuneGoal.Stock) SelectedModes = value == TuneGoal.Overclock ? AutoModes.Overclock : AutoModes.Undervolt; LastStable = null; Result = null; OnPropertyChanged(nameof(CanTest)); OnPropertyChanged(nameof(CanAuto)); }
    partial void OnTestingChanged(bool value) { OnPropertyChanged(nameof(CanCompare)); OnPropertyChanged(nameof(CanApplySelected)); OnPropertyChanged(nameof(CanReset)); OnPropertyChanged(nameof(SelectedRecommendation)); OnPropertyChanged(nameof(ReadinessSummaryKey)); OnPropertyChanged(nameof(CanTest)); OnPropertyChanged(nameof(CanAuto)); OnPropertyChanged(nameof(CanSave)); OnPropertyChanged(nameof(CanRestoreProfiles)); }

    // The values for Afterburner, in plain words.
    public string Values => Current.Goal switch
    {
        TuneGoal.Undervolt => Text.Format("GtValuesUndervolt", Current.VoltageMv, Current.CoreMhz),
        TuneGoal.Overclock => Text.Format("GtValuesOverclock", Current.CoreMhz, Current.MemoryMhz),
        _ => Text["GtValuesStock"],
    };
    partial void OnCurrentChanged(TuneStep value) => OnPropertyChanged(nameof(Values));

    // The verdict and next step in plain pt-BR (or the app language).
    public string Verdict => Result is not { } r ? "" : r.Verdict switch
    {
        TuneVerdict.Stable => Text.Format(r.MaxTempC >= GpuTuning.WarnTempC ? "GtStableWarm" : "GtStable", Math.Round(r.MaxTempC), Math.Round(r.AvgCoreMhz)),
        TuneVerdict.Crashed => Text["GtCrashed"],
        TuneVerdict.TooHot => Text.Format("GtTooHot", Math.Round(r.MaxTempC)),
        TuneVerdict.Throttled => Text.Format("GtThrottled", Math.Round(r.ClockSpreadMhz)),
        _ => Text["GtNoData"],
    };
    partial void OnResultChanged(TuneResult? value) => OnPropertyChanged(nameof(Verdict));

    public async Task TestAsync()
    {
        if (!CanTest || runTest is null) return;
        Testing = true; Progress = 0; Result = null;
        using var run = cts = new CancellationTokenSource();
        try
        {
            var tried = Current;
            var (samples, reset) = await runTest(TestLength, new Progress<double>(p => Progress = p), run.Token);
            var r = GpuTuning.Judge(tried, samples, reset, LastStable, GpuTuning.For(gpuName()));
            if (r.Verdict == TuneVerdict.Stable) LastStable = tried;
            Result = r;
            if (r.Next is { } next) Current = next;
        }
        catch (OperationCanceledException) { Result = null; }
        catch (Exception ex) { log?.Invoke(ex); Result = new(TuneVerdict.NoData, 0, 0, 0, null); }
        finally { Testing = false; cts = null; }
    }
    public void Cancel() => cts?.Cancel();

    // ---- "Ajustar automaticamente": Lotus applies through Afterburner, tests and keeps the card as it was at the end ----
    public Func<AutoModes, IProgress<AutoStep>, IProgress<string>, CancellationToken, Task<AutoTuneReport>>? AutoTune { get; init; }
    public Func<bool>? AutoAvailable { get; init; }
    public Func<Task>? OpenAfterburner { get; init; }
    public bool Connected => AutoAvailable?.Invoke() ?? false;
    public string ReadinessSummaryKey => Testing ? "GtCheckTesting" : Saving ? "GtCheckApplying" : Goal == TuneGoal.Stock ? "GtCheckStock" :
        !Ready.Nvidia ? "GtFixNvidia" : !Ready.Afterburner ? "GtFixAfterburner" : !Connected ? "GtCheckConnectHint" :
        Goal == TuneGoal.Undervolt ? "GtCheckReadyUv" : "GtCheckReadyOc";
    public IReadOnlyList<TuneReadinessItem> ReadinessItems
    {
        get
        {
            if (Goal == TuneGoal.Stock) return [];
            var items = new List<TuneReadinessItem>
            {
                new(Ready.Nvidia ? TuneCheckState.Ready : TuneCheckState.Blocked, "GtReadyNvidia", Ready.Nvidia ? null : "GtFixNvidia"),
                new(Ready.Afterburner ? TuneCheckState.Ready : TuneCheckState.Blocked, "GtReadyAfterburner", Ready.Afterburner ? null : "GtFixAfterburner")
            };
            if (Ready.Afterburner) items.Add(new(Connected ? TuneCheckState.Ready : TuneCheckState.Blocked,
                Connected ? "GtConnected" : "GtDisconnected", Connected ? null : "GtCheckConnectHint"));
            // This saved preference does not prove curve support. The real test validates control/readback.
            if (Goal == TuneGoal.Undervolt) items.Add(new(TuneCheckState.Information, "GtCheckCurve", "GtCheckCurveHint"));
            items.Add(new(TuneCheckState.Information, "GtCheckOverlay", Ready.RivaTuner ? "GtCheckOverlayReady" : "GtCheckOverlayOptional"));
            items.Add(new(TuneCheckState.Information, "GtCheckGames", Ready.AutoProfiles ? "GtCheckGamesConfigured" : "GtCheckGamesLater"));
            return items;
        }
    }
    [ObservableProperty] private AutoModes selectedModes = AutoModes.Undervolt;
    partial void OnSelectedModesChanged(AutoModes value)
    {
        if (value == AutoModes.Undervolt) Goal = TuneGoal.Undervolt;
        else if (value == AutoModes.Overclock) Goal = TuneGoal.Overclock;
    }
    private string? reportGpu;
    [ObservableProperty] private GpuSetting? appliedSetting;
    public Action? ClearKept { get; init; }
    public bool CanCompare => AutoTune is not null && !Testing && !Saving && Ready.Nvidia && Connected;
    public AutoStep? Recommendation(TuneGoal goal)
    {
        if (Report is not { Cancelled: false, Stopped: null, Baseline: { } baseline } r || reportGpu != Identity || !Usable(baseline)) return null;
        var result = goal switch { TuneGoal.Undervolt => r.Undervolt, TuneGoal.Overclock => r.Overclock, _ => r.Baseline };
        return result is not null && Usable(result) ? result : null;
    }
    private static bool Usable(AutoStep s) => s.Ok && double.IsFinite(s.Score) && s.Score > 0
        && double.IsFinite(s.MaxTempC) && s.MaxTempC > 0 && s.MaxTempC <= GpuTuning.StopTempC
        && double.IsFinite(s.AvgCoreMhz) && s.AvgCoreMhz > 0 && double.IsFinite(s.AvgPowerW) && s.AvgPowerW >= 0;
    public AutoStep? SelectedRecommendation => Recommendation(Goal);
    public bool CanApplySelected => !Testing && !Saving && Connected && ApplySetting is not null && SelectedRecommendation is not null;
    public bool CanReset => !Testing && !Saving && Connected && Ready.Nvidia && ApplySetting is not null;
    public Task CompareAsync() => CanCompare ? RunAutoAsync(AutoModes.Undervolt | AutoModes.Overclock) : Task.CompletedTask;
    public async Task<bool> ApplySelectedAsync()
    {
        if (!CanApplySelected || SelectedRecommendation is not { } selected) return false;
        if (!await KeepAsync(selected.Setting)) return false;
        AppliedSetting = selected.Setting;
        return true;
    }
    public async Task<bool> ResetAsync()
    {
        if (!CanReset || ApplySetting is null) return false;
        Saving = true; SaveStatus = Text["GtApplying"];
        try
        {
            if (!await Task.Run(() => ApplySetting(GpuSetting.Stock))) { SaveStatus = Text["GtSaveFailed"]; return false; }
            SetOnGames?.Invoke(false); ClearKept?.Invoke(); AppliedSetting = GpuSetting.Stock; Goal = TuneGoal.Stock;
            OnPropertyChanged(nameof(HasKept)); OnPropertyChanged(nameof(TuneOnGames));
            SaveStatus = Text["GtResetDone"]; ReadReady(); return true;
        }
        catch (Exception ex) { log?.Invoke(ex); SaveStatus = Text["GtSaveFailed"]; return false; }
        finally { Saving = false; }
    }
    public Func<GpuSetting, bool>? ApplySetting { get; init; } // keep a result: Afterburner applies it now (save it to Profile 2 for games)
    public System.Collections.ObjectModel.ObservableCollection<AutoStep> AutoSteps { get; } = [];
    [ObservableProperty] private string autoStatus = "";
    [ObservableProperty] private AutoTuneReport? report;
    public bool CanAuto => AutoTune is not null && !Testing && !Saving && Goal != TuneGoal.Stock && Ready.Nvidia && (AutoAvailable?.Invoke() ?? false);

    // ---- "Salvar para todos os jogos": the tune in use goes to Afterburner's Profile 2 (one UAC), or, without
    // administrator, Lotus applies the kept tune itself while a game is open ----
    public Func<Task<int>>? SaveToProfiles { get; init; }   // 0 ok, 1223 UAC declined, 6 rolled back, other = not possible
    public Func<Task<int>>? RestoreProfiles { get; init; }
    public Func<bool>? HasProfilesBackup { get; init; }
    public Action<GpuSetting>? Kept { get; init; }          // remembered for the no-administrator option
    public Func<GpuSetting?>? KeptSetting { get; init; }
    public Func<bool>? GetOnGames { get; init; }
    public Action<bool>? SetOnGames { get; init; }
    [ObservableProperty] private string saveStatus = "";
    [ObservableProperty] private bool saving;
    public bool CanSave => SaveToProfiles is not null && !Testing && !Saving && (AutoAvailable?.Invoke() ?? false);
    public bool CanRestoreProfiles => RestoreProfiles is not null && !Testing && !Saving && (HasProfilesBackup?.Invoke() ?? false);
    public bool HasKept => KeptSetting?.Invoke() is not null;
    public bool TuneOnGames
    {
        get => GetOnGames?.Invoke() ?? false;
        set { if (value != TuneOnGames && (!value || HasKept)) { SetOnGames?.Invoke(value); OnPropertyChanged(); ReadReady(); } }
    }
    partial void OnSavingChanged(bool value) { OnPropertyChanged(nameof(CanCompare)); OnPropertyChanged(nameof(CanApplySelected)); OnPropertyChanged(nameof(CanReset)); OnPropertyChanged(nameof(SelectedRecommendation)); OnPropertyChanged(nameof(ReadinessSummaryKey)); OnPropertyChanged(nameof(CanSave)); OnPropertyChanged(nameof(CanRestoreProfiles)); OnPropertyChanged(nameof(CanAuto)); OnPropertyChanged(nameof(CanTest)); }

    public bool Keep(GpuSetting setting)
    {
        if (Testing || Saving) return false;
        if (ApplySetting?.Invoke(setting) != true) return false;
        Kept?.Invoke(setting); OnPropertyChanged(nameof(HasKept)); ReadReady();
        return true;
    }

    public async Task<bool> KeepAsync(GpuSetting setting)
    {
        if (Testing || Saving || ApplySetting is null) return false;
        Saving = true; SaveStatus = Text["GtApplying"];
        try
        {
            if (!await Task.Run(() => ApplySetting(setting))) { SaveStatus = Text["GtSaveFailed"]; return false; }
            Kept?.Invoke(setting); OnPropertyChanged(nameof(HasKept)); ReadReady();
            SaveStatus = Text["GtApplied"];
            return true;
        }
        catch (Exception ex) { log?.Invoke(ex); SaveStatus = Text["GtSaveFailed"]; return false; }
        finally { Saving = false; }
    }

    public async Task SaveAsync()
    {
        if (!CanSave || SaveToProfiles is null) return;
        Saving = true; SaveStatus = Text["GtSaveSaving"];
        try
        {
            // After a save the desktop runs Profile 1 (stock): the kept tune is applied first so that one is what gets saved.
            if (KeptSetting?.Invoke() is { } kept && ApplySetting is { } apply && !await Task.Run(() => apply(kept)))
            {
                SaveStatus = Text["GtSaveFailed"];
                return;
            }
            var code = await SaveToProfiles();
            SaveStatus = Text[code switch { 0 => "GtSaveOk", 1223 => "GtSaveDeclined", 6 => "GtSaveRolledBack", _ => "GtSaveFailed" }];
            if (code == 0) Refresh();
        }
        catch (Exception ex) { log?.Invoke(ex); SaveStatus = Text["GtSaveFailed"]; }
        finally { Saving = false; }
    }

    public async Task RestoreProfilesAsync()
    {
        if (!CanRestoreProfiles || RestoreProfiles is null) return;
        Saving = true;
        try { var code = await RestoreProfiles(); SaveStatus = Text[code switch { 0 => "GtSaveRestored", 1223 => "GtSaveDeclined", _ => "GtSaveFailed" }]; if (code == 0) Refresh(); }
        catch (Exception ex) { log?.Invoke(ex); SaveStatus = Text["GtSaveFailed"]; }
        finally { Saving = false; }
    }
    partial void OnReadyChanged(TuneReadiness value) { OnPropertyChanged(nameof(CanCompare)); OnPropertyChanged(nameof(CanApplySelected)); OnPropertyChanged(nameof(CanReset)); OnPropertyChanged(nameof(SelectedRecommendation)); OnPropertyChanged(nameof(CanAuto)); OnPropertyChanged(nameof(CanTest)); OnPropertyChanged(nameof(ReadinessItems)); OnPropertyChanged(nameof(ReadinessSummaryKey)); }
    public Task AutoAsync(AutoModes modes) => CanAuto ? RunAutoAsync(modes) : Task.CompletedTask;
    private async Task RunAutoAsync(AutoModes modes)
    {
        if (!CanCompare || AutoTune is null) return;
        Testing = true; Progress = 0; AutoSteps.Clear(); Report = null; reportGpu = null; AppliedSetting = null; SaveStatus = ""; AutoStatus = Text["GtAutoStatusbaseline"];
        using var run = cts = new CancellationTokenSource();
        try
        {
            var testedGpu = Identity;
            var testedDepth = TestDepth;
            Report = await AutoTune(modes,
                new Progress<AutoStep>(s => { if (ReferenceEquals(cts,run)) AutoSteps.Add(s); }),
                new Progress<string>(s => { if (ReferenceEquals(cts,run)) AutoStatus = Text["GtAutoStatus" + s]; }), run.Token);
            reportGpu = testedGpu;
            AutoStatus = Text[Report.Cancelled ? "GtAutoCancelled" : Report.Stopped is null ? "GtAutoDone" : "GtAutoStopped" + Report.Stopped];
            if (testedGpu is not null && testedGpu == Identity && Report is { Cancelled: false, Stopped: null, Baseline: { } baseline } && Usable(baseline))
            {
                TestedAt = DateTimeOffset.UtcNow;
                MeasuredDepth = testedDepth;
                var saved = new GpuTuneCalibration(testedGpu,GpuName,TestedAt.Value,Report with { Steps = Report.Steps.Take(128).ToArray() }, Depth: testedDepth);
                if (SaveCalibration is { } persist && !persist(saved)) SaveStatus = Text["GtMeasurementSaveFailed"];
                OnPropertyChanged(nameof(TestedAt));
            }
            OnPropertyChanged(nameof(SelectedRecommendation));
        }
        catch (Exception ex) { log?.Invoke(ex); AutoStatus = Text["GtAutoStoppedfailed"]; }
        finally { Testing = false; cts = null; OnPropertyChanged(nameof(CanAuto)); }
    }
}
