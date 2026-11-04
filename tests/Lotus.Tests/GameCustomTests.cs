using Lotus.Core;
using Lotus.Core.Games;
using Lotus.Core.Saves;
using Lotus.Infrastructure.Games;
using Xunit;

namespace Lotus.Tests;

// Games added by hand (with or without an .exe), the player's own names and covers, removing, and the save page's
// manual folder and art for uninstalled games. Only temporary folders.
public sealed class GameCustomTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "lotus-custom-" + Guid.NewGuid().ToString("N"));
    public GameCustomTests() => Directory.CreateDirectory(dir);
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

    private sealed class FakeLibrary(params GameEntry[] games) : IGameLibrary
    {
        // Same rule as the real scan: a manual game stays when it has no .exe, or its .exe still exists.
        public LibraryScan Scan(IReadOnlyList<GameEntry> manual) => new([.. games, .. manual.Where(m => m.ExePath is null || File.Exists(m.ExePath))], []);
    }
    private sealed class NoLauncher : IGameLauncher { public void Launch(GameEntry game) { } }

    private string Image(string name) { var p = Path.Combine(dir, name); File.WriteAllBytes(p, [0x89, 0x50, 0x4E, 0x47]); return p; }

    [Fact] public async Task GamesAddedByNameKeepTheirCoverAndSurviveARestart()
    {
        var storeFile = Path.Combine(dir, "Library", "games.json");
        var vm = new GamesViewModel(new FakeLibrary(), new JsonManualGameStore(storeFile), new NoLauncher(), new TextCatalog(), ex => throw ex);
        await vm.EnsureLoadedAsync();
        var mine = Image("modrinth.png");
        var added = await vm.AddCustomAsync("Minecraft (Modrinth)", null, mine, null);
        Assert.NotNull(added); Assert.Null(added!.ExePath); Assert.StartsWith("custom-", added.Id);
        Assert.NotEqual(mine, added.CoverPath); Assert.True(File.Exists(added.CoverPath));           // copied into Lotus's folder
        var steam = await vm.AddCustomAsync("Hades", null, "https://example.com/hades.jpg", "1145360");
        Assert.Equal("steam://run/1145360", steam!.LaunchUri); Assert.Equal("https://example.com/hades.jpg", steam.CoverPath);
        var exe = Path.Combine(dir, "Game.exe"); File.WriteAllText(exe, "");
        var withExe = await vm.AddCustomAsync("", exe, null, null);
        Assert.Equal("Game", withExe!.Name);
        // Lotus closes and opens again: everything is still there.
        var again = new GamesViewModel(new FakeLibrary(), new JsonManualGameStore(storeFile), new NoLauncher(), new TextCatalog(), ex => throw ex);
        await again.EnsureLoadedAsync();
        var names = again.InstalledGames().Select(g => g.Name).ToList();
        Assert.Contains("Minecraft (Modrinth)", names); Assert.Contains("Hades", names); Assert.Contains("Game", names);
        Assert.True(File.Exists(again.InstalledGames().Single(g => g.Name.StartsWith("Minecraft")).CoverPath));
        Assert.Null(await vm.AddCustomAsync("", Path.Combine(dir, "missing.exe"), null, null));   // an .exe that does not exist
    }

    [Fact] public async Task AnyGameCanGetItsOwnNameAndCoverAndGoBack()
    {
        var steamGame = new GameEntry(GameStores.Steam, "620", "Portal 2", CoverPath: "https://steam/portal2.jpg");
        var storeFile = Path.Combine(dir, "Library", "games.json");
        var vm = new GamesViewModel(new FakeLibrary(steamGame), new JsonManualGameStore(storeFile), new NoLauncher(), new TextCatalog(), ex => throw ex);
        await vm.EnsureLoadedAsync();
        Assert.True(await vm.CustomizeAsync(steamGame, "Portal 2 (co-op)", Image("p2.png"), resetCover: false));
        var shown = vm.InstalledGames().Single();
        Assert.Equal("Portal 2 (co-op)", shown.Name); Assert.EndsWith(".png", shown.CoverPath); Assert.Equal("Portal 2", vm.OriginalName(shown));
        // After a restart the custom name and cover are applied again.
        var again = new GamesViewModel(new FakeLibrary(steamGame), new JsonManualGameStore(storeFile), new NoLauncher(), new TextCatalog(), ex => throw ex);
        await again.EnsureLoadedAsync();
        Assert.Equal("Portal 2 (co-op)", again.InstalledGames().Single().Name);
        // Back to the original name (blank) and cover.
        Assert.True(await again.CustomizeAsync(steamGame, "  ", null, resetCover: true));
        Assert.Equal("Portal 2", again.InstalledGames().Single().Name); Assert.Equal("https://steam/portal2.jpg", again.InstalledGames().Single().CoverPath);
    }

    [Fact] public async Task RemovingDeletesNameOnlyGamesAndHidesTheRest()
    {
        var steamGame = new GameEntry(GameStores.Steam, "620", "Portal 2");
        var storeFile = Path.Combine(dir, "Library", "games.json");
        var vm = new GamesViewModel(new FakeLibrary(steamGame), new JsonManualGameStore(storeFile), new NoLauncher(), new TextCatalog(), ex => throw ex);
        await vm.EnsureLoadedAsync();
        var custom = await vm.AddCustomAsync("Old launcher game", null, null, null);
        await vm.RemoveAsync(custom!);
        await vm.RemoveAsync(steamGame);
        Assert.Empty(vm.InstalledGames());
        Assert.Contains(vm.Removed, g => g.Key == steamGame.Key);                // Steam game: hidden, can come back
        Assert.DoesNotContain(vm.Removed, g => g.Key == custom!.Key);            // added by name: gone for good
    }

    [Fact] public async Task TheSaveFolderCanAlwaysBePickedByHand()
    {
        var protection = new SaveProtection(new MemorySettings(), new SaveVault(Path.Combine(dir, "Vault")), new SaveDiscovery(new NoFolders()), ex => throw ex);
        var game = new GameEntry(GameStores.Manual, "custom-mc", "Minecraft (Modrinth)");
        var page = new GameSavesViewModel(protection, game, new TextCatalog(), _ => false);
        await page.LoadAsync();
        Assert.Equal(ProtectionState.NotFound, page.State);
        Assert.True(page.CanPick);                                               // the button is on when Lotus found nothing
        var saves = Path.Combine(dir, "saves", "World"); Directory.CreateDirectory(saves); File.WriteAllText(Path.Combine(saves, "level.dat"), "world");
        Assert.True(await page.UseFolderAsync(Path.Combine(dir, "saves")));
        Assert.True(page.IsOn); Assert.Single(page.Backups);
    }

    [Fact] public async Task UninstalledProtectedGamesKeepTheirArt()
    {
        var settings = new MemorySettings();
        var vault = new SaveVault(Path.Combine(dir, "Vault"));
        var protection = new SaveProtection(settings, vault, new SaveDiscovery(new NoFolders()), ex => throw ex);
        var saves = Path.Combine(dir, "s"); Directory.CreateDirectory(saves); File.WriteAllText(Path.Combine(saves, "a.sav"), "x");
        var withCover = new GameEntry(GameStores.Steam, "1", "Covered", CoverPath: "https://art/covered.jpg");
        var noCover = new GameEntry(GameStores.Steam, "2", "Old game");
        await protection.EnableAsync(withCover, protection.Discovery.Manual(saves)!, true, 5);
        await protection.EnableAsync(noCover, protection.Discovery.Manual(saves)!, true, 5);
        var page = new SaveLibraryViewModel(protection, () => [], new TextCatalog(), _ => false) { CoverLookup = id => Task.FromResult<string?>(id == "2" ? "https://art/exact-2.jpg" : null) };
        await page.LoadAsync();
        Assert.Equal("https://art/covered.jpg", page.Items.Single(t => t.Game.Id == "1").CoverPath);   // kept when protection was turned on
        Assert.Equal("https://art/exact-2.jpg", page.Items.Single(t => t.Game.Id == "2").CoverPath);   // looked up for older settings
    }

    private sealed class MemorySettings : ISaveSettingsStore { private SaveProtectionData data = new(); public SaveProtectionData Load() => data; public void Save(SaveProtectionData d) => data = d; }
    private sealed class NoFolders : IKnownFolders { public string? Get(string token) => null; public string? SteamPath() => null; }

    [Fact] public async Task AStoreGameAlreadyInTheLibraryIsNotAddedTwice()
    {
        var palworld = new GameEntry(GameStores.Steam, "1623730", "Palworld");
        var vm = new GamesViewModel(new FakeLibrary(palworld), new JsonManualGameStore(Path.Combine(dir, "Library", "games.json")), new NoLauncher(), new TextCatalog(), ex => throw ex);
        await vm.EnsureLoadedAsync();
        var result = await vm.AddCustomAsync("Palworld", null, "https://art/p.jpg", "1623730");
        Assert.Equal(palworld.Key, result!.Key);
        Assert.Single(vm.InstalledGames());
    }

    [Fact] public async Task RemovingANameOnlyGameDeletesItsCopiedCover()
    {
        var vm = new GamesViewModel(new FakeLibrary(), new JsonManualGameStore(Path.Combine(dir, "Library", "games.json")), new NoLauncher(), new TextCatalog(), ex => throw ex);
        await vm.EnsureLoadedAsync();
        var added = await vm.AddCustomAsync("Temp", null, Image("t.png"), null);
        Assert.True(File.Exists(added!.CoverPath));
        await vm.RemoveAsync(added);
        Assert.False(File.Exists(added.CoverPath));
        Assert.True(File.Exists(Path.Combine(dir, "t.png")));                       // the player's own file is never touched
    }
}