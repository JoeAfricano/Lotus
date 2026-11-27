using Lotus.Core.Games;
using Lotus.Infrastructure.Games;
using Xunit;

namespace Lotus.Tests;

public sealed class GameHooksTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lotus-hooks-" + Guid.NewGuid().ToString("N"));
    private readonly string bin;
    public GameHooksTests()
    {
        bin = Path.Combine(root, "SB", "Binaries", "Win64");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "SB-Win64-Shipping.exe"), "x");
    }
    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }
    private GameEntry Game => new(GameStores.Steam, "3489700", "Stellar Blade", root);

    [Fact] public void ParsesActiveAndTurnedOffNames()
    {
        Assert.Equal(("d3d12.dll", false), GameHooks.Parse("D3D12.dll"));
        Assert.Equal(("d3d12.dll", true), GameHooks.Parse("d3d12.dll.lotus-off"));
        Assert.Equal(("d3d12.dll", true), GameHooks.Parse("d3d12.dll.scsk-off")); // the study's manual rename
        Assert.Equal(("dinput8.dll", false), GameHooks.Parse("dinput8.dll"));
        Assert.Null(GameHooks.Parse("D3D12Core.dll")); // Agility SDK, Microsoft's own
        Assert.Null(GameHooks.Parse("d3d12.dll.bak"));
    }

    [Fact] public void MicrosoftFilesAreNotHooksUnlessTheySayProxy()
    {
        Assert.False(GameHooks.IsHook("Microsoft Corporation", "Direct3D 12 Runtime"));
        Assert.True(GameHooks.IsHook("Microsoft Corporation", "SCSKiller D3D12 proxy"));
        Assert.True(GameHooks.IsHook(null, null));
        Assert.True(GameHooks.IsScsKiller(null, "SCSKiller D3D12 proxy"));
        Assert.Equal("SCSKiller", new GameHook("x", "d3d12.dll", "Whatever", "SCSKiller D3D12 proxy", false).Label);
        Assert.Equal("ReShade", new GameHook("x", "dxgi.dll", " ReShade ", null, false).Label);
        Assert.Equal("dxgi.dll", new GameHook("x", "dxgi.dll", null, " ", false).Label);
    }

    [Fact] public void TurnsOffAndBackOnByRenamingOnly()
    {
        File.WriteAllText(Path.Combine(bin, "d3d12.dll"), "proxy");
        var store = new WindowsGameHooks();
        var hook = Assert.Single(store.Find(Game));
        Assert.False(hook.Disabled);

        Assert.Equal(HookChange.Done, store.Disable(hook));
        Assert.False(File.Exists(Path.Combine(bin, "d3d12.dll")));
        Assert.Equal("proxy", File.ReadAllText(Path.Combine(bin, "d3d12.dll.lotus-off"))); // nothing deleted
        var off = Assert.Single(store.Find(Game));
        Assert.True(off.Disabled);

        Assert.Equal(HookChange.Done, store.Enable(off));
        Assert.Equal("proxy", File.ReadAllText(Path.Combine(bin, "d3d12.dll")));
        Assert.False(Assert.Single(store.Find(Game)).Disabled);
    }

    [Fact] public void ReinstalledFileWinsAndBlocksTurningBackOn()
    {
        File.WriteAllText(Path.Combine(bin, "d3d12.dll.scsk-off"), "old");
        File.WriteAllText(Path.Combine(bin, "d3d12.dll"), "new"); // the tool installed its file again
        var store = new WindowsGameHooks();
        var hook = Assert.Single(store.Find(Game));
        Assert.False(hook.Disabled);
        Assert.Equal(HookChange.Conflict, store.Enable(new GameHook(Path.Combine(bin, "d3d12.dll.scsk-off"), "d3d12.dll", null, null, true)));
        Assert.Equal("new", File.ReadAllText(Path.Combine(bin, "d3d12.dll")));
    }

    [Fact] public void IgnoresFoldersWithoutAnExecutableAndMissingFiles()
    {
        var other = Path.Combine(root, "Redist"); Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "dxgi.dll"), "x");
        var store = new WindowsGameHooks();
        Assert.Empty(store.Find(Game));
        Assert.Empty(store.Find(new GameEntry(GameStores.Manual, "x", "No folder")));
        Assert.Equal(HookChange.Missing, store.Disable(new GameHook(Path.Combine(bin, "d3d12.dll"), "d3d12.dll", null, null, false)));
    }
}
