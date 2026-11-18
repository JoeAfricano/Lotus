namespace Lotus.Core.Games;

public sealed partial class GamesViewModel
{
    public IGameDisplayControl? DisplayControl { get; set; }
    public string PresentationStatus { get; private set; } = "";
    public Task<IReadOnlyList<GameDisplayMode>> DisplayModesAsync() => Task.Run(()=>DisplayControl?.Modes()??(IReadOnlyList<GameDisplayMode>)[]);

    // Saving configures only the game's RTSS profile. The monitor is changed later, by foreground events.
    public async Task<bool> SavePresentationAsync(GameEntry game,GamePresentationPrefs prefs,LowLatencyInfo? info,bool adaptiveSync)
    {
        bool Finish(bool ok,string key) { PresentationStatus=Text[key]; return ok; }
        if(processCheck?.FirstRunning([game]) is not null) return Finish(false,"GpCloseFirst");
        if(PlayPrefs is null) return Finish(false,"GpSaveFailed");
        var old=PrefsFor(game).Presentation;
        if(!prefs.Enabled)
        {
            if(old.Enabled && info?.Exe is { } oldExe && Rtss is not null && !await Rtss.DisableLowLatencyAsync(oldExe)) return Finish(false,"GpRtssFailed");
            var disabled=PlayPrefs.Set(game.Key,PrefsFor(game) with {Presentation=prefs});
            return Finish(disabled,disabled?"GpDisabled":"GpSaveFailed");
        }
        if(!PresentationPlan.Valid(prefs) || DisplayControl is null || info is not {State:"ok",Exe:not null} || Rtss is null) return Finish(false,"GpUnavailable");
        var current=await Task.Run(()=>DisplayControl.Current(prefs.Device));
        var modes=await DisplayModesAsync(); var hz=PresentationPlan.ResolveHz(prefs,modes);
        if(current is null || current.Width!=prefs.Width || current.Height!=prefs.Height || hz==0) return Finish(false,"GpUnsupported");
        var mode=PresentationPlan.ResolveLimiter(prefs,info.Nvidia,info.Api,info.UsesFrameGen);
        if(mode=="responsive" && (!info.Nvidia || !info.ReflexFits)) return Finish(false,"GpReflexUnavailable");
        if(info.UsesFrameGen && info.Nvidia && mode!="responsive") return Finish(false,"GpFrameGenConflict");
        var selected=modes.First(m=>m.Device==prefs.Device&&m.Hz==hz&&m.Width==prefs.Width&&m.Height==prefs.Height);
        var rate=current.Hz==hz?current.Rate:selected.Rate;
        var cap=PresentationPlan.Cap(prefs,rate,adaptiveSync);
        if(!double.IsFinite(cap) || cap<20 || cap>1000) return Finish(false,"GpUnsupported");
        var priorMode=Rtss.LimiterMode(info.Exe); var priorCap=Rtss.LimiterCap(info.Exe);
        async Task Undo()
        {
            if(priorMode is not null && priorCap is >0) await Rtss.EnableLimiterAsync(info.Exe,priorCap.Value,priorMode);
            else await Rtss.DisableLowLatencyAsync(info.Exe);
        }
        if(!await Rtss.EnableLimiterAsync(info.Exe,cap,mode)) { await Undo(); return Finish(false,"GpRtssFailed"); }
        // Record the exact plan approved before launch, including the resolved automatic choice.
        var saved=prefs with {Cap=cap,Exe=info.Exe,AppliedLimiter=mode,PreparedRate=rate,RequestedCap=prefs.Cap};
        if(!PlayPrefs.Set(game.Key,PrefsFor(game) with {Presentation=saved,UseFrameGen=info.UsesFrameGen}))
        {
            await Undo();
            return Finish(false,"GpSaveFailed");
        }
        return Finish(true,"GpSaved");
    }
}
