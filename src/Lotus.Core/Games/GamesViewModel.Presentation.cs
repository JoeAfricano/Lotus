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

    // "Modo Console" (José, 19/11/2026; 21/11/2026): the graphics for 60 FPS plus everything that keeps 60 steady, like a
    // console. Without FreeSync/G-Sync the monitor runs at 60 Hz while the game is in front (when the output offers 60 Hz
    // at this resolution); RivaTuner caps at 60 itself (José asked for 60, not 59.99) in Fluidez (front edge sync, the
    // console-like pacing), or the automatic mode when the player keeps frame generation on (only Reflex gets along with it).
    // Turning it off brings back the per-game plan that was there before. The graphics choice is kept even when the
    // RivaTuner/Hz part cannot be prepared (RivaTuner missing, game open): the status says why.
    public string Console60Status { get; private set; } = "";
    public async Task<bool> SetConsole60Async(GameEntry game,bool on,LowLatencyInfo? info,bool adaptiveSync)
    {
        Console60Status="";
        if(PlayPrefs is null) return false;
        var prefs=PrefsFor(game);
        if(!on)
        {
            var before=prefs.Before60;
            if(!PlayPrefs.Set(game.Key,prefs with {Prefer60=false,Before60=null})) return false;
            if(before is null) return true;
            var restored=before.Enabled
                ? await SavePresentationAsync(game,before with {Cap=before.RequestedCap},info,adaptiveSync)
                : !prefs.Presentation.Enabled || await SavePresentationAsync(game,prefs.Presentation with {Enabled=false},info,adaptiveSync);
            if(!restored) Console60Status=PresentationStatus;
            return true;
        }
        if(!PlayPrefs.Set(game.Key,prefs with {Prefer60=true,Before60=prefs.Before60??prefs.Presentation})) return false;
        if(DisplayControl is null || profileSource is null) return true;
        var pc=await Task.Run(profileSource.Read); int width=pc.Width,height=pc.Height;
        var modes=await DisplayModesAsync();
        GameDisplayMode? output=null;
        foreach(var device in modes.Select(m=>m.Device).Distinct())
            if(await Task.Run(()=>DisplayControl.Current(device)) is {} c && c.Width==width && c.Height==height) {output=c;break;}
        if(output is null) {Console60Status=Text["GpUnsupported"];return true;}
        var has60=modes.Any(m=>m.SameOutput(output) && m.Hz==60);
        var plan=new GamePresentationPrefs(true,output.Device,output.Width,output.Height,!adaptiveSync && has60 ? 60 : 0,ConsoleLimiter(info),ConsoleCap);
        if(!await SavePresentationAsync(game,plan,info,adaptiveSync)) Console60Status=PresentationStatus;
        return true;
    }
    public const double ConsoleCap=60;
    public static string ConsoleLimiter(LowLatencyInfo? info)=>info is {UsesFrameGen:true} ? "auto" : "fluid";
}
