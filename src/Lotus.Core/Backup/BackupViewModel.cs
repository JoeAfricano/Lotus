using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Backup;

public sealed record CloudBackupItem(string Id, string Name, string DateText, string SizeText);

// Settings › Backup: save/restore a file on this PC and, optionally, a copy in Google Drive.
public sealed partial class BackupViewModel(IBackupArchive archive, ICloudBackup cloud, ITextCatalog text, Action<Exception> log) : ObservableObject
{
    public ITextCatalog Text { get; } = text;
    public IBackupArchive Archive => archive;
    public ObservableCollection<CloudBackupItem> CloudItems { get; } = [];
    [ObservableProperty] private bool busy;
    [ObservableProperty] private string message = "";
    [ObservableProperty] private bool isError;
    private CultureInfo Culture => CultureInfo.GetCultureInfo(Text.Language);

    public string SuggestedName => $"Lotus-{DateTime.Now:yyyy-MM-dd-HHmm}";
    public string Extension => archive.FileExtension;
    public string BackupFolder => archive.BackupFolder;
    public bool CloudConfigured => cloud.Configured;
    public bool CloudConnected => cloud.Configured && cloud.Account is not null;
    public bool CloudCanConnect => cloud.Configured && cloud.Account is null;
    public string CloudAccountText => cloud.Account is { } a ? Text.Format("BkConnectedAs", a) : "";
    public bool HasCloudItems => CloudItems.Count > 0;

    // "Backup of 26/10/2026 19:40 · Joe · 12 games in the diary · 13 achievements · 2 favorites · 3 FPS measurements"
    public string Describe(BackupManifest m)
    {
        var parts = new List<string> { Text.Format("BkFrom", m.CreatedAt.ToLocalTime().ToString("g", Culture), m.Machine) };
        if (m.Summary.Player.Length > 0) parts.Add(m.Summary.Player);
        parts.Add(Text.Format("BkDiary", m.Summary.DiaryGames));
        parts.Add(Text.Format("BkAchievements", m.Summary.Achievements));
        parts.Add(Text.Format("BkFavorites", m.Summary.Favorites));
        if (m.Summary.Benchmarks > 0) parts.Add(Text.Format("BkBenchmarks", m.Summary.Benchmarks));
        return string.Join(" · ", parts);
    }

    public BackupManifest? Save(Stream output)
    {
        try { var m = archive.Write(output); Say(Text.Format("BkSaved", m.Files.Count)); return m; }
        catch (Exception ex) { log(ex); Say(Text["BkSaveFailed"], error: true); return null; }
    }

    public BackupManifest? Inspect(Stream input)
    {
        try { return archive.Inspect(input); }
        catch (BackupFormatException ex) { Say(Text[ex.Message == "newer format" ? "BkNewer" : "BkInvalid"], error: true); return null; }
        catch (Exception ex) { log(ex); Say(Text["BkInvalid"], error: true); return null; }
    }

    // Keeps the backup for the next start; the app restarts right after.
    public bool Stage(Stream input)
    {
        try { archive.Stage(input); return true; }
        catch (Exception ex) { log(ex); Say(Text["BkInvalid"], error: true); return false; }
    }

    public async Task ConnectAsync()
    {
        await Run(async () =>
        {
            var account = await cloud.ConnectAsync(CancellationToken.None);
            Say(Text.Format("BkConnectedAs", account));
            await LoadCloudAsync();
        }, "BkConnectFailed");
    }

    public async Task DisconnectAsync()
    {
        await Run(async () => { await cloud.DisconnectAsync(); CloudItems.Clear(); Say(Text["BkDisconnected"]); }, "BkConnectFailed");
    }

    public async Task UploadAsync()
    {
        await Run(async () =>
        {
            using var data = new MemoryStream();
            archive.Write(data);
            data.Position = 0;
            await cloud.UploadAsync(data, SuggestedName + Extension, CancellationToken.None);
            Say(Text["BkUploaded"]);
            await LoadCloudAsync();
        }, "BkUploadFailed");
    }

    public async Task RefreshCloudAsync()
    {
        if (!CloudConnected) return;
        await Run(LoadCloudAsync, "BkListFailed");
    }

    // Downloads one Drive backup into memory for the restore preview.
    public async Task<MemoryStream?> DownloadAsync(string id)
    {
        MemoryStream? result = null;
        await Run(async () =>
        {
            var data = new MemoryStream();
            await cloud.DownloadAsync(id, data, CancellationToken.None);
            data.Position = 0;
            result = data;
        }, "BkDownloadFailed");
        return result;
    }

    private async Task LoadCloudAsync()
    {
        var items = await cloud.ListAsync(CancellationToken.None);
        CloudItems.Clear();
        foreach (var i in items)
            CloudItems.Add(new(i.Id, i.Name, i.Created.ToLocalTime().ToString("g", Culture), i.Size < 1024 * 1024 ? $"{Math.Max(1, i.Size / 1024)} KB" : (i.Size / 1048576.0).ToString("0.0", Culture) + " MB"));
        OnPropertyChanged(nameof(HasCloudItems));
    }

    private async Task Run(Func<Task> work, string failKey)
    {
        if (Busy) return;
        Busy = true;
        try { await work(); }
        catch (OperationCanceledException) { Say(Text["BkCancelled"], error: true); }
        catch (Exception ex) { log(ex); Say(Text[failKey], error: true); }
        finally { Busy = false; Notify(); }
    }

    private void Say(string text, bool error = false) { Message = text; IsError = error; }
    public void Notify()
    {
        foreach (var n in new[] { nameof(CloudConfigured), nameof(CloudConnected), nameof(CloudCanConnect), nameof(CloudAccountText), nameof(HasCloudItems) }) OnPropertyChanged(n);
    }
}
