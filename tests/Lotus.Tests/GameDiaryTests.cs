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

    [Fact] public void EditorKeepsCategoriesMoodsDifficultyAndPlaythroughs()
    {
        var diary = new GameDiary(new Memory(), ex => throw ex); var text = new Lotus.Core.TextCatalog();
        var editor = new DiaryEditorViewModel(diary, text);
        editor.Open(new DiaryEntry { Key = "steam:7", Name = "Hollow Knight" });
        Assert.Null(editor.CategoryAverage);
        editor.Categories.Single(c => c.Id == "story").Value = 8; editor.Categories.Single(c => c.Id == "gameplay").Value = 9.5; editor.NotifyCategories();
        Assert.Equal(9, editor.CategoryAverage);                                   // 8.75 rounded to the half step
        editor.UseCategoryAverage(); Assert.Equal(9, editor.Rating); Assert.Equal(text["DyLabelGreat"], editor.RatingLabel);
        editor.Moods.Single(m => m.Id == "challenging").Selected = true; editor.Difficulty = "hard";
        editor.AddRun(); editor.Runs[0].Hours = 12;
        Assert.True(editor.IsDirty);
        editor.Save();
        var saved = diary.Get("steam:7")!;
        Assert.Equal(2, saved.Categories.Count); Assert.Equal(["challenging"], saved.Moods); Assert.Equal("hard", saved.Difficulty);
        Assert.Equal(720, saved.Runs.Single().Minutes);
        editor.Open(saved); Assert.False(editor.IsDirty); Assert.Single(editor.Runs); Assert.Equal(9.5, editor.Categories.Single(c => c.Id == "gameplay").Value);
    }

    private static DiaryEntry E(string id, string name, params string[] genres) => new() { Key = DiaryEntry.SteamKey(id), AppId = id, Name = name, Genres = [.. genres] };
    [Fact] public void StatsSpreadHoursOverMonthsAndBuildTheYearRecap()
    {
        var diary = new GameDiary(new Memory(), ex => throw ex);
        var a = E("1", "Hades", "Ação", "Roguelike"); a.Status = DiaryStatus.Completed; a.Rating = 9.5; a.Minutes = 1200;
        a.Started = new DateTime(2026, 1, 10); a.Finished = new DateTime(2026, 3, 5); a.Moods = ["addictive"]; a.Categories = new() { ["gameplay"] = 10 }; a.Difficulty = "hard";
        var b = E("2", "Celeste", "Plataforma"); b.Status = DiaryStatus.Completed; b.Rating = 7; b.Minutes = 600; b.Started = new DateTime(2026, 3, 1); b.Finished = new DateTime(2026, 3, 20); b.Platform = "Switch";
        var c = E("3", "Antigo", "Ação"); c.Status = DiaryStatus.Completed; c.Rating = 4; c.Minutes = 300; c.Started = new DateTime(2024, 5, 1); c.Finished = new DateTime(2024, 5, 9);
        foreach (var e in new[] { a, b, c }) diary.Save(e);

        var s = DiaryStats.Compute(diary.Entries, 2026, new DateTime(2026, 10, 30));
        Assert.Equal(3, s.Games); Assert.Equal(3, s.Completed); Assert.Equal(2100, s.TotalMinutes);
        Assert.Equal([400, 400, 1000], s.MonthMinutes.Take(3)); Assert.Equal(0, s.MonthMinutes[4]); // Hades over Jan–Mar, Celeste in March
        Assert.Equal(1800, s.YearMinutes); Assert.Equal(3, s.PeakMonth);
        Assert.Equal(1, s.RatingBuckets[9]); Assert.Equal(1, s.RatingBuckets[6]); Assert.Equal(1, s.RatingBuckets[3]);
        Assert.Equal(("Ação", 2), s.Genres[0]);
        Assert.Equal(2, s.YearPlayed); Assert.Equal(2, s.YearCompleted); Assert.Equal("Hades", s.BestGame?.Name); Assert.Equal("Hades", s.MostPlayed?.Name);
        Assert.Equal("addictive", s.TopMood); Assert.Equal([2026, 2024], s.Years);
        Assert.Equal(10, s.Categories.Single().Average); Assert.Equal(("hard", 1), s.Difficulties.Single());

        var text = new Lotus.Core.TextCatalog();
        var stats = new DiaryStatsViewModel(diary, text); stats.Year = 2026; stats.Refresh();
        Assert.Equal(12, stats.MonthBars.Count); Assert.True(stats.MonthBars[2].Highlight); Assert.True(stats.MonthBars[2].ShowValue); Assert.False(stats.MonthBars[0].ShowValue);
        Assert.Equal(6, stats.Tiles.Count); Assert.True(stats.HasCategories); Assert.True(stats.HasMoods);

        var recap = new DiaryRecapViewModel(diary, text); recap.Open(2026, "Ana", new DateTime(2026, 10, 30));
        Assert.Equal(["intro", "hours", "games", "genre", "most", "best", "mood", "summary"], recap.Slides.Select(x => x.Id));
        Assert.Equal(30, recap.Slides.Single(x => x.Id == "hours").CountTo);
        recap.Open(2025, "Ana", new DateTime(2026, 10, 30));
        Assert.Equal(["intro", "empty", "summary"], recap.Slides.Select(x => x.Id));
    }

    [Fact] public void EpicCatalogGivesTheWideKeyArt()
    {
        var json = """[{"title":"Game","releaseInfo":[{"appId":"Fortune"}],"keyImages":[{"type":"DieselGameBoxTall","url":"https://x/tall.jpg"},{"type":"DieselGameBox","url":"https://x/wide.jpg"}]},{"title":"NoArt","releaseInfo":[{"appId":"Plain"}],"keyImages":[]}]""";
        var map = new Dictionary<string, string>();
        Lotus.Infrastructure.Games.StoreArt.ParseEpicCatalog(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json)), map);
        Assert.Equal("https://x/wide.jpg", map["Fortune"]); Assert.False(map.ContainsKey("Plain"));
    }

    [Fact] public void SessionTrackerCountsRealPlayAndSkipsSleepAndShortRuns()
    {
        var t = new PlaySessionTracker(); var g = new Lotus.Core.Games.GameEntry("Steam", "10", "Hades");
        var start = new DateTimeOffset(2026, 10, 1, 20, 0, 0, TimeSpan.Zero);
        Assert.Null(t.Update(g, start));
        for (var m = 1; m <= 30; m++) Assert.Null(t.Update(g, start.AddMinutes(m)));
        var done = t.Update(null, start.AddMinutes(31));
        Assert.Equal(30, done?.Minutes); Assert.Equal("Hades", done?.Game.Name);
        // Sleep: seen at 0 and 5, then 2 hours later → first session ends at the last time it was seen.
        for (var m = 0; m <= 5; m++) Assert.Null(t.Update(g, start.AddHours(3).AddMinutes(m)));
        Assert.Equal(5, t.Update(g, start.AddHours(5))?.Minutes);
        // Quick launch (1 minute) is not a session.
        Assert.Null(t.Update(null, start.AddHours(5).AddMinutes(1)));
        Assert.Null(t.Flush());
    }

    [Fact] public void RecordedSessionsStartTheGameAndLandInTheirMonth()
    {
        var diary = new GameDiary(new Memory(), ex => throw ex);
        var g = new Lotus.Core.Games.GameEntry("Steam", "10", "Hades");
        Assert.Equal("steam:10", DiaryGames.KeyFor(g));
        diary.RecordSession(DiaryGames.FromGame(g), new DateTime(2026, 2, 3, 20, 0, 0), 90);
        var e = diary.Get("steam:10")!;
        Assert.Equal(DiaryStatus.Playing, e.Status); Assert.Equal(90, e.Minutes); Assert.Equal(new DateTime(2026, 2, 3), e.Started); Assert.Single(e.Sessions);
        var done = e.Clone(); done.Status = DiaryStatus.Completed; diary.Save(done);
        diary.RecordSession(DiaryGames.FromGame(g), new DateTime(2026, 5, 1), 30);
        Assert.Equal(DiaryStatus.Completed, diary.Get("steam:10")!.Status); // finished games keep their status
        var s = DiaryStats.Compute(diary.Entries, 2026, new DateTime(2026, 10, 30));
        Assert.Equal(90, s.MonthMinutes[1]); Assert.Equal(30, s.MonthMinutes[4]); Assert.Equal(120, s.YearMinutes);
    }

    [Fact] public void PlayNextFitsTimeVibeAndTaste()
    {
        var playing = E("1", "Hades", "Ação", "Roguelike"); playing.Status = DiaryStatus.Playing;
        var want = E("2", "Stardew Valley", "Simulação", "Casual"); want.Status = DiaryStatus.Want; want.Added = DateTimeOffset.Now.AddDays(-90);
        var rpg = E("3", "Persona 5", "RPG"); rpg.Status = DiaryStatus.Want;
        var loved = E("4", "Elden Ring", "RPG", "Ação"); loved.Status = DiaryStatus.Completed; loved.Rating = 10;
        var list = new[] { playing, want, rpg, loved };
        var today = DateTime.Today;
        Assert.Equal("Hades", PlayNext.Suggest(list, PlayTime.Short, "any", today)[0].Entry.Name);
        var relax = PlayNext.Suggest(list, PlayTime.Long, "relax", today)[0];
        Assert.Equal("Stardew Valley", relax.Entry.Name); Assert.Contains(relax.Reasons, r => r.Key == "PnViberelax");
        var story = PlayNext.Suggest(list, PlayTime.Long, "story", today)[0];
        Assert.Equal("Persona 5", story.Entry.Name); Assert.Contains(story.Reasons, r => r.Key == "PnTaste" && r.Arg == "RPG");
        Assert.DoesNotContain(PlayNext.Suggest(list, PlayTime.Long, "any", today, take: 10), p => p.Entry.Name == "Elden Ring");
    }

    [Fact] public void DealsOnlyForWantedGamesOnSale()
    {
        var a = E("1", "A"); a.Status = DiaryStatus.Want; var b = E("2", "B"); b.Status = DiaryStatus.Want; var c = E("3", "C"); c.Status = DiaryStatus.Completed;
        var prices = new Dictionary<string, Lotus.Core.Catalog.GamePrice> { ["1"] = new("1", 0, "R$ 10", ""), ["2"] = new("2", 60, "R$ 40", "R$ 100"), ["3"] = new("3", 80, "R$ 5", "R$ 25") };
        var deals = DiaryDealsViewModel.Pick([a, b, c], prices);
        Assert.Equal("B", deals.Single().Name); Assert.Equal("-60%", deals[0].DiscountText); Assert.Equal("https://store.steampowered.com/app/2", deals[0].StoreUrl);
    }

    [Fact] public void OverlaysFlagHeavyAppsButNotRtss()
    {
        var found = Lotus.Infrastructure.Games.WindowsOverlayScanner.FromProcesses(["Discord", "wallpaper64", "RTSS", "NVIDIA Overlay", "explorer"], discordHardwareAcceleration: true, xboxCapture: true);
        Assert.Equal(["discord", "xbox", "nvidia", "wallpaper"], found.Select(f => f.Id));
        Assert.True(found.Single(f => f.Id == "discord").Heavy);
        Assert.DoesNotContain(Lotus.Infrastructure.Games.WindowsOverlayScanner.FromProcesses(["Discord"], false, false), f => f.Id == "discord");
        Assert.Contains(Lotus.Core.Games.StutterDiagnosis.Analyze(new(60, 20, 30, 60, 50, 60, null, 0, 40, [], false, ["Discord"])), f => f.Key == "WhyOverlays");
        Assert.True(Lotus.Core.Games.GamePriorityRules.IsBackground("chrome")); Assert.False(Lotus.Core.Games.GamePriorityRules.IsBackground("Discord"));
    }
}
