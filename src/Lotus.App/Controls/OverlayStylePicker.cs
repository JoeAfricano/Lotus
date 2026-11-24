using System.ComponentModel;
using Lotus.Core.Integrations;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Lotus.App.Controls;

// Meus Jogos › Overlay: pick the overlay style by looking at it. Two styles (top bar, Neon card) as tiles with a mini
// drawing; the big preview follows the tile chosen, and one button turns that style on (with the usual confirmation).
// The preview is an illustration with example numbers, labeled as such; the layout files themselves are never rewritten.
public sealed class OverlayStylePicker : UserControl
{
    private IntegrationsViewModel? vm;
    private bool subscribed, renderQueued;
    private string selected = "top";
    private bool picked; // the player chose a tile; until then the selection follows the active layout
    private readonly Grid tileRow = new() { ColumnSpacing = 14, RowSpacing = 14 };
    private readonly Grid stage = new() { Height = 300, CornerRadius = new CornerRadius(16) };
    private readonly TextBlock status = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock title = new() { FontSize = 20, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock what = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock hint = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button use = new() { MinHeight = 44 };
    private readonly Button preview = new() { MinHeight = 44 };
    private readonly Button restore = new() { MinHeight = 44 };
    private readonly ToggleSwitch previewDetail = new();
    private readonly Dictionary<string,Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage>> previewImages = new();
    private int windowPreviewVersion;
    private readonly TextBlock message = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    // Show/hide key of the overlay, kept by RivaTuner (works with or without Lotus open).
    private readonly StackPanel hotkeyPanel = new() { Spacing = 8 };
    private readonly ComboBox hotkeyBox = new() { MinWidth = 220 };
    private readonly Button hotkeySave = new() { MinHeight = 36, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly TextBlock hotkeyNote = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private string? hotkeyShown; // the choice the list was last filled with
    private readonly StackPanel windowPanel = new() { Spacing = 14 };
    private readonly Image windowPreview = new() { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(14) };
    private readonly ProgressBar busy = new() { IsIndeterminate = true };
    private readonly Border statusPill = new() { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private static readonly Color Ok = Color.FromArgb(255, 0x7F, 0xD1, 0xA8);
    private readonly Color rose, gold, accent;

    public OverlayStylePicker()
    {
        var res = Application.Current.Resources;
        rose = res["RoseBrush"] is SolidColorBrush rb ? rb.Color : Color.FromArgb(255, 0xF2, 0x7B, 0xA8);
        gold = res["GoldBrush"] is SolidColorBrush gb ? gb.Color : Color.FromArgb(255, 0xEE, 0xC1, 0xA8);
        accent = res["AccentFillColorDefaultBrush"] is SolidColorBrush ab ? ab.Color : Color.FromArgb(255, 0xD6, 0x30, 0x6F);
        what.Foreground = message.Foreground = (Brush)res["MutedBrush"];
        message.Foreground = (Brush)res["GoldBrush"];
        use.Style = (Style)res["PrimaryButtonStyle"]; preview.Style = restore.Style = (Style)res["SoftButtonStyle"];
        Motion.SetPress(use, true);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(use, "overlay-use-style");
        use.Click += async (_, _) => { if (vm is not null && await Confirm(vm.Text["IntLayoutConfirmTitle"], vm.Text["IntLayoutConfirmBody"])) { await vm.UseStyleAsync(selected); picked = false; Render(); } };
        preview.Click += (_, _) => vm?.PreviewOverlay();
        restore.Click += async (_, _) => { if (vm is not null && await Confirm(vm.Text["IntLayoutRestoreTitle"], vm.Text["IntRestoreConfirmBody"])) { await vm.RestoreLayoutAsync(); Render(); } };
        statusPill.Child = status;
        hotkeySave.Style = (Style)res["SoftButtonStyle"]; hotkeyNote.Foreground = (Brush)res["MutedBrush"];
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(hotkeyBox, "overlay-hotkey");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(hotkeySave, "overlay-hotkey-save");
        hotkeyBox.SelectionChanged += (_, _) => UpdateHotkeySave();
        hotkeySave.Click += async (_, _) => { if (vm is not null && hotkeyBox.SelectedItem is ComboBoxItem { Tag: string key }) { await vm.SetOverlayHotkeyAsync(key); hotkeyShown = null; Render(); } };
        var hotkeyRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        hotkeyRow.Children.Add(hotkeyBox); hotkeyRow.Children.Add(hotkeySave);
        hotkeyPanel.Children.Add(hotkeyRow); hotkeyPanel.Children.Add(hotkeyNote);

        var head = new Grid { ColumnSpacing = 16 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new Border { Style = (Style)res["IconBadgeStyle"], Child = new FontIcon { Glyph = "", Style = (Style)res["BadgeIconStyle"] } });
        var words = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(title);
        hint.Style = (Style)res["CaptionStyle"];
        words.Children.Add(hint);
        Grid.SetColumn(words, 1); head.Children.Add(words);
        Grid.SetColumn(statusPill, 2); head.Children.Add(statusPill);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(use); actions.Children.Add(preview); actions.Children.Add(restore);
        var body = new StackPanel { Spacing = 16 };
        body.Children.Add(head);
        body.Children.Add(tileRow);
        body.Children.Add(previewDetail);
        body.Children.Add(stage);
        body.Children.Add(what);
        body.Children.Add(windowPanel);
        body.Children.Add(actions);
        body.Children.Add(hotkeyPanel);
        body.Children.Add(busy);
        body.Children.Add(message);
        Content = new Border { Style = (Style)res["CardStyle"], Child = body };
        tileRow.SizeChanged += (_, _) => ArrangeTiles();
        previewDetail.Toggled += (_, _) => Render();
        stage.SizeChanged += (_, _) => SizePreview();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(this, "overlay-style-picker");
        DataContextChanged += (_, _) => Attach(DataContext as IntegrationsViewModel);
        Loaded += (_, _) => { Subscribe(); vm?.Refresh(); QueueRender(); };
        Unloaded += (_, _) => Unsubscribe();
    }

    private void Attach(IntegrationsViewModel? model)
    {
        Unsubscribe();
        vm = model;
        if (IsLoaded) Subscribe();
        QueueRender();
    }
    private void Subscribe() { if (vm is not null && !subscribed) { vm.PropertyChanged += OnChanged; subscribed = true; } }
    private void Unsubscribe() { if (vm is not null && subscribed) { vm.PropertyChanged -= OnChanged; subscribed = false; } }
    private void OnChanged(object? sender, PropertyChangedEventArgs e) => QueueRender();
    private void QueueRender()
    {
        if (renderQueued || !IsLoaded) return;
        renderQueued = true;
        if (!DispatcherQueue.TryEnqueue(() => { renderQueued = false; if (IsLoaded) Render(); })) renderQueued = false;
    }

    private void Render()
    {
        if (vm is null) return;
        var t = vm.Text;
        if (!picked && vm.ActiveVariant is { } active) selected = active;
        title.Text = t["GmOverlayTitle"];
        hint.Text = t["OsPickHint"];
        var on = vm.ActiveVariant;
        status.Text = on is null ? t["OsNoneActive"] : t.Format("OsActive", t[Key(on)]);
        var c = on is null ? gold : Ok;
        statusPill.Background = new SolidColorBrush(Color.FromArgb(0x24, c.R, c.G, c.B));
        status.Foreground = new SolidColorBrush(c);

        tileRow.Children.Clear();
        tileRow.Children.Add(Tile("premium-top", t["OsPremiumTop"], t["OsPremiumTopShort"], on == "premium-top"));
        tileRow.Children.Add(Tile("premium-neon", t["OsPremiumNeon"], t["OsPremiumNeonShort"], on == "premium-neon"));
        tileRow.Children.Add(Tile("premium-pulse", t["OsPremiumPulse"], t["OsPremiumPulseShort"], on == "premium-pulse"));
        tileRow.Children.Add(Tile("window", t["OsWindow"], t["OsWindowShort"], on == "window"));
        tileRow.Children.Add(Tile("neon", t["OsNeon"], t["OsNeonShort"], on == "neon"));
        tileRow.Children.Add(Tile("lateral", t["OsLateral"], t["OsLateralShort"], on == "lateral"));
        tileRow.Children.Add(Tile("pulse", t["OsPulse"], t["OsPulseShort"], on == "pulse"));
        ArrangeTiles();

        stage.Children.Clear();
        stage.Children.Add(GameScene());
        previewDetail.OnContent=t["OsPreviewDetail"];previewDetail.OffContent=t["OsPreviewScreen"];
        previewDetail.Visibility=selected=="window"?Visibility.Collapsed:Visibility.Visible;
        SizePreview(); stage.MinHeight = 300;
        if (selected == "window") { stage.Children.Add(windowPreview); _ = RenderWindowPreviewAsync(); }
        else stage.Children.Add(Drawing(selected, 1.0));
        stage.Children.Add(new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(14), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(Color.FromArgb(0xB3, 18, 9, 15)),
            Child = new TextBlock { Text = t["OsExample"], FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 255, 255, 255)) } });

        what.Inlines.Clear();
        what.Text = t[Key(selected) + "What"];
        // The window style depends on the display keeping the game on its own plane: say so when it cannot.
        if (selected == "window" && vm.DisplayMpo is Lotus.Core.Benchmark.MpoStatus.NoOverlayPlane or Lotus.Core.Benchmark.MpoStatus.SwitchedOff)
            what.Text += "\n\n" + t["Mpo" + vm.DisplayMpo] + " " + t["OsWindowPreferPulse"];
        RenderWindowPanel(t);
        var isOn = on == selected;
        use.Content = Label(isOn ? "" : "", t.Format(isOn ? "OsInUse" : "OsUse", t[Key(selected)]));
        use.IsEnabled = !isOn && vm.RtssInstalled && !vm.Busy;
        preview.Content = Label("", t["IntPreview"]);
        preview.Visibility = vm.RtssInstalled && vm.CanPreview ? Visibility.Visible : Visibility.Collapsed;
        restore.Content = t["IntLayoutRestore"];
        restore.Visibility = vm.CanRestoreLayout ? Visibility.Visible : Visibility.Collapsed;
        busy.Visibility = vm.Busy ? Visibility.Visible : Visibility.Collapsed;
        RenderHotkey(t);
        // "Ver o overlay agora" shows what RivaTuner has loaded, not the tile being looked at: say so when they differ.
        message.Text = !vm.RtssInstalled ? t["OsNeedRtss"] : vm.Message.Length > 0 ? vm.Message : !isOn ? vm.PendingVariant is { } pend ? t.Format("OsPendingName", t[Key(pend)]) : t.Format("OsPreviewActive", on is null ? t["OsNoneShort"] : t[Key(on)]) : "";
        message.Visibility = message.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderHotkey(Lotus.Core.ITextCatalog t)
    {
        hotkeyPanel.Visibility = vm!.RtssInstalled && selected != "window" ? Visibility.Visible : Visibility.Collapsed;
        hotkeyBox.Header = t["OsHotkeyTitle"];
        if (hotkeyShown != vm.OverlayHotkey) // refill only when RivaTuner's key changed, so a pending pick is kept
        {
            hotkeyShown = vm.OverlayHotkey;
            hotkeyBox.Items.Clear();
            foreach (var key in Lotus.Core.Integrations.OverlayHotkeys.Choices)
                hotkeyBox.Items.Add(new ComboBoxItem { Content = key == Lotus.Core.Integrations.OverlayHotkeys.None ? t["OsHotkeyNone"] : Lotus.Core.Integrations.OverlayHotkeys.Describe(Lotus.Core.Integrations.OverlayHotkeys.Code(key)!.Value), Tag = key });
            if (vm.OverlayHotkey == Lotus.Core.Integrations.OverlayHotkeys.Custom)
                hotkeyBox.Items.Insert(0, new ComboBoxItem { Content = t.Format("OsHotkeyCustom", vm.OverlayHotkeyText ?? ""), Tag = Lotus.Core.Integrations.OverlayHotkeys.Custom });
            hotkeyBox.SelectedIndex = hotkeyBox.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string)i.Tag == vm.OverlayHotkey);
        }
        hotkeySave.Content = t["OsHotkeySave"];
        hotkeyNote.Text = t["OsHotkeyHint"] + (vm.AfterburnerHotkeyText is { } ab ? " " + t.Format("OsHotkeyAfterburner", ab) : "");
        UpdateHotkeySave();
    }
    private void UpdateHotkeySave() => hotkeySave.IsEnabled = vm is { Busy: false } && hotkeyBox.SelectedItem is ComboBoxItem { Tag: string key } && key != vm.OverlayHotkey && key != Lotus.Core.Integrations.OverlayHotkeys.Custom;
    private void ArrangeTiles()
    {
        int columns = Math.Clamp((int)((tileRow.ActualWidth > 0 ? tileRow.ActualWidth : 1000) / 230), 1, 4);
        int rows = (int)Math.Ceiling(tileRow.Children.Count / (double)columns);
        if (tileRow.ColumnDefinitions.Count != columns || tileRow.RowDefinitions.Count != rows)
        {
            tileRow.ColumnDefinitions.Clear(); tileRow.RowDefinitions.Clear();
            for (int i=0;i<columns;i++) tileRow.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i=0;i<rows;i++) tileRow.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        }
        for (int i=0;i<tileRow.Children.Count;i++) { Grid.SetColumn((FrameworkElement)tileRow.Children[i],i%columns); Grid.SetRow((FrameworkElement)tileRow.Children[i],i/columns); }
    }
    private static string Key(string variant) => variant switch { "premium-top" => "OsPremiumTop", "premium-neon" => "OsPremiumNeon", "premium-pulse" => "OsPremiumPulse", "neon" => "OsNeon", "lateral" => "OsLateral", "pulse" => "OsPulse", "window" => "OsWindow", _ => "OsTop" };
    private void SizePreview()
    {
        double height=selected=="window"?300:previewDetail.IsOn?360:Math.Clamp((stage.ActualWidth>0?stage.ActualWidth:960)*9/16,300,420);
        if(double.IsNaN(stage.Height)||Math.Abs(stage.Height-height)>1)stage.Height=height;
    }
    private FrameworkElement Drawing(string variant, double scale) => variant=="window"
        ? new Viewbox { Stretch = Stretch.Uniform, Margin = new Thickness(4), Child = WindowCard(1) }
        : NativeLayoutPreview(variant,scale<1);
    private FrameworkElement NativeLayoutPreview(string variant,bool mini)
    {
        var image=new Image { Stretch=Stretch.Uniform,IsHitTestVisible=false };
        var view=new Viewbox { Stretch=Stretch.Uniform,StretchDirection=mini?StretchDirection.Both:StretchDirection.DownOnly,Margin=new Thickness(mini?4:16) };
        Canvas? scene=null;
        // Tiles show the complete overlay at a readable size; the large overview shows its true screen position.
        if(!mini&&!previewDetail.IsOn)
        {
            scene=new Canvas { Width=1920,Height=1080 };
            var backdrop=GameScene();backdrop.Width=1920;backdrop.Height=1080;
            backdrop.Background=HomeMotion.Gradient(new(0,0),new(0,1),(0,Color.FromArgb(255,8,17,31)),(1,Color.FromArgb(255,22,32,47)));
            scene.Children.Add(backdrop);
            var screenLabel=new TextBlock { Text="1920 × 1080",FontSize=24,Foreground=new SolidColorBrush(Color.FromArgb(255,170,188,212)) };
            Canvas.SetLeft(screenLabel,36);Canvas.SetTop(screenLabel,1014);scene.Children.Add(screenLabel);
            scene.Children.Add(image);view.Child=scene;
        }
        else view.Child=image;
        _=LoadLayoutPreviewAsync(variant,image,scene);
        if(scene is null)return view;
        return new Border { BorderThickness=new Thickness(2),BorderBrush=new SolidColorBrush(Color.FromArgb(255,0x84,0x91,0xAC)),CornerRadius=new CornerRadius(10),Padding=new Thickness(8),Margin=new Thickness(14),Background=new SolidColorBrush(Color.FromArgb(255,8,12,22)),Child=view };
    }
    private async Task LoadLayoutPreviewAsync(string variant,Image image,Canvas? scene)
    {
        try
        {
            var frame=await Task.Run(()=>Lotus.Infrastructure.Integrations.RtssLayoutPreview.Render(variant));
            image.Width=frame.Width;image.Height=frame.Height;
            if(scene is not null){Canvas.SetLeft(image,frame.X);Canvas.SetTop(image,frame.Y);}
            if(!previewImages.TryGetValue(frame.Key,out var source))
            {
                if(previewImages.Count>32)previewImages.Clear();
                source=LoadBitmapAsync(frame.Png);previewImages[frame.Key]=source;
            }
            image.Source=await source;
        }
        catch(Exception ex){Serilog.Log.Warning(ex,"Native overlay preview {Variant}",variant);image.Visibility=Visibility.Collapsed;}
    }
    private static async Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage> LoadBitmapAsync(byte[] bytes)
    {
        var bitmap=new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
        using var stream=new MemoryStream(bytes);await bitmap.SetSourceAsync(stream.AsRandomAccessStream());return bitmap;
    }
    // Lotus (janela): the same numbers in a rounded card drawn by Lotus itself (real fonts, accents, gradient border).
    private FrameworkElement WindowCard(double scale)
    {
        var card = (Border)PulsePanel(scale);
        card.CornerRadius = new CornerRadius(14 * scale); card.BorderThickness = new Thickness(Math.Max(1, 1.5 * scale));
        card.BorderBrush = HomeMotion.Gradient(new(0, 0), new(1, 1), (0, rose), (1, gold));
        card.Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x14, 0x0B, 0x14));
        return card;
    }
    // Lotus Pulse: big FPS with the lows as pills, the frametime graph with its reference lines, bars below (theme colors).
    private FrameworkElement PulsePanel(double scale)
    {
        var panel = new Border { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12 * scale), Width = 190 * scale, Padding = new Thickness(12 * scale, 10 * scale, 10 * scale, 10 * scale),
            Background = new SolidColorBrush(Color.FromArgb(0xA6, 0x12, 0x0A, 0x12)), BorderBrush = new SolidColorBrush(rose), BorderThickness = new Thickness(Math.Max(1, 3 * scale), 0, 0, 0) };
        var p = new StackPanel { Spacing = 5 * scale };
        p.Children.Add(new TextBlock { Text = "LOTUS", FontSize = Math.Max(5, 10 * scale), FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(gold), CharacterSpacing = 120 });
        var top = new Grid(); top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(new TextBlock { Text = "75", FontSize = Math.Max(10, 36 * scale), FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Colors.White) });
        var pills = new StackPanel { Spacing = 4 * scale, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (k, v, a) in new[] { ("1%", "71", (byte)0x59), ("0.1%", "60", (byte)0x33) })
            pills.Children.Add(new Border { CornerRadius = new CornerRadius(6 * scale), Padding = new Thickness(6 * scale, 1, 6 * scale, 1), Background = new SolidColorBrush(Color.FromArgb(a, rose.R, rose.G, rose.B)),
                Child = new TextBlock { Text = k + "  " + v, FontSize = Math.Max(5, 10 * scale), FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White) } });
        Grid.SetColumn(pills, 1); top.Children.Add(pills);
        p.Children.Add(top);
        p.Children.Add(new TextBlock { Text = "FPS   13.3 ms", FontSize = Math.Max(5, 10 * scale), FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(rose) });
        // Graph: reference lines (33.3, 16.7 and the limit) and a nearly flat frametime line.
        var graph = new Grid { Height = 44 * scale };
        foreach (var (y, a) in new[] { (0.0, (byte)0x40), (0.5, (byte)0x40), (0.6, (byte)0x99) })
            graph.Children.Add(new Rectangle { Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, y * 44 * scale, 0, 0), Fill = new SolidColorBrush(a == 0x99 ? Color.FromArgb(a, rose.R, rose.G, rose.B) : Color.FromArgb(a, 0xBE, 0xB8, 0xC1)) });
        var line = new Polyline { Stroke = new SolidColorBrush(rose), StrokeThickness = Math.Max(1, 1.5 * scale) };
        var w = 160 * scale;
        for (var i = 0; i <= 24; i++) line.Points.Add(new Windows.Foundation.Point(w * i / 24, 44 * scale * (0.6 + (i == 15 ? -0.18 : (i % 3 - 1) * 0.02))));
        graph.Children.Add(line);
        p.Children.Add(graph);
        foreach (var (k, share) in new[] { ("GPU", 0.71), ("CPU", 0.38) })
        {
            p.Children.Add(new TextBlock { Text = k + "  " + (int)(share * 100) + "%  60\u00B0C", FontSize = Math.Max(5, 10 * scale), Foreground = new SolidColorBrush(Colors.White) });
            var track = new Grid { Height = Math.Max(2, 4 * scale), Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)) };
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) }); track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - share, GridUnitType.Star) });
            track.Children.Add(new Rectangle { Fill = new SolidColorBrush(rose) });
            p.Children.Add(track);
        }
        panel.Child = p;
        return panel;
    }
    // Lotus Lateral: a slim column in the top-left corner, everything stacked.
    private FrameworkElement LateralColumn(double scale)
    {
        var col = new Border { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12 * scale), Width = 150 * scale, Padding = new Thickness(12 * scale, 10 * scale, 10 * scale, 10 * scale),
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0x12, 0x0A, 0x12)), BorderBrush = new SolidColorBrush(rose), BorderThickness = new Thickness(Math.Max(1, 3 * scale), 0, 0, 0) };
        var p = new StackPanel { Spacing = 4 * scale };
        p.Children.Add(new TextBlock { Text = "LOTUS", FontSize = Math.Max(5, 10 * scale), FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(gold), CharacterSpacing = 120 });
        var fps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 * scale };
        fps.Children.Add(new TextBlock { Text = "144", FontSize = Math.Max(10, 34 * scale), FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Colors.White) });
        fps.Children.Add(new TextBlock { Text = "FPS", FontSize = Math.Max(5, 11 * scale), FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(rose), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 5 * scale) });
        p.Children.Add(fps);
        foreach (var (k, v) in new[] { ("ms", "6.9   1% 118"), ("GPU", "71%  64°"), ("CPU", "38%  58°"), ("VRAM", "52%"), ("RAM", "61%") })
        {
            var row = new Grid { ColumnSpacing = 6 * scale }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34 * scale) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = k, FontSize = Math.Max(5, 10 * scale), FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(gold) });
            var val = new TextBlock { Text = v, FontSize = Math.Max(5, 11 * scale), Foreground = new SolidColorBrush(Colors.White) }; Grid.SetColumn(val, 1); row.Children.Add(val);
            p.Children.Add(row);
        }
        col.Child = p;
        return col;
    }
    // ---- Janela Lotus: profiles, items (on/off and order), corner, opacity and size; the preview is the real renderer ----
    private void RenderWindowPanel(Lotus.Core.ITextCatalog t)
    {
        windowPanel.Children.Clear();
        windowPanel.Visibility = selected == "window" && vm?.WindowSettings is not null ? Visibility.Visible : Visibility.Collapsed;
        if (windowPanel.Visibility != Visibility.Visible) return;
        var s = vm!.WindowSettings!();
        void Change(Func<Lotus.Core.Benchmark.OverlayWindowSettings, Lotus.Core.Benchmark.OverlayWindowSettings> f, bool rebuild)
        {
            vm.ChangeWindowSettings?.Invoke(f);
            if (rebuild) Render(); else _ = RenderWindowPreviewAsync();
        }

        // Profiles: three slots, each keeps its own choices.
        var profiles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var current = vm.WindowProfile?.Invoke() ?? 0;
        for (var i = 0; i < 3; i++)
        {
            var index = i;
            var b = new Button { Content = t["OwProfile" + (i + 1)], Style = (Style)Application.Current.Resources[i == current ? "PrimaryButtonStyle" : "SoftButtonStyle"], MinHeight = 36 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(b, "ow-profile-" + (i + 1));
            b.Click += (_, _) => { vm.UseWindowProfile?.Invoke(index); Render(); };
            profiles.Children.Add(b);
        }
        windowPanel.Children.Add(Section(t["OwProfiles"], profiles));

        // Corner, opacity and size.
        var place = new Grid { ColumnSpacing = 16 };
        for (var c = 0; c < 3; c++) place.ColumnDefinitions.Add(new ColumnDefinition());
        var corner = new ComboBox { Header = t["OwPosition"], HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var p in Lotus.Core.Benchmark.OverlayWindowSettings.Positions) corner.Items.Add(new ComboBoxItem { Content = t["OwPos" + p.Replace("-", "")], Tag = p });
        corner.SelectedIndex = Array.IndexOf(Lotus.Core.Benchmark.OverlayWindowSettings.Positions, s.Position);
        corner.SelectionChanged += (_, _) => { if (corner.SelectedItem is ComboBoxItem { Tag: string p }) Change(x => x with { Position = p }, false); };
        place.Children.Add(corner);
        var opacity = new Slider { Header = t["OwOpacity"], Minimum = 40, Maximum = 100, StepFrequency = 5, Value = Math.Round(s.Opacity * 100) };
        opacity.ValueChanged += (_, e) => Change(x => x with { Opacity = e.NewValue / 100 }, false);
        Grid.SetColumn(opacity, 1); place.Children.Add(opacity);
        var zoom = new Slider { Header = t["OwSize"], Minimum = 75, Maximum = 150, StepFrequency = 5, Value = Math.Round(s.Zoom * 100) };
        zoom.ValueChanged += (_, e) => Change(x => x with { Zoom = e.NewValue / 100 }, false);
        Grid.SetColumn(zoom, 2); place.Children.Add(zoom);
        windowPanel.Children.Add(place);
        var visibility = new StackPanel { Orientation=Orientation.Horizontal, Spacing=12 };
        var show = new Button { Content=t[s.Visible ? "OwHide" : "OwShow"],Style=(Style)Application.Current.Resources["SoftButtonStyle"] };
        show.Click += (_,_) => Change(x=>x with {Visible=!x.Visible},true);
        visibility.Children.Add(show);
        var shortcut = new ComboBox { Header=t["OwToggleShortcut"],MinWidth=180 };
        foreach(var key in Lotus.Core.Benchmark.OverlayWindowSettings.ToggleKeys) shortcut.Items.Add(new ComboBoxItem {Content=key=="none"?t["OwNoShortcut"]:"Ctrl + Alt + "+key,Tag=key});
        shortcut.SelectedIndex=Array.IndexOf(Lotus.Core.Benchmark.OverlayWindowSettings.ToggleKeys,s.ToggleKey);
        shortcut.SelectionChanged+=(_,_)=>{if(shortcut.SelectedItem is ComboBoxItem {Tag:string key}) Change(x=>x with {ToggleKey=key},false);};
        visibility.Children.Add(shortcut);windowPanel.Children.Add(visibility);

        // Items: the shown ones in their order (with up/down), then the hidden ones.
        var items = new StackPanel { Spacing = 4 };
        var shown = s.Items.ToList();
        foreach (var id in shown.Concat(Lotus.Core.Benchmark.OverlayWindowSettings.AllItems.Except(shown)))
        {
            var on = shown.Contains(id); var at = shown.IndexOf(id);
            var row = new Grid { ColumnSpacing = 6 };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var check = new CheckBox { Content = t["OwItem" + id], IsChecked = on };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(check, "ow-item-" + id);
            check.Click += (_, _) => Change(x => x.Toggle(id), true);
            row.Children.Add(check);
            if (on)
            {
                var up = new Button { Content = new FontIcon { Glyph = "", FontSize = 12 }, IsEnabled = at > 0, Style = (Style)Application.Current.Resources["SoftButtonStyle"], Padding = new Thickness(10, 6, 10, 6) };
                up.Click += (_, _) => Change(x => x.Move(id, -1), true);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(up, t.Format("OwMoveUp", t["OwItem" + id]));
                var down = new Button { Content = new FontIcon { Glyph = "", FontSize = 12 }, IsEnabled = at < shown.Count - 1, Style = (Style)Application.Current.Resources["SoftButtonStyle"], Padding = new Thickness(10, 6, 10, 6) };
                down.Click += (_, _) => Change(x => x.Move(id, 1), true);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(down, t.Format("OwMoveDown", t["OwItem" + id]));
                Grid.SetColumn(up, 1); Grid.SetColumn(down, 2); row.Children.Add(up); row.Children.Add(down);
            }
            items.Children.Add(row);
        }
        windowPanel.Children.Add(Section(t["OwItems"], items));
        windowPanel.Children.Add(new TextBlock { Text = t["OwHotkey"], FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["MutedBrush"] });
    }

    private static StackPanel Section(string title, UIElement content)
    {
        var p = new StackPanel { Spacing = 8 };
        p.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        p.Children.Add(content);
        return p;
    }

    // Draws the overlay exactly as the game window would (example numbers) and shows it in the stage.
    private async Task RenderWindowPreviewAsync()
    {
        var version = ++windowPreviewVersion;
        await Task.Delay(60); // Coalesce slider changes; an older render must never overwrite the newest choice.
        if (version != windowPreviewVersion || selected != "window" || !IsLoaded) return;
        if (vm?.WindowSettings is not { } settings) return;
        var s = settings();
        var look = new Lotus.Infrastructure.Games.GameOverlayWindow.Look(System.Drawing.Color.FromArgb(255, rose.R, rose.G, rose.B), System.Drawing.Color.FromArgb(255, gold.R, gold.G, gold.B),
            74.97, vm.Text["OwAverage"], "—", s, System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Lotus.ico"));
        var frames = new Lotus.Core.Benchmark.OverlayFrames();
        var random = new Random(7);
        for (var i = 0; i < 2000; i++) frames.Add(13.34 + (random.NextDouble() - 0.5) * 0.6 + (i % 240 == 0 ? 6 : 0));
        var sensors = new Lotus.Infrastructure.Games.GameOverlayWindow.Sensors(71, 64, 38, null, 52, 61);
        var bytes = await Task.Run(() =>
        {
            var h = Lotus.Infrastructure.Games.GameOverlayWindow.Measure(s);
            var scale = s.Zoom;
            using var bmp = new System.Drawing.Bitmap((int)Math.Ceiling(Lotus.Infrastructure.Games.GameOverlayWindow.Width * scale), (int)Math.Ceiling(h * scale), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.ScaleTransform((float)scale, (float)scale);
                using var logo = File.Exists(look.LogoPath) ? new System.Drawing.Icon(look.LogoPath!, 32, 32).ToBitmap() : null;
                Lotus.Infrastructure.Games.GameOverlayWindow.Draw(g, frames.Summarize(), sensors, look, logo);
            }
            using var ms = new MemoryStream(); bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png); return ms.ToArray();
        });
        var image = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
        using var stream = new MemoryStream(bytes);
        await image.SetSourceAsync(stream.AsRandomAccessStream());
        if (version != windowPreviewVersion || selected != "window" || !IsLoaded) return;
        windowPreview.Width = Math.Min(300, Lotus.Infrastructure.Games.GameOverlayWindow.Width * s.Zoom);
        windowPreview.MaxHeight = 268;
        windowPreview.Source = image;
        windowPreview.HorizontalAlignment = s.Position is "top-right" or "bottom-right" ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        windowPreview.VerticalAlignment = s.Position is "bottom-left" or "bottom-right" ? VerticalAlignment.Bottom : VerticalAlignment.Top;
    }

    private static StackPanel Label(string glyph, string text)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        p.Children.Add(new FontIcon { Glyph = glyph, FontSize = 13 });
        p.Children.Add(new TextBlock { Text = text });
        return p;
    }

    // A style tile: a small drawing of the style, its name and one line; gold border when chosen, green badge when active.
    private Button Tile(string id, string name, string line, bool active)
    {
        var chosen = id == selected;
        var mini = new Grid { Height = 96, CornerRadius = new CornerRadius(10) };
        mini.Children.Add(GameScene());
        mini.Children.Add(Drawing(id, 0.42));
        var info = new StackPanel { Spacing = 2 };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        nameRow.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, FontSize = 14 });
        if (id == "window") nameRow.Children.Add(new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 1, 8, 1), Background = new SolidColorBrush(Color.FromArgb(0x2E, gold.R, gold.G, gold.B)), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = vm!.Text["OsExperimental"], FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(gold) } });
        if (active) nameRow.Children.Add(new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 1, 8, 1), Background = new SolidColorBrush(Color.FromArgb(0x2E, Ok.R, Ok.G, Ok.B)), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = vm!.Text["OsActiveBadge"], FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Ok) } });
        info.Children.Add(nameRow);
        info.Children.Add(new TextBlock { Text = line, FontSize = 12, Foreground = (Brush)Application.Current.Resources["MutedBrush"], TextWrapping = TextWrapping.Wrap });
        var stack = new StackPanel { Spacing = 10 }; stack.Children.Add(mini); stack.Children.Add(info);
        var tile = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(12), CornerRadius = new CornerRadius(14), HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = stack,
            Background = (Brush)Application.Current.Resources[chosen ? "CardHoverBrush" : "CardBrush"],
            BorderThickness = new Thickness(chosen ? 2 : 1),
            BorderBrush = chosen ? HomeMotion.Gradient(new(0, 0), new(1, 1), (0, rose), (1, gold)) : (Brush)Application.Current.Resources["CardBorderBrush"],
        };
        Motion.SetHover(tile, 1.02); Motion.SetPress(tile, true);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(tile, "overlay-style-" + id);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tile, name);
        tile.Click += (_, _) => { selected = id; picked = true; Render(); };
        return tile;
    }

    // A dark "game" backdrop so the overlay reads like it would over a game.
    private Grid GameScene()
    {
        var g = new Grid { Background = HomeMotion.Gradient(new(0, 0), new(1, 1), (0, Color.FromArgb(255, 0x24, 0x10, 0x2A)), (0.55, Color.FromArgb(255, 0x12, 0x0A, 0x1C)), (1, Color.FromArgb(255, 0x3A, 0x14, 0x22))) };
        g.Children.Add(new Ellipse { Width = 420, Height = 420, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -260), Fill = new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B)) });
        return g;
    }

    // Lotus Top: one slim bar across the top with every metric.
    private FrameworkElement TopBar(double scale)
    {
        var bar = new Border { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12 * scale, 12 * scale, 12 * scale, 0), CornerRadius = new CornerRadius(8 * scale), Padding = new Thickness(12 * scale, 6 * scale, 12 * scale, 6 * scale),
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x12, 0x09, 0x0F)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, rose.R, rose.G, rose.B)), BorderThickness = new Thickness(1) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 * scale };
        foreach (var (k, v, hi) in new[] { ("FPS", "144", true), ("1% LOW", "118", false), ("0.1%", "96", false), ("MS", "6.9", false), ("GPU", "71% 64°", false), ("CPU", "38% 58°", false), ("RAM", "9.8 GB", false) })
        {
            var cell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 * scale };
            cell.Children.Add(new TextBlock { Text = k, FontSize = Math.Max(5, 11 * scale), FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(gold), VerticalAlignment = VerticalAlignment.Center });
            cell.Children.Add(new TextBlock { Text = v, FontSize = Math.Max(6, (hi ? 18 : 13) * scale), FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(hi ? rose : Colors.White), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(cell);
        }
        bar.Child = row;
        return bar;
    }

    // Lotus Neon: a card in the corner with the big FPS, the lows and usage bars.
    private FrameworkElement NeonCard(double scale)
    {
        var card = new Border { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(14 * scale), Width = 220 * scale, CornerRadius = new CornerRadius(12 * scale), Padding = new Thickness(14 * scale),
            Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x12, 0x09, 0x0F)), BorderBrush = HomeMotion.Gradient(new(0, 0), new(1, 1), (0, rose), (1, gold)), BorderThickness = new Thickness(Math.Max(1, 1.5 * scale)) };
        var p = new StackPanel { Spacing = 6 * scale };
        var fps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 * scale };
        fps.Children.Add(new TextBlock { Text = "144", FontSize = Math.Max(10, 40 * scale), FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(rose) });
        fps.Children.Add(new TextBlock { Text = "FPS", FontSize = Math.Max(5, 12 * scale), FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(gold), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6 * scale) });
        p.Children.Add(fps);
        p.Children.Add(new TextBlock { Text = "1% 118  ·  0.1% 96  ·  6.9 ms", FontSize = Math.Max(5, 11 * scale), Foreground = new SolidColorBrush(Colors.White) });
        foreach (var (k, share) in new[] { ("GPU", 0.71), ("CPU", 0.38), ("RAM", 0.61) })
        {
            var line = new Grid { ColumnSpacing = 8 * scale };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30 * scale) }); line.ColumnDefinitions.Add(new ColumnDefinition());
            line.Children.Add(new TextBlock { Text = k, FontSize = Math.Max(5, 10 * scale), FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(gold) });
            var track = new Grid { Height = Math.Max(2, 6 * scale), CornerRadius = new CornerRadius(3 * scale), VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)) };
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) }); track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - share, GridUnitType.Star) });
            track.Children.Add(new Rectangle { Fill = HomeMotion.Gradient(new(0, 0), new(1, 0), (0, accent), (1, rose)) });
            Grid.SetColumn(track, 1); line.Children.Add(track);
            p.Children.Add(line);
        }
        card.Child = p;
        return card;
    }

    private async Task<bool> Confirm(string head, string body)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = head, RequestedTheme = ThemePalette.Element, DefaultButton = ContentDialogButton.Close,
            Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 },
            PrimaryButtonText = vm!.Text["FeatConfirm"], CloseButtonText = vm.Text["DlgCancel"]
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
