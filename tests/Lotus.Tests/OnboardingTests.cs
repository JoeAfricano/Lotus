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
        Assert.True(vm.IsWelcome); Assert.Equal(5, vm.StepCount); Assert.True(vm.UseSteam); Assert.Equal("Joe", vm.PreviewName);
        string? preview = null; vm.AccentPreview += (_, a) => preview = a;
        vm.Next(); vm.Next(); vm.Goal = "competitive"; vm.Next(); vm.Accent = "dark"; Assert.Equal("dark", preview);
        Assert.Single(vm.Accents, a => a.Selected && a.Id == "dark"); Assert.Single(vm.Goals, g => g.Selected && g.Id == "competitive");
        vm.Next(); Assert.True(vm.IsTour && vm.IsLast); vm.Next();
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
        Assert.Equal(75, balanced.FpsCap); Assert.Equal(150, fps.FpsCap); Assert.Equal(150, comp.FpsCap);
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
