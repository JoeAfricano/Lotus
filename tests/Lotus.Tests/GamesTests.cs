using System.Text;
using Lotus.Core;
using Lotus.Core.Games;
using Lotus.Infrastructure.Games;
using Xunit;
using Xunit.Abstractions;

namespace Lotus.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class GamesTests(ITestOutputHelper output) : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "LotusGamesTests-" + Guid.NewGuid());
    private string Dir(params string[] parts) { var p = Path.Combine([root, .. parts]); Directory.CreateDirectory(p); return p; }
    private string FileAt(string content, params string[] parts) { var p = Path.Combine([root, .. parts]); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, content); return p; }

    private const string Acf = "\"AppState\"\n{\n\t\"appid\"\t\t\"730\"\n\t\"name\"\t\t\"Counter-Strike 2\"\n\t\"StateFlags\"\t\t\"4\"\n\t\"installdir\"\t\t\"Counter-Strike Global Offensive\"\n}";

    [Fact] public void VdfReadsLibrariesAndInstalledManifestsOnly()
    {
        var libs = GameParsers.SteamLibraries("\"libraryfolders\"\n{\n \"0\" { \"path\" \"C:\\\\Program Files (x86)\\\\Steam\" }\n \"1\" { \"path\" \"D:\\\\SteamLibrary\" \"label\" \"\" }\n}");
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], libs);
        Assert.Equal([@"\\nas\games"], GameParsers.SteamLibraries("\"libraryfolders\" { \"0\" { \"path\" \"\\\\\\\\nas\\\\games\" } }")); // UNC share
        Assert.Equal(new SteamApp("730", "Counter-Strike 2", "Counter-Strike Global Offensive"), GameParsers.SteamManifest(Acf));
        Assert.Null(GameParsers.SteamManifest(Acf.Replace("\"4\"", "\"1026\"").Replace("StateFlags\"\t\t\"1026", "StateFlags\"\t\t\"2")));
        Assert.Null(GameParsers.SteamManifest(Acf.Replace("Counter-Strike 2", "Steamworks Common Redistributables")));
    }

    [Fact] public void EpicManifestBuildsLaunchUriAndSkipsNonGames()
    {
        const string game = """{"DisplayName":"Fortnite","InstallLocation":"C:\\Games\\Fortnite","AppName":"Fortnite","CatalogNamespace":"fn","CatalogItemId":"4fe7","LaunchExecutable":"FortniteGame\\Binaries\\Win64\\FortniteLauncher.exe","AppCategories":["public","games","applications"]}""";
        var item = GameParsers.EpicManifest(game)!;
        Assert.Equal("com.epicgames.launcher://apps/fn%3A4fe7%3AFortnite?action=launch&silent=true", item.LaunchUri);
        Assert.Null(GameParsers.EpicManifest(game.Replace("\"games\",", "")));
    }

    [Fact] public void PackageFamilyNameMatchesKnownMicrosoftValue() =>
        Assert.Equal("Microsoft.MinecraftUWP_8wekyb3d8bbwe",
            GameParsers.PackageFamilyName("Microsoft.MinecraftUWP", "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US"));

    [Fact] public void XboxConfigAndGamingRootAreParsed()
    {
        var bytes = new List<byte>(Encoding.ASCII.GetBytes("RGBX")); bytes.AddRange(BitConverter.GetBytes(1)); bytes.AddRange(Encoding.Unicode.GetBytes("XboxGames\0"));
        Assert.Equal(["XboxGames"], GameParsers.GamingRootFolders(bytes.ToArray()));
        Assert.Empty(GameParsers.GamingRootFolders(Encoding.ASCII.GetBytes("nope")));
        var g = GameParsers.MicrosoftGameConfig(XboxConfig("Forza Horizon 5"))!;
        Assert.Equal("Forza Horizon 5", g.Name); Assert.Equal("Game", g.AppId); Assert.Equal("ForzaHorizon5.exe", g.Executable);
        Assert.StartsWith("Microsoft.624F8B84B80_", g.PackageFamilyName);
        Assert.Equal("Microsoft.624F8B84B80", GameParsers.MicrosoftGameConfig(XboxConfig("ms-resource:Title"))!.Name);
    }
    private static string XboxConfig(string display) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <Game configVersion="1"><Identity Name="Microsoft.624F8B84B80" Publisher="CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US" Version="1.0.0.0"/>
        <ExecutableList><Executable Name="ForzaHorizon5.exe" Id="Game"/></ExecutableList>
        <ShellVisuals DefaultDisplayName="{display}" Square150x150Logo="Logo150.png"/></Game>
        """;

    [Fact] public void EaInstallerDataPrefersPortugueseTitle()
    {
        var ea = GameParsers.EaInstallerData("""<DiPManifest><contentIDs><contentID>1234</contentID><contentID>5678</contentID></contentIDs><gameTitles><gameTitle locale="en_US">Battlefield 2042</gameTitle><gameTitle locale="pt_BR">Battlefield™ 2042</gameTitle></gameTitles></DiPManifest>""")!;
        Assert.Equal("Battlefield™ 2042", ea.Name); Assert.Equal(["1234", "5678"], ea.ContentIds);
    }

    [Fact] public void LibraryScansFakeSteamEpicXboxAndKeepsValidManualGames()
    {
        var steam = Dir("Steam");
        FileAt(Acf, "Steam", "steamapps", "appmanifest_730.acf");
        Dir("Steam", "steamapps", "common", "Counter-Strike Global Offensive");
        FileAt("x", "Steam", "appcache", "librarycache", "730", "abc", "library_600x900.jpg");
        var epicDir = Dir("Epic");
        var fortnite = Dir("Fortnite");
        FileAt($$"""{"DisplayName":"Fortnite","InstallLocation":"{{fortnite.Replace("\\", "\\\\")}}","AppName":"Fortnite","CatalogNamespace":"fn","CatalogItemId":"4fe7"}""", "Epic", "a.item");
        var xbox = Dir("XboxGames"); FileAt(XboxConfig("Forza Horizon 5"), "XboxGames", "Forza Horizon 5", "Content", "MicrosoftGame.config");
        var exe = FileAt("", "Manual", "game.exe");
        var manual = new[] { new GameEntry(GameStores.Manual, exe.ToLowerInvariant(), "Meu Jogo", ExePath: exe), new GameEntry(GameStores.Manual, "gone", "Removido", ExePath: Path.Combine(root, "missing.exe")) };
        var scan = new WindowsGameLibrary(steam, epicDir, [xbox]).Scan(manual);
        var cs = Assert.Single(scan.Games, g => g.Store == GameStores.Steam);
        Assert.Equal("steam://rungameid/730", cs.LaunchUri); Assert.EndsWith("library_600x900.jpg", cs.CoverPath);
        Assert.Contains(scan.Games, g => g.Store == GameStores.Epic && g.LaunchUri!.Contains("Fortnite"));
        Assert.Contains(scan.Games, g => g.Store == GameStores.Xbox && g.LaunchUri!.StartsWith(@"shell:AppsFolder\Microsoft.624F8B84B80_") && g.LaunchUri.EndsWith("!Game"));
        Assert.Contains(scan.Games, g => g.Name == "Meu Jogo"); Assert.DoesNotContain(scan.Games, g => g.Name == "Removido");
    }

    [Fact] public void SteamCoverSupportsNewCapsuleNames()
    {
        var steam = Dir("SteamCovers");
        FileAt("x", "SteamCovers", "appcache", "librarycache", "1371980", "724aaa", "library_capsule.jpg");
        FileAt("x", "SteamCovers", "appcache", "librarycache", "1371980", "69044d", "library_header.jpg");
        Assert.EndsWith("library_capsule.jpg", WindowsGameLibrary.SteamCover(steam, "1371980"));
        FileAt("x", "SteamCovers", "appcache", "librarycache", "42", "abc", "library_header.jpg");
        Assert.EndsWith("library_header.jpg", WindowsGameLibrary.SteamCover(steam, "42"));
        Assert.Null(WindowsGameLibrary.SteamCover(steam, "999"));
    }

    [Theory]
    [InlineData("steam://rungameid/730", true)]
    [InlineData("com.epicgames.launcher://apps/x?action=launch", true)]
    [InlineData("uplay://launch/1/0", true)]
    [InlineData("origin2://game/launch?offerIds=1", true)]
    [InlineData(@"shell:AppsFolder\Microsoft.X_8wekyb3d8bbwe!Game", true)]
    [InlineData(@"shell:AppsFolder\x"" & calc", false)]
    [InlineData("cmd.exe /c calc", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    public void LauncherOnlyAllowsKnownSchemes(string uri, bool allowed) => Assert.Equal(allowed, WindowsGameLauncher.IsAllowedUri(uri));

    [Fact] public void LauncherRejectsUnknownUriAndMissingExe()
    {
        var launcher = new WindowsGameLauncher();
        Assert.Throws<InvalidOperationException>(() => launcher.Launch(new("X", "1", "Bad", LaunchUri: "cmd://x")));
        Assert.Throws<FileNotFoundException>(() => launcher.Launch(new(GameStores.Manual, "1", "Missing", ExePath: Path.Combine(root, "nope.exe"))));
    }

    [Fact] public void ManualStoreRoundTripsAtomically()
    {
        var path = Path.Combine(Dir("store"), "games.json"); var store = new JsonManualGameStore(path);
        Assert.Empty(store.Load());
        store.Save([new(GameStores.Manual, "c:\\g\\a.exe", "Jogo A", ExePath: @"C:\G\a.exe", Args: "-dx12")]);
        var loaded = Assert.Single(store.Load()); Assert.Equal("Jogo A", loaded.Name); Assert.Equal("-dx12", loaded.Args);
        Assert.False(File.Exists(path + ".tmp"));
        store.SaveHidden(["Steam:431960", "steam:431960"]); Assert.Equal(["Steam:431960"], store.LoadHidden());
    }

    private sealed class FakeLibrary(params GameEntry[] games) : IGameLibrary { public LibraryScan Scan(IReadOnlyList<GameEntry> manual) => new([.. games, .. manual], []); }
    private sealed class MemoryStore : IManualGameStore
    {
        public List<GameEntry> Items = []; public List<string> Hidden = [];
        public IReadOnlyList<GameEntry> Load() => Items; public void Save(IReadOnlyList<GameEntry> g) => Items = [.. g];
        public IReadOnlyList<string> LoadHidden() => Hidden; public void SaveHidden(IReadOnlyList<string> keys) => Hidden = [.. keys];
    }
    private sealed class FakeLauncher : IGameLauncher { public List<string> Launched = []; public void Launch(GameEntry game) => Launched.Add(game.Key); }

    [Fact] public async Task ViewModelSearchesFiltersAddsRemovesAndLaunches()
    {
        var store = new MemoryStore(); var launcher = new FakeLauncher();
        var vm = new GamesViewModel(new FakeLibrary(new(GameStores.Steam, "1", "Pokémon Arena", LaunchUri: "steam://rungameid/1"), new(GameStores.Epic, "2", "Fortnite", LaunchUri: "com.epicgames.launcher://x")),
            store, launcher, new TextCatalog(), _ => { });
        await vm.EnsureLoadedAsync();
        Assert.Equal(2, vm.Games.Count); Assert.Equal(3, vm.Stores.Count);
        vm.Search = "pokemon"; Assert.Equal("Pokémon Arena", Assert.Single(vm.Games).Name);
        vm.Search = "zzz"; Assert.True(vm.IsEmpty); Assert.Equal(vm.Text["GamesNoMatch"], vm.EmptyMessage);
        vm.Search = ""; vm.SelectedStore = vm.Stores.Single(s => s.Id == GameStores.Epic); Assert.Equal("Fortnite", Assert.Single(vm.Games).Name);
        vm.LaunchCommand.Execute(vm.Games[0]); Assert.Equal(["Epic Games:2"], launcher.Launched);
        vm.SelectedStore = vm.Stores[0];
        var exe = FileAt("", "Add", "MeuJogo.exe");
        Assert.True(await vm.AddManualAsync(exe));
        var added = Assert.Single(vm.Games, g => g.Store == GameStores.Manual); Assert.Equal("MeuJogo", added.Name); Assert.Single(store.Items);
        Assert.False(await vm.AddManualAsync(Path.Combine(root, "nao-existe.exe")));
        // Removing only hides: the manual entry stays saved and both can come back after a rescan.
        var pokemon = vm.Games.Single(g => g.Name == "Pokémon Arena"); Assert.Equal(vm.Text["GamesPlay"], pokemon.PlayLabel);
        added.Remove.Execute(added); pokemon.Remove.Execute(pokemon);
        Assert.DoesNotContain(vm.Games, g => g.Name is "MeuJogo" or "Pokémon Arena"); Assert.Single(store.Items);
        Assert.DoesNotContain(vm.Stores, s => s.Id == GameStores.Steam);
        var removed = await vm.RescanAsync();
        Assert.Equal(["MeuJogo", "Pokémon Arena"], removed.Select(g => g.Name));
        Assert.Equal("", vm.Status); // the rescan button confirms the result itself; no extra status line
        vm.Restore(removed.Where(g => g.Store == GameStores.Steam));
        Assert.Contains(vm.Games, g => g.Name == "Pokémon Arena"); Assert.Equal(["Manual:" + exe.ToLowerInvariant()], store.Hidden);
        Assert.True(await vm.AddManualAsync(exe)); Assert.Empty(store.Hidden); Assert.Contains(vm.Games, g => g.Name == "MeuJogo");
        Assert.Equal("PA", GamesViewModel.Initials("Pokémon Arena"));
    }

    [Fact] public void RealLibraryScanIsReadOnly()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var scan = new WindowsGameLibrary().Scan([]);
        output.WriteLine($"games={scan.Games.Count} errors={string.Join(",", scan.Errors)}");
        foreach (var group in scan.Games.GroupBy(g => g.Store)) output.WriteLine($"{group.Key}: {group.Count()} ({group.Count(g => g.CoverPath is not null)} with cover, {group.Count(g => !g.CanLaunch)} without launch)");
        foreach (var g in scan.Games.Take(40)) output.WriteLine($"  [{g.Store}] {g.Name}");
        Assert.Empty(scan.Errors);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
