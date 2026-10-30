namespace Lotus.Core.Diary;

// "Onde eu parei?": one short personal note per game ("Terminei a missão do Barão; procurar pistas no pântano").
// The single source of truth for that note: the diary editor, Home ("Continuar jogando"), the player panel and the
// end-of-session prompt all read and write here. Keyed like the diary (DiaryGames.KeyFor: "steam:<appid>" or the
// store key), so two games with the same name in different stores never share a note. Lotus never reads game memory
// or save files for this: it is only what the player wrote.
public sealed record ResumeNote(string Text, DateTimeOffset Created, DateTimeOffset Updated, DateTimeOffset? SessionStart = null);

public sealed class ResumeNoteData
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;
    public Dictionary<string, ResumeNote> Notes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool PlayerNotesMigrated { get; set; } // the short notes of the player panel (player.json) were brought in once
}

public interface IResumeNoteStore { ResumeNoteData Load(); void Save(ResumeNoteData data); }

public sealed class ResumeNotes
{
    public const int Limit = 600;
    private readonly IResumeNoteStore store;
    private readonly Action<Exception> log;
    private ResumeNoteData data;
    public event EventHandler<string>? Changed; // the game key

    public ResumeNotes(IResumeNoteStore store, Action<Exception> log)
    {
        this.store = store; this.log = log;
        try { data = Normalize(store.Load()); }
        catch (Exception ex) { log(ex); data = new(); } // a broken file never blocks the app (the store keeps a .bak)
    }

    private static ResumeNoteData Normalize(ResumeNoteData? d)
    {
        d ??= new();
        var clean = new Dictionary<string, ResumeNote>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, n) in d.Notes ?? [])
            if (!string.IsNullOrWhiteSpace(k) && n is { Text.Length: > 0 }) clean[k.Trim()] = n with { Text = Trim(n.Text) };
        return new() { Version = ResumeNoteData.CurrentVersion, Notes = clean, PlayerNotesMigrated = d.PlayerNotesMigrated };
    }
    private static string Trim(string? text)
    {
        var t = (text ?? "").Trim().Replace("\r\n", "\n");
        return t.Length > Limit ? t[..Limit] : t;
    }

    public ResumeNote? Get(string key) => data.Notes.GetValueOrDefault(key);
    public bool Has(string key) => data.Notes.ContainsKey(key);
    public IReadOnlyDictionary<string, ResumeNote> All => data.Notes;

    // Saves (or, when empty, removes) the note. Created is kept across edits; Updated is now. False when it could not be saved.
    public bool Set(string key, string? text, DateTimeOffset? sessionStart = null, DateTimeOffset? now = null)
    {
        var clean = Trim(text);
        if (clean.Length == 0) return Clear(key);
        var at = now ?? DateTimeOffset.Now;
        var old = Get(key);
        if (old is not null && old.Text == clean && old.SessionStart == (sessionStart ?? old.SessionStart)) return true; // nothing changed
        var note = new ResumeNote(clean, old?.Created ?? at, at, sessionStart ?? (old?.Text == clean ? old.SessionStart : null));
        return Commit(key, d => d.Notes[key] = note);
    }

    public bool Clear(string key) => !Has(key) || Commit(key, d => d.Notes.Remove(key));

    // End-of-session prompt: only when the player has not written about this session already.
    public bool ShouldPrompt(string key, DateTimeOffset sessionStart) => Get(key) is not { } n || n.Updated < sessionStart;

    // The player panel used to keep its own short notes in player.json; they become resume notes once (nothing is
    // deleted there, so going back to an older Lotus still finds them). An existing resume note always wins.
    public void MigratePlayerNotes(IReadOnlyDictionary<string, string> playerNotes, Func<string, string> keyFor, DateTimeOffset? now = null)
    {
        if (data.PlayerNotesMigrated) return;
        var at = now ?? DateTimeOffset.Now;
        Commit("", d =>
        {
            foreach (var (libraryKey, text) in playerNotes)
            {
                var key = keyFor(libraryKey);
                if (Trim(text) is { Length: > 0 } t && !d.Notes.ContainsKey(key)) d.Notes[key] = new(t, at, at);
            }
            d.PlayerNotesMigrated = true;
        });
    }

    private bool Commit(string key, Action<ResumeNoteData> change)
    {
        var copy = new ResumeNoteData { Notes = new(data.Notes, StringComparer.OrdinalIgnoreCase), PlayerNotesMigrated = data.PlayerNotesMigrated };
        change(copy);
        try { store.Save(copy); }
        catch (Exception ex) { log(ex); return false; } // never report a save that did not happen
        data = copy;
        if (key.Length > 0) Changed?.Invoke(this, key);
        return true;
    }
}
