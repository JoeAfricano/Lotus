using Lotus.Core;
using Lotus.Core.Games;
using Xunit;

namespace Lotus.Tests;

public sealed class ShaderPrecompileTests
{
    // Real "scskiller status" output (SCSKiller 1.3.2) captured on this PC.
    private const string Output = "Stale               steam:1593500            God of War  [cache 512 MB]  - driver changed: 617.42 -> 621.01\r\n" +
        "               middleware: DLSS: compiled by the NVIDIA driver itself\r\n" +
        "Ready               steam:1371980            No Rest for the Wicked  [cache 16 MB]  - compiles every DirectX 11 shader\r\n" +
        "Unsupported         steam:1687950            Persona 5 Royal  - no raw DXBC/DXIL shaders in its files (0,1 GB sampled): shaders are compressed or packed: needs an engine reader\r\n";

    [Fact] public void ParsesRealStatusOutput()
    {
        var s = ScsKillerOutput.ParseStatus(Output);
        Assert.Equal(3, s.Count);
        Assert.Equal(new ShaderStatus("steam:1593500", "God of War", ShaderState.Stale, "Stale", "512 MB", "driver changed: 617.42 -> 621.01"), s[0]);
        Assert.Equal(ShaderState.Ready, s[1].State); Assert.Equal("16 MB", s[1].CacheSize);
        Assert.Equal(ShaderState.Unsupported, s[2].State); Assert.Null(s[2].CacheSize); Assert.StartsWith("no raw DXBC", s[2].Detail);
        Assert.Empty(ScsKillerOutput.ParseStatus("usage: scskiller <command>"));
    }

    [Fact] public void MatchesSteamIdsThenNames()
    {
        var s = ScsKillerOutput.ParseStatus(Output);
        Assert.Equal("God of War", ScsKillerOutput.Match(s, new(GameStores.Steam, "1593500", "GOW"))!.Name);
        Assert.Equal("steam:1371980", ScsKillerOutput.Match(s, new(GameStores.Epic, "x", "no rest for the wicked"))!.Id);
        Assert.Null(ScsKillerOutput.Match(s, new(GameStores.Steam, "1", "Other")));
    }

    private sealed class FakeTool(bool installed) : IShaderPrecompiler
    {
        public List<string> Compiled = []; public string Current = Output;
        public bool IsAvailable => installed;
        public Task<IReadOnlyList<ShaderStatus>> StatusAsync(CancellationToken t) => Task.FromResult(ScsKillerOutput.ParseStatus(Current));
        public Task<int> CompileAsync(string id, IProgress<string> p, CancellationToken t) { Compiled.Add(id); Current = Current.Replace("Ready               steam:1371980", "Compiled            steam:1371980"); return Task.FromResult(0); }
    }
    private sealed class Library : IGameLibrary
    {
        public LibraryScan Scan(IReadOnlyList<GameEntry> manual) => new([new(GameStores.Steam, "1593500", "God of War"), new(GameStores.Steam, "1371980", "No Rest for the Wicked"), new(GameStores.Steam, "1687950", "Persona 5 Royal")], []);
    }
    private sealed class NoStore : IManualGameStore { public IReadOnlyList<GameEntry> Load() => []; public void Save(IReadOnlyList<GameEntry> g) { } }
    private sealed class NoLauncher : IGameLauncher { public void Launch(GameEntry game) { } }
    private static GamesViewModel Vm(FakeTool tool) => new(new Library(), new NoStore(), new NoLauncher(), new TextCatalog(), _ => { }, shaders: tool);

    [Fact] public async Task CardsShowShaderStateAndPrecompileUpdatesIt()
    {
        var tool = new FakeTool(true); var vm = Vm(tool);
        await vm.EnsureLoadedAsync();
        Assert.False(vm.ShowShaderToolOffer);
        var gow = vm.Games.Single(g => g.Name == "God of War"); var nrftw = vm.Games.Single(g => g.Name == "No Rest for the Wicked"); var p5 = vm.Games.Single(g => g.Name == "Persona 5 Royal");
        Assert.Equal(vm.Text["ScsStateStale"], gow.ShaderLabel); Assert.True(gow.CanPrecompile);
        Assert.True(nrftw.CanPrecompile); Assert.False(p5.CanPrecompile); Assert.Equal(vm.Text["ScsStateUnsupported"], p5.ShaderLabel);
        Assert.True(await vm.PrecompileAsync(nrftw));
        Assert.Equal(["steam:1371980"], tool.Compiled);
        var after = vm.Games.Single(g => g.Name == "No Rest for the Wicked");
        Assert.Equal(vm.Text["ScsStateCompiled"], after.ShaderLabel); Assert.False(after.CanPrecompile);
        Assert.Equal(vm.Text.Format("ScsDone", "No Rest for the Wicked"), vm.Status);
    }

    [Fact] public async Task WithoutScsKillerTheOptionIsOfferedButNothingRuns()
    {
        var tool = new FakeTool(false); var vm = Vm(tool);
        await vm.EnsureLoadedAsync();
        Assert.True(vm.ShowShaderToolOffer); Assert.All(vm.Games, g => Assert.Null(g.ShaderLabel));
        Assert.False(await vm.PrecompileAsync(vm.Games[0])); Assert.Empty(tool.Compiled);
        Assert.Equal("https://github.com/BlueHeisenberg/SCSKiller/releases/latest", GamesViewModel.ScsKillerReleases);
    }
}
