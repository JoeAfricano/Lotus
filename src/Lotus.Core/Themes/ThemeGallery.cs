namespace Lotus.Core.Themes;

// The 12 ready-made themes (each with its own background). Any of them can be opened in the studio and changed.
public static class ThemeGallery
{
    public sealed record Preset(string Id, ThemeSpec Spec);

    private static ThemeSpec T(string name, bool light, string canvas, string surface, string text, string accent, string detail, string kind, string[] colors, double intensity, bool grain = true, double speed = 1, double cards = 1) =>
        new() { Name = name, Light = light, Canvas = canvas, Surface = surface, Text = text, Accent = accent, Detail = detail, CardOpacity = cards,
                Backdrop = new() { Kind = kind, Colors = colors, Intensity = intensity, Grain = grain, Speed = speed } };

    public static readonly IReadOnlyList<Preset> All =
    [
        new("lotus", T("Lotus Rosé", false, "#100D15", "#211A29", "#F8F5FA", "#E04C84", "#EEC1A8", "lotus", ["#A8124F", "#5E0A2E", "#2A0F24"], 0.6)),
        new("sakura", T("Sakura", true, "#FBEFF3", "#FFFAFC", "#3A1424", "#C2185B", "#9C4A2E", "petals", ["#EC407A", "#F06292", "#F8A5C2"], 0.8, cards: 0.92)),
        new("synthwave", T("Synthwave", false, "#120A24", "#1E1238", "#F7F0FF", "#FF4FD8", "#FFB86B", "waves", ["#FF4FD8", "#7A5CFF", "#FF9E4F"], 0.7, cards: 0.88)),
        new("cyberpunk", T("Cyberpunk", false, "#0B0B0F", "#16161D", "#F4F4F0", "#FCEE0A", "#00F0FF", "particles", ["#FCEE0A", "#00F0FF", "#FF2A6D"], 0.6, grain: false)),
        new("aurora", T("Aurora", false, "#071417", "#0F2226", "#EFFFFB", "#34E0A1", "#9EC5FF", "aurora", ["#1DE9B6", "#7C4DFF", "#00B0FF"], 0.65, cards: 0.9)),
        new("ocean", T("Oceano", false, "#06121F", "#0E1E30", "#EEF6FF", "#3BA0FF", "#7FE3E0", "gradient", ["#0B4F8A", "#06306B", "#0E7490", "#123A6B"], 0.6, speed: 0.7)),
        new("forest", T("Floresta", false, "#0A140E", "#132219", "#F0F7F1", "#5CC98A", "#D9C58A", "particles", ["#5CC98A", "#A3D977", "#2E7D4F"], 0.5)),
        new("gold", T("Ouro Real", false, "#0D0B08", "#1A1712", "#FBF6EC", "#D4A84F", "#F0D9A0", "particles", ["#D4A84F", "#8C6A2A", "#F0D9A0"], 0.5, speed: 0.6)),
        new("mono", T("Monocromático", false, "#0E0E0F", "#1B1B1D", "#F5F5F5", "#C8C8CC", "#FFFFFF", "gradient", ["#2A2A2E", "#18181B", "#3A3A40"], 0.45)),
        new("vampire", T("Vampiro", false, "#0C0507", "#1C0B10", "#FBEFF1", "#E0243F", "#F2A7B0", "petals", ["#8B0A1F", "#E0243F", "#3D0610"], 0.55)),
        new("ice", T("Gelo", true, "#EEF5FA", "#FAFDFF", "#0F2333", "#1F6FB2", "#2E7D8C", "aurora", ["#9FD3FF", "#C6DCFF", "#A8EDEA"], 0.5, cards: 0.92)),
        new("porcelain", T("Porcelana", true, "#F7ECF1", "#FFFAFC", "#2B1020", "#B0195A", "#8E581E", "gradient", ["#F5D0DF", "#F7E1D3", "#EAD3F0"], 0.45, speed: 0.6)),
    ];

    public static Preset? Find(string id) => All.FirstOrDefault(p => p.Id == id);
}
