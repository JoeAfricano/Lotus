using Lotus.Core.Diary;
using Lotus.Core.Games;
using Lotus.Infrastructure.Backup;
using Lotus.Infrastructure.Games;
using Xunit;

namespace Lotus.Tests;

// "Onde eu parei?": notes, persistence, one source of truth with the player panel, end-of-session rules and backup.
public sealed class ResumeNoteTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "lotus-resume-" + Guid.NewGuid().ToString("N"));
    public ResumeNoteTests() => Directory.CreateDirectory(dir);
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }
    private string File_(string name) => Path.Combine(dir, name);
    private sealed class Memory : IResumeNoteStore { public ResumeNoteData Data = new(); public int Saves; public bool Fail;
        public ResumeNoteData Load() => Data; public void Save(ResumeNoteData d) { if (Fail) throw new IOException("disk"); Data = d; Saves++; } }
    private sealed class DiaryMemory : IGameDiaryStore { public DiaryData Saved = new(); public DiaryData Load() => Saved; public void Save(DiaryData d) => Saved = d; }
    private sealed class JournalMemory : IPlayerJournalStore { public PlayerJournalData Data = PlayerJournalData.Empty; public PlayerJournalData Load() => Data; public void Save(PlayerJournalData d) => Data = d; }

    [Fact] public void CreateEditClearKeepsCreatedAndReportsChanges()
    {
        var store = new Memory(); var notes = new ResumeNotes(store, ex => throw ex); var changed = new List<string>();
        notes.Changed += (_, k) => changed.Add(k);
        var t0 = new DateTimeOffset(2026, 10, 1, 20, 0, 0, TimeSpan.Zero);
        Assert.True(notes.Set("steam:292030", "  Terminei a missão do Barão.  ", now: t0));
        Assert.Equal("Terminei a missão do Barão.", notes.Get("steam:292030")!.Text);
        Assert.True(notes.Set("steam:292030", "Procurar pistas no pântano.", now: t0.AddDays(3)));
        var n = notes.Get("STEAM:292030")!; // keys ignore case
        Assert.Equal(t0, n.Created); Assert.Equal(t0.AddDays(3), n.Updated);
        Assert.True(notes.Set("steam:292030", "   ")); // empty = clear
        Assert.Null(notes.Get("steam:292030"));
        Assert.Equal(3, changed.Count);
        Assert.True(notes.Set("x", new string('a', 900))); Assert.Equal(ResumeNotes.Limit, notes.Get("x")!.Text.Length);
    }

    [Fact] public void FailedSaveIsNeverReportedAsSaved()
    {
        var store = new Memory { Fail = true }; var errors = 0;
        var notes = new ResumeNotes(store, _ => errors++);
        Assert.False(notes.Set("steam:1", "nota"));
        Assert.Null(notes.Get("steam:1")); Assert.Equal(1, errors);
    }

    [Fact] public void SurvivesRestartAndFallsBackToBackupFile()
    {
        var path = File_("resume-notes.json");
        var a = new ResumeNotes(new JsonResumeNoteStore(path), ex => throw ex);
        a.Set("steam:1", "primeira"); a.Set("steam:2", "segunda");
        var b = new ResumeNotes(new JsonResumeNoteStore(path), ex => throw ex);
        Assert.Equal("primeira", b.Get("steam:1")!.Text); Assert.Equal("segunda", b.Get("steam:2")!.Text);
        File.WriteAllText(path, "{ quebrado");
        var c = new ResumeNotes(new JsonResumeNoteStore(path), ex => throw ex);
        Assert.Equal("primeira", c.Get("steam:1")!.Text); // from resume-notes.json.bak (the version before the last save)
    }

    [Fact] public void SameNameInTwoStoresKeepsTwoNotes()
    {
        var steam = new GameEntry(GameStores.Steam, "1091500", "Cyberpunk 2077");
        var gog = new GameEntry(GameStores.Gog, "1423049311", "Cyberpunk 2077");
        Assert.NotEqual(DiaryGames.KeyFor(steam), DiaryGames.KeyFor(gog));
        Assert.Equal("steam:1091500", DiaryGames.KeyFor("Steam:1091500")); Assert.Equal(DiaryGames.KeyFor(gog), DiaryGames.KeyFor(gog.Key));
        var notes = new ResumeNotes(new Memory(), ex => throw ex);
        notes.Set(DiaryGames.KeyFor(steam), "Steam"); notes.Set(DiaryGames.KeyFor(gog), "GOG");
        Assert.Equal("Steam", notes.Get(DiaryGames.KeyFor(steam))!.Text); Assert.Equal("GOG", notes.Get(DiaryGames.KeyFor(gog))!.Text);
    }

    [Fact] public void PlayerPanelNotesBecomeTheSameNoteOnceWithoutDeletingTheOldCopy()
    {
        var journalStore = new JournalMemory { Data = new([], new() { ["Steam:10"] = "parei no chefe", ["Epic Games:abc"] = "jogar com a Ana" }) };
        var journal = new PlayerJournal(journalStore, ex => throw ex);
        var notes = new ResumeNotes(new Memory(), ex => throw ex);
        notes.Set("steam:10", "nota mais nova"); // an existing resume note always wins
        journal.Resume = notes;
        Assert.Equal("nota mais nova", journal.Note("Steam:10")); Assert.Equal("jogar com a Ana", journal.Note("Epic Games:abc"));
        journal.SetNote("Steam:10", "escrevi no painel");
        Assert.Equal("escrevi no painel", notes.Get("steam:10")!.Text); // one source of truth
        Assert.Equal("parei no chefe", journalStore.Data.Notes["Steam:10"]); // the old copy in player.json is kept
        var again = new PlayerJournal(journalStore, ex => throw ex) { Resume = notes };
        Assert.Equal("escrevi no painel", again.Note("Steam:10")); // migration happens only once
    }

    [Fact] public void PromptOnlyAfterANormalCloseAndOncePerSession()
    {
        var notes = new ResumeNotes(new Memory(), ex => throw ex);
        var start = new DateTimeOffset(2026, 10, 30, 20, 0, 0, TimeSpan.Zero);
        Assert.True(notes.ShouldPrompt("steam:1", start));
        notes.Set("steam:1", "antiga", now: start.AddDays(-5));
        Assert.True(notes.ShouldPrompt("steam:1", start));          // written before this session
        notes.Set("steam:1", "agora", start, start.AddMinutes(90));
        Assert.False(notes.ShouldPrompt("steam:1", start));         // already written for this session

        var t = new PlaySessionTracker(); var g = new GameEntry("Steam", "1", "Hades");
        for (var m = 0; m <= 10; m++) t.Update(g, start.AddMinutes(m));
        Assert.Equal(SessionEnd.Closed, t.Update(null, start.AddMinutes(11))!.End);
        for (var m = 0; m <= 10; m++) t.Update(g, start.AddHours(2).AddMinutes(m));
        Assert.Equal(SessionEnd.Gap, t.Update(g, start.AddHours(5))!.End); // the PC slept
        for (var m = 1; m <= 10; m++) t.Update(g, start.AddHours(5).AddMinutes(m));
        Assert.Equal(SessionEnd.Switched, t.Update(new GameEntry("Steam", "2", "Celeste"), start.AddHours(5).AddMinutes(11))!.End);
        for (var m = 12; m <= 20; m++) t.Update(new GameEntry("Steam", "2", "Celeste"), start.AddHours(5).AddMinutes(m));
        Assert.Equal(SessionEnd.LotusClosing, t.Flush()!.End);
        Assert.False(t.Flush()?.EndedNormally ?? false);
    }

    [Fact] public void EditorNoteNeverChangesTheDiaryEntry()
    {
        var diary = new GameDiary(new DiaryMemory(), ex => throw ex);
        var hades = new DiaryEntry { Key = "steam:1145360", Name = "Hades", Status = DiaryStatus.Playing, Rating = 9, Minutes = 600 };
        diary.Save(hades);
        var before = diary.Get("steam:1145360")!.Updated;
        var notes = new ResumeNotes(new Memory(), ex => throw ex);
        var editor = new DiaryEditorViewModel(diary, new Lotus.Core.TextCatalog()) { Resume = notes };
        editor.Open(diary.Get("steam:1145360")!);
        Assert.True(editor.ShowResumeEmpty); Assert.False(editor.IsDirty);
        editor.EditResume(); editor.ResumeDraft = "Sala do Hades, terceira tentativa";
        Assert.True(editor.IsDirty);                         // an unsaved note is not lost silently
        Assert.True(editor.SaveResume());
        Assert.False(editor.IsDirty); Assert.True(editor.ShowResumeText);
        var e = diary.Get("steam:1145360")!;
        Assert.Equal(9, e.Rating); Assert.Equal(600, e.Minutes); Assert.Equal(DiaryStatus.Playing, e.Status); Assert.Equal(before, e.Updated);
        Assert.True(editor.ClearResume()); Assert.Null(notes.Get("steam:1145360"));
        // A note being written goes in with the main "Salvar no diário" too.
        editor.EditResume(); editor.ResumeDraft = "salvo junto"; editor.Save();
        Assert.Equal("salvo junto", notes.Get("steam:1145360")!.Text);
    }

    [Fact] public void MoodChipsUseRealIcons()
    {
        var editor = new DiaryEditorViewModel(new GameDiary(new DiaryMemory(), ex => throw ex), new Lotus.Core.TextCatalog());
        Assert.All(editor.Moods, m => { Assert.InRange(m.Glyph.Length, 1, 2); Assert.False(m.Glyph.All(char.IsAsciiHexDigit)); });
    }

    [Fact] public void BackupCarriesNotesAndOldBackupsStillRestore()
    {
        var root = Path.Combine(dir, "Lotus"); Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "preferences.json"), "{}");
        File.WriteAllText(Path.Combine(root, "resume-notes.json"), """{"Version":1,"Notes":{"steam:1":{"Text":"pântano","Created":"2026-10-01T20:00:00+00:00","Updated":"2026-10-01T20:00:00+00:00"}}}""");
        var archive = new LotusBackupArchive(root, "0.6.0");
        using var withNotes = new MemoryStream();
        Assert.Contains("resume-notes.json", archive.Write(withNotes).Files);
        Assert.True(LotusBackupArchive.Allowed("resume-notes.json"));
        // An older backup (no notes file) restores and keeps the notes already on this PC.
        File.Delete(Path.Combine(root, "resume-notes.json"));
        using var old = new MemoryStream();
        archive.Write(old);
        File.WriteAllText(Path.Combine(root, "resume-notes.json"), """{"Version":1,"Notes":{"steam:2":{"Text":"local","Created":"2026-10-02T20:00:00+00:00","Updated":"2026-10-02T20:00:00+00:00"}}}""");
        old.Position = 0; archive.Stage(old); archive.ApplyPending();
        Assert.Contains("local", File.ReadAllText(Path.Combine(root, "resume-notes.json")));
        // The newer backup brings its notes back.
        withNotes.Position = 0; archive.Stage(withNotes); archive.ApplyPending();
        var notes = new ResumeNotes(new JsonResumeNoteStore(Path.Combine(root, "resume-notes.json")), ex => throw ex);
        Assert.Equal("pântano", notes.Get("steam:1")!.Text);
    }
}
