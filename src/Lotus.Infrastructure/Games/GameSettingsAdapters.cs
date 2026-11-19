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
        return new(gpu ?? "", vram, Environment.ProcessorCount, ramMb, d?.Width ?? 1920, d?.Height ?? 1080, d?.CurrentHz ?? 60, ExactPrimaryRefresh(), CpuName());
    }
    // The processor's marketing name (read-only), used to find it in the Lotus Score parts table.
    private static string CpuName()
    {
        try { using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"); return (k?.GetValue("ProcessorNameString") as string)?.Trim() ?? ""; }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return ""; }
    }

    // The primary display's real refresh rate (numerator/denominator from QueryDisplayConfig, e.g. 74973/1000), where
    // EnumDisplaySettings only gives whole hertz. Read-only; 0 when Windows does not answer. Raw buffers with fixed
    // offsets from wingdi.h: DISPLAYCONFIG_PATH_INFO is 72 bytes (source mode index at 12, target refresh rate at 48/52),
    // DISPLAYCONFIG_MODE_INFO is 64 bytes (source mode position at 28/32; the primary display sits at 0,0).
    private const uint QdcOnlyActivePaths = 2;
    private const int PathSize = 72, ModeSize = 64;
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, byte[] pathInfo, ref uint modes, byte[] modeInfo, IntPtr topology);
    public static double ExactPrimaryRefresh()
    {
        try
        {
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount) != 0 || pathCount == 0) return 0;
            var paths = new byte[pathCount * PathSize]; var modes = new byte[modeCount * ModeSize];
            if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return 0;
            double first = 0;
            for (var i = 0; i < pathCount; i++)
            {
                var o = i * PathSize;
                uint num = BitConverter.ToUInt32(paths, o + 48), den = BitConverter.ToUInt32(paths, o + 52);
                if (num == 0 || den == 0) continue;
                var hz = num / (double)den;
                if (first == 0) first = hz;
                var mode = BitConverter.ToUInt32(paths, o + 12);
                if (mode < modeCount && BitConverter.ToInt32(modes, (int)mode * ModeSize) == 1 // DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE
                    && BitConverter.ToInt32(modes, (int)mode * ModeSize + 28) == 0 && BitConverter.ToInt32(modes, (int)mode * ModeSize + 32) == 0) return hz;
            }
            return first;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ArgumentException) { return 0; }
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
    // Steps every scalability group except textures (VRAM-bound) by one level within 0..3.
    public string? StepQuality(string raw, int step)
    {
        var ini = new IniText(raw); var changed = false;
        foreach (var g in Groups)
        {
            var current = int.TryParse(ini.Get("ScalabilityGroups", g), out var v) ? v : 3;
            var next = Math.Clamp(current + Math.Sign(step), 0, 3);
            if (next != current) { ini.Set("ScalabilityGroups", g, next.ToString()); changed = true; }
        }
        return changed ? ini.ToString() : null;
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
        // Written to a temp file first: a crash mid-write must never leave a cut copy that "Restaurar" would put back.
        Directory.CreateDirectory(root);
        var temp = path + ".tmp"; File.WriteAllText(temp, raw, new UTF8Encoding(false)); File.Move(temp, path, overwrite: true);
    }
    public void ClearOriginal(string gameKey) { var p = PathFor(gameKey); if (File.Exists(p)) File.Delete(p); }
}

// A game counts as running when any process image lives under its install folder.
[SupportedOSPlatform("windows")]
public sealed class WindowsGameProcessCheck : IGameProcessCheck
{
    // Main game process = largest working set among processes under the install folder.
    public RunningGame? Find(GameEntry game)
    {
        var dir = game.InstallDir is { Length: > 0 } d ? Path.GetFullPath(d).TrimEnd('\\') + "\\" : null;
        if (dir is null) return null;
        RunningGame? best = null; long bestMemory = -1;
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.MainModule?.FileName is { } f && f.StartsWith(dir, StringComparison.OrdinalIgnoreCase) && p.WorkingSet64 > bestMemory)
                    { bestMemory = p.WorkingSet64; best = new(p.Id, Path.GetFileName(f), new DateTimeOffset(p.StartTime.ToUniversalTime(), TimeSpan.Zero)); }
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            }
        }
        return best;
    }
    // One pass over the processes, reading each image path with the light query (no module enumeration): cheap enough
    // for the background watcher that puts Lotus in light mode while a game runs.
    public GameEntry? FirstRunning(IReadOnlyList<GameEntry> games)
    {
        var dirs = games.Where(g => g.InstallDir is { Length: > 3 }).Select(g => (Game: g, Dir: Path.GetFullPath(g.InstallDir!).TrimEnd('\\') + "\\"))
            .Where(d => !GameProcessRules.TooBroad(d.Dir)).ToList();
        if (dirs.Count == 0) return null;
        var buffer = new StringBuilder(1024);
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (p.Id <= 4) continue;
                var handle = OpenProcess(QueryLimited, false, p.Id);
                if (handle == IntPtr.Zero) continue;
                try
                {
                    buffer.Clear(); var size = buffer.Capacity;
                    if (!QueryFullProcessImageName(handle, 0, buffer, ref size)) continue;
                    var path = buffer.ToString();
                    if (GameProcessRules.AlwaysOn(path)) continue;
                    foreach (var (game, dir) in dirs) if (path.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) return game;
                }
                finally { CloseHandle(handle); }
            }
        }
        return null;
    }
    private const uint QueryLimited = 0x1000; // PROCESS_QUERY_LIMITED_INFORMATION
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    public bool IsRunning(GameEntry game)
    {
        // Minecraft Java runs inside the launcher's own javaw.exe, outside the game folder: any open Java window counts (safe side).
        if (MinecraftJavaRecipe.IsMinecraft(game) && Process.GetProcessesByName("javaw") is { Length: > 0 } java) { foreach (var j in java) j.Dispose(); return true; }
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

// No Rest for the Wicked (Unity, Moon Studios): JSON in HKCU\Software\Moon Studios\NoRestForTheWicked, value "DeviceSettings_Key_h…".
// Calibrated against the in-game menu: QualityPreset 4 = "Qualidade Máxima", UpscaleMethod 1 = DLSS, UpscaleQualityMode 0 = Qualidade.
// Initial apply never lowers quality: it sets the upscaler (RTX only, verified values) and the FPS cap; QualityPreset changes only
// through the measurement loop (QualityStep), one level at a time.
[SupportedOSPlatform("windows")]
public sealed class NoRestForTheWickedRecipe(string registryPath = @"Software\Moon Studios\NoRestForTheWicked") : IGameSettingsRecipe
{
    public string Id => "moonstudios.norestforthewicked.v1";
    public const int MaxPreset = 4;
    private string? ValueName()
    {
        using var key = Registry.CurrentUser.OpenSubKey(registryPath);
        return key?.GetValueNames().FirstOrDefault(n => n.StartsWith("DeviceSettings_Key", StringComparison.Ordinal));
    }
    public GameSettingsSupport? Probe(GameEntry game) =>
        game.Name.Contains("No Rest for the Wicked", StringComparison.OrdinalIgnoreCase) || game.Id == "1371980"
            ? ValueName() is null ? new(Id, "Moon Studios (Unity)", false, "GsRunOnce") : new(Id, "Moon Studios (Unity)", true)
            : null;
    public string? ReadRaw(GameEntry game)
    {
        if (ValueName() is not { } name) return null;
        using var key = Registry.CurrentUser.OpenSubKey(registryPath);
        return key?.GetValue(name) is byte[] b ? Encoding.UTF8.GetString(b).TrimEnd('\0') : null;
    }
    public void WriteRaw(GameEntry game, string raw)
    {
        var name = ValueName() ?? throw new InvalidOperationException("Game settings not found.");
        using var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: true) ?? throw new InvalidOperationException("Game settings key not found.");
        key.SetValue(name, Encoding.UTF8.GetBytes(raw + "\0"), RegistryValueKind.Binary); // Unity PlayerPrefs strings are NUL-terminated UTF-8
    }
    public string Transform(string raw, SettingsRecommendation r) => TransformJson(raw, r);
    public string? StepQuality(string raw, int step)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(raw)!.AsObject();
        var current = json["QualityPreset"]?.GetValue<int>() ?? MaxPreset;
        var next = current + Math.Sign(step);
        if (next < 0 || next > MaxPreset) return null;
        json["QualityPreset"] = next;
        return json.ToJsonString();
    }
    public static string TransformJson(string raw, SettingsRecommendation r, int? qualityPreset = null)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(raw)!.AsObject();
        json["FrameRateCap"] = r.FpsCap;
        if (r.Upscaler == Upscaler.Dlss) { json["UpscaleMethod"] = 1; if (r.Mode == UpscaleMode.Quality) json["UpscaleQualityMode"] = 0; }
        if (qualityPreset is { } q) json["QualityPreset"] = Math.Clamp(q, 0, MaxPreset);
        return json.ToJsonString();
    }
}

// Minecraft Java: options.txt ("key:value" per line) of the launcher profile played most recently (Modrinth, Prism, CurseForge
// or the official .minecraft). Only maxFps, renderDistance and simulationDistance change. The first apply never raises the
// view distance (it only trims it to the tier's ceiling, and only with a game-aware recommendation); the measurement loop
// steps it 2 chunks at a time.
// Shaders, Sodium/Iris files and every other line stay untouched, because their format changes with each mod version.
public sealed class MinecraftJavaRecipe(string? appData = null, string? userProfile = null) : IGameSettingsRecipe
{
    public string Id => "minecraft.java.options.v1";
    public const int MinDistance = 4, MaxDistance = 32, Unlimited = 260;
    private string AppData => appData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private string UserProfile => userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static bool IsMinecraft(GameEntry game) =>
        game.Name.Contains("Minecraft", StringComparison.OrdinalIgnoreCase) && !game.Name.Contains("Dungeons", StringComparison.OrdinalIgnoreCase)
        && !game.Name.Contains("Legends", StringComparison.OrdinalIgnoreCase);

    private IEnumerable<string> Candidates(GameEntry game)
    {
        if (game.InstallDir is { } dir) { yield return Path.Combine(dir, "options.txt"); yield return Path.Combine(dir, ".minecraft", "options.txt"); }
        yield return Path.Combine(AppData, ".minecraft", "options.txt");
        foreach (var (root, inner) in new[] { (Path.Combine(AppData, "ModrinthApp", "profiles"), ""), (Path.Combine(AppData, "PrismLauncher", "instances"), ".minecraft"),
                     (Path.Combine(UserProfile, "curseforge", "minecraft", "Instances"), "") })
        {
            if (!Directory.Exists(root)) continue;
            string[] profiles; try { profiles = Directory.GetDirectories(root); } catch (IOException) { continue; } catch (UnauthorizedAccessException) { continue; }
            foreach (var p in profiles) yield return Path.Combine(p, inner, "options.txt");
        }
    }
    // The profile folder the game was last played from: options.txt is rewritten whenever the game closes.
    private string? Find(GameEntry game) =>
        Candidates(game).Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();

    public GameSettingsSupport? Probe(GameEntry game)
    {
        if (!IsMinecraft(game)) return null;
        return Find(game) is { } path ? new(Id, "Minecraft Java", true, path) : new(Id, "Minecraft Java", false, "GsRunOnce");
    }
    public string? ReadRaw(GameEntry game) => Find(game) is { } p ? File.ReadAllText(p) : null;
    public void WriteRaw(GameEntry game, string raw)
    {
        var path = Find(game) ?? throw new FileNotFoundException("options.txt not found.");
        var temp = path + ".lotus.tmp"; File.WriteAllText(temp, raw, new UTF8Encoding(false)); File.Move(temp, path, overwrite: true);
    }
    public static int Ceiling(QualityTier tier) => tier switch { QualityTier.Low => 8, QualityTier.Medium => 12, QualityTier.High => 16, _ => MaxDistance };

    public string Transform(string raw, SettingsRecommendation r)
    {
        var o = new ColonOptions(raw);
        o.Set("maxFps", Math.Clamp(r.FpsCap, 10, Unlimited).ToString());
        // Without a measured curve the tier is a generic guess (Minecraft is light but CPU-bound), so the view distance only
        // drops when the recommendation is game-aware; otherwise the measurement loop decides.
        if (r.Game is not null)
        {
            var render = Math.Clamp(o.Int("renderDistance") ?? 12, MinDistance, MaxDistance);
            Distances(o, Math.Min(render, Ceiling(r.Tier)));
        }
        return o.ToString();
    }
    public string? StepQuality(string raw, int step)
    {
        var o = new ColonOptions(raw);
        var current = Math.Clamp(o.Int("renderDistance") ?? 12, MinDistance, MaxDistance);
        var next = Math.Clamp(current + 2 * Math.Sign(step), MinDistance, MaxDistance);
        if (next == current) return null;
        Distances(o, next);
        return o.ToString();
    }
    // Simulation distance (CPU work) never exceeds the view distance.
    private static void Distances(ColonOptions o, int render)
    {
        o.Set("renderDistance", render.ToString());
        if (o.Int("simulationDistance") is { } sim && sim > render) o.Set("simulationDistance", Math.Max(5, render).ToString());
    }
}

// "key:value" lines (Minecraft options.txt): changes a key in place or appends it; keeps the file's own line ending.
public sealed class ColonOptions(string text)
{
    private readonly string newline = text.Contains("\r\n") ? "\r\n" : "\n";
    private readonly List<string> lines = text.Replace("\r\n", "\n").Split('\n').ToList();
    private int IndexOf(string key) => lines.FindIndex(l => l.StartsWith(key + ":", StringComparison.Ordinal));
    public string? Get(string key) => IndexOf(key) is var i and >= 0 ? lines[i][(key.Length + 1)..].Trim() : null;
    public int? Int(string key) => int.TryParse(Get(key), out var v) ? v : null;
    public void Set(string key, string value)
    {
        var i = IndexOf(key);
        if (i >= 0) { lines[i] = $"{key}:{value}"; return; }
        var insert = lines.Count; while (insert > 0 && lines[insert - 1].Length == 0) insert--;
        lines.Insert(insert, $"{key}:{value}");
    }
    public override string ToString() => string.Join(newline, lines);
}
