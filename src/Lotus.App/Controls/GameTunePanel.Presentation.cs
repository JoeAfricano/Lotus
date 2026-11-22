using Lotus.Core.Games;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Lotus.App.Controls;

public sealed partial class GameTunePanel
{
    private async Task<UIElement> PresentationAsync(GamesViewModel.GameTune t)
    {
        var model=vm!; var game=card!.Game; var text=model.Text; var info=t.LowLatency;
        var saved=model.PrefsFor(game).Presentation;
        var s=new StackPanel {Spacing=10};
        Action? markDirty=null;
        var box=new Border {Padding=new Thickness(16),CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),
            Background=Res("CardBrush"),BorderBrush=Res(saved.Enabled?"SuccessBrush":"CardBorderBrush"),Child=s};
        s.Children.Add(new TextBlock {Text=text["GpTitle"],FontSize=17,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
        s.Children.Add(Caption(text["GpIntro"]));
        s.Children.Add(new InfoBar {IsOpen=true,IsClosable=false,Severity=InfoBarSeverity.Informational,Message=text["GpBeforeLaunch"]});
        var enabled=new ToggleSwitch {Header=text["GpEnable"],IsOn=saved.Enabled};
        AutomationProperties.SetAutomationId(enabled,"game-presentation-enabled"); s.Children.Add(enabled);
        var options=new StackPanel {Spacing=10};
        var optionHost=new ContentControl {Content=options,IsEnabled=enabled.IsOn,HorizontalContentAlignment=HorizontalAlignment.Stretch}; s.Children.Add(optionHost);
        var modes=await model.DisplayModesAsync();
        var outputs=modes.DistinctBy(m=>m.Device).ToArray();
        var monitor=new ComboBox {Header=text["GpMonitor"],HorizontalAlignment=HorizontalAlignment.Stretch};
        foreach(var m in outputs) monitor.Items.Add(new ComboBoxItem {Content=$"{m.Device} · {m.Name} · {m.Width} × {m.Height}",Tag=m});
        monitor.SelectedIndex=Math.Max(0,Array.FindIndex(outputs,m=>m.Device==saved.Device));
        AutomationProperties.SetAutomationId(monitor,"game-presentation-monitor"); options.Children.Add(monitor);
        var hz=new ComboBox {Header=text["GpHz"],HorizontalAlignment=HorizontalAlignment.Stretch};
        AutomationProperties.SetAutomationId(hz,"game-presentation-hz"); options.Children.Add(hz);
        void LoadHz()
        {
            hz.Items.Clear(); hz.Items.Add(new ComboBoxItem {Content=text["GpHzAuto"],Tag=0});
            if(monitor.SelectedItem is ComboBoxItem {Tag:GameDisplayMode output})
                foreach(var mode in PresentationPlan.ForOutput(modes,output)) hz.Items.Add(new ComboBoxItem {Content=$"{mode.Rate:0.###} Hz",Tag=mode.Hz});
            hz.SelectedIndex=Math.Max(0,hz.Items.OfType<ComboBoxItem>().ToList().FindIndex(i=>Equals(i.Tag,saved.Hz)));
        }
        monitor.SelectionChanged+=(_,_)=>LoadHz(); LoadHz();
        var details=new StackPanel {Spacing=8};
        details.Children.Add(Caption(text["GpResolution"]));
        var limiter=new ComboBox {Header=text["GpLimiter"],HorizontalAlignment=HorizontalAlignment.Stretch};
        foreach(var mode in new[]{"auto","fluid","back","async","responsive"})
            limiter.Items.Add(new ComboBoxItem {Content=text["GpMode_"+mode],Tag=mode,IsEnabled=mode!="responsive"||info is {Nvidia:true,ReflexFits:true}});
        limiter.SelectedIndex=Math.Max(0,limiter.Items.OfType<ComboBoxItem>().ToList().FindIndex(i=>Equals(i.Tag,saved.Limiter)));
        AutomationProperties.SetAutomationId(limiter,"game-presentation-limiter"); options.Children.Add(limiter);
        var why=Caption(""); options.Children.Add(why);
        void Explain() => why.Text=text["GpWhy_"+((limiter.SelectedItem as ComboBoxItem)?.Tag as string??"auto")];
        limiter.SelectionChanged+=(_,_)=>Explain(); Explain();
        details.Children.Add(Caption(info?.Nvidia==true?text["GpNvidia"]:text["GpOtherGpu"]));
        if(info?.Api is null) details.Children.Add(Caption(text["GpApiUnknown"]));
        if(info is {FrameGen:true})
        {
            var fg=new CheckBox {Content=text["LlUseFrameGen"],IsChecked=info.UsesFrameGen};
            fg.Click+=(_,_)=>
            {
                info=info with {UsesFrameGen=fg.IsChecked==true};
                // With frame generation on NVIDIA only the automatic mode fits (Reflex with VRR, driver V-Sync without):
                // pick it for the player instead of failing on Save with a manual mode.
                if(fg.IsChecked==true && info.Nvidia) limiter.SelectedIndex=Math.Max(0,limiter.Items.OfType<ComboBoxItem>().ToList().FindIndex(i=>Equals(i.Tag,"auto")));
                markDirty?.Invoke();
            };
            options.Children.Add(fg);
            // Without G-Sync/FreeSync on NVIDIA, saving with the automatic mode applies the frame generation preset.
            if(info.Nvidia && !t.Settings.Recommendation.AdaptiveSync) options.Children.Add(Caption(text["GpFrameGenFixedHint"]));
        }
        details.Children.Add(Caption(t.Settings.Recommendation.AdaptiveSync?text["GpCapVrr"]:text["GpCapFixed"]));
        details.Children.Add(Caption(text["GpSyncHint"]));
        details.Children.Add(Caption(text["GpGraphNote"]));
        var custom=new NumberBox {Header=text["GpCapCustom"],Minimum=0,Maximum=1000,Value=saved.RequestedCap,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact};
        AutomationProperties.SetAutomationId(custom,"game-presentation-cap");
        details.Children.Add(custom);
        options.Children.Add(new Expander {Header=text["GpAdvanced"],Content=details,HorizontalAlignment=HorizontalAlignment.Stretch});
        var result=new InfoBar {IsOpen=saved.Enabled,IsClosable=false,Severity=InfoBarSeverity.Success,
            Message=!saved.Enabled?"":saved.AppliedLimiter=="fgvsync"?text.Format("GpPreparedFgVsync",saved.Cap.ToString("0.##"),saved.PreparedRate.ToString("0.###"))
                :text.Format("GpPrepared",saved.Cap.ToString("0.##"),saved.PreparedRate.ToString("0.###"))};
        markDirty=()=> {result.Message=text["GpUnsaved"];result.Severity=InfoBarSeverity.Warning;result.IsOpen=true;PrimaryAction.IsEnabled=false;};
        enabled.Toggled+=(_,_)=>markDirty();
        monitor.SelectionChanged+=(_,_)=>markDirty(); hz.SelectionChanged+=(_,_)=>markDirty();
        limiter.SelectionChanged+=(_,_)=>markDirty(); custom.ValueChanged+=(_,_)=>markDirty();
        var apply=new Button {Content=text["GpSave"],Style=(Style)Application.Current.Resources["PrimaryButtonStyle"],HorizontalAlignment=HorizontalAlignment.Stretch};
        AutomationProperties.SetAutomationId(apply,"game-presentation-save");
        enabled.Toggled+=(_,_)=>optionHost.IsEnabled=enabled.IsOn && outputs.Length>0 && info is {State:"ok"};
        if(outputs.Length==0 || info is not {State:"ok"})
        {
            optionHost.IsEnabled=false; enabled.IsEnabled=saved.Enabled;
            s.Children.Add(Caption(text[info?.State=="noRtss"?"LlNoRtss":outputs.Length==0?"GpNoModes":"LlNoExe"]));
        }
        apply.Click+=async (_,_)=>
        {
            apply.IsEnabled=false;
            try
            {
                var output=(monitor.SelectedItem as ComboBoxItem)?.Tag as GameDisplayMode;
                var prefs=new GamePresentationPrefs(enabled.IsOn,output?.Device??"",output?.Width??0,output?.Height??0,
                    (int?)((hz.SelectedItem as ComboBoxItem)?.Tag)??0,(limiter.SelectedItem as ComboBoxItem)?.Tag as string??"auto",custom.Value);
                var ok=await model.SavePresentationAsync(game,prefs,info,t.Settings.Recommendation.AdaptiveSync);
                result.Message=model.PresentationStatus; result.Severity=ok?InfoBarSeverity.Success:InfoBarSeverity.Error; result.IsOpen=true;
                if(ok) {PrimaryAction.IsEnabled=!t.Running && game.CanLaunch;SecondaryAction.IsEnabled=!t.Running && (prefs.Enabled||t.Settings.HasOriginal);}
            }
            catch(Exception ex) { Serilog.Log.Warning(ex,"Save game presentation"); result.Message=text["GpSaveFailed"];result.Severity=InfoBarSeverity.Error;result.IsOpen=true; }
            finally { apply.IsEnabled=true; }
        };
        s.Children.Add(apply); s.Children.Add(result);
        var live=Caption(""); s.Children.Add(live);
        void State() => live.Text=MainWindow.Shell?.PresentationActiveFor(game.Key)==true?text["GpActive"]:"";
        State();
        if(MainWindow.Shell is {} shell)
        {
            EventHandler changed=(_,_)=>State();
            box.Loaded+=(_,_)=> {shell.PresentationChanged+=changed; State();};
            box.Unloaded+=(_,_)=>shell.PresentationChanged-=changed;
        }
        // Retain the old limiter actions for an existing profile until the optional combined plan is enabled.
        if(!saved.Enabled && info?.On==true)
            s.Children.Add(new Expander {Header=text["GpExisting"],Content=LowLatency(t),HorizontalAlignment=HorizontalAlignment.Stretch});
        return box;
    }
}
