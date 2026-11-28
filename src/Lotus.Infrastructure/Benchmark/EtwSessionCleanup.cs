using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Lotus.Infrastructure.Benchmark;

// Frame recordings that die without a word (Stellar Blade study, 25/11/2026): ETW sessions left open by a PresentMon that was
// killed use up the provider's session slots; the next PresentMon then loses every event ("66431 ETW events were lost") and
// writes nothing. Before a recording, the stuck sessions named by Lotus (and by its study scripts) are stopped with logman,
// in the elevated helper ("Lotus.App.exe --etw-clean"): listing and stopping ETW sessions needs administrator.
// Only names Lotus creates are ever stopped: RivaTuner's "PresentMonDataProvider" and every other tool's session stay.
public static partial class EtwSessionCleanup
{
    [GeneratedRegex("^Lotus[A-Za-z]*-[0-9a-fA-F]{32}$")] private static partial Regex LotusName();

    // Names from "logman query -ets" (first column of each line).
    public static IReadOnlyList<string> LotusSessions(string logmanOutput) =>
        [.. logmanOutput.Split('\n').Select(l => l.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "")
            .Where(n => LotusName().IsMatch(n)).Distinct(StringComparer.OrdinalIgnoreCase)];

    // A recording that got no frame or lost events leaves this mark: the next one cleans first.
    public static string SuspectFile(string root) => Path.Combine(root, "etw-suspect.txt");
    public static bool NeedsCleanup(string root, IReadOnlyList<string> pendingLedger) => pendingLedger.Count > 0 || File.Exists(SuspectFile(root));
    public static void MarkSuspect(string root, string why)
    {
        try { Directory.CreateDirectory(root); File.WriteAllText(SuspectFile(root), DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture) + " " + why); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    public static void ClearSuspect(string root) { try { File.Delete(SuspectFile(root)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
}

// Entry point of the elevated helper: "Lotus.App.exe --etw-clean". Exit code = number of sessions stopped (0..100), 200 on failure.
[SupportedOSPlatform("windows")]
public static class EtwElevatedHelper
{
    public static int? TryRun(string[] args)
    {
        if (!args.Contains("--etw-clean")) return null;
        try
        {
            var stopped = 0;
            var stuck = EtwSessionCleanup.LotusSessions(Logman("query", "-ets"));
            foreach (var name in stuck) Logman("stop", name, "-ets");
            var left = stuck.Count == 0 ? [] : EtwSessionCleanup.LotusSessions(Logman("query", "-ets"));
            stopped = stuck.Count(n => !left.Contains(n, StringComparer.OrdinalIgnoreCase));
            return Math.Min(stopped, 100);
        }
        catch (Exception) { return 200; }
    }
    private static string Logman(params string[] args)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "logman.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info) ?? throw new IOException("logman did not start");
        var output = p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
        if (!p.WaitForExit(15000)) { try { p.Kill(); } catch (InvalidOperationException) { } }
        return output;
    }
}
