using Lotus.Core.Benchmark;
using Lotus.Infrastructure.Integrations;
using Xunit;

namespace Lotus.Tests;

public sealed class OverlayWindowTests
{
    [Fact] public void Fps_reacts_to_recent_frames_without_replacing_the_longer_average()
    {
        var frames=new OverlayFrames();
        for(var i=0;i<1000;i++)frames.Add(10);
        var first=frames.Summarize(); Assert.Same(first,frames.Summarize());
        for(var i=0;i<10;i++)frames.Add(25);
        var changed=frames.Summarize()!;
        Assert.Equal(40,changed.Fps,3); Assert.InRange(changed.Average,98,100);
        frames.Clear(); Assert.Null(frames.Summarize());
    }
    [Fact] public void Visibility_and_shortcut_survive_serialization_and_invalid_keys_fall_back()
    {
        var settings=new OverlayWindowSettings { Visible=false,ToggleKey="F8" };
        var loaded=System.Text.Json.JsonSerializer.Deserialize<OverlayWindowSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        Assert.False(loaded.Visible); Assert.Equal("F8",loaded.Normalized().ToggleKey);
        Assert.Equal("F10",(loaded with { ToggleKey="broken" }).Normalized().ToggleKey);
        Assert.Equal("none",(loaded with { ToggleKey="none" }).Normalized().ToggleKey);
    }
    [Theory]
    [InlineData("top-left")][InlineData("top-right")][InlineData("bottom-left")][InlineData("bottom-right")]
    public void Window_overlay_stays_inside_the_client_at_every_corner_and_dpi(string corner)
    {
        foreach (var client in new[] { new System.Drawing.Rectangle(296,165,1280,720), new(-1600,80,800,600), new(700,300,180,120) })
        foreach (var dpi in new[] {1.0,1.5,2.0,3.0})
        {
            var s = new OverlayWindowSettings { Position=corner, Zoom=1.5 };
            var p = Lotus.Infrastructure.Games.GameOverlayWindow.Placement(client,dpi*s.Zoom,s);
            Assert.True(client.Contains(p.Bounds)); Assert.True(p.Bounds.Width>0 && p.Bounds.Height>0);
            Assert.InRange(p.Scale,0.001,dpi*s.Zoom);
            if(corner.EndsWith("right")) Assert.True(p.Bounds.Right<=client.Right);
            if(corner.StartsWith("bottom")) Assert.True(p.Bounds.Bottom<=client.Bottom);
        }
    }

    [Fact] public void Client_origin_is_added_without_using_the_monitor_origin()
    {
        var s = new OverlayWindowSettings { Position="top-left" };
        var a=Lotus.Infrastructure.Games.GameOverlayWindow.Placement(new(0,0,1280,720),1,s).Bounds;
        var b=Lotus.Infrastructure.Games.GameOverlayWindow.Placement(new(296,165,1280,720),1,s).Bounds;
        Assert.Equal(a.X+296,b.X); Assert.Equal(a.Y+165,b.Y); Assert.Equal(a.Size,b.Size);
    }

    [Fact] public void Frames_give_fps_lows_and_graph()
    {
        var f = new OverlayFrames();
        Assert.Null(f.Summarize());
        for (var i = 0; i < 990; i++) f.Add(13.3);
        for (var i = 0; i < 10; i++) f.Add(40); // ten hitches in a thousand frames
        var s = f.Summarize()!;
        Assert.InRange(s.Low1, 24.9, 25.1);    // the slowest 1% = the ten 40 ms frames
        Assert.InRange(s.Low01, 24.9, 25.1);
        Assert.Equal(40, s.FrametimeMs, 3);
        Assert.Equal(OverlayFrames.GraphFrames, s.Graph.Count);
        Assert.InRange(s.Average, 72, 74);
        Assert.Equal(25,s.Fps,3);             // recent quarter-second consists entirely of hitches
    }

    [Fact] public void Frames_ignore_nonsense_and_keep_only_the_recent_ones()
    {
        var f = new OverlayFrames();
        f.Add(double.NaN); f.Add(-1); f.Add(0); f.Add(9999);
        Assert.Null(f.Summarize());
        for (var i = 0; i < OverlayFrames.Capacity; i++) f.Add(50);
        for (var i = 0; i < OverlayFrames.Capacity; i++) f.Add(10);
        Assert.InRange(f.Summarize()!.Low1, 99.9, 100.1); // the old 50 ms frames fell out of the ring
    }

    [Fact] public void Settings_toggle_move_and_corner_cycle()
    {
        var s = new OverlayWindowSettings { Items = ["fps", "graph"] };
        Assert.Equal(["fps", "graph", "gpu"], s.Toggle("gpu").Items);           // a new item goes to the end
        Assert.Equal(["graph"], s.Toggle("fps").Items);
        Assert.Equal(["graph", "fps"], s.Move("fps", 1).Items);
        Assert.Same(s, s.Move("fps", -1));                                     // already first
        Assert.Equal(OverlayWindowSettings.TopRight, s.NextPosition().Position);
        Assert.Equal(OverlayWindowSettings.TopLeft, s with { Position = OverlayWindowSettings.BottomLeft } is var b ? b.NextPosition().Position : "");
    }

    [Fact] public void Settings_fall_back_when_hand_edited()
    {
        var bad = new OverlayWindowSettings { Position = "middle", Opacity = 7, Zoom = double.NaN, Items = ["nope", "fps", "fps"] }.Normalized();
        Assert.Equal(OverlayWindowSettings.TopLeft, bad.Position);
        Assert.Equal(OverlayWindowSettings.MaxOpacity, bad.Opacity);
        Assert.Equal(1, bad.Zoom);
        Assert.Equal(["fps"], bad.Items);
        Assert.Equal(OverlayWindowSettings.AllItems, new OverlayWindowSettings { Items = [] }.Normalized().Items);
        var prefs = new Lotus.Core.InterfacePreferences { OverlayProfile = 9 };
        Assert.Equal(OverlayWindowSettings.DefaultProfiles[2].Items, prefs.OverlayWindow.Items); // clamped to the third profile
    }

    [Fact] public void Window_height_follows_the_items()
    {
        var full = Lotus.Infrastructure.Games.GameOverlayWindow.Measure(new OverlayWindowSettings());
        var small = Lotus.Infrastructure.Games.GameOverlayWindow.Measure(new OverlayWindowSettings { Items = ["fps", "lows"] });
        Assert.True(small < full);
        Assert.Equal(0, Lotus.Infrastructure.Games.GameOverlayWindow.RowHeight("lows", new OverlayWindowSettings { Items = ["fps", "lows"] })); // beside the FPS
        Assert.Equal(30, Lotus.Infrastructure.Games.GameOverlayWindow.RowHeight("lows", new OverlayWindowSettings { Items = ["lows", "fps"] }));
    }

    [Fact] public void Window_layout_keeps_the_sources_and_draws_nothing()
    {
        var text = LotusPulseLayout.Empty("[Source0]\r\nName=Frametime\r\n");
        Assert.Contains("Layers=0", text);
        Assert.Contains("Sources=1", text);
        Assert.DoesNotContain("[Layer", text);
        Assert.Equal("window", LotusOverlayLayout.VariantOf("Lotus-Window"));
    }

    [Fact] public void Window_style_is_recognized_from_RivaTuner_config()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lotus-ow-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var cfg = Path.Combine(dir, "OverlayEditor.cfg");
        try
        {
            File.WriteAllText(cfg, "[Settings]\r\nLayout=C:\\Users\\x\\AppData\\Local\\Lotus\\Overlays\\Lotus-Window.ovl\r\n");
            Assert.True(LotusOverlayLayout.WindowActive(cfg));
            File.WriteAllText(cfg, "[Settings]\r\nLayout=C:\\Users\\x\\AppData\\Local\\Lotus\\Overlays\\Lotus-Neon.ovl\r\n");
            Assert.False(LotusOverlayLayout.WindowActive(cfg));
        }
        finally { Directory.Delete(dir, true); }
    }
}
