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
            presentation=new(games.DisplayControl,games.Rtss,ex=>Serilog.Log.Warning(ex,"Per-game display session"));
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
                if(state is "failed" or "restore-pending" or "rtss-changed" or "api-unsupported" or "prepare-again")
                    ShowWarning(text["GpSession_"+state]);
            });
            Refresh();
            Closed+=(_,_)=>presentation.Dispose();
        }
        catch(Exception ex) { Serilog.Log.Warning(ex,"Foreground display events unavailable"); }
    }
}
