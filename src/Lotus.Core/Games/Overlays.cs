using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Games;

// Overlays and background capture/effects that often cause stutter (Discord with hardware acceleration, vendor overlays,
// Xbox capture, Overwolf/Medal, animated wallpapers...). Read-only: Lotus only explains how to turn each one off.
public sealed record OverlayFinding(string Id, string Name, bool Heavy);
public interface IOverlayScanner { IReadOnlyList<OverlayFinding> Scan(); }

public sealed record OverlayRow(string Id, string Name, string Advice, bool Heavy)
{
    public string Glyph => Heavy ? "" : "";
}

public sealed class OverlayCheckViewModel(IOverlayScanner scanner, ITextCatalog text) : ObservableObject
{
    public ITextCatalog Text { get; } = text;
    public IReadOnlyList<OverlayRow> Rows { get; private set; } = [];
    public bool Busy { get; private set; }
    public bool Checked { get; private set; }
    public bool HasRows => Rows.Count > 0;
    public bool AllClear => Checked && Rows.Count == 0;
    public string Summary => !Checked ? Text["OvHint"] : Rows.Count == 0 ? Text["OvClear"] : Text.Format(Rows.Count == 1 ? "OvFoundOne" : "OvFound", Rows.Count);

    public async Task ScanAsync()
    {
        if (Busy) return;
        Busy = true; OnPropertyChanged(nameof(Busy));
        try
        {
            var found = await Task.Run(scanner.Scan);
            Rows = [.. found.OrderByDescending(f => f.Heavy).Select(f => new OverlayRow(f.Id, f.Name, Text["OvAdvice" + f.Id], f.Heavy))];
            Checked = true;
        }
        finally
        {
            Busy = false;
            foreach (var n in new[] { nameof(Rows), nameof(Busy), nameof(Checked), nameof(HasRows), nameof(AllClear), nameof(Summary) }) OnPropertyChanged(n);
        }
    }
}
