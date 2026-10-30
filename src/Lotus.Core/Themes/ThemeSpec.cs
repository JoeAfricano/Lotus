using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lotus.Core.Themes;

// What the player picks in the Theme Studio. Everything else (borders, hover, secondary text, accent variants…) is
// derived from these, so any combination still looks finished — and stays readable.
public sealed record ThemeSpec
{
    public string Name { get; init; } = "Meu tema";
    public bool Light { get; init; }
    public string Canvas { get; init; } = "#100D15";   // window background
    public string Surface { get; init; } = "#211A29";  // cards and panels
    public string Text { get; init; } = "#F8F5FA";
    public string Accent { get; init; } = "#E04C84";   // buttons, selection, highlights
    public string Detail { get; init; } = "#EEC1A8";   // the second color: titles, numbers, the gold of the default theme
    public double CardOpacity { get; init; } = 1;       // 0.78–1: lower lets the background show through the cards
    public BackdropSpec Backdrop { get; init; } = new();

    public const int NameLimit = 32;
    public static readonly string[] Kinds = ["none", "gradient", "petals", "aurora", "particles", "waves", "lotus"];
}

// The animated background: a slow gradient of 2–5 colors or one of the Lotus scenes (which use the same colors).
public sealed record BackdropSpec
{
    public string Kind { get; init; } = "lotus";
    public IReadOnlyList<string> Colors { get; init; } = ["#A8124F", "#5E0A2E", "#2A0F24"];
    public double Speed { get; init; } = 1;      // 0.25–2
    public double Intensity { get; init; } = 0.6; // 0.1–1
    public bool Grain { get; init; } = true;
    public const int MinColors = 2, MaxColors = 5;
}

// Every color the app uses, computed from a spec. "Adjusted" lists what was changed to keep the text readable.
public sealed record ThemeTokens(
    bool Light, ThemeColor Canvas, ThemeColor Shell, ThemeColor Card, ThemeColor Raised, ThemeColor Border, ThemeColor Divider,
    ThemeColor Text, ThemeColor TextSecondary, ThemeColor AccentFill, ThemeColor OnAccent, ThemeColor AccentText, ThemeColor Detail,
    ThemeColor Success, ThemeColor Warning, ThemeColor Error, double CardOpacity, IReadOnlyList<string> Adjusted)
{
    public bool WasAdjusted => Adjusted.Count > 0;
}

public static class ThemeEngine
{
    // WCAG: body text 7:1 (AAA) on cards and the canvas, secondary text and colored text 4.5:1 (AA), white on buttons 4.5:1.
    public const double TextContrast = 7, SecondaryContrast = 4.5, ColorContrast = 4.5;

    public static ThemeTokens Derive(ThemeSpec spec)
    {
        var s = Normalize(spec);
        var adjusted = new List<string>();
        var canvas = ThemeColor.Parse(s.Canvas);
        var surface = ThemeColor.Parse(s.Surface);
        // Transparent cards: what the text really sits on is the card over the brightest (or darkest) backdrop color.
        var worst = s.Backdrop.Kind == "none" ? canvas
            : s.Backdrop.Colors.Select(ThemeColor.Parse).Append(canvas).OrderBy(c => s.Light ? c.Luminance : -c.Luminance).First();
        var effectiveCard = surface.Mix(worst, 1 - s.CardOpacity);

        var text = ThemeColor.Parse(s.Text);
        var readable = text.Readable(effectiveCard, TextContrast).Readable(canvas, TextContrast);
        if (readable != text) adjusted.Add("text");
        text = readable;
        var secondary = text.Mix(surface, 0.3).Readable(effectiveCard, SecondaryContrast);

        var accent = ThemeColor.Parse(s.Accent);
        var accentText = accent.Readable(effectiveCard, ColorContrast).Readable(canvas, ColorContrast);
        if (accentText != accent) adjusted.Add("accent");
        // Text on buttons: white when it reads well; on bright accents (neon yellow, mint) dark text keeps the color as
        // chosen; otherwise the fill is darkened until white reads well.
        var fill = accent; var onAccent = ThemeColor.White;
        var ink = ThemeColor.Parse("#141018");
        if (ThemeColor.White.Contrast(fill) < ColorContrast && ink.Contrast(fill) >= ColorContrast) onAccent = ink;
        else for (var i = 0; i < 20 && ThemeColor.White.Contrast(fill) < ColorContrast; i++) fill = fill.Darken(0.06);

        var detail = ThemeColor.Parse(s.Detail);
        var detailText = detail.Readable(effectiveCard, ColorContrast);
        if (detailText != detail) adjusted.Add("detail");

        var shell = canvas.Mix(surface, 0.4);
        var raised = surface.Mix(text, s.Light ? 0.05 : 0.07);
        var border = surface.Mix(text, s.Light ? 0.16 : 0.13);
        var divider = surface.Mix(text, 0.08);
        var success = (s.Light ? ThemeColor.Parse("#246C4D") : ThemeColor.Parse("#8AD6B3")).Readable(effectiveCard, ColorContrast);
        var warning = (s.Light ? ThemeColor.Parse("#77501B") : ThemeColor.Parse("#EAC183")).Readable(effectiveCard, ColorContrast);
        var error = (s.Light ? ThemeColor.Parse("#B32242") : ThemeColor.Parse("#FF9CA6")).Readable(effectiveCard, ColorContrast);
        return new(s.Light, canvas, shell, surface, raised, border, divider, text, secondary, fill, onAccent, accentText, detailText, success, warning, error, s.CardOpacity, adjusted);
    }

    // Cleans anything coming from the studio or from a pasted code: valid colors, sane ranges, a short name.
    public static ThemeSpec Normalize(ThemeSpec s)
    {
        string C(string? hex, string fallback) => ThemeColor.TryParse(hex, out var c) ? c.Hex : fallback;
        var d = new ThemeSpec();
        var b = s.Backdrop ?? new BackdropSpec();
        var colors = (b.Colors ?? []).Select(x => ThemeColor.TryParse(x, out var c) ? c.Hex : null).OfType<string>().Take(BackdropSpec.MaxColors).ToList();
        if (colors.Count < BackdropSpec.MinColors) colors = [.. d.Backdrop.Colors];
        var name = (s.Name ?? "").Trim();
        return s with
        {
            Name = name.Length == 0 ? d.Name : name.Length > ThemeSpec.NameLimit ? name[..ThemeSpec.NameLimit] : name,
            Canvas = C(s.Canvas, d.Canvas), Surface = C(s.Surface, d.Surface), Text = C(s.Text, d.Text), Accent = C(s.Accent, d.Accent), Detail = C(s.Detail, d.Detail),
            CardOpacity = double.IsFinite(s.CardOpacity) ? Math.Clamp(s.CardOpacity, 0.78, 1) : 1,
            Backdrop = b with
            {
                Kind = ThemeSpec.Kinds.Contains(b.Kind) ? b.Kind : "none", Colors = colors,
                Speed = double.IsFinite(b.Speed) ? Math.Clamp(b.Speed, 0.25, 2) : 1,
                Intensity = double.IsFinite(b.Intensity) ? Math.Clamp(b.Intensity, 0.1, 1) : 0.6,
            },
        };
    }

    // "Começar de uma cor": a complete, readable theme from one color (dark or light).
    public static ThemeSpec FromColor(ThemeColor seed, bool light, string name)
    {
        var (h, sat, _) = seed.Hsl;
        var s = Math.Max(sat, 0.45);
        return light
            ? new ThemeSpec
            {
                Name = name, Light = true,
                Canvas = ThemeColor.FromHsl(h, Math.Min(s, 0.45), 0.95).Hex, Surface = ThemeColor.FromHsl(h, Math.Min(s, 0.5), 0.985).Hex,
                Text = ThemeColor.FromHsl(h, 0.45, 0.12).Hex, Accent = ThemeColor.FromHsl(h, Math.Min(s + 0.1, 0.8), 0.42).Hex, Detail = ThemeColor.FromHsl(h + 30, 0.55, 0.33).Hex,
                Backdrop = new() { Kind = "gradient", Colors = [ThemeColor.FromHsl(h, 0.7, 0.82).Hex, ThemeColor.FromHsl(h + 35, 0.6, 0.86).Hex, ThemeColor.FromHsl(h - 35, 0.6, 0.88).Hex], Intensity = 0.5, Grain = true },
            }
            : new ThemeSpec
            {
                Name = name, Light = false,
                Canvas = ThemeColor.FromHsl(h, Math.Min(s, 0.35), 0.06).Hex, Surface = ThemeColor.FromHsl(h, Math.Min(s, 0.3), 0.12).Hex,
                Text = ThemeColor.FromHsl(h, 0.2, 0.96).Hex, Accent = ThemeColor.FromHsl(h, Math.Max(s, 0.6), 0.6).Hex, Detail = ThemeColor.FromHsl(h + 30, 0.6, 0.8).Hex,
                Backdrop = new() { Kind = "gradient", Colors = [ThemeColor.FromHsl(h, 0.7, 0.32).Hex, ThemeColor.FromHsl(h + 40, 0.65, 0.26).Hex, ThemeColor.FromHsl(h - 40, 0.6, 0.2).Hex], Intensity = 0.6, Grain = true },
            };
    }

    // ---- Sharing: "LOTUS-THEME:" + base64url(JSON). Works in any chat; nothing goes through a server. ----
    public const string CodePrefix = "LOTUS-THEME:";
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public static string ToCode(ThemeSpec spec)
    {
        var s = Normalize(spec);
        var dto = new CodeDto(1, s.Name, s.Light, s.Canvas, s.Surface, s.Text, s.Accent, s.Detail, Math.Round(s.CardOpacity, 2),
            s.Backdrop.Kind, [.. s.Backdrop.Colors], Math.Round(s.Backdrop.Speed, 2), Math.Round(s.Backdrop.Intensity, 2), s.Backdrop.Grain);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(dto, ThemeJson.Default.CodeDto);
        return CodePrefix + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryFromCode(string? code, out ThemeSpec spec)
    {
        spec = new ThemeSpec();
        var text = code?.Trim() ?? "";
        var at = text.IndexOf(CodePrefix, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return false;
        var body = new string([.. text[(at + CodePrefix.Length)..].TakeWhile(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')]);
        if (body.Length is 0 or > 4000) return false;
        try
        {
            var b64 = body.Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            var dto = JsonSerializer.Deserialize(Convert.FromBase64String(b64), ThemeJson.Default.CodeDto);
            if (dto is null || dto.V != 1) return false;
            spec = Normalize(new ThemeSpec
            {
                Name = dto.N ?? "", Light = dto.L, Canvas = dto.Bg ?? "", Surface = dto.Sf ?? "", Text = dto.Tx ?? "", Accent = dto.Ac ?? "", Detail = dto.Dt ?? "", CardOpacity = dto.Op,
                Backdrop = new BackdropSpec { Kind = dto.K ?? "none", Colors = dto.Cs ?? [], Speed = dto.Sp, Intensity = dto.In, Grain = dto.Gr },
            });
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException) { return false; }
    }

    internal sealed record CodeDto(int V, string? N, bool L, string? Bg, string? Sf, string? Tx, string? Ac, string? Dt, double Op, string? K, List<string>? Cs, double Sp, double In, bool Gr);
}

[JsonSerializable(typeof(ThemeEngine.CodeDto))]
internal sealed partial class ThemeJson : JsonSerializerContext { }
