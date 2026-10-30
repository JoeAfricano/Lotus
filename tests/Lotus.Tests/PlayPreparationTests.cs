using Lotus.Core;
using Lotus.Core.Games;
using Lotus.Infrastructure.Games;
using Xunit;

namespace Lotus.Tests;

// "Preparar e Jogar": quick checks, one question only for real problems, profile applied after the answer, restore.
public sealed class PlayPreparationTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "lotus-prep-" + Guid.NewGuid().ToString("N"));
    public PlayPreparationTests() => Directory.CreateDirectory(dir);
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

    private sealed class Probes : IPrepareProbes
    {
        public bool Present = true; public long? Free = 500L * 1024 * 1024 * 1024; public (double, IReadOnlyList<string>)? Mem = (40, []);
        public int? Pads = 1; public int Delay;
        public bool InstallPresent(GameEntry game) { if (Delay > 0) Thread.Sleep(Delay); return Present; }
        public long? FreeBytesFor(GameEntry game) => Free;
        public (double LoadPercent, IReadOnlyList<string> TopApps)? Memory(GameEntry game) => Mem;
        public int? Controllers() => Pads;
    }
    private sealed class Profile(int pending, bool applies = true) : IGameProfile
    {
        public int Applied, Undone, Previews;
        public Task<IReadOnlyList<ProfileStep>> PreviewAsync() { Previews++; return Task.FromResult<IReadOnlyList<ProfileStep>>([.. Enumerable.Range(0, 3).Select(i => new ProfileStep("S" + i, "a", "b", i >= pending - Applied * pending))]); }
        public Task<bool> ApplyAsync() { Applied++; return Task.FromResult(applies); }
        public Task<bool> UndoAsync() { Undone++; return Task.FromResult(true); }
    }
    private sealed class PrefsMemory : IGamePrefsStore { public GamePrefsData Data = new(); public GamePrefsData Load() => Data; public void Save(GamePrefsData d) => Data = d; }
    private sealed class Library(GameEntry game) : IGameLibrary { public GameEntry Game = game; public LibraryScan Scan(IReadOnlyList<GameEntry> manual) => new([Game], []); }
    private sealed class NoStore : IManualGameStore { public IReadOnlyList<GameEntry> Load() => []; public void Save(IReadOnlyList<GameEntry> g) { } }
    private sealed class Launcher : IGameLauncher { public int Launched; public void Launch(GameEntry game) => Launched++; }

    private static readonly GameEntry Hades = new(GameStores.Steam, "1145360", "Hades", LaunchUri: "steam://rungameid/1145360");

    [Fact] public async Task QuietWhenAllIsWellAndClearWhenSomethingIsWrong()
    {
        var probes = new Probes(); var prep = new PlayPreparation(probes, new TextCatalog(), ex => throw ex);
        var ok = await prep.CheckAsync(Hades, new GamePlayPrefs());
        Assert.Empty(ok.Findings); Assert.False(ok.HasWarnings);

        probes.Present = false;
        var missing = await prep.CheckAsync(Hades, new GamePlayPrefs());
        Assert.True(missing.Blocked); Assert.Equal("missing", missing.Findings.Single().Id);

        probes.Present = true; probes.Mem = (94, ["Google Chrome", "Discord"]); probes.Free = 1L * 1024 * 1024 * 1024;
        var heavy = await prep.CheckAsync(Hades, new GamePlayPrefs());
        Assert.Equal(["memory", "space"], heavy.Findings.Select(f => f.Id)); Assert.Contains("Google Chrome", heavy.Findings[0].Detail);
        Assert.Empty((await prep.CheckAsync(Hades, new GamePlayPrefs { CheckSystem = false })).Findings); // the player turned it off

        prep.Snooze(Hades.Key, heavy.Findings);                                          // "Jogar mesmo assim"
        Assert.Empty((await prep.CheckAsync(Hades, new GamePlayPrefs())).Findings);       // not repeated soon
        Assert.Equal(2, (await prep.CheckAsync(Hades, new GamePlayPrefs(), now: DateTimeOffset.Now + PlayPreparation.SnoozeFor + TimeSpan.FromMinutes(1))).Findings.Count);
    }

    [Fact] public async Task ControllerAndSavesNeverBlock()
    {
        var probes = new Probes { Pads = 0 }; var prep = new PlayPreparation(probes, new TextCatalog(), ex => throw ex) { SaveWarning = _ => "O último backup falhou." };
        var r = await prep.CheckAsync(Hades, new GamePlayPrefs { CheckController = true });
        Assert.False(r.Blocked);
        Assert.Equal(PrepLevel.Warning, r.Findings.Single(f => f.Id == "controller").Level);
        Assert.Equal(PrepLevel.Info, r.Findings.Single(f => f.Id == "saves").Level);
        Assert.DoesNotContain((await prep.CheckAsync(Hades, new GamePlayPrefs())).Findings, f => f.Id == "controller"); // only when asked for
        probes.Pads = null; // unknown: no warning
        Assert.DoesNotContain((await prep.CheckAsync(Hades, new GamePlayPrefs { CheckController = true })).Findings, f => f.Id == "controller");
    }

    [Fact] public async Task SlowChecksAreSkippedNotWaitedFor()
    {
        var prep = new PlayPreparation(new Probes { Delay = 3000 }, new TextCatalog(), ex => throw ex);
        var r = await prep.CheckAsync(Hades, new GamePlayPrefs());
        Assert.True(r.Took < TimeSpan.FromSeconds(2)); Assert.False(r.Blocked); // an unknown answer never blocks the game
    }

    [Fact] public async Task ProfileOnlyWhenSomethingIsPending()
    {
        var pending = new Profile(pending: 2); var prep = new PlayPreparation(new Probes(), new TextCatalog(), ex => throw ex) { Profile = pending };
        var (applied, finding) = await prep.ApplyProfileAsync();
        Assert.True(applied); Assert.Equal(1, pending.Applied); Assert.Equal(PrepLevel.Info, finding!.Level);
        var ready = new Profile(pending: 0); prep.Profile = ready;
        var (again, none) = await prep.ApplyProfileAsync();
        Assert.False(again); Assert.Null(none); Assert.Equal(0, ready.Applied); // already applied: never reapplied
        var partial = new Profile(pending: 2, applies: false); prep.Profile = partial;
        Assert.Equal(PrepLevel.Warning, (await prep.ApplyProfileAsync()).Finding!.Level);
    }

    private static async Task<(GamesViewModel Vm, Launcher Launcher, Probes Probes, GamePrefsBook Book, Profile Profile, List<PrepResult> Asked)> Setup(PrepDecision answer, bool useProfile = false, Action<Exception>? log = null)
    {
        var launcher = new Launcher(); var probes = new Probes(); var profile = new Profile(pending: 2);
        var book = new GamePrefsBook(new PrefsMemory(), ex => throw ex);
        if (useProfile) book.Set(Hades.Key, new GamePlayPrefs { UseProfile = true });
        var asked = new List<PrepResult>();
        var vm = new GamesViewModel(new Library(Hades), new NoStore(), launcher, new TextCatalog(), log ?? (ex => throw ex))
        {
            GameProfile = profile, PlayPrefs = book,
            Preparation = new PlayPreparation(probes, new TextCatalog(), ex => throw ex) { Profile = profile },
            AskBeforePlay = (_, r) => { asked.Add(r); return Task.FromResult(answer); },
        };
        await vm.EnsureLoadedAsync();
        return (vm, launcher, probes, book, profile, asked);
    }
    private static async Task Settle() { for (var i = 0; i < 50; i++) await Task.Delay(20); }

    [Fact] public async Task EveryPlayButtonGoesThroughTheSameLauncher()
    {
        var (vm, launcher, _, _, _, asked) = await Setup(PrepDecision.Play);
        vm.Play(Hades); await Settle();
        Assert.Equal(1, launcher.Launched); Assert.Empty(asked);   // nothing wrong: no question, the game just opens
        vm.Games[0].Launch.Execute(vm.Games[0]); await Settle();
        Assert.Equal(2, launcher.Launched);
    }

    [Fact] public async Task CancelKeepsTheGameClosedAndNothingApplied()
    {
        var (vm, launcher, probes, book, profile, asked) = await Setup(PrepDecision.Cancel, useProfile: true);
        probes.Mem = (95, ["Chrome"]);
        vm.Play(Hades); await Settle();
        Assert.Single(asked); Assert.Equal(0, launcher.Launched); Assert.Equal(0, profile.Applied); Assert.Empty(book.PendingRestore);
    }

    [Fact] public async Task PlayAnywayAppliesTheAuthorizedProfileThenRestoresAfter()
    {
        var (vm, launcher, probes, book, profile, _) = await Setup(PrepDecision.Play, useProfile: true);
        probes.Mem = (95, ["Chrome"]);
        (GameEntry, PrepResult, bool)? prepared = null; vm.Prepared += (_, e) => prepared = e;
        vm.Play(Hades); await Settle();
        Assert.Equal(1, launcher.Launched); Assert.Equal(1, profile.Applied);
        Assert.True(prepared!.Value.Item2.ProfileApplied); Assert.True(book.PendingRestore.ContainsKey(Hades.Key));
        Assert.True(await vm.RestoreAfterSessionAsync(Hades.Key));
        Assert.Equal(1, profile.Undone); Assert.Empty(book.PendingRestore);
        Assert.Null(await vm.RestoreAfterSessionAsync(Hades.Key)); // nothing pending: nothing touched
        Assert.Equal(1, profile.Undone);
    }

    [Fact] public async Task TurnedOffOrFailingPreparationStillOpensTheGame()
    {
        var (vm, launcher, _, book, _, asked) = await Setup(PrepDecision.Cancel);
        book.Set(Hades.Key, new GamePlayPrefs { Prepare = false });
        vm.Play(Hades); await Settle();
        Assert.Equal(1, launcher.Launched); Assert.Empty(asked);
        var errors = 0;
        var (vm2, launcher2, probes2, _, _, _) = await Setup(PrepDecision.Play, log: _ => errors++);
        probes2.Present = false;
        vm2.AskBeforePlay = (_, _) => throw new InvalidOperationException("dialog failed");
        vm2.Play(Hades); await Settle();
        Assert.Equal(1, errors);
        Assert.Equal(1, launcher2.Launched); // the optional part failed: the game opened normally
    }

    [Fact] public async Task SharedSessionHubWaitsWithoutPolling()
    {
        var hub = new SessionHub(); var start = DateTimeOffset.Now;
        var wait = hub.WaitForSessionAsync(Hades, TimeSpan.FromMinutes(3), CancellationToken.None);
        hub.Observe(null, start);
        hub.Observe(Hades, start.AddSeconds(10));
        hub.Observe(Hades, start.AddMinutes(30));
        hub.Observe(null, start.AddMinutes(42));
        Assert.Equal(TimeSpan.FromMinutes(42) - TimeSpan.FromSeconds(10), await wait);
        var never = hub.WaitForSessionAsync(Hades, TimeSpan.FromMilliseconds(1), CancellationToken.None);
        hub.Observe(null, DateTimeOffset.Now.AddMinutes(5));
        Assert.Null(await never); // the game never started
    }

    [Fact] public void PrefsSurviveRestartAndBrokenFiles()
    {
        var path = Path.Combine(dir, "game-prefs.json");
        var book = new GamePrefsBook(new JsonGamePrefsStore(path), ex => throw ex);
        book.Set("Steam:1", new GamePlayPrefs { UseProfile = true, CheckController = true });
        book.MarkRestore("Steam:1", DateTimeOffset.Now);
        book.Set("Steam:2", new GamePlayPrefs { Prepare = false });
        var again = new GamePrefsBook(new JsonGamePrefsStore(path), ex => throw ex);
        Assert.True(again.Get("steam:1").UseProfile); Assert.True(again.PendingRestore.ContainsKey("Steam:1")); Assert.False(again.Get("Steam:2").Prepare);
        File.WriteAllText(path, "{ quebrado");
        Assert.True(new GamePrefsBook(new JsonGamePrefsStore(path), ex => throw ex).Get("Steam:1").UseProfile); // from the .bak
    }

    [Fact] public void InstallCheckLooksOnlyAtTheGamesOwnFolders()
    {
        var probes = new WindowsPrepareProbes(null);
        var folder = Path.Combine(dir, "Game"); Directory.CreateDirectory(folder);
        var exe = Path.Combine(folder, "game.exe"); File.WriteAllText(exe, "");
        Assert.True(probes.InstallPresent(new GameEntry(GameStores.Gog, "1", "X", folder, exe)));
        Assert.True(probes.InstallPresent(Hades)); // store URI only
        Directory.Delete(folder, true);
        Assert.False(probes.InstallPresent(new GameEntry(GameStores.Gog, "1", "X", folder, exe)));
        Assert.NotNull(probes.FreeBytesFor(new GameEntry(GameStores.Gog, "1", "X", dir)));
    }

    // Files written before a setting existed keep its default (memory/space checks stay on, no "all good" notices).
    [Fact] public void OlderPrefsFilesKeepTheDefaults()
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "game-prefs.json");
        File.WriteAllText(path, """{"Games":{"Steam:1":{"Prepare":true}}}""");
        var p = new JsonGamePrefsStore(path).Load().Games["Steam:1"];
        Assert.True(p.Prepare); Assert.True(p.CheckSystem); Assert.True(p.OnlyRelevant); Assert.False(p.UseProfile);
    }
}