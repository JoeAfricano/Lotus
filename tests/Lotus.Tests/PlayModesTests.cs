using Lotus.Core.Games;
using Xunit;

namespace Lotus.Tests;

// "Escolha seu tipo de jogo" (28/11/2026): Modo PC, Modo Console and Personalizado cards on the Otimizar screen.
public sealed class PlayModesTests
{
    [Fact]
    public void The_card_in_use_follows_the_saved_plan()
    {
        Assert.Null(PlayModes.Current(new GamePlayPrefs()));
        Assert.Equal(PlayModes.Console, PlayModes.Current(new GamePlayPrefs { Prefer60 = true, Presentation = new(true, "d", 1920, 1080, 60, "fluid") }));
        Assert.Equal(PlayModes.Pc, PlayModes.Current(new GamePlayPrefs { Presentation = new(true, "d", 1920, 1080, 0, "auto") }));
        Assert.Equal(PlayModes.Custom, PlayModes.Current(new GamePlayPrefs { Presentation = new(true, "d", 1920, 1080, 75, "responsive") }));
        Assert.Equal(PlayModes.Custom, PlayModes.Current(new GamePlayPrefs { Presentation = new(true, "d", 1920, 1080, 0, "auto"), PacingPreset = PacingPresets.LowLatency }));
    }

    [Fact]
    public void Cards_say_what_each_mode_sets_on_this_monitor()
    {
        var pc = PlayModes.For(PlayModes.Pc, 75, 74.973, frameGen: false, nvidia: true, vrr: false);
        Assert.Equal((75, 74.97, "reflex"), (pc.Hz, pc.Cap, pc.Pacing)); // NVIDIA: Reflex by default
        Assert.Equal("front-edge", PlayModes.For(PlayModes.Pc, 75, 74.973, false, nvidia: false, vrr: false).Pacing);
        var console = PlayModes.For(PlayModes.Console, 75, 74.973, false, true, false);
        Assert.Equal((60, 59.99), (console.Hz, console.Cap));
        Assert.Equal("vsync-reflex", PlayModes.For(PlayModes.Console, 75, 74.973, frameGen: true, nvidia: true, vrr: false).Pacing); // Gothic's preset
        Assert.Equal("front-edge", PlayModes.For(PlayModes.Pc, 75, 74.973, frameGen: true, nvidia: false, vrr: false).Pacing);   // no Reflex off NVIDIA
        var vrr = PlayModes.For(PlayModes.Console, 144, 143.9, false, true, vrr: true);
        Assert.Equal((144, 60.0), (vrr.Hz, vrr.Cap)); // with G-Sync/FreeSync the rate stays and RivaTuner caps 60
    }
}