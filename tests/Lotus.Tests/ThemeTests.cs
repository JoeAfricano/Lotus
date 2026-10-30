using Lotus.Core;
using Lotus.Core.Themes;
using Xunit;

namespace Lotus.Tests;

public sealed class ThemeTests
{
    private static void AssertReadable(ThemeTokens t, string name)
    {
        var card = t.Card.Mix(t.Light ? ThemeColor.White : ThemeColor.Black, 0); // opaque reference
        Assert.True(t.Text.Contrast(t.Card) >= ThemeEngine.TextContrast, $"{name}: text {t.Text.Contrast(t.Card):0.0}");
        Assert.True(t.Text.Contrast(t.Canvas) >= ThemeEngine.TextContrast, $"{name}: text on canvas");
        Assert.True(t.TextSecondary.Contrast(card) >= ThemeEngine.SecondaryContrast, $"{name}: secondary");
        Assert.True(t.AccentText.Contrast(t.Card) >= ThemeEngine.ColorContrast, $"{name}: accent text");
        Assert.True(t.OnAccent.Contrast(t.AccentFill) >= ThemeEngine.ColorContrast, $"{name}: button text {t.OnAccent.Contrast(t.AccentFill):0.0}");
        Assert.True(t.Detail.Contrast(t.Card) >= ThemeEngine.ColorContrast, $"{name}: detail");
    }

    [Fact] public void ColorMathMatchesWcag()
    {
        Assert.Equal(21, ThemeColor.White.Contrast(ThemeColor.Black), 1);
        Assert.Equal(1, ThemeColor.Parse("#777777").Contrast(ThemeColor.Parse("#777")), 3);
        Assert.Equal("#FF0000", ThemeColor.FromHsl(0, 1, 0.5).Hex);
        var (h, s, l) = ThemeColor.Parse("#E04C84").Hsl; Assert.Equal("#E04C84", ThemeColor.FromHsl(h, s, l).Hex);
        Assert.False(ThemeColor.TryParse("#12345G", out _)); Assert.False(ThemeColor.TryParse(null, out _));
    }

    [Fact] public void EveryGalleryThemeIsReadable()
    {
        Assert.Equal(12, ThemeGallery.All.Count);
        Assert.Equal(12, ThemeGallery.All.Select(p => p.Id).Distinct().Count());
        foreach (var p in ThemeGallery.All)
        {
            var t = ThemeEngine.Derive(p.Spec);
            AssertReadable(t, p.Id);
            Assert.Equal(p.Spec.Light, t.Light);
        }
        // Bright accents keep their color with dark text on buttons instead of turning muddy.
        var cyber = ThemeEngine.Derive(ThemeGallery.Find("cyberpunk")!.Spec);
        Assert.Equal("#FCEE0A", cyber.AccentFill.Hex); Assert.True(cyber.OnAccent.Luminance < 0.05);
    }

    [Fact] public void UnreadableChoicesAreFixedAndReported()
    {
        var bad = new ThemeSpec { Canvas = "#101010", Surface = "#181818", Text = "#303030", Accent = "#200010", Detail = "#222222", Backdrop = new() { Kind = "none" } };
        var t = ThemeEngine.Derive(bad);
        AssertReadable(t, "bad");
        Assert.Contains("text", t.Adjusted); Assert.Contains("accent", t.Adjusted); Assert.True(t.WasAdjusted);
        // Transparent cards over a bright backdrop: text is checked against what it really sits on.
        var glass = new ThemeSpec { Canvas = "#0A0A0A", Surface = "#151515", Text = "#9A9A9A", CardOpacity = 0.78, Backdrop = new() { Kind = "gradient", Colors = ["#FFFFFF", "#F0F0F0"] } };
        var g = ThemeEngine.Derive(glass);
        var effective = g.Card.Mix(ThemeColor.White, 1 - g.CardOpacity);
        Assert.True(g.Text.Contrast(effective) >= ThemeEngine.TextContrast);
    }

    [Fact] public void ShareCodeRoundTripsAndRejectsGarbage()
    {
        var spec = ThemeGallery.Find("synthwave")!.Spec with { Name = "Meu Synthwave ✨", CardOpacity = 0.85 };
        var code = ThemeEngine.ToCode(spec);
        Assert.StartsWith(ThemeEngine.CodePrefix, code);
        Assert.True(ThemeEngine.TryFromCode("olha meu tema: " + code + " curtiu?", out var back));
        Assert.Equal(ThemeEngine.Normalize(spec) with { Backdrop = back.Backdrop }, back);
        Assert.Equal(spec.Backdrop.Colors, back.Backdrop.Colors); Assert.Equal("waves", back.Backdrop.Kind);
        Assert.False(ThemeEngine.TryFromCode("LOTUS-THEME:!!!", out _));
        Assert.False(ThemeEngine.TryFromCode("LOTUS-THEME:" + Convert.ToBase64String("{\"V\":9}"u8.ToArray()), out _));
        Assert.False(ThemeEngine.TryFromCode("nada aqui", out _));
        // Out-of-range values from a hand-made code are clamped, unknown backgrounds become "none".
        var raw = "{\"V\":1,\"N\":\"" + new string('x', 80) + "\",\"L\":false,\"Bg\":\"#000\",\"Sf\":\"zzz\",\"Tx\":\"#FFF\",\"Ac\":\"#F0F\",\"Dt\":\"#FF0\",\"Op\":0.1,\"K\":\"fire\",\"Cs\":[\"#111\"],\"Sp\":50,\"In\":-3,\"Gr\":true}";
        Assert.True(ThemeEngine.TryFromCode("LOTUS-THEME:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw)), out var odd));
        Assert.Equal(ThemeSpec.NameLimit, odd.Name.Length); Assert.Equal(0.78, odd.CardOpacity); Assert.Equal("none", odd.Backdrop.Kind);
        Assert.Equal(2, odd.Backdrop.Speed); Assert.Equal(0.1, odd.Backdrop.Intensity); Assert.Equal(new ThemeSpec().Surface, odd.Surface);
        Assert.Equal(BackdropSpec.MinColors, Math.Min(odd.Backdrop.Colors.Count, BackdropSpec.MinColors));
    }

    [Fact] public void OneColorMakesAReadableThemeInBothModes()
    {
        foreach (var seed in new[] { "#E04C84", "#FCEE0A", "#00F0FF", "#2E7D4F", "#808080", "#3B1F9E", "#FF6A00" })
            foreach (var light in new[] { false, true })
            {
                var spec = ThemeEngine.FromColor(ThemeColor.Parse(seed), light, "Teste");
                Assert.Equal(light, spec.Light); Assert.Equal("gradient", spec.Backdrop.Kind);
                AssertReadable(ThemeEngine.Derive(spec), seed + (light ? " claro" : " escuro"));
            }
    }

    [Fact] public void PreferencesKeepOnlyValidCustomThemes()
    {
        var code = ThemeEngine.ToCode(ThemeGallery.Find("aurora")!.Spec);
        var p = new InterfacePreferences { Theme = "Custom", CustomTheme = code, SavedThemes = InterfacePreferences.JoinThemes([code, "lixo", code]) }.Normalize();
        Assert.Equal("Custom", p.Theme); Assert.Equal("Aurora", p.ActiveCustom()?.Name); Assert.Single(p.MyThemes);
        Assert.Equal("Midnight", new InterfacePreferences { Theme = "Custom", CustomTheme = "quebrado" }.Normalize().Theme);
        // A clean file must compare equal after Normalize, or Lotus would warn about "invalid preferences" on every start.
        var clean = new InterfacePreferences { SavedThemes = code }.Normalize();
        Assert.Equal(clean, clean.Normalize());
        Assert.Equal(new InterfacePreferences(), new InterfacePreferences().Normalize());
    }
}
