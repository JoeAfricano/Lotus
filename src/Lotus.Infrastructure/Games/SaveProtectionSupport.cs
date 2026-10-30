using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lotus.Core.Saves;

namespace Lotus.Infrastructure.Games;

// Windows known folders for the save rules. Saved Games and LocalLow come from the shell (they can be moved by the
// user), never from a guessed path.
[SupportedOSPlatform("windows")]
public sealed class WindowsKnownFolders : IKnownFolders
{
    private static readonly Guid SavedGames = new("4C5C32FF-BB9D-43b0-B5B4-2D72E54EAAA4"), LocalLow = new("A520A1A4-1780-4FF6-BD18-167343C5AF16");

    public string? Get(string token) => token switch
    {
        "APPDATA" => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LOCALAPPDATA" => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "USERPROFILE" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "DOCUMENTS" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "SAVEDGAMES" => Known(SavedGames),
        "LOCALLOW" => Known(LocalLow),
        _ => null,
    };
    public string? SteamPath() => WindowsGameLibrary.DefaultSteamPath();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
    private static string? Known(Guid id)
    {
        try
        {
            if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var p) != 0) return null;
            try { return Marshal.PtrToStringUni(p); } finally { Marshal.FreeCoTaskMem(p); }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }
}

// %LOCALAPPDATA%\Lotus\save-protection.json — which games are protected, where their saves are and how many versions
// to keep. Only these settings travel in a .lotusbackup; the save files themselves stay in SaveBackups.
public sealed class JsonSaveSettingsStore(string? path = null) : ISaveSettingsStore
{
    private readonly string file = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "save-protection.json");
    public SaveProtectionData Load()
    {
        foreach (var candidate in new[] { file, file + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try { if (JsonSerializer.Deserialize(File.ReadAllText(candidate), SaveJson.Default.SaveProtectionData) is { } d) return d; }
            catch (JsonException) { }
        }
        return new SaveProtectionData();
    }
    public void Save(SaveProtectionData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, SaveJson.Default.SaveProtectionData));
        if (File.Exists(file)) File.Replace(tmp, file, file + ".bak"); else File.Move(tmp, file);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SaveProtectionData))]
internal sealed partial class SaveJson : JsonSerializerContext { }
