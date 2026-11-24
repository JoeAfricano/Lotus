using System.Text;
using System.Text.RegularExpressions;
using Lotus.Infrastructure.Games;
using Lotus.Infrastructure.Integrations;
using Xunit;

namespace Lotus.Tests;

public sealed class PremiumOverlayTests : IDisposable
{
    [Fact] public void Concurrent_preview_requests_produce_decodable_pngs_including_pulse()
    {
        if(!OperatingSystem.IsWindows())return;
        var variants=LotusOverlayLayout.Variants.Where(v=>v!=LotusOverlayLayout.Window).ToArray();
        Parallel.For(0,70,i=>
        {
            var frame=RtssLayoutPreview.Render(variants[i%variants.Length]);
            using var stream=new MemoryStream(frame.Png);
            using var bitmap=System.Drawing.Image.FromStream(stream);
            Assert.Equal(frame.Width*2,bitmap.Width); Assert.Equal(frame.Height*2,bitmap.Height); // supersampled previews
        });
        Assert.True(RtssLayoutPreview.Render("premium-pulse").Png.Length>1000);
    }
    readonly string root=Path.Combine(Path.GetTempPath(),"Lotus-Premium-Test-"+Guid.NewGuid().ToString("N"));
    public PremiumOverlayTests()=>Directory.CreateDirectory(root);
    [Theory]
    [InlineData("premium-top","equilibrio",578,22)]
    [InlineData("premium-neon","neon",264,30)]
    [InlineData("premium-pulse","pulse",188,34)]
    public void NativeLayoutsExportEveryDependencyWithoutCapFrameX(string variant,string art,int graphWidth,int graphHeight)
    {
        string file=LotusOverlayLayout.Export(root,variant),text=File.ReadAllText(file,Encoding.Latin1);
        var ini=new IniText(text);
        Assert.Equal("Lotus-atlas-"+art+".png",ini.Get("Settings","EmbeddedImage"));
        Assert.True(File.Exists(Path.Combine(root,ini.Get("Settings","EmbeddedImage")!)));
        Assert.Contains($"<G=Frametime,{graphWidth},{graphHeight},0,0,33.3,0>",text);
        Assert.Contains("ExtentX="+graphWidth+"\r\nExtentY="+graphHeight,text);
        Assert.DoesNotContain("CapFrameX",text,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Provider=MSIAB",text);
        Assert.DoesNotContain("http",text,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(variant,LotusOverlayLayout.VariantOf(Path.GetFileNameWithoutExtension(file)));
        Assert.Equal(Regex.Matches(text,@"(?m)^\[Layer\d+\]").Count.ToString(),ini.Get("General","Layers"));
        Assert.Equal(Regex.Matches(text,@"(?m)^\[Source\d+\]").Count.ToString(),ini.Get("General","Sources"));
        foreach(Match macro in Regex.Matches(text,@"%([A-Za-z][^%\r\n]*?)%"))Assert.Contains("Name="+macro.Groups[1].Value+"\r\n",text);
        Assert.DoesNotContain("<FRAVG>",text); // do not use benchmark-only tags as live statistics
        Assert.Contains("1000/percentile(Frametime,99.9)",text);
    }
    [Fact] public void PluginActivationBacksUpOnlyItsFlagAndRestorationPreservesUnrelatedChanges()
    {
        var config=Path.Combine(root,"Config");var backups=Path.Combine(root,"Backup");
        File.WriteAllText(config,"[Plugins]\r\nOverlayEditor.dll=0\r\nOther.dll=1\r\n[Settings]\r\nColor=old");
        RtssOverlayPlugin.Enable(config,backups);RtssOverlayPlugin.Enable(config,backups);
        var ini=new IniText(File.ReadAllText(config));Assert.Equal("1",ini.Get("Plugins","OverlayEditor.dll"));
        ini.Set("Settings","Color","new");File.WriteAllText(config,ini.ToString());
        Assert.True(RtssOverlayPlugin.Restore(config,backups));
        ini=new IniText(File.ReadAllText(config));Assert.Equal("0",ini.Get("Plugins","OverlayEditor.dll"));Assert.Equal("1",ini.Get("Plugins","Other.dll"));Assert.Equal("new",ini.Get("Settings","Color"));
    }
    [Fact] public void UnknownPluginConfigurationIsNotWritten()
    {
        var config=Path.Combine(root,"Config");const string original="[Plugins]\r\nOverlayEditor.dll=yes";File.WriteAllText(config,original);
        Assert.Throws<InvalidDataException>(()=>RtssOverlayPlugin.Enable(config,root));Assert.Equal(original,File.ReadAllText(config));
    }
    // A RivaTuner installed but never opened (fresh PC): no [Plugins] list yet, or no Config file at all.
    [Fact] public void NeverOpenedRivaTunerGetsThePluginEnabledAndRestoredToOff()
    {
        var config=Path.Combine(root,"Config");var backups=Path.Combine(root,"Backup");File.WriteAllText(config,"[Settings]\r\nFirstRun=1");
        RtssOverlayPlugin.Enable(config,backups);
        var ini=new IniText(File.ReadAllText(config));Assert.Equal("1",ini.Get("Plugins","OverlayEditor.dll"));Assert.Equal("1",ini.Get("Settings","FirstRun"));
        Assert.True(RtssOverlayPlugin.Restore(config,backups));Assert.Equal("0",new IniText(File.ReadAllText(config)).Get("Plugins","OverlayEditor.dll"));
        var missing=Path.Combine(root,"NoConfig");var backups2=Path.Combine(root,"Backup2");
        RtssOverlayPlugin.Enable(missing,backups2);Assert.Equal("1",new IniText(File.ReadAllText(missing)).Get("Plugins","OverlayEditor.dll"));
        File.Delete(missing);Assert.True(RtssOverlayPlugin.Restore(missing,backups2));Assert.False(File.Exists(missing));
    }
    // Any style leaves the OSD on (a fresh RivaTuner or one with the OSD off would otherwise show nothing); premium also
    // turns RivaTuner's own fill and box off. Switching styles and undoing give back the player's original flags.
    [Fact] public void EveryStyleTurnsTheOsdOnAndUndoGivesBackTheOriginal()
    {
        var overlays=Path.Combine(root,"Overlays");var backups=Path.Combine(root,"Backups");var layoutCfg=Path.Combine(root,"OverlayEditor.cfg");
        var plugin=Path.Combine(root,"Config");var global=Path.Combine(root,"Global");
        const string original="[OSD]\r\nEnableOSD=0\r\nEnableFill=1\r\nEnableBgnd=1";File.WriteAllText(global,original);
        RtssOverlayPlugin.ActivateLayout(LotusOverlayLayout.Export(overlays,LotusOverlayLayout.Lateral),layoutCfg,plugin,backups,overlays,global);
        var ini=new IniText(File.ReadAllText(global));Assert.Equal("1",ini.Get("OSD","EnableOSD"));Assert.Equal("1",ini.Get("OSD","EnableFill"));Assert.Equal("1",ini.Get("OSD","EnableBgnd"));
        RtssOverlayPlugin.ActivateLayout(LotusOverlayLayout.Export(overlays,LotusOverlayLayout.PremiumNeon),layoutCfg,plugin,backups,overlays,global);
        ini=new IniText(File.ReadAllText(global));Assert.Equal("1",ini.Get("OSD","EnableOSD"));Assert.Equal("0",ini.Get("OSD","EnableFill"));Assert.Equal("0",ini.Get("OSD","EnableBgnd"));
        RtssOverlayPlugin.ActivateLayout(LotusOverlayLayout.Export(overlays,LotusOverlayLayout.Neon),layoutCfg,plugin,backups,overlays,global);
        ini=new IniText(File.ReadAllText(global));Assert.Equal("1",ini.Get("OSD","EnableOSD"));Assert.Equal("1",ini.Get("OSD","EnableFill"));
        Assert.True(RtssOverlayPlugin.RestoreLayout(layoutCfg,plugin,backups,global));
        Assert.Equal(original,File.ReadAllText(global));
    }
    [Fact] public void FreshInstallActivationWorksWithoutConfigOrGlobalAndRollsBackToNothing()
    {
        var overlays=Path.Combine(root,"Overlays");var backups=Path.Combine(root,"Backups");var layoutCfg=Path.Combine(root,"OverlayEditor.cfg");
        var plugin=Path.Combine(root,"Config");var global=Path.Combine(root,"Global");
        var layout=LotusOverlayLayout.Export(overlays,LotusOverlayLayout.PremiumTop);
        RtssOverlayPlugin.ActivateLayout(layout,layoutCfg,plugin,backups,overlays,global);
        Assert.Equal("1",new IniText(File.ReadAllText(plugin)).Get("Plugins","OverlayEditor.dll"));
        Assert.Equal("1",new IniText(File.ReadAllText(global)).Get("OSD","EnableOSD"));
        Assert.Equal(Path.GetFullPath(layout),LotusOverlayLayout.ActiveLayout(layoutCfg));
        Assert.True(RtssOverlayPlugin.RestoreLayout(layoutCfg,plugin,backups,global));
        Assert.False(File.Exists(layoutCfg));Assert.Equal("0",new IniText(File.ReadAllText(plugin)).Get("Plugins","OverlayEditor.dll"));
        Assert.Null(new IniText(File.ReadAllText(global)).Get("OSD","EnableOSD"));
    }
    [Fact] public void RestoreDoesNotOverwriteUserDisableWhenOriginalWasEnabled()
    {
        var config=Path.Combine(root,"Config");File.WriteAllText(config,"[Plugins]\r\nOverlayEditor.dll=1");RtssOverlayPlugin.Enable(config,root);
        File.WriteAllText(config,"[Plugins]\r\nOverlayEditor.dll=0");Assert.False(RtssOverlayPlugin.Restore(config,root));Assert.Contains("OverlayEditor.dll=0",File.ReadAllText(config));
    }
    [Fact] public void PluginFailureRollsBackLayoutAndNewBackups()
    {
        var overlays=Path.Combine(root,"Overlays");var backups=Path.Combine(root,"Backups");var config=Path.Combine(root,"OverlayEditor.cfg");var plugin=Path.Combine(root,"Config");
        var layout=LotusOverlayLayout.Export(overlays,LotusOverlayLayout.PremiumTop);
        const string original="[Settings]\r\nLayout=previous.ovl";File.WriteAllText(config,original);File.WriteAllText(plugin,"[Plugins]\r\nOverlayEditor.dll=invalid");
        Assert.Throws<InvalidDataException>(()=>RtssOverlayPlugin.ActivateLayout(layout,config,plugin,backups,overlays));
        Assert.Equal(original,File.ReadAllText(config));Assert.Equal("[Plugins]\r\nOverlayEditor.dll=invalid",File.ReadAllText(plugin));Assert.False(File.Exists(Path.Combine(backups,"OverlayEditor.cfg.original")));
    }
    [Fact] public void CleanBackgroundRestoresOnlyItsKeysAndKeepsNewFpsLimit()
    {
        var profile=Path.Combine(root,"Global");File.WriteAllText(profile,"[OSD]\r\nEnableOSD=1\r\nEnableFill=1\r\n[Framerate]\r\nLimit=75");
        RtssOverlayBackground.Apply(profile,root);var ini=new IniText(File.ReadAllText(profile));Assert.Equal("0",ini.Get("OSD","EnableFill"));Assert.Equal("0",ini.Get("OSD","EnableBgnd"));
        ini.Set("Framerate","Limit","90");File.WriteAllText(profile,ini.ToString());Assert.True(RtssOverlayBackground.Restore(profile,root));
        ini=new IniText(File.ReadAllText(profile));Assert.Equal("1",ini.Get("OSD","EnableFill"));Assert.Null(ini.Get("OSD","EnableBgnd"));Assert.Equal("90",ini.Get("Framerate","Limit"));
    }
    [Theory]
    [InlineData("top")][InlineData("neon")][InlineData("lateral")][InlineData("pulse")]
    [InlineData("premium-top")][InlineData("premium-neon")][InlineData("premium-pulse")]
    public void PreviewUsesTheActualExportedLayoutAndIncludesEveryNativeStyle(string variant)
    {
        if(!OperatingSystem.IsWindows())return;
        var frame=RtssLayoutPreview.Render(variant);
        Assert.True(frame.Width>100&&frame.Height>20);Assert.Equal(new byte[]{137,80,78,71},frame.Png[..4]);
        using var stream=new MemoryStream(frame.Png);using var image=System.Drawing.Image.FromStream(stream);
        Assert.Equal(frame.Width*2,image.Width);Assert.Equal(frame.Height*2,image.Height);
        Assert.Same(frame,RtssLayoutPreview.Render(variant)); // source-keyed rendering is cached
        if(variant=="premium-top"){Assert.Equal(576,frame.X);Assert.Equal(18,frame.Y);Assert.Equal(768,frame.Width);Assert.Equal(152,frame.Height);}
        if(variant=="premium-neon"){Assert.Equal(1602,frame.X);Assert.Equal(300,frame.Width);Assert.Equal(304,frame.Height);}
        if(variant=="premium-pulse"){Assert.Equal(18,frame.X);Assert.Equal(220,frame.Width);Assert.Equal(300,frame.Height);}
    }
    public void Dispose()=>Directory.Delete(root,true);
}
