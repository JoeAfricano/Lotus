using System.Security.Cryptography;
using System.Text;
using Lotus.Core.Updates;
using Lotus.Infrastructure.PortsLink;
using Xunit;

namespace Lotus.Tests;

// Installing Lotus Ports from Lotus: only a setup whose SHA-256 matches the published one is kept; nothing real runs.
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class PortsOfferTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "lotus-ports-offer-" + Guid.NewGuid().ToString("N"));
    public PortsOfferTests() => Directory.CreateDirectory(dir);
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

    private static readonly byte[] Setup = Encoding.UTF8.GetBytes("pretend installer bytes");
    private static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b));

    private sealed class FakeSource(string? sha) : IUpdateSource
    {
        public Task<UpdateRelease?> LatestAsync(CancellationToken token) => Task.FromResult<UpdateRelease?>(new(new Version(0, 1, 2), "v0.1.2-alpha", "Lotus Ports 0.1.2", "", DateTimeOffset.Now,
            "https://github.com/JoeAfricano/LotusPorts/releases/download/v0.1.2-alpha/LotusPorts-Setup-0.1.2-alpha.exe", "LotusPorts-Setup-0.1.2-alpha.exe", Setup.Length, sha, ""));
        public async Task DownloadAsync(UpdateRelease release, Stream target, IProgress<long> progress, CancellationToken token) { await target.WriteAsync(Setup, token); progress.Report(Setup.Length); }
    }

    [Fact] public async Task OnlyASetupMatchingThePublishedHashIsKept()
    {
        var good = new PortsOffer(new FakeSource(Hash(Setup)), dir);
        var release = (await good.LatestAsync())!;
        var file = await good.DownloadAsync(release);
        Assert.NotNull(file); Assert.Equal(Setup, File.ReadAllBytes(file!));
        Directory.Delete(Path.GetDirectoryName(file)!, true);

        var tampered = new PortsOffer(new FakeSource(new string('0', 64)), dir);
        Assert.Null(await tampered.DownloadAsync((await tampered.LatestAsync())!));          // different hash: refused and removed
        var unsigned = new PortsOffer(new FakeSource(null), dir);
        Assert.Null(await unsigned.DownloadAsync((await unsigned.LatestAsync())!));          // no published hash: nothing downloaded
    }

    [Fact] public void InstalledIsFoundInEitherFolderLayout()
    {
        var offer = new PortsOffer(new FakeSource(null), dir);
        Assert.False(offer.IsInstalled);
        File.WriteAllText(Path.Combine(dir, "LotusPorts.exe"), "old layout");
        Assert.Equal(Path.Combine(dir, "LotusPorts.exe"), offer.InstalledExe());
        Directory.CreateDirectory(Path.Combine(dir, "app")); File.WriteAllText(Path.Combine(dir, "app", "LotusPorts.exe"), "app layout");
        Assert.Equal(Path.Combine(dir, "app", "LotusPorts.exe"), offer.InstalledExe());     // the current layout wins
    }
}
