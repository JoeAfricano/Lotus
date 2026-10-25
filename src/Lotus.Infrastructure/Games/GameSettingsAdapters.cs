using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using Lotus.Core.Games;
using Lotus.Core.Optimization;
using Microsoft.Win32;

namespace Lotus.Infrastructure.Games;

// GPU name and dedicated VRAM from the display adapter class key (read-only), plus CPU, RAM and primary display.
[SupportedOSPlatform("windows")]
public sealed class WindowsSystemProfile(IDisplayModes display) : ISystemProfileSource
{
    private const string DisplayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    public SystemProfile Read()
    {
        var (gpu, vram) = Adapters().OrderByDescending(a => a.Vram).FirstOrDefault();
        var d = display.Primary();
        var ramMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
        return new(gpu ?? "", vram, Environment.ProcessorCount, ramMb, d?.Width ?? 1920, d?.Height ?? 1080, d?.CurrentHz ?? 60);
    }
    private static IEnumerable<(string Name, long Vram)> Adapters()
    {
        using var root = Registry.LocalMachine.OpenSubKey(DisplayClass);
        if (root is null) yield break;
        foreach (var sub in root.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
        {
            using var k = root.OpenSubKey(sub);
            if (k?.GetValue("DriverDesc") is not string name) continue;
            var bytes = k.GetValue("HardwareInformation.qwMemorySize") switch { long q => q, byte[] b when b.Length >= 8 => BitConverter.ToInt64(b), _ => 0 };
            if (bytes == 0) bytes = k.GetValue("HardwareInformation.MemorySize") switch { int i => (uint)i, byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b), _ => 0 };
            yield return (name, bytes / (1024 * 1024));
        }
    }
}

// Unreal Engine (UE4/UE5): GameUserSettings.ini under %LOCALAPPDATA%\<Project>\Saved\Config\<Platform>\.
// Only ScalabilityGroups (0 = Low … 3 = Epic) and FrameRateLimit change; every other line is kept as is.
public sealed class UnrealEngineRecipe(string? localAppData = null) : IGameSettingsRecipe
{
    public string Id => "unreal.gameusersettings.v1";
    private string LocalAppData => localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string[] Groups = ["sg.ViewDistanceQuality", "sg.AntiAliasingQuality", "sg.ShadowQuality", "sg.GlobalIlluminationQuality",
        "sg.ReflectionQuality", "sg.PostProcessQuality", "sg.EffectsQuality", "sg.FoliageQuality", "sg.ShadingQuality"];

    public static string? ProjectName(string installDir)
    {
        if (!Directory.Exists(installDir)) return null;
        var shipping = Directory.EnumerateFiles(installDir, "*-Win*-Shipping.exe", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true }).FirstOrDefault();
        if (shipping is null || !Directory.Exists(Path.Combine(installDir, "Engine"))) return null;
        // <install>\<Project>\Binaries\Win64\<Name>-Win64-Shipping.exe
        return new DirectoryInfo(Path.GetDirectoryName(shipping)!).Parent?.Parent?.Name;
    }
    private string? Find(GameEntry game)
    {
        if (game.InstallDir is not { } dir || ProjectName(dir) is not { } project) return null;
        return new[] { "Windows", "WindowsNoEditor", "WinGDK" }.Select(p => Path.Combine(LocalAppData, project, "Saved", "Config", p, "GameUserSettings.ini")).FirstOrDefault(File.Exists);
    }
    public GameSettingsSupport? Probe(GameEntry game)
    {
        if (game.InstallDir is not { } dir || ProjectName(dir) is null) return null;
        return Find(game) is { } path ? new(Id, "Unreal Engine", true, path) : new(Id, "Unreal Engine", false, "GsRunOnce");
    }
    public string? ReadRaw(GameEntry game) => Find(game) is { } p ? File.ReadAllText(p) : null;
    public void WriteRaw(GameEntry game, string raw)
    {
        var path = Find(game) ?? throw new FileNotFoundException("GameUserSettings.ini not found.");
        var temp = path + ".lotus.tmp"; File.WriteAllText(temp, raw, new UTF8Encoding(false)); File.Move(temp, path, overwrite: true);
    }
    public string Transform(string raw, SettingsRecommendation r)
    {
        var ini = new IniText(raw);
        foreach (var g in Groups) ini.Set("ScalabilityGroups", g, ((int)r.Tier).ToString());
        ini.Set("ScalabilityGroups", "sg.TextureQuality", ((int)r.TextureTier).ToString());
        ini.Set("/Script/Engine.GameUserSettings", "FrameRateLimit", r.FpsCap.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture));
        return ini.ToString();
    }
}

// Minimal line-preserving INI editor: changes or appends a key inside a section, never reorders or drops other lines.
public sealed class IniText(string text)
{
    private readonly List<string> lines = text.Replace("\r\n", "\n").Split('\n').ToList();
    public string? Get(string section, string key)
    {
        var (start, end) = Section(section);
        if (start < 0) return null;
        for (var i = start + 1; i < end; i++) if (KeyOf(lines[i]) is { } k && k.Equals(key, StringComparison.OrdinalIgnoreCase)) return lines[i][(lines[i].IndexOf('=') + 1)..].Trim();
        return null;
    }
    public void Set(string section, string key, string value)
    {
        var (start, end) = Section(section);
        if (start < 0)
        {
            while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
            lines.Add(""); lines.Add($"[{section}]"); lines.Add($"{key}={value}"); lines.Add("");
            return;
        }
        for (var i = start + 1; i < end; i++)
            if (KeyOf(lines[i]) is { } k && k.Equals(key, StringComparison.OrdinalIgnoreCase)) { lines[i] = $"{k}={value}"; return; }
        var insert = end; while (insert > start + 1 && lines[insert - 1].Trim().Length == 0) insert--;
        lines.Insert(insert, $"{key}={value}");
    }
    private (int Start, int End) Section(string name)
    {
        var start = lines.FindIndex(l => l.Trim().Equals($"[{name}]", StringComparison.OrdinalIgnoreCase));
        if (start < 0) return (-1, -1);
        var end = lines.FindIndex(start + 1, l => l.TrimStart().StartsWith('['));
        return (start, end < 0 ? lines.Count : end);
    }
    private static string? KeyOf(string line) { var i = line.IndexOf('='); return i > 0 && !line.TrimStart().StartsWith(';') ? line[..i].Trim() : null; }
    public override string ToString() => string.Join("\r\n", lines);
}

// First (original) settings text per game, under %LOCALAPPDATA%\Lotus\GameSettingsBackups.
public sealed class FileGameSettingsStore(string root) : IGameSettingsStore
{
    private string PathFor(string key) => Path.Combine(root, string.Concat(key.Select(c => char.IsLetterOrDigit(c) ? c : '_')) + ".original.txt");
    public string? LoadOriginal(string gameKey) => File.Exists(PathFor(gameKey)) ? File.ReadAllText(PathFor(gameKey)) : null;
    public void SaveOriginalOnce(string gameKey, string raw)
    {
        var path = PathFor(gameKey);
        if (File.Exists(path)) return;
        Directory.CreateDirectory(root); File.WriteAllText(path, raw, new UTF8Encoding(false));
    }
    public void ClearOriginal(string gameKey) { var p = PathFor(gameKey); if (File.Exists(p)) File.Delete(p); }
}

// A game counts as running when any process image lives under its install folder.
[SupportedOSPlatform("windows")]
public sealed class WindowsGameProcessCheck : IGameProcessCheck
{
    public bool IsRunning(GameEntry game)
    {
        var dir = game.InstallDir is { Length: > 0 } d ? Path.GetFullPath(d).TrimEnd('\\') + "\\" : null;
        if (dir is null) return false;
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try { if (p.MainModule?.FileName is { } f && f.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) return true; }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            }
        }
        return false;
    }
}
