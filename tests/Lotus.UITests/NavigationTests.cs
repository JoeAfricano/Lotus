using FlaUI.Core.Definitions;
using Xunit;

namespace Lotus.UITests;

/// <summary>
/// Walks the side menu like a player: every section opens, shows content, keeps Lotus running and
/// writes no screen error to the log. Each section leaves a proof print in artifacts\ui-tests\&lt;date&gt;.
/// Nothing here presses an action button: no optimization, cleanup or restore is ever started.
/// </summary>
[Collection(LotusAppCollection.Name)]
public sealed class NavigationTests(LotusAppFixture lotus)
{
    // Same order as the side menu (src/Lotus.Core/Navigation.cs).
    private static readonly string[] Ids = ["home", "optimize", "games", "lotusscore", "tools", "gamelist", "saves", "diary", "settings"];
    public static TheoryData<string> Sections => new(Ids);

    [UiFact]
    public void Menu_lists_every_section()
    {
        foreach (var id in Ids)
        {
            var item = lotus.Window.FindFirstDescendant(cf => cf.ByAutomationId("nav-" + id));
            Assert.True(item is not null, $"Menu item nav-{id} is missing.");
            Assert.False(string.IsNullOrWhiteSpace(item.Name), $"Menu item nav-{id} has no readable name.");
        }
    }

    [UiTheory]
    [MemberData(nameof(Sections))]
    public void Section_opens_and_shows_content(string id)
    {
        lotus.OpenSection(id);
        Thread.Sleep(2500); // let the page load its cards and finish the entrance animation
        lotus.Screenshot($"{Array.IndexOf(Ids, id) + 1:00}-{id}");

        Assert.True(lotus.IsRunning, $"Lotus closed after opening {id}.");
        Assert.True(lotus.Window.IsAvailable, $"Lotus window vanished after opening {id}.");
        var texts = lotus.Window.FindAllDescendants(cf => cf.ByControlType(ControlType.Text)).Count(t => !string.IsNullOrWhiteSpace(t.Properties.Name.ValueOrDefault));
        Assert.True(texts >= 10, $"Section {id} shows almost nothing ({texts} texts on screen).");
        var errors = lotus.ScreenErrorsInLog();
        Assert.True(errors.Count == 0, $"Lotus logged a screen error around {id}:\n{string.Join("\n", errors)}");
    }
}
