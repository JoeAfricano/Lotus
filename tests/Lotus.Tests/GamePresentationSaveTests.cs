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
        Assert.Equal((true,60,59.93),(on.Presentation.Enabled,on.Presentation.Hz,on.Presentation.Cap));
        Assert.False(on.Before60!.Enabled);
        Assert.True(await vm.SetConsole60Async(Game,false,Info,false));
        var off=store.Data.Games[Game.Key];
        Assert.False(off.Prefer60);Assert.False(off.Presentation.Enabled);Assert.Null(off.Before60);
        Assert.Empty(Directory.GetFiles(Profiles));
        // Without RivaTuner the graphics part still works and the status says why.
        Assert.True(await vm.SetConsole60Async(Game,true,Info with {State="noRtss"},false));
        Assert.True(store.Data.Games[Game.Key].Prefer60);Assert.NotEqual("",vm.Console60Status);
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
