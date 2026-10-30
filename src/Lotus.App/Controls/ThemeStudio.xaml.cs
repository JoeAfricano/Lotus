using Lotus.Core;
using Lotus.Core.Themes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;

namespace Lotus.App.Controls;

// Theme Studio (Settings › Appearance): five colors, card transparency and the animated background, previewed live on
// the whole app. The engine keeps every text readable and says what it adjusted. Nothing is saved until "Salvar".
public sealed partial class ThemeStudio : UserControl
{
    private PreferencesViewModel? prefs;
    private ThemePalette? palette;
    private Action? revert;
    private ITextCatalog? text;
    private ThemeSpec spec = new(), start = new();
    private ThemeColor seed = ThemeColor.Parse("#E04C84");
    private bool filling;
    private readonly DispatcherTimer preview = new() { Interval = TimeSpan.FromMilliseconds(90) };
    public bool IsOpen => Visibility == Visibility.Visible;
    public event EventHandler? Saved;

    private static readonly (string Id, string Glyph)[] KindList =
        [("none", ""), ("gradient", ""), ("petals", ""), ("aurora", ""), ("particles", ""), ("waves", ""), ("lotus", "")];

    public ThemeStudio()
    {
        InitializeComponent();
        preview.Tick += (_, _) => { preview.Stop(); palette?.Preview(spec); PaintReadability(); };
        KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; _ = CancelAsync(); } };
    }

    public void Open(ThemeSpec theme, PreferencesViewModel preferences, ThemePalette themePalette, Action revertTheme)
    {
        prefs = preferences; palette = themePalette; revert = revertTheme; text = preferences.Text;
        spec = ThemeEngine.Normalize(theme); start = spec;
        if (ThemeColor.TryParse(spec.Accent, out var a)) seed = a;
        Labels();
        Fill();
        palette.Preview(spec); PaintReadability();
        Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var anim = new DoubleAnimation { From = 460, To = 0, Duration = TimeSpan.FromMilliseconds(Motion.Enabled ? 380 : 0), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(anim, SheetShift); Storyboard.SetTargetProperty(anim, "X"); sb.Children.Add(anim); sb.Begin();
        UiSound.Play(UiSound.Cue.Open);
        NameBox.Focus(FocusState.Programmatic);
    }

    private void Close()
    {
        preview.Stop();
        var sb = new Storyboard();
        var anim = new DoubleAnimation { From = 0, To = 460, Duration = TimeSpan.FromMilliseconds(Motion.Enabled ? 240 : 0), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        Storyboard.SetTarget(anim, SheetShift); Storyboard.SetTargetProperty(anim, "X"); sb.Children.Add(anim);
        sb.Completed += (_, _) => Visibility = Visibility.Collapsed;
        sb.Begin();
        UiSound.Play(UiSound.Cue.Close);
    }

    private string T(string key) => text?[key] ?? key;
    private void Labels()
    {
        Eyebrow.Text = T("TsEyebrow").ToUpperInvariant();
        StartTitle.Text = T("TsStart"); BaseDark.Content = T("TsDark"); BaseLight.Content = T("TsLight");
        SeedTitle.Text = T("TsSeedTitle"); SeedHint.Text = T("TsSeedHint"); SeedGo.Content = T("TsSeedGo");
        ColorsTitle.Text = T("TsColors"); GlassTitle.Text = T("TsGlass"); GlassHint.Text = T("TsGlassHint");
        BackdropTitle.Text = T("TsBackdrop"); BackdropColorsTitle.Text = T("TsBackdropColors");
        SpeedTitle.Text = T("TsSpeed"); IntensityTitle.Text = T("TsIntensity"); GrainTitle.Text = T("TsGrain"); GrainHint.Text = T("TsGrainHint");
        CancelButton.Content = T("TsCancel"); CopyLabel.Text = T("TsCopy"); SaveLabel.Text = T("TsSave");
    }

    // Puts the spec into every control (after open, "Gerar" or a base switch).
    private void Fill()
    {
        filling = true;
        NameBox.Text = spec.Name;
        PaintBase();
        SetSwatch(SeedButton, seed.Hex);
        SeedButton.Flyout = PickerFlyout(seed.Hex, hex => { seed = ThemeColor.Parse(hex); SetSwatch(SeedButton, hex); });
        ColorRows.Children.Clear();
        foreach (var (key, get, set) in new (string, Func<ThemeSpec, string>, Func<ThemeSpec, string, ThemeSpec>)[]
        {
            ("Canvas", s => s.Canvas, (s, v) => s with { Canvas = v }), ("Surface", s => s.Surface, (s, v) => s with { Surface = v }),
            ("Text", s => s.Text, (s, v) => s with { Text = v }), ("Accent", s => s.Accent, (s, v) => s with { Accent = v }),
            ("Detail", s => s.Detail, (s, v) => s with { Detail = v }),
        })
            ColorRows.Children.Add(ColorRow(T("TsColor" + key), T("TsColor" + key + "Hint"), get(spec), hex => Change(set(spec, hex))));
        GlassSlider.Value = Math.Round((1 - spec.CardOpacity) * 100); GlassValue.Text = $"{GlassSlider.Value:0}%";
        SpeedSlider.Value = spec.Backdrop.Speed * 100; SpeedValue.Text = $"{spec.Backdrop.Speed:0.##}×";
        IntensitySlider.Value = spec.Backdrop.Intensity * 100; IntensityValue.Text = $"{spec.Backdrop.Intensity * 100:0}%";
        GrainSwitch.IsOn = spec.Backdrop.Grain;
        BuildKinds(); BuildBackdropColors();
        filling = false;
    }

    private void Change(ThemeSpec next, bool now = false)
    {
        spec = next;
        if (now) { preview.Stop(); palette?.Preview(spec); PaintReadability(); }
        else { preview.Stop(); preview.Start(); }
    }

    // ---- Base and "start from one color" ----
    private void PaintBase()
    {
        foreach (var b in new[] { BaseDark, BaseLight })
        {
            var on = (string)b.Tag == (spec.Light ? "light" : "dark");
            b.Background = (Brush)Application.Current.Resources[on ? "AccentSoftBrush" : "SubtleBrush"];
            b.BorderBrush = (Brush)Application.Current.Resources[on ? "RoseBrush" : "CardBorderBrush"];
            b.BorderThickness = new Thickness(on ? 1.5 : 1);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, (string)b.Content + (on ? " ✓" : ""));
        }
    }
    // Switching dark/light rebuilds the colors from the current accent, so the theme stays coherent.
    private void OnBase(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        var light = tag == "light";
        if (light == spec.Light) return;
        var made = ThemeEngine.FromColor(ThemeColor.TryParse(spec.Accent, out var a) ? a : seed, light, spec.Name);
        Change(made with { Backdrop = made.Backdrop with { Kind = spec.Backdrop.Kind is "lotus" && light ? "gradient" : spec.Backdrop.Kind, Speed = spec.Backdrop.Speed, Grain = spec.Backdrop.Grain } }, now: true);
        Fill(); UiSound.Play(UiSound.Cue.Toggle);
    }
    private void OnGenerate(object sender, RoutedEventArgs e)
    {
        var made = ThemeEngine.FromColor(seed, spec.Light, spec.Name);
        // Keeps the chosen background type; only its colors follow the new theme.
        Change(made with { CardOpacity = spec.CardOpacity, Backdrop = made.Backdrop with { Kind = spec.Backdrop.Kind, Speed = spec.Backdrop.Speed, Grain = spec.Backdrop.Grain } }, now: true);
        Fill(); Motion.Pop(SeedGo); UiSound.Play(UiSound.Cue.Done);
    }

    // ---- Colors ----
    private FrameworkElement ColorRow(string label, string hint, string hex, Action<string> set)
    {
        var swatch = new Button { Width = 44, Height = 32, Padding = new Thickness(0), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1.5), BorderBrush = (Brush)Application.Current.Resources["CardBorderBrush"] };
        var code = new TextBlock { Text = hex, FontFamily = new FontFamily("Consolas"), FontSize = 12.5, Foreground = (Brush)Application.Current.Resources["MutedBrush"], VerticalAlignment = VerticalAlignment.Center };
        SetSwatch(swatch, hex);
        swatch.Flyout = PickerFlyout(hex, h => { SetSwatch(swatch, h); code.Text = h; set(h); });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(swatch, label);
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        info.Children.Add(new TextBlock { Text = label, FontSize = 13.5 });
        info.Children.Add(new TextBlock { Text = hint, FontSize = 11.5, Foreground = (Brush)Application.Current.Resources["MutedBrush"], TextWrapping = TextWrapping.Wrap });
        var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(10), Background = (Brush)Application.Current.Resources["SubtleBrush"] };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(swatch); Grid.SetColumn(info, 1); row.Children.Add(info); Grid.SetColumn(code, 2); row.Children.Add(code);
        return row;
    }

    private Flyout PickerFlyout(string hex, Action<string> changed)
    {
        var picker = new ColorPicker
        {
            Color = ToColor(hex), IsAlphaEnabled = false, IsMoreButtonVisible = false, IsColorSliderVisible = true, IsColorChannelTextInputVisible = false,
            IsHexInputVisible = true, ColorSpectrumShape = ColorSpectrumShape.Ring, Width = 300,
        };
        picker.ColorChanged += (_, args) => { if (!filling) changed($"#{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}"); };
        return new Flyout { Content = picker, Placement = FlyoutPlacementMode.Left };
    }
    private static void SetSwatch(Button b, string hex) => b.Background = new SolidColorBrush(ToColor(hex));
    private static Color ToColor(string hex) => ThemeColor.TryParse(hex, out var c) ? Color.FromArgb(255, c.R, c.G, c.B) : Color.FromArgb(255, 128, 128, 128);

    private void OnName(object sender, TextChangedEventArgs e) { if (!filling) spec = spec with { Name = NameBox.Text }; }
    private void OnGlass(object sender, RangeBaseValueChangedEventArgs e)
    {
        GlassValue.Text = $"{e.NewValue:0}%";
        if (!filling) Change(spec with { CardOpacity = 1 - e.NewValue / 100 });
    }

    // ---- Background ----
    private void BuildKinds()
    {
        KindChips.Children.Clear();
        foreach (var (id, glyph) in KindList)
        {
            var on = spec.Backdrop.Kind == id;
            var art = new Border { Height = 30, CornerRadius = new CornerRadius(6), Background = KindPreview(id) };
            art.Child = new FontIcon { Glyph = glyph, FontSize = 14, Foreground = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)) };
            var stack = new StackPanel { Spacing = 5 };
            stack.Children.Add(art);
            var label = new TextBlock { Text = T("TsKind" + id) + (on ? "  ✓" : ""), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal };
            stack.Children.Add(label);
            var b = new Button
            {
                Content = stack, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(7), CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = (Brush)Application.Current.Resources[on ? "AccentSoftBrush" : "SubtleBrush"], BorderBrush = (Brush)Application.Current.Resources[on ? "RoseBrush" : "CardBorderBrush"], BorderThickness = new Thickness(on ? 1.5 : 1),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, T("TsKind" + id) + (on ? " ✓" : ""));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(b, "theme-kind-" + id);
            var kind = id;
            b.Click += (_, _) => { Change(spec with { Backdrop = spec.Backdrop with { Kind = kind } }, now: true); BuildKinds(); PaintBackdropOptions(); UiSound.Play(UiSound.Cue.Toggle); };
            KindChips.Children.Add(b);
        }
        PaintBackdropOptions();
    }
    private Brush KindPreview(string id)
    {
        if (id == "none") return new SolidColorBrush(ToColor(spec.Canvas));
        if (id == "lotus") return (Brush)Application.Current.Resources["BrandPetalBrush"];
        var stops = new GradientStopCollection();
        var cs = spec.Backdrop.Colors;
        for (var i = 0; i < cs.Count; i++) stops.Add(new GradientStop { Color = ToColor(cs[i]), Offset = cs.Count == 1 ? 0 : (double)i / (cs.Count - 1) });
        return new LinearGradientBrush(stops, id == "aurora" ? 90 : id == "waves" ? 0 : 35);
    }
    private void PaintBackdropOptions()
    {
        var animated = spec.Backdrop.Kind is not ("none" or "lotus");
        BackdropOptions.Visibility = animated ? Visibility.Visible : Visibility.Collapsed;
        BackdropNote.Text = spec.Backdrop.Kind switch { "none" => T("TsKindNoneNote"), "lotus" => T("TsKindLotusNote"), _ => T("TsBackdropNote") };
    }
    private void BuildBackdropColors()
    {
        BackdropColors.Children.Clear();
        var colors = spec.Backdrop.Colors.ToList();
        for (var i = 0; i < colors.Count; i++)
        {
            var index = i;
            var b = new Button { Width = 40, Height = 40, Padding = new Thickness(0), CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(2), BorderBrush = (Brush)Application.Current.Resources["CardBorderBrush"] };
            SetSwatch(b, colors[i]);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, T("TsBackdropColors") + " " + (i + 1));
            b.Flyout = PickerFlyout(colors[i], h =>
            {
                SetSwatch(b, h);
                var list = spec.Backdrop.Colors.ToList(); if (index < list.Count) list[index] = h;
                Change(spec with { Backdrop = spec.Backdrop with { Colors = list } });
            });
            BackdropColors.Children.Add(b);
        }
        if (colors.Count < BackdropSpec.MaxColors) BackdropColors.Children.Add(SmallButton("", T("TsAddColor"), () =>
        {
            var last = ThemeColor.Parse(colors[^1]); var (h, s, l) = last.Hsl;
            Change(spec with { Backdrop = spec.Backdrop with { Colors = [.. colors, ThemeColor.FromHsl(h + 40, s, l).Hex] } }, now: true);
            BuildBackdropColors(); BuildKinds();
        }));
        if (colors.Count > BackdropSpec.MinColors) BackdropColors.Children.Add(SmallButton("", T("TsRemoveColor"), () =>
        {
            Change(spec with { Backdrop = spec.Backdrop with { Colors = [.. colors.Take(colors.Count - 1)] } }, now: true);
            BuildBackdropColors(); BuildKinds();
        }));
    }
    private static Button SmallButton(string glyph, string name, Action click)
    {
        var b = new Button { Width = 40, Height = 40, Padding = new Thickness(0), CornerRadius = new CornerRadius(20), Content = new FontIcon { Glyph = glyph, FontSize = 13 } };
        ToolTipService.SetToolTip(b, name); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, name);
        b.Click += (_, _) => click();
        return b;
    }
    private void OnSpeed(object sender, RangeBaseValueChangedEventArgs e)
    {
        SpeedValue.Text = $"{e.NewValue / 100:0.##}×";
        if (!filling) Change(spec with { Backdrop = spec.Backdrop with { Speed = e.NewValue / 100 } });
    }
    private void OnIntensity(object sender, RangeBaseValueChangedEventArgs e)
    {
        IntensityValue.Text = $"{e.NewValue:0}%";
        if (!filling) Change(spec with { Backdrop = spec.Backdrop with { Intensity = e.NewValue / 100 } });
    }
    private void OnGrain(object sender, RoutedEventArgs e) { if (!filling) Change(spec with { Backdrop = spec.Backdrop with { Grain = GrainSwitch.IsOn } }, now: true); }

    // ---- Readability ----
    private void PaintReadability()
    {
        var t = ThemeEngine.Derive(spec);
        var res = Application.Current.Resources;
        if (!t.WasAdjusted)
        {
            ReadIcon.Glyph = ""; ReadIcon.Foreground = (Brush)res["SuccessBrush"];
            ReadTitle.Text = T("TsReadGood"); ReadText.Text = T("TsReadGoodHint");
        }
        else
        {
            ReadIcon.Glyph = ""; ReadIcon.Foreground = (Brush)res["WarningBrush"];
            ReadTitle.Text = T("TsReadAdjusted");
            ReadText.Text = text?.Format("TsReadAdjustedHint", string.Join(", ", t.Adjusted.Select(a => T("TsPart" + a)))) ?? "";
        }
    }

    // ---- Footer ----
    private bool Dirty => ThemeEngine.ToCode(spec) != ThemeEngine.ToCode(start);
    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (prefs is null) return;
        spec = ThemeEngine.Normalize(spec with { Name = NameBox.Text });
        if (prefs.SaveMyTheme(spec) && prefs.UseTheme(spec)) { start = spec; UiSound.Play(UiSound.Cue.Done); Saved?.Invoke(this, EventArgs.Empty); Close(); }
    }
    private void OnCopy(object sender, RoutedEventArgs e)
    {
        var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
        data.SetText(ThemeEngine.ToCode(spec with { Name = NameBox.Text }));
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
        CopyLabel.Text = T("TsCopied"); Motion.Pop(CopyButton); UiSound.Play(UiSound.Cue.Toggle);
        var reset = DispatcherQueue.CreateTimer(); reset.Interval = TimeSpan.FromSeconds(2); reset.IsRepeating = false; reset.Tick += (_, _) => CopyLabel.Text = T("TsCopy"); reset.Start();
    }
    private void OnCancel(object sender, RoutedEventArgs e) => _ = CancelAsync();
    // Leaving with changes asks first; the theme in use comes back as it was.
    private async Task CancelAsync()
    {
        if (!IsOpen) return;
        if (Dirty)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, RequestedTheme = ThemePalette.Element, Title = T("TsDiscardTitle"), Content = T("TsDiscardBody"),
                PrimaryButtonText = T("TsDiscard"), CloseButtonText = T("TsKeepEditing"), DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }
        revert?.Invoke();
        Close();
    }
}
