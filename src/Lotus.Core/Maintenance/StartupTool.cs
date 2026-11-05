using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lotus.Core.Optimization;

namespace Lotus.Core.Maintenance;

// ---- Inicialização do Windows: what starts with Windows, who made it, whether it is signed; turning off is reversible ----

public enum SignatureState { Valid, Unsigned, Invalid, Unknown }
public sealed record StartupDetails(string? Path, string? Publisher, SignatureState Signature, string? Signer);
public interface IStartupDetails { StartupDetails Inspect(StartupEntry entry); }

public static class StartupCommand
{
    // "C:\Program Files\App\app.exe" --min  →  C:\Program Files\App\app.exe ; C:\x\y.exe /s → C:\x\y.exe ; rundll32 … → rundll32
    public static string? ExecutableOf(string command, Func<string, string>? expand = null)
    {
        var c = (expand ?? Environment.ExpandEnvironmentVariables)(command.Trim());
        if (c.Length == 0) return null;
        if (c[0] == '"') { var end = c.IndexOf('"', 1); return end > 1 ? c[1..end] : null; }
        var exe = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0) return c[..(exe + 4)];
        var space = c.IndexOf(' ');
        return space > 0 ? c[..space] : c;
    }
}

public sealed partial class StartupToolRow(StartupEntry entry, StartupDetails details, string title, string detail, string stateText, string signatureText, string mechanism, bool canToggle, string? toggleLabel) : ObservableObject
{
    public StartupEntry Entry { get; } = entry;
    public StartupDetails Details { get; } = details;
    public string Title { get; } = title;
    public string Detail { get; } = detail;
    public string StateText { get; } = stateText;
    public string SignatureText { get; } = signatureText;
    public string Mechanism { get; } = mechanism;
    public bool CanToggle { get; } = canToggle;
    public string? ToggleLabel { get; } = toggleLabel;
    public bool IsDisabled => Entry.Enabled == false;
    public bool Warning => Details.Signature is SignatureState.Invalid;
}

// toggle: the same journaled action Otimizar uses (exact undo when Lotus turned it off). Returns false when it did not apply.
public sealed partial class StartupToolViewModel(IStartupInventory inventory, IStartupDetails details, Func<StartupEntry, Task<bool>>? toggle, ITextCatalog text, IMaintenanceJournal journal, Action<Exception> log) : ObservableObject
{
    public ITextCatalog Text { get; } = text;
    public ObservableCollection<StartupToolRow> Rows { get; } = [];
    public TaskState Work { get; } = new();
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string summary = "";
    [ObservableProperty] private bool busy;
    public bool CanAct => !Busy;
    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(CanAct));
    public string TileStatus => Summary.Length > 0 ? Summary : Text["SuTileHint"];
    partial void OnSummaryChanged(string value) => OnPropertyChanged(nameof(TileStatus));

    public async Task LoadAsync()
    {
        if (Busy) return;
        Busy = true; Work.Start();
        try { await Fill(); Work.Finish(Text["SuRead"]); }
        catch (Exception ex) { log(ex); Status = Text["ToolFailed"]; Work.Fail(); }
        finally { Busy = false; }
    }

    private async Task<IReadOnlyList<StartupEntry>> Fill()
    {
        var (result, info) = await Task.Run(() => { var r = inventory.Read(); return (r, r.Entries.Select(details.Inspect).ToList()); });
        Rows.Clear();
        var rows = result.Entries.Zip(info).OrderBy(x => x.First.Enabled == false).ThenBy(x => x.First.Name, StringComparer.CurrentCultureIgnoreCase);
        foreach (var (e, d) in rows)
        {
            var canToggle = toggle is not null && e.UserScope && e.Enabled is not null && e.ApprovalKind is not null;
            var publisher = d.Signer ?? d.Publisher ?? Text["SuNoPublisher"];
            var mechanism = Text[e.ApprovalKind == "StartupFolder" ? "SuMechFolder" : e.UserScope ? "SuMechRunUser" : "SuMechRunAll"];
            Rows.Add(new(e, d, e.Name, Text.Format("SuDetail", publisher, d.Path ?? e.Command), Text[e.Enabled switch { true => "PowerStartupEnabled", false => "PowerStartupDisabled", _ => "PowerStartupUnknown" }],
                Text["SuSig" + d.Signature], mechanism + (e.UserScope ? "" : " · " + Text["StartupNeedsAdmin"]), canToggle, canToggle ? Text[e.Enabled == false ? "StartupEnable" : "StartupDisable"] : null));
        }
        var on = result.Entries.Count(e => e.Enabled == true);
        Summary = Text.Format("SuSummary", result.Entries.Count, on, result.Entries.Count(e => e.Enabled == false));
        Status = Summary + (result.Errors.Count > 0 ? " " + Text.Format("SuErrors", result.Errors.Count) : "");
        return result.Entries;
    }

    // Asks first (in the UI); after the change the list is read again and the result is what Windows now says.
    public async Task ToggleAsync(StartupToolRow row)
    {
        if (Busy || !row.CanToggle || toggle is null) return;
        Busy = true; Work.Start();
        var wanted = row.Entry.Enabled == false; // true = turn back on
        try
        {
            var applied = await toggle(row.Entry);
            var now = await Fill();
            var after = now.FirstOrDefault(e => e.ApprovalKind == row.Entry.ApprovalKind && e.ApprovalName == row.Entry.ApprovalName && e.UserScope == row.Entry.UserScope);
            var ok = applied && after?.Enabled == wanted;
            Status = Text.Format(ok ? (wanted ? "StartupEnabledOk" : "StartupDisabledOk") : "StartupConflict", row.Title) + " " + Summary;
            journal.Add(new(DateTimeOffset.Now, "startup", wanted ? "enable" : "disable", row.Entry.Name, ok));
            if (ok) Work.Finish(Text[wanted ? "SuEnabledShort" : "SuDisabledShort"]); else Work.Fail();
        }
        catch (Exception ex) { log(ex); Status = Text["ToolFailed"]; Work.Fail(); }
        finally { Busy = false; }
    }
}

