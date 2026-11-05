using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Games;

// Overlays and background capture/effects that often cause stutter (Discord with hardware acceleration, vendor overlays,
// Xbox capture, Overwolf/Medal, animated wallpapers...). Read-only: Lotus only explains how to turn each one off.
// Processes = the process names behind the finding (to show their real memory and processor use).
public sealed record OverlayFinding(string Id, string Name, bool Heavy, IReadOnlyList<string>? Processes = null);
public interface IOverlayScanner { IReadOnlyList<OverlayFinding> Scan(); }

public sealed record OverlayRow(string Id, string Name, string Advice, bool Heavy, string Usage = "")
{
    public string Glyph => Heavy ? "" : "";
}

public sealed class OverlayCheckViewModel(IOverlayScanner scanner, ITextCatalog text, Func<CancellationToken, Task<IReadOnlyList<Maintenance.ProcessSample>>>? usage = null) : ObservableObject
{
    public ITextCatalog Text { get; } = text;
    public IReadOnlyList<OverlayRow> Rows { get; private set; } = [];
    // Launchers share one advice; the tools Lotus works with are listed as compatible, never as a problem.
    public static string AdviceKey(string id) => id.StartsWith("launcher-", StringComparison.Ordinal) ? "OvAdviceLauncher" : id.StartsWith("compat-", StringComparison.Ordinal) ? "OvAdviceCompat" : "OvAdvice" + id;
    private static int Group(string id) => id.StartsWith("compat-", StringComparison.Ordinal) ? 2 : id.StartsWith("launcher-", StringComparison.Ordinal) ? 1 : 0;
    public bool Busy { get; private set; }
    public bool Checked { get; private set; }
    public bool HasRows => Rows.Count > 0;
    public bool AllClear => Checked && Rows.Count == 0;
    // Only overlays and capture/effects apps count as "worth a look"; launchers and compatible tools are just listed.
    private int Attention => Rows.Count(r => Group(r.Id) == 0);
    public string Summary => !Checked ? Text["OvHint"] : Attention == 0 ? Text["OvClear"] : Text.Format(Attention == 1 ? "OvFoundOne" : "OvFound", Attention);

    public async Task ScanAsync()
    {
        if (Busy) return;
        Busy = true; OnPropertyChanged(nameof(Busy));
        try
        {
            var found = await Task.Run(scanner.Scan);
            IReadOnlyList<Maintenance.ProcessSample> samples = [];
            if (usage is not null) try { samples = await usage(CancellationToken.None); } catch (Exception ex) when (ex is not OperationCanceledException) { }
            var culture = System.Globalization.CultureInfo.GetCultureInfo(Text.Language);
            string Usage(OverlayFinding f)
            {
                var mine = samples.Where(s => f.Processes?.Contains(s.ExeName, StringComparer.OrdinalIgnoreCase) == true).ToList();
                return mine.Count == 0 ? "" : Text.Format("OvUsage", (mine.Sum(s => s.Bytes) / (double)(1L << 20)).ToString("0", culture), mine.Sum(s => s.CpuPercent).ToString("0.0", culture));
            }
            Rows = [.. found.OrderBy(f => Group(f.Id)).ThenByDescending(f => f.Heavy).Select(f => new OverlayRow(f.Id, f.Name, Text[AdviceKey(f.Id)], f.Heavy, Usage(f)))];
            Checked = true;
        }
        finally
        {
            Busy = false;
            foreach (var n in new[] { nameof(Rows), nameof(Busy), nameof(Checked), nameof(HasRows), nameof(AllClear), nameof(Summary) }) OnPropertyChanged(n);
        }
    }
}
