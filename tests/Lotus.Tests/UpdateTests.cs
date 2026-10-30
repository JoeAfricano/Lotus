using System.Net;
using System.Security.Cryptography;
using System.Text;
using Lotus.Core;
using Lotus.Core.Updates;
using Lotus.Infrastructure.Updates;
using Xunit;

namespace Lotus.Tests;

public sealed class UpdateTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "lotus-upd-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

    private sealed class FakeSource(UpdateRelease? release, byte[] payload) : IUpdateSource
    {
        public Task<UpdateRelease?> LatestAsync(CancellationToken token) => Task.FromResult(release);
        public async Task DownloadAsync(UpdateRelease r, Stream target, IProgress<long> progress, CancellationToken token)
        {
            for (var i = 0; i < payload.Length; i += 1000) { token.ThrowIfCancellationRequested(); await target.WriteAsync(payload.AsMemory(i, Math.Min(1000, payload.Length - i)), token); progress.Report(Math.Min(payload.Length, i + 1000)); }
        }
    }
    private sealed class FakeInstaller : IUpdateInstaller { public string? Path; public void Install(string installerPath) => Path = installerPath; }

    private static UpdateRelease Release(string tag, byte[] payload, string? sha = null) =>
        new(UpdateVersions.Parse(tag)!, tag, "Lotus " + tag, "## Novo\n- **Aba** nova", DateTimeOffset.Now, "https://github.com/x/y/releases/download/a/Lotus-Setup.exe", "Lotus-Setup.exe", payload.Length,
            sha ?? Convert.ToHexString(SHA256.HashData(payload)), "https://github.com/x/y/releases/tag/" + tag);

    [Theory]
    [InlineData("v0.7.0-alpha", "0.7.0")] [InlineData("0.6.1", "0.6.1")] [InlineData("v1.2", "1.2.0")] [InlineData("V2.0.3-beta+abc", "2.0.3")]
    public void TagsBecomeVersions(string tag, string expected) => Assert.Equal(Version.Parse(expected), UpdateVersions.Parse(tag));

    [Fact] public void OnlyHigherVersionsCount()
    {
        Assert.True(UpdateVersions.IsNewer(new(0, 7, 0), new(0, 6, 0, 0)));
        Assert.False(UpdateVersions.IsNewer(new(0, 6, 0), new(0, 6, 0, 0)));
        Assert.False(UpdateVersions.IsNewer(new(0, 5, 1), new(0, 6, 0)));
        Assert.Null(UpdateVersions.Parse("nightly"));
    }

    [Fact] public async Task NewVersionIsFoundDownloadedCheckedAndHandedToTheInstaller()
    {
        var payload = Encoding.UTF8.GetBytes(new string('x', 5000));
        var installer = new FakeInstaller();
        var vm = new UpdateViewModel(new FakeSource(Release("v0.7.0-alpha", payload), payload), installer, new(0, 6, 0), new TextCatalog(), ex => throw ex, dir);
        UpdateRelease? found = null; vm.Found += (_, r) => found = r;
        Assert.True(await vm.CheckAsync());
        Assert.Equal(UpdateState.Available, vm.State); Assert.NotNull(found); Assert.True(vm.CanInstall);
        Assert.True(await vm.DownloadAsync());
        Assert.Equal(UpdateState.Ready, vm.State); Assert.Equal(1, vm.Progress);
        Assert.Equal(payload, File.ReadAllBytes(vm.ReadyFile!));
        Assert.True(vm.Install()); Assert.Equal(vm.ReadyFile, installer.Path);
    }

    [Fact] public async Task SameVersionIsUpToDateAndATamperedFileIsNeverInstalled()
    {
        var payload = new byte[3000];
        var same = new UpdateViewModel(new FakeSource(Release("v0.6.0-alpha", payload), payload), new FakeInstaller(), new(0, 6, 0), new TextCatalog(), ex => throw ex, dir);
        Assert.False(await same.CheckAsync()); Assert.Equal(UpdateState.UpToDate, same.State); Assert.False(same.CanInstall);
        var installer = new FakeInstaller();
        var bad = new UpdateViewModel(new FakeSource(Release("v0.7.0", payload, sha: new string('0', 64)), payload), installer, new(0, 6, 0), new TextCatalog(), ex => throw ex, dir);
        await bad.CheckAsync();
        Assert.False(await bad.DownloadAsync());
        Assert.Equal(UpdateState.Failed, bad.State); Assert.False(bad.Install()); Assert.Null(installer.Path);
        Assert.Empty(Directory.GetFiles(dir));                                     // the bad download was deleted
        var noSha = new UpdateViewModel(new FakeSource(Release("v0.7.0", payload) with { Sha256 = null }, payload), installer, new(0, 6, 0), new TextCatalog(), ex => throw ex, dir);
        await noSha.CheckAsync(); Assert.False(await noSha.DownloadAsync());     // no published hash: not installed
    }

    [Fact] public async Task OfflineCheckFailsQuietly()
    {
        var vm = new UpdateViewModel(new ThrowingSource(), new FakeInstaller(), new(0, 6, 0), new TextCatalog(), _ => { }, dir);
        Assert.False(await vm.CheckAsync(quiet: true));
        Assert.Equal(UpdateState.Failed, vm.State); Assert.Equal("", vm.Status);
    }
    private sealed class ThrowingSource : IUpdateSource
    {
        public Task<UpdateRelease?> LatestAsync(CancellationToken token) => throw new HttpRequestException("offline");
        public Task DownloadAsync(UpdateRelease r, Stream t, IProgress<long> p, CancellationToken c) => throw new HttpRequestException("offline");
    }

    // GitHub's releases list: the highest version wins (pre-releases included, drafts not), only GitHub https assets.
    [Fact] public async Task GitHubReleasesAreReadSafely()
    {
        const string json = """
        [
          {"tag_name":"v0.8.0-alpha","draft":true,"name":"draft","assets":[{"name":"Lotus-Setup-0.8.0-alpha.exe","browser_download_url":"https://github.com/a/b/releases/download/v0.8.0-alpha/Lotus-Setup-0.8.0-alpha.exe","size":1}]},
          {"tag_name":"v0.7.0-alpha","draft":false,"prerelease":true,"name":"Lotus 0.7.0-alpha","body":"notes","html_url":"https://github.com/a/b/releases/tag/v0.7.0-alpha","published_at":"2026-10-31T10:00:00Z",
           "assets":[{"name":"Lotus-Setup-0.7.0-alpha.exe","browser_download_url":"https://github.com/a/b/releases/download/v0.7.0-alpha/Lotus-Setup-0.7.0-alpha.exe","size":73000000},
                     {"name":"Lotus-Setup-0.7.0-alpha.exe.sha256","browser_download_url":"https://github.com/a/b/releases/download/v0.7.0-alpha/Lotus-Setup-0.7.0-alpha.exe.sha256","size":99}]},
          {"tag_name":"v0.9.0","draft":false,"name":"evil","assets":[{"name":"Lotus-Setup-0.9.0.exe","browser_download_url":"https://evil.example.com/Lotus-Setup.exe","size":1}]},
          {"tag_name":"v0.6.0-alpha","draft":false,"name":"old","assets":[{"name":"Lotus-New-Setup-0.6.0-alpha.exe","browser_download_url":"https://github.com/a/b/releases/download/v0.6.0-alpha/Lotus-New-Setup-0.6.0-alpha.exe","size":1}]}
        ]
        """;
        var http = new HttpClient(new Handler(url => url.EndsWith(".sha256") ? "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789  Lotus-Setup-0.7.0-alpha.exe" : json));
        var r = await new GitHubUpdateSource("a", "b", http).LatestAsync(CancellationToken.None);
        Assert.NotNull(r);
        Assert.Equal(new Version(0, 7, 0), r!.Version);                      // draft and untrusted host skipped
        Assert.Equal("Lotus-Setup-0.7.0-alpha.exe", r.InstallerName); Assert.Equal(73000000, r.Size);
        Assert.Equal("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789", r.Sha256);
    }
    private sealed class Handler(Func<string, string> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply(request.RequestUri!.ToString())), RequestMessage = request });
    }
}
