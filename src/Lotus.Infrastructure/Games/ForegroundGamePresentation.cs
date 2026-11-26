using System.Diagnostics;
using System.Runtime.Versioning;
using Lotus.Core.Games;
using Lotus.Core.Integrations;

namespace Lotus.Infrastructure.Games;

public sealed record GamePresentationRegistration(string Key, string Directory, GamePresentationPrefs Prefs)
{
    public bool Matches(string path) => Prefs.Enabled && !string.IsNullOrWhiteSpace(Directory)
        && string.Equals(Path.GetFileName(path), Prefs.Exe, StringComparison.OrdinalIgnoreCase)
        && Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Directory)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

// One coalesced worker per foreground event. No process-list polling, UAC or profile writes during play.
[SupportedOSPlatform("windows")]
public sealed class ForegroundGamePresentation : IDisposable
{
    private readonly FocusDisplaySession session;
    private readonly IGameDisplayControl display;
    private readonly IRtssIntegration rtss;
    private readonly Action<Exception> log;
    private readonly SemaphoreSlim gate = new(1);
    private readonly ForegroundEvents events;
    private GamePresentationRegistration[] registrations = [];
    private readonly FocusTransitionGate transitions=new();
    private int apiWaitPid;
    private DateTimeOffset apiWaitStarted;
    private volatile bool stopped;
    private string status = "idle";
    public event EventHandler<string>? Changed;
    public string Status => Volatile.Read(ref status);
    public string? GameKey {get;private set;}
    // False when the focused game already runs at the Hz it asks for: a skipped switch then is not worth a warning.
    public bool SwitchNeeded {get;private set;}
    public ForegroundGamePresentation(IGameDisplayControl display, IRtssIntegration rtss, Action<Exception> log)
    {
        this.display=display; this.rtss=rtss; this.log=log;
        session=new(display,new JsonDisplayRecovery());
        events=new(Queue);
        Queue();
    }
    public void SetGames(IEnumerable<GamePresentationRegistration> games)
    {
        Volatile.Write(ref registrations,games.ToArray()); Queue();
    }
    private void Queue()
    {
        var ticket=transitions.Next();
        _=Task.Run(async () =>
        {
            // Alt+Tab, Start and mode changes can emit several transient foreground events.
            // Only the latest stable event may reach the driver, avoiding repeated black-screen handshakes.
            if(!await transitions.WaitAsync(ticket)) return;
            await gate.WaitAsync();
            var matched=false;
            try
            {
                if(stopped || !transitions.IsCurrent(ticket)) return;
                if(!session.Active && !session.Recover()) { Publish(session.Status); return; }
                var pid=ForegroundEvents.ProcessId();
                GamePresentationRegistration? game=null;
                if(pid>0 && registrations.Length>0)
                {
                    // Elevated or protected windows (Task Manager, installers, anti-cheat) deny access to their module:
                    // they are simply not one of the player's games, never a failed Hz switch.
                    string? path=null;
                    try { using var process=Process.GetProcessById(pid); path=process.MainModule?.FileName; }
                    catch(Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException or UnauthorizedAccessException) { path=null; }
                    if(path is not null) game=Volatile.Read(ref registrations).FirstOrDefault(g=>g.Matches(path));
                }
                if(!transitions.IsCurrent(ticket) || pid!=ForegroundEvents.ProcessId()) return;
                GameKey=game?.Key;
                if(game is null) { apiWaitPid=0; session.Update(null,null); Publish(session.Status); return; }
                var p=game.Prefs; matched=true;
                var modes=display.Modes();
                var hz=PresentationPlan.ResolveHz(p,modes);
                SwitchNeeded=session.Active || display.Current(p.Device) is not {} now || now.Width!=p.Width || now.Height!=p.Height || now.Hz!=hz;
                // The approved profile must still be present; never overwrite the player's changes while playing.
                // Scanline sync has no RivaTuner cap; every other mode must still hold the approved cap.
                var uncapped=p.AppliedLimiter=="scanline";
                // Closed RivaTuner (not a changed profile): the app opens it and asks to reopen the game.
                if(rtss.Running()==false) { session.Update(null,null); Publish("rtss-closed"); return; }
                if(rtss.Running()!=true || rtss.LimiterMode(p.Exe)!=p.AppliedLimiter || (!uncapped && (rtss.LimiterCap(p.Exe) is not {} cap || Math.Abs(cap-p.Cap)>0.011)))
                { session.Update(null,null); Publish("rtss-changed"); return; }
                if(p.AppliedLimiter is "responsive" or "fgvsync") // both use RivaTuner's Reflex limiter
                {
                    var api=rtss.GameApi(p.Exe);
                    if(api is null)
                    {
                        session.Update(null,null); Publish("api-unconfirmed");
                        if(apiWaitPid!=pid) {apiWaitPid=pid;apiWaitStarted=DateTimeOffset.UtcNow;}
                        // A bounded wait for RTSS to hook a newly launched game; no process-list polling.
                        if(DateTimeOffset.UtcNow-apiWaitStarted<TimeSpan.FromSeconds(5))
                            _=Task.Delay(300).ContinueWith(_=> {if(!stopped&&transitions.IsCurrent(ticket))Queue();});
                        return;
                    }
                    apiWaitPid=0;
                    if(api is not ("Direct3D 11" or "Direct3D 12"))
                    { session.Update(null,null); Publish("api-unsupported"); return; }
                }
                var target=modes.FirstOrDefault(m=>m.Device==p.Device&&m.Width==p.Width&&m.Height==p.Height&&m.Hz==hz);
                if(target is null || Math.Abs(target.Rate-p.PreparedRate)>0.02)
                { session.Update(null,null); Publish("prepare-again"); return; }
                if(!transitions.IsCurrent(ticket) || pid!=ForegroundEvents.ProcessId()) return;
                var wasActive=session.Active;
                var ok=session.Update(game.Key,p);
                if(ok && !wasActive && session.Active)
                    await Task.Delay(300); // let the mode transition settle before interpreting temporary focus loss
                // A real Alt+Tab still restores; transient focus loss during the driver call must not flip twice.
                if(pid!=ForegroundEvents.ProcessId()) { session.Update(null,null); Publish(session.Status); return; }
                if(ok && display.Current(p.Device) is {} actual && Math.Abs(actual.Rate-p.PreparedRate)>0.02)
                { session.Update(null,null); Publish("prepare-again"); return; }
                Publish(ok?"active":session.Status);
            }
            catch(Exception ex)
            {
                log(ex);
                try { session.Update(null,null); } catch(Exception restore) { log(restore); }
                Publish(matched?"failed":session.Status);
            }
            finally { gate.Release(); }
        });
    }
    private void Publish(string value)
    {
        if(Interlocked.Exchange(ref status,value)!=value) Changed?.Invoke(this,value);
    }
    public void Dispose()
    {
        stopped=true; events.Dispose();
        gate.Wait();
        try { session.Recover(); } catch(Exception ex) { log(ex); }
        finally { gate.Release(); }
    }
}
