using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

namespace Lotus.Infrastructure.Integrations;

// "Salvar para todos os jogos": writes the chosen GPU tune into MSI Afterburner's own profiles, so Afterburner applies it
// whenever a game (3D) starts and goes back to stock on the desktop (2D), with or without Lotus open.
//   Profile 1 = the card's defaults ([Defaults] of the card file), Profile 2 = the tune, Profile2D = 1, Profile3D = 2.
// The files live under Program Files, so the writing runs in the short-lived elevated helper after the player's UAC "yes";
// everything is backed up first and put back when Afterburner does not load the tune as written.
// Card file VFCurve: hex of { uint version 0x00020000, uint points, uint flags, points × { float mV, float MHz, float offset
// MHz }, zero padding } (the same curve the control memory exposes in µV / kHz).
public static class AfterburnerProfileFile
{
    public const int Stock = 1, Tune = 2;
    private const uint CurveVersion = 0x00020000;
    private const int CurveHeader = 12, PointSize = 12;

    public sealed record Values(int PowerLimit, int CoreBoostKHz, int MemBoostKHz, int[] OffsetsKHz);

    public static byte[]? Decode(string hex)
    {
        if (hex.Length < CurveHeader * 2 || hex.Length % 2 != 0) return null;
        try { return Convert.FromHexString(hex); } catch (FormatException) { return null; }
    }

    // Point count of a stored curve; null when it is not a curve Lotus knows.
    public static int? Points(byte[] curve) =>
        curve.Length >= CurveHeader && BinaryPrimitives.ReadUInt32LittleEndian(curve) == CurveVersion
        && BinaryPrimitives.ReadInt32LittleEndian(curve.AsSpan(4)) is var n && n is > 0 and <= 256 && curve.Length >= CurveHeader + n * PointSize ? n : null;

    // The card's own curve with the offsets replaced (kHz → MHz); voltages and factory frequencies stay as Afterburner wrote them.
    public static string? WithOffsets(string templateHex, IReadOnlyList<int> offsetsKHz)
    {
        if (Decode(templateHex) is not { } curve || Points(curve) is not { } n || n != offsetsKHz.Count) return null;
        for (var i = 0; i < n; i++)
            BinaryPrimitives.WriteSingleLittleEndian(curve.AsSpan(CurveHeader + i * PointSize + 8), offsetsKHz[i] / 1000f);
        return Convert.ToHexString(curve);
    }

    public static int[]? Offsets(string hex)
    {
        if (Decode(hex) is not { } curve || Points(curve) is not { } n) return null;
        var offsets = new int[n];
        for (var i = 0; i < n; i++) offsets[i] = (int)MathF.Round(BinaryPrimitives.ReadSingleLittleEndian(curve.AsSpan(CurveHeader + i * PointSize + 8)) * 1000);
        return offsets;
    }

    // ---- INI text, edited line by line so everything else in the file stays byte for byte ----
    public static string? Get(string text, string section, string key)
    {
        string? current = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { current = line[1..^1]; continue; }
            var eq = line.IndexOf('=');
            if (eq > 0 && string.Equals(current, section, StringComparison.OrdinalIgnoreCase) && line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return line[(eq + 1)..].Trim();
        }
        return null;
    }

    // Replaces the whole [section] (or appends it) with these keys in this order.
    public static string SetSection(string text, string section, IReadOnlyList<(string Key, string Value)> keys)
    {
        var nl = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        var block = new List<string> { "[" + section + "]" };
        block.AddRange(keys.Select(k => k.Key + "=" + k.Value));
        var start = lines.FindIndex(l => l.Trim().Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase));
        if (start < 0) lines.AddRange(block);
        else
        {
            var end = start + 1;
            while (end < lines.Count && !lines[end].TrimStart().StartsWith('[')) end++;
            lines.RemoveRange(start, end - start);
            lines.InsertRange(start, block);
        }
        return string.Join(nl, lines) + nl;
    }

    // Sets one existing "Key=value" line of a section (Afterburner's main cfg keeps its own spacing); appended when missing.
    public static string SetKey(string text, string section, string key, string value)
    {
        var nl = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        string? current = null; var sectionEnd = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { current = line[1..^1]; continue; }
            if (!string.Equals(current, section, StringComparison.OrdinalIgnoreCase)) continue;
            if (line.Length > 0) sectionEnd = i;
            var eq = line.IndexOf('=');
            if (eq > 0 && line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) { lines[i] = key + "=" + value; return string.Join(nl, lines); }
        }
        if (sectionEnd < 0) { lines.Insert(lines.Count > 0 && lines[^1].Length == 0 ? lines.Count - 1 : lines.Count, "[" + section + "]"); sectionEnd = lines.FindLastIndex(l => l.Trim() == "[" + section + "]"); }
        lines.Insert(sectionEnd + 1, key + "=" + value);
        return string.Join(nl, lines);
    }

    // Profile 1 = the card's [Defaults]; Profile 2 = the tune (curve from the card's default curve, offsets replaced).
    public static string? WithProfiles(string cardText, Values tune)
    {
        if (Get(cardText, "Defaults", "VFCurve") is not { Length: > 0 } defaultCurve) return null;
        if (WithOffsets(defaultCurve, tune.OffsetsKHz) is not { } tuneCurve) return null;
        string D(string key, string fallback) => Get(cardText, "Defaults", key) is { Length: > 0 } v ? v : fallback;
        var stock = new List<(string, string)> { ("Format", D("Format", "2")), ("PowerLimit", D("PowerLimit", "100")), ("CoreClkBoost", D("CoreClkBoost", "0")),
            ("VFCurve", defaultCurve), ("MemClkBoost", D("MemClkBoost", "0")), ("FanMode", D("FanMode", "1")), ("FanSpeed", D("FanSpeed", "30")) };
        var inv = CultureInfo.InvariantCulture;
        var tuned = new List<(string, string)> { ("Format", D("Format", "2")), ("PowerLimit", tune.PowerLimit.ToString(inv)), ("CoreClkBoost", tune.CoreBoostKHz.ToString(inv)),
            ("VFCurve", tuneCurve), ("MemClkBoost", tune.MemBoostKHz.ToString(inv)), ("FanMode", D("FanMode", "1")), ("FanSpeed", D("FanSpeed", "30")) };
        return SetSection(SetSection(cardText, "Profile" + Stock, stock), "Profile" + Tune, tuned);
    }

    // Profile2D / Profile3D hold the profile's index from 0 (-1 = none): written as 1 and 2, Afterburner switched nothing
    // in a real game (14/11/2026), so Profile 1 is 0 and Profile 2 is 1.
    public static string WithAutoSwitch(string mainText) =>
        SetKey(SetKey(mainText, "Settings", "Profile2D", (Stock - 1).ToString(CultureInfo.InvariantCulture)), "Settings", "Profile3D", (Tune - 1).ToString(CultureInfo.InvariantCulture));

    // The card file whose default curve has as many points as the card exposes now (one card: one file).
    public static string? CardFile(string profilesDir, int points)
    {
        if (!Directory.Exists(profilesDir)) return null;
        var matches = Directory.GetFiles(profilesDir, "VEN_*.cfg").Where(f =>
        {
            try { return Get(File.ReadAllText(f), "Defaults", "VFCurve") is { } hex && Decode(hex) is { } c && Points(c) == points; }
            catch (IOException) { return false; }
        }).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    // True when the live curve (control memory) matches the expected one within 1 MHz on every point.
    public static bool Matches(IReadOnlyList<int> liveKHz, IReadOnlyList<int> expectedKHz) =>
        liveKHz.Count == expectedKHz.Count && liveKHz.Zip(expectedKHz).All(p => Math.Abs(p.First - p.Second) <= 1000);
}

// Lotus's side (no administrator): asks for the one UAC, hands the tune read from the control memory to the helper.
[SupportedOSPlatform("windows")]
public sealed class AfterburnerProfiles(AfterburnerControl control, Func<string[], Task<int>>? elevatedRunner = null)
{
    private static string Data => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus");
    public bool HasBackup => Directory.Exists(Path.Combine(Data, "AfterburnerBackups", "original"));
    public bool Saved => AfterburnerSetup.AutoProfiles();

    // What the card runs now (the tune the player kept) goes to Profile 2. 0 = saved and checked; 1223 = UAC declined.
    public async Task<int> SaveCurrentAsync()
    {
        if (control.Read() is not { } s || s.Offsets.Length == 0) return 5;
        var dir = Path.Combine(Data, "Requests");
        Directory.CreateDirectory(dir);
        var request = Path.Combine(dir, "ab-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(request, JsonSerializer.Serialize(new AfterburnerProfileFile.Values(s.PowerLimit, s.CoreBoostKHz, s.MemBoostKHz, s.Offsets)));
        try
        {
            var code = await RunElevated(["--ab-save", request]);
            if (code == 0) control.ForgetOriginal(); // the tune is Afterburner's own profile now: nothing to undo at the next start
            return code;
        }
        finally { File.Delete(request); }
    }

    public Task<int> RestoreAsync() => RunElevated(["--ab-restore"]);

    private Task<int> RunElevated(string[] args) => elevatedRunner?.Invoke(args) ?? Task.Run(() =>
    {
        var info = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
        foreach (var a in args) info.ArgumentList.Add(a);
        info.ArgumentList.Add("--lotus-data"); info.ArgumentList.Add(Data);
        try { using var p = System.Diagnostics.Process.Start(info)!; p.WaitForExit(); return p.ExitCode; }
        catch (System.ComponentModel.Win32Exception) { return 1223; }
    });
}

// The elevated helper: "Lotus.App.exe --ab-save <request.json>" or "--ab-restore".
[SupportedOSPlatform("windows")]
public static class AfterburnerElevatedHelper
{
    public static string ProfilesDir => Path.Combine(AfterburnerSetup.Dir, "Profiles");

    public static int? TryRun(string[] args)
    {
        var i = Array.FindIndex(args, a => a is "--ab-save" or "--ab-restore");
        if (i < 0 || (args[i] == "--ab-save" && i + 1 >= args.Length)) return null;
        var data = RtssElevatedHelper.DataRoot(args);
        var backups = Path.Combine(data, "AfterburnerBackups");
        try
        {
            if (args[i] == "--ab-restore") return Restore(backups, Path.Combine(backups, "original")) ? 0 : 4;
            var requestPath = Path.GetFullPath(args[i + 1]);
            var allowedRoot = Path.Combine(data, "Requests") + Path.DirectorySeparatorChar;
            if (!requestPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase)) return 3;
            var tune = JsonSerializer.Deserialize<AfterburnerProfileFile.Values>(File.ReadAllText(requestPath)) ?? throw new InvalidDataException();
            return Save(tune, backups);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or InvalidDataException) { return 2; }
    }

    private static int Save(AfterburnerProfileFile.Values tune, string backups)
    {
        if (AfterburnerProfileFile.CardFile(ProfilesDir, tune.OffsetsKHz.Length) is not { } card) return 5;
        var main = Path.Combine(ProfilesDir, "MSIAfterburner.cfg");
        if (!File.Exists(main)) return 5;
        var newCard = AfterburnerProfileFile.WithProfiles(File.ReadAllText(card), tune);
        if (newCard is null) return 5;
        // The player's files before Lotus ever touched them stay in "original"; each save also keeps its own copy.
        var original = Path.Combine(backups, "original");
        if (!Directory.Exists(original)) Copy(ProfilesDir, original);
        var before = Path.Combine(backups, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        Copy(ProfilesDir, before);

        Close(); // opened again below even if it was closed: the 2D/3D switch needs Afterburner running
        File.WriteAllText(card, newCard, new UTF8Encoding(false));
        File.WriteAllText(main, AfterburnerProfileFile.WithAutoSwitch(File.ReadAllText(main)), new UTF8Encoding(false));
        Start();
        // Afterburner must load the tune exactly as written (profile 2) and stock as profile 1; otherwise everything goes back.
        var ok = Loads(AfterburnerProfileFile.Tune, tune.OffsetsKHz) && Loads(AfterburnerProfileFile.Stock, new int[tune.OffsetsKHz.Length]);
        if (!ok) { Restore(backups, before); return 6; }
        return 0;
    }

    private static bool Loads(int profile, int[] expected)
    {
        Run("-Profile" + profile.ToString(CultureInfo.InvariantCulture));
        var control = new AfterburnerControl(Path.Combine(Path.GetTempPath(), "lotus-ab-check.json"));
        for (var i = 0; i < 40; i++)
        {
            Thread.Sleep(250);
            if (control.Read() is { } s && AfterburnerProfileFile.Matches(s.Offsets, expected)) return true;
        }
        return false;
    }

    private static bool Restore(string backups, string from)
    {
        if (!Directory.Exists(from)) return false;
        Close();
        foreach (var f in Directory.GetFiles(from, "*.cfg")) File.Copy(f, Path.Combine(ProfilesDir, Path.GetFileName(f)), true);
        Start();
        return true;
    }

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from, "*.cfg")) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
    }

    // Afterburner reads its profiles only when it starts, so it is closed before the files change and opened again.
    private static bool Close()
    {
        var was = false;
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("MSIAfterburner"))
            using (p)
            {
                was = true;
                try { if (!p.CloseMainWindow() || !p.WaitForExit(4000)) { p.Kill(); p.WaitForExit(5000); } }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        if (was) Thread.Sleep(800);
        return was;
    }

    private static void Start()
    {
        using (System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AfterburnerSetup.Exe) { UseShellExecute = true, WorkingDirectory = AfterburnerSetup.Dir })) { }
        var control = new AfterburnerControl(Path.Combine(Path.GetTempPath(), "lotus-ab-check.json"));
        for (var i = 0; i < 60 && !control.Available; i++) Thread.Sleep(500);
        Thread.Sleep(1500); // let it finish applying its startup state
    }

    // "MSIAfterburner.exe -ProfileN" hands the command to the running Afterburner.
    private static void Run(string arg)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AfterburnerSetup.Exe, arg) { UseShellExecute = true, WorkingDirectory = AfterburnerSetup.Dir });
        p?.WaitForExit(5000);
    }
}
