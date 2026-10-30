using System.Diagnostics;
using System.Text.Json;
using Lotus.Core;
using Lotus.Core.Games;
using Lotus.Core.Saves;
using Lotus.Infrastructure.Backup;
using Lotus.Infrastructure.Games;
using Xunit;

namespace Lotus.Tests;

// Save protection, only with temporary folders and fake files — never real saves of this PC.
public sealed class SaveProtectionTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "lotus-saves-" + Guid.NewGuid().ToString("N"));
    private string Src => Path.Combine(dir, "Save");
    private string VaultDir => Path.Combine(dir, "Vault");
    public SaveProtectionTests() { Directory.CreateDirectory(Src); Directory.CreateDirectory(VaultDir); }
    public void Dispose()
    {
        foreach (var j in Directory.Exists(dir) ? Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories).Where(d => new DirectoryInfo(d).LinkTarget is not null).ToList() : [])
            try { Directory.Delete(j); } catch (IOException) { }
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }
    private void Put(string rel, string text) { var p = Path.Combine(Src, rel.Replace('/', '\\')); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, text); }
    private string Read(string rel) => File.ReadAllText(Path.Combine(Src, rel.Replace('/', '\\')));
    private SaveVault Vault() => new(VaultDir) { MaxAttempts = 1, RetryDelay = TimeSpan.Zero };
    private const string Key = "Steam:413150";

    private sealed class Folders(string root) : IKnownFolders
    {
        public string? Get(string token) => token switch { "APPDATA" => Path.Combine(root, "AppData"), "DOCUMENTS" => Path.Combine(root, "Docs"), _ => null };
        public string? SteamPath() => Path.Combine(root, "Steam");
    }
    private sealed class SettingsMemory : ISaveSettingsStore { public SaveProtectionData Data = new(); public SaveProtectionData Load() => Data; public void Save(SaveProtectionData d) => Data = d; }

    // ---------- Discovery ----------
    [Fact] public void FindsKnownFolderByStoreIdOnlyAndReportsLimits()
    {
        var root = Path.Combine(dir, "Profile");
        var stardew = Path.Combine(root, "AppData", "StardewValley", "Saves", "Farm_1"); Directory.CreateDirectory(stardew);
        File.WriteAllText(Path.Combine(stardew, "Farm_1"), "save");
        var discovery = new SaveDiscovery(new Folders(root));
        var found = discovery.Find(new GameEntry(GameStores.Steam, "413150", "Stardew Valley"));
        Assert.Equal(SaveFindStatus.Found, found.Status);
        Assert.Equal(Path.Combine(root, "AppData", "StardewValley", "Saves"), found.Candidates[0].Root); Assert.Equal(1, found.Candidates[0].Files);
        // Same name from another store: no guess by name.
        Assert.Equal(SaveFindStatus.NotFound, discovery.Find(new GameEntry(GameStores.Gog, "1453375253", "Stardew Valley")).Status);
        // A rule whose folder does not exist (or is empty) finds nothing.
        Assert.Equal(SaveFindStatus.NotFound, discovery.Find(new GameEntry(GameStores.Steam, "292030", "The Witcher 3")).Status);
        Directory.CreateDirectory(Path.Combine(root, "Docs", "The Witcher 3", "gamesaves"));
        Assert.Equal(SaveFindStatus.NotFound, discovery.Find(new GameEntry(GameStores.Steam, "292030", "The Witcher 3")).Status);
        Assert.Equal(SaveFindStatus.Unsupported, discovery.Find(new GameEntry(GameStores.Xbox, "x", "Halo")).Status);
        // Steam Cloud folder of the game (by app id), marked as synced.
        var remote = Path.Combine(root, "Steam", "userdata", "12345", "620", "remote"); Directory.CreateDirectory(remote); File.WriteAllText(Path.Combine(remote, "save.dat"), "x");
        var cloud = discovery.Find(new GameEntry(GameStores.Steam, "620", "Portal 2"));
        Assert.Equal("steamcloud", cloud.Candidates.Single().Source); Assert.True(cloud.Candidates[0].CloudSynced);
        Assert.Null(discovery.Manual(Path.Combine(dir, "nope")));
    }

    // ---------- Snapshots ----------
    [Fact] public async Task SnapshotCopiesSubfoldersWithHashesAndSkipsDuplicates()
    {
        Put("slot1/save.sav", "progress 1"); Put("slot2/deep/save.sav", "progress 2"); Put("config.ini", "x=1");
        var vault = Vault();
        var r = await vault.CreateAsync(Key, "Stardew", Src, SnapshotManifest.Manual, 5);
        Assert.Equal(SnapshotStatus.Created, r.Status);
        Assert.Equal(["config.ini", "slot1/save.sav", "slot2/deep/save.sav"], r.Manifest!.Files.Select(f => f.Path));
        Assert.All(r.Manifest.Files, f => Assert.Equal(64, f.Sha256.Length));
        Assert.True(vault.Verify(Key, r.Manifest.Id));
        var again = await vault.CreateAsync(Key, "Stardew", Src, SnapshotManifest.Auto, 5);
        Assert.Equal(SnapshotStatus.Unchanged, again.Status); Assert.Single(vault.List(Key)); // nothing changed: no duplicate
        Put("slot1/save.sav", "progress 1b");
        Assert.Equal(SnapshotStatus.Created, (await vault.CreateAsync(Key, "Stardew", Src, SnapshotManifest.Auto, 5)).Status);
        Assert.Equal(2, vault.List(Key).Count);
    }

    [Fact] public async Task LargeFilesAreCopiedAndVerified()
    {
        var big = new byte[24 * 1024 * 1024]; new Random(1).NextBytes(big);
        File.WriteAllBytes(Path.Combine(Src, "world.bin"), big);
        var vault = Vault();
        var r = await vault.CreateAsync(Key, "G", Src, SnapshotManifest.Manual, 5);
        Assert.Equal(SnapshotStatus.Created, r.Status); Assert.Equal(big.Length, r.Manifest!.TotalBytes); Assert.True(vault.Verify(Key, r.Manifest.Id));
    }

    [Fact] public async Task LockedFileFailsAndLeavesNoPartialSnapshot()
    {
        Put("a.sav", "a");
        using (new FileStream(Path.Combine(Src, "a.sav"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var r = await Vault().CreateAsync(Key, "G", Src, SnapshotManifest.Auto, 5);
            Assert.Equal(SnapshotStatus.Failed, r.Status); Assert.Equal(SaveFailure.Locked, r.Failure);
        }
        Assert.Empty(Vault().List(Key));
        Assert.Empty(Directory.EnumerateDirectories(VaultDir, "*.partial", SearchOption.AllDirectories));
    }

    [Fact] public async Task FileChangingDuringCopyIsNotSavedAsComplete()
    {
        Put("a.sav", "a"); Put("b.sav", "b");
        var vault = Vault();
        vault.BeforeCopyFile = rel => { if (rel == "b.sav") File.WriteAllText(Path.Combine(Src, "b.sav"), "the game is still writing"); };
        var r = await vault.CreateAsync(Key, "G", Src, SnapshotManifest.Auto, 5);
        Assert.Equal(SaveFailure.Changing, r.Failure); Assert.Empty(vault.List(Key));
        // With retries, the next attempt (files now still) succeeds.
        var patient = new SaveVault(VaultDir) { MaxAttempts = 3, RetryDelay = TimeSpan.FromMilliseconds(10) };
        var once = true; patient.BeforeCopyFile = rel => { if (rel == "b.sav" && once) { once = false; File.AppendAllText(Path.Combine(Src, "b.sav"), "!"); } };
        Assert.Equal(SnapshotStatus.Created, (await patient.CreateAsync(Key, "G", Src, SnapshotManifest.Auto, 5)).Status);
    }

    [Fact] public async Task NoSpaceAndWriteFailuresNeverLeaveAHalfSnapshot()
    {
        Put("a.sav", "a"); Put("b.sav", "b");
        var full = Vault(); full.FreeSpace = _ => 1024;
        Assert.Equal(SaveFailure.NoSpace, (await full.CreateAsync(Key, "G", Src, SnapshotManifest.Auto, 5)).Failure);
        var broken = Vault(); broken.BeforeCopyFile = rel => { if (rel == "b.sav") throw new UnauthorizedAccessException("write failed"); };
        var r = await broken.CreateAsync(Key, "G", Src, SnapshotManifest.Auto, 5);
        Assert.Equal(SnapshotStatus.Failed, r.Status);
        Assert.Empty(Vault().List(Key));
        Assert.Empty(Directory.EnumerateDirectories(VaultDir, "*.partial", SearchOption.AllDirectories));
    }

    [Fact] public async Task InterruptedOrCorruptedSnapshotsAreNeverTrusted()
    {
        Put("a.sav", "a");
        var vault = Vault();
        var r = await vault.CreateAsync(Key, "G", Src, SnapshotManifest.Manual, 5);
        var gameDir = Path.Combine(VaultDir, SaveVault.FolderFor(Key));
        Directory.CreateDirectory(Path.Combine(gameDir, "20260101-000000-000-abc123-auto.partial")); // Lotus closed mid-snapshot
        Assert.Single(vault.List(Key));
        // Hash no longer matches: invalid, never restored.
        File.WriteAllText(Path.Combine(gameDir, r.Manifest!.Id, "files", "a.sav"), "tampered");
        Assert.False(vault.Verify(Key, r.Manifest.Id));
        Assert.Equal(SaveFailure.InvalidSnapshot, (await vault.RestoreAsync(Key, "G", r.Manifest.Id, Src, () => false, 3)).Failure);
        Assert.Equal("a", Read("a.sav"));
        // Broken manifest: not listed at all.
        File.WriteAllText(Path.Combine(gameDir, r.Manifest.Id, "manifest.json"), "{ broken");
        Assert.Empty(vault.List(Key));
    }

    [Fact] public async Task MaliciousPathsInAManifestAreRefused()
    {
        Put("a.sav", "a");
        var vault = Vault();
        var r = await vault.CreateAsync(Key, "G", Src, SnapshotManifest.Manual, 5);
        var snap = Path.Combine(VaultDir, SaveVault.FolderFor(Key), r.Manifest!.Id);
        var evil = r.Manifest with { Files = [r.Manifest.Files[0] with { Path = "../../evil.txt" }] };
        File.WriteAllText(Path.Combine(snap, "manifest.json"), JsonSerializer.Serialize(evil));
        Assert.Null(vault.Plan(Key, r.Manifest.Id, Src));
        Assert.False((await vault.RestoreAsync(Key, "G", r.Manifest.Id, Src, () => false, 3)).Restored);
        Assert.False(File.Exists(Path.Combine(dir, "evil.txt")));
        foreach (var bad in new[] { "../x", "a/../../x", "C:/x", "/x", "a\\b", "CON", "a/NUL.txt", "a/ ", "a/.", "x:y" }) Assert.False(SaveVault.IsSafeRelative(bad), bad);
        Assert.True(SaveVault.IsSafeRelative("slot 1/save.dat"));
        Assert.Null(SaveVault.Inside(Src, "../outside"));
    }

    [Fact] public async Task LinksAndJunctionsAreNotFollowed()
    {
        var outside = Path.Combine(dir, "Outside"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "private.txt"), "secret");
        Put("save.sav", "s");
        var junction = Path.Combine(Src, "linked");
        var mk = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{outside}\"") { CreateNoWindow = true, UseShellExecute = false })!;
        mk.WaitForExit();
        Assert.True(Directory.Exists(junction)); Assert.NotNull(new DirectoryInfo(junction).LinkTarget);
        var scan = SaveVault.Scan(Src);
        Assert.Equal(["save.sav"], scan.Files.Select(f => f.Rel)); Assert.Equal(1, scan.SkippedLinks);
        var r = await Vault().CreateAsync(Key, "G", Src, SnapshotManifest.Manual, 5);
        Assert.DoesNotContain(r.Manifest!.Files, f => f.Path.Contains("private"));
    }

    [Fact] public async Task SameNameDifferentGamesNeverShareBackups()
    {
        Put("a.sav", "steam");
        var vault = Vault();
        await vault.CreateAsync("Steam:1091500", "Cyberpunk 2077", Src, SnapshotManifest.Manual, 5);
        Put("a.sav", "gog");
        await vault.CreateAsync("GOG:1423049311", "Cyberpunk 2077", Src, SnapshotManifest.Manual, 5);
        Assert.NotEqual(SaveVault.FolderFor("Steam:1091500"), SaveVault.FolderFor("GOG:1423049311"));
        Assert.Single(vault.List("Steam:1091500")); Assert.Single(vault.List("GOG:1423049311"));
    }

    [Fact] public async Task RetentionKeepsTheNewestAndNeverDropsBeforeTheNewOneIsValid()
    {
        var vault = Vault();
        for (var i = 0; i < 7; i++) { Put("a.sav", "v" + i); Assert.Equal(SnapshotStatus.Created, (await vault.CreateAsync(Key, "G", Src, SnapshotManifest.Auto, 5)).Status); }
        Put("a.sav", "manual"); await vault.CreateAsync(Key, "G", Src, SnapshotManifest.Manual, 5);
        var list = vault.List(Key);
        Assert.Equal(5, list.Count(m => m.Origin == SnapshotManifest.Auto)); Assert.Single(list, m => m.Origin == SnapshotManifest.Manual);
        // A failing new backup leaves the 5 valid ones alone.
        vault.BeforeCopyFile = _ => throw new UnauthorizedAccessException();
        Put("a.sav", "v8"); await vault.CreateAsync(Key, "G", Src, SnapshotManifest.Auto, 5);
        Assert.Equal(5, vault.List(Key).Count(m => m.Origin == SnapshotManifest.Auto));
    }

    // ---------- Restore ----------
    [Fact] public async Task RestoreReplacesKeepsACopyOfBeforeAndVerifies()
    {
        Put("slot1/save.sav", "old progress"); Put("keep.cfg", "mine");
        var vault = Vault();
        var good = await vault.CreateAsync(Key, "G", Src, SnapshotManifest.Manual, 5);
        Put("slot1/save.sav", "CORRUPTED"); File.Delete(Path.Combine(Src, "keep.cfg")); Put("newer.cfg", "n");
        var plan = vault.Plan(Key, good.Manifest!.Id, Src)!;
        Assert.Equal(["slot1/save.sav"], plan.Replace); Assert.Equal(["keep.cfg"], plan.Create); Assert.Equal(["newer.cfg"], plan.KeepUntouched);
        var r = await vault.RestoreAsync(Key, "G", good.Manifest.Id, Src, () => false, 3);
        Assert.True(r.Restored);
        Assert.Equal("old progress", Read("slot1/save.sav")); Assert.Equal("mine", Read("keep.cfg")); Assert.Equal("n", Read("newer.cfg"));
        var safety = vault.List(Key).Single(m => m.Id == r.SafetySnapshotId);
        Assert.Equal(SnapshotManifest.BeforeRestore, safety.Origin); Assert.True(vault.Verify(Key, safety.Id)); // the corrupted state can still come back
        Assert.Empty(Directory.EnumerateFiles(Src, "*.lotus-restore", SearchOption.AllDirectories));
    }

    [Fact] public async Task RestoreRefusedWhileTheGameIsOpenAndCancellableBeforeChanges()
    {
        Put("a.sav", "old");
        var vault = Vault();
        var s = await vault.CreateAsync(Key, "G", Src, SnapshotManifest.Manual, 5);
        Put("a.sav", "new");
        Assert.Equal(SaveFailure.GameRunning, (await vault.RestoreAsync(Key, "G", s.Manifest!.Id, Src, () => true, 3)).Failure);
        Assert.Equal("new", Read("a.sav"));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vault.RestoreAsync(Key, "G", s.Manifest.Id, Src, () => false, 3, cts.Token));
        Assert.Equal("new", Read("a.sav"));
    }

    [Fact] public async Task FailureInTheMiddleRollsBackToTheExactPreviousState()
    {
        Put("a.sav", "A-old"); Put("b.sav", "B-old");
        var vault = Vault();
        var s = await vault.CreateAsync(Key, "G", Src, SnapshotManifest.Manual, 5);
        Put("a.sav", "A-now"); Put("b.sav", "B-now"); File.Delete(Path.Combine(Src, "b.sav"));
        Put("c.sav", "C-now");
        vault.BeforeReplaceFile = rel => { if (rel == "b.sav") throw new IOException("disk removed"); };
        var r = await vault.RestoreAsync(Key, "G", s.Manifest!.Id, Src, () => false, 3);
        Assert.False(r.Restored); Assert.Equal(SaveFailure.RolledBack, r.Failure);
        Assert.Equal("A-now", Read("a.sav")); Assert.Equal("C-now", Read("c.sav"));
        Assert.False(File.Exists(Path.Combine(Src, "b.sav")));                     // created by the failed restore: removed
        Assert.Empty(Directory.EnumerateFiles(Src, "*.lotus-restore", SearchOption.AllDirectories));
        Assert.False(vault.RestoreInterrupted(Key));
    }

    [Fact] public async Task RestoreInterruptedByAClosedLotusIsRecoveredAfterwards()
    {
        Put("a.sav", "A-old"); Put("b.sav", "B-old");
        var vault = Vault();
        var s = await vault.CreateAsync(Key, "G", Src, SnapshotManifest.Manual, 5);
        Put("a.sav", "A-now"); Put("b.sav", "B-now");
        vault.BeforeReplaceFile = rel => { if (rel == "b.sav") throw new ThreadInterruptedException("Lotus closed"); }; // not handled: like a crash
        await Assert.ThrowsAsync<ThreadInterruptedException>(() => vault.RestoreAsync(Key, "G", s.Manifest!.Id, Src, () => false, 3));
        Assert.Equal("A-old", Read("a.sav"));                                   // half restored…
        var later = Vault();
        Assert.True(later.RestoreInterrupted(Key));                              // …and noticed at the next start
        Assert.True(later.RecoverInterrupted(Key));
        Assert.Equal("A-now", Read("a.sav")); Assert.Equal("B-now", Read("b.sav"));
        Assert.False(later.RestoreInterrupted(Key));
    }

    // ---------- Service ----------
    [Fact] public async Task ProtectionTurnsOnOnlyAfterAVerifiedFirstBackupAndRunsAfterSessions()
    {
        Put("a.sav", "a");
        var settings = new SettingsMemory();
        var protection = new SaveProtection(settings, Vault(), new SaveDiscovery(new Folders(dir)), ex => throw ex);
        var game = new GameEntry(GameStores.Steam, "413150", "Stardew Valley");
        var place = protection.Discovery.Manual(Src)!;
        using (new FileStream(Path.Combine(Src, "a.sav"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Equal(SnapshotStatus.Failed, (await protection.EnableAsync(game, place, auto: true, keep: 5)).Status);
        Assert.False(protection.Get(game.Key).Enabled);                         // never "on" without a good first copy
        Assert.Equal(SnapshotStatus.Created, (await protection.EnableAsync(game, place, auto: true, keep: 5)).Status);
        Assert.Equal(ProtectionState.Active, protection.State(game));
        Assert.Null(await protection.AfterSessionAsync(game, () => true, TimeSpan.Zero));          // still running: wait for later
        Put("a.sav", "after the session");
        Assert.Equal(SnapshotStatus.Created, (await protection.AfterSessionAsync(game, () => false, TimeSpan.Zero))!.Status);
        Assert.Equal(SnapshotStatus.Unchanged, (await protection.AfterSessionAsync(game, () => false, TimeSpan.Zero))!.Status);
        // A failed automatic backup becomes "attention" and a short note for "Preparar e Jogar", never a blocker.
        using (new FileStream(Path.Combine(Src, "a.sav"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { Put2(); await protection.AfterSessionAsync(game, () => false, TimeSpan.Zero); }
        Assert.Equal(ProtectionState.Attention, protection.State(game)); Assert.NotNull(protection.Warning(game, new TextCatalog()));
        protection.Disable(game);
        Assert.NotEmpty(protection.Vault.List(game.Key));                        // turning off never deletes backups
        void Put2() { }
    }

    [Fact] public void SettingsSurviveBrokenFilesAndOnlySettingsTravelInTheLotusBackup()
    {
        var path = Path.Combine(dir, "save-protection.json");
        var store = new JsonSaveSettingsStore(path);
        var data = new SaveProtectionData(); data.Games["Steam:1"] = new GameSaveSettings { Enabled = true, Root = Src, Keep = 10 };
        store.Save(data); store.Save(data);
        File.WriteAllText(path, "{ broken");
        Assert.True(new JsonSaveSettingsStore(path).Load().Games["Steam:1"].Enabled);    // from the .bak
        // An older settings file without the newer fields still loads with safe defaults.
        File.WriteAllText(path, """{"Version":1,"Games":{"Steam:2":{"Enabled":true,"Root":"C:\\x"}}}""");
        var old = new JsonSaveSettingsStore(path).Load().Games["Steam:2"];
        Assert.True(old.Auto); Assert.Equal(5, old.Keep);
        Assert.True(LotusBackupArchive.Allowed("save-protection.json"));
        Assert.False(LotusBackupArchive.Allowed("SaveBackups/x/manifest.json")); // the save files never go into a .lotusbackup
    }

    [Fact] public async Task NewerSnapshotFormatsAreLeftAlone()
    {
        Put("a.sav", "a");
        var vault = Vault();
        var r = await vault.CreateAsync(Key, "G", Src, SnapshotManifest.Manual, 5);
        var file = Path.Combine(VaultDir, SaveVault.FolderFor(Key), r.Manifest!.Id, "manifest.json");
        File.WriteAllText(file, JsonSerializer.Serialize(r.Manifest with { Format = SnapshotManifest.CurrentFormat + 1 }));
        Assert.Empty(vault.List(Key)); // a format this version does not know is never restored blindly
    }

    // ---------- "Backup de saves" page ----------
    [Fact] public async Task SavesPageListsFiltersAndKeepsUninstalledProtectedGames()
    {
        var root = Path.Combine(dir, "Profile");
        var stardew = Path.Combine(root, "AppData", "StardewValley", "Saves"); Directory.CreateDirectory(stardew); File.WriteAllText(Path.Combine(stardew, "Farm"), "save");
        Put("old.sav", "x");
        var protection = new SaveProtection(new SettingsMemory(), Vault(), new SaveDiscovery(new Folders(root)), ex => throw ex);
        var gone = new GameEntry(GameStores.Steam, "999", "Old Game");
        await protection.EnableAsync(gone, protection.Discovery.Manual(Src)!, auto: true, keep: 5);
        GameEntry[] installed = [new(GameStores.Steam, "413150", "Stardew Valley"), new(GameStores.Steam, "292030", "The Witcher 3"), new(GameStores.Xbox, "x", "Halo")];
        var page = new SaveLibraryViewModel(protection, () => installed, new TextCatalog(), _ => false);
        await page.LoadAsync();
        Assert.Equal(4, page.Items.Count);
        Assert.Equal("Steam:999", page.Items[0].Game.Key);                       // protected first, even though uninstalled
        Assert.True(page.Items[0].IsProtected); Assert.False(page.Items[0].Installed); Assert.Equal(1, page.BackupCount);
        Assert.Equal(SaveTileKind.Found, page.Items.Single(t => t.Game.Id == "413150").Kind);
        Assert.Equal(SaveTileKind.Unsupported, page.Items.Single(t => t.Game.Store == GameStores.Xbox).Kind);
        page.Filter = "protected"; Assert.Single(page.Items);
        page.Filter = "none"; Assert.Equal(2, page.Items.Count); Assert.Equal(2, page.CountFor("none"));
        page.Filter = "all"; page.Search = "star"; Assert.Equal("Stardew Valley", Assert.Single(page.Items).Name);
        // Opening a game shows its own saves; after a backup there, the list is updated on the way back.
        page.Search = "";
        await page.OpenAsync(page.Items.Single(t => t.Game.Id == "413150"));
        Assert.True(page.HasSelection); Assert.Equal(stardew, page.Selected!.FolderPath);
        await page.Selected.EnableAsync();
        await page.CloseAsync();
        Assert.False(page.HasSelection);
        Assert.Equal(2, page.ProtectedCount); Assert.Equal(2, page.BackupCount);
        // A game's page can ask for one game to open directly.
        page.PendingOpen = "steam:413150";
        await page.LoadAsync();
        Assert.Equal("Stardew Valley", page.SelectedTile!.Name); Assert.Null(page.PendingOpen);
    }
}