using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Lotus.Core.Games;

namespace Lotus.Core.Saves;

// One backup in the "Gerenciar backups" list.
public sealed record SaveBackupItem(string Id, string Title, string Detail, bool Valid, bool CanRestore);

// "Proteção de progresso" on a game's page. All disk work runs off the UI thread; the page only shows results.
public sealed class GameSavesViewModel(SaveProtection protection, GameEntry game, ITextCatalog text, Func<GameEntry, bool> gameRunning) : ObservableObject
{
    public ITextCatalog Text { get; } = text;
    public GameEntry Game { get; } = game;
    public ObservableCollection<SaveBackupItem> Backups { get; } = [];
    private CultureInfo Culture => CultureInfo.GetCultureInfo(Text.Language);

    private ProtectionState state = ProtectionState.NotFound;
    private SaveCandidate? found;
    private GameSaveSettings settings = new();
    private bool busy, loaded;
    private string message = "", folderInfo = "";

    public ProtectionState State { get => state; private set { if (SetProperty(ref state, value)) Notify(); } }
    public bool Busy { get => busy; private set { if (SetProperty(ref busy, value)) Notify(); } }
    public bool Loaded { get => loaded; private set => SetProperty(ref loaded, value); }
    public string Message { get => message; private set { if (SetProperty(ref message, value)) OnPropertyChanged(nameof(HasMessage)); } }
    public bool HasMessage => Message.Length > 0;
    public SaveCandidate? Found => found;

    public bool IsOn => settings.Enabled;
    public bool Auto => settings.Auto;
    public int Keep => settings.Keep;
    public bool Supported => State != ProtectionState.Unsupported;
    public bool CanEnable => !Busy && !IsOn && FolderPath.Length > 0 && State is not (ProtectionState.Unsupported or ProtectionState.Attention);
    public bool CanBackup => !Busy && IsOn && State is ProtectionState.Active or ProtectionState.Attention && !Interrupted;
    public bool Interrupted { get; private set; }
    public bool CloudSynced => (IsOn || settings.Root.Length > 0 ? settings.CloudSynced : found?.CloudSynced) == true;
    public string FolderPath => settings.Root.Length > 0 ? settings.Root : found?.Root ?? "";
    public string FolderInfo { get => folderInfo; private set => SetProperty(ref folderInfo, value); }
    public string Where => Text["SpWhere"];

    public string Status => State switch
    {
        ProtectionState.Active => Text["SpStateActive"],
        ProtectionState.Attention => Interrupted ? Text["SpStateInterrupted"] : Text["SpStateAttention"],
        ProtectionState.Unavailable => Text["SpStateUnavailable"],
        ProtectionState.Off => Text["SpStateOff"],
        ProtectionState.Available => Text["SpStateAvailable"],
        ProtectionState.Unsupported => Text["SpStateUnsupported"],
        _ => Text["SpStateNotFound"],
    };
    public string Summary => !Loaded ? Text["SpChecking"] : Status;

    private void Notify()
    {
        foreach (var n in new[] { nameof(Status), nameof(Summary), nameof(CanEnable), nameof(CanBackup), nameof(IsOn), nameof(Auto), nameof(Keep), nameof(Supported), nameof(CloudSynced), nameof(FolderPath), nameof(Interrupted) })
            OnPropertyChanged(n);
    }

    // Reads everything off the UI thread and applies it in one go.
    public async Task LoadAsync()
    {
        var snapshot = await Task.Run(() =>
        {
            var s = protection.Get(Game.Key);
            var find = protection.Discovery.Find(Game);
            var first = find.Candidates.FirstOrDefault();
            var st = protection.State(Game, find);
            var interrupted = protection.Vault.RestoreInterrupted(Game.Key);
            var list = protection.Vault.List(Game.Key);
            var live = s.Root.Length > 0 ? protection.Discovery.Manual(s.Root) : first;
            return (s, first, st, interrupted, list, live);
        });
        settings = snapshot.s; found = snapshot.first; Interrupted = snapshot.interrupted;
        FolderInfo = snapshot.live is { } c ? Text.Format("SpFolderInfo", c.Files, Size(c.Bytes), c.LastWriteUtc?.ToLocalTime().ToString("g", Culture) ?? "—") : "";
        Backups.Clear();
        foreach (var m in snapshot.list) Backups.Add(Item(m));
        Loaded = true;
        State = snapshot.st;
        Notify();
    }

    private SaveBackupItem Item(SnapshotManifest m)
    {
        var kind = m.Origin switch { SnapshotManifest.Manual => Text["SpKindManual"], SnapshotManifest.BeforeRestore => Text["SpKindBefore"], _ => Text["SpKindAuto"] };
        var title = m.Created.ToLocalTime().ToString("g", Culture);
        var detail = $"{kind} · {Text.Format("SpFiles", m.Files.Count)} · {Size(m.TotalBytes)} · {(m.IsValid ? Text["SpValid"] : Text["SpInvalid"])}";
        return new(m.Id, title, detail, m.IsValid, m.IsValid && settings.Root.Length > 0);
    }

    public string Size(long bytes) => bytes >= 1L << 30 ? (bytes / (double)(1L << 30)).ToString("0.0", Culture) + " GB"
        : bytes >= 1L << 20 ? (bytes / (double)(1L << 20)).ToString("0.0", Culture) + " MB"
        : Math.Max(1, bytes / 1024).ToString(Culture) + " KB";

    public string FailureText(SaveFailure f) => Text[f switch
    {
        SaveFailure.NoSpace => "SpFailNoSpace",
        SaveFailure.Locked or SaveFailure.Changing => "SpFailBusy",
        SaveFailure.SourceMissing or SaveFailure.Empty => "SpFailMissing",
        SaveFailure.GameRunning => "SpFailRunning",
        SaveFailure.InvalidSnapshot or SaveFailure.VerifyFailed or SaveFailure.UnsafePath => "SpFailInvalid",
        SaveFailure.RolledBack => "SpFailRolledBack",
        _ => "SpFailGeneric",
    }];

    private async Task Run(Func<Task<string>> work)
    {
        if (Busy) return;
        Busy = true; Message = Text["SpWorking"];
        try { Message = await work(); }
        catch (Exception) { Message = Text["SpFailGeneric"]; }
        finally { Busy = false; await LoadAsync(); }
    }

    // A folder the player picked (or the one found): turned on only after a verified first copy.
    public Task EnableAsync(SaveCandidate? place = null) => Run(async () =>
    {
        place ??= found ?? (settings.Root.Length > 0 ? await Task.Run(() => protection.Discovery.Manual(settings.Root)) : null);
        if (place is null) return Text["SpFailMissing"];
        var r = await Task.Run(() => protection.EnableAsync(Game, place, settings.Auto, settings.Keep));
        return r.Status == SnapshotStatus.Failed ? FailureText(r.Failure) : Text["SpEnabled"];
    });

    public async Task<bool> UseFolderAsync(string path)
    {
        var place = await Task.Run(() => protection.Discovery.Manual(path));
        if (place is null) { Message = Text["SpFolderEmpty"]; return false; }
        await EnableAsync(place);
        return IsOn;
    }

    public Task BackupNowAsync() => Run(async () =>
    {
        var r = await Task.Run(() => protection.BackupAsync(Game, SnapshotManifest.Manual));
        return r.Status switch { SnapshotStatus.Created => Text["SpBackedUp"], SnapshotStatus.Unchanged => Text["SpUnchanged"], _ => FailureText(r.Failure) };
    });

    public Task DeleteAsync(string id) => Run(async () => await Task.Run(() => protection.Vault.Delete(Game.Key, id)) ? Text["SpDeleted"] : Text["SpFailGeneric"]);

    public void SetAuto(bool on) { if (on != Auto) { protection.SetAuto(Game, on); settings = protection.Get(Game.Key); Notify(); } }
    public void SetKeep(int keep) { if (keep != Keep) { protection.SetKeep(Game, keep); settings = protection.Get(Game.Key); Notify(); } }
    public Task DisableAsync() { protection.Disable(Game); Message = Text["SpDisabled"]; return LoadAsync(); }

    // Restore: checked and planned first (files listed for the player), then run only after confirmation.
    public Task<RestorePlan?> PlanAsync(string id) => Task.Run(() => protection.Vault.Verify(Game.Key, id) ? protection.PlanRestore(Game, id) : null);
    public bool GameIsRunning() { try { return gameRunning(Game); } catch (Exception) { return true; } } // unsure = treat as open

    public async Task<RestoreResult> RestoreAsync(string id)
    {
        RestoreResult result = new(false, SaveFailure.Cancelled);
        await Run(async () =>
        {
            result = await Task.Run(() => protection.RestoreAsync(Game, id, GameIsRunning));
            return result.Restored ? Text["SpRestored"] : FailureText(result.Failure);
        });
        return result;
    }

    // A restore that Lotus could not finish (closed, power cut): put the saves back as they were before it.
    public Task RecoverAsync() => Run(async () => await Task.Run(() => protection.RecoverInterrupted(Game)) ? Text["SpRecovered"] : Text["SpFailGeneric"]);
}
