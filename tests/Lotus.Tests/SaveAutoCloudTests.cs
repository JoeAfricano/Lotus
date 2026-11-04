using Lotus.Core.Games;
using Lotus.Core.Saves;
using Xunit;

namespace Lotus.Tests;

// Steam Auto-Cloud games (Palworld and many others) keep saves outside Steam; Steam's remotecache.vdf says where.
public sealed class SaveAutoCloudTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "lotus-autocloud-" + Guid.NewGuid().ToString("N"));
    public SaveAutoCloudTests() => Directory.CreateDirectory(dir);
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

    private sealed class Folders(string root) : IKnownFolders
    {
        public string? Get(string token) => token switch { "LOCALAPPDATA" => Path.Combine(root, "Local"), "DOCUMENTS" => Path.Combine(root, "Docs"), _ => null };
        public string? SteamPath() => Path.Combine(root, "Steam");
    }

    private void Write(string relative, string text) { var p = Path.Combine(dir, relative); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, text); }
    private static string Cache(string appId, params (string Path, string Root)[] files) =>
        $"\"{appId}\"\n{{\n\t\"ChangeNumber\"\t\t\"0\"\n" + string.Concat(files.Select(f => $"\t\"{f.Path}\"\n\t{{\n\t\t\"root\"\t\t\"{f.Root}\"\n\t\t\"size\"\t\t\"10\"\n\t}}\n")) + "}\n";

    [Fact] public void PalworldStyleSavesAreFound()
    {
        Write(@"Local\Pal\Saved\SaveGames\7656\WORLD1\Level.sav", "w1");
        Write(@"Local\Pal\Saved\SaveGames\7656\WORLD2\Level.sav", "w2");
        Write(@"Steam\userdata\123\1623730\remotecache.vdf", Cache("1623730",
            ("Pal/Saved/SaveGames/7656/WORLD1/Level.sav", "3"), ("Pal/Saved/SaveGames/7656/WORLD2/Level.sav", "3"), ("Pal/Saved/SaveGames/7656/WORLD1/backup/local/x/LocalData.sav", "3")));
        var find = new SaveDiscovery(new Folders(dir)).Find(new GameEntry(GameStores.Steam, "1623730", "Palworld"));
        Assert.Equal(SaveFindStatus.Found, find.Status);
        var c = Assert.Single(find.Candidates);
        Assert.Equal(Path.Combine(dir, @"Local\Pal\Saved\SaveGames\7656"), c.Root);
        Assert.Equal(2, c.Files); Assert.True(c.CloudSynced);
    }

    [Fact] public void TooBroadOrUnsafeFoldersAreNeverUsed()
    {
        Write(@"Docs\notes.txt", "personal");
        Write(@"Steam\userdata\123\42\remotecache.vdf", Cache("42", ("notes.txt", "2"), ("../escape.sav", "2")));
        Assert.Equal(SaveFindStatus.NotFound, new SaveDiscovery(new Folders(dir)).Find(new GameEntry(GameStores.Steam, "42", "X")).Status);
        Assert.Null(SaveDiscovery.CommonFolder(["A/x.sav", "B/y.sav"]));          // nothing in common: no folder
        Assert.Equal("A/B", SaveDiscovery.CommonFolder(["A/B/c.sav", "A/B/D/e.sav"]));
    }

    [Fact] public void TheCacheFileIsReadByRoot()
    {
        var parsed = SaveDiscovery.ParseRemoteCache(Cache("1", ("a/b.sav", "3"), ("c.sav", "0"), ("d/e.sav", "3")).Split('\n'));
        Assert.Equal(["a/b.sav", "d/e.sav"], parsed["3"]);
        Assert.Equal(["c.sav"], parsed["0"]);
    }
}
