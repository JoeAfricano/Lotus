using Lotus.Core.Games;
using Lotus.Core.Saves;
using Xunit;

namespace Lotus.Tests;

// The Ludusavi manifest: thousands of save folders, read without a YAML library and used only when safe.
public sealed class SaveManifestTests : IDisposable
{
    private const string Sample = """
        "!BurnToDie!":
          installDir:
            Anyway: {}
          steam:
            id: 866510
        Hollow Knight:
          cloud:
            steam: true
          files:
            "<base>/hollow_knight_Data/Config.ini":
              tags:
                - config
              when:
                - os: windows
            "<home>/AppData/LocalLow/Team Cherry/Hollow Knight/*.bak":
              tags:
                - save
              when:
                - os: windows
            "<home>/AppData/LocalLow/Team Cherry/Hollow Knight/*.dat":
              tags:
                - save
              when:
                - os: windows
            "<home>/Library/Application Support/unity.Team Cherry.Hollow Knight":
              tags:
                - save
              when:
                - os: mac
            "<winProgramData>/Packages/TeamCherry.15373CD61C66B_y4jvztpgccj42/SystemAppData/wgs":
              tags:
                - config
                - save
              when:
                - os: windows
                  store: microsoft
          gog:
            id: 1308320804
          id:
            gogExtra:
              - 1984697392
            lutris: hollow-knight
            steamExtra:
              - 598190
          registry:
            HKEY_CURRENT_USER/Software/Team Cherry/Hollow Knight:
              tags:
                - config
          steam:
            id: 367520
        "Baldur's Gate 3":
          files:
            "<winLocalAppData>/Larian Studios/Baldur's Gate 3/PlayerProfiles":
              tags:
                - save
            "<winLocalAppData>/Larian Studios/Baldur's Gate 3/PlayerProfiles/Public/Savegames":
              tags:
                - save
            "<winDocuments>/*":
              tags:
                - save
          steam:
            id: 1086940
        Too Broad:
          files:
            "<winDocuments>/My Games/*.sav": {}
            "<home>/*.sav": {}
          steam:
            id: 1
        """;

    [Fact] public void ParsesWindowsSaveFoldersAndIdsOnly()
    {
        var games = LudusaviManifest.Parse(new StringReader(Sample));
        Assert.Equal(["Hollow Knight", "Baldur's Gate 3"], games.Select(g => g.Name));   // no usable folder = not kept
        var hk = games[0];
        Assert.Equal(["367520", "598190"], hk.Steam.Order()); Assert.Equal(["1308320804", "1984697392"], hk.Gog);
        Assert.Contains(new ManifestFolder(@"{LOCALLOW}\Team Cherry\Hollow Knight"), hk.Folders);
        Assert.Contains(new ManifestFolder(@"{PROGRAMDATA}\Packages\TeamCherry.15373CD61C66B_y4jvztpgccj42\SystemAppData\wgs", "microsoft"), hk.Folders);
        Assert.Equal(2, hk.Folders.Count);                                                    // .bak/.dat once, config and mac skipped
        var bg3 = Assert.Single(games[1].Folders);                                            // the inner folder is covered; Documents\* refused
        Assert.Equal(@"{LOCALAPPDATA}\Larian Studios\Baldur's Gate 3\PlayerProfiles", bg3.Template);
    }

    [Fact] public void TemplatesRefuseSharedAndUnknownFolders()
    {
        Assert.Equal(@"{DOCUMENTS}\My Games\Terraria\Players", LudusaviManifest.ToTemplate("<home>/Documents/My Games/Terraria/Players/*.plr"));
        Assert.Equal(@"{SAVEDGAMES}\CD Projekt Red\Cyberpunk 2077", LudusaviManifest.ToTemplate("<home>/Saved Games/CD Projekt Red/Cyberpunk 2077"));
        Assert.Equal(@"{APPDATA}\EldenRing", LudusaviManifest.ToTemplate("<winAppData>/EldenRing/<storeUserId>/*.sl2"));
        Assert.Null(LudusaviManifest.ToTemplate("<winDocuments>/My Games/*"));
        Assert.Null(LudusaviManifest.ToTemplate("<home>/AppData/LocalLow/*"));
        Assert.Null(LudusaviManifest.ToTemplate("<home>/save.dat"));
        Assert.Null(LudusaviManifest.ToTemplate("<root>/<game>/save"));
        Assert.Null(LudusaviManifest.ToTemplate("<xdgConfig>/game/save"));
        Assert.Null(LudusaviManifest.ToTemplate("<base>/options.ini"));
        Assert.Equal(@"{INSTALL}\Saves", LudusaviManifest.ToTemplate("<base>/Saves/*.sav"));
    }

    [Fact] public void KeysWithQuotesAndEscapes()
    {
        Assert.True(LudusaviManifest.KeyOf("\"\\\"LIFE\\\" not found\":", out var k, out var v)); Assert.Equal("\"LIFE\" not found", k); Assert.Null(v);
        Assert.True(LudusaviManifest.KeyOf("'It''s': x", out k, out v)); Assert.Equal("It's", k); Assert.Equal("x", v);
        Assert.True(LudusaviManifest.KeyOf("id: 367520", out k, out v)); Assert.Equal("id", k); Assert.Equal("367520", v);
        Assert.True(LudusaviManifest.KeyOf("HKEY_CURRENT_USER/Software/1$ Ride:", out k, out _)); Assert.Equal("HKEY_CURRENT_USER/Software/1$ Ride", k);
    }

    [Fact] public void MatchesByStoreIdAndOnlyUniqueExactTitles()
    {
        var manifest = new SaveManifest(LudusaviManifest.Parse(new StringReader(Sample)));
        Assert.False(manifest.Find(new GameEntry(GameStores.Steam, "598190", "HK demo"))!.ByName);
        Assert.Single(manifest.Find(new GameEntry(GameStores.Steam, "367520", "x"))!.Templates);            // the Microsoft-only folder is left out
        Assert.Equal(2, manifest.Find(new GameEntry(GameStores.Xbox, "TeamCherry", "Hollow Knight"))!.Templates.Count);
        Assert.NotNull(manifest.Find(new GameEntry(GameStores.Gog, "1984697392", "x")));
        Assert.Null(manifest.Find(new GameEntry(GameStores.Gog, "999", "Hollow Knight")));                    // GOG ids never fall back to the title
        var epic = manifest.Find(new GameEntry(GameStores.Epic, "Larian", "BALDUR’S GATE 3™"))!;
        Assert.True(epic.ByName);
        Assert.NotNull(manifest.Find(new GameEntry(GameStores.Manual, "steam-1086940", "Whatever")));
        Assert.Null(manifest.Find(new GameEntry(GameStores.Epic, "x", "Baldur's Gate")));                    // close is not enough
    }

    private readonly string dir = Path.Combine(Path.GetTempPath(), "lotus-manifest-" + Guid.NewGuid().ToString("N"));
    public SaveManifestTests() => Directory.CreateDirectory(dir);
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }
    private sealed class Folders(string root) : IKnownFolders
    {
        public string? Get(string token) => token switch { "LOCALAPPDATA" => Path.Combine(root, "Local"), "DOCUMENTS" => Path.Combine(root, "Docs"), "LOCALLOW" => Path.Combine(root, "LocalLow"), _ => null };
        public string? SteamPath() => null;
    }

    [Fact] public void DiscoveryUsesTheManifestOnlyForTheGamesOwnFolder()
    {
        if (!OperatingSystem.IsWindows()) return; // templates are Windows paths
        var save = Path.Combine(dir, "LocalLow", "Team Cherry", "Hollow Knight");
        Directory.CreateDirectory(save); File.WriteAllText(Path.Combine(save, "user1.dat"), "progress");
        Directory.CreateDirectory(Path.Combine(dir, "Docs")); File.WriteAllText(Path.Combine(dir, "Docs", "cv.docx"), "private");
        var discovery = new SaveDiscovery(new Folders(dir))
        {
            Manifest = new SaveManifest([
                new("Hollow Knight", ["367520"], [], [new(@"{LOCALLOW}\Team Cherry\Hollow Knight")]),
                new("Sneaky", ["5"], [], [new(@"{DOCUMENTS}\..\Docs"), new(@"{DOCUMENTS}\cv.docx")]),
            ]),
        };
        var hk = discovery.Find(new GameEntry(GameStores.Steam, "367520", "Hollow Knight"));
        Assert.Equal(SaveManifest.Source, Assert.Single(hk.Candidates).Source);
        Assert.Equal(save, hk.Candidates[0].Root);
        Assert.Equal(SaveManifest.NameSource, Assert.Single(discovery.Find(new GameEntry(GameStores.Epic, "x", "Hollow Knight")).Candidates).Source);
        Assert.Equal(SaveFindStatus.NotFound, discovery.Find(new GameEntry(GameStores.Steam, "5", "Sneaky")).Status); // Documents itself: refused
    }
}

public sealed class SaveManifestRealFileTests
{
    // Optional: LOTUS_MANIFEST=<path to a downloaded manifest.yaml> checks the reader on the real file.
    [Fact] public void RealManifestParses()
    {
        if (Environment.GetEnvironmentVariable("LOTUS_MANIFEST") is not { Length: > 0 } path || !File.Exists(path)) return;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using var reader = File.OpenText(path);
        var games = LudusaviManifest.Parse(reader);
        var manifest = new SaveManifest(games);
        Console.WriteLine($"games={games.Count} folders={games.Sum(g => g.Folders.Count)} steam={games.Count(g => g.Steam.Count > 0)} gog={games.Count(g => g.Gog.Count > 0)} ms={watch.ElapsedMilliseconds}");
        Assert.True(games.Count > 5000);
        Assert.Contains(@"{LOCALLOW}\Team Cherry\Hollow Knight", manifest.Find(new GameEntry(GameStores.Steam, "367520", "Hollow Knight"))!.Templates);
        Assert.NotNull(manifest.Find(new GameEntry(GameStores.Steam, "1245620", "ELDEN RING")));
    }
}
