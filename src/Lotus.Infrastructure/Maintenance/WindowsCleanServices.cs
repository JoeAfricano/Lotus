using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Lotus.Core.Maintenance;

namespace Lotus.Infrastructure.Maintenance;

// Windows' own folders for the clean rules (no guessing: each comes from the system).
[SupportedOSPlatform("windows")]
public sealed class WindowsCleanLocations : ICleanLocations
{
    public string? Resolve(string token) => token switch
    {
        "TEMP" => Path.GetTempPath(),
        "LOCALAPPDATA" => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "APPDATA" => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PROGRAMDATA" => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "WINDIR" => Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "USERPROFILE" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "DOCUMENTS" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "DESKTOP" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "PICTURES" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "PROGRAMFILES" => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "PROGRAMFILESX86" => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        "DOWNLOADS" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        _ => null
    } is { Length: > 0 } p ? p : null;
}

public sealed class WindowsProcessProbe : IProcessProbe
{
    public bool AnyRunning(IEnumerable<string> processNames)
    {
        foreach (var name in processNames)
        {
            var found = Process.GetProcessesByName(name);
            var any = found.Length > 0;
            foreach (var p in found) p.Dispose();
            if (any) return true;
        }
        return false;
    }
}

// Size and emptying through the shell (no confirmation window, no sound); Windows decides what it can remove.
[SupportedOSPlatform("windows")]
public sealed class ShellRecycleBin : IRecycleBin
{
    [StructLayout(LayoutKind.Sequential)]
    private struct QueryInfo { public int cbSize; public long i64Size; public long i64NumItems; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHQueryRecycleBinW(string? root, ref QueryInfo info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? root, uint flags);
    private const uint NoConfirmation = 0x1, NoProgressUi = 0x2, NoSound = 0x4;

    public (long Bytes, long Items) Query()
    {
        var info = new QueryInfo { cbSize = Marshal.SizeOf<QueryInfo>() };
        return SHQueryRecycleBinW(null, ref info) == 0 ? (info.i64Size, info.i64NumItems) : (0, 0);
    }
    public bool Empty() => SHEmptyRecycleBinW(IntPtr.Zero, null, NoConfirmation | NoProgressUi | NoSound) is 0 or unchecked((int)0x8000FFFF); // E_UNEXPECTED = already empty
}

// %LOCALAPPDATA%\Lotus\Maintenance\history.json: the last 200 operations (what, when, how many bytes; never file contents).
public sealed class JsonMaintenanceJournal(string file) : IMaintenanceJournal
{
    private readonly object gate = new();
    private List<MaintenanceEntry>? items;
    private List<MaintenanceEntry> Items()
    {
        if (items is not null) return items;
        try { items = File.Exists(file) ? JsonSerializer.Deserialize<List<MaintenanceEntry>>(File.ReadAllText(file)) ?? [] : []; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { items = []; }
        return items;
    }
    public void Add(MaintenanceEntry entry)
    {
        lock (gate)
        {
            var list = Items(); list.Add(entry);
            if (list.Count > 200) list.RemoveRange(0, list.Count - 200);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                var temp = file + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(list));
                File.Move(temp, file, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
    public IReadOnlyList<MaintenanceEntry> Recent(int count = 20) { lock (gate) return [.. Items().AsEnumerable().Reverse().Take(count)]; }
}
