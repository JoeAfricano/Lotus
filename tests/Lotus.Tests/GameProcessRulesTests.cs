using Lotus.Core.Games;
using Lotus.Infrastructure.PortsLink;
using Xunit;

namespace Lotus.Tests;

public sealed class GameProcessRulesTests
{
    [Theory]
    [InlineData(@"D:\SteamLibrary\steamapps\common\wallpaper_engine\wallpaper64.exe", true)]
    [InlineData(@"C:\Program Files (x86)\Steam\steam.exe", true)]
    [InlineData(@"D:\SteamLibrary\steamapps\common\Hades\x64\Hades.exe", false)]
    [InlineData(@"C:\Users\a\AppData\Local\Modrinth App\Modrinth App.exe", false)]
    public void Always_on_apps_never_count_as_a_game(string path, bool expected) => Assert.Equal(expected, GameProcessRules.AlwaysOn(path));

    [Theory]
    [InlineData(@"C:\", true)]
    [InlineData(@"D:\SteamLibrary\steamapps\common\", true)]
    [InlineData(@"C:\Program Files\", true)]
    [InlineData(@"C:\Users\junior\", true)]
    [InlineData(@"D:\SteamLibrary\steamapps\common\Hades\", false)]
    [InlineData(@"C:\Users\junior\AppData\Local\Modrinth App\", false)]
    public void Whole_libraries_and_drives_are_not_one_game(string dir, bool expected) => Assert.Equal(expected, GameProcessRules.TooBroad(dir));

    // Gothic 1 Remake, 28/11/2026: the hidden error window kept a 151-minute ghost session going.
    [Theory]
    [InlineData(@"D:\Games\Gothic 1 Remake\Engine\Binaries\Win64\CrashReportClient.exe", ProcessRole.CrashReporter)]
    [InlineData(@"D:\Games\Some Unity Game\UnityCrashHandler64.exe", ProcessRole.CrashReporter)]
    [InlineData(@"D:\Games\Gothic 1 Remake\Engine\Binaries\Win64\UnrealCEFSubProcess.exe", ProcessRole.Helper)]
    [InlineData(@"D:\Games\Fortnite\EpicWebHelper.exe", ProcessRole.Helper)]
    [InlineData(@"D:\Games\Gothic 1 Remake\G1R\Binaries\Win64\G1R-Win64-Shipping.exe", ProcessRole.Game)]
    [InlineData("", ProcessRole.Helper)]
    public void Helpers_and_crash_reporters_are_not_the_game(string path, ProcessRole expected) => Assert.Equal(expected, GameProcessRules.Role(path));

    [Fact]
    public void Small_unreal_stub_at_the_root_is_not_the_game()
    {
        const string stub = @"D:\Games\Gothic 1 Remake\G1R-Win64-Shipping.exe", real = @"D:\Games\Gothic 1 Remake\G1R\Binaries\Win64\G1R-Win64-Shipping.exe";
        (string, long)[] both = [(stub, 300_000), (real, 180_000_000)];
        Assert.True(GameProcessRules.IsLauncherStub(stub, 300_000, both));
        Assert.False(GameProcessRules.IsLauncherStub(real, 180_000_000, both));             // the real one is big
        Assert.False(GameProcessRules.IsLauncherStub(stub, 300_000, [(stub, 300_000)]));    // a small game with no bigger twin is a game
        Assert.False(GameProcessRules.IsLauncherStub(stub, 300_000, [(stub, 300_000), (@"D:\Games\Gothic 1 Remake\Backup\G1R-Win64-Shipping.exe", 9_000_000)])); // twin outside Binaries
        Assert.Equal(ProcessRole.Helper, GameProcessRules.Role(stub, p => GameProcessRules.IsLauncherStub(p, 300_000, both)));
    }

    // Gothic, 27/11/2026: "Sessão registrada no diário" stayed over the whole next session.
    [Fact]
    public void Balloons_wait_while_a_game_is_open_and_go_out_together()
    {
        var shown = new List<(string, string)>(); var q = new NoticeQueue((t, b) => shown.Add((t, b)), capacity: 2);
        q.Notify("A", "1"); Assert.Single(shown);              // no game: right away
        Assert.True(q.SetGameOpen(true));                       // just opened: the caller clears a balloon on screen
        Assert.False(q.SetGameOpen(true));
        q.Notify("Sessão", "2"); q.Notify("Sessão", "2");       // a repeat is not queued twice
        q.Notify("Preço", "3"); q.Notify("Driver", "4");        // full: the oldest goes
        Assert.Single(shown); Assert.Equal(2, q.Waiting);
        q.SetGameOpen(false);
        Assert.Equal(("Driver", "4\n3"), shown[1]);             // one balloon, newest first
        Assert.Equal(0, q.Waiting);
        q.SetGameOpen(false); Assert.Equal(2, shown.Count);     // nothing left to show
    }

    [Fact]
    public void A_crashed_session_is_not_a_normal_close()
    {
        var s = new Lotus.Core.Diary.FinishedSession(new GameEntry(GameStores.Steam, "1", "Gothic"), DateTimeOffset.Now, 20, Lotus.Core.Diary.SessionEnd.Crashed);
        Assert.False(s.EndedNormally); // no "Onde eu parei?" prompt after a crash
    }

    [Fact]
    public void Tray_status_joins_its_lines_and_says_when_it_changes()
    {
        var status = new TrayStatus(); var seen = new List<string?>();
        status.Changed += (_, line) => seen.Add(line);
        status.Set("crash", "Gothic travou");
        status.Set("console", "Modo Console não está ativo");
        status.Set("console", "Modo Console não está ativo"); // same text: no event
        Assert.Equal("Gothic travou · Modo Console não está ativo", status.Line);
        status.Clear("crash");
        Assert.Equal("Modo Console não está ativo", status.Line);
        status.Clear("console");
        Assert.Null(status.Line);
        Assert.Equal(4, seen.Count);
    }

    [Fact]
    public void Ports_catalog_gives_key_and_cover()
    {
        var hit = PortsCatalogCovers.Parse("""{"id":"sonic-unleashed","lotusGameKey":"port:sonic-unleashed","cover":{"url":"https://cdn2.steamgriddb.com/grid/x.png","source":"steamgriddb"}}""");
        Assert.Equal(("port:sonic-unleashed", "https://cdn2.steamgriddb.com/grid/x.png"), hit);
        Assert.Null(PortsCatalogCovers.Parse("""{"id":"x","cover":{"url":"https://a/b.png"}}"""));
        Assert.Null(PortsCatalogCovers.Parse("not json"));
    }

    [Fact]
    public void Ports_catalog_folder_next_to_the_exe_is_read()
    {
        var root = Path.Combine(Path.GetTempPath(), "lotus-ports-" + Guid.NewGuid().ToString("N"));
        var games = Path.Combine(root, "catalog", "games");
        Directory.CreateDirectory(games);
        try
        {
            File.WriteAllText(Path.Combine(games, "sonic.json"), """{"lotusGameKey":"port:sonic-unleashed","cover":{"url":"https://a/s.png"}}""");
            var covers = PortsCatalogCovers.Read(Path.Combine(root, "LotusPorts.exe"));
            Assert.Equal("https://a/s.png", covers["port:sonic-unleashed"]);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Ports_catalog_bundle_lists_every_game()
    {
        var all = PortsCatalogCovers.ParseAll("""{"version":3,"games":[{"id":"sonic-unleashed","title":"Sonic Unleashed","cover":{"url":"https://cdn2.steamgriddb.com/grid/b.png","source":"steamgriddb"}},{"id":"other","lotusGameKey":"port:other-key","cover":{"url":"https://a/o.png"}},{"id":"nocover"}]}""");
        Assert.Equal([("port:sonic-unleashed", "https://cdn2.steamgriddb.com/grid/b.png"), ("port:other-key", "https://a/o.png")], all);
    }

    [Fact]
    public void Embedded_resource_is_read_without_loading_the_assembly()
    {
        var assembly = typeof(GameProcessRules).Assembly;
        var name = assembly.GetManifestResourceNames().First();
        using var expected = assembly.GetManifestResourceStream(name)!;
        using var copy = new MemoryStream(); expected.CopyTo(copy);
        var text = PortsCatalogCovers.ResourceText(assembly.Location, name);
        Assert.NotNull(text);
        Assert.Equal(System.Text.Encoding.UTF8.GetString(copy.ToArray()).TrimStart('\uFEFF'), text);
        Assert.Null(PortsCatalogCovers.ResourceText(assembly.Location, "missing.json"));
    }
}
