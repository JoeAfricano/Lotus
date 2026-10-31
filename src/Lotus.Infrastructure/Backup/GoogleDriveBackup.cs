using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lotus.Core.Backup;

namespace Lotus.Infrastructure.Backup;

public sealed record GoogleOAuthConfig(string ClientId, string ClientSecret)
{
    // google-oauth.json next to Lotus.exe ({"client_id": "...", "client_secret": "..."}), from the "Desktop app" OAuth
    // client of the Lotus project in Google Cloud. A desktop app is a public client: the "secret" ships with the app and
    // is not trusted as a secret; PKCE and the loopback redirect protect the sign-in.
    public static GoogleOAuthConfig? Load(string? folder = null)
    {
        // Next to Lotus.exe (installer builds), or in %LOCALAPPDATA%\Lotus (handy for development builds).
        var file = Path.Combine(folder ?? AppContext.BaseDirectory, "google-oauth.json");
        if (folder is null && !File.Exists(file)) file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "google-oauth.json");
        try
        {
            if (!File.Exists(file)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement.TryGetProperty("installed", out var installed) ? installed : doc.RootElement; // the file Google offers for download also works
            var id = root.TryGetProperty("client_id", out var i) ? i.GetString() : null;
            var secret = root.TryGetProperty("client_secret", out var s) ? s.GetString() : "";
            return id is { Length: > 0 } ? new(id, secret ?? "") : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }
}

// Where the refresh token lives (Windows Credential Locker, protected by Windows for the user, in the app; memory in tests).
public interface ITokenVault { string? Read(); void Write(string value); void Clear(); }

// Drive folder ids found or created by Lotus, per account, so the same folders are reused (they are not secret).
public interface IDriveFolderCache { (string? Lotus, string? Backups) Read(string account); void Write(string account, string lotus, string backups); void Clear(string account); }

public sealed class JsonDriveFolderCache(string? path = null) : IDriveFolderCache
{
    private readonly string file = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "drive-folders.json");
    private Dictionary<string, string[]> Load()
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(file)) ?? [] : []; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }
    private void Save(Dictionary<string, string[]> data)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, JsonSerializer.Serialize(data)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // the folders are found again next time
    }
    public (string? Lotus, string? Backups) Read(string account) => Load().TryGetValue(account.ToLowerInvariant(), out var v) && v.Length == 2 ? (v[0], v[1]) : (null, null);
    public void Write(string account, string lotus, string backups) { var d = Load(); d[account.ToLowerInvariant()] = [lotus, backups]; Save(d); }
    public void Clear(string account) { var d = Load(); if (d.Remove(account.ToLowerInvariant())) Save(d); }
}

// Google Drive copy of the backup (Drive API v3 over HTTPS).
// - Sign-in in the player's browser: OAuth 2.0 authorization code with PKCE (S256), loopback redirect on 127.0.0.1,
//   checked "state"; Lotus never sees the password.
// - Only the drive.file scope: Lotus sees and changes only the files it created itself.
// - Backups live in "Lotus/Backups"; the folder ids are kept and checked, so folders are never duplicated.
// - Expired or revoked access, network problems, permissions and quotas become a CloudException the app can explain.
public sealed class GoogleDriveBackup(GoogleOAuthConfig? initial, ITokenVault vault, Func<Uri, Task> openBrowser, HttpClient? http = null,
    IDriveFolderCache? folders = null, Func<TimeSpan, CancellationToken, Task>? delay = null) : ICloudBackup
{
    public const string Scope = "https://www.googleapis.com/auth/drive.file";
    public const string RootFolder = "Lotus", BackupsFolder = "Backups";
    private const string FolderMime = "application/vnd.google-apps.folder";
    private const string Api = "https://www.googleapis.com/drive/v3";
    private GoogleOAuthConfig? config = initial;
    private readonly HttpClient http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    private readonly IDriveFolderCache folderCache = folders ?? new JsonDriveFolderCache();
    private readonly Func<TimeSpan, CancellationToken, Task> wait = delay ?? Task.Delay;
    private readonly SemaphoreSlim folderLock = new(1, 1);
    private string? accessToken, backupsId;
    private DateTimeOffset accessUntil;

    public bool Configured => config is not null;

    // The Google Cloud "Desktop app" client file, kept in %LOCALAPPDATA%\Lotus so this PC can use Google Drive even in
    // a build that has no client inside.
    public bool ImportClient(string path)
    {
        var folder = Path.Combine(Path.GetTempPath(), "lotus-client-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            File.Copy(path, Path.Combine(folder, "google-oauth.json"));
            if (GoogleOAuthConfig.Load(folder) is not { } loaded) return false;
            var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "google-oauth.json");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target, overwrite: true);
            config = loaded;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    // vault value: "<email>\n<refresh token>"
    public string? Account => vault.Read()?.Split('\n', 2)[0] is { Length: > 0 } a ? a : null;

    // ---------- Sign-in ----------
    public async Task<string> ConnectAsync(CancellationToken token)
    {
        if (config is null) throw new CloudException(CloudErrorKind.NotConfigured, "Google client not configured");
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(24));
        var port = FreePort();
        var redirect = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirect);
        listener.Start();
        await openBrowser(new Uri(AuthorizationUrl(config.ClientId, redirect, challenge, state)));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var reg = timeout.Token.Register(() => { try { listener.Stop(); } catch (ObjectDisposedException) { } });
        HttpListenerContext context;
        while (true)
        {
            try { context = await listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException) { throw new OperationCanceledException("Sign-in timed out or was cancelled"); }
            if (context.Request.Url?.AbsolutePath == "/") break;
            context.Response.StatusCode = 404; context.Response.Close(); // e.g. /favicon.ico
        }
        var q = System.Web.HttpUtility.ParseQueryString(context.Request.Url?.Query ?? "");
        var ok = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(q["state"] ?? ""), Encoding.ASCII.GetBytes(state)) && q["code"] is { Length: > 0 };
        await Reply(context, ok);
        if (!ok) throw new CloudException(CloudErrorKind.Auth, q["error"] ?? "Sign-in was not completed");
        return await FinishSignInAsync(q["code"]!, redirect, verifier, token);
    }

    public static string AuthorizationUrl(string clientId, string redirect, string challenge, string state) => "https://accounts.google.com/o/oauth2/v2/auth?" + Query(new()
    {
        ["client_id"] = clientId, ["redirect_uri"] = redirect, ["response_type"] = "code", ["scope"] = Scope,
        ["code_challenge"] = challenge, ["code_challenge_method"] = "S256", ["state"] = state, ["access_type"] = "offline", ["prompt"] = "consent",
    });

    // Code → tokens (with the PKCE verifier) → account e-mail from Drive itself (no extra scope needed).
    public async Task<string> FinishSignInAsync(string code, string redirect, string verifier, CancellationToken token)
    {
        var tokens = await Token(new()
        {
            ["code"] = code, ["client_id"] = config!.ClientId, ["client_secret"] = config.ClientSecret, ["redirect_uri"] = redirect,
            ["grant_type"] = "authorization_code", ["code_verifier"] = verifier,
        }, token);
        var refresh = tokens.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        if (refresh is null) throw new CloudException(CloudErrorKind.Auth, "No refresh token");
        if (tokens.TryGetProperty("scope", out var granted) && granted.GetString() is { } scopes && !scopes.Split(' ').Contains(Scope))
            throw new CloudException(CloudErrorKind.Permission, "Drive access was not granted"); // the player unticked the Drive box
        accessToken = tokens.GetProperty("access_token").GetString();
        accessUntil = DateTimeOffset.UtcNow.AddSeconds((tokens.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600) - 60);
        vault.Write("Google\n" + refresh);
        string email;
        try
        {
            using var about = await Json(HttpMethod.Get, $"{Api}/about?fields=user(emailAddress,displayName)", null, token);
            var user = about.RootElement.GetProperty("user");
            email = user.TryGetProperty("emailAddress", out var m) && m.GetString() is { Length: > 0 } x ? x : user.TryGetProperty("displayName", out var d) ? d.GetString() ?? "Google" : "Google";
        }
        catch (CloudException) { email = "Google"; }
        vault.Write(email + "\n" + refresh);
        backupsId = null;
        return email;
    }

    // Revokes the access with Google (when online) and forgets it on this PC.
    public async Task DisconnectAsync()
    {
        var refresh = Refresh();
        var account = Account;
        vault.Clear(); accessToken = null; backupsId = null;
        if (account is not null) folderCache.Clear(account);
        if (refresh is null) return;
        try { using var _ = await http.PostAsync("https://oauth2.googleapis.com/revoke", new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refresh })); }
        catch (HttpRequestException) { } // offline: the local credential is gone anyway
    }

    // True when the saved access still works (refreshes it if needed).
    public async Task<bool> ValidateAsync(CancellationToken token)
    {
        try { await AccessAsync(token); return true; }
        catch (CloudException ex) when (ex.Kind == CloudErrorKind.Auth) { return false; }
    }

    // ---------- Files ----------
    public async Task<CloudBackup> UploadAsync(Stream data, string name, CancellationToken token)
    {
        var folder = await BackupsFolderAsync(token);
        var meta = JsonSerializer.Serialize(new Dictionary<string, object> { ["name"] = name, ["parents"] = new[] { folder } });
        CloudBackup created;
        // Multipart upload in one request; the stream is rewound if a temporary error needs another attempt.
        using (var doc = await Json(HttpMethod.Post, "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id,name,createdTime,size", () =>
        {
            if (data.CanSeek) data.Position = 0;
            var body = new MultipartContent("related");
            body.Add(new StringContent(meta, Encoding.UTF8, "application/json"));
            var file = new StreamContent(new KeepOpenStream(data)); file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream"); // the request disposes its content; the backup stream must survive for a retry
            body.Add(file);
            return body;
        }, token, retry: data.CanSeek))
            created = Item(doc.RootElement);
        await PruneAsync(folder, keep: 5, token); // the 5 newest stay in Drive, so it never fills up
        return created;
    }

    public async Task<IReadOnlyList<CloudBackup>> ListAsync(CancellationToken token)
    {
        var folder = await BackupsFolderAsync(token);
        using var doc = await Json(HttpMethod.Get, $"{Api}/files?" + Query(new()
        {
            ["q"] = $"'{folder}' in parents and trashed=false and mimeType!='{FolderMime}'", ["orderBy"] = "createdTime desc",
            ["fields"] = "files(id,name,createdTime,size)", ["pageSize"] = "100", ["spaces"] = "drive",
        }), null, token);
        return [.. doc.RootElement.GetProperty("files").EnumerateArray().Select(Item)];
    }

    public async Task DownloadAsync(string id, Stream target, CancellationToken token)
    {
        using var response = await SendRaw(HttpMethod.Get, $"{Api}/files/{Uri.EscapeDataString(id)}?alt=media", null, token, HttpCompletionOption.ResponseHeadersRead);
        await response.Content.CopyToAsync(target, token);
    }

    // drive.file only lets Lotus touch files it created, so only Lotus backups can be deleted here.
    public async Task DeleteAsync(string id, CancellationToken token)
    {
        using var _ = await SendRaw(HttpMethod.Delete, $"{Api}/files/{Uri.EscapeDataString(id)}", null, token);
    }

    // "Lotus/Backups": the saved ids are checked first; otherwise the folders Lotus created before are found (the
    // oldest wins if there are several) and only missing ones are created. One at a time, so no duplicates.
    public async Task<string> BackupsFolderAsync(CancellationToken token)
    {
        if (backupsId is not null) return backupsId;
        await folderLock.WaitAsync(token);
        try
        {
            if (backupsId is not null) return backupsId;
            var account = Account ?? "Google";
            var (cachedLotus, cachedBackups) = folderCache.Read(account);
            if (cachedLotus is not null && cachedBackups is not null && await IsLiveFolderAsync(cachedLotus, token) && await IsLiveFolderAsync(cachedBackups, token))
                return backupsId = cachedBackups;
            var lotus = await FindFolderAsync(RootFolder, "root", token) ?? await CreateFolderAsync(RootFolder, "root", token);
            var backups = await FindFolderAsync(BackupsFolder, lotus, token) ?? await CreateFolderAsync(BackupsFolder, lotus, token);
            folderCache.Write(account, lotus, backups);
            return backupsId = backups;
        }
        finally { folderLock.Release(); }
    }

    private async Task<bool> IsLiveFolderAsync(string id, CancellationToken token)
    {
        try
        {
            using var doc = await Json(HttpMethod.Get, $"{Api}/files/{Uri.EscapeDataString(id)}?fields=id,mimeType,trashed", null, token);
            return doc.RootElement.TryGetProperty("mimeType", out var m) && m.GetString() == FolderMime && !(doc.RootElement.TryGetProperty("trashed", out var t) && t.GetBoolean());
        }
        catch (CloudException ex) when (ex.Kind == CloudErrorKind.NotFound) { return false; }
    }

    private async Task<string?> FindFolderAsync(string name, string parent, CancellationToken token)
    {
        using var doc = await Json(HttpMethod.Get, $"{Api}/files?" + Query(new()
        {
            ["q"] = $"name='{name}' and mimeType='{FolderMime}' and '{parent}' in parents and trashed=false",
            ["orderBy"] = "createdTime", ["fields"] = "files(id)", ["pageSize"] = "10", ["spaces"] = "drive",
        }), null, token);
        var files = doc.RootElement.GetProperty("files");
        return files.GetArrayLength() > 0 ? files[0].GetProperty("id").GetString() : null;
    }

    private async Task<string> CreateFolderAsync(string name, string parent, CancellationToken token)
    {
        var meta = JsonSerializer.Serialize(new Dictionary<string, object> { ["name"] = name, ["mimeType"] = FolderMime, ["parents"] = new[] { parent } });
        using var doc = await Json(HttpMethod.Post, $"{Api}/files?fields=id", () => new StringContent(meta, Encoding.UTF8, "application/json"), token);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private async Task PruneAsync(string folder, int keep, CancellationToken token)
    {
        try { foreach (var old in (await ListAsync(token)).Skip(keep)) await DeleteAsync(old.Id, token); }
        catch (CloudException) { } // pruning is a nicety
    }

    // ---------- HTTP ----------
    private async Task<JsonDocument> Json(HttpMethod method, string url, Func<HttpContent>? content, CancellationToken token, bool retry = true)
    {
        using var response = await SendRaw(method, url, content, token, retry: retry);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
    }

    // Temporary problems (429, 5xx, rate limits) are tried again with growing pauses; a 401 refreshes the access once.
    private async Task<HttpResponseMessage> SendRaw(HttpMethod method, string url, Func<HttpContent>? content, CancellationToken token,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead, bool retry = true)
    {
        var refreshed = false;
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, url) { Content = content?.Invoke() };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessAsync(token));
            HttpResponseMessage response;
            try { response = await http.SendAsync(request, completion, token); }
            catch (HttpRequestException ex)
            {
                if (retry && attempt < 4) { await wait(Backoff(attempt), token); continue; }
                throw new CloudException(CloudErrorKind.Network, ex.Message, ex);
            }
            catch (TaskCanceledException ex) when (!token.IsCancellationRequested)
            {
                if (retry && attempt < 4) { await wait(Backoff(attempt), token); continue; }
                throw new CloudException(CloudErrorKind.Network, "Request timed out", ex);
            }
            if (response.IsSuccessStatusCode) return response;
            var body = await response.Content.ReadAsStringAsync(token);
            var reason = Reason(body);
            var status = (int)response.StatusCode;
            response.Dispose();
            if (status == 401 && !refreshed) { accessToken = null; refreshed = true; continue; }
            var temporary = status is 429 or 500 or 502 or 503 or 504 || status == 403 && reason is "rateLimitExceeded" or "userRateLimitExceeded" or "backendError";
            if (temporary && retry && attempt < 5) { await wait(Backoff(attempt), token); continue; }
            throw new CloudException(status switch
            {
                401 => CloudErrorKind.Auth,
                404 => CloudErrorKind.NotFound,
                403 when reason is "storageQuotaExceeded" or "quotaExceeded" => CloudErrorKind.StorageFull,
                403 when temporary => CloudErrorKind.Quota,
                429 => CloudErrorKind.Quota,
                403 => CloudErrorKind.Permission,
                _ => CloudErrorKind.Network,
            }, $"Google Drive {status} {reason}");
        }
    }

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromMilliseconds(Math.Min(16000, 1000 * Math.Pow(2, attempt - 1)) + Random.Shared.Next(0, 400));

    private static string? Reason(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
            {
                if (err.TryGetProperty("errors", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0 && list[0].TryGetProperty("reason", out var r)) return r.GetString();
                if (err.TryGetProperty("status", out var s)) return s.GetString();
            }
        }
        catch (JsonException) { }
        return null;
    }

    private async Task<string> AccessAsync(CancellationToken token)
    {
        if (accessToken is not null && DateTimeOffset.UtcNow < accessUntil) return accessToken;
        if (config is null) throw new CloudException(CloudErrorKind.NotConfigured, "Google client not configured");
        if (Refresh() is not { } refresh) throw new CloudException(CloudErrorKind.Auth, "Not connected");
        var t = await Token(new() { ["client_id"] = config.ClientId, ["client_secret"] = config.ClientSecret, ["refresh_token"] = refresh, ["grant_type"] = "refresh_token" }, token);
        accessToken = t.GetProperty("access_token").GetString();
        accessUntil = DateTimeOffset.UtcNow.AddSeconds((t.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600) - 60);
        return accessToken!;
    }

    private async Task<JsonElement> Token(Dictionary<string, string> form, CancellationToken token)
    {
        HttpResponseMessage response;
        try { response = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form), token); }
        catch (HttpRequestException ex) { throw new CloudException(CloudErrorKind.Network, ex.Message, ex); }
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode)
            {
                // Revoked in the Google account, password changed or expired: forget it and ask to sign in again.
                if (text.Contains("invalid_grant")) { vault.Clear(); accessToken = null; backupsId = null; }
                throw new CloudException((int)response.StatusCode >= 500 ? CloudErrorKind.Network : CloudErrorKind.Auth, "Google token request failed");
            }
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
    }

    private string? Refresh() => vault.Read()?.Split('\n', 2) is [_, var r] && r.Length > 0 ? r : null;

    private static CloudBackup Item(JsonElement e) => new(e.GetProperty("id").GetString()!, e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
        e.TryGetProperty("createdTime", out var c) && DateTimeOffset.TryParse(c.GetString(), out var at) ? at : DateTimeOffset.MinValue,
        e.TryGetProperty("size", out var s) && long.TryParse(s.GetString(), out var size) ? size : 0);

    private static async Task Reply(HttpListenerContext context, bool ok)
    {
        var html = ok
            ? "<html><head><meta charset='utf-8'></head><body style='font-family:Segoe UI;background:#170b13;color:#f5eff3;text-align:center;padding-top:80px'><h2>Lotus conectado ao Google Drive ✓</h2><p>Pode fechar esta aba e voltar ao Lotus.</p></body></html>"
            : "<html><head><meta charset='utf-8'></head><body style='font-family:Segoe UI;background:#170b13;color:#f5eff3;text-align:center;padding-top:80px'><h2>Conexão não concluída</h2><p>Volte ao Lotus e tente de novo.</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start(); var port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
        return port;
    }
    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Query(Dictionary<string, string> q) => string.Join("&", q.Select(kv => kv.Key + "=" + Uri.EscapeDataString(kv.Value)));
}

// Lets an HTTP request read a stream without closing it, so a retried upload can rewind and send it again.
internal sealed class KeepOpenStream(Stream inner) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => inner.ReadAsync(buffer, token);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => inner.ReadAsync(buffer, offset, count, token);
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { } // the owner closes the real stream
}