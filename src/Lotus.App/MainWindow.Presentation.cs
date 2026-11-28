using Lotus.Core.Games;
using Lotus.Infrastructure.Games;

namespace Lotus.App;

public sealed partial class MainWindow
{
    private ForegroundGamePresentation? presentation;
    public string PresentationState => presentation?.Status ?? "idle";
    public event EventHandler? PresentationChanged;
    public bool PresentationActiveFor(string key)=>presentation is {Status:"active"} && presentation.GameKey==key;
    private void SetUpPresentation()
    {
        games.DisplayControl=new WindowsGameDisplay();
        if(games.Rtss is null) return;
        try
        {
            presentation=new(games.DisplayControl,games.Rtss,ex=>Serilog.Log.Warning(ex,"Per-game display session"),line=>Serilog.Log.Information("{Line}",line));
            void Refresh()
            {
                var registrations=games.InstalledGames().Select(g=>new {Game=g,Prefs=playPrefs.Get(g.Key).Presentation})
                    .Where(x=>x.Prefs is {Enabled:true,Exe.Length:>0})
                    .Select(x=>new GamePresentationRegistration(x.Game.Key,x.Game.InstallDir??Path.GetDirectoryName(x.Game.ExePath??"")??"",x.Prefs));
                presentation.SetGames(registrations);
            }
            playPrefs.Changed+=(_,_)=>DispatcherQueue.TryEnqueue(Refresh);
            games.PropertyChanged+=(_,e)=> { if(e.PropertyName is "IsEmpty" or "Summary") DispatcherQueue.TryEnqueue(Refresh); };
            presentation.Changed+=(_,state)=>DispatcherQueue.TryEnqueue(()=>
            {
                PresentationChanged?.Invoke(this,EventArgs.Empty);
                // Quiet line in the tray icon while Modo Console should be on and is not (never a balloon over the game).
                if(PresentationPlan.ConsoleModeShouldBeOn(state,presentation?.SwitchNeeded==true)) trayStatus.Set("console",text["TrayConsoleOff_"+(state is "contested" or "rtss-closed" ? state : "other")]);
                else if(state is "active" or "already" or "restored" or "idle") trayStatus.Clear("console");
                if(state=="rtss-closed") { _=OnRtssClosedAsync(); return; }
                if(state=="contested") { ShowWarning(text["TrayConsoleOff_contested"]); return; }
                // Only when the Hz really had to change (or the restore is pending): a game already at its Hz stays quiet.
                if(state is "restore-pending" || (state is "failed" or "rtss-changed" or "api-unsupported" or "prepare-again" && presentation?.SwitchNeeded == true))
                    ShowWarning(text["GpSession_"+state]);
            });
            Refresh();
            Closed+=(_,_)=>presentation.Dispose();
            // RivaTuner open from the start, for games launched from Steam or a shortcut (after the library has loaded).
            var startTimer=DispatcherQueue.CreateTimer(); startTimer.Interval=TimeSpan.FromSeconds(8); startTimer.IsRepeating=false;
            startTimer.Tick+=async (_,_)=>await games.EnsureRtssAtStartAsync();
            startTimer.Start();
            // A game that started on its own and needs RivaTuner (overlay or a RivaTuner plan): opened and confirmed now.
            sessionHub.Started+=(_,game)=>DispatcherQueue.TryEnqueue(async ()=>
            {
                var opened=await games.EnsureRtssForDetectedGameAsync(game);
                if(opened==true) trayStatus.Set("rtss",text["RtssOpenedReopen"]);
                if(opened is not null) Serilog.Log.Information("RivaTuner for {Game}: {Result}",game.Name,opened==true?"opened and confirmed":"could not be opened");
            });
        }
        catch(Exception ex) { Serilog.Log.Warning(ex,"Foreground display events unavailable"); }
    }
    private async Task OnRtssClosedAsync()
    {
        if(await games.OpenRtssForRunningGameAsync()) ShowWarning(text["GpSession_rtss-closed"]);
    }
}
