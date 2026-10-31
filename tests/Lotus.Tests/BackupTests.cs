using System.IO.Compression;
using System.Net;
using System.Text;
using Lotus.Core.Backup;
using Lotus.Infrastructure.Backup;
using Xunit;

namespace Lotus.Tests;

public sealed class BackupTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "lotus-backup-" + Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(dir, "Lotus");
    public BackupTests() => Directory.CreateDirectory(Root);
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }
    private void Put(string rel, string text) { var p = Path.Combine(Root, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, text); }
    private string Get(string rel) => File.ReadAllText(Path.Combine(Root, rel));

    [Fact] public void SavesOnlyPlayerDataAndRestoresAfterRestart()
    {
        var avatar = Path.Combine(dir, "steam-avatar.png"); File.WriteAllText(avatar, "PNG");
        Put("preferences.json", $$"""{"DisplayName":"Joe","AvatarPath":{{System.Text.Json.JsonSerializer.Serialize(avatar)}}}""");
        Put("diary.json", """{"Entries":[{"Name":"Hades"},{"Name":"Celeste"}],"Bio":""}""");
        Put("achievements.json", """{"Earned":{"hunt_first":"2026-10-26T10:00:00-03:00"}}""");
        Put("player.json", """{"Favorites":["Steam:1"],"Notes":{}}""");
        Put("Profiles/games.json", "{}");
        Put("Benchmarks/abc/result.json", "{}");
        Put("Catalog/cache.json", "cache");      // not player data
        Put("Logs/lotus.jsonl", "log");
        Put("Optimizer/journal.db", "journal");   // describes this PC's Windows changes
        Put("diary.json.bak", "old");

        var archive = new LotusBackupArchive(Root, "0.2.0");
        using var file = new MemoryStream();
        var m = archive.Write(file);
        Assert.Contains("diary.json", m.Files); Assert.Contains("Profiles/games.json", m.Files); Assert.Contains("Benchmarks/abc/result.json", m.Files);
        Assert.DoesNotContain(m.Files, f => f.StartsWith("Catalog") || f.StartsWith("Logs") || f.StartsWith("Optimizer") || f.EndsWith(".bak"));
        Assert.Equal(new BackupSummary("Joe", 2, 1, 1, 1), m.Summary);
        Assert.Equal(avatar, m.AvatarOriginal);

        // Later the data changes; the backup is staged and applied on the next start.
        Put("diary.json", """{"Entries":[],"Bio":""}""");
        File.Delete(avatar);
        file.Position = 0;
        archive.Stage(file);
        Assert.True(archive.HasPending);
        var applied = archive.ApplyPending();
        Assert.NotNull(applied); Assert.False(archive.HasPending);
        Assert.Contains("Celeste", Get("diary.json"));
        Assert.Single(Directory.GetFiles(archive.BackupFolder, "before-restore-*"));       // safety copy of what existed
        var moved = Path.Combine(Root, "Profile", "avatar.png");
        Assert.True(File.Exists(moved));                                                   // the picture came back into Lotus
        Assert.Contains("avatar.png", Get("preferences.json"));
        Assert.Equal("journal", Get("Optimizer/journal.db"));                              // untouched
    }

    [Fact] public void ResetArchivesPlayerDataAndKeepsWindowsHistory()
    {
        Put("preferences.json", """{"Onboarded":true}"""); Put("diary.json", "{}"); Put("diary.json.bak", "old");
        Put("Profiles/games.json", "{}"); Put("Optimizer/journal.db", "journal"); Put("Logs/lotus.jsonl", "log");
        var archive = new LotusBackupArchive(Root);
        Assert.Null(archive.ApplyPendingReset());                          // nothing asked yet
        archive.StageReset(); Assert.True(archive.HasPendingReset);
        var kept = archive.ApplyPendingReset();
        Assert.NotNull(kept); Assert.False(archive.HasPendingReset);
        Assert.False(File.Exists(Path.Combine(Root, "preferences.json")));  // the welcome shows again
        Assert.False(Directory.Exists(Path.Combine(Root, "Profiles")));
        Assert.True(File.Exists(Path.Combine(kept!, "diary.json")) && File.Exists(Path.Combine(kept!, "diary.json.bak")) && File.Exists(Path.Combine(kept!, "Profiles", "games.json")));
        Assert.Equal("journal", Get("Optimizer/journal.db"));              // Windows changes can still be undone
        Assert.Equal("log", Get("Logs/lotus.jsonl"));
        Assert.Single(Directory.GetFiles(archive.BackupFolder, "before-reset-*"));
    }

    [Fact] public void RefusesBadFilesAndUnsafePaths()
    {
        var archive = new LotusBackupArchive(Root);
        Assert.Throws<BackupFormatException>(() => archive.Inspect(new MemoryStream(Encoding.UTF8.GetBytes("not a zip"))));
        Assert.Throws<BackupFormatException>(() => archive.Stage(new MemoryStream(Zip(("diary.json", "{}")))));          // no manifest
        Assert.False(archive.HasPending);
        Assert.Throws<BackupFormatException>(() => archive.Inspect(new MemoryStream(Zip(("manifest.json", """{"Format":99,"AppVersion":"","CreatedAt":"2026-01-01T00:00:00Z","Machine":"x","Files":[],"Summary":{"Player":"","DiaryGames":0,"Achievements":0,"Favorites":0,"Benchmarks":0}}""")))));
        foreach (var bad in new[] { "../evil.json", "Catalog/x.json", "C:/x.json", "Profiles/../../x", "tools.exe", "external/avatar/../x" }) Assert.False(LotusBackupArchive.Allowed(bad), bad);
        foreach (var good in new[] { "diary.json", "Profiles/games.json", "Benchmarks/a/frames.csv", "external/avatar.png" }) Assert.True(LotusBackupArchive.Allowed(good), good);

        // A crafted backup with an escaping entry: only the allowed file is written.
        var manifest = """{"Format":1,"AppVersion":"","CreatedAt":"2026-01-01T00:00:00Z","Machine":"x","Files":[],"Summary":{"Player":"","DiaryGames":0,"Achievements":0,"Favorites":0,"Benchmarks":0}}""";
        archive.Stage(new MemoryStream(Zip(("manifest.json", manifest), ("../escaped.json", "x"), ("player.json", "{\"Favorites\":[]}"))));
        archive.ApplyPending();
        Assert.False(File.Exists(Path.Combine(dir, "escaped.json")));
        Assert.True(File.Exists(Path.Combine(Root, "player.json")));
    }

    [Fact] public void AutomaticCopiesAreWeeklyAndKept()
    {
        Put("diary.json", "{}");
        var archive = new LotusBackupArchive(Root);
        Assert.NotNull(archive.AutoBackup(TimeSpan.FromDays(7), keep: 2));
        Assert.Null(archive.AutoBackup(TimeSpan.FromDays(7), keep: 2));                     // too soon
        Assert.Single(Directory.GetFiles(archive.BackupFolder, "auto-*"));
        Assert.Null(new LotusBackupArchive(Path.Combine(dir, "empty")).AutoBackup(TimeSpan.Zero, 2)); // nothing to save
    }

    [Fact] public void ReadsGoogleClientFile()
    {
        File.WriteAllText(Path.Combine(dir, "google-oauth.json"), """{"installed":{"client_id":"abc.apps.googleusercontent.com","client_secret":"s"}}""");
        Assert.Equal(new GoogleOAuthConfig("abc.apps.googleusercontent.com", "s"), GoogleOAuthConfig.Load(dir));
        Assert.Null(GoogleOAuthConfig.Load(Path.Combine(dir, "none")));
    }

    private sealed class MemoryVault : ITokenVault { public string? Value; public string? Read() => Value; public void Write(string v) => Value = v; public void Clear() => Value = null; }
    private sealed class FakeGoogle : HttpMessageHandler
    {
        public List<string> Calls = [];
        public int Uploaded;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken t)
        {
            var url = r.RequestUri!.ToString();
            Calls.Add(r.Method + " " + url.Split('?')[0]);
            string json;
            if (url.StartsWith("https://oauth2.googleapis.com/token")) json = """{"access_token":"A","expires_in":3600}""";
            else if (url.Contains("/upload/")) { Uploaded++; Assert.Equal("Bearer A", r.Headers.Authorization!.ToString()); json = """{"id":"f1","name":"b.lotusbackup","createdTime":"2026-10-26T20:00:00Z","size":"2048"}"""; }
            else if (url.Contains("mimeType")) json = """{"files":[{"id":"folder1"}]}""";
            else if (url.Contains("in parents") || url.Contains("in%20parents")) json = """{"files":[{"id":"f1","name":"b.lotusbackup","createdTime":"2026-10-26T20:00:00Z","size":"2048"}]}""";
            else if (url.Contains("alt=media")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
            else json = "{}";
            await Task.Yield();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    [Fact] public async Task DriveUploadsListsAndDownloadsWithStoredSession()
    {
        var fake = new FakeGoogle();
        var vault = new MemoryVault { Value = "joe@example.com\nREFRESH" };
        var drive = new GoogleDriveBackup(new("id", "secret"), vault, _ => Task.CompletedTask, new HttpClient(fake),
            new JsonDriveFolderCache(Path.Combine(Path.GetTempPath(), "lotus-drive-cache-" + Guid.NewGuid().ToString("N") + ".json")), (_, _) => Task.CompletedTask);
        Assert.True(drive.Configured); Assert.Equal("joe@example.com", drive.Account);
        var up = await drive.UploadAsync(new MemoryStream([9, 9]), "b.lotusbackup", default);
        Assert.Equal("f1", up.Id); Assert.Equal(1, fake.Uploaded);
        Assert.Single(await drive.ListAsync(default));
        using var target = new MemoryStream();
        await drive.DownloadAsync("f1", target, default);
        Assert.Equal(new byte[] { 1, 2, 3 }, target.ToArray());
        Assert.Single(fake.Calls, c => c.StartsWith("POST https://oauth2.googleapis.com/token"));    // access token reused
        await drive.DisconnectAsync();
        Assert.Null(vault.Value); Assert.Null(drive.Account);
        Assert.Equal(CloudErrorKind.Auth, (await Assert.ThrowsAsync<CloudException>(() => drive.ListAsync(default))).Kind);
        Assert.False(new GoogleDriveBackup(null, vault, _ => Task.CompletedTask).Configured);
    }

    private static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (name, text) in entries) { using var w = new StreamWriter(zip.CreateEntry(name).Open()); w.Write(text); }
        return ms.ToArray();
    }
}
