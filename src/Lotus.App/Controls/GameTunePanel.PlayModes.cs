using Lotus.Core.Games;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lotus.App.Controls;

// "Escolha seu tipo de jogo" (José, 28/11/2026): Modo PC, Modo Console and Personalizado as large cards in the style of the
// welcome screen, each saying what it changes. "Desejo usar FrameGen neste jogo" redraws the cards for frame generation.
// Personalizado opens the detailed Modo Console settings in their own view ("aba") inside the panel.
public sealed partial class GameTunePanel
{
    private bool customOpen;

    private async Task<UIElement> PlayModesAsync(GamesViewModel.GameTune t)
    {
        var model = vm!; var game = card!.Game; var text = model.Text; var info = t.LowLatency;
        var vrr = t.Settings.Recommendation.AdaptiveSync; var nvidia = info?.Nvidia == true;
        var (maxHz, maxRate) = await model.MaxRateAsync();
        var current = PlayModes.Current(model.PrefsFor(game));
        var root = new StackPanel { Spacing = 14 };
        root.Children.Add(Caption(text["PmIntro"]));
        var grid = new Grid { ColumnSpacing = 14 };
        for (var i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        var fgBox = new CheckBox { Content = text["PmUseFrameGen"], IsChecked = model.PrefsFor(game).UseFrameGen, IsEnabled = !t.Running };
        AutomationProperties.SetAutomationId(fgBox, "tune-mode-framegen");
        var fgNote = new TextBlock { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Res("RoseBrush"), TextWrapping = TextWrapping.Wrap };
        void Fill()
        {
            grid.Children.Clear();
            var fg = fgBox.IsChecked == true;
            string[] modes = [PlayModes.Pc, PlayModes.Console, PlayModes.Custom];
            for (var i = 0; i < modes.Length; i++)
            {
                var tile = ModeCard(t, modes[i], current == modes[i], fg, nvidia, vrr, maxHz, maxRate);
                Grid.SetColumn(tile, i); grid.Children.Add(tile);
            }
        }
        fgBox.Click += (_, _) =>
        {
            // Only the choice is saved here; the cards redraw and nothing in RivaTuner or the screen changes until a card is chosen.
            model.SetUseFrameGen(game, fgBox.IsChecked == true);
            info = info is null ? null : info with { UsesFrameGen = fgBox.IsChecked == true };
            if (tune is not null) tune = tune with { LowLatency = info };
            Fill();
            // A mode already in use keeps its RivaTuner/driver plan until it is applied again with the new choice.
            fgNote.Text = current is not null ? text["PmFrameGenReapply"] : "";
        };
        Fill();
        Motion.SetStagger(grid, true);
        root.Children.Add(grid);
        if (t.Tech.FrameGen || model.PrefsFor(game).UseFrameGen)
        {
            // A strip under the cards (José's sketch): the FrameGen choice with what it does.
            var strip = new Grid { ColumnSpacing = 14 };
            strip.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); strip.ColumnDefinitions.Add(new ColumnDefinition());
            strip.Children.Add(new FontIcon { Glyph = "", FontSize = 18, Foreground = Res("RoseBrush"), VerticalAlignment = VerticalAlignment.Center });
            var words = new StackPanel { Spacing = 2 }; words.Children.Add(fgBox); words.Children.Add(Caption(text["PmFrameGenHint"])); words.Children.Add(fgNote);
            Grid.SetColumn(words, 1); strip.Children.Add(words);
            root.Children.Add(new Border { Padding = new Thickness(18, 12, 18, 12), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1),
                Background = Res("AccentSoftBrush"), BorderBrush = Res("CardBorderBrush"), Child = strip });
        }
        if (t.Running) root.Children.Add(Caption(text["GpCloseFirst"]));
        return root;
    }

    // Premium card (José, 28/11/2026): dark plum gradient like the welcome stage, a rose-to-gold accent line on top, a soft
    // glow in the corner, the rate as a large gold number, what changes as a checklist and one action. Hover lifts it.
    // Colors come from the theme the player uses (rosé, escuro, grafite, sistema or a custom one), read when the cards are built.
    private static Windows.UI.Color C(string key) => Application.Current.Resources[key] is SolidColorBrush b ? b.Color : Microsoft.UI.Colors.Gray;
    private static Windows.UI.Color A(Windows.UI.Color c, byte alpha) => Windows.UI.Color.FromArgb(alpha, c.R, c.G, c.B);
    private static Windows.UI.Color Mix(Windows.UI.Color a, Windows.UI.Color b, double t) =>
        Windows.UI.Color.FromArgb(255, (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    private static Brush Gradient(params (Windows.UI.Color Color, double At)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1) };
        foreach (var s in stops) b.GradientStops.Add(new GradientStop { Color = s.Color, Offset = s.At });
        return b;
    }
    private Border ModeCard(GamesViewModel.GameTune t, string mode, bool selected, bool frameGen, bool nvidia, bool vrr, int maxHz, double maxRate)
    {
        var text = vm!.Text; var culture = System.Globalization.CultureInfo.GetCultureInfo(text.Language);
        var rose = A(C("RoseBrush"), 255); var gold = A(C("GoldBrush"), 255); var window = A(C("WindowBrush"), 255); var main = A(C("MainTextBrush"), 255);
        var surface = Mix(window, rose, 0.10); // the card body: the window color warmed by the theme's accent
        var plan = mode == PlayModes.Custom ? null : PlayModes.For(mode, maxHz, maxRate, frameGen, nvidia, vrr);
        // The card in use describes the limiter really saved for this game (a game set before 1.0.7 may keep front edge).
        if (selected && plan is not null && vm.PrefsFor(card!.Game).Presentation.AppliedLimiter is { Length: > 0 } applied)
            plan = plan with { Pacing = applied switch { "responsive" => "reflex", "fgvsync" => "vsync-reflex", "fluid" => "front-edge", _ => plan.Pacing } };
        var layers = new Grid();
        // Soft glow in the top-right corner (stronger on the card in use).
        layers.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 260, Height = 260, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -110, -90, 0), IsHitTestVisible = false,
            Fill = new RadialGradientBrush { GradientStops = { new GradientStop { Color = A(rose, (byte)(selected ? 0x66 : 0x38)), Offset = 0 }, new GradientStop { Color = A(rose, 0), Offset = 1 } } } });
        layers.Children.Add(Illustration(mode, selected));
        var s = new StackPanel { Spacing = 12, Padding = new Thickness(22, 22, 22, 20) };
        var head = new Grid { ColumnSpacing = 12 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var glyph = mode switch { PlayModes.Pc => "", PlayModes.Console => "", _ => "" };
        head.Children.Add(new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(22), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(A(gold, 0x66)), Background = Gradient((A(rose, 0x55), 0), (A(gold, 0x22), 1)),
            Child = new FontIcon { Glyph = glyph, FontSize = 19, Foreground = new SolidColorBrush(gold) } });
        var names = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = text["PmEyebrow_" + mode].ToUpperInvariant(), FontSize = 10.5, CharacterSpacing = 120, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(rose) });
        names.Children.Add(new TextBlock { Text = text["PmTitle_" + mode], FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(main) });
        Grid.SetColumn(names, 1); head.Children.Add(names);
        if (selected)
        {
            var badge = new Border { CornerRadius = new CornerRadius(11), Padding = new Thickness(10, 3, 10, 3), VerticalAlignment = VerticalAlignment.Top, Background = Gradient((rose, 0), (gold, 1)),
                Child = new TextBlock { Text = text["PmSelected"], FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(window) } };
            Grid.SetColumn(badge, 2); head.Children.Add(badge);
        }
        s.Children.Add(head);
        // The hero number: the screen rate (or "Você decide" for Personalizado), with RivaTuner's cap under it.
        var hero = new StackPanel { Spacing = 0, Margin = new Thickness(0, 4, 0, 0) };
        var big = new TextBlock { FontSize = 44, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Gradient((Mix(gold, main, 0.35), 0), (gold, 0.55), (rose, 1)) };
        if (plan is { Hz: > 0 }) { big.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = plan.Rate.ToString("0.##", culture) }); big.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = " Hz", FontSize = 20 }); }
        else big.Text = plan is null ? text["PmHeroCustom"] : "— Hz";
        hero.Children.Add(big);
        hero.Children.Add(new TextBlock { Text = plan is null ? text["PmHeroCustomSub"] : text.Format("PmHeroSub", plan.Cap.ToString("0.##", culture)), FontSize = 12.5, Foreground = Res("MutedBrush") });
        s.Children.Add(hero);
        s.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 2, 0, 2), Background = Gradient((A(gold, 0), 0), (A(gold, 0x66), 0.5), (A(gold, 0), 1)) });
        s.Children.Add(new TextBlock { Text = text["PmWhat_" + mode], FontSize = 13, Foreground = new SolidColorBrush(A(main, 0xDD)), TextWrapping = TextWrapping.Wrap });
        var lines = new List<string>();
        if (plan is null) { lines.Add(text["PmCustom1"]); lines.Add(text["PmCustom2"]); lines.Add(text["PmCustom3"]); }
        else
        {
            lines.Add(plan.Hz > 0 ? text.Format(vrr ? "PmLineHzVrr" : "PmLineHz", plan.Rate.ToString("0.##", culture)) : text["PmLineHzUnknown"]);
            lines.Add(plan.Pacing == "vsync-reflex" ? text["PmLineFgVsync"] : text.Format(plan.Pacing == "reflex" ? "PmLineReflex" : "PmLineFrontEdge", plan.Cap.ToString("0.##", culture)));
            lines.Add(text[frameGen ? "PmLineFrameGen" : mode == PlayModes.Console ? "PmLineConsoleGraphics" : "PmLinePcGraphics"]);
        }
        var list = new StackPanel { Spacing = 7 };
        foreach (var line in lines)
        {
            var row = new Grid { ColumnSpacing = 10 }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(A(rose, 0x33)), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0),
                Child = new FontIcon { Glyph = "", FontSize = 10, Foreground = new SolidColorBrush(rose) } });
            var tb = new TextBlock { Text = line, FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(main) }; Grid.SetColumn(tb, 1); row.Children.Add(tb);
            list.Children.Add(row);
        }
        s.Children.Add(list);
        var action = new Button { Content = text[mode == PlayModes.Custom ? "PmOpenCustom" : selected ? "PmApplyAgain" : "PmChoose"], HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 42,
            Style = (Style)Application.Current.Resources[selected || mode == PlayModes.Custom ? "SoftButtonStyle" : "PrimaryButtonStyle"], IsEnabled = mode == PlayModes.Custom || !t.Running, Margin = new Thickness(0, 8, 0, 0) };
        Motion.SetPress(action, true);
        AutomationProperties.SetAutomationId(action, "tune-mode-" + mode);
        action.Click += async (_, _) => await ChooseModeAsync(mode, frameGen);
        s.Children.Add(action);
        layers.Children.Add(s);
        // Accent line on top: rose to gold, full on the card in use.
        layers.Children.Add(new Border { Height = 3, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Opacity = selected ? 1 : 0.45,
            Background = Gradient((rose, 0), (gold, 1)) });
        var cardBorder = new Border
        {
            CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(selected ? 2 : 1),
            BorderBrush = selected ? Gradient((rose, 0), (gold, 1)) : new SolidColorBrush(A(gold, 0x55)),
            Background = Gradient((Mix(surface, rose, 0.08), 0), (surface, 0.6), (Mix(surface, window, 0.6), 1)), Child = layers,
        };
        Motion.SetHover(cardBorder, 1.02);
        AutomationProperties.SetAutomationId(cardBorder, "tune-mode-card-" + mode);
        return cardBorder;
    }
    private async Task ChooseModeAsync(string mode, bool frameGen)
    {
        if (vm is null || card is null || tune is null) return;
        if (mode == PlayModes.Custom) { customOpen = true; await Reload(); return; }
        var text = vm.Text;
        if (XamlRoot is { } root)
        {
            var dialog = new ContentDialog { XamlRoot = root, Title = text["PmConfirmTitle_" + mode], Content = new TextBlock { Text = text["PmConfirmBody"], TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = text["PmChoose"], CloseButtonText = text["DlgCancel"], DefaultButton = ContentDialogButton.Primary };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }
        var info = tune.LowLatency is { } i ? i with { UsesFrameGen = frameGen } : null;
        var vrr = tune.Settings.Recommendation.AdaptiveSync;
        var ok = mode == PlayModes.Console ? await vm.UseConsoleModeAsync(card.Game, info, vrr) : await vm.UsePcModeAsync(card.Game, info, vrr);
        Serilog.Log.Information("Play mode {Mode} for {Game}: {Result} {Status}", mode, card.Game.Name, ok ? "set" : "not set", vm.PresentationStatus);
        UiSound.Play(ok ? UiSound.Cue.Done : UiSound.Cue.Fail);
        await Reload();
    }

    // Personalizado: the detailed Modo Console settings in their own view, with a way back to the cards.
    private async Task<UIElement> CustomViewAsync(GamesViewModel.GameTune t)
    {
        var s = new StackPanel { Spacing = 16 };
        var back = new Button { Style = (Style)Application.Current.Resources["SoftButtonStyle"],
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new FontIcon { Glyph = "", FontSize = 13 }, new TextBlock { Text = vm!.Text["PmBack"] } } } };
        AutomationProperties.SetAutomationId(back, "tune-mode-back");
        back.Click += async (_, _) => { customOpen = false; await Reload(); };
        s.Children.Add(back);
        s.Children.Add(Section(vm.Text["PmTitle_custom"], await PresentationAsync(t)));
        return s;
    }

    // Right of the recommendations: the game itself (cover, name, what was detected, last measurement).
    private Border GameBox(GamesViewModel.GameTune t)
    {
        var text = vm!.Text; var game = card!.Game;
        var s = new StackPanel { Spacing = 8 };
        var url = game.Store == GameStores.Steam ? Lotus.Core.Catalog.CatalogText.Portrait(game.Id) : card.CoverPath;
        var art = new Border { CornerRadius = new CornerRadius(12), Height = 300, Background = Res("AccentSoftBrush") };
        if (url is not null && Uri.TryCreate(url, UriKind.Absolute, out var u))
            art.Child = new Image { Stretch = Stretch.UniformToFill, Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(u) { DecodePixelWidth = 520 } };
        s.Children.Add(art);
        s.Children.Add(new TextBlock { Text = game.Name, FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (t.TechChips.Count > 0) s.Children.Add(Caption(string.Join(" · ", t.TechChips)));
        if (t.Last is { AverageFps: > 0 } last)
            s.Children.Add(Caption(text.Format("PmLastMeasured", Math.Round(last.AverageFps), Math.Round(last.Low1Fps)) + vm.LatencyText(last.LatencyMs, null)));
        var box = new Border { Padding = new Thickness(14), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Background = Res("CardBrush"), BorderBrush = Res("CardBorderBrush"), Child = s, VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetAutomationId(box, "tune-game-box");
        return box;
    }

    // Background art of each card (José, 28/11/2026): Lotus's own line drawings, no maker's design or logo. A game controller
    // for Modo Console, a monitor for Modo PC and sliders for Personalizado, in a rose-to-gold stroke, faint behind the text.
    private static readonly Dictionary<string, (string Lines, (double X, double Y, double R)[] Dots)> Art = new()
    {
        [PlayModes.Console] = ("M58,40 C34,40 18,62 14,96 C10,130 22,150 42,150 C58,150 66,128 80,116 L160,116 C174,128 182,150 198,150 C218,150 230,130 226,96 C222,62 206,40 182,40 Z M54,70 L54,98 M40,84 L68,84 M100,64 L140,64",
            [(176, 66, 7), (192, 82, 7), (160, 82, 7), (176, 98, 7), (96, 92, 9), (144, 92, 9)]),
        [PlayModes.Pc] = ("M20,24 L220,24 L220,142 L20,142 Z M30,34 L210,34 L210,132 L30,132 Z M44,118 L84,78 L116,104 L150,64 L196,118 M104,142 L98,166 L142,166 L136,142 M84,170 L156,170",
            [(186, 54, 8)]),
        [PlayModes.Custom] = ("M24,50 L216,50 M24,100 L216,100 M24,150 L216,150", [(150, 50, 12), (72, 100, 12), (180, 150, 12)]),
    };
    private static UIElement Illustration(string mode, bool selected)
    {
        var (lines, dots) = Art[mode];
        var stroke = Gradient((A(C("RoseBrush"), 255), 0), (A(C("GoldBrush"), 255), 1));
        var canvas = new Canvas { Width = 240, Height = 190 };
        canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Stroke = stroke, StrokeThickness = 3.5, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), lines) });
        foreach (var (x, y, r) in dots)
        {
            var dot = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = r * 2, Height = r * 2, Stroke = stroke, StrokeThickness = 3.5 };
            Canvas.SetLeft(dot, x - r); Canvas.SetTop(dot, y - r); canvas.Children.Add(dot);
        }
        return new Viewbox { Child = canvas, Width = 230, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 70, -24, 0), Opacity = selected ? 0.30 : 0.18, IsHitTestVisible = false };
    }
}