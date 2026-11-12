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
