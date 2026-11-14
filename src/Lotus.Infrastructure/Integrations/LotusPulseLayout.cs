using System.Globalization;
using System.Text;

namespace Lotus.Infrastructure.Integrations;

// "Lotus Pulse": the overlay layout Lotus generates itself (RivaTuner OverlayEditor), in the colors of the active theme.
// Big FPS with 1% / 0.1% low pills, frametime graph with reference lines (33.3 ms, 16.7 ms and the FPS limit target),
// GPU / CPU use and temperature with bars, VRAM, RAM and the average. Same data sources as the player's Neon layout
// (read, never written). Labels stay ASCII: RivaTuner's overlay font drops accented letters (seen in game, 14/11/2026).
public static class LotusPulseLayout
{
    // Colors as RRGGBB; Panel as AARRGGBB (translucent so the game shows through).
    public sealed record Colors(string Accent, string Detail, string Text = "FFF5FA", string Muted = "BEB8C1", string Panel = "A6120A12");
    public static readonly Colors Default = new("FF3480", "EEC1A8");

    public const int Width = 236, Height = 302, GraphTop = 124, GraphHeight = 56, GraphWidth = 168;
    public const double GraphMaxMs = 33.3;

    // Y of a frametime inside the graph (0 ms at the bottom, 33.3 ms at the top).
    public static int LineY(double ms) => GraphTop + (int)Math.Round(GraphHeight * (1 - Math.Clamp(ms, 0, GraphMaxMs) / GraphMaxMs));

    public static string Build(string sourcesSection, Colors c, double? capFps)
    {
        var sources = sourcesSection.TrimEnd() + "\r\n";
        var sourceCount = System.Text.RegularExpressions.Regex.Matches(sources, @"\[Source\d+\]").Count;
        var inv = CultureInfo.InvariantCulture;
        var grid = "40" + c.Muted; var track = "26FFFFFF";
        var layers = new List<(string Name, string Text, int X, int Y, int W, int H, int Size, string Color)>
        {
            ("Panel", $"<B={Width},{Height}>", 10, 10, Width, Height, 100, c.Panel),
            ("Accent", $"<B=3,{Height}>", 10, 10, 3, Height, 100, "FF" + c.Accent),
            ("Brand", "LOTUS", 24, 18, 80, 16, 80, c.Detail),
            ("FPS", "<FR>", 22, 32, 120, 58, 330, c.Text),
            ("1% pill", "<B=80,22>", 148, 36, 80, 22, 100, "59" + c.Accent),
            ("1% low", "1%  %FPS Low1 Live%", 156, 38, 70, 18, 90, c.Text),
            ("0.1% pill", "<B=80,22>", 148, 64, 80, 22, 100, "33" + c.Accent),
            ("0.1% low", "0.1%  %FPS Low01 Live%", 156, 66, 70, 18, 90, c.Text),
            ("FPS label", "FPS", 24, 96, 40, 18, 90, c.Accent),
            ("Frametime", "<FT> ms", 64, 96, 100, 18, 90, c.Text),
            ("Line 33", $"<B={GraphWidth},1>", 24, LineY(33.3), GraphWidth, 1, 100, grid),
            ("Label 33", "33", 198, LineY(33.3) - 7, 36, 14, 70, c.Muted),
        };
        // The 16.7 ms line is left out when the limit line sits right next to it (60-75 FPS limits): the limit matters more.
        var targetY = capFps is > 30 and < 1000 ? LineY(1000 / capFps.Value) : (int?)null;
        if (targetY is not { } ty || Math.Abs(ty - LineY(16.7)) >= 12)
        {
            layers.Add(("Line 16", $"<B={GraphWidth},1>", 24, LineY(16.7), GraphWidth, 1, 100, grid));
            layers.Add(("Label 16", "16.7", 198, LineY(16.7) - 7, 36, 14, 70, c.Muted));
        }
        if (capFps is > 30 and < 1000)
        {
            var targetMs = 1000 / capFps.Value;
            layers.Add(("Limit line", $"<B={GraphWidth},1>", 24, LineY(targetMs), GraphWidth, 1, 100, "99" + c.Accent));
            layers.Add(("Limit label", targetMs.ToString("0.0", inv), 198, LineY(targetMs) - 7, 36, 14, 70, c.Accent));
        }
        layers.AddRange(
        [
            ("Frametime graph", "<G=Frametime,0,0,1,0,33.3,0>", 24, GraphTop, GraphWidth, GraphHeight, 73, c.Accent),
            ("GPU label", "GPU", 24, 192, 44, 18, 90, c.Detail),
            ("GPU", "%GPU Load%%  %GPU Temp% °C", 72, 192, 160, 18, 95, c.Text),
            ("GPU track", "<B=208,5>", 24, 214, 208, 5, 100, track),
            ("GPU bar", "<G=GPU Load,0,0,0,0,100,9>", 24, 214, 208, 5, 73, c.Accent),
            ("CPU label", "CPU", 24, 226, 44, 18, 90, c.Detail),
            ("CPU", "%CPU Load%%  %CPU Temp% °C", 72, 226, 160, 18, 95, c.Text),
            ("CPU track", "<B=208,5>", 24, 248, 208, 5, 100, track),
            ("CPU bar", "<G=CPU Load,0,0,0,0,100,9>", 24, 248, 208, 5, 73, c.Accent),
            ("VRAM label", "VRAM", 24, 260, 50, 18, 90, c.Detail),
            ("VRAM", "%VRAM Load%%", 80, 260, 52, 18, 95, c.Text),
            ("RAM label", "RAM", 140, 260, 40, 18, 90, c.Detail),
            ("RAM", "%RAM Load%%", 184, 260, 50, 18, 95, c.Text),
            ("Average label", "AVG", 24, 284, 44, 18, 85, c.Detail),
            ("Average", "%FPS Avg Live% FPS", 72, 284, 160, 18, 85, c.Muted),
        ]);
        var sb = new StringBuilder();
        sb.Append("[Master]\r\nImplementation=2\r\nFontFace=Segoe UI\r\nFontHeight=-16\r\nFontWeight=600\r\nZoomRatio=1\r\n");
        sb.Append("[Settings]\r\nLockUserSettings=1\r\nName=Lotus Pulse\r\nRefreshPeriod=250\r\n");
        sb.Append(inv, $"[General]\r\nSources={sourceCount}\r\nTables=0\r\nLayers={layers.Count}\r\n");
        sb.Append(sources);
        for (var i = 0; i < layers.Count; i++)
        {
            var l = layers[i];
            sb.Append(inv, $"[Layer{i}]\r\nName={l.Name}\r\nText={l.Text}\r\nPositionX={l.X}\r\nPositionY={l.Y}\r\nExtentX={l.W}\r\nExtentY={l.H}\r\nExtentOrigin=0\r\nFixedAlignment=1\r\nSize={l.Size}\r\nTextColor={l.Color}\r\n");
        }
        return sb.ToString();
    }

    // "#RRGGBB" (or "RRGGBB") → "RRGGBB"; anything else falls back.
    public static string Hex(string? color, string fallback)
    {
        var s = (color ?? "").Trim().TrimStart('#');
        return s.Length == 6 && s.All(Uri.IsHexDigit) ? s.ToUpperInvariant() : fallback;
    }
}
