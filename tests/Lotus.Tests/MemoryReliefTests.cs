using Lotus.Core;
using Lotus.Core.Games;
using Lotus.Core.Maintenance;
using Xunit;

namespace Lotus.Tests;

public sealed class MemoryReliefTests
{
    private static readonly RunningGame Game = new(100, "game.exe", DateTimeOffset.UtcNow);
    private const int Self = 50, Foreground = 100;

    private sealed class FakeSystem : IMemoryReliefSystem
    {
        public List<ReliefProcess> List = [];
        public long Available = 1L << 30, AfterAvailable = 3L << 30;
        public int Foreground = MemoryReliefTests.Foreground;
        public List<string> Log = [];
        public HashSet<string> Running = new(StringComparer.OrdinalIgnoreCase);
        private bool measured;
        public long AvailableBytes() { var v = measured ? AfterAvailable : Available; measured = true; return v; }
        public IReadOnlyList<ReliefProcess> Processes() => List;
        public int ForegroundPid() => Foreground;
        public bool CloseGracefully(ReliefProcess p) { Log.Add("close " + p.ExeName); return true; }
        public long Trim(ReliefProcess p) { Log.Add("trim " + p.ExeName); return 100L << 20; }
        public bool PauseWallpaper(string exe) { Log.Add("pause " + exe); return true; }
        public bool ResumeWallpaper(string exe) { Log.Add("play " + exe); return true; }
        public bool IsRunning(string exeName) => Running.Contains(exeName);
        public bool Reopen(string exe) { Log.Add("open " + exe); return true; }
        public void Settle(TimeSpan time) { }
    }

    private static ReliefProcess P(int pid, string exe, ProcessKind kind = ProcessKind.Normal, bool window = true, string? path = null) =>
        new(pid, exe, path ?? $@"C:\Apps\{exe}.exe", kind, window);

    [Fact]
    public void Trims_only_the_players_idle_programs_and_the_assistant()
    {
        Assert.True(MemoryRelief.CanTrim(P(10, "chrome"), Game.Pid, Foreground, Self));
        Assert.True(MemoryRelief.CanTrim(P(11, "claude", ProcessKind.Lotus), Game.Pid, Foreground, Self));
        Assert.False(MemoryRelief.CanTrim(P(100, "game"), Game.Pid, 7, Self));             // the game
        Assert.False(MemoryRelief.CanTrim(P(12, "notepad"), Game.Pid, 12, Self));          // foreground
        Assert.False(MemoryRelief.CanTrim(P(Self, "Lotus.App", ProcessKind.Lotus), Game.Pid, Foreground, Self));
        Assert.False(MemoryRelief.CanTrim(P(13, "RTSS", ProcessKind.Lotus), Game.Pid, Foreground, Self));
        Assert.False(MemoryRelief.CanTrim(P(14, "svchost", ProcessKind.System), Game.Pid, Foreground, Self));
        Assert.False(MemoryRelief.CanTrim(P(15, "EasyAntiCheat", ProcessKind.AntiCheat), Game.Pid, Foreground, Self));
        Assert.False(MemoryRelief.CanTrim(P(16, "other", ProcessKind.Game), Game.Pid, Foreground, Self));
        Assert.False(MemoryRelief.CanTrim(P(17, "steamwebhelper"), Game.Pid, Foreground, Self)); // the game's launcher and overlay
        Assert.False(MemoryRelief.CanTrim(P(18, "Discord"), Game.Pid, Foreground, Self));        // voice calls
    }

    [Fact]
    public void Never_shrink_list_is_never_trimmed_closed_or_paused()
    {
        var sys = new FakeSystem { List = [P(10, "chrome"), P(20, "spotify"), P(21, "vlc"), P(30, "wallpaper64", path: @"C:\WE\wallpaper64.exe")] };
        var step = new MemoryReliefStep(sys, () => ["chrome"], null, Self, keepList: () => ["Chrome", "spotify", "wallpaper64"]);
        step.Apply(Game);
        Assert.Equal(["trim vlc"], sys.Log);
    }

    [Theory]
    [InlineData(16253, true)]   // 16 GB as Windows reports it (José's PC)
    [InlineData(8192, true)]
    [InlineData(16384, true)]
    [InlineData(32700, false)]  // 32 GB: no RAM tip
    [InlineData(0, false)]      // unknown: nothing invented
    public void Ram_tip_is_for_16_GB_or_less(long ramMb, bool tip) =>
        Assert.Equal(tip, SettingsAdvisor.LowRam(new SystemProfile("RTX 5060", 8192, 12, ramMb, 1920, 1080, 75)));

    [Fact]
    public void Game_tips_mention_memory_only_on_16_GB_or_less_and_say_if_it_is_on()
    {
        var tech = new GameTech(false, false, false, false, "Unreal Engine");
        SettingsRecommendation Rec(int? ramGb) => new(QualityTier.High, QualityTier.High, Upscaler.None, UpscaleMode.Off, 60, 60, 0, [], Game: new GameFpsCurve([60, 60, 60, 60], [60, 60, 60, 60], null, "requirements", RamGb: ramGb));
        SystemProfile Pc(long mb) => new("RTX 5060", 8192, 12, mb, 1920, 1080, 75);

        Assert.Contains(InGameAdvisor.Advise(tech, Rec(16), Pc(16253), memoryRelief: true), t => t.Key == "IgRamOn");
        Assert.Contains(InGameAdvisor.Advise(tech, Rec(16), Pc(16253)), t => t.Key == "IgRamOff");
        Assert.DoesNotContain(InGameAdvisor.Advise(tech, Rec(16), Pc(32700)), t => t.Key.StartsWith("IgRam"));
        var heavy = InGameAdvisor.Advise(tech, Rec(24), Pc(16253)).Single(t => t.Key == "IgRamOff");
        var light = InGameAdvisor.Advise(tech, Rec(8), Pc(16253)).Single(t => t.Key == "IgRamOff");
        Assert.True(heavy.Weight > light.Weight); // the game asks for more RAM than the PC has: higher in the list
    }

    [Fact]
    public void Small_programs_are_left_alone()
    {
        Assert.False(MemoryRelief.CanTrim(P(10, "notepad") with { Bytes = 20L << 20 }, Game.Pid, Foreground, Self));
        Assert.True(MemoryRelief.CanTrim(P(10, "chrome") with { Bytes = MemoryRelief.TrimFrom }, Game.Pid, Foreground, Self));
    }

    [Fact]
    public void Closes_only_listed_normal_programs_with_a_window_never_the_assistant()
    {
        string[] list = ["chrome", "claude", "wallpaper64"];
        Assert.True(MemoryRelief.CanClose(P(10, "chrome"), list, Game.Pid));
        Assert.False(MemoryRelief.CanClose(P(10, "chrome", window: false), list, Game.Pid));
        Assert.False(MemoryRelief.CanClose(P(11, "claude", ProcessKind.Lotus), list, Game.Pid));
        Assert.False(MemoryRelief.CanClose(P(12, "spotify"), list, Game.Pid));          // not in the list
        Assert.False(MemoryRelief.CanClose(P(13, "wallpaper64"), list, Game.Pid));      // paused, never closed
    }

    [Fact]
    public void Apply_closes_pauses_trims_and_reports_the_measured_difference()
    {
        var sys = new FakeSystem
        {
            List = [P(10, "chrome"), P(11, "chrome", window: false), P(20, "spotify"), P(30, "wallpaper64", path: @"C:\WE\wallpaper64.exe"), P(100, "game"), P(40, "svchost", ProcessKind.System)],
        };
        MemoryReliefReport? report = null;
        var step = new MemoryReliefStep(sys, () => ["chrome"], r => report = r, Self);

        var saved = step.Apply(Game);

        Assert.NotNull(saved);
        Assert.Contains("close chrome", sys.Log);
        Assert.Contains(@"pause C:\WE\wallpaper64.exe", sys.Log);
        Assert.Contains("trim spotify", sys.Log);
        Assert.DoesNotContain("trim chrome", sys.Log);   // closing, not trimming
        Assert.DoesNotContain("trim game", sys.Log);
        Assert.DoesNotContain("trim svchost", sys.Log);
        Assert.NotNull(report);
        Assert.Equal(2L << 30, report!.FreedBytes);       // 3 GB after - 1 GB before, measured
        Assert.True(report.WallpaperPaused);
        Assert.Equal([@"C:\Apps\chrome.exe"], report.Closed);
    }

    [Fact]
    public void Freed_memory_is_never_negative()
    {
        var sys = new FakeSystem { Available = 3L << 30, AfterAvailable = 2L << 30, List = [P(20, "spotify")] };
        MemoryReliefReport? report = null;
        new MemoryReliefStep(sys, () => [], r => report = r, Self).Apply(Game);
        Assert.Equal(0, report!.FreedBytes);
    }

    [Fact]
    public void Revert_plays_wallpaper_and_reopens_closed_programs_not_already_open()
    {
        var sys = new FakeSystem { List = [P(10, "chrome"), P(12, "notepad"), P(30, "wallpaper64", path: @"C:\WE\wallpaper64.exe")] };
        var step = new MemoryReliefStep(sys, () => ["chrome", "notepad"], null, Self);
        var saved = step.Apply(Game)!;
        sys.Log.Clear(); sys.Running.Add("notepad"); // the player opened it again during the game

        Assert.True(step.Revert(saved));
        Assert.Contains(@"play C:\WE\wallpaper64.exe", sys.Log);
        Assert.Contains(@"open C:\Apps\chrome.exe", sys.Log);
        Assert.DoesNotContain(@"open C:\Apps\notepad.exe", sys.Log);
    }

    [Fact]
    public void Only_trimming_still_records_the_step_and_reverts_nothing()
    {
        var sys = new FakeSystem { List = [P(20, "spotify")] };
        var step = new MemoryReliefStep(sys, () => [], null, Self);
        var saved = step.Apply(Game)!;
        sys.Log.Clear();
        Assert.False(step.Revert(saved));
        Assert.Empty(sys.Log);
    }

    private sealed class MemoryJournal : IBoostJournal
    {
        public List<BoostRecord> Stored = [];
        public IReadOnlyList<BoostRecord> Load() => [.. Stored];
        public void Save(IReadOnlyList<BoostRecord> records) => Stored = [.. records];
        public void Clear() => Stored = [];
    }

    [Fact]
    public void Once_per_session_saved_before_the_game_and_recovered_after_a_crash()
    {
        var sys = new FakeSystem { List = [P(10, "chrome"), P(30, "wallpaper64", path: @"C:\WE\wallpaper64.exe")] };
        var journal = new MemoryJournal();
        var boost = new GameBoost([new MemoryReliefStep(sys, () => ["chrome"], null, Self)], journal);

        Assert.Equal([BoostSteps.Memory], boost.Start(Game, new BoostOptions()).Applied);
        Assert.Single(journal.Stored);                                 // on disk before anything else
        Assert.Empty(boost.Start(Game, new BoostOptions()).Applied);   // already done for this session
        Assert.Single(sys.Log, l => l == "close chrome");

        // Lotus closed by force: the next start reads the record and puts things back.
        sys.Log.Clear();
        var next = new GameBoost([new MemoryReliefStep(sys, () => ["chrome"], null, Self)], journal);
        Assert.Equal([BoostSteps.Memory], next.Recover());
        Assert.Contains(@"open C:\Apps\chrome.exe", sys.Log);
        Assert.Contains(@"play C:\WE\wallpaper64.exe", sys.Log);
    }

    [Fact]
    public void Switch_off_means_nothing_happens()
    {
        var sys = new FakeSystem { List = [P(10, "chrome"), P(20, "spotify")] };
        var boost = new GameBoost([new MemoryReliefStep(sys, () => ["chrome"], null, Self)], new MemoryJournal());
        Assert.Empty(boost.Start(Game, new BoostOptions(Memory: false)).Applied);
        Assert.Empty(sys.Log);
        Assert.False(new BoostOptions(Memory: false).Wants(BoostSteps.Memory));
        Assert.True(new InterfacePreferences().BoostMemory); // on by default
    }
}
