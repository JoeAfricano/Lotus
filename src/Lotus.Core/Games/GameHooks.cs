namespace Lotus.Core.Games;

// Graphics/input "hooks": a DLL with a Windows system name (d3d12.dll, dxgi.dll...) placed next to the game's executable.
// The game loads it instead of Windows' own copy. SCSKiller (recording mode), ReShade, OptiScaler and mod loaders work this way.
// Lotus only lists them and, when the player presses the button, renames one to "<name>.lotus-off" (and back): nothing is deleted.
// Stellar Blade study (26/11/2026): turning SCSKiller's d3d12.dll off removed the remaining hitches; on Wukong it made no difference.
public sealed record GameHook(string FilePath, string Name, string? Product, string? Description, bool Disabled)
{
    public bool IsScsKiller => GameHooks.IsScsKiller(Product, Description);
    // What the button calls it: "SCSKiller" for its proxy, else the file's own product name, else the file name.
    public string Label => IsScsKiller ? "SCSKiller" : GameHooks.Clean(Product) ?? GameHooks.Clean(Description) ?? Name;
}

public enum HookChange { Done, GameRunning, NoAccess, Conflict, Missing }

public interface IGameHookStore
{
    IReadOnlyList<GameHook> Find(GameEntry game); // read-only: file names and version info only
    HookChange Disable(GameHook hook);
    HookChange Enable(GameHook hook);
}

public static class GameHooks
{
    // The names the study found worth checking (Direct3D, DXGI, DirectInput and the generic version.dll loader).
    public static readonly string[] Names = ["d3d12.dll", "dxgi.dll", "d3d11.dll", "dinput8.dll", "version.dll"];
    public const string OffSuffix = ".lotus-off";
    // ".scsk-off" is how the Stellar Blade study turned SCSKiller off by hand; Lotus can turn that one back on too.
    public static readonly string[] OffSuffixes = [OffSuffix, ".scsk-off"];

    public static bool IsScsKiller(string? product, string? description) =>
        (product?.Contains("SCSKiller", StringComparison.OrdinalIgnoreCase) ?? false) || (description?.Contains("SCSKiller", StringComparison.OrdinalIgnoreCase) ?? false);

    // Windows' own copies are Microsoft's; a "proxy" description is a hook even if it borrows the company name.
    public static bool IsHook(string? company, string? description) =>
        (description?.Contains("proxy", StringComparison.OrdinalIgnoreCase) ?? false) || !(company?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ?? false);

    // "d3d12.dll" (active) or "d3d12.dll.lotus-off" (turned off): the system name it stands for, else null.
    public static (string Name, bool Disabled)? Parse(string fileName)
    {
        foreach (var n in Names)
        {
            if (fileName.Equals(n, StringComparison.OrdinalIgnoreCase)) return (n, false);
            foreach (var s in OffSuffixes) if (fileName.Equals(n + s, StringComparison.OrdinalIgnoreCase)) return (n, true);
        }
        return null;
    }

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
