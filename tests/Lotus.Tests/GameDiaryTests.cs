using Lotus.Core.Diary;
using Lotus.Infrastructure.Games;
using Xunit;

namespace Lotus.Tests;

public sealed class GameDiaryTests
{
    private sealed class Memory : IGameDiaryStore { public DiaryData Saved = new(); public DiaryData Load() => Saved; public void Save(DiaryData d) => Saved = d; }
    private static DiaryEntry G(string id, string name, params string[] genres) => new() { Key = DiaryEntry.SteamKey(id), AppId = id, Name = name, Genres = [.. genres] };

    [Fact] public void FinishingAGameCelebratesWithXpBadgesAndDates()
    {
        var diary = new GameDiary(new Memory(), ex => throw ex);
        Assert.NotNull(diary.Want(G("1", "Hades", "Ação")));
        Assert.Null(diary.Want(G("1", "Hades")));                         // already in the list
        Assert.Equal(DiaryStatus.Want, diary.Get("STEAM:1")!.Status);      // keys ignore case

        var playing = diary.Get("steam:1")!.Clone(); playing.Status = DiaryStatus.Playing;
        var p = diary.Save(playing);
        Assert.False(p.WorthCelebrating); Assert.Equal(DateTime.Today, diary.Get("steam:1")!.Started);

        var done = diary.Get("steam:1")!.Clone();
        done.Status = DiaryStatus.Completed; done.Rating = 9.3; done.Minutes = 40 * 60; done.Feats = ["platinum", "made-up"];
        done.Review = new string('a', 450);
        var c = diary.Save(done);
        Assert.True(c.JustCompleted && c.WorthCelebrating);
        Assert.Equal(9.5, diary.Get("steam:1")!.Rating);                   // half steps
        Assert.Equal(["platinum"], diary.Get("steam:1")!.Feats);           // unknown feats dropped
        Assert.Equal(DateTime.Today, diary.Get("steam:1")!.Finished);
        Assert.Contains(c.NewBadges, b => b.Id == "first"); Assert.Contains(c.NewBadges, b => b.Id == "platinum");
        Assert.Equal(100 + 10 + 60 + 60 + 20, c.XpAfter);                  // completed, rated, long review, platinum, 40 h
        Assert.True(c.LevelUp); Assert.Equal(2, c.LevelAfter);

        var again = diary.Save(diary.Get("steam:1")!);                     // saving again is not a new finish
        Assert.False(again.JustCompleted); Assert.Empty(again.NewBadges);
    }

    [Fact] public void LevelsTitlesAndStats()
    {
        Assert.Equal(1, Progression.LevelFor(99)); Assert.Equal(2, Progression.LevelFor(100)); Assert.Equal(4, Progression.LevelFor(650));
        Assert.Equal("DyTitle1", Progression.TitleKey(1)); Assert.Equal("DyTitle6", Progression.TitleKey(20));
        var entries = new[] { "RPG", "Ação", "Corrida", "Indie", "Estratégia" }.Select((g, i) => { var e = G(i.ToString(), "G" + i, g); e.Status = DiaryStatus.Completed; e.Rating = 8; e.Finished = DateTime.Today; return e; }).ToList();
        var p = Progression.Of(entries);
        Assert.Equal(5, p.Completed); Assert.Equal(8, p.AverageRating); Assert.Equal(5, p.CompletedThisYear);
        Assert.True(p.Badges.Single(b => b.Id == "explorer").Unlocked); Assert.True(p.Badges.Single(b => b.Id == "finisher5").Unlocked);
        Assert.Equal(0.2, p.Badges.Single(b => b.Id == "finisher25").Fraction);
    }

    [Fact] public void StoreKeepsTheDiaryAndRecoversFromABadFile()
    {
        var file = Path.Combine(Path.GetTempPath(), "LotusDiary-" + Guid.NewGuid() + ".json");
        try
        {
            var d = new GameDiary(new JsonGameDiaryStore(file), ex => throw ex);
            var e = G("9", "Celeste", "Indie"); e.Status = DiaryStatus.Dropped; e.Review = "Difícil demais pra mim agora, volto depois com calma.";
            d.Save(e); d.SetBio("  Um CLT, jogando de vez em quando.  ");
            d.Save(G("10", "Outro"));                                       // second save creates the .bak
            var again = new GameDiary(new JsonGameDiaryStore(file), ex => throw ex);
            Assert.Equal(DiaryStatus.Dropped, again.Get("steam:9")!.Status); Assert.Equal("Um CLT, jogando de vez em quando.", again.Bio);
            Assert.True(again.Progress.Badges.Single(b => b.Id == "honest").Unlocked);
            File.WriteAllText(file, "{ broken");
            Assert.Equal("Celeste", new GameDiary(new JsonGameDiaryStore(file), _ => { }).Get("steam:9")?.Name); // from the backup
        }
        finally { foreach (var f in new[] { file, file + ".bak", file + ".tmp" }) File.Delete(f); }
    }
}

public sealed class DiaryScreenTests
{
    private sealed class Memory : IGameDiaryStore { public DiaryData Saved = new(); public DiaryData Load() => Saved; public void Save(DiaryData d) => Saved = d; }
    private sealed class Prefs : Lotus.Core.IPreferencesStore
    {
        public Lotus.Core.InterfacePreferences Saved = new() { DisplayName = "Joe" };
        public Lotus.Core.PreferencesLoadResult Load() => new(Saved, "Loaded");
        public bool TrySave(Lotus.Core.InterfacePreferences p) { Saved = p; return true; }
    }

    [Fact] public void EditorPrefillsSavesAndPageFiltersTabs()
    {
        var diary = new GameDiary(new Memory(), ex => throw ex); var text = new Lotus.Core.TextCatalog();
        var editor = new DiaryEditorViewModel(diary, text);
        var game = new DiaryEntry { Key = "steam:5", AppId = "5", Name = "Hollow Knight", Genres = ["Indie"] };
        editor.Open(game, steamMinutes: 600); Assert.False(editor.IsDirty); // opening is not a change
        editor.Started = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero); editor.Finished = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.True(editor.IsDirty); Assert.False(editor.CanSave); Assert.Equal(text["DyDateOrder"], editor.DateError);
        editor.Started = null; editor.Finished = null; Assert.True(editor.CanSave); Assert.False(editor.IsDirty);
        Assert.True(editor.IsNew); Assert.Equal(DiaryStatus.Playing, editor.Status); Assert.Equal(10, editor.Hours); // Steam hours prefilled
        Assert.Equal(text["DyNoScore"], editor.RatingText);
        editor.SelectStatus(DiaryStatus.Completed); Assert.Equal(DateTimeOffset.Now.Date, editor.Finished?.Date);
        editor.Rating = 8.5; editor.Review = "Lindo e difícil."; editor.FeatToggles.Single(f => f.Id == "nodeath").Selected = true;
        var change = editor.Save();
        Assert.True(change.JustCompleted); Assert.Equal("good", editor.RatingKind);

        diary.Want(new DiaryEntry { Key = "steam:6", Name = "Silksong" });
        var page = new DiaryViewModel(diary, text, new Lotus.Core.PreferencesService(new Prefs()));
        Assert.Equal(2, page.Tabs.Single(t => t.Id == "all").Count); Assert.Equal(1, page.Tabs.Single(t => t.Id == "Completed").Count);
        page.Tab = "Want"; Assert.Equal(["Silksong"], page.Items.Select(i => i.Name));
        page.Tab = "all"; page.Query = "difícil"; Assert.Equal(["Hollow Knight"], page.Items.Select(i => i.Name)); // searches reviews too
        page.Query = ""; page.Sort = "rating"; Assert.Equal("Hollow Knight", page.Items[0].Name);
        Assert.Equal(["Silksong"], page.Shelf().Select(c => c.Name));
        Assert.Equal("Joe", page.Name); Assert.True(page.Badges.First().Unlocked);              // unlocked badges first

        editor.Open(game); Assert.False(editor.IsNew); Assert.Equal(8.5, editor.Rating); Assert.True(editor.FeatToggles.Single(f => f.Id == "nodeath").Selected);
        editor.Remove(); Assert.False(diary.Contains("steam:5"));
    }

    [Fact] public void DiaryFiltersCombineSaveAndRememberTheLayout()
    {
        var diary = new GameDiary(new Memory(), ex => throw ex); var text = new Lotus.Core.TextCatalog();
        diary.Save(new DiaryEntry { Key = "steam:1", Name = "Elden Ring", Genres = ["RPG"], Rating = 9.5, Status = DiaryStatus.Completed, Finished = new DateTime(2025, 3, 1) });
        diary.Save(new DiaryEntry { Key = "epic:2", Name = "Hades", Genres = ["Roguelike"], Rating = 8, Status = DiaryStatus.Completed, Finished = new DateTime(2026, 5, 1), Platform = "Steam Deck" });
        diary.Save(new DiaryEntry { Key = "steam:3", Name = "Starfield", Genres = ["RPG"], Status = DiaryStatus.Dropped });
        var prefs = new Lotus.Core.PreferencesService(new Prefs());
        var page = new DiaryViewModel(diary, text, prefs);
        Assert.Equal(["RPG", "Roguelike"], page.GenreOptions.Select(o => o.Id));                    // most used first
        page.Genre = "RPG"; Assert.Equal(2, page.Items.Count);
        page.RatingBand = "9"; Assert.Equal(["Elden Ring"], page.Items.Select(i => i.Name));
        Assert.Contains("RPG", page.ActiveFilters); Assert.True(page.HasFilters);
        page.SaveCurrentFilter("RPGs nota 9+");
        page.ClearFilters(); Assert.Equal(3, page.Items.Count); Assert.False(page.HasFilters);
        page.Store = "Epic"; Assert.Equal(["Hades"], page.Items.Select(i => i.Name));
        page.ClearFilters(); page.RatingBand = "none"; Assert.Equal(["Starfield"], page.Items.Select(i => i.Name));
        page.ApplyFilter(page.SavedFilters.Single()); Assert.Equal(["Elden Ring"], page.Items.Select(i => i.Name)); // saved filter restored
        page.ClearFilters(); page.Year = "2026"; Assert.Equal(["Hades"], page.Items.Select(i => i.Name));
        Assert.Equal("grid", page.ViewMode); page.ViewMode = "table"; Assert.True(page.IsTable);
        Assert.Equal("table", new DiaryViewModel(diary, text, prefs).ViewMode);                       // layout remembered
        page.DeleteFilter("RPGs nota 9+"); Assert.False(page.HasSavedFilters);
    }

    [Fact] public void ShowcaseKeepsFourSlotsAndOneGameOnce()
    {
        var diary = new GameDiary(new Memory(), ex => throw ex); var text = new Lotus.Core.TextCatalog();
        diary.Save(new DiaryEntry { Key = "steam:1", Name = "Elden Ring", Rating = 10, Feats = ["platinum"], Status = DiaryStatus.Completed });
        diary.Save(new DiaryEntry { Key = "steam:2", Name = "Hades", Status = DiaryStatus.Dropped });
        var page = new DiaryViewModel(diary, text, new Lotus.Core.PreferencesService(new Prefs()));
        Assert.Equal(4, page.Showcase.Count); Assert.All(page.Showcase, s => Assert.True(s.IsEmpty));
        page.SetShowcase(2, "steam:1"); Assert.Equal("Elden Ring", page.Showcase[2].Card!.Name);
        page.SetShowcase(0, "steam:1"); Assert.True(page.Showcase[2].IsEmpty); Assert.Equal("Elden Ring", page.Showcase[0].Card!.Name); // moved, not copied
        page.SetShowcase(0, null); Assert.True(page.Showcase[0].IsEmpty);
        var elden = page.Items.Single(i => i.Name == "Elden Ring"); Assert.True(elden.IsPlatinum && elden.IsPerfect);
        Assert.Equal(0.42, page.Items.Single(i => i.Name == "Hades").CoverOpacity);
        Assert.True(page.HeaderCompact); page.HeaderCompact = false; Assert.False(page.HeaderCompact);
    }
}
