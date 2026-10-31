using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lotus.Core.Backup;
using Lotus.Infrastructure.Backup;
using Xunit;

namespace Lotus.Tests;

// Google Drive client against an in-memory Drive (no network, no real account).
public sealed class GoogleDriveTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "lotus-drive-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

    private sealed class MemoryVault(string? value = null) : ITokenVault
    {
        public string? Value = value;
        public string? Read() => Value; public void Write(string value) => Value = value; public void Clear() => Value = null;
    }

    private sealed record DriveFile(string Id, string Name, string Mime, string Parent, DateTimeOffset Created) { public bool Trashed; public byte[] Data = []; }

    // Enough of Drive v3 for Lotus: files list/get/create/upload/download/delete, about, token and revoke.
    private sealed class FakeDrive : HttpMessageHandler
    {
        public readonly List<DriveFile> Files = [];
        public readonly Queue<(int Status, string Reason)> Failures = new();
        public readonly List<string> Calls = [];
        public bool RevokedGrant, Revoked, Offline;
        public string GrantedScope = GoogleDriveBackup.Scope;
        private int next;
        public string Add(string name, string mime, string parent, DateTimeOffset? created = null)
        {
            var id = "id" + ++next; Files.Add(new(id, name, mime, parent, created ?? DateTimeOffset.UtcNow.AddMinutes(next))); return id;
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var url = request.RequestUri!; var path = url.AbsolutePath; var query = System.Web.HttpUtility.ParseQueryString(url.Query);
            Calls.Add($"{request.Method} {path}");
            if (Offline) throw new HttpRequestException("offline");
            if (url.Host == "oauth2.googleapis.com" && path == "/token")
            {
                var form = await request.Content!.ReadAsStringAsync(token);
                if (RevokedGrant && form.Contains("refresh_token")) return Json(400, """{"error":"invalid_grant"}""");
                return Json(200, $$"""{"access_token":"at","expires_in":3600,"refresh_token":"rt","scope":"{{GrantedScope}}"}""");
            }
            if (path == "/revoke") { Revoked = true; return Json(200, "{}"); }
            if (request.Headers.Authorization?.Parameter != "at") return Json(401, """{"error":{"code":401}}""");
            if (Failures.TryDequeue(out var f)) return Json(f.Status, "{\"error\":{\"code\":" + f.Status + ",\"errors\":[{\"reason\":\"" + f.Reason + "\"}]}}");
            if (path.EndsWith("/about")) return Json(200, """{"user":{"emailAddress":"jogador@gmail.com","displayName":"Jogador"}}""");
            if (path == "/upload/drive/v3/files")
            {
                var raw = await request.Content!.ReadAsByteArrayAsync(token);
                var text = Encoding.UTF8.GetString(raw);
                var meta = JsonNode.Parse(text[text.IndexOf('{')..(text.IndexOf('}') + 1)])!;
                var parent = meta["parents"]![0]!.GetValue<string>();
                var id = Add(meta["name"]!.GetValue<string>(), "application/octet-stream", parent);
                Files.Single(x => x.Id == id).Data = Encoding.UTF8.GetBytes(text.Split("\r\n\r\n")[^1].Split("\r\n--")[0]);
                return Json(200, Meta(Files.Single(x => x.Id == id)));
            }
            if (path == "/drive/v3/files" && request.Method == HttpMethod.Post)
            {
                var meta = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!;
                return Json(200, $$"""{"id":"{{Add(meta["name"]!.GetValue<string>(), meta["mimeType"]!.GetValue<string>(), meta["parents"]![0]!.GetValue<string>())}}"}""");
            }
            if (path == "/drive/v3/files")
            {
                var q = query["q"]!;
                IEnumerable<DriveFile> r = Files.Where(x => !x.Trashed);
                if (Between(q, "name='", "'") is { } name) r = r.Where(x => x.Name == name);
                if (Between(q, "mimeType='", "'") is { } mime) r = r.Where(x => x.Mime == mime);
                if (q.Contains("mimeType!='")) r = r.Where(x => !x.Mime.Contains("folder"));
                if (System.Text.RegularExpressions.Regex.Match(q, "'([^']+)' in parents") is { Success: true } pm) r = r.Where(x => x.Parent == pm.Groups[1].Value);
                r = query["orderBy"] == "createdTime desc" ? r.OrderByDescending(x => x.Created) : r.OrderBy(x => x.Created);
                return Json(200, "{\"files\":[" + string.Join(",", r.Select(Meta)) + "]}");
            }
            if (path.StartsWith("/drive/v3/files/"))
            {
                var file = Files.SingleOrDefault(x => x.Id == path[16..]);
                if (file is null) return Json(404, """{"error":{"code":404,"errors":[{"reason":"notFound"}]}}""");
                if (request.Method == HttpMethod.Delete) { Files.Remove(file); return new HttpResponseMessage(HttpStatusCode.NoContent); }
                if (query["alt"] == "media") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file.Data) };
                return Json(200, Meta(file));
            }
            return Json(404, "{}");
        }
        private static string? Between(string s, string a, string b) { var i = s.IndexOf(a, StringComparison.Ordinal); if (i < 0) return null; i += a.Length; var j = s.IndexOf(b, i, StringComparison.Ordinal); return j < 0 ? null : s[i..j]; }
        private static string Meta(DriveFile f) => JsonSerializer.Serialize(new { id = f.Id, name = f.Name, mimeType = f.Mime, trashed = f.Trashed, size = f.Data.Length.ToString(), createdTime = f.Created.ToString("o") });
        private static HttpResponseMessage Json(int status, string body) => new((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private const string Folder = "application/vnd.google-apps.folder";
    private readonly List<TimeSpan> pauses = [];
    private GoogleDriveBackup Client(FakeDrive drive, MemoryVault? vault = null, string cache = "folders.json") =>
        new(new GoogleOAuthConfig("client", "public"), vault ?? new MemoryVault("jogador@gmail.com\nrt"), _ => Task.CompletedTask, new HttpClient(drive),
            new JsonDriveFolderCache(Path.Combine(dir, cache)), (t, _) => { pauses.Add(t); return Task.CompletedTask; });

    [Fact] public void SignInAsksOnlyForDriveFileWithPkceAndState()
    {
        var url = new Uri(GoogleDriveBackup.AuthorizationUrl("client", "http://127.0.0.1:5000/", "challenge", "state123"));
        var q = System.Web.HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("https://www.googleapis.com/auth/drive.file", q["scope"]);
        Assert.Equal("S256", q["code_challenge_method"]); Assert.Equal("challenge", q["code_challenge"]); Assert.Equal("state123", q["state"]);
        Assert.Equal("http://127.0.0.1:5000/", q["redirect_uri"]); Assert.Equal("offline", q["access_type"]);
    }

    [Fact] public async Task FinishingSignInKeepsTheRefreshTokenAndTheDriveEmail()
    {
        var drive = new FakeDrive(); var vault = new MemoryVault();
        var client = Client(drive, vault);
        Assert.Equal("jogador@gmail.com", await client.FinishSignInAsync("code", "http://127.0.0.1:1/", "verifier", default));
        Assert.Equal("jogador@gmail.com\nrt", vault.Value); Assert.Equal("jogador@gmail.com", client.Account);
        // The player unticked the Drive permission on Google's screen: nothing is kept as connected.
        var unticked = new FakeDrive { GrantedScope = "openid" }; var empty = new MemoryVault();
        var ex = await Assert.ThrowsAsync<CloudException>(() => Client(unticked, empty).FinishSignInAsync("code", "http://127.0.0.1:1/", "v", default));
        Assert.Equal(CloudErrorKind.Permission, ex.Kind); Assert.Null(empty.Value);
    }

    [Fact] public async Task BackupFolderIsCreatedOnceAndReusedAcrossRuns()
    {
        var drive = new FakeDrive();
        var first = await Client(drive).BackupsFolderAsync(default);
        var lotus = Assert.Single(drive.Files, f => f.Name == "Lotus" && f.Mime == Folder);
        Assert.Equal("root", lotus.Parent);
        Assert.Equal(lotus.Id, drive.Files.Single(f => f.Id == first).Parent);
        Assert.Equal("Backups", drive.Files.Single(f => f.Id == first).Name);
        // Another start of Lotus (new instance, same cache): same folder, nothing new created.
        Assert.Equal(first, await Client(drive).BackupsFolderAsync(default));
        Assert.Equal(2, drive.Files.Count(f => f.Mime == Folder));
        // A new PC (no cache): the folders Lotus created before are found, not duplicated.
        Assert.Equal(first, await Client(drive, cache: "other-pc.json").BackupsFolderAsync(default));
        Assert.Equal(2, drive.Files.Count(f => f.Mime == Folder));
    }

    [Fact] public async Task TrashedOrDeletedFoldersAreReplacedAndConcurrentCallsShareOneFolder()
    {
        var drive = new FakeDrive();
        var old = await Client(drive).BackupsFolderAsync(default);
        drive.Files.Single(f => f.Id == old).Trashed = true;                     // the player trashed it in Drive
        var replaced = await Client(drive).BackupsFolderAsync(default);
        Assert.NotEqual(old, replaced);
        Assert.Single(drive.Files, f => f.Name == "Lotus" && !f.Trashed);
        var client = Client(drive, cache: "fresh.json");
        drive.Files.RemoveAll(f => f.Mime == Folder);
        var ids = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.BackupsFolderAsync(default)));
        Assert.Single(ids.Distinct());
        Assert.Equal(2, drive.Files.Count(f => f.Mime == Folder));
    }

    [Fact] public async Task UploadListDownloadDeleteAndKeepOnlyTheFiveNewest()
    {
        var drive = new FakeDrive(); var client = Client(drive);
        for (var i = 1; i <= 7; i++) await client.UploadAsync(new MemoryStream(Encoding.UTF8.GetBytes("backup " + i)), $"Lotus-{i}.lotusbackup", default);
        var list = await client.ListAsync(default);
        Assert.Equal(5, list.Count);
        Assert.Equal("Lotus-7.lotusbackup", list[0].Name);                       // newest first
        using var target = new MemoryStream();
        await client.DownloadAsync(list[0].Id, target, default);
        Assert.Equal("backup 7", Encoding.UTF8.GetString(target.ToArray()));
        await client.DeleteAsync(list[0].Id, default);
        Assert.Equal(4, (await client.ListAsync(default)).Count);
        var ex = await Assert.ThrowsAsync<CloudException>(() => client.DeleteAsync("missing", default));
        Assert.Equal(CloudErrorKind.NotFound, ex.Kind);
    }

    [Fact] public async Task TemporaryErrorsAreRetriedAndOthersExplained()
    {
        var drive = new FakeDrive(); var client = Client(drive);
        await client.BackupsFolderAsync(default);
        drive.Failures.Enqueue((429, "rateLimitExceeded")); drive.Failures.Enqueue((503, "backendError")); drive.Failures.Enqueue((403, "userRateLimitExceeded"));
        Assert.Empty(await client.ListAsync(default));                           // worked on the 4th try
        Assert.Equal(3, pauses.Count); Assert.True(pauses[2] > pauses[0]);       // growing pauses
        drive.Failures.Enqueue((403, "storageQuotaExceeded"));
        Assert.Equal(CloudErrorKind.StorageFull, (await Assert.ThrowsAsync<CloudException>(() => client.UploadAsync(new MemoryStream([1]), "x", default))).Kind);
        drive.Failures.Enqueue((403, "insufficientFilePermissions"));
        Assert.Equal(CloudErrorKind.Permission, (await Assert.ThrowsAsync<CloudException>(() => client.ListAsync(default))).Kind);
        for (var i = 0; i < 6; i++) drive.Failures.Enqueue((429, "rateLimitExceeded"));
        Assert.Equal(CloudErrorKind.Quota, (await Assert.ThrowsAsync<CloudException>(() => client.ListAsync(default))).Kind);
        drive.Failures.Clear(); drive.Offline = true;
        Assert.Equal(CloudErrorKind.Network, (await Assert.ThrowsAsync<CloudException>(() => client.ListAsync(default))).Kind);
    }

    [Fact] public async Task RevokedAccessIsForgottenAndDisconnectRevokes()
    {
        var drive = new FakeDrive { RevokedGrant = true }; var vault = new MemoryVault("jogador@gmail.com\nrt");
        var client = Client(drive, vault);
        Assert.False(await client.ValidateAsync(default));
        Assert.Null(vault.Value); Assert.Null(client.Account);                   // the app shows "Connect" again
        var ok = new FakeDrive(); var vault2 = new MemoryVault("jogador@gmail.com\nrt"); var client2 = Client(ok, vault2);
        Assert.True(await client2.ValidateAsync(default));
        await client2.DisconnectAsync();
        Assert.True(ok.Revoked); Assert.Null(vault2.Value);
        Assert.Equal(CloudErrorKind.Auth, (await Assert.ThrowsAsync<CloudException>(() => client2.ListAsync(default))).Kind);
    }

    [Fact] public async Task TheAppExplainsDriveErrorsInPlainWords()
    {
        var drive = new FakeDrive { RevokedGrant = true };
        var vm = new BackupViewModel(new LotusBackupArchive(Path.Combine(dir, "lotus")), Client(drive), new Lotus.Core.TextCatalog(), _ => { });
        await vm.LoadFromCloudAsync();
        Assert.True(vm.IsError);
        Assert.Equal(new Lotus.Core.TextCatalog()["BkErrAuth"], vm.Message);
        Assert.False(vm.CloudConnected); Assert.True(vm.CloudCanConnect);
    }

    [Fact] public async Task ScheduledCopyRunsOnlyWhenChosenAndDue()
    {
        var drive = new FakeDrive(); var schedule = "manual";
        var vm = new BackupViewModel(new LotusBackupArchive(Path.Combine(dir, "lotus")), Client(drive), new Lotus.Core.TextCatalog(), _ => { })
        { ScheduleGet = () => schedule, ScheduleSet = v => schedule = v };
        Directory.CreateDirectory(Path.Combine(dir, "lotus")); File.WriteAllText(Path.Combine(dir, "lotus", "preferences.json"), "{}");
        Assert.False(await vm.ScheduledUploadAsync());                           // "only when I click": nothing is sent
        vm.ScheduleIndex = 1; Assert.Equal("weekly", schedule);
        Assert.True(await vm.ScheduledUploadAsync());                            // no copy yet: sent
        Assert.False(await vm.ScheduledUploadAsync());                           // a recent copy exists: not again
        Assert.Single(drive.Files, f => f.Name.EndsWith(".lotusbackup"));
        var old = drive.Files.Single(f => f.Name.EndsWith(".lotusbackup"));
        drive.Files[drive.Files.IndexOf(old)] = old with { Created = DateTimeOffset.UtcNow.AddDays(-8) };
        Assert.True(await vm.ScheduledUploadAsync());                            // a week later: sent again
        vm.ScheduleIndex = 2;
        Assert.False(await vm.ScheduledUploadAsync());                           // monthly, last copy is new
        Assert.Equal(TimeSpan.FromDays(30), BackupViewModel.Period("monthly")); Assert.Null(BackupViewModel.Period("manual"));
    }

    [Fact] public async Task AnUploadInterruptedByATemporaryErrorIsSentAgainComplete()
    {
        var drive = new FakeDrive(); var client = Client(drive);
        await client.BackupsFolderAsync(default);
        drive.Failures.Enqueue((503, "backendError"));
        using var data = new MemoryStream(Encoding.UTF8.GetBytes("full backup"));
        await client.UploadAsync(data, "retry.lotusbackup", default);
        var file = Assert.Single(drive.Files, f => f.Name == "retry.lotusbackup");
        Assert.Equal("full backup", Encoding.UTF8.GetString(file.Data));
        Assert.True(data.CanRead);                                              // the caller still owns the stream
    }
}