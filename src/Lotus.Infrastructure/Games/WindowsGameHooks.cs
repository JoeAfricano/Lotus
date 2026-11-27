using System.Diagnostics;
using Lotus.Core.Games;

namespace Lotus.Infrastructure.Games;

// Finds hook DLLs (see GameHooks) in the game's folders that hold an executable, and turns one off/on by renaming it.
// Bounded like GameTechScanner: 6 folder levels and 40 000 entries. Never deletes, never asks for administrator.
public sealed class WindowsGameHooks : IGameHookStore
{
    public IReadOnlyList<GameHook> Find(GameEntry game)
    {
        if (game.InstallDir is not { Length: > 3 } root || !Directory.Exists(root)) return [];
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 6, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var found = new List<GameHook>(); var seen = 0;
        try
        {
            foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", options))
            {
                if (++seen > 40_000) break;
                if (GameHooks.Parse(file.Name) is not { } p || !HasExe(file.DirectoryName)) continue;
                var (product, description, company) = Version(file.FullName);
                if (!p.Disabled && !GameHooks.IsHook(company, description)) continue; // Windows' own copy shipped with the game
                found.Add(new(file.FullName, p.Name, product, description, p.Disabled));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        // When a tool reinstalled its file over one Lotus turned off, the active one is what matters.
        return [.. found.Where(h => !h.Disabled || !found.Any(a => !a.Disabled && SameSlot(a, h))).OrderBy(h => h.Disabled).ThenBy(h => h.FilePath, StringComparer.OrdinalIgnoreCase)];
    }

    public HookChange Disable(GameHook hook) => hook.Disabled ? HookChange.Done : Move(hook.FilePath, hook.FilePath + GameHooks.OffSuffix);

    public HookChange Enable(GameHook hook)
    {
        if (!hook.Disabled) return HookChange.Done;
        var original = Path.Combine(Path.GetDirectoryName(hook.FilePath)!, hook.Name);
        return Move(hook.FilePath, original);
    }

    private static HookChange Move(string from, string to)
    {
        try
        {
            if (!File.Exists(from)) return HookChange.Missing;
            // A tool such as SCSKiller puts its file back each time it opens. When the copy Lotus kept is the same file
            // byte for byte, replacing it loses nothing; a different file (another version) is never overwritten.
            if (File.Exists(to) && !SameBytes(from, to)) return HookChange.Conflict;
            File.Move(from, to, overwrite: true);
            return HookChange.Done;
        }
        catch (UnauthorizedAccessException) { return HookChange.NoAccess; }
        catch (FileNotFoundException) { return HookChange.Missing; }
        catch (IOException) { return File.Exists(to) ? HookChange.Conflict : HookChange.GameRunning; } // in use by the running game
    }

    private static bool SameBytes(string a, string b)
    {
        var x = new FileInfo(a); var y = new FileInfo(b);
        if (x.Length != y.Length) return false;
        using var fa = x.OpenRead(); using var fb = y.OpenRead();
        Span<byte> ba = stackalloc byte[8192], bb = stackalloc byte[8192];
        while (true)
        {
            var n = fa.ReadAtLeast(ba, ba.Length, false);
            if (fb.ReadAtLeast(bb, bb.Length, false) != n || !ba[..n].SequenceEqual(bb[..n])) return false;
            if (n == 0) return true;
        }
    }

    private static bool SameSlot(GameHook a, GameHook b) =>
        a.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(Path.GetDirectoryName(a.FilePath), Path.GetDirectoryName(b.FilePath), StringComparison.OrdinalIgnoreCase);

    private static bool HasExe(string? dir)
    {
        try { return dir is not null && Directory.EnumerateFiles(dir, "*.exe").Any(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static (string? Product, string? Description, string? Company) Version(string path)
    {
        try { var v = FileVersionInfo.GetVersionInfo(path); return (v.ProductName, v.FileDescription, v.CompanyName); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException) { return (null, null, null); }
    }
}
