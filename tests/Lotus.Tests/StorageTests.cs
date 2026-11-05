using Lotus.Core;
using Lotus.Core.Maintenance;
using Xunit;

namespace Lotus.Tests;

// Files and storage on throw-away folders: sizes, duplicates by content, old downloads, protected paths, the Recycle Bin rule.
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class StorageTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "lotus-store-" + Guid.NewGuid().ToString("N"));
    private readonly DateTime now = DateTime.UtcNow;
    public StorageTests() => Directory.CreateDirectory(dir);
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

    private string F(string rel, byte[] content, TimeSpan? age = null)
    {
        var p = Path.Combine(dir, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllBytes(p, content);
        File.SetLastWriteTimeUtc(p, now - (age ?? TimeSpan.FromDays(1))); return p;
    }
    private static byte[] Bytes(int n, byte fill) { var b = new byte[n]; Array.Fill(b, fill); return b; }
    private string? Folders(string token) => token switch { "DOWNLOADS" => Path.Combine(dir, "Downloads"), "APPDATA" => Path.Combine(dir, "AppData"), "USERPROFILE" => dir, _ => null };
    private StorageScanner Scanner() => new(Folders, () => now) { LargeFileMin = 3000, DuplicateMin = 100, OldDownloadAge = TimeSpan.FromDays(90) };

    private sealed class FakeRecycler : IFileRecycler { public List<string> Moved { get; } = []; public bool Recycle(string path) { Moved.Add(path); File.Delete(path); return true; } }
    private sealed class NoReveal : IFileRevealer { public void Reveal(string path) { } }

    [Fact] public async Task FindsLargeFilesFoldersAndOldDownloads()
    {
        F(@"Videos\big.mp4", Bytes(5000, 1)); F(@"Videos\small.txt", Bytes(10, 2));
        F(@"Downloads\setup-old.exe", Bytes(400, 3), TimeSpan.FromDays(200)); F(@"Downloads\new.zip", Bytes(400, 4), TimeSpan.FromDays(3));
        var r = await Scanner().ScanAsync(dir);
        Assert.Equal("big.mp4", Path.GetFileName(Assert.Single(r.LargeFiles).Path));
        Assert.Equal(Path.Combine(dir, "Videos"), r.Folders[0].Path); Assert.Equal(5010, r.Folders[0].Bytes);
        Assert.Equal("setup-old.exe", Path.GetFileName(Assert.Single(r.OldDownloads).Path));
        Assert.Equal(4, r.ScannedFiles);
    }

    [Fact] public async Task DuplicatesNeedTheSameContentNotTheSameName()
    {
        F(@"a\photo.jpg", Bytes(500, 7)); F(@"b\copy of photo.jpg", Bytes(500, 7));        // same content, different names
        F(@"c\photo.jpg", Bytes(500, 8));                                                  // same name and size, different content
        var g = Assert.Single((await Scanner().ScanAsync(dir)).Duplicates);
        Assert.Equal(2, g.Files.Count); Assert.Equal(500, g.Wasted);
        Assert.DoesNotContain(g.Files, f => f.Path.Contains(@"\c\"));
    }

    [Fact] public async Task OneCopyAlwaysStaysAndSystemFilesAreNeverOffered()
    {
        F(@"a\x.bin", Bytes(500, 9)); F(@"b\x.bin", Bytes(500, 9));
        F(@"AppData\Game\cache.bin", Bytes(5000, 5));
        var recycler = new FakeRecycler();
        var vm = new StorageViewModel(Scanner(), recycler, new NoReveal(), () => [], Folders, new TextCatalog(), new MemoryMaintenanceJournal(), ex => throw ex);
        vm.Load(); await vm.ScanAsync();
        Assert.False(vm.Rows.Single(r => r.Title == "cache.bin").CanRecycle);            // app data: shown, never removable here
        vm.ViewIndex = 2;
        foreach (var r in vm.Rows) r.Selected = true;
        Assert.False(vm.KeepsOneCopyOfEach());
        await vm.RecycleSelectedAsync();
        Assert.Empty(recycler.Moved);                                                     // refused: every copy was ticked
        vm.Rows.First(r => !r.IsHeader).Selected = false;
        await vm.RecycleSelectedAsync();
        Assert.Single(recycler.Moved);
        Assert.Single(Directory.GetFiles(dir, "x.bin", SearchOption.AllDirectories));
    }

    [Fact] public async Task AFileChangedAfterTheAnalysisIsNotMoved()
    {
        var p = F(@"Videos\big.mp4", Bytes(5000, 1));
        var recycler = new FakeRecycler();
        var vm = new StorageViewModel(Scanner(), recycler, new NoReveal(), () => [], Folders, new TextCatalog(), new MemoryMaintenanceJournal(), ex => throw ex);
        vm.Load(); await vm.ScanAsync();
        vm.Rows.Single().Selected = true;
        File.WriteAllBytes(p, Bytes(6000, 1));                                            // the player kept using it
        await vm.RecycleSelectedAsync();
        Assert.Empty(recycler.Moved); Assert.True(File.Exists(p));
    }

    [Fact] public async Task CancellingStopsTheScan()
    {
        for (var i = 0; i < 20; i++) F($@"m\{i}.bin", Bytes(200, (byte)i));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Scanner().ScanAsync(dir, token: cts.Token));
    }

    [Fact] public void SystemLocationsAreRecognised()
    {
        var s = Scanner();
        Assert.True(s.IsSystemPath(Path.Combine(dir, "AppData", "x.dat")));
        Assert.True(s.IsSystemPath(@"C:\pagefile.sys"));
        Assert.False(s.IsSystemPath(Path.Combine(dir, "Videos", "x.mp4")));
    }
}
