using Lotus.Core;
using Lotus.Infrastructure;
using Serilog;
using Serilog.Formatting.Json;
using Xunit;

namespace Lotus.Tests;

public sealed class NavigationTests
{
    private sealed class Events : IAppEvents
    {
        public List<string> Visited { get; } = [];
        public void Navigated(string id) => Visited.Add(id);
    }
    [Fact] public void StartsOnHomeWithoutAnArtificialNavigationEvent()
    {
        var events = new Events(); var vm = new ShellViewModel(events);
        Assert.Equal("home", vm.SelectedSection.Id); Assert.Empty(events.Visited);
    }
    [Fact] public void AllFiveSectionsAreReachableAndNotifyBindings()
    {
        var events = new Events(); var vm = new ShellViewModel(events); var changes = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.SelectedSection)) changes++; };
        foreach (var section in vm.Items.Skip(1).Append(vm.Items[0]))
        { vm.NavigateCommand.Execute(section.Id); Assert.Same(section, vm.SelectedSection); }
        Assert.Equal(5, changes); Assert.Equal(5, events.Visited.Count);
        Assert.Equal(5, vm.Items.Select(s => s.Id).Distinct().Count());
    }
    [Theory][InlineData(null)][InlineData("unknown")][InlineData("dashboard")]
    public void InvalidOrRepeatedNavigationDoesNotChangeState(string? id)
    {
        var events = new Events(); var vm = new ShellViewModel(events);
        vm.NavigateCommand.Execute(id); Assert.Equal("home", vm.SelectedSection.Id); Assert.Empty(events.Visited);
    }
    [Fact] public void NavigationProducesStructuredJsonOnDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lotus-test-{Guid.NewGuid():N}.jsonl");
        try
        {
            using (var logger = new LoggerConfiguration().WriteTo.File(new JsonFormatter(), path).CreateLogger())
                new ShellViewModel(new AppEvents(logger)).NavigateCommand.Execute("analysis");
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal("analysis", json.RootElement.GetProperty("Properties").GetProperty("SectionId").GetString());
        }
        finally { File.Delete(path); }
    }
}
