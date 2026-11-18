namespace Lotus.Core.Benchmark;

// How the Lotus overlay window looks. The set of options follows CapFrameX's overlay (entries the player turns on and
// orders, position, opacity, size, three profiles, a hotkey for the position); the code and the look are Lotus's own.
public sealed record OverlayWindowSettings
{
    public static readonly string[] AllItems = ["fps", "lows", "frametime", "graph", "gpu", "cpu", "memory", "average"];
    public const string TopLeft = "top-left", TopRight = "top-right", BottomLeft = "bottom-left", BottomRight = "bottom-right";
    public static readonly string[] Positions = [TopLeft, TopRight, BottomRight, BottomLeft]; // the hotkey goes round in this order

    public string Position { get; init; } = TopLeft;
    public double Opacity { get; init; } = 0.85;  // of the card body; text stays solid
    public double Zoom { get; init; } = 1;
    public bool Visible { get; init; } = true;
    public string ToggleKey { get; init; } = "F10";
    public static readonly string[] ToggleKeys = ["F6","F7","F8","F9","F10","F11","none"];
    public IReadOnlyList<string> Items { get; init; } = AllItems; // shown, in this order

    public const double MinOpacity = 0.4, MaxOpacity = 1, MinZoom = 0.75, MaxZoom = 1.5;

    // Anything unknown or out of range falls back, so a hand-edited preferences file never breaks the overlay.
    public OverlayWindowSettings Normalized()
    {
        var items = (Items ?? []).Where(i => AllItems.Contains(i)).Distinct().ToList();
        return this with
        {
            Position = Positions.Contains(Position) ? Position : TopLeft,
            Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, MinOpacity, MaxOpacity) : 0.85,
            Zoom = double.IsFinite(Zoom) ? Math.Clamp(Zoom, MinZoom, MaxZoom) : 1,
            ToggleKey = ToggleKeys.Contains(ToggleKey) ? ToggleKey : "F10",
            Items = items.Count > 0 ? items : AllItems,
        };
    }

    public OverlayWindowSettings NextPosition() => this with { Position = Positions[(Array.IndexOf(Positions, Position) + 1) % Positions.Length] };

    public OverlayWindowSettings Toggle(string item) => AllItems.Contains(item)
        ? this with { Items = Items.Contains(item) ? [.. Items.Where(i => i != item)] : [.. Items, item] } : this; // a new item goes to the end

    // Moves a shown item one place up (-1) or down (+1).
    public OverlayWindowSettings Move(string item, int step)
    {
        var list = Items.ToList(); var at = list.IndexOf(item); var to = at + step;
        if (at < 0 || to < 0 || to >= list.Count) return this;
        (list[at], list[to]) = (list[to], list[at]);
        return this with { Items = list };
    }

    // Three ready profiles: complete, compact (numbers only) and minimal (FPS and lows).
    public static IReadOnlyList<OverlayWindowSettings> DefaultProfiles =>
    [
        new(),
        new() { Items = ["fps", "lows", "frametime", "gpu", "cpu"], Zoom = 0.9 },
        new() { Items = ["fps", "lows"], Zoom = 0.85, Opacity = 0.7 },
    ];
}
