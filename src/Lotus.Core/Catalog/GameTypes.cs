namespace Lotus.Core.Catalog;

// One kind of game: a Steam genre ("genre:Action") or community tag ("tag:Open World"). The label comes from the
// text catalog ("Gt" + the name without spaces or symbols).
public sealed record GameTypeOption(string Id)
{
    public string Name => Id[(Id.IndexOf(':') + 1)..];
    public string LabelKey => "Gt" + new string([.. Name.Where(char.IsLetterOrDigit)]);
}
public sealed record GameTypeGroup(string Key, string Glyph, IReadOnlyList<GameTypeOption> Options);

// Lotus's own grouping of Steam genres and tags (the same Steam data, arranged for players who do not know the jargon).
public static class GameTypes
{
    private static GameTypeOption[] O(params string[] ids) => [.. ids.Select(i => new GameTypeOption(i))];

    // What players pick most: shown first, in plain words. The full list is under "Advanced" (Groups).
    public static readonly IReadOnlyList<GameTypeOption> Popular =
        O("genre:Action", "genre:Adventure", "genre:RPG", "genre:Strategy", "genre:Simulation", "tag:FPS", "tag:Open World", "tag:Horror", "tag:Survival",
          "tag:Story Rich", "tag:Co-op", "tag:Multiplayer", "genre:Racing", "genre:Sports", "tag:Souls-like", "tag:Roguelike", "tag:Puzzle", "tag:Platformer",
          "tag:Fighting", "tag:Anime", "genre:Indie", "genre:Casual");

    public static readonly IReadOnlyList<GameTypeGroup> Groups =
    [
        new("Action", "\uE7FC", O("genre:Action", "tag:FPS", "tag:Third-Person Shooter", "tag:Hack and Slash", "tag:Fighting", "tag:Martial Arts", "tag:Platformer",
            "tag:Arcade", "tag:Rhythm", "tag:Shoot 'Em Up", "tag:Endless Runner", "tag:Battle Royale", "tag:Stealth")),
        new("Adventure", "\uE909", O("genre:Adventure", "tag:Action-Adventure", "tag:Exploration", "tag:Metroidvania", "tag:Puzzle", "tag:Hidden Object", "tag:Visual Novel", "tag:Mystery", "tag:Detective")),
        new("Rpg", "\uE7BE", O("genre:RPG", "tag:Action RPG", "tag:JRPG", "tag:Party-Based RPG", "tag:Turn-Based Combat", "tag:Strategy RPG", "tag:Souls-like", "tag:Roguelike", "tag:Roguelite")),
        new("Strategy", "\uE8B9", O("genre:Strategy", "tag:Turn-Based Strategy", "tag:RTS", "tag:Tower Defense", "tag:Grand Strategy", "tag:4X", "tag:Colony Sim",
            "tag:Military", "tag:Board Game", "tag:Card Game", "tag:MOBA")),
        new("Simulation", "\uE80F", O("genre:Simulation", "tag:City Builder", "tag:Automation", "tag:Building", "tag:Crafting", "tag:Life Sim", "tag:Farming Sim",
            "tag:Dating Sim", "tag:Space Sim", "tag:Flight", "tag:Immersive Sim", "tag:Physics", "tag:Sandbox")),
        new("Sports", "\uE804", O("genre:Sports", "tag:Team-Based", "genre:Racing", "tag:Driving", "tag:Automobile Sim")),
        new("Themes", "\uE790", O("tag:Horror", "tag:Sci-fi", "tag:Cyberpunk", "tag:Space", "tag:Fantasy", "tag:Anime", "tag:Open World", "tag:Post-apocalyptic", "tag:Dark", "tag:Atmospheric")),
        new("Look", "\uE7B3", O("tag:2D", "tag:3D", "tag:Pixel Graphics", "tag:Stylized", "tag:Colorful", "tag:Cute", "tag:Realistic", "tag:Retro", "tag:First-Person", "tag:Third Person", "tag:Top-Down")),
        new("Story", "\uE736", O("tag:Story Rich", "tag:Choices Matter", "tag:Multiple Endings", "tag:Female Protagonist", "tag:Great Soundtrack", "tag:Beautiful")),
        new("Friends", "\uE716", O("tag:Singleplayer", "tag:Co-op", "tag:Online Co-Op", "tag:Local Co-Op", "tag:Multiplayer", "genre:Massively Multiplayer", "tag:PvP", "tag:PvE")),
        new("Mood", "\uE706", O("genre:Casual", "tag:Relaxing", "tag:Cozy", "tag:Funny", "tag:Family Friendly", "genre:Indie", "tag:Survival", "tag:Difficult", "tag:Controller", "genre:Early Access")),
    ];
    public static IEnumerable<GameTypeOption> All => Groups.SelectMany(g => g.Options).Concat(Popular).DistinctBy(o => o.Id);
    public static bool IsKnown(string id) => All.Any(o => o.Id == id);
}
