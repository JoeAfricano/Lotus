using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Lotus.Core.Updates;
using Lotus.Infrastructure.Updates;
using Microsoft.Win32;

namespace Lotus.Infrastructure.PortsLink;

/// <summary>
/// Lotus Ports for players who want it: is it installed, and installing it from its official GitHub release. The setup
/// is downloaded only after the player clicks Install, checked against the SHA-256 published with the release, and then
/// opened normally: Lotus Ports' own installer shows its wizard, so the player confirms every step there.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PortsOffer(IUpdateSource? source = null, string? installDir = null)
{
    public const string RepositoryUrl = "https://github.com/JoeAfricano/LotusPorts";
    private readonly IUpdateSource source = source ?? new GitHubUpdateSource("JoeAfricano", "LotusPorts");
    private string InstallDir => installDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Lotus Ports");

    /// <summary>The installed Lotus Ports program, or null: its integration entry, then its install folder (app\ or the older layout).</summary>
    public string? InstalledExe()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Lotus.Integration.Contracts.Protocol.PortsRegistryKey);
            if (installDir is null && key?.GetValue(Lotus.Integration.Contracts.RegistryValueNames.ExePath) is string registered && File.Exists(registered)) return registered;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        foreach (var candidate in new[] { Path.Combine(InstallDir, "app", "LotusPorts.exe"), Path.Combine(InstallDir, "LotusPorts.exe") })
            if (File.Exists(candidate)) return candidate;
        return null;
    }

    public bool IsInstalled => InstalledExe() is not null;

    /// <summary>The newest published release (pre-releases included), or null when GitHub has none with an installer.</summary>
    public Task<UpdateRelease?> LatestAsync(CancellationToken token = default) => source.LatestAsync(token);

    /// <summary>
    /// Downloads the setup to a private temporary folder and checks it. Returns the file only when its SHA-256 matches the
    /// one published with the release; a missing or different hash means nothing is run.
    /// </summary>
    public async Task<string?> DownloadAsync(UpdateRelease release, IProgress<double>? progress = null, CancellationToken token = default)
    {
        if (release.Sha256 is not { Length: 64 } expected) return null;
        var dir = Path.Combine(Path.GetTempPath(), "Lotus-PortsSetup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, Path.GetFileName(release.InstallerName));
        await using (var target = File.Create(file))
            await source.DownloadAsync(release, target, new Progress<long>(done => progress?.Report(release.Size > 0 ? Math.Min(99, done * 100.0 / release.Size) : 0)), token);
        if (!Verify(file, expected)) { TryDelete(dir); return null; }
        progress?.Report(100);
        return file;
    }

    public static bool Verify(string file, string expectedSha256)
    {
        using var stream = File.OpenRead(file);
        return string.Equals(Convert.ToHexString(SHA256.HashData(stream)), expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Opens the checked setup with its wizard (per-user, no administrator) and waits for it to finish.</summary>
    public async Task<bool> RunSetupAsync(string setupFile, CancellationToken token = default)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(setupFile) { UseShellExecute = true });
            if (process is null) return false;
            await process.WaitForExitAsync(token);
            return IsInstalled;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
        finally { TryDelete(Path.GetDirectoryName(setupFile)!); }
    }

    public bool Open()
    {
        if (InstalledExe() is not { } exe) return false;
        try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! })?.Dispose(); return true; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private static void TryDelete(string dir)
    {
        if (!dir.Contains("Lotus-PortsSetup-", StringComparison.Ordinal)) return; // only the folder this class created
        try { Directory.Delete(dir, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
