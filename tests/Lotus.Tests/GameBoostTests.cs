using Lotus.Core;
using Lotus.Core.Games;
using Lotus.Core.Optimization;
using Xunit;

namespace Lotus.Tests;

public sealed class GameBoostTests
{
    private static readonly RunningGame Game = new(1234, "game.exe", DateTimeOffset.UtcNow);

    private sealed class MemoryJournal : IBoostJournal
    {
        public List<BoostRecord> Stored = [];
        public IReadOnlyList<BoostRecord> Load() => [.. Stored];
        public void Save(IReadOnlyList<BoostRecord> records) => Stored = [.. records];
        public void Clear() => Stored = [];
    }

    private sealed class ValueStep(string id, int during) : IBoostStep
    {
        public int Value = 1;
        public bool Throws;
        public List<string> Log = [];
        public string Id => id;
        public string? Apply(RunningGame game)
        {
            if (Throws) throw new InvalidOperationException("no access");
            if (Value == during) return null;
            var old = Value; Value = during; Log.Add("apply " + id);
            return old.ToString();
        }
        public bool Revert(string saved)
        {
            if (Value != during) return false;
            Value = int.Parse(saved); Log.Add("revert " + id);
            return true;
        }
    }

    private sealed class Power(Guid active, params Guid[] available) : IPowerSchemeService
    {
        public Guid Current = active;
        public IReadOnlyList<PowerScheme> List() => [.. available.Select(g => new PowerScheme(g, g.ToString()))];
        public Guid Active() => Current;
        public bool? OnAcPower() => true;
        public void SetActive(Guid id) => Current = id;
    }

    [Fact]
    public void Start_applies_wanted_steps_and_stop_puts_them_back_in_reverse()
    {
        var order = new List<string>();
        var a = new ValueStep(BoostSteps.Notifications, 0) { Log = order };
        var b = new ValueStep(BoostSteps.BackgroundRecording, 0) { Log = order };
        var journal = new MemoryJournal();
        var boost = new GameBoost([a, b], journal);

        var result = boost.Start(Game, new BoostOptions());
        Assert.Equal([BoostSteps.Notifications, BoostSteps.BackgroundRecording], result.Applied);
        Assert.Equal(2, journal.Stored.Count);
        Assert.True(boost.IsActive);

        var back = boost.Stop();
        Assert.Equal([BoostSteps.BackgroundRecording, BoostSteps.Notifications], back);
        Assert.Equal(1, a.Value); Assert.Equal(1, b.Value);
        Assert.Empty(journal.Stored);
        Assert.False(boost.IsActive);
    }

    [Fact]
    public void Steps_turned_off_are_left_alone()
    {
        var notes = new ValueStep(BoostSteps.Notifications, 0);
        var boost = new GameBoost([notes], new MemoryJournal());
        boost.Start(Game, new BoostOptions(Notifications: false));
        Assert.Equal(1, notes.Value);
        Assert.False(boost.IsActive);
    }

    [Fact]
    public void A_failing_step_does_not_stop_the_others()
    {
        var bad = new ValueStep(BoostSteps.Notifications, 0) { Throws = true };
        var good = new ValueStep(BoostSteps.BackgroundRecording, 0);
        var result = new GameBoost([bad, good], new MemoryJournal()).Start(Game, new BoostOptions());
        Assert.Equal([BoostSteps.Notifications], result.Failed);
        Assert.Equal([BoostSteps.BackgroundRecording], result.Applied);
        Assert.Equal(0, good.Value);
    }

    [Fact]
    public void A_value_the_player_changed_meanwhile_is_not_overwritten()
    {
        var notes = new ValueStep(BoostSteps.Notifications, 0);
        var boost = new GameBoost([notes], new MemoryJournal());
        boost.Start(Game, new BoostOptions());
        notes.Value = 5; // the player turned it to something else during the game
        Assert.Empty(boost.Stop());
        Assert.Equal(5, notes.Value);
    }

    [Fact]
    public void Recover_undoes_a_session_left_by_a_crash()
    {
        var journal = new MemoryJournal();
        var first = new ValueStep(BoostSteps.Notifications, 0);
        new GameBoost([first], journal).Start(Game, new BoostOptions()); // Lotus "crashes" here: no Stop
        Assert.Single(journal.Stored);

        var after = new ValueStep(BoostSteps.Notifications, 0) { Value = 0 }; // Windows still as Lotus left it
        var undone = new GameBoost([after], journal).Recover();
        Assert.Equal([BoostSteps.Notifications], undone);
        Assert.Equal(1, after.Value);
        Assert.Empty(journal.Stored);
    }

    [Fact]
    public void Power_step_switches_to_high_performance_and_back()
    {
        var power = new Power(ProfileSuggestions.Balanced, ProfileSuggestions.Balanced, ProfileSuggestions.HighPerformance);
        var step = new PowerBoostStep(power);
        var saved = step.Apply(Game);
        Assert.Equal(ProfileSuggestions.HighPerformance, power.Current);
        Assert.True(step.Revert(saved!));
        Assert.Equal(ProfileSuggestions.Balanced, power.Current);
    }

    [Fact]
    public void Power_step_does_nothing_when_already_high_performance_or_missing()
    {
        var already = new Power(ProfileSuggestions.HighPerformance, ProfileSuggestions.HighPerformance);
        Assert.Null(new PowerBoostStep(already).Apply(Game));
        var missing = new Power(ProfileSuggestions.Balanced, ProfileSuggestions.Balanced);
        Assert.Null(new PowerBoostStep(missing).Apply(Game));
        Assert.Equal(ProfileSuggestions.Balanced, missing.Current);
    }

    [Fact]
    public void Preferences_build_the_options_without_closing_apps()
    {
        var p = new InterfacePreferences { BoostNotifications = false };
        Assert.Equal(new BoostOptions(Power: true, Notifications: false, BackgroundRecording: true), p.BoostOptions);
        Assert.DoesNotContain("close-apps", BoostSteps.All); // apps are never closed for a game
        Assert.False(new InterfacePreferences().GameBoost); // off until the Game profile is applied
    }
}
