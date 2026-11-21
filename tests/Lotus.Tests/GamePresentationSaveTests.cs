using System.Text.Json;
using Lotus.Core;
using Lotus.Core.Games;
using Lotus.Infrastructure.Games;
using Lotus.Infrastructure.Integrations;
using Xunit;

namespace Lotus.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class GamePresentationSaveTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"LotusPresentationSave-"+Guid.NewGuid());
    private string Profiles=>Path.Combine(root,"Profiles");
    private string Backups=>Path.Combine(root,"Backups");
    private static readonly GameEntry Game=new("Steam","1","Test Game");
    private static readonly GamePresentationPrefs Prefs=new(true,"display",1920,1080,60);
    private static readonly GamesViewModel.LowLatencyInfo Info=new("ok","game.exe",null,true);
    private sealed class Library : IGameLibrary {public LibraryScan Scan(IReadOnlyList<GameEntry> manual)=>new([],[]);}
    private sealed class Manual : IManualGameStore {public IReadOnlyList<GameEntry> Load()=>[];public void Save(IReadOnlyList<GameEntry> games) {}}
    private sealed class Launcher : IGameLauncher {public void Launch(GameEntry game)=>throw new InvalidOperationException("not a launch test");}
    private sealed class Processes : IGameProcessCheck {public bool Running;public bool IsRunning(GameEntry game)=>Running;}
    private sealed class Store : IGamePrefsStore
    {
        public bool Fail;public GamePrefsData Data=new();public GamePrefsData Load()=>Data;
        public void Save(GamePrefsData data) {if(Fail)throw new IOException("disk full");Data=data;}
    }
    private sealed class Display : IGameDisplayControl
    {
        public IReadOnlyList<GameDisplayMode> Modes()=>[new("display","Monitor",1920,1080,60,59.94)];
        public GameDisplayMode? Current(string device)=>new("display","Monitor",1920,1080,75,74.973);
        public bool SetHz(GameDisplayMode mode,int hz)=>throw new InvalidOperationException("Saving must not change the monitor");
    }
    private sealed class Pc : ISystemProfileSource {public SystemProfile Read()=>new("NVIDIA GeForce RTX 5060",8192,12,16_000,1920,1080,75,74.973,"AMD Ryzen 5 5500");}
    public GamePresentationSaveTests()=>Directory.CreateDirectory(Profiles);
    public void Dispose() {if(Directory.Exists(root))Directory.Delete(root,true);}
    private Task<int> Elevated(string[] args)
    {
        if(args[0]=="--rtss-apply") {RtssProfiles.Apply(JsonSerializer.Deserialize<RtssRequest>(File.ReadAllText(args[1]))!,Profiles,Backups);return Task.FromResult(0);}
        return Task.FromResult(RtssProfiles.Restore(args[1],Profiles,Backups)?0:4);
    }
    private GamesViewModel Vm(Store store,Processes? processes=null,Func<string[],Task<int>>? helper=null)=>new(new Library(),new Manual(),new Launcher(),new TextCatalog(),_=>{},processCheck:processes??new())
    {PlayPrefs=new(store,_=>{}),DisplayControl=new Display(),Rtss=new WindowsRtssIntegration(Profiles,Backups,helper??Elevated)};
    [Fact] public async Task SavePreparesAndVerifiesRtssWithoutTouchingHz()
    {
        var store=new Store();var vm=Vm(store);
        Assert.True(await vm.SavePresentationAsync(Game,Prefs,Info,false));
        var saved=store.Data.Games[Game.Key].Presentation;
        Assert.Equal("fluid",saved.AppliedLimiter);Assert.Equal(59.93,saved.Cap);Assert.Equal(59.94,saved.PreparedRate);
        Assert.Equal(0,saved.RequestedCap);Assert.Equal("game.exe",saved.Exe);
        Assert.Equal("fluid",vm.Rtss!.LimiterMode("game.exe"));
    }
    [Fact] public async Task ConsoleSixtySwitchesTo60HzWithRtssAndRestoresWhenOff()
    {
        var store=new Store();
        var vm=new GamesViewModel(new Library(),new Manual(),new Launcher(),new TextCatalog(),_=>{},profileSource:new Pc(),processCheck:new Processes())
            {PlayPrefs=new(store,_=>{}),DisplayControl=new Display(),Rtss=new WindowsRtssIntegration(Profiles,Backups,Elevated)};
        Assert.True(await vm.SetConsole60Async(Game,true,Info,false));
        var on=store.Data.Games[Game.Key];
        Assert.True(on.Prefer60);Assert.Equal("",vm.Console60Status);
        // Modo Console (21/11/2026): RivaTuner just under the real 60 Hz (a cap equal to it queued frames) and Fluidez pacing.
        Assert.Equal((true,60,59.93,"fluid"),(on.Presentation.Enabled,on.Presentation.Hz,on.Presentation.Cap,on.Presentation.AppliedLimiter));
        Assert.Equal(59.93,vm.Rtss!.LimiterCap("game.exe"));Assert.Equal("fluid",vm.Rtss.LimiterMode("game.exe"));
        Assert.False(on.Before60!.Enabled);
        Assert.True(await vm.SetConsole60Async(Game,false,Info,false));
        var off=store.Data.Games[Game.Key];
        Assert.False(off.Prefer60);Assert.False(off.Presentation.Enabled);Assert.Null(off.Before60);
        Assert.Empty(Directory.GetFiles(Profiles));
        // Without RivaTuner the graphics part still works and the status says why.
        Assert.True(await vm.SetConsole60Async(Game,true,Info with {State="noRtss"},false));
        Assert.True(store.Data.Games[Game.Key].Prefer60);Assert.NotEqual("",vm.Console60Status);
    }
    private sealed class LimitRecipe : IGameSettingsRecipe
    {
        public string Raw="limit=60";public int Writes;
        public string Id=>"fake";
        public GameSettingsSupport? Probe(GameEntry game)=>new(Id,"Fake",true,"");
        public string? ReadRaw(GameEntry game)=>Raw;
        public void WriteRaw(GameEntry game,string raw) {Raw=raw;Writes++;}
        public string Transform(string raw,SettingsRecommendation recommendation)=>raw;
        public string? ClearFpsLimit(string raw)=>raw=="limit=0" ? null : "limit=0";
    }
    private sealed class Originals : IGameSettingsStore
    {
        public string? Original;
        public string? LoadOriginal(string gameKey)=>Original;
        public void SaveOriginalOnce(string gameKey,string raw)=>Original??=raw;
        public void ClearOriginal(string gameKey)=>Original=null;
    }
    [Fact] public async Task ConsoleModeTurnsTheGamesOwnLimitOffAndKeepsTheOriginal()
    {
        var store=new Store();var recipe=new LimitRecipe();var originals=new Originals();var processes=new Processes();
        var vm=new GamesViewModel(new Library(),new Manual(),new Launcher(),new TextCatalog(),_=>{},profileSource:new Pc(),recipes:[recipe],settingsStore:originals,processCheck:processes)
            {PlayPrefs=new(store,_=>{}),DisplayControl=new Display(),Rtss=new WindowsRtssIntegration(Profiles,Backups,Elevated)};
        Assert.True(await vm.SetConsole60Async(Game,true,Info,false));
        // Stellar Blade (21/11/2026) kept its own 60 under RivaTuner's 59.99: only RivaTuner limits now.
        Assert.Equal("limit=0",recipe.Raw);Assert.Equal("limit=60",originals.Original);
        Assert.True(await vm.SetConsole60Async(Game,false,Info,false));
        Assert.True(await vm.SetConsole60Async(Game,true,Info,false));
        Assert.Equal(1,recipe.Writes);Assert.Equal("limit=60",originals.Original);
        // Without RivaTuner nothing paces the game, so its own limit stays.
        var other=new LimitRecipe();
        var noRtss=new GamesViewModel(new Library(),new Manual(),new Launcher(),new TextCatalog(),_=>{},profileSource:new Pc(),recipes:[other],settingsStore:new Originals(),processCheck:processes)
            {PlayPrefs=new(new Store(),_=>{}),DisplayControl=new Display(),Rtss=new WindowsRtssIntegration(Profiles,Backups,Elevated)};
        Assert.True(await noRtss.SetConsole60Async(Game,true,Info with {State="noRtss"},false));
        Assert.Equal("limit=60",other.Raw);
    }
    [Fact] public void ConsoleModeKeepsTheFrameGenLimiterWhenThePlayerUsesFrameGen()
    {
        Assert.Equal("fluid",GamesViewModel.ConsoleLimiter(Info));
        Assert.Equal("fluid",GamesViewModel.ConsoleLimiter(null));
        Assert.Equal("auto",GamesViewModel.ConsoleLimiter(Info with {FrameGen=true,UsesFrameGen=true}));
        Assert.Equal(60,GamesViewModel.ConsoleCap);
    }
    [Fact] public async Task RunningGamePreventsProfileOrPreferenceWrites()
    {
        var store=new Store();var vm=Vm(store,new(){Running=true});
        Assert.False(await vm.SavePresentationAsync(Game,Prefs,Info,false));
        Assert.Empty(store.Data.Games);Assert.Empty(Directory.GetFiles(Profiles));
    }
    [Fact] public async Task FailedPreferenceSaveRollsBackTheRtssFile()
    {
        var vm=Vm(new Store{Fail=true});
        Assert.False(await vm.SavePresentationAsync(Game,Prefs,Info,false));Assert.Empty(Directory.GetFiles(Profiles));
    }
    [Fact] public async Task DeclinedPermissionDoesNotEnableAPlan()
    {
        var store=new Store();var vm=Vm(store,helper:_=>Task.FromResult(1223));
        Assert.False(await vm.SavePresentationAsync(Game,Prefs,Info,false));Assert.Empty(store.Data.Games);
    }
    [Fact] public async Task DisableRestoresTheOriginalProfileAndPersistsOff()
    {
        var store=new Store();var vm=Vm(store);
        Assert.True(await vm.SavePresentationAsync(Game,Prefs,Info,false));
        Assert.True(await vm.SavePresentationAsync(Game,Prefs with {Enabled=false},Info,false));
        Assert.False(store.Data.Games[Game.Key].Presentation.Enabled);Assert.Empty(Directory.GetFiles(Profiles));
    }
    [Theory] [InlineData(false,"Vulkan")] [InlineData(true,"Vulkan")]
    public async Task UnsupportedReflexDoesNotWrite(bool nvidia,string api)
    {
        var store=new Store();var vm=Vm(store);
        Assert.False(await vm.SavePresentationAsync(Game,Prefs with {Limiter="responsive"},Info with {Nvidia=nvidia,Api=api},false));
        Assert.Empty(store.Data.Games);Assert.Empty(Directory.GetFiles(Profiles));
    }
    [Fact] public async Task AmdFrameGenerationDoesNotRequireNvidiaLimiter()
    {
        var store=new Store();var vm=Vm(store);
        Assert.True(await vm.SavePresentationAsync(Game,Prefs,Info with {Nvidia=false,UsesFrameGen=true},false));
        Assert.Equal("fluid",store.Data.Games[Game.Key].Presentation.AppliedLimiter);
    }
}
