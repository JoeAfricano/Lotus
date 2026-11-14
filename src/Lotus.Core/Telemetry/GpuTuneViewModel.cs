using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Telemetry;

// "Ajuste da placa" (Otimizar › Ajuste da placa): the assistant around GpuTuning. Lotus shows the values to type in
// MSI Afterburner, runs a stability test on request (GPU load + sensors + driver-reset check) and says what to try next.
// It never writes clocks, voltages or Afterburner settings.
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
    private CancellationTokenSource? cts;
    public string GpuName => gpuName() ?? Text["UxUnavailable"];
    public bool IsRadeon => GpuTuning.IsRadeon(gpuName());
    public bool CanTest => runTest is not null && Ready.CanStart && !Testing && Goal != TuneGoal.Stock;

    public void Refresh()
    {
        try { Ready = readiness(); } catch (Exception ex) { log?.Invoke(ex); }
        Current = GpuTuning.Start(Goal, gpuName());
        OnPropertyChanged(nameof(GpuName)); OnPropertyChanged(nameof(CanTest));
    }
    partial void OnGoalChanged(TuneGoal value) { Current = GpuTuning.Start(value, gpuName()); LastStable = null; Result = null; OnPropertyChanged(nameof(CanTest)); }
    partial void OnTestingChanged(bool value) { OnPropertyChanged(nameof(CanTest)); OnPropertyChanged(nameof(CanAuto)); }

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
    public Func<GpuSetting, bool>? ApplySetting { get; init; } // keep a result: Afterburner applies it now (save it to Profile 2 for games)
    public System.Collections.ObjectModel.ObservableCollection<AutoStep> AutoSteps { get; } = [];
    [ObservableProperty] private string autoStatus = "";
    [ObservableProperty] private AutoTuneReport? report;
    public bool CanAuto => AutoTune is not null && !Testing && Ready.Nvidia && (AutoAvailable?.Invoke() ?? false);

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
    public bool CanRestoreProfiles => RestoreProfiles is not null && !Saving && (HasProfilesBackup?.Invoke() ?? false);
    public bool HasKept => KeptSetting?.Invoke() is not null;
    public bool TuneOnGames
    {
        get => GetOnGames?.Invoke() ?? false;
        set { if (value != TuneOnGames && (!value || HasKept)) { SetOnGames?.Invoke(value); OnPropertyChanged(); } }
    }
    partial void OnSavingChanged(bool value) { OnPropertyChanged(nameof(CanSave)); OnPropertyChanged(nameof(CanRestoreProfiles)); }

    public bool Keep(GpuSetting setting)
    {
        if (ApplySetting?.Invoke(setting) != true) return false;
        Kept?.Invoke(setting); OnPropertyChanged(nameof(HasKept));
        return true;
    }

    public async Task SaveAsync()
    {
        if (!CanSave || SaveToProfiles is null) return;
        Saving = true; SaveStatus = Text["GtSaveSaving"];
        try
        {
            // After a save the desktop runs Profile 1 (stock): the kept tune is applied first so that one is what gets saved.
            if (KeptSetting?.Invoke() is { } kept && ApplySetting is { } apply) await Task.Run(() => apply(kept));
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
    partial void OnReadyChanged(TuneReadiness value) => OnPropertyChanged(nameof(CanAuto));
    public async Task AutoAsync(AutoModes modes)
    {
        if (!CanAuto || AutoTune is null) return;
        Testing = true; AutoSteps.Clear(); Report = null; AutoStatus = Text["GtAutoStatusbaseline"];
        using var run = cts = new CancellationTokenSource();
        try { Report = await AutoTune(modes, new Progress<AutoStep>(AutoSteps.Add), new Progress<string>(s => AutoStatus = Text["GtAutoStatus" + s]), run.Token); AutoStatus = Text[Report.Stopped is null ? "GtAutoDone" : "GtAutoStopped" + Report.Stopped]; }
        catch (Exception ex) { log?.Invoke(ex); AutoStatus = Text["GtAutoStoppedfailed"]; }
        finally { Testing = false; cts = null; OnPropertyChanged(nameof(CanAuto)); }
    }
}
