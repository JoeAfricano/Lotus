namespace Lotus.Core.Backup;

// What a backup holds, shown before restoring.
public sealed record BackupSummary(string Player, int DiaryGames, int Achievements, int Favorites, int Benchmarks, int Saves = 0); // Saves: games whose latest save copy is inside
public sealed record BackupManifest(int Format, string AppVersion, DateTimeOffset CreatedAt, string Machine, IReadOnlyList<string> Files, BackupSummary Summary, string? AvatarOriginal = null)
{
    public const int CurrentFormat = 1;
}

// One file (.lotusbackup) with the player's settings and lists. Restoring happens in two steps: the file is staged,
// Lotus restarts and applies it before anything is loaded, so nothing in memory can overwrite the restored data.
public interface IBackupArchive
{
    string FileExtension { get; }
    BackupManifest Write(Stream output);
    BackupManifest Inspect(Stream input);
    void Stage(Stream input);                 // validated copy kept for the next start
    bool HasPending { get; }
    BackupManifest? ApplyPending();           // at startup: safety copy of the current data, then restore
    string? AutoBackup(TimeSpan every, int keep);
    string BackupFolder { get; }
    void StageReset();                        // "Restore Lotus": applied at the next start
    bool HasPendingReset { get; }
    string? ApplyPendingReset();              // safety copy, then the player's data is archived (never deleted)
}

public sealed record CloudBackup(string Id, string Name, DateTimeOffset Created, long Size);

// Optional copy in the player's Google Drive (a "Lotus" folder the app creates and only sees itself).
public interface ICloudBackup
{
    bool Configured { get; }
    string? Account { get; }
    Task<string> ConnectAsync(CancellationToken token);
    Task DisconnectAsync();
    Task<CloudBackup> UploadAsync(Stream data, string name, CancellationToken token);
    Task<IReadOnlyList<CloudBackup>> ListAsync(CancellationToken token);
    Task DownloadAsync(string id, Stream target, CancellationToken token);
    bool ImportClient(string path) => false;
    Task DeleteAsync(string id, CancellationToken token) => throw new NotSupportedException(); // a Google Cloud client file chosen by the player
}

public sealed class BackupFormatException(string message) : Exception(message);

// Why a Google Drive operation failed, so the app can say it in plain words.
public enum CloudErrorKind { NotConfigured, Auth, Network, Permission, Quota, StorageFull, NotFound }
public sealed class CloudException(CloudErrorKind kind, string message, Exception? inner = null) : Exception(message, inner) { public CloudErrorKind Kind { get; } = kind; }
