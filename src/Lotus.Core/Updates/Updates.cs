using System.Globalization;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Updates;

// A published Lotus version (a GitHub release with its installer).
public sealed record UpdateRelease(Version Version, string Tag, string Title, string Notes, DateTimeOffset Published, string InstallerUrl, string InstallerName,
    long Size, string? Sha256, string PageUrl);

public interface IUpdateSource
{
    Task<UpdateRelease?> LatestAsync(CancellationToken token);                                       // newest release, or null
    Task DownloadAsync(UpdateRelease release, Stream target, IProgress<long> progress, CancellationToken token);
}

// Starts the downloaded installer (silent, reopening Lotus afterwards); the app then closes so its files can be replaced.
public interface IUpdateInstaller { void Install(string installerPath); }

public enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, Verifying, Ready, Installing, Failed }

public static class UpdateVersions
{
    // "v0.7.0-alpha", "0.7.0", "v1.2" → 0.7.0 / 1.2.0 (suffixes such as -alpha are ignored: every build so far is alpha).
    public static Version? Parse(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var core = tag.Trim().TrimStart('v', 'V').Split('-', '+', ' ')[0];
        if (!Version.TryParse(core.Contains('.') ? core : core + ".0", out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }
    public static bool IsNewer(Version candidate, Version current) => candidate > new Version(current.Major, current.Minor, Math.Max(0, current.Build));
}

// Settings › Atualizações. Checks GitHub on request (and quietly at start), downloads with progress, checks the
// SHA-256 published with the release, then hands the installer over. Nothing is installed without a click, unless
// the player turned on automatic updates.
public sealed partial class UpdateViewModel(IUpdateSource source, IUpdateInstaller installer, Version current, ITextCatalog text, Action<Exception> log, string? downloadFolder = null) : ObservableObject
{
    private readonly string folder = downloadFolder ?? Path.Combine(Path.GetTempPath(), "Lotus-Update");
    private CancellationTokenSource? cts;
    private DateTimeOffset started;
    private CultureInfo Culture => CultureInfo.GetCultureInfo(Text.Language);

    public ITextCatalog Text { get; } = text;
    public Version Current { get; } = current;
    public string CurrentText => Text.Format("UpCurrent", $"{Current.Major}.{Current.Minor}.{Math.Max(0, Current.Build)}-alpha");
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Busy), nameof(IsDownloading), nameof(HasUpdate), nameof(CanInstall), nameof(CanCheck), nameof(IsReady))] private UpdateState state;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasUpdate), nameof(LatestTitle), nameof(LatestDate), nameof(LatestNotes), nameof(LatestSize))] private UpdateRelease? latest;
    [ObservableProperty] private double progress;       // 0..1
    [ObservableProperty] private string progressText = "";
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string lastChecked = "";
    public string? ReadyFile { get; private set; }

    public Func<bool> AutoEnabled { get; set; } = () => false;
    public Action<bool> SetAuto { get; set; } = _ => { };
    public bool AutoUpdate { get => AutoEnabled(); set { if (value != AutoEnabled()) { SetAuto(value); OnPropertyChanged(); } } }

    public bool Busy => State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Verifying or UpdateState.Installing;
    public bool CanCheck => !Busy;
    public bool IsDownloading => State is UpdateState.Downloading or UpdateState.Verifying;
    public bool IsReady => State == UpdateState.Ready;
    public bool HasUpdate => Latest is not null && UpdateVersions.IsNewer(Latest.Version, Current);
    public bool CanInstall => HasUpdate && State is UpdateState.Available or UpdateState.Ready or UpdateState.Failed;
    public string LatestTitle => Latest is { } l ? Text.Format("UpNewVersion", l.Tag.TrimStart('v')) : "";
    public string LatestDate => Latest is { } l ? Text.Format("UpPublished", l.Published.ToLocalTime().ToString("d", Culture)) : "";
    public string LatestNotes => Latest?.Notes ?? "";
    public string LatestSize => Latest is { Size: > 0 } l ? Size(l.Size) : "";

    public event EventHandler<UpdateRelease>? Found;

    public string Size(long bytes) => (bytes / (double)(1L << 20)).ToString(bytes < 10L << 20 ? "0.0" : "0", Culture) + " MB";

    // quiet: the start-up check (no "up to date" message, failures only in the log).
    public async Task<bool> CheckAsync(bool quiet = false)
    {
        if (Busy) return HasUpdate;
        State = UpdateState.Checking; Status = quiet ? Status : Text["UpChecking"];
        try
        {
            var release = await source.LatestAsync(CancellationToken.None);
            LastChecked = Text.Format("UpLastChecked", DateTime.Now.ToString("t", Culture));
            Latest = release;
            if (release is not null && UpdateVersions.IsNewer(release.Version, Current))
            {
                State = ReadyFile is not null && File.Exists(ReadyFile) ? UpdateState.Ready : UpdateState.Available;
                Status = Text.Format("UpAvailable", release.Tag.TrimStart('v'));
                Found?.Invoke(this, release);
                return true;
            }
            State = UpdateState.UpToDate; Status = Text["UpToDate"];
            return false;
        }
        catch (Exception ex)
        {
            log(ex);
            State = UpdateState.Failed; Status = quiet ? "" : Text["UpCheckFailed"];
            return false;
        }
    }

    // Downloads (with progress) and checks the installer; true when it is ready to install.
    public async Task<bool> DownloadAsync()
    {
        if (Latest is not { } release || !HasUpdate || Busy) return false;
        if (State == UpdateState.Ready && ReadyFile is not null && File.Exists(ReadyFile)) return true;
        cts = new CancellationTokenSource();
        State = UpdateState.Downloading; Progress = 0; Status = Text["UpDownloading"]; started = DateTimeOffset.Now;
        var file = Path.Combine(folder, release.InstallerName);
        var partial = file + ".partial";
        try
        {
            Directory.CreateDirectory(folder);
            var report = new Progress<long>(done => Report(done, release.Size));
            await using (var output = File.Create(partial))
                await source.DownloadAsync(release, output, report, cts.Token);
            State = UpdateState.Verifying; Status = Text["UpVerifying"]; Progress = 1;
            var hash = await Task.Run(() => { using var s = File.OpenRead(partial); return Convert.ToHexString(SHA256.HashData(s)); });
            if (release.Sha256 is not { Length: 64 } expected || !hash.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(partial);
                State = UpdateState.Failed; Status = Text["UpBadFile"];
                return false;
            }
            File.Move(partial, file, overwrite: true);
            ReadyFile = file;
            State = UpdateState.Ready; Status = Text["UpReady"]; ProgressText = "";
            return true;
        }
        catch (OperationCanceledException) { State = UpdateState.Available; Status = Text["UpCancelled"]; ProgressText = ""; Clean(partial); return false; }
        catch (Exception ex) { log(ex); State = UpdateState.Failed; Status = Text["UpDownloadFailed"]; ProgressText = ""; Clean(partial); return false; }
        finally { cts?.Dispose(); cts = null; }
    }
    private static void Clean(string path) { try { File.Delete(path); } catch (IOException) { } }

    private void Report(long done, long total)
    {
        Progress = total > 0 ? Math.Clamp(done / (double)total, 0, 1) : 0;
        var seconds = Math.Max(0.5, (DateTimeOffset.Now - started).TotalSeconds);
        var speed = done / seconds;
        var left = speed > 0 && total > done ? TimeSpan.FromSeconds((total - done) / speed) : TimeSpan.Zero;
        ProgressText = Text.Format("UpProgress", Size(done), Size(total), Size((long)speed) + "/s", left.TotalSeconds < 60 ? $"{(int)left.TotalSeconds} s" : $"{(int)left.TotalMinutes} min");
    }

    public void Cancel() => cts?.Cancel();

    // Hands over to the installer (it closes Lotus, updates and opens it again).
    public bool Install()
    {
        if (ReadyFile is null || !File.Exists(ReadyFile)) return false;
        try { State = UpdateState.Installing; Status = Text["UpInstalling"]; installer.Install(ReadyFile); return true; }
        catch (Exception ex) { log(ex); State = UpdateState.Failed; Status = Text["UpInstallFailed"]; return false; }
    }
}
