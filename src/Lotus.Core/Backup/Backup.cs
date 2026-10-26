namespace Lotus.Core.Backup;

// What a backup holds, shown before restoring.
public sealed record BackupSummary(string Player, int DiaryGames, int Achievements, int Favorites, int Benchmarks);
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
}

public sealed class BackupFormatException(string message) : Exception(message);
