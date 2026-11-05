using System.Diagnostics;
using System.Runtime.Versioning;
using Lotus.Core.Maintenance;
using Lotus.Infrastructure.Tools;

namespace Lotus.Infrastructure.Maintenance;

// Files leave only to the Recycle Bin (the player can bring them back from there).
[SupportedOSPlatform("windows")]
public sealed class ShellFileRecycler : IFileRecycler { public bool Recycle(string path) => File.Exists(path) && RecycleBin.Send(path); }

// Explorer opens with the item selected; only existing local paths are passed, quoted.
[SupportedOSPlatform("windows")]
public sealed class ExplorerRevealer : IFileRevealer
{
    public void Reveal(string path)
    {
        var full = Path.GetFullPath(path);
        if (File.Exists(full)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{full}\"") { UseShellExecute = false });
        else if (Directory.Exists(full)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{full}\"") { UseShellExecute = false });
    }
}

public static class LocalDrives
{
    public static IReadOnlyList<DriveSpace> Read() => [.. DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
        .Select(d => new DriveSpace(d.Name, d.VolumeLabel?.Trim() ?? "", d.AvailableFreeSpace, d.TotalSize))];
}
