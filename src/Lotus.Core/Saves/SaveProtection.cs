using Lotus.Core.Games;

namespace Lotus.Core.Saves;

// ---------- Where saves live ----------
// Windows folders the rules use, resolved by the app (Saved Games and LocalLow have no .NET shortcut).
public interface IKnownFolders
{
    string? Get(string token);   // APPDATA, LOCALAPPDATA, LOCALLOW, USERPROFILE, DOCUMENTS, SAVEDGAMES
    string? SteamPath();
}

// A known save location, matched by the store's own id (never by a game's name or its executable).
public sealed record SaveRule(string Store, string Id, string Template);

// Versioned catalog of save folders checked one by one. Short on purpose: only locations known to be right. Anything
// else uses the Steam Cloud folder of that game or a folder the player picks.
public static class SaveCatalog
{
    public const int Version = 1;
    public static readonly IReadOnlyList<SaveRule> Rules =
    [
        new(GameStores.Steam, "413150", @"{APPDATA}\StardewValley\Saves"),
        new(GameStores.Steam, "367520", @"{LOCALLOW}\Team Cherry\Hollow Knight"),
        new(GameStores.Steam, "105600", @"{DOCUMENTS}\My Games\Terraria"),
        new(GameStores.Steam, "292030", @"{DOCUMENTS}\The Witcher 3\gamesaves"),
        new(GameStores.Steam, "1091500", @"{SAVEDGAMES}\CD Projekt Red\Cyberpunk 2077"),
        new(GameStores.Steam, "1245620", @"{APPDATA}\EldenRing"),
        new(GameStores.Steam, "374320", @"{APPDATA}\DarkSoulsIII"),
        new(GameStores.Steam, "814380", @"{APPDATA}\Sekiro"),
        new(GameStores.Steam, "489830", @"{DOCUMENTS}\My Games\Skyrim Special Edition\Saves"),
        new(GameStores.Steam, "377160", @"{DOCUMENTS}\My Games\Fallout4\Saves"),
        new(GameStores.Steam, "1086940", @"{LOCALAPPDATA}\Larian Studios\Baldur's Gate 3\PlayerProfiles"),
        new(GameStores.Steam, "1174180", @"{DOCUMENTS}\Rockstar Games\Red Dead Redemption 2\Profiles"),
        new(GameStores.Steam, "271590", @"{DOCUMENTS}\Rockstar Games\GTA V\Profiles"),
        new(GameStores.Steam, "1593500", @"{SAVEDGAMES}\God of War"),
        new(GameStores.Steam, "1145360", @"{DOCUMENTS}\Saved Games\Hades"),
        new(GameStores.Steam, "504230", @"{INSTALL}\Saves"),
        new(GameStores.Steam, "990080", @"{LOCALAPPDATA}\Hogwarts Legacy\Saved\SaveGames"),
        new(GameStores.Steam, "1687950", @"{APPDATA}\SEGA\P5R\Steam"),
    ];
}

public enum SaveFindStatus { Found, NotFound, Unsupported }
public sealed record SaveCandidate(string Root, string Source, bool CloudSynced, long Bytes, int Files, DateTime? LastWriteUtc);
public sealed record SaveFind(SaveFindStatus Status, IReadOnlyList<SaveCandidate> Candidates, string? Reason = null);

public sealed class SaveDiscovery(IKnownFolders folders)
{
    public SaveFind Find(GameEntry game)
    {
        // Xbox / Game Pass keep saves in a protected, synced container: Lotus does not touch it.
        if (game.Store == GameStores.Xbox) return new(SaveFindStatus.Unsupported, [], "xbox");
        var list = new List<SaveCandidate>();
        var steamCloud = game.Store == GameStores.Steam && SteamCloudFolders(game.Id).Any();
        foreach (var rule in SaveCatalog.Rules.Where(r => r.Store == game.Store && r.Id == game.Id))
            if (Resolve(rule.Template, game) is { } path && Describe(path, "catalog", steamCloud) is { } c) list.Add(c);
        if (game.Store == GameStores.Steam)
            foreach (var remote in SteamCloudFolders(game.Id))
                if (Describe(remote, "steamcloud", true) is { } c && list.All(x => !SamePath(x.Root, c.Root))) list.Add(c);
        return list.Count > 0 ? new(SaveFindStatus.Found, list) : new(SaveFindStatus.NotFound, []);
    }

    // A folder the player picked: same checks (it must exist and hold files; links are not followed).
    public SaveCandidate? Manual(string folder) => Describe(folder, "manual", false);

    private string? Resolve(string template, GameEntry game)
    {
        var path = template;
        foreach (var token in new[] { "APPDATA", "LOCALAPPDATA", "LOCALLOW", "USERPROFILE", "DOCUMENTS", "SAVEDGAMES", "INSTALL" })
        {
            var marker = "{" + token + "}";
            if (!path.Contains(marker)) continue;
            var value = token == "INSTALL" ? game.InstallDir : folders.Get(token);
            if (string.IsNullOrEmpty(value)) return null;
            path = path.Replace(marker, value.TrimEnd('\\'));
        }
        return path;
    }

    // <Steam>\userdata\<account>\<appid>\remote, newest first (one per Steam account on this PC).
    private IEnumerable<string> SteamCloudFolders(string appId)
    {
        if (!appId.All(char.IsAsciiDigit) || folders.SteamPath() is not { } steam) yield break;
        var userdata = Path.Combine(steam, "userdata");
        if (!Directory.Exists(userdata)) yield break;
        IEnumerable<string> accounts;
        try { accounts = Directory.EnumerateDirectories(userdata).Where(d => Path.GetFileName(d).All(char.IsAsciiDigit)).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }
        foreach (var remote in accounts.Select(a => Path.Combine(a, appId, "remote")).Where(Directory.Exists).OrderByDescending(Directory.GetLastWriteTimeUtc))
            yield return remote;
    }

    private static SaveCandidate? Describe(string path, string source, bool cloud)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            if (!dir.Exists) return null;
            var scan = SaveVault.Scan(path);
            if (scan.Files.Count == 0) return null; // an empty folder is not "the save"
            var last = scan.Files.Max(f => f.File.LastWriteTimeUtc);
            return new(dir.FullName, source, cloud, scan.TotalBytes, scan.Files.Count, last);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return null; }
    }
    private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}

// ---------- Choices per game ----------
public sealed record GameSaveSettings
{
    public bool Enabled { get; set; }
    public bool Auto { get; set; } = true;
    public string Root { get; set; } = "";
    public string Source { get; set; } = "";
    public bool CloudSynced { get; set; }
    public int Keep { get; set; } = 5;
    public string GameName { get; set; } = "";
    public DateTimeOffset? LastAttempt { get; set; }
    public string? LastFailure { get; set; } // null = last attempt worked
}
public sealed class SaveProtectionData
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;
    public Dictionary<string, GameSaveSettings> Games { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
public interface ISaveSettingsStore { SaveProtectionData Load(); void Save(SaveProtectionData data); }

public enum ProtectionState { Available, Active, Off, NotFound, Unsupported, Attention, Unavailable }

// Ties discovery, settings and the vault together. Every file operation runs off the UI thread; failures are recorded,
// never reported as success, and never stop anything else (diary, launch).
public sealed class SaveProtection
{
    private readonly ISaveSettingsStore store;
    private readonly Action<Exception> log;
    private SaveProtectionData data;
    private readonly HashSet<string> busy = new(StringComparer.OrdinalIgnoreCase);
    public SaveVault Vault { get; }
    public SaveDiscovery Discovery { get; }
    public event EventHandler<string>? Changed;
    public static readonly int[] KeepChoices = [3, 5, 10];

    public SaveProtection(ISaveSettingsStore store, SaveVault vault, SaveDiscovery discovery, Action<Exception> log)
    {
        this.store = store; this.log = log; Vault = vault; Discovery = discovery;
        try { data = store.Load(); data.Games = new(data.Games ?? [], StringComparer.OrdinalIgnoreCase); } catch (Exception ex) { log(ex); data = new(); }
    }

    public GameSaveSettings Get(string key) => data.Games.GetValueOrDefault(key) ?? new();
    public IReadOnlyList<string> Keys => [.. data.Games.Keys];
    public bool IsBusy(string key) => busy.Contains(key);
    private bool Put(string key, GameSaveSettings s)
    {
        var copy = new SaveProtectionData { Games = new(data.Games, StringComparer.OrdinalIgnoreCase) };
        copy.Games[key] = s;
        try { store.Save(copy); } catch (Exception ex) { log(ex); return false; }
        data = copy; Changed?.Invoke(this, key); return true;
    }

    public ProtectionState State(GameEntry game, SaveFind? find = null)
    {
        var s = Get(game.Key);
        if (Vault.RestoreInterrupted(game.Key)) return ProtectionState.Attention;
        if (s.Enabled)
        {
            if (!Directory.Exists(s.Root)) return ProtectionState.Unavailable;   // drive missing, folder moved…
            return s.LastFailure is null ? ProtectionState.Active : ProtectionState.Attention;
        }
        find ??= Discovery.Find(game);
        return find.Status switch
        {
            SaveFindStatus.Unsupported => ProtectionState.Unsupported,
            SaveFindStatus.NotFound => s.Root.Length > 0 ? ProtectionState.Off : ProtectionState.NotFound,
            _ => s.Root.Length > 0 ? ProtectionState.Off : ProtectionState.Available,
        };
    }

    // The player authorized this folder: a first snapshot is made and checked before protection counts as on.
    public async Task<SnapshotResult> EnableAsync(GameEntry game, SaveCandidate place, bool auto, int keep, CancellationToken token = default)
    {
        if (!busy.Add(game.Key)) return new(SnapshotStatus.Failed, null, SaveFailure.Cancelled, "busy");
        try
        {
            var result = await Vault.CreateAsync(game.Key, game.Name, place.Root, SnapshotManifest.Manual, keep, token);
            var ok = result.Status != SnapshotStatus.Failed && result.Manifest is { IsValid: true } m && Vault.Verify(game.Key, m.Id);
            Put(game.Key, new GameSaveSettings
            {
                Enabled = ok, Auto = auto, Root = place.Root, Source = place.Source, CloudSynced = place.CloudSynced, Keep = keep, GameName = game.Name,
                LastAttempt = DateTimeOffset.Now, LastFailure = ok ? null : result.Failure.ToString(),
            });
            return ok ? result : result with { Status = SnapshotStatus.Failed, Failure = result.Failure == SaveFailure.None ? SaveFailure.VerifyFailed : result.Failure };
        }
        finally { busy.Remove(game.Key); }
    }

    public void Disable(GameEntry game) => Put(game.Key, Get(game.Key) with { Enabled = false }); // backups stay; nothing is deleted
    public void SetAuto(GameEntry game, bool auto) => Put(game.Key, Get(game.Key) with { Auto = auto });
    public void SetKeep(GameEntry game, int keep) => Put(game.Key, Get(game.Key) with { Keep = KeepChoices.Contains(keep) ? keep : 5 });

    public async Task<SnapshotResult> BackupAsync(GameEntry game, string origin, CancellationToken token = default)
    {
        var s = Get(game.Key);
        if (!s.Enabled) return new(SnapshotStatus.Failed, null, SaveFailure.SourceMissing, "off");
        if (!busy.Add(game.Key)) return new(SnapshotStatus.Failed, null, SaveFailure.Cancelled, "busy"); // never two at once
        try
        {
            SnapshotResult result;
            try { result = await Vault.CreateAsync(game.Key, game.Name, s.Root, origin, s.Keep, token); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log(ex); result = new(SnapshotStatus.Failed, null, SaveFailure.Unreadable); }
            Put(game.Key, Get(game.Key) with { LastAttempt = DateTimeOffset.Now, LastFailure = result.Status == SnapshotStatus.Failed ? result.Failure.ToString() : null });
            return result;
        }
        finally { busy.Remove(game.Key); }
    }

    // After a session: wait for the files to settle, make sure the game is really closed, then back up (unchanged
    // saves make no new version). Null when nothing was attempted.
    public async Task<SnapshotResult?> AfterSessionAsync(GameEntry game, Func<bool> stillRunning, TimeSpan settle, CancellationToken token = default)
    {
        var s = Get(game.Key);
        if (!s.Enabled || !s.Auto) return null;
        await Task.Delay(settle, token);
        if (stillRunning()) return null; // reopened, or a helper process is still writing: next session will do it
        return await BackupAsync(game, SnapshotManifest.Auto, token);
    }

    public RestorePlan? PlanRestore(GameEntry game, string snapshotId) => Get(game.Key) is { Root.Length: > 0 } s ? Vault.Plan(game.Key, snapshotId, s.Root) : null;

    public async Task<RestoreResult> RestoreAsync(GameEntry game, string snapshotId, Func<bool> gameRunning, CancellationToken token = default)
    {
        var s = Get(game.Key);
        if (s.Root.Length == 0) return new(false, SaveFailure.SourceMissing);
        if (!busy.Add(game.Key)) return new(false, SaveFailure.Cancelled, null, "busy");
        try { return await Vault.RestoreAsync(game.Key, game.Name, snapshotId, s.Root, gameRunning, keepSafety: 3, token); }
        catch (OperationCanceledException) { return new(false, SaveFailure.Cancelled); }
        catch (Exception ex) { log(ex); return new(false, SaveFailure.Unreadable); }
        finally { busy.Remove(game.Key); Changed?.Invoke(this, game.Key); }
    }

    public bool RecoverInterrupted(GameEntry game)
    {
        try { return Vault.RecoverInterrupted(game.Key); }
        catch (Exception ex) { log(ex); return false; }
        finally { Changed?.Invoke(this, game.Key); }
    }

    // Games with a restore Lotus could not finish (checked once at start).
    public IEnumerable<string> InterruptedGames() => data.Games.Keys.Where(Vault.RestoreInterrupted).ToList();

    // For "Preparar e Jogar": a short note only when something needs the player's attention.
    public string? Warning(GameEntry game, ITextCatalog text)
    {
        var s = Get(game.Key);
        if (Vault.RestoreInterrupted(game.Key)) return text["SpWarnInterrupted"];
        if (!s.Enabled) return null;
        if (!Directory.Exists(s.Root)) return text["SpWarnMissing"];
        return s.LastFailure is null ? null : text["SpWarnFailed"];
    }
}
