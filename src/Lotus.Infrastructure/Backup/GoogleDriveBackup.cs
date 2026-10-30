using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lotus.Core.Backup;

namespace Lotus.Infrastructure.Backup;

public sealed record GoogleOAuthConfig(string ClientId, string ClientSecret)
{
    // google-oauth.json next to Lotus.exe ({"client_id": "...", "client_secret": "..."}), from a "Desktop app" OAuth
    // client in Google Cloud. Without it the Google Drive option explains that it is not set up in this build.
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

// Where the refresh token lives (Windows Credential Locker in the app; memory in tests).
public interface ITokenVault { string? Read(); void Write(string value); void Clear(); }


// Google Drive copy of the backup. Sign-in happens in the player's browser (OAuth for desktop apps: loopback redirect
// with PKCE); Lotus never sees the password. Scope drive.file: Lotus only sees the files it created itself.
public sealed class GoogleDriveBackup(GoogleOAuthConfig? initial, ITokenVault vault, Func<Uri, Task> openBrowser, HttpClient? http = null) : ICloudBackup
{
    private GoogleOAuthConfig? config = initial;
    private const string Scope = "https://www.googleapis.com/auth/drive.file openid email";
    private const string FolderName = "Lotus Backups";
    private readonly HttpClient http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    private string? accessToken;
    private DateTimeOffset accessUntil;

    public bool Configured => config is not null;

    // The Google Cloud "Desktop app" client file (as Google offers it for download), kept in %LOCALAPPDATA%\Lotus so
    // this PC can use Google Drive even in a build that has no client inside.
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

    public async Task<string> ConnectAsync(CancellationToken token)
    {
        if (config is null) throw new InvalidOperationException("Google client not configured");
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        var port = FreePort();
        var redirect = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirect);
        listener.Start();
        var url = "https://accounts.google.com/o/oauth2/v2/auth?" + Query(new()
        {
            ["client_id"] = config.ClientId, ["redirect_uri"] = redirect, ["response_type"] = "code", ["scope"] = Scope,
            ["code_challenge"] = challenge, ["code_challenge_method"] = "S256", ["state"] = state, ["access_type"] = "offline", ["prompt"] = "consent",
        });
        await openBrowser(new Uri(url));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var reg = timeout.Token.Register(() => { try { listener.Stop(); } catch (ObjectDisposedException) { } });
        HttpListenerContext context;
        try { context = await listener.GetContextAsync(); }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException) { throw new OperationCanceledException("Sign-in timed out or was cancelled"); }
        var q = System.Web.HttpUtility.ParseQueryString(context.Request.Url?.Query ?? "");
        var ok = q["state"] == state && q["code"] is { Length: > 0 };
        await Reply(context, ok);
        if (!ok) throw new UnauthorizedAccessException(q["error"] ?? "Sign-in was not completed");

        var tokens = await Token(new()
        {
            ["code"] = q["code"]!, ["client_id"] = config.ClientId, ["client_secret"] = config.ClientSecret, ["redirect_uri"] = redirect,
            ["grant_type"] = "authorization_code", ["code_verifier"] = verifier,
        }, token);
        var refresh = tokens.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        if (refresh is null) throw new UnauthorizedAccessException("No refresh token");
        var email = tokens.TryGetProperty("id_token", out var idt) ? EmailFrom(idt.GetString()) : null;
        vault.Write((email ?? "Google") + "\n" + refresh);
        return email ?? "Google";
    }

    public async Task DisconnectAsync()
    {
        var refresh = Refresh();
        vault.Clear(); accessToken = null;
        if (refresh is null) return;
        try { using var _ = await http.PostAsync("https://oauth2.googleapis.com/revoke", new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refresh })); }
        catch (HttpRequestException) { } // offline: the local token is gone anyway
    }

    public async Task<CloudBackup> UploadAsync(Stream data, string name, CancellationToken token)
    {
        var folder = await FolderAsync(token);
        var meta = JsonSerializer.Serialize(new Dictionary<string, object> { ["name"] = name, ["parents"] = new[] { folder } });
        using var body = new MultipartContent("related");
        body.Add(new StringContent(meta, Encoding.UTF8, "application/json"));
        var file = new StreamContent(data); file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        body.Add(file);
        using var doc = await Send(HttpMethod.Post, "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id,name,createdTime,size", body, token);
        var created = Item(doc.RootElement);
        await PruneAsync(folder, keep: 5, token); // the 5 newest stay in Drive, so it never fills up
        return created;
    }

    public async Task<IReadOnlyList<CloudBackup>> ListAsync(CancellationToken token)
    {
        var folder = await FolderAsync(token);
        using var doc = await Send(HttpMethod.Get, "https://www.googleapis.com/drive/v3/files?" + Query(new()
        {
            ["q"] = $"'{folder}' in parents and trashed=false", ["orderBy"] = "createdTime desc", ["fields"] = "files(id,name,createdTime,size)", ["pageSize"] = "50",
        }), null, token);
        return [.. doc.RootElement.GetProperty("files").EnumerateArray().Select(Item)];
    }

    public async Task DownloadAsync(string id, Stream target, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(id)}?alt=media");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessAsync(token));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await response.Content.CopyToAsync(target, token);
    }

    // Only files Lotus created itself can be seen (drive.file), so only Lotus backups can be deleted here.
    public async Task DeleteAsync(string id, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(id)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessAsync(token));
        using var response = await http.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
    }

    // The "Lotus Backups" folder (created on first use).
    private async Task<string> FolderAsync(CancellationToken token)
    {
        using (var found = await Send(HttpMethod.Get, "https://www.googleapis.com/drive/v3/files?" + Query(new()
        {
            ["q"] = $"name='{FolderName}' and mimeType='application/vnd.google-apps.folder' and trashed=false", ["fields"] = "files(id)",
        }), null, token))
        {
            var files = found.RootElement.GetProperty("files");
            if (files.GetArrayLength() > 0) return files[0].GetProperty("id").GetString()!;
        }
        var meta = JsonSerializer.Serialize(new Dictionary<string, string> { ["name"] = FolderName, ["mimeType"] = "application/vnd.google-apps.folder" });
        using var made = await Send(HttpMethod.Post, "https://www.googleapis.com/drive/v3/files?fields=id", new StringContent(meta, Encoding.UTF8, "application/json"), token);
        return made.RootElement.GetProperty("id").GetString()!;
    }

    private async Task PruneAsync(string folder, int keep, CancellationToken token)
    {
        try
        {
            var all = await ListAsync(token);
            foreach (var old in all.Skip(keep))
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete, $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(old.Id)}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessAsync(token));
                using var _ = await http.SendAsync(request, token);
            }
        }
        catch (HttpRequestException) { } // pruning is a nicety
    }

    private async Task<JsonDocument> Send(HttpMethod method, string url, HttpContent? content, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessAsync(token));
        using var response = await http.SendAsync(request, token);
        if (response.StatusCode == HttpStatusCode.Unauthorized) { accessToken = null; throw new UnauthorizedAccessException("Google session expired"); }
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
    }

    private async Task<string> AccessAsync(CancellationToken token)
    {
        if (accessToken is not null && DateTimeOffset.UtcNow < accessUntil) return accessToken;
        if (config is null || Refresh() is not { } refresh) throw new UnauthorizedAccessException("Not connected");
        var t = await Token(new() { ["client_id"] = config.ClientId, ["client_secret"] = config.ClientSecret, ["refresh_token"] = refresh, ["grant_type"] = "refresh_token" }, token);
        accessToken = t.GetProperty("access_token").GetString();
        accessUntil = DateTimeOffset.UtcNow.AddSeconds((t.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600) - 60);
        return accessToken!;
    }

    private async Task<JsonElement> Token(Dictionary<string, string> form, CancellationToken token)
    {
        using var response = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form), token);
        var text = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
        {
            if (text.Contains("invalid_grant")) vault.Clear(); // revoked in the Google account: sign in again
            throw new UnauthorizedAccessException("Google token request failed");
        }
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private string? Refresh() => vault.Read()?.Split('\n', 2) is [_, var r] && r.Length > 0 ? r : null;

    private static CloudBackup Item(JsonElement e) => new(e.GetProperty("id").GetString()!, e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
        e.TryGetProperty("createdTime", out var c) && DateTimeOffset.TryParse(c.GetString(), out var at) ? at : DateTimeOffset.MinValue,
        e.TryGetProperty("size", out var s) && long.TryParse(s.GetString(), out var size) ? size : 0);

    private static string? EmailFrom(string? idToken)
    {
        var parts = idToken?.Split('.');
        if (parts is not { Length: >= 2 }) return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            return doc.RootElement.TryGetProperty("email", out var email) ? email.GetString() : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException) { return null; }
    }

    private static async Task Reply(HttpListenerContext context, bool ok)
    {
        var html = ok
            ? "<html><body style='font-family:Segoe UI;background:#170b13;color:#f5eff3;text-align:center;padding-top:80px'><h2>Lotus conectado ao Google Drive ✓</h2><p>Pode fechar esta aba e voltar ao Lotus.</p></body></html>"
            : "<html><body style='font-family:Segoe UI;background:#170b13;color:#f5eff3;text-align:center;padding-top:80px'><h2>Conexão não concluída</h2><p>Volte ao Lotus e tente de novo.</p></body></html>";
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
