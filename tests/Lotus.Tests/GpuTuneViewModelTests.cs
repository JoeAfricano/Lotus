using Lotus.Core;
using Lotus.Core.Telemetry;
using Xunit;

namespace Lotus.Tests;

public sealed class GpuTuneViewModelTests
{
    [Theory] [InlineData(2, GpuTestDepth.Quick)] [InlineData(1, (GpuTestDepth)99)]
    public void Unsupported_saved_calibrations_do_not_unlock_application(int schema,GpuTestDepth depth)
    {
        var saved=new GpuTuneCalibration("gpu-id","RTX 5060",DateTimeOffset.UtcNow,MeasuredReport(),schema,depth);
        var vm=new GpuTuneViewModel(new TextCatalog(),()=>Ready,()=>"RTX 5060")
        { GpuIdentity=()=>"gpu-id",LoadCalibration=()=>saved,AutoAvailable=()=>true,ApplySetting=_=>throw new Exception("must not apply") };
        vm.Refresh(); Assert.Null(vm.Report); Assert.False(vm.CanApplySelected);
    }
    [Fact] public async Task Saved_measurements_restore_without_hardware_writes_and_keep_the_measured_depth()
    {
        GpuTuneCalibration? saved = null;
        var vm = new GpuTuneViewModel(new TextCatalog(),()=>Ready,()=>"RTX 5060")
        { GpuIdentity=()=>"gpu-id", AutoAvailable=()=>true, SaveCalibration=s=>{saved=s;return true;}, AutoTune=(_,_,_,_)=>Task.FromResult(MeasuredReport()) };
        vm.Refresh(); vm.TestDepth=GpuTestDepth.Balanced; await vm.CompareAsync();
        Assert.NotNull(saved); Assert.Equal(GpuTestDepth.Balanced,saved.Depth);
        var gpu="gpu-id"; var writes=0;
        var restarted = new GpuTuneViewModel(new TextCatalog(),()=>Ready,()=>"RTX 5060")
        { GpuIdentity=()=>gpu, AutoAvailable=()=>true, LoadCalibration=()=>saved, ApplySetting=_=>{writes++;return true;} };
        restarted.Refresh(); Assert.True(restarted.CanApplySelected); Assert.Equal(0,writes);
        restarted.TestDepth=GpuTestDepth.Quick; Assert.Equal(GpuTestDepth.Balanced,restarted.MeasuredDepth);
        Assert.Equal(saved.TestedAt,restarted.TestedAt);
        gpu="other-gpu"; restarted.RefreshStatus(); Assert.False(restarted.CanApplySelected);
    }

    [Fact] public async Task Cancelled_repeat_does_not_replace_saved_calibration()
    {
        var saves=0; var report=MeasuredReport();
        var vm=new GpuTuneViewModel(new TextCatalog(),()=>Ready,()=>"RTX 5060")
        { AutoAvailable=()=>true, SaveCalibration=_=>{saves++;return true;}, AutoTune=(_,_,_,_)=>Task.FromResult(report) };
        vm.Refresh(); await vm.CompareAsync(); Assert.Equal(1,saves);
        report=report with { Cancelled=true }; await vm.CompareAsync(); Assert.Equal(1,saves);
        Assert.False(vm.CanApplySelected);
    }
    private static TuneReadiness Ready => new(true, true, true, true, true);
    private static AutoStep Measured(string mode, GpuSetting setting) => new(mode, setting, TuneVerdict.Stable, 1000, 65, 2700, 120);
    private static AutoTuneReport MeasuredReport() => new(Measured("baseline", GpuSetting.Stock), Measured("undervolt", new(0,0,900,2700)), Measured("overclock", new(150,500)), null, [], false);

    [Fact] public async Task Compare_unlocks_measured_cards_and_selection_only_applies_on_command()
    {
        GpuSetting? applied = null; var tested = AutoModes.All;
        var vm = new GpuTuneViewModel(new TextCatalog(), () => Ready, () => "RTX 5060")
        {
            AutoAvailable = () => true, ApplySetting = s => { applied = s; return true; },
            AutoTune = (m,_,_,_) => { tested=m; return Task.FromResult(MeasuredReport()); }
        };
        vm.Refresh(); Assert.False(vm.CanApplySelected); Assert.False(await vm.ApplySelectedAsync()); Assert.Null(applied);
        await vm.CompareAsync(); Assert.Equal(AutoModes.Undervolt | AutoModes.Overclock, tested);
        Assert.Equal(900, vm.SelectedRecommendation!.Setting.FlatMv);
        vm.SelectedModes = AutoModes.Overclock;
        Assert.Equal(TuneGoal.Overclock, vm.Goal); Assert.Equal(150, vm.SelectedRecommendation!.Setting.CoreOffsetMhz);
        Assert.Null(applied); Assert.True(await vm.ApplySelectedAsync());
        Assert.Equal(new GpuSetting(150,500), applied); Assert.Equal(applied, vm.AppliedSetting);
        vm.Goal = TuneGoal.Undervolt; Assert.Equal(AutoModes.Undervolt, vm.SelectedModes);
        Assert.Equal(900, vm.SelectedRecommendation!.Setting.FlatMv); Assert.Equal(new GpuSetting(150,500), applied);
    }

    [Theory] [InlineData(true, null)] [InlineData(false, "hot")] [InlineData(false, "sensors")]
    public async Task Incomplete_reports_never_unlock_application(bool cancelled, string? stopped)
    {
        var vm = new GpuTuneViewModel(new TextCatalog(), () => Ready, () => "RTX 5060")
        { AutoAvailable=()=>true, ApplySetting=_=>throw new Exception("must not apply"), AutoTune=(_,_,_,_)=>Task.FromResult(MeasuredReport() with { Cancelled=cancelled, Stopped=stopped }) };
        vm.Refresh(); await vm.CompareAsync(); Assert.False(vm.CanApplySelected); Assert.Null(vm.Recommendation(TuneGoal.Stock)); Assert.False(await vm.ApplySelectedAsync());
    }

    [Fact] public async Task Failed_apply_and_reset_preserve_the_saved_choice()
    {
        var cleared=false; var enabled=true;
        var vm = new GpuTuneViewModel(new TextCatalog(), () => Ready, () => "RTX 5060")
        { AutoAvailable=()=>true, ApplySetting=_=>false, ClearKept=()=>cleared=true, SetOnGames=v=>enabled=v, AutoTune=(_,_,_,_)=>Task.FromResult(MeasuredReport()) };
        vm.Refresh(); await vm.CompareAsync(); Assert.False(await vm.ApplySelectedAsync()); Assert.Null(vm.AppliedSetting);
        Assert.False(await vm.ResetAsync()); Assert.False(cleared); Assert.True(enabled); Assert.Null(vm.AppliedSetting);
    }

    [Fact] public async Task Reset_confirms_stock_before_disabling_automatic_game_use()
    {
        var enabled=true; var cleared=false; GpuSetting? applied=null;
        var vm = new GpuTuneViewModel(new TextCatalog(), () => Ready, () => "RTX 5060")
        { AutoAvailable=()=>true, ApplySetting=s=>{applied=s;return true;}, ClearKept=()=>cleared=true, SetOnGames=v=>enabled=v, AutoTune=(_,_,_,_)=>Task.FromResult(MeasuredReport()) };
        vm.Refresh(); Assert.True(await vm.ResetAsync()); Assert.Equal(GpuSetting.Stock,applied);
        Assert.False(enabled); Assert.True(cleared); Assert.Equal(TuneGoal.Stock,vm.Goal); Assert.Equal(GpuSetting.Stock,vm.AppliedSetting);
        Assert.True(vm.CanCompare); // The next test must work even with the default card selected.
    }

    [Fact] public async Task New_run_and_gpu_change_invalidate_previous_recommendations()
    {
        var gpu="RTX 5060"; var report=MeasuredReport();
        var vm = new GpuTuneViewModel(new TextCatalog(), () => Ready, () => gpu)
        { AutoAvailable=()=>true, ApplySetting=_=>true, AutoTune=(_,_,_,_)=>Task.FromResult(report) };
        vm.Refresh(); await vm.CompareAsync(); Assert.True(vm.CanApplySelected);
        gpu="RTX 5070"; vm.RefreshStatus(); Assert.False(vm.CanApplySelected);
        gpu="RTX 5060"; report=report with { Baseline=report.Baseline! with { Score=double.NaN } };
        await vm.CompareAsync(); Assert.False(vm.CanApplySelected);
    }

    [Fact] public void Readiness_tracks_connection_changes_without_resetting_the_test_values()
    {
        var connected = false;
        var vm = new GpuTuneViewModel(new TextCatalog(), () => Ready, () => "RTX 5060") { AutoAvailable = () => connected };
        vm.Refresh();
        vm.Current = new(TuneGoal.Undervolt, 2775, 0, 900, 0);
        Assert.Contains(vm.ReadinessItems, x => x.LabelKey == "GtDisconnected" && x.State == TuneCheckState.Blocked);
        var events = new List<string?>(); vm.PropertyChanged += (_, e) => events.Add(e.PropertyName);
        connected = true; vm.RefreshStatus();
        Assert.Contains(nameof(vm.Connected), events);
        Assert.DoesNotContain(vm.ReadinessItems, x => x.State == TuneCheckState.Blocked);
        Assert.Equal(900, vm.Current.VoltageMv);
        events.Clear(); vm.RefreshStatus(); Assert.Empty(events);
        connected = false; vm.RefreshStatus();
        Assert.Equal("GtCheckConnectHint", vm.ReadinessSummaryKey);
    }

    [Fact] public void Optional_settings_never_appear_as_blockers_for_automatic_tuning()
    {
        var vm = new GpuTuneViewModel(new TextCatalog(), () => new(true, true, false, false, false), () => "RTX 5060")
        { AutoAvailable = () => true, AutoTune = (_, _, _, _) => throw new Exception("not executed") };
        vm.Refresh();
        Assert.True(vm.CanAuto);
        Assert.DoesNotContain(vm.ReadinessItems, x => x.State == TuneCheckState.Blocked);
        Assert.Contains(vm.ReadinessItems, x => x.LabelKey == "GtCheckCurve");
        Assert.Equal("GtCheckReadyUv", vm.ReadinessSummaryKey);
        vm.Goal = TuneGoal.Overclock;
        Assert.DoesNotContain(vm.ReadinessItems, x => x.LabelKey == "GtCheckCurve");
        Assert.Equal("GtCheckReadyOc", vm.ReadinessSummaryKey);
        vm.Goal = TuneGoal.Stock;
        Assert.Empty(vm.ReadinessItems); Assert.False(vm.CanAuto);
        Assert.Equal("GtCheckStock", vm.ReadinessSummaryKey);
    }

    [Fact] public void Readiness_rechecks_installation_and_game_configuration()
    {
        var state = new TuneReadiness(true, false, false, false, false);
        var vm = new GpuTuneViewModel(new TextCatalog(), () => state, () => "RTX 5060");
        vm.RefreshStatus(); Assert.Equal("GtFixAfterburner", vm.ReadinessSummaryKey);
        state = state with { Afterburner = true, RivaTuner = true, AutoProfiles = true };
        vm.RefreshStatus();
        Assert.Contains(vm.ReadinessItems, x => x.DetailKey == "GtCheckOverlayReady");
        Assert.Contains(vm.ReadinessItems, x => x.DetailKey == "GtCheckGamesConfigured" && x.State == TuneCheckState.Information);
        Assert.Contains(vm.ReadinessItems, x => x.LabelKey == "GtDisconnected" && x.State == TuneCheckState.Blocked);
    }

    [Fact] public async Task Revisiting_the_panel_does_not_reset_a_running_manual_step()
    {
        var finish = new TaskCompletionSource<(IReadOnlyList<TuneSample>,bool)>();
        var vm = new GpuTuneViewModel(new TextCatalog(),()=>Ready,()=>"RTX 5060",(_,_,_)=>finish.Task);
        vm.Refresh(); var tried=new TuneStep(TuneGoal.Undervolt,2775,0,900,0); vm.Current=tried;
        var test=vm.TestAsync(); vm.Refresh(); Assert.Equal(tried,vm.Current);
        finish.SetResult((Enumerable.Repeat(new TuneSample(65,2775,120),10).ToArray(),false)); await test;
        var next=vm.Current; vm.Refresh(); Assert.Equal(next,vm.Current);
    }

    [Fact] public async Task Failed_application_does_not_save_an_unrelated_profile()
    {
        var saves = 0;
        var vm = new GpuTuneViewModel(new TextCatalog(), () => Ready, () => "RTX 5060")
        {
            AutoAvailable = () => true, ApplySetting = _ => false, KeptSetting = () => new(100, 500),
            SaveToProfiles = () => { saves++; return Task.FromResult(0); }
        };
        await vm.SaveAsync();
        Assert.Equal(0, saves); Assert.False(vm.Saving);
        Assert.Equal(vm.Text["GtSaveFailed"], vm.SaveStatus);
    }

    [Fact] public async Task Saving_excludes_testing_restoring_and_keeping()
    {
        var finish = new TaskCompletionSource<int>();
        var vm = new GpuTuneViewModel(new TextCatalog(), () => Ready, () => "RTX 5060", (_, _, _) => throw new Exception("must not run"))
        {
            AutoAvailable = () => true, AutoTune = (_, _, _, _) => throw new Exception("must not run"),
            SaveToProfiles = () => finish.Task, RestoreProfiles = () => throw new Exception("must not run"), HasProfilesBackup = () => true,
            ApplySetting = _ => throw new Exception("must not apply")
        };
        vm.Refresh(); var saving = vm.SaveAsync();
        Assert.True(vm.Saving); Assert.False(vm.CanAuto); Assert.False(vm.CanTest); Assert.False(vm.CanRestoreProfiles);
        Assert.False(vm.Keep(new(100, 500))); Assert.False(await vm.KeepAsync(new(100, 500)));
        await vm.TestAsync(); await vm.AutoAsync(AutoModes.All); await vm.RestoreProfilesAsync();
        finish.SetResult(0); await saving;
        Assert.True(vm.CanTest); Assert.True(vm.CanAuto); Assert.True(vm.CanRestoreProfiles);
    }

    [Fact] public async Task Failed_async_keep_never_persists_a_candidate()
    {
        var remembered = false;
        var vm = new GpuTuneViewModel(new TextCatalog(), () => Ready, () => "RTX 5060")
        { ApplySetting = _ => false, Kept = _ => remembered = true };
        Assert.False(await vm.KeepAsync(new(100, 500)));
        Assert.False(remembered); Assert.False(vm.Saving);
    }

    [Theory]
    [InlineData(0, "GtSaveOk")] [InlineData(1223, "GtSaveDeclined")] [InlineData(6, "GtSaveRolledBack")] [InlineData(5, "GtSaveFailed")]
    public async Task Saving_to_profiles_says_what_happened(int code, string key)
    {
        var text = new TextCatalog();
        var vm = new GpuTuneViewModel(text, () => Ready, () => "RTX 5060") { AutoAvailable = () => true, SaveToProfiles = () => Task.FromResult(code) };
        Assert.True(vm.CanSave);
        await vm.SaveAsync();
        Assert.Equal(text[key], vm.SaveStatus);
        Assert.False(vm.Saving);
    }

    [Fact] public void The_no_administrator_option_needs_a_kept_tune()
    {
        GpuSetting? kept = null; var on = false;
        var vm = new GpuTuneViewModel(new TextCatalog(), () => Ready, () => "RTX 5060")
        { ApplySetting = _ => true, Kept = s => kept = s, KeptSetting = () => kept, GetOnGames = () => on, SetOnGames = v => on = v };
        vm.TuneOnGames = true;
        Assert.False(on); // nothing kept yet
        Assert.True(vm.Keep(new GpuSetting(0, 0, 925, 2775)));
        vm.TuneOnGames = true;
        Assert.True(on);
        Assert.Equal(925, kept!.FlatMv);
    }

    [Fact] public async Task A_stable_undervolt_moves_one_step_and_keeps_the_last_stable()
    {
        var samples = Enumerable.Range(0, 20).Select(_ => new TuneSample(70, 2700, 120)).ToList();
        var vm = new GpuTuneViewModel(new TextCatalog(), () => Ready, () => "NVIDIA GeForce RTX 5060", (_, _, _) => Task.FromResult<(IReadOnlyList<TuneSample>, bool)>((samples, false)));
        vm.Refresh();
        var first = vm.Current;
        await vm.TestAsync();
        Assert.Equal(TuneVerdict.Stable, vm.Result!.Verdict);
        Assert.Equal(first, vm.LastStable);
        Assert.Equal(first.VoltageMv - 25, vm.Current.VoltageMv); // next try: 25 mV lower at the same clock
        Assert.Contains("70", vm.Verdict);
    }

    [Fact] public async Task A_driver_reset_goes_back_and_stock_never_tests()
    {
        var vm = new GpuTuneViewModel(new TextCatalog(), () => Ready, () => "NVIDIA GeForce RTX 5060", (_, _, _) => Task.FromResult<(IReadOnlyList<TuneSample>, bool)>(([], true)));
        vm.Refresh();
        await vm.TestAsync();
        Assert.Equal(TuneVerdict.Crashed, vm.Result!.Verdict);
        vm.Goal = TuneGoal.Stock;
        Assert.False(vm.CanTest);
        var notReady = new GpuTuneViewModel(new TextCatalog(), () => new(true, false, false, false, false), () => "RTX 5060", (_, _, _) => throw new InvalidOperationException());
        notReady.Refresh();
        Assert.False(notReady.CanTest); // no Afterburner: nothing to test with
    }

    [Fact] public void Overclock_ladder_stops_at_the_family_ceiling()
    {
        var card = GpuTuning.For("NVIDIA GeForce RTX 5060");
        Assert.Equal(450, card.OcCoreMax);
        Assert.Equal(375, GpuTuning.For("NVIDIA GeForce RTX 5060 Ti").OcCoreMax);
        var near = new TuneStep(TuneGoal.Overclock, 400, 900, 0, 0);
        Assert.Equal(450, GpuTuning.Next(near, TuneVerdict.Stable, near, card)!.CoreMhz);          // one more step allowed on a 5060
        var top = new TuneStep(TuneGoal.Overclock, 450, 1000, 0, 0);
        Assert.Equal(GpuTuning.Keep(top), GpuTuning.Next(top, TuneVerdict.Stable, top, card)); // ceiling: keep 70%
    }
}
