using Lotus.Core.Games;
using Lotus.Infrastructure.Games;
using Xunit;

namespace Lotus.Tests;

public sealed class GamePresentationTests
{
    private static readonly GameDisplayMode Original=new("display1","Monitor",1920,1080,75,74.973);
    private static GamePresentationPrefs Prefs(int hz=60)=>new(true,"display1",1920,1080,hz);
    private sealed class Display : IGameDisplayControl
    {
        public GameDisplayMode? Mode=Original;
        public bool Fail,Partial;
        public List<int> Writes=[];
        public IReadOnlyList<GameDisplayMode> Modes()=>[Original,Original with {Hz=60,ExactHz=59.94},Original with {Width=1280,Hz=144},Original with {Device="display2",Hz=240}];
        public GameDisplayMode? Current(string device)=>Mode;
        public bool SetHz(GameDisplayMode before,int hz)
        {
            Assert.NotNull(Mode); Assert.True(Mode.SameSetting(before));
            Writes.Add(hz);
            if(!Fail || Partial) Mode=Mode with {Hz=hz,ExactHz=hz==75?74.973:59.94};
            if(Partial) {Partial=false;Fail=false;return false;}
            return !Fail;
        }
    }
    private sealed class Store : IDisplayRecoveryStore
    {
        public DisplayRecovery? Value;
        public bool Fail;
        public DisplayRecovery? Load()=>Value;
        public void Save(DisplayRecovery? value) {if(Fail)throw new IOException("disk full");Value=value;}
    }
    [Fact] public void DefaultIsOff()=>Assert.False(new GamePlayPrefs().Presentation.Enabled);
    [Fact] public async Task TransientLossOfFocusDoesNotFlipHzBackAndForth()
    {
        var wait=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transitions=new FocusTransitionGate(_=>wait.Task);
        var display=new Display(); var session=new FocusDisplaySession(display,new Store());
        Assert.True(session.Update("game",Prefs()));
        async Task Observe(string? key)
        {
            var ticket=transitions.Next();
            if(await transitions.WaitAsync(ticket)) session.Update(key,key is null?null:Prefs());
        }
        var temporary=Observe(null);var backToGame=Observe("game");
        wait.SetResult();await Task.WhenAll(temporary,backToGame);
        Assert.Equal(new[]{60},display.Writes);Assert.Equal(60,display.Mode!.Hz);
        await Observe(null);Assert.Equal(new[]{60,75},display.Writes);
    }
    [Fact] public void AutoNeverTradesResolutionForHz()
    {
        var d=new Display(); Assert.Equal(75,PresentationPlan.ResolveHz(Prefs(0),d.Modes()));
        Assert.Equal(0,PresentationPlan.ResolveHz(Prefs(144),d.Modes()));
        Assert.Equal(2,PresentationPlan.ForOutput(d.Modes(),Original).Count);
    }
    [Fact] public void FocusChangesOnlyHzAndRestoresOnAltTab()
    {
        var d=new Display();var s=new Store();var session=new FocusDisplaySession(d,s);
        Assert.True(session.Update("game",Prefs())); Assert.Equal(60,d.Mode!.Hz);Assert.NotNull(s.Value);
        Assert.True(session.Update("game",Prefs())); Assert.Single(d.Writes);
        Assert.True(session.Update(null,null));Assert.Equal(75,d.Mode.Hz);Assert.Null(s.Value);
        Assert.Equal(1920,d.Mode.Width);Assert.Equal(1080,d.Mode.Height);
        Assert.True(session.Update("game",Prefs()));Assert.Equal(new[]{60,75,60},d.Writes);
    }
    [Fact] public void AlreadyAtTargetRemainsValidUntilFocusChanges()
    {
        var d=new Display();var session=new FocusDisplaySession(d,new Store());
        Assert.True(session.Update("game",Prefs(75)));Assert.True(session.Update("game",Prefs(75)));Assert.Empty(d.Writes);
    }
    [Fact] public void ExternalChangeIsNotOverwritten()
    {
        var d=new Display();var s=new Store();var session=new FocusDisplaySession(d,s);
        session.Update("game",Prefs());d.Mode=Original with {Hz=50,ExactHz=50};
        Assert.True(session.Update(null,null));Assert.Equal(50,d.Mode.Hz);Assert.Single(d.Writes);Assert.Null(s.Value);
    }
    [Fact] public void ChangedResolutionIsNotForced()
    {
        var d=new Display {Mode=Original with {Width=1280}};var session=new FocusDisplaySession(d,new Store());
        Assert.False(session.Update("game",Prefs()));Assert.Empty(d.Writes);Assert.Equal("resolution-changed",session.Status);
    }
    [Fact] public void CannotWriteJournalMeansNoPhysicalChange()
    {
        var d=new Display();var session=new FocusDisplaySession(d,new Store {Fail=true});
        Assert.Throws<IOException>(()=>session.Update("game",Prefs()));Assert.Empty(d.Writes);
    }
    [Fact] public void PartialDriverFailureRollsBackAndDoesNotLoop()
    {
        var d=new Display {Partial=true};var s=new Store();var session=new FocusDisplaySession(d,s);
        Assert.False(session.Update("game",Prefs()));Assert.Equal(75,d.Mode!.Hz);Assert.Null(s.Value);
        Assert.False(session.Update("game",Prefs()));Assert.Equal(new[]{60,75},d.Writes);
    }
    [Fact] public void DisconnectedMonitorRetainsRecoveryUntilAvailable()
    {
        var d=new Display();var s=new Store();var session=new FocusDisplaySession(d,s);
        session.Update("game",Prefs());var applied=d.Mode;d.Mode=null;
        Assert.False(session.Update(null,null));Assert.NotNull(s.Value);
        d.Mode=applied;Assert.True(session.Update(null,null));Assert.Equal(75,d.Mode!.Hz);
    }
    [Fact] public void NewInstanceRecoversInterruptedSession()
    {
        var d=new Display();var s=new Store();new FocusDisplaySession(d,s).Update("game",Prefs());
        Assert.True(new FocusDisplaySession(d,s).Recover());Assert.Equal(75,d.Mode!.Hz);Assert.Null(s.Value);
    }
    [Theory]
    [InlineData(false,null,false,"fluid")]
    [InlineData(true,null,false,"fluid")]
    [InlineData(true,"Direct3D 12",true,"responsive")]
    [InlineData(true,"Vulkan",true,"fluid")]
    [InlineData(false,"Direct3D 12",true,"fluid")]
    public void AutomaticModeHonorsGpuApiAndFrameGeneration(bool nvidia,string? api,bool fg,string expected)
        =>Assert.Equal(expected,PresentationPlan.ResolveLimiter(Prefs(),nvidia,api,fg));
    [Fact] public void ManualChoiceIsPreserved()=>Assert.Equal("back",PresentationPlan.ResolveLimiter(Prefs() with {Limiter="back"},true,"Direct3D 12",false));
    [Fact] public void FractionalHzAndVrrCapsAreNotRoundedUp()
    {
        Assert.InRange(PresentationPlan.Cap(Prefs(),59.94,false),59.92,59.94);
        Assert.Equal(56.94,PresentationPlan.Cap(Prefs(),59.94,true),5);
    }
    [Theory] [InlineData("../game.exe")] [InlineData("other.exe")]
    public void ProcessWithWrongPathDoesNotAcquireDisplay(string exe)
    {
        var root=Path.Combine(Path.GetTempPath(),"LotusGame");var plan=Prefs() with {Exe="game.exe"};
        var registration=new GamePresentationRegistration("game",root,plan);
        Assert.False(registration.Matches(Path.GetFullPath(Path.Combine(root,exe))));
        Assert.True(registration.Matches(Path.Combine(root,"Bin","game.exe")));
    }
    [Fact] public void DisplayRecoveryPersistsWithoutChangingWindows()
    {
        var root=Path.Combine(Path.GetTempPath(),"LotusDisplayTest-"+Guid.NewGuid());
        try
        {
            var store=new JsonDisplayRecovery(Path.Combine(root,"session.json"));
            var value=new DisplayRecovery(Original,Original with {Hz=60});store.Save(value);
            Assert.Equal(value,store.Load());store.Save(null);Assert.Null(store.Load());
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Fact] public void PerGamePlanSurvivesRestartAndOldFilesRemainDisabled()
    {
        var root=Path.Combine(Path.GetTempPath(),"LotusPrefsTest-"+Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(root);var path=Path.Combine(root,"prefs.json");
            File.WriteAllText(path,"{\"Version\":1,\"Games\":{\"Steam:1\":{\"UseProfile\":true}}}");
            var book=new GamePrefsBook(new JsonGamePrefsStore(path),ex=>throw ex);
            Assert.False(book.Get("Steam:1").Presentation.Enabled);
            var plan=Prefs() with {Exe="game.exe",Cap=59.93,AppliedLimiter="fluid",PreparedRate=59.94};
            Assert.True(book.Set("Steam:1",book.Get("Steam:1") with {Presentation=plan}));
            var again=new GamePrefsBook(new JsonGamePrefsStore(path),ex=>throw ex);
            Assert.Equal(plan,again.Get("Steam:1").Presentation);Assert.True(again.Get("Steam:1").UseProfile);
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
