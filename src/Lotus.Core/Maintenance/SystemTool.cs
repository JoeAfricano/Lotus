using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Maintenance;

// ---- Drivers e Sistema: hardware, devices with problems, Windows errors worth knowing, Windows' own repair tools ----

public sealed record HardwareFact(string Key, string Value);
public sealed record ProblemDevice(string Name, string Class, int Code, string? Driver);
public sealed record SystemEvent(DateTime When, string Source, int EventId, string Level, string Message);
public sealed record SystemSnapshot(IReadOnlyList<HardwareFact> Hardware, IReadOnlyList<ProblemDevice> Devices, IReadOnlyList<SystemEvent> Events);

public interface ISystemInspector
{
    SystemSnapshot Read(TimeSpan eventsSince);
    // "sfc" or "dism": Windows' own repair, in a visible console, after the administrator prompt. Never runs by itself.
    bool StartRepair(string tool);
}

public sealed partial class SystemToolViewModel(ISystemInspector inspector, ITextCatalog text, IMaintenanceJournal journal, Action<Exception> log) : ObservableObject
{
    public ITextCatalog Text { get; } = text;
    public ObservableCollection<Tools.InfoRow> Hardware { get; } = [];
    public ObservableCollection<Tools.InfoRow> Devices { get; } = [];
    public ObservableCollection<Tools.InfoRow> Events { get; } = [];
    public TaskState Work { get; } = new();
    [ObservableProperty] private string status = "";
    [ObservableProperty] private bool busy;
    [ObservableProperty] private bool loaded;
    public bool CanAct => !Busy;
    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(CanAct));
    public bool HasDevices => Devices.Count > 0;
    public bool HasEvents => Events.Count > 0;
    private System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.GetCultureInfo(Text.Language);

    // Device Manager error codes players meet most; the rest show the number.
    public static string CodeKey(int code) => code switch { 1 or 18 or 28 or 31 or 39 => "SyCode" + code, 10 => "SyCode10", 22 => "SyCode22", 43 => "SyCode43", 45 => "SyCode45", _ => "SyCodeOther" };

    public async Task LoadAsync()
    {
        if (Busy) return;
        Busy = true; Work.Start();
        try
        {
            var snap = await Task.Run(() => inspector.Read(TimeSpan.FromDays(7)));
            Hardware.Clear(); Devices.Clear(); Events.Clear();
            foreach (var h in snap.Hardware) Hardware.Add(new(Text["SyHw" + h.Key], h.Value, false));
            foreach (var d in snap.Devices) Devices.Add(new(d.Name, Text.Format(CodeKey(d.Code), d.Code) + (d.Class.Length > 0 ? " · " + d.Class : ""), true));
            foreach (var g in snap.Events.GroupBy(e => (e.Source, e.EventId)).OrderByDescending(g => g.Max(e => e.When)).Take(12))
            {
                var last = g.MaxBy(e => e.When)!;
                Events.Add(new($"{last.Source} ({last.EventId})", Text.Format("SyEventLine", g.Count(), last.When.ToString("g", Culture), Shorten(last.Message)), last.Level == "Critical"));
            }
            Status = Text.Format(Devices.Count == 0 ? "SyAllDevicesOk" : "SyDevicesBad", Devices.Count) + " " + Text.Format(Events.Count == 0 ? "SyNoEvents" : "SyEvents", Events.Count);
            Loaded = true;
            Work.Finish(Text["SyRead"]);
        }
        catch (Exception ex) { log(ex); Status = Text["ToolFailed"]; Work.Fail(); }
        finally { Busy = false; OnPropertyChanged(nameof(HasDevices)); OnPropertyChanged(nameof(HasEvents)); }
    }
    private static string Shorten(string s) { var line = s.Split('\n', 2)[0].Trim(); return line.Length > 160 ? line[..160] + "…" : line; }

    public bool StartRepair(string tool)
    {
        if (tool is not ("sfc" or "dism")) return false;
        try
        {
            var ok = inspector.StartRepair(tool);
            journal.Add(new(DateTimeOffset.Now, "system", tool, "", ok));
            Status = Text[ok ? "SyRepairStarted" : "SyRepairDeclined"];
            return ok;
        }
        catch (Exception ex) { log(ex); Status = Text["ToolFailed"]; return false; }
    }
}
