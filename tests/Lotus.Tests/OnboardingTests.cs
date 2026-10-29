using Lotus.Core;
using Lotus.Core.Games;
using Lotus.Core.Profile;
using Lotus.Infrastructure.Profile;
using Xunit;

namespace Lotus.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class OnboardingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "LotusOnboarding-" + Guid.NewGuid());
    public OnboardingTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    private sealed class MemoryStore : IPreferencesStore
    {
        public InterfacePreferences Saved = new();
        public PreferencesLoadResult Load() => new(Saved, "Loaded");
        public bool TrySave(InterfacePreferences p) { Saved = p; return true; }
    }
    private sealed class Source(SteamProfile? steam) : IProfileSource
    {
        public SteamProfile? Steam = steam;
        public SteamProfile? ReadSteam() => Steam; public bool SteamInstalled() => true;
        public string ImportPicture(string sourcePath) => sourcePath + ".copy";
    }

    [Fact] public void WelcomeWithSteamSavesNameGoalAndAccent()
    {
        var store = new MemoryStore(); var prefs = new PreferencesService(store);
        var vm = new OnboardingViewModel(prefs, new Source(new("Joe", @"C:\a.png")), new TextCatalog(), ex => throw ex);
        Assert.True(vm.NeedsWelcome);
        vm.Start(edit: false);
        Assert.True(vm.IsWelcome); Assert.Equal(6, vm.StepCount); Assert.True(vm.UseSteam); Assert.Equal("Joe", vm.PreviewName);
        string? preview = null; vm.AccentPreview += (_, a) => preview = a;
        vm.Next(); vm.Next(); vm.Goal = "competitive"; vm.Next(); vm.Accent = "dark"; Assert.Equal("dark", preview);
        Assert.Single(vm.Accents, a => a.Selected && a.Id == "dark"); Assert.Single(vm.Goals, g => g.Selected && g.Id == "competitive");
        vm.Next(); Assert.True(vm.IsApps); vm.Next(); Assert.True(vm.IsTour && vm.IsLast); vm.Next();
        var s = store.Saved;
        Assert.True(s.Onboarded); Assert.Equal(("steam", "Joe", @"C:\a.png", "competitive", "dark"), (s.ProfileSource, s.DisplayName, s.AvatarPath, s.Goal, s.Accent));
        Assert.False(new OnboardingViewModel(prefs, new Source(null), new TextCatalog(), _ => { }).NeedsWelcome);
    }

    [Fact] public void WithoutSteamTheOwnProfileIsUsedAndSteamAppearsLater()
    {
        var store = new MemoryStore(); var prefs = new PreferencesService(store); var source = new Source(null);
        var vm = new OnboardingViewModel(prefs, source, new TextCatalog(), ex => throw ex);
        vm.Start(edit: true);
        Assert.True(vm.IsProfile); Assert.Equal(3, vm.StepCount); Assert.False(vm.SteamFound); Assert.True(vm.UseCustom);
        vm.CustomName = "  Ana  "; vm.ChoosePicture(@"C:\me.jpg");
        Assert.Equal("Ana", vm.PreviewName); Assert.Equal(@"C:\me.jpg.copy", vm.PreviewPicture);
        Assert.False(vm.RecheckSteam());
        source.Steam = new("Ana Steam", null);
        Assert.True(vm.RecheckSteam()); Assert.True(vm.UseSteam); Assert.Equal("Ana Steam", vm.PreviewName); // signed in meanwhile
        vm.UseCustom = true; vm.Next(); vm.Next(); vm.Next(); // profile → goal → accent → save
        Assert.Equal(("custom", "Ana"), (store.Saved.ProfileSource, store.Saved.DisplayName));
    }

    [Fact] public void SkipKeepsDefaultsAndInvalidValuesAreNormalized()
    {
        var store = new MemoryStore(); var prefs = new PreferencesService(store);
        var vm = new OnboardingViewModel(prefs, new Source(null), new TextCatalog(), ex => throw ex);
        vm.Start(edit: false); vm.Skip();
        Assert.True(store.Saved.Onboarded); Assert.Equal("balanced", store.Saved.Goal); Assert.Equal("rose", store.Saved.Accent);
        var odd = new InterfacePreferences { Goal = "turbo", Accent = "neon", DisplayName = new string('x', 50) }.Normalize();
        Assert.Equal(("balanced", "rose", 32), (odd.Goal, odd.Accent, odd.DisplayName.Length));
    }

    [Fact] public void GoalChangesTheRecommendation()
    {
        var pc = new SystemProfile("NVIDIA GeForce RTX 5060", 8192, 12, 16_000, 1920, 1080, 75);
        var balanced = SettingsAdvisor.Recommend(pc); var fps = SettingsAdvisor.Recommend(pc, "fps"); var comp = SettingsAdvisor.Recommend(pc, "competitive");
        Assert.True(fps.Tier < balanced.Tier || balanced.Tier == QualityTier.Low); Assert.True(comp.Tier <= fps.Tier);
        Assert.Equal(72, balanced.FpsCap); Assert.Equal(72, fps.FpsCap); Assert.Equal(72, comp.FpsCap); // 3 below the 75 Hz monitor
        Assert.Contains("GsReasonGoalCompetitive", comp.ReasonKeys);
    }

    [Fact] public void ReadsSteamAccountAndAvatarFromLocalFiles()
    {
        var steam = Path.Combine(root, "Steam"); Directory.CreateDirectory(Path.Combine(steam, "config", "avatarcache"));
        File.WriteAllText(Path.Combine(steam, "config", "loginusers.vdf"),
            "\"users\"\n{\n \"76561198000000001\" { \"PersonaName\" \"Old\" \"MostRecent\" \"0\" \"Timestamp\" \"1\" }\n \"76561198000000002\" { \"PersonaName\" \"Joe\" \"MostRecent\" \"1\" \"Timestamp\" \"2\" }\n}");
        File.WriteAllBytes(Path.Combine(steam, "config", "avatarcache", "76561198000000002.png"), [1]);
        var source = new LocalProfileSource(steam, Path.Combine(root, "Lotus"));
        var p = source.ReadSteam()!;
        Assert.Equal("Joe", p.Name); Assert.EndsWith("76561198000000002.png", p.AvatarPath);
        var pic = Path.Combine(root, "me.png"); File.WriteAllBytes(pic, [2]);
        Assert.True(File.Exists(source.ImportPicture(pic)));
        Assert.Throws<InvalidOperationException>(() => source.ImportPicture(Path.Combine(root, "evil.exe")));
        Assert.Null(new LocalProfileSource(Path.Combine(root, "none")).ReadSteam());
    }
}

public sealed class BackgroundPreferencesTests
{
    [Fact] public void DefaultsKeepLotusInTheTrayWithSoundsAndNoAutostart()
    {
        var p = new Lotus.Core.InterfacePreferences();
        Assert.True(p.CloseToTray); Assert.True(p.UiSounds); Assert.False(p.StartWithWindows);
    }
}

public sealed class ReportBuilderTests
{
    [Fact] public void HtmlEscapesAndSkipsEmptySectionsAndNameIsSafe()
    {
        var html = Lotus.Core.Reports.ReportBuilder.Html("<Meu PC>", "sub", [new("A", [new("x", "<b>")]), new("Vazia", [])], "rodapé");
        Assert.Contains("&lt;Meu PC&gt;", html); Assert.Contains("&lt;b&gt;", html); Assert.DoesNotContain("Vazia", html);
        Assert.Equal("Meu PC 2026-10-25 1852.html", Lotus.Core.Reports.ReportBuilder.FileName("Meu: PC?", new DateTime(2026, 10, 25, 18, 52, 0)));
    }
}

public sealed class LogDigestTests
{
    [Fact] public void ReadsOnlyRecentWarningsAndErrorsWithValuesFilledIn()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LotusLog-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllLines(Path.Combine(dir, "lotus-20261025.jsonl"), [
                """{"Timestamp":"2026-10-25T10:00:00-03:00","Level":"Information","MessageTemplate":"ok"}""",
                """{"Timestamp":"2026-10-25T11:00:00-03:00","Level":"Warning","MessageTemplate":"DNS failed {Server}","Properties":{"Server":"1.1.1.1"}}""",
                """{"Timestamp":"2026-10-25T12:00:00-03:00","Level":"Error","MessageTemplate":"Report failed","Exception":"System.IO.IOException: disk full\r\n   at X"}""",
                """{"Timestamp":"2026-10-01T12:00:00-03:00","Level":"Error","MessageTemplate":"old"}""",
                "not json"]);
            var e = Lotus.Core.Reports.LogDigest.Read(dir, new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.FromHours(-3)));
            Assert.Equal(2, e.Count);
            Assert.Equal(("Error", "Report failed", "System.IO.IOException: disk full"), (e[0].Level, e[0].Message, e[0].Detail));
            Assert.Equal("DNS failed 1.1.1.1", e[1].Message);
        }
        finally { Directory.Delete(dir, true); }
    }
}

public sealed class RecommendedAppsTests
{
    private sealed class FakeInstaller : Lotus.Core.Profile.IAppInstaller
    {
        public HashSet<string> Present = ["rtss"]; public bool Winget = true; public List<string> Installed = [];
        public bool IsInstalled(Lotus.Core.Profile.RecommendedApp app) => Present.Contains(app.Id);
        public bool WingetAvailable() => Winget;
        public Task<bool> InstallAsync(Lotus.Core.Profile.RecommendedApp app, CancellationToken token) { Installed.Add(app.WingetId!); Present.Add(app.Id); return Task.FromResult(true); }
    }

    [Fact] public async Task DetectsInstalledAndInstallsOnlyWingetPackages()
    {
        var fake = new FakeInstaller(); var vm = new Lotus.Core.Profile.RecommendedAppsViewModel(fake, new Lotus.Core.TextCatalog(), ex => throw ex);
        vm.Refresh();
        var rtss = vm.Rows.Single(r => r.App.Id == "rtss"); var ab = vm.Rows.Single(r => r.App.Id == "afterburner"); var scs = vm.Rows.Single(r => r.App.Id == "scskiller");
        Assert.True(rtss.Installed); Assert.False(rtss.CanInstall); Assert.True(ab.CanInstall);
        Assert.False(vm.InstallsDirectly(scs)); // no winget package: official page instead
        await vm.InstallAsync(scs); Assert.Empty(fake.Installed);
        await vm.InstallAsync(ab); Assert.Equal(["Guru3D.Afterburner"], fake.Installed); Assert.True(ab.Installed); Assert.False(ab.Busy);
        await vm.InstallAsync(rtss); Assert.Single(fake.Installed); // already there
        fake.Winget = false; vm.Refresh(); Assert.False(vm.InstallsDirectly(vm.Rows.Single(r => r.App.Id == "discord")));
    }
}
