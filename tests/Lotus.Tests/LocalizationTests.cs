using System.Collections;
using System.Globalization;
using System.Resources;
using Lotus.Core;
using Xunit;

namespace Lotus.Tests;

public sealed class LocalizationTests
{
    private sealed class Events : IAppEvents
    {
        public int Count { get; private set; }
        public void Navigated(string sectionId) => Count++;
    }
    [Fact] public void LanguageChangeKeepsCurrentRouteAndDoesNotCreateNavigationEvents()
    {
        var text = new TextCatalog(); var events = new Events(); var shell = new ShellViewModel(events, text);
        shell.NavigateCommand.Execute("settings");
        var notifications = new List<string?>(); text.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        text.SetLanguage("en-US"); shell.RefreshLanguage();
        Assert.Equal("settings", shell.SelectedSection.Id); Assert.Equal("Settings", shell.SelectedSection.Title);
        Assert.Equal(1, events.Count); Assert.Contains("Item[]", notifications);
        text.SetLanguage("pt-BR"); shell.RefreshLanguage(); Assert.Equal("Configurações", shell.SelectedSection.Title);
    }
    [Fact] public void BothLanguagesHaveExactlyTheSameResourceKeysAndNoEmptyText()
    {
        var resources = new ResourceManager("Lotus.Core.Localization.Strings", typeof(TextCatalog).Assembly);
        var pt = resources.GetResourceSet(CultureInfo.GetCultureInfo("pt-BR"), true, false)!;
        var en = resources.GetResourceSet(CultureInfo.GetCultureInfo("en-US"), true, false)!;
        Assert.NotNull(pt); Assert.NotNull(en);
        var ptKeys = pt.Cast<DictionaryEntry>().Select(e => (string)e.Key).Order().ToArray();
        var enKeys = en.Cast<DictionaryEntry>().Select(e => (string)e.Key).Order().ToArray();
        Assert.Equal(ptKeys, enKeys);
        foreach (var key in ptKeys)
        {
            Assert.False(string.IsNullOrWhiteSpace(new TextCatalog("pt-BR")[key]));
            Assert.False(string.IsNullOrWhiteSpace(new TextCatalog("en-US")[key]));
        }
    }
    [Theory][InlineData("pt-BR", "Configurações")][InlineData("en-US", "Settings")]
    public void AllRoutesKeepIdentityAndGetLocalizedContent(string language, string settingsTitle)
    {
        var text = new TextCatalog(language); var sections = Sections.Create(text);
        Assert.Equal(Sections.All.Select(s => s.Id), sections.Select(s => s.Id));
        Assert.Equal(settingsTitle, sections.Single(s => s.Id == "settings").Title);
        Assert.All(sections, s => { Assert.False(string.IsNullOrWhiteSpace(s.Description)); Assert.False(string.IsNullOrWhiteSpace(s.NextStep)); });
        Assert.Contains("C:/Lotus", text.Format("LogLocation", "C:/Lotus"));
    }
    [Fact] public void UnknownLanguageUsesPortugueseAndMissingKeysFailVisiblyInTests()
    {
        var text = new TextCatalog("unsupported"); Assert.Equal("pt-BR", text.Language);
        Assert.Throws<MissingManifestResourceException>(() => text["MissingResource"]);
    }
}
