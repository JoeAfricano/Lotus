using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Maintenance;

// ---- Check-up do PC: every read-only analysis in one go, summed up by importance; each line opens its tool ----
// No score and no promised gains: only what was measured, with the tool that can act on it.

public enum CheckLevel { Good, Tip, Attention }
public sealed record CheckupFinding(CheckLevel Level, string Title, string Detail, string ToolId)
{
    public string Glyph => Level switch { CheckLevel.Attention => "", CheckLevel.Tip => "", _ => "" };
    public bool IsAttention => Level == CheckLevel.Attention;
    public bool IsTip => Level == CheckLevel.Tip;
    public bool IsGood => Level == CheckLevel.Good;
}

public sealed partial class CheckupViewModel(Tools.ToolsViewModel tools, ITextCatalog text, Action<Exception> log) : ObservableObject
{
    public ITextCatalog Text { get; } = text;
    public ObservableCollection<CheckupFinding> Findings { get; } = [];
    public TaskState Work { get; } = new();
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string headline = "";
    [ObservableProperty] private string step = "";
    [ObservableProperty] private bool busy;
    [ObservableProperty] private bool done;
    [ObservableProperty] private int attention;
    [ObservableProperty] private int tips;
    [ObservableProperty] private int good;
    public DateTimeOffset? LastRun { get; private set; }
    public string AttentionText => Text.Format("CkChipAttention", Attention);
    public string TipsText => Text.Format("CkChipTips", Tips);
    public string GoodText => Text.Format("CkChipGood", Good);
    public string RunLabel => Text[Done ? "CkRunAgain" : "CkRun"];
    partial void OnDoneChanged(bool value) => OnPropertyChanged(nameof(RunLabel));
    public bool CanAct => !Busy;
    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(CanAct));
    public string TileStatus => !Done ? Text["CkTileHint"] : Headline;
    partial void OnHeadlineChanged(string value) => OnPropertyChanged(nameof(TileStatus));
    private System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.GetCultureInfo(Text.Language);
    private string Size(long b) => b >= 1L << 30 ? (b / (double)(1L << 30)).ToString("0.0", Culture) + " GB" : (b / (double)(1L << 20)).ToString("0", Culture) + " MB";

    public async Task RunAsync()
    {
        if (Busy) return;
        Busy = true; Done = false; Work.Start(); Findings.Clear(); Status = "";
        var steps = new List<(string Key, Func<Task> Run)>();
        if (tools.Gaming is { } gm) steps.Add(("CkStepMonitor", gm.LoadAsync));
        if (tools.Crashes is { } cr) steps.Add(("CkStepCrashes", () => cr.LoadAsync()));
        if (tools.SystemTool is { } sy) steps.Add(("CkStepSystem", sy.LoadAsync));
        if (tools.DiskTool is { } dk) steps.Add(("CkStepDisks", dk.LoadAsync));
        if (tools.Upkeep is { } up) steps.Add(("CkStepWindows", up.LoadAsync));
        if (tools.Processes is { } pr) steps.Add(("CkStepProcesses", pr.RefreshAsync));
        if (tools.Overlays is { } ov) steps.Add(("CkStepOverlays", ov.ScanAsync));
        if (tools.Startup is { } st) steps.Add(("CkStepStartup", st.LoadAsync));
        if (tools.Clean is { } cl) steps.Add(("CkStepClean", cl.AnalyzeAsync));
        try
        {
            for (var i = 0; i < steps.Count; i++)
            {
                Step = Text[steps[i].Key];
                try { await steps[i].Run(); } catch (Exception ex) { log(ex); } // one tool failing never stops the check-up
                Work.Report((i + 1) * 100.0 / steps.Count);
            }
            Build();
            LastRun = DateTimeOffset.Now;
            Done = true; Step = "";
            Work.Finish(Text["CkDone"]);
        }
        catch (Exception ex) { log(ex); Status = Text["ToolFailed"]; Work.Fail(); }
        finally { Busy = false; }
    }


    public void Build()
    {
        var list = new List<CheckupFinding>();
        void F(CheckLevel l, string title, string detail, string tool) => list.Add(new(l, title, detail, tool));

        if (tools.Gaming is { Monitor: { } m } g)
            F(g.MonitorLow ? CheckLevel.Attention : CheckLevel.Good, Text.Format(g.MonitorLow ? "CkMonitorLow" : "CkMonitorOk", m.CurrentHz, m.MaxHz), Text[g.MonitorLow ? "CkMonitorLowDetail" : "CkMonitorOkDetail"], "monitor");
        if (tools.Gaming is { Rows.Count: > 0 } gf)
            F(gf.Attention > 0 ? CheckLevel.Tip : CheckLevel.Good, gf.Attention > 0 ? Text.Format("CkFeatures", gf.Attention) : Text["CkFeaturesOk"], Text["CkFeaturesDetail"], "features");
        if (tools.Crashes is { Loaded: true } c)
            F(c.Count > 0 ? CheckLevel.Attention : CheckLevel.Good, c.Count > 0 ? Text.Format("CkCrashes", c.Count) : Text["CkCrashesOk"], c.Count > 0 ? c.Rows[0].Title + ": " + c.Rows[0].CauseTitle : Text["CkCrashesOkDetail"], "crash");
        if (tools.SystemTool is { Loaded: true } s)
        {
            if (s.Devices.Count > 0) F(CheckLevel.Tip, Text.Format("CkDevices", s.Devices.Count), s.Devices[0].Title, "drivers");
            var critical = s.Events.Count(e => e.Warning);
            F(critical > 0 ? CheckLevel.Attention : s.Events.Count > 0 ? CheckLevel.Tip : CheckLevel.Good,
                s.Events.Count > 0 ? Text.Format("CkEvents", s.Events.Count) : Text["CkEventsOk"], s.Events.Count > 0 ? s.Events[0].Title : Text["CkEventsOkDetail"], "drivers");
        }
        if (tools.DiskTool is { Rows.Count: > 0 } d)
        {
            var bad = d.Rows.Where(r => r.Facts.Any(f => f.Warning && !f.Label.StartsWith(Text.Format("DkVolume", "").Trim()))).ToList();
            var low = d.Rows.SelectMany(r => r.Disk.Volumes).Where(v => v.Total > 0 && v.Free * 100.0 / v.Total < 10).ToList();
            F(bad.Count > 0 ? CheckLevel.Attention : CheckLevel.Good, bad.Count > 0 ? Text.Format("CkDisks", bad.Count) : Text["CkDisksOk"], bad.Count > 0 ? bad[0].Title : Text["CkDisksOkDetail"], "health");
            if (low.Count > 0) F(CheckLevel.Attention, Text.Format("CkLowSpace", low[0].Letter, Size(low[0].Free)), Text["CkLowSpaceDetail"], "store");
        }
        if (tools.Upkeep is { Loaded: true, Current: { } w } u)
        {
            if (u.RestartNeeded) F(CheckLevel.Tip, Text["CkRestart"], u.RestartText, "windows");
            else if (w.Uptime >= WindowsUpkeepViewModel.LongUptime) F(CheckLevel.Tip, Text.Format("CkUptime", (int)w.Uptime.TotalDays), Text[w.FastStartup ? "WuUptimeFast" : "WuUptimeLong"], "windows");
            else F(CheckLevel.Good, Text["CkWindowsOk"], u.UptimeText, "windows");
        }
        if (tools.Processes is { } p)
        {
            var heavy = p.Rows.Where(r => r.CanClose && r.Sample.Bytes >= 1L << 30).ToList();
            F(heavy.Count > 0 ? CheckLevel.Tip : CheckLevel.Good, heavy.Count > 0 ? Text.Format("CkHeavy", heavy.Count, Size(heavy.Sum(h => h.Sample.Bytes))) : Text["CkHeavyOk"],
                heavy.Count > 0 ? string.Join(", ", heavy.Take(3).Select(h => h.Name)) : Text["CkHeavyOkDetail"], "proc");
        }
        if (tools.Overlays is { Checked: true } o)
        {
            var attention = o.Rows.Count(r => !r.Id.StartsWith("launcher-") && !r.Id.StartsWith("compat-"));
            F(attention > 0 ? CheckLevel.Tip : CheckLevel.Good, attention > 0 ? Text.Format("CkOverlays", attention) : Text["CkOverlaysOk"], attention > 0 ? o.Rows[0].Name : Text["CkOverlaysOkDetail"], "overlays");
        }
        if (tools.Startup is { Rows.Count: > 0 } st)
        {
            var on = st.Rows.Count(r => r.Entry.Enabled == true);
            F(on >= 10 ? CheckLevel.Tip : CheckLevel.Good, Text.Format(on >= 10 ? "CkStartupMany" : "CkStartupOk", on), Text["CkStartupDetail"], "startup");
        }
        if (tools.Clean is { Analyzed: true } cl)
        {
            var bytes = cl.Rows.Where(r => !r.Blocked).Sum(r => r.Finding.Bytes);
            F(bytes >= 1L << 30 ? CheckLevel.Tip : CheckLevel.Good, bytes >= 1L << 30 ? Text.Format("CkClean", Size(bytes)) : Text["CkCleanOk"], Text["CkCleanDetail"], "clean");
        }

        Findings.Clear();
        foreach (var f in list.OrderByDescending(f => f.Level)) Findings.Add(f);
        Attention = list.Count(f => f.Level == CheckLevel.Attention); Tips = list.Count(f => f.Level == CheckLevel.Tip); Good = list.Count(f => f.Level == CheckLevel.Good);
        Headline = Attention > 0 ? Text.Format(Attention == 1 ? "CkHeadAttentionOne" : "CkHeadAttention", Attention) : Tips > 0 ? Text.Format(Tips == 1 ? "CkHeadTipsOne" : "CkHeadTips", Tips) : Text["CkHeadGood"];
        Status = Text.Format("CkCounts", Attention, Tips, Good);
        foreach (var n in new[] { nameof(AttentionText), nameof(TipsText), nameof(GoodText) }) OnPropertyChanged(n);
    }
}
