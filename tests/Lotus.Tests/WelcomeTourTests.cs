using Lotus.Core;
using Lotus.Core.Welcome;
using Xunit;

namespace Lotus.Tests;

public sealed class WelcomeTourTests
{
    private static WelcomeOpening Decide(bool onboarded = true, string? seen = "1.9.0", string current = "1.9.0", bool minimized = false, bool dev = false, bool tour = false, bool news = false)
        => WelcomePlan.Decide(onboarded, seen, current, minimized, dev, tour, news);

    [Fact] public void FirstRunShowsTheTour() => Assert.Equal(WelcomeOpening.Tour, Decide(onboarded: false, seen: null));
    [Fact] public void AfterAnUpdateShowsWhatsNew()
    {
        Assert.Equal(WelcomeOpening.WhatsNew, Decide(seen: "1.8.1"));
        Assert.Equal(WelcomeOpening.WhatsNew, Decide(seen: null)); // updating from a version without the guide
    }
    [Fact] public void SameVersionShowsNothing() => Assert.Equal(WelcomeOpening.None, Decide(seen: "1.9.0"));
    [Fact] public void TrayStartAndTestInstanceShowNothingUnlessAsked()
    {
        Assert.Equal(WelcomeOpening.None, Decide(seen: "1.8.1", minimized: true));
        Assert.Equal(WelcomeOpening.None, Decide(onboarded: false, dev: true));
        Assert.Equal(WelcomeOpening.Tour, Decide(dev: true, tour: true));
        Assert.Equal(WelcomeOpening.WhatsNew, Decide(dev: true, news: true));
    }

    private const string Changelog = """
        # Histórico de versões

        Texto de abertura.

        ## 1.9.0-alpha "Lotus" (19/11/2026)

        ### Novo
        - **Ajustes gráficos por jogo.** Em Meus Jogos.
        - Base com os 1000 jogos
          que continua na linha seguinte.

        ### Melhorado
        - O cartão de upscaling diz o motivo.

        ## 1.8.1-alpha "Lotus" (18/11/2026)

        ### Corrigido
        - Antigo.
        """;

    [Fact] public void ReadsOnlyTheCurrentVersionFromTheChangelog()
    {
        var notes = WhatsNewReader.Read(Changelog, "1.9.0")!;
        Assert.Equal(("1.9.0-alpha", "Lotus", "19/11/2026"), (notes.Version, notes.Name, notes.Date));
        Assert.Equal(["Novo", "Melhorado"], notes.Groups.Select(g => g.Title));
        Assert.Equal("Base com os 1000 jogos que continua na linha seguinte.", notes.Groups[0].Items[1]);
        Assert.DoesNotContain(notes.Groups, g => g.Items.Any(i => i.Contains("Antigo")));
        Assert.Equal("1.8.1-alpha", WhatsNewReader.Read(Changelog, "1.8.1-alpha")!.Version);
    }

    [Fact] public void UnknownVersionOrEmptyChangelogHasNoNotes()
    {
        Assert.Null(WhatsNewReader.Read(Changelog, "2.0.0"));
        Assert.Null(WhatsNewReader.Read("", "1.9.0"));
    }

    [Fact] public void ShippedChangelogHasNotesForThisVersion()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "CHANGELOG.md"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        var props = File.ReadAllText(Path.Combine(root!, "Directory.Build.props"));
        var version = System.Text.RegularExpressions.Regex.Match(props, "<Version>([^<]+)</Version>").Groups[1].Value;
        Assert.NotNull(WhatsNewReader.Read(File.ReadAllText(Path.Combine(root!, "CHANGELOG.md")), version));
        Assert.NotNull(WhatsNewReader.Read(File.ReadAllText(Path.Combine(root!, "CHANGELOG.en-US.md")), version)); // English notes too
    }

    [Fact] public void EnglishPrefersItsOwnNotes()
    {
        Assert.Equal(["CHANGELOG.en-US.md", "CHANGELOG.md"], WhatsNewReader.Files("en-US"));
        Assert.Equal(["CHANGELOG.md"], WhatsNewReader.Files("pt-BR"));
    }

    [Theory, InlineData("pt-BR"), InlineData("en-US")]
    public void EveryPageHasItsTextsAndFeaturePagesOpenAScreen(string language)
    {
        Assert.Equal(8, new WelcomeTourViewModel(new TextCatalog(language), null).Pages.Count);
        var vm = new WelcomeTourViewModel(new TextCatalog(language), null, includeSetup: true);
        Assert.Equal(["welcome", "profile", "theme"], vm.Pages.Take(3).Select(p => p.Id));
        Assert.All(vm.Pages, p => { Assert.False(string.IsNullOrWhiteSpace(p.Title)); Assert.False(string.IsNullOrWhiteSpace(p.Headline)); Assert.False(string.IsNullOrWhiteSpace(p.Points[0])); });
        Assert.All(vm.Pages.Where(p => p.Id is not ("welcome" or "news") && !p.IsSetup), p => Assert.NotNull(AppRoutes.Resolve(p.Route)));
    }

    [Fact] public void OpensOnWhatsNewAfterAnUpdateAndWalksThePages()
    {
        var vm = new WelcomeTourViewModel(new TextCatalog(), null);
        vm.Open(WelcomeOpening.WhatsNew);
        Assert.True(vm.IsWhatsNew && vm.IsLast); Assert.Equal(new TextCatalog()["WtNewsNone"], vm.NotesTitle);
        vm.Open(WelcomeOpening.Tour);
        Assert.Equal("welcome", vm.Current.Id); Assert.False(vm.CanBack);
        vm.Next(); Assert.Equal("optimize", vm.Current.Id);
        vm.Go("saves"); Assert.Equal("saves", vm.Current.Id);
        vm.Back(); Assert.Equal("tools", vm.Current.Id);
    }
}
