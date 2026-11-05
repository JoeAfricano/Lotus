using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lotus.Core.Optimization;

namespace Lotus.Core.Maintenance;

// ---- Monitor na taxa certa + Recursos do Windows para jogos ----
// Reads the same inventory as Otimizar and uses its reversible actions (apply/undo are delegated, never reimplemented).
public sealed record FeatureActions(Func<string, Task> Apply, Func<string, Task> Undo, Func<string, bool> HasAction, Func<string, bool> IsApplied, Func<Task> Refresh, Func<string> LastMessage);

public sealed partial class GamingFeatureRow(GameFeature feature, string name, string state, string advice, bool attention, bool canApply, bool canUndo) : ObservableObject
{
    public GameFeature Feature { get; } = feature;
    public string Id => Feature.Id;
    public string Name { get; } = name;
    public string State { get; } = state;
    public string Advice { get; } = advice;
    public bool Attention { get; } = attention;
    public bool CanApply { get; } = canApply;
    public bool CanUndo { get; } = canUndo;
    public string SettingsUri => Feature.SettingsUri;
}

public sealed partial class GamingSetupViewModel(IGameFeatureInventory features, IDisplayModes display, FeatureActions? actions, ITextCatalog text, IMaintenanceJournal journal, Action<Exception> log) : ObservableObject
{
    public const string Profile = "game";
    public ITextCatalog Text { get; } = text;
    public ObservableCollection<GamingFeatureRow> Rows { get; } = [];
    public TaskState Work { get; } = new();
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string monitorText = "";
    [ObservableProperty] private string monitorAdvice = "";
    [ObservableProperty] private bool monitorLow;
    [ObservableProperty] private bool canRaiseMonitor;
    [ObservableProperty] private bool canUndoMonitor;
    [ObservableProperty] private bool busy;
    public DisplayModeState? Monitor { get; private set; }
    public string CurrentHz => Monitor is { } m ? m.CurrentHz.ToString() : "–";
    public string MaxHz => Monitor is { } m ? m.MaxHz.ToString() : "–";
    public string Resolution => Monitor is { } m ? $"{m.Width} × {m.Height}" : "";
    public string RaiseLabel => Monitor is { } m ? Text.Format("MnRaise", m.MaxHz) : "";
    public int Attention => Rows.Count(r => r.Attention);
    public bool CanAct => !Busy;
    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(CanAct));
    public string MonitorTile => MonitorText.Length > 0 ? MonitorText : Text["MnTileHint"];
    public string FeaturesTile => Rows.Count == 0 ? Text["GfTileHint"] : Attention == 0 ? Text["GfAllGood"] : Text.Format("FeatAttention", Attention);
    partial void OnMonitorTextChanged(string value) => OnPropertyChanged(nameof(MonitorTile));

    public async Task LoadAsync()
    {
        if (Busy) return;
        Busy = true; Work.Start();
        try { await Fill(); Work.Finish(Text["SyRead"]); }
        catch (Exception ex) { log(ex); Status = Text["ToolFailed"]; Work.Fail(); }
        finally { Busy = false; }
    }

    private async Task Fill()
    {
        if (actions is not null) await actions.Refresh();
        var (list, mon) = await Task.Run(() => (features.Read(), SafeMonitor()));
        Monitor = mon;
        if (mon is null) { MonitorText = Text["MnUnavailable"]; MonitorAdvice = ""; MonitorLow = false; CanRaiseMonitor = false; }
        else
        {
            MonitorLow = mon.MaxHz - mon.CurrentHz > 1;
            MonitorText = Text.Format(MonitorLow ? "MnLow" : "MnOk", mon.CurrentHz, mon.MaxHz, mon.Width, mon.Height);
            MonitorAdvice = Text[MonitorLow ? "MnLowAdvice" : "MnOkAdvice"];
            CanRaiseMonitor = MonitorLow && actions?.HasAction(DisplayRefreshAction.FeatureId) == true;
        }
        CanUndoMonitor = actions?.IsApplied(DisplayRefreshAction.FeatureId) == true;
        foreach (var n in new[] { nameof(CurrentHz), nameof(MaxHz), nameof(Resolution), nameof(RaiseLabel), nameof(Monitor) }) OnPropertyChanged(n);
        Rows.Clear();
        foreach (var f in list.Where(f => f.Id != DisplayRefreshAction.FeatureId))
        {
            var desired = GameFeatureRules.Desired(f.Id, Profile);
            var advice = f.State == FeatureState.Unavailable ? Text["FeatUnavailableAdvice"]
                : desired is null ? Text["FeatAdvice" + f.Id] : Text[desired == FeatureState.On ? "FeatRecommendOn" : "FeatRecommendOff"] + " " + Text["FeatAdvice" + f.Id];
            var attention = GameFeatureRules.NeedsAttention(f, Profile);
            var has = actions?.HasAction(f.Id) == true; var applied = actions?.IsApplied(f.Id) == true;
            Rows.Add(new(f, Text["FeatName" + f.Id], Text["FeatState" + f.State], advice, attention, has && !applied && attention, has && applied));
        }
        Status = Attention == 0 ? Text["GfAllGood"] : Text.Format("FeatAttention", Attention);
        OnPropertyChanged(nameof(Attention)); OnPropertyChanged(nameof(FeaturesTile));
    }
    private DisplayModeState? SafeMonitor()
    {
        try { return display.Primary(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return null; }
    }

    // Same journaled action as Otimizar; afterwards everything is read again from Windows.
    public async Task ApplyAsync(string id) => await Change(id, undo: false);
    public async Task UndoAsync(string id) => await Change(id, undo: true);
    private async Task Change(string id, bool undo)
    {
        if (Busy || actions is null || !actions.HasAction(id)) return;
        Busy = true; Work.Start();
        try
        {
            await (undo ? actions.Undo(id) : actions.Apply(id));
            await Fill();
            var message = actions.LastMessage();
            Status = message.Length > 0 ? message : Status;
            journal.Add(new(DateTimeOffset.Now, "gaming", (undo ? "undo:" : "apply:") + id, message, true));
            Work.Finish(Text["SyRead"]);
        }
        catch (Exception ex) { log(ex); Status = Text["ToolFailed"]; Work.Fail(); }
        finally { Busy = false; }
    }
}
