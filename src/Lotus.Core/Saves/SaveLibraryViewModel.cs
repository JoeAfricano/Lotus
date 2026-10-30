using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Lotus.Core.Games;

namespace Lotus.Core.Saves;

public enum SaveTileKind { Protected, Attention, Found, Off, None, Unsupported }

// One game on the "Backup de saves" page.
public sealed record SaveGameTile(GameEntry Game, string Name, string Initials, string? CoverPath, string Store, SaveTileKind Kind, string Badge, string Detail,
    int Backups, long Bytes, bool Installed)
{
    public bool IsProtected => Kind == SaveTileKind.Protected;
    public bool NeedsAttention => Kind == SaveTileKind.Attention;
    public bool IsDim => Kind is SaveTileKind.None or SaveTileKind.Unsupported;
    public string BadgeGlyph => Kind switch
    {
        SaveTileKind.Protected => "", SaveTileKind.Attention => "", SaveTileKind.Unsupported => "",
        SaveTileKind.None => "", _ => "",
    };
}

// "Backup de saves": every game on this PC (and any protected game that was uninstalled), what is protected, and one
// game's backups when it is opened. Folders are only read when the page is opened, off the UI thread.
public sealed class SaveLibraryViewModel(SaveProtection protection, Func<IReadOnlyList<GameEntry>> installed, ITextCatalog text, Func<GameEntry, bool> gameRunning) : ObservableObject
{
    public ITextCatalog Text { get; } = text;
    public ObservableCollection<SaveGameTile> Items { get; } = [];
    private List<SaveGameTile> all = [];
    private string filter = "all", search = "";
    private bool loading, loaded;
    private GameSavesViewModel? selected;
    private SaveGameTile? selectedTile;
    private CultureInfo Culture => CultureInfo.GetCultureInfo(Text.Language);

    public static readonly string[] Filters = ["all", "protected", "found", "none"];
    public string Filter { get => filter; set { if (SetProperty(ref filter, Filters.Contains(value) ? value : "all")) Apply(); } }
    public string Search { get => search; set { if (SetProperty(ref search, value ?? "")) Apply(); } }
    public bool Loading { get => loading; private set { if (SetProperty(ref loading, value)) OnPropertyChanged(nameof(IsEmpty)); } }
    public bool Loaded { get => loaded; private set => SetProperty(ref loaded, value); }
    public bool IsEmpty => !Loading && Loaded && Items.Count == 0;
    public string? PendingOpen { get; set; } // game key to open once the list is ready (from a game's page)

    public GameSavesViewModel? Selected { get => selected; private set { if (SetProperty(ref selected, value)) OnPropertyChanged(nameof(HasSelection)); } }
    public SaveGameTile? SelectedTile { get => selectedTile; private set => SetProperty(ref selectedTile, value); }
    public bool HasSelection => Selected is not null;

    public int ProtectedCount => all.Count(t => t.Kind is SaveTileKind.Protected or SaveTileKind.Attention);
    public int BackupCount => all.Sum(t => t.Backups);
    public string SpaceUsed => Size(all.Sum(t => t.Bytes));
    public int FoundCount => all.Count(t => t.Kind is not (SaveTileKind.None or SaveTileKind.Unsupported));

    public string Size(long bytes) => bytes <= 0 ? "0 MB" : bytes >= 1L << 30 ? (bytes / (double)(1L << 30)).ToString("0.0", Culture) + " GB"
        : (bytes / (double)(1L << 20)).ToString(bytes < 10L << 20 ? "0.0" : "0", Culture) + " MB";

    public async Task LoadAsync()
    {
        if (Loading) return;
        Loading = true;
        try
        {
            all = await Task.Run(Build);
            Loaded = true;
            Apply();
            foreach (var n in new[] { nameof(ProtectedCount), nameof(BackupCount), nameof(SpaceUsed), nameof(FoundCount) }) OnPropertyChanged(n);
        }
        finally { Loading = false; }
        if (PendingOpen is { } key && all.FirstOrDefault(t => string.Equals(t.Game.Key, key, StringComparison.OrdinalIgnoreCase)) is { } tile) { PendingOpen = null; await OpenAsync(tile); }
    }

    private List<SaveGameTile> Build()
    {
        var games = installed().ToList();
        // Protected games that are no longer installed keep their backups visible (and restorable once reinstalled).
        foreach (var key in protection.Keys.Where(k => games.All(g => !string.Equals(g.Key, k, StringComparison.OrdinalIgnoreCase))))
        {
            var parts = key.Split(':', 2);
            if (parts.Length == 2 && protection.Vault.List(key).Count > 0) games.Add(new GameEntry(parts[0], parts[1], protection.Get(key).GameName is { Length: > 0 } n ? n : key));
        }
        var installedKeys = installed().Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var list = new List<SaveGameTile>();
        foreach (var g in games.DistinctBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            try { list.Add(Tile(g, installedKeys.Contains(g.Key))); }
            catch (Exception) { list.Add(new(g, g.Name, Initials(g.Name), g.CoverPath, g.Store, SaveTileKind.None, Text["SlBadgeNone"], "", 0, 0, true)); }
        }
        return [.. list.OrderBy(t => t.Kind switch { SaveTileKind.Attention => 0, SaveTileKind.Protected => 1, SaveTileKind.Off => 2, SaveTileKind.Found => 3, _ => 4 })
            .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private SaveGameTile Tile(GameEntry g, bool isInstalled)
    {
        var snaps = protection.Vault.List(g.Key);
        var state = isInstalled ? protection.State(g) : protection.Get(g.Key).Enabled ? ProtectionState.Active : ProtectionState.Off;
        var kind = state switch
        {
            ProtectionState.Active => SaveTileKind.Protected,
            ProtectionState.Attention or ProtectionState.Unavailable => SaveTileKind.Attention,
            ProtectionState.Off => SaveTileKind.Off,
            ProtectionState.Available => SaveTileKind.Found,
            ProtectionState.Unsupported => SaveTileKind.Unsupported,
            _ => snaps.Count > 0 ? SaveTileKind.Off : SaveTileKind.None,
        };
        var badge = Text[kind switch
        {
            SaveTileKind.Protected => "SlBadgeProtected", SaveTileKind.Attention => "SlBadgeAttention", SaveTileKind.Found => "SlBadgeFound",
            SaveTileKind.Off => "SlBadgeOff", SaveTileKind.Unsupported => "SlBadgeXbox", _ => "SlBadgeNone",
        }];
        var detail = snaps.Count == 0 ? (kind is SaveTileKind.None or SaveTileKind.Unsupported ? "" : Text["SlNoBackupsYet"])
            : $"{(snaps.Count == 1 ? Text["SlBackupOne"] : Text.Format("SlBackups", snaps.Count))} · {Ago(snaps.Max(s => s.Created))}";
        if (!isInstalled) detail = Text["SlNotInstalled"] + (detail.Length > 0 ? " · " + detail : "");
        return new(g, g.Name, Initials(g.Name), g.CoverPath, g.Store, kind, badge, detail, snaps.Count, snaps.Sum(s => s.TotalBytes), isInstalled);
    }

    public string Ago(DateTimeOffset when)
    {
        var span = DateTimeOffset.Now - when;
        return span.TotalMinutes < 1 ? Text["SlNow"]
            : span.TotalHours < 1 ? Text.Format("SlMinutes", (int)span.TotalMinutes)
            : span.TotalDays < 1 ? Text.Format("SlHours", (int)span.TotalHours)
            : span.TotalDays < 2 ? Text["SlYesterday"]
            : span.TotalDays < 30 ? Text.Format("SlDays", (int)span.TotalDays)
            : when.ToLocalTime().ToString("d", Culture);
    }

    private static string Initials(string name)
    {
        var words = name.Split([' ', '-', ':'], StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsLetterOrDigit(w[0])).Take(2).ToArray();
        return words.Length == 0 ? "?" : string.Concat(words.Select(w => char.ToUpperInvariant(w[0])));
    }

    private void Apply()
    {
        var q = all.Where(t => filter switch
        {
            "protected" => t.Kind is SaveTileKind.Protected or SaveTileKind.Attention,
            "found" => t.Kind is SaveTileKind.Found or SaveTileKind.Off,
            "none" => t.Kind is SaveTileKind.None or SaveTileKind.Unsupported,
            _ => true,
        });
        if (search.Trim() is { Length: > 0 } s) q = q.Where(t => t.Name.Contains(s, StringComparison.CurrentCultureIgnoreCase));
        Items.Clear();
        foreach (var t in q) Items.Add(t);
        OnPropertyChanged(nameof(IsEmpty));
    }
    public int CountFor(string f) => f switch
    {
        "protected" => ProtectedCount,
        "found" => all.Count(t => t.Kind is SaveTileKind.Found or SaveTileKind.Off),
        "none" => all.Count(t => t.Kind is SaveTileKind.None or SaveTileKind.Unsupported),
        _ => all.Count,
    };

    public async Task OpenAsync(SaveGameTile tile)
    {
        var vm = new GameSavesViewModel(protection, tile.Game, Text, gameRunning);
        SelectedTile = tile; Selected = vm;
        await vm.LoadAsync();
    }

    // Back to the list: the tile shows what changed while the game was open.
    public async Task CloseAsync()
    {
        var tile = SelectedTile;
        Selected = null; SelectedTile = null;
        if (tile is null) return;
        var fresh = await Task.Run(() => Tile(tile.Game, tile.Installed));
        var i = all.FindIndex(t => t.Game.Key == tile.Game.Key);
        if (i >= 0) all[i] = fresh;
        Apply();
        foreach (var n in new[] { nameof(ProtectedCount), nameof(BackupCount), nameof(SpaceUsed), nameof(FoundCount) }) OnPropertyChanged(n);
    }
}
