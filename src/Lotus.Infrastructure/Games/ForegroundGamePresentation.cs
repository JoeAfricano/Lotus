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
// While the game that got the switch is still running, the rate stays (Alt+Tab, a launcher or an error window in front
// never brings 74 Hz back: Gothic, 28/11/2026, the screen flipped every ~2.5 s). It goes back when that process exits.
// For ~10 s after the switch the real rate is checked once a second and asked again if it did not stick (RateGuard).
[SupportedOSPlatform("windows")]
public sealed class ForegroundGamePresentation : IDisposable
{
    private readonly FocusDisplaySession session;
    private readonly IGameDisplayControl display;
    private readonly IRtssIntegration rtss;
    private readonly Action<Exception> log;
    private readonly Action<string> trace;
    private readonly SemaphoreSlim gate = new(1);
    private readonly ForegroundEvents events;
    private GamePresentationRegistration[] registrations = [];
    private readonly FocusTransitionGate transitions=new();
    private readonly RateGuard guard=new();
    private CancellationTokenSource? watch;
    private int apiWaitPid;
    private DateTimeOffset apiWaitStarted;
    private int activePid;
    private volatile bool stopped;
    private string status = "idle";
    public event EventHandler<string>? Changed;
    public string Status => Volatile.Read(ref status);
    public string? GameKey {get;private set;}
    // False when the focused game already runs at the Hz it asks for: a skipped switch then is not worth a warning.
    public bool SwitchNeeded {get;private set;}
    public ForegroundGamePresentation(IGameDisplayControl display, IRtssIntegration rtss, Action<Exception> log, Action<string>? trace = null)
    {
        this.display=display; this.rtss=rtss; this.log=log; this.trace=trace ?? (_ => { });
        session=new(display,new JsonDisplayRecovery(),this.trace);
        events=new(Queue);
        Queue();
    }
    public void SetGames(IEnumerable<GamePresentationRegistration> games)
    {
        Volatile.Write(ref registrations,games.ToArray()); Queue();
    }
    private static bool Alive(int pid)
    {
        if(pid<=0) return false;
        try { using var p=Process.GetProcessById(pid); return !p.HasExited; }
        catch(Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
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
                // The game that got the switch still runs (another window is in front, or it is the same game again):
                // nothing is undone and nothing is checked again; the guard keeps watching the rate.
                if(session.Active && Alive(activePid) && (game is null || pid==activePid)) { Publish("active"); return; }
                if(session.Active && activePid>0 && !Alive(activePid)) { StopWatch(); session.Update(null,null,"the game closed"); activePid=0; }
                GameKey=game?.Key;
                if(game is null) { apiWaitPid=0; StopWatch(); session.Update(null,null); Publish(session.Status); return; }
                var p=game.Prefs; matched=true;
                var modes=display.Modes();
                var hz=PresentationPlan.ResolveHz(p,modes);
                SwitchNeeded=session.Active || display.Current(p.Device) is not {} now || now.Width!=p.Width || now.Height!=p.Height || now.Hz!=hz;
                // The approved profile must still be present; never overwrite the player's changes while playing.
                // Scanline sync has no RivaTuner cap; every other mode must still hold the approved cap.
                // "game" (preset Menor latência): the game's own limiter, RivaTuner keeps no profile limit for it.
                var uncapped=p.AppliedLimiter is "scanline" or PacingPresets.GameLimiter;
                // Closed RivaTuner (not a changed profile): the app opens it and asks to reopen the game.
                if(rtss.Running()==false) { session.Update(null,null); trace($"Modo Console: {game.Key}: RivaTuner is closed; no switch"); Publish("rtss-closed"); return; }
                if(rtss.Running()!=true || (p.AppliedLimiter!=PacingPresets.GameLimiter && rtss.LimiterMode(p.Exe)!=p.AppliedLimiter) || (!uncapped && (rtss.LimiterCap(p.Exe) is not {} cap || Math.Abs(cap-p.Cap)>0.011)))
                { session.Update(null,null); trace($"Modo Console: {game.Key}: RivaTuner profile differs from the prepared one; no switch"); Publish("rtss-changed"); return; }
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
                        else trace($"Modo Console: {game.Key}: RivaTuner did not report the game's 3D API in 5 s; no switch");
                        return;
                    }
                    apiWaitPid=0;
                    if(api is not ("Direct3D 11" or "Direct3D 12"))
                    { session.Update(null,null); trace($"Modo Console: {game.Key}: {api} has no Reflex limiter; no switch"); Publish("api-unsupported"); return; }
                }
                var target=modes.FirstOrDefault(m=>m.Device==p.Device&&m.Width==p.Width&&m.Height==p.Height&&m.Hz==hz);
                if(target is null || Math.Abs(target.Rate-p.PreparedRate)>0.02)
                { session.Update(null,null); trace($"Modo Console: {game.Key}: screen modes changed since it was prepared; prepare again"); Publish("prepare-again"); return; }
                if(!transitions.IsCurrent(ticket) || pid!=ForegroundEvents.ProcessId()) return;
                var wasActive=session.Active;
                var ok=session.Update(game.Key,p);
                if(ok && !wasActive && session.Active)
                    await Task.Delay(300); // let the mode transition settle before interpreting temporary focus loss
                if(ok && display.Current(p.Device) is {} actual && Math.Abs(actual.Rate-p.PreparedRate)>0.02)
                { session.Update(null,null); Publish("prepare-again"); return; }
                if(ok && session.Active) { activePid=pid; StartWatch(pid); }
                Publish(ok?"active":session.Status);
            }
            catch(Exception ex)
            {
                log(ex);
                try { StopWatch(); session.Update(null,null,"error"); } catch(Exception restore) { log(restore); }
                Publish(matched?"failed":session.Status);
            }
            finally { gate.Release(); }
        });
    }

    // One watcher per switch: the rate check for ~10 s, then the wait for the game process to exit (then Queue restores).
    private void StartWatch(int pid)
    {
        StopWatch();
        if(session.Applied is not {} applied) return;
        var cts=watch=new CancellationTokenSource(); var token=cts.Token;
        guard.Start(applied,DateTimeOffset.UtcNow);
        _=Task.Run(async () =>
        {
            try
            {
                while(!token.IsCancellationRequested)
                {
                    await Task.Delay(1000,token);
                    await gate.WaitAsync(token);
                    RateCheck check;
                    try
                    {
                        if(!session.Active || session.Applied is not {} now) return;
                        check=guard.Check(display.Current(now.Device),DateTimeOffset.UtcNow);
                        if(check==RateCheck.Retry && !session.Reassert() && !session.Active) { Publish(session.Status); return; }
                        if(check==RateCheck.GiveUp) { session.Release("contested"); Publish("contested"); return; }
                    }
                    finally { gate.Release(); }
                    if(check==RateCheck.Done) { trace($"Modo Console: rate held for {RateGuard.Window.TotalSeconds:0} s ({guard.Retries} new requests)"); break; }
                }
                using var process=Process.GetProcessById(pid);
                await process.WaitForExitAsync(token);
                if(!token.IsCancellationRequested) Queue();
            }
            catch(OperationCanceledException) { }
            catch(Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { if(!token.IsCancellationRequested) Queue(); } // already gone
            catch(Exception ex) { log(ex); }
        },token);
    }
    private void StopWatch() { var w=Interlocked.Exchange(ref watch,null); w?.Cancel(); guard.Stop(); }
    private void Publish(string value)
    {
        if(Interlocked.Exchange(ref status,value)!=value) Changed?.Invoke(this,value);
    }
    public void Dispose()
    {
        stopped=true; events.Dispose(); StopWatch();
        gate.Wait();
        try { session.Recover(); } catch(Exception ex) { log(ex); }
        finally { gate.Release(); }
    }
}
