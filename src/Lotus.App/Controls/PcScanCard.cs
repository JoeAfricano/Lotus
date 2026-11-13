using System.ComponentModel;
using Lotus.Core;
using Lotus.Core.Maintenance;
using Lotus.Core.Telemetry;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Lotus.App.Controls;

// "Raio-X para jogar" (Otimizar meu PC): one button runs the memory measurement and the whole read-only Check-up together.
// Live tiles show CPU, GPU, RAM and GPU temperature; the result is a prioritized list of suggestions, each opening
// the tool that can act on it. Analyzing never changes Windows, and nothing here shows a number that was not measured.
public sealed class PcScanCard : UserControl
{
    private SectionViewModel? vm;
    private ExperienceViewModel? experience;
    private CheckupViewModel? checkup;
    private MonitoringViewModel? monitoring;
    private readonly TextBlock eyebrow = new() { FontSize = 12, FontWeight = FontWeights.Bold, CharacterSpacing = 160 };
    private readonly TextBlock title = new() { FontSize = 30, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, CharacterSpacing = -10 };
    private readonly TextBlock body = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button run = new() { MinHeight = 48, MinWidth = 210, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button cancel = new() { MinHeight = 40, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly StackPanel progress = new() { Spacing = 8 };
    private readonly TextBlock step = new() { FontSize = 12, FontWeight = FontWeights.SemiBold };
    private readonly ProgressBar bar = new() { Minimum = 0, Maximum = 100, Height = 6, CornerRadius = new CornerRadius(3) };
    private readonly Grid tiles = new() { ColumnSpacing = 12, RowSpacing = 12 };
    private readonly Border verdict = new() { CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12, 16, 12) };
    private readonly TextBlock verdictText = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel results = new() { Spacing = 10 };
    private readonly StackPanel chips = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly StackPanel list = new() { Spacing = 8 };
    private readonly TextBlock safe = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private bool showGood;
    private readonly Color rose, gold, accent;
    private static readonly Color Muted = Color.FromArgb(255, 0xC9, 0xB6, 0xC0), Warn = Color.FromArgb(255, 0xF0, 0xC2, 0x7D), Ok = Color.FromArgb(255, 0x7F, 0xD1, 0xA8);
    public event EventHandler<string>? NavigateRequested;

    public PcScanCard()
    {
        RequestedTheme = ElementTheme.Dark; // the same dark glass as the Home readiness card, in every theme
        var res = Application.Current.Resources;
        rose = res["RoseBrush"] is SolidColorBrush rb ? rb.Color : Color.FromArgb(255, 0xF2, 0x7B, 0xA8);
        gold = res["GoldBrush"] is SolidColorBrush gb ? gb.Color : Color.FromArgb(255, 0xEE, 0xC1, 0xA8);
        accent = res["AccentFillColorDefaultBrush"] is SolidColorBrush ab ? ab.Color : Color.FromArgb(255, 0xD6, 0x30, 0x6F);
        eyebrow.Foreground = new SolidColorBrush(gold);
        title.Foreground = new SolidColorBrush(Colors.White);
        body.Foreground = safe.Foreground = new SolidColorBrush(Muted);
        step.Foreground = new SolidColorBrush(gold);
        verdictText.Foreground = new SolidColorBrush(Colors.White);
        run.Style = (Style)res["PrimaryButtonStyle"]; Motion.SetPress(run, true);
        cancel.Style = (Style)res["SoftButtonStyle"];
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(run, "scan-run");
        run.Click += async (_, _) => await RunAsync();
        cancel.Click += (_, _) => experience?.CancelCommand.Execute(null);

        var head = new Grid { ColumnSpacing = 24 };
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var words = new StackPanel { Spacing = 8 };
        var eyebrowRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        eyebrowRow.Children.Add(new Rectangle { Width = 24, Height = 2, VerticalAlignment = VerticalAlignment.Center, Fill = new SolidColorBrush(rose) });
        eyebrowRow.Children.Add(eyebrow);
        words.Children.Add(eyebrowRow); words.Children.Add(title); words.Children.Add(body);
        head.Children.Add(words);
        var buttons = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(run); buttons.Children.Add(cancel);
        Grid.SetColumn(buttons, 1); head.Children.Add(buttons);

        progress.Children.Add(step); progress.Children.Add(bar);
        for (var i = 0; i < 4; i++) tiles.ColumnDefinitions.Add(new ColumnDefinition());
        var verdictRow = new Grid { ColumnSpacing = 12 };
        verdictRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); verdictRow.ColumnDefinitions.Add(new ColumnDefinition());
        verdictRow.Children.Add(new FontIcon { Glyph = "", FontSize = 18, Foreground = new SolidColorBrush(gold), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(verdictText, 1); verdictRow.Children.Add(verdictText);
        verdict.Child = verdictRow;
        results.Children.Add(chips); results.Children.Add(list);

        var stack = new StackPanel { Spacing = 20 };
        stack.Children.Add(head); stack.Children.Add(progress); stack.Children.Add(tiles); stack.Children.Add(verdict); stack.Children.Add(results); stack.Children.Add(safe);
        var card = new Border
        {
            Padding = new Thickness(28), CornerRadius = new CornerRadius(22), BorderThickness = new Thickness(1), Child = stack,
            Background = HomeMotion.Gradient(new(0, 0), new(1, 1), (0, Color.FromArgb(0xF2, 0x1E, 0x0C, 0x18)), (0.6, Color.FromArgb(0xF2, 0x16, 0x0A, 0x13)), (1, Color.FromArgb(0xF2, 0x2A, 0x0E, 0x1E))),
            BorderBrush = HomeMotion.Gradient(new(0, 0), new(1, 1), (0, Color.FromArgb(0x66, rose.R, rose.G, rose.B)), (1, Color.FromArgb(0x40, gold.R, gold.G, gold.B))),
        };
        Content = card;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(this, "pc-scan");
        SizeChanged += (_, e) => LayoutTiles(e.NewSize.Width);
        Unloaded += (_, _) => Detach();
    }

    public void Attach(SectionViewModel model)
    {
        Detach();
        vm = model; experience = model.Experience; checkup = model.Tools?.Checkup; monitoring = model.Monitoring;
        if (experience is not null) experience.PropertyChanged += OnChanged;
        if (checkup is not null) { checkup.PropertyChanged += OnChanged; checkup.Work.PropertyChanged += OnChanged; }
        if (monitoring is not null) monitoring.Updated += OnUpdated;
        Render();
    }
    private void Detach()
    {
        if (experience is not null) experience.PropertyChanged -= OnChanged;
        if (checkup is not null) { checkup.PropertyChanged -= OnChanged; checkup.Work.PropertyChanged -= OnChanged; }
        if (monitoring is not null) monitoring.Updated -= OnUpdated;
    }
    private void OnChanged(object? sender, PropertyChangedEventArgs e) => DispatcherQueue.TryEnqueue(Render);
    private void OnUpdated(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(Render);

    private bool Busy => experience?.IsAnalyzing == true || checkup?.Busy == true;
    private bool Done => checkup?.Done == true || experience?.HasResult == true;

    // Both parts run together: the memory measurement needs ~30 s of samples, the Check-up reads each tool meanwhile.
    private async Task RunAsync()
    {
        if (Busy || vm is null) return;
        showGood = false;
        var jobs = new List<Task>();
        if (experience is not null) jobs.Add(experience.AnalyzeCommand.ExecuteAsync(null));
        if (checkup is not null) jobs.Add(checkup.RunAsync());
        Render();
        try { await Task.WhenAll(jobs); } catch (Exception ex) { Serilog.Log.Warning(ex, "PC scan"); }
        Render();
    }

    private void Render()
    {
        if (vm is null) return;
        var t = vm.Text;
        eyebrow.Text = t["PxEyebrow"].ToUpperInvariant();
        title.Text = Busy ? t["PxTitleRunning"] : checkup is { Done: true } c ? c.Headline : Done ? t["PxTitleDone"] : t["PxTitleReady"];
        body.Text = t[Busy ? "PxBodyRunning" : Done ? "PxBodyDone" : "PxBodyReady"];
        run.Content = Label(Busy ? "" : "", t[Done ? "PxAnalyzeAgain" : "PxAnalyze"]);
        run.IsEnabled = !Busy;
        cancel.Content = t["UxCancel"];
        cancel.Visibility = experience?.IsAnalyzing == true ? Visibility.Visible : Visibility.Collapsed;
        safe.Text = t["PxSafe"];

        // Progress: the Check-up steps and the memory observation, weighted by how long each takes.
        progress.Visibility = Busy ? Visibility.Visible : Visibility.Collapsed;
        if (Busy)
        {
            var ram = experience?.IsAnalyzing == true ? Math.Min(1, (experience.ObservationSeconds) / 30) : 1;
            var ck = checkup is { Busy: true } ? checkup.Work.Percent / 100 : 1;
            bar.Value = Math.Round((ram * 0.5 + ck * 0.5) * 100);
            step.Text = checkup is { Busy: true, Step.Length: > 0 } ? checkup.Step : t["PxStepMemory"];
        }

        RenderTiles(t);
        RenderResults(t);
    }

    private static StackPanel Label(string glyph, string text)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        p.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14 });
        p.Children.Add(new TextBlock { Text = text });
        return p;
    }

    // ---- Live tiles ----
    private void RenderTiles(ITextCatalog t)
    {
        var s = monitoring?.Snapshot;
        var culture = System.Globalization.CultureInfo.GetCultureInfo(t.Language);
        double? cpu = s?.Cpu.TotalUtilizationPercent is { Status: TelemetryStatus.Available, Value: { } c } ? c : null;
        double? mem = s?.Memory.UsedPhysicalPercent is { Status: TelemetryStatus.Available, Value: { } m } ? m : null;
        var gpu = s?.Gpu.Devices.FirstOrDefault();
        double? gpuUse = gpu?.Get(GpuSensor.Utilization) is { Status: TelemetryStatus.Available, Value: { } gu } ? gu : null;
        double? temp = gpu?.Get(GpuSensor.Temperature) is { Status: TelemetryStatus.Available, Value: { } gt } ? gt : null;
        string Gb(ulong? b) => b is { } n ? TelemetryMath.GiB(n).ToString("0.0", culture) + " GB" : "—";
        ulong? used = s?.Memory.UsedPhysicalBytes is { Status: TelemetryStatus.Available, Value: { } u } ? u : null;
        ulong? total = s?.Memory.TotalPhysicalBytes is { Status: TelemetryStatus.Available, Value: { } tt } ? tt : null;
        var waiting = t[s is null ? "PxNoReading" : "PxUnavailable"];

        tiles.Children.Clear();
        AddTile(0, "", t["PxCpu"], cpu is { } a ? Math.Round(a).ToString(culture) + "%" : "—", experience?.CpuModel ?? "", cpu, cpu >= 70);
        AddTile(1, "", t["PxGpu"], gpuUse is { } g ? Math.Round(g).ToString(culture) + "%" : "—", gpu?.Name ?? experience?.GpuModel ?? waiting, gpuUse, false);
        AddTile(2, "", t["PxRam"], mem is { } r ? Math.Round(r).ToString(culture) + "%" : "—", used is not null && total is not null ? t.Format("PxRamUse", Gb(used), Gb(total)) : experience?.RamCapacity ?? waiting, mem, mem >= 80);
        AddTile(3, "", t["PxTemp"], temp is { } h ? Math.Round(h).ToString(culture) + " °C" : "—", temp is null ? waiting : t[temp >= 80 ? "PxTempHot" : "PxTempOk"], temp is { } h2 ? Math.Min(100, h2) : null, temp >= 80);
        LayoutTiles(ActualWidth);

        // What weighs most right now (measured only): memory, processor, heat, or nothing.
        var key = mem >= 80 ? "PxHoldMem" : cpu >= 70 ? "PxHoldCpu" : temp >= 80 ? "PxHoldHot" : s is null ? null : "PxHoldNone";
        verdict.Visibility = key is null ? Visibility.Collapsed : Visibility.Visible;
        if (key is not null)
        {
            var warn = key != "PxHoldNone";
            verdict.Background = new SolidColorBrush(warn ? Color.FromArgb(0x26, Warn.R, Warn.G, Warn.B) : Color.FromArgb(0x1F, Ok.R, Ok.G, Ok.B));
            verdictText.Text = t["PxHoldLabel"] + "  ·  " + t[key];
        }
    }

    private void AddTile(int index, string glyph, string label, string value, string sub, double? percent, bool hot)
    {
        var tile = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 14, 16, 14), Background = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 255, 255, 255)), BorderThickness = new Thickness(1) };
        var p = new StackPanel { Spacing = 6 };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var icon = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(9), Background = HomeMotion.Gradient(new(0, 0), new(1, 1), (0, accent), (1, rose)) };
        icon.Child = new FontIcon { Glyph = glyph, FontSize = 14, Foreground = new SolidColorBrush(Colors.White) };
        top.Children.Add(icon);
        top.Children.Add(new TextBlock { Text = label.ToUpperInvariant(), FontSize = 12, FontWeight = FontWeights.SemiBold, CharacterSpacing = 60, Foreground = new SolidColorBrush(Muted), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        p.Children.Add(top);
        p.Children.Add(new TextBlock { Text = value, FontSize = 30, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(hot ? Warn : Colors.White) });
        var track = new Grid { Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255)) };
        if (percent is { } v)
        {
            var share = Math.Clamp(v, 0, 100);
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(share, 0.01), GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - share, 0.01), GridUnitType.Star) });
            track.Children.Add(new Rectangle { RadiusX = 3, RadiusY = 3, Fill = hot ? new SolidColorBrush(Warn) : HomeMotion.Gradient(new(0, 0), new(1, 0), (0, accent), (1, gold)) });
        }
        p.Children.Add(track);
        var subText = new TextBlock { Text = sub, FontSize = 12, Foreground = new SolidColorBrush(Muted), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
        ToolTipService.SetToolTip(subText, sub);
        p.Children.Add(subText);
        tile.Child = p;
        tile.Tag = index;
        tiles.Children.Add(tile);
    }

    // Four across on a wide window, two by two when narrow.
    private void LayoutTiles(double width)
    {
        var columns = width is > 0 and < 820 ? 2 : 4;
        while (tiles.RowDefinitions.Count < 2) tiles.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var child in tiles.Children.OfType<FrameworkElement>())
        {
            var i = child.Tag is int n ? n : 0;
            Grid.SetColumn(child, columns == 4 ? i : i % 2); Grid.SetColumnSpan(child, columns == 4 ? 1 : 2);
            Grid.SetRow(child, columns == 4 ? 0 : i / 2);
        }
        // Two columns of equal width when narrow: each tile spans two of the four columns.
        if (columns == 2) foreach (var child in tiles.Children.OfType<FrameworkElement>()) { var i = child.Tag is int n ? n : 0; Grid.SetColumn(child, i % 2 * 2); }
    }

    // ---- Suggestions, most important first; each opens the tool that can act on it ----
    private void RenderResults(ITextCatalog t)
    {
        list.Children.Clear(); chips.Children.Clear();
        var items = new List<(CheckLevel Level, string Title, string Detail, string? Tool)>();
        if (experience is { HasResult: true } x)
            items.Add(x.NeedsAttention ? (CheckLevel.Attention, t["PxMemLow"], t["PxMemLowDetail"], "proc") : (CheckLevel.Good, t["PxMemOk"], t.Format("PxMemOkDetail", x.Available), null));
        if (experience is { State: AnalysisExperienceState.Partial }) items.Add((CheckLevel.Tip, t["PxPartial"], t["PxPartialDetail"], null));
        if (checkup is { Done: true } c) items.AddRange(c.Findings.Select(f => (f.Level, f.Title, f.Detail, (string?)f.ToolId)));
        results.Visibility = items.Count > 0 && !Busy ? Visibility.Visible : Visibility.Collapsed;
        if (items.Count == 0 || Busy) return;

        var attention = items.Count(i => i.Level == CheckLevel.Attention); var tips = items.Count(i => i.Level == CheckLevel.Tip); var good = items.Count(i => i.Level == CheckLevel.Good);
        chips.Children.Add(Chip("", t.Format("CkChipAttention", attention), Warn));
        chips.Children.Add(Chip("", t.Format("CkChipTips", tips), gold));
        chips.Children.Add(Chip("", t.Format("CkChipGood", good), Ok));
        list.Children.Add(new TextBlock { Text = t["PxSuggestions"].ToUpperInvariant(), FontSize = 12, FontWeight = FontWeights.Bold, CharacterSpacing = 140, Foreground = new SolidColorBrush(gold), Margin = new Thickness(0, 8, 0, 0) });
        foreach (var i in items.Where(i => i.Level != CheckLevel.Good).OrderByDescending(i => i.Level)) list.Children.Add(Row(t, i.Level, i.Title, i.Detail, i.Tool));
        if (attention + tips == 0) list.Children.Add(Row(t, CheckLevel.Good, t["PxAllGood"], t["PxAllGoodDetail"], null));
        if (good > 0)
        {
            var fold = new HyperlinkButton { Content = t.Format(showGood ? "PxGoodHide" : "PxGoodShow", good), Foreground = new SolidColorBrush(Ok), Padding = new Thickness(0, 4, 0, 4) };
            fold.Click += (_, _) => { showGood = !showGood; Render(); };
            list.Children.Add(fold);
            if (showGood) foreach (var i in items.Where(i => i.Level == CheckLevel.Good)) list.Children.Add(Row(t, i.Level, i.Title, i.Detail, i.Tool));
        }
    }

    private static Border Chip(string glyph, string text, Color color)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        p.Children.Add(new FontIcon { Glyph = glyph, FontSize = 12, Foreground = new SolidColorBrush(color) });
        p.Children.Add(new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold });
        return new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 5, 14, 5), Background = new SolidColorBrush(Color.FromArgb(0x1F, color.R, color.G, color.B)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x4D, color.R, color.G, color.B)), BorderThickness = new Thickness(1), Child = p };
    }

    private FrameworkElement Row(ITextCatalog t, CheckLevel level, string head, string detail, string? tool)
    {
        var color = level switch { CheckLevel.Attention => Warn, CheckLevel.Tip => gold, _ => Ok };
        var grid = new Grid { ColumnSpacing = 14, Padding = new Thickness(0, 12, 16, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new Rectangle { Fill = new SolidColorBrush(color), RadiusX = 2, RadiusY = 2, Margin = new Thickness(0, -12, 0, -12) });
        var badge = new Grid { Width = 36, Height = 36, VerticalAlignment = VerticalAlignment.Center };
        badge.Children.Add(new Ellipse { Fill = new SolidColorBrush(Color.FromArgb(0x26, color.R, color.G, color.B)) });
        badge.Children.Add(new FontIcon { Glyph = level switch { CheckLevel.Attention => "", CheckLevel.Tip => "", _ => "" }, FontSize = 16, Foreground = new SolidColorBrush(color) });
        Grid.SetColumn(badge, 1); grid.Children.Add(badge);
        var words = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = head, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Colors.White) });
        if (detail.Length > 0) words.Children.Add(new TextBlock { Text = detail, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = new SolidColorBrush(Muted) });
        Grid.SetColumn(words, 2); grid.Children.Add(words);
        if (tool is not null)
        {
            var go = new Button { Content = Label("", t[level == CheckLevel.Good ? "ToolOpen" : "PxFix"]), VerticalAlignment = VerticalAlignment.Center, MinHeight = 36,
                Style = (Style)Application.Current.Resources[level == CheckLevel.Attention ? "PrimaryButtonStyle" : "SoftButtonStyle"] };
            Motion.SetPress(go, true);
            go.Click += (_, _) => NavigateRequested?.Invoke(this, tool);
            Grid.SetColumn(go, 3); grid.Children.Add(go);
        }
        var row = new Border { CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromArgb(0x10, 255, 255, 255)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255)), BorderThickness = new Thickness(1), Child = grid };
        Motion.SetHover(row, 1.01);
        return row;
    }
}
