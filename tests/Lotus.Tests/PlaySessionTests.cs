using Lotus.Core;
using Lotus.Core.Games;
using Xunit;

namespace Lotus.Tests;

public sealed class PlaySessionTests
{
    private sealed class FakeProfile(int pending) : IGameProfile
    {
        public int Applied, Undone;
        public Task<IReadOnlyList<ProfileStep>> PreviewAsync() =>
            Task.FromResult<IReadOnlyList<ProfileStep>>([.. Enumerable.Range(0, 3).Select(i => new ProfileStep("S" + i, "a", "b", i >= pending))]);
        public Task<bool> ApplyAsync() { Applied++; return Task.FromResult(true); }
        public Task<bool> UndoAsync() { Undone++; return Task.FromResult(true); }
    }
    private sealed class FakeWatch(TimeSpan? played) : IGameWatch
    {
        public Task<TimeSpan?> WaitForSessionAsync(GameEntry game, TimeSpan startTimeout, CancellationToken token) => Task.FromResult(played);
    }
    private sealed class Library : IGameLibrary { public LibraryScan Scan(IReadOnlyList<GameEntry> manual) => new([new(GameStores.Steam, "1", "Game", LaunchUri: "steam://rungameid/1")], []); }
    private sealed class NoStore : IManualGameStore { public IReadOnlyList<GameEntry> Load() => []; public void Save(IReadOnlyList<GameEntry> g) { } }
    private sealed class Launcher : IGameLauncher { public int Launched; public void Launch(GameEntry game) => Launched++; }
    private sealed class Processes(params bool[] states) : IGameProcessCheck
    {
        private int i; public bool IsRunning(GameEntry game) => states[Math.Min(i++, states.Length - 1)];
    }

    private static async Task<(GamesViewModel Vm, Launcher Launcher)> Vm(IGameProfile profile, IGameWatch watch)
    {
        var launcher = new Launcher();
        var vm = new GamesViewModel(new Library(), new NoStore(), launcher, new TextCatalog(), ex => throw ex) { GameProfile = profile, Watch = watch };
        await vm.EnsureLoadedAsync();
        return (vm, launcher);
    }

    [Fact] public async Task AppliesProfileLaunchesFollowsAndAsksAboutUndo()
    {
        var profile = new FakeProfile(pending: 2); var (vm, launcher) = await Vm(profile, new FakeWatch(TimeSpan.FromMinutes(42)));
        PlaySessionEnded? ended = null; vm.SessionEnded += (_, e) => ended = e;
        var plan = (await vm.PlanPlayAsync(vm.Games[0]))!;
        Assert.Equal(2, plan.Pending); Assert.Equal(0, profile.Applied); // planning changes nothing
        await vm.PlayAsync(plan, applyProfile: true, precompile: false);
        Assert.Equal(1, profile.Applied); Assert.Equal(1, launcher.Launched);
        Assert.True(ended!.ProfileApplied); Assert.Equal(42, ended.Played.TotalMinutes);
        Assert.Equal(vm.Text.Format("PlayEnded", "Game", 42), vm.Status);
        Assert.True(await vm.UndoProfileAsync()); Assert.Equal(1, profile.Undone);
    }

    [Fact] public async Task DecliningTheProfileOnlyLaunchesAndNothingToUndo()
    {
        var profile = new FakeProfile(pending: 3); var (vm, launcher) = await Vm(profile, new FakeWatch(null));
        PlaySessionEnded? ended = null; vm.SessionEnded += (_, e) => ended = e;
        await vm.PlayAsync((await vm.PlanPlayAsync(vm.Games[0]))!, applyProfile: false, precompile: false);
        Assert.Equal(0, profile.Applied); Assert.Equal(1, launcher.Launched); Assert.False(ended!.ProfileApplied);
        Assert.Equal(vm.Text.Format("PlayNotStarted", "Game"), vm.Status);
    }

    [Fact] public async Task WatchWaitsForStartThenForExit()
    {
        var watch = new PollingGameWatch(new Processes(false, true, true, false), TimeSpan.FromMilliseconds(5));
        Assert.NotNull(await watch.WaitForSessionAsync(new(GameStores.Steam, "1", "Game"), TimeSpan.FromSeconds(5), default));
        var never = new PollingGameWatch(new Processes(false), TimeSpan.FromMilliseconds(5));
        Assert.Null(await never.WaitForSessionAsync(new(GameStores.Steam, "1", "Game"), TimeSpan.FromMilliseconds(30), default));
    }
}
