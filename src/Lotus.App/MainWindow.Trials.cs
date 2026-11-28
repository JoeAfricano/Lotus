using Lotus.Core.Games;

namespace Lotus.App;

// Modo Console: each RivaTuner limiter measured once per game during normal play (RivaTuner's counters: no administrator
// prompt, nothing changes), then, with the game closed and Lotus on screen, a notice offers the next one to try or the best
// one. The switch itself needs the player's click (RivaTuner profile, with the usual Windows permission).
public sealed partial class MainWindow
{
    private CancellationTokenSource? trialRun;
    private string? trialTriedFor;
    private DateTimeOffset trialSince;
    private GameEntry? pendingAdvice;

    private void StartLimiterTrial(GameEntry? running)
    {
        if (running is null || DevBesideInstalled()) { trialRun?.Cancel(); trialRun = null; trialTriedFor = null; return; }
        if (trialTriedFor != running.Key) { trialTriedFor = running.Key; trialSince = DateTimeOffset.Now; return; } // once per session, after it settles
        if (trialRun is not null || analysing || !PresentationActiveFor(running.Key) || DateTimeOffset.Now - trialSince < LimiterTrial.SettleFor) return;
        if (games.TrialPending(running) is not { } mode) return;
        var rate = playPrefs.Get(running.Key).Presentation.PreparedRate;
        var run = trialRun = new CancellationTokenSource();
        trialSince = DateTimeOffset.MaxValue; // not again this session
        Serilog.Log.Information("Limiter trial: measuring {Mode} in {Game} for {Seconds} s", mode, running.Name, LimiterTrial.MeasureFor.TotalSeconds);
        _ = Task.Run(async () =>
        {
            var result = await games.MeasureTrialAsync(running, rate, run.Token);
            Serilog.Log.Information("Limiter trial {Mode} in {Game}: {Result}", mode, running.Name,
                result is null ? "not measured" : $"{result.OffBeatPerMinute:0.#} off-beat/min, latency {result.LatencyMs?.ToString("0.#") ?? "n/a"} ms (p95 {result.LatencyP95Ms?.ToString("0.#") ?? "n/a"}), {result.Frames} frames");
        });
    }

    // After the session: offer the next limiter to try, or the best one. Shown when Lotus is on screen (never over a game).
    private void OfferLimiterAdvice(GameEntry game)
    {
        trialRun?.Cancel(); trialRun = null;
        if (games.AdviceAfterSession(game) is null) return;
        pendingAdvice = game;
        ShowPendingAdvice();
    }

    internal string TrialLine(LimiterTrialResult r) => text.Format("LtResultLine", text["LtMode_" + r.Mode], r.OffBeatPerMinute.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo(text.Language)),
        r.LatencyMs is { } l ? text.Format("LtLatency", Math.Round(l), r.LatencyP95Ms is { } p ? Math.Round(p) : Math.Round(l)) : text["LtNoLatency"]);

    // The Neon overlay's latency line (Overlay settings). The exported Neon copy in Lotus's folder is rewritten now (no
    // administrator needed); RivaTuner reads a layout when it starts, so the line shows from the next time it opens.
    public bool OverlayLatency => preferences.Current.OverlayLatency;
    public bool SetOverlayLatency(bool on)
    {
        preferencesViewModel.OverlayLatency = on;
        Lotus.Infrastructure.Integrations.LotusOverlayLayout.LatencyLine = on;
        var neon = Path.Combine(Lotus.Infrastructure.Integrations.LotusOverlayLayout.OverlaysDir, Lotus.Infrastructure.Integrations.LotusOverlayLayout.FileFor(Lotus.Infrastructure.Integrations.LotusOverlayLayout.PremiumNeon));
        try
        {
            if (!File.Exists(neon)) return false;
            Lotus.Infrastructure.Integrations.LotusOverlayLayout.Export(variant: Lotus.Infrastructure.Integrations.LotusOverlayLayout.PremiumNeon);
            Serilog.Log.Information("Neon latency line {State}: layout rewritten", on ? "on" : "off");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Serilog.Log.Warning(ex, "Neon latency line"); return false; }
    }

    private void ShowPendingAdvice()
    {
        if (pendingAdvice is not { } game || !initialized) return;
        var visible = AppWindow.IsVisible && AppWindow.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized };
        if (!visible || QuietMode.Active || lightGame is not null) return;
        pendingAdvice = null;
        if (games.AdviceAfterSession(game) is not { } advice) return;
        var lines = string.Join("\n", advice.Results.Select(TrialLine));
        var title = advice.Kind == "best" ? text.Format("LtBestTitle", text["LtMode_" + advice.Mode], game.Name) : text.Format("LtNextTitle", text["LtMode_" + advice.Mode]);
        var body = (advice.Kind == "best" ? text["LtBestBody"] : text["LtNextBody"]) + "\n" + lines;
        Notice.Show(new(text["LtEyebrow"], title, body, "", null,
        [
            new(text[advice.Kind == "best" ? "LtUseBest" : "LtTryNext"], () =>
            {
                Notice.Hide();
                DispatcherQueue.TryEnqueue(async () =>
                {
                    var ok = await games.UseLimiterAsync(game, advice.Mode);
                    Serilog.Log.Information("Limiter for {Game}: {Mode} {Result}", game.Name, advice.Mode, ok ? "set" : "not set");
                    if (!ok) ShowWarning(text["GpRtssFailed"]);
                });
            }, Primary: true),
            new(text["LtLater"], () => Notice.Hide()),
        ], Seconds: 30));
    }
}
