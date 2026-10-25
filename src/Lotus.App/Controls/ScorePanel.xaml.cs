using System.Runtime.InteropServices.WindowsRuntime;
using Lotus.Core.Score;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Lotus.App.Controls;

public sealed partial class ScorePanel : UserControl
{
    private readonly DispatcherTimer previewTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private WriteableBitmap? bitmap;
    private int shownVersion = -1;

    public ScorePanel()
    {
        InitializeComponent();
        previewTimer.Tick += (_, _) => DrawPreview();
        Loaded += (_, _) =>
        {
            if (DataContext is not LotusScoreViewModel vm) return;
            vm.Refresh();
            vm.PropertyChanged += OnVmChanged;
            ShowNumbers(vm, animate: false);
            OnlineLinks.Children.Clear();
            foreach (var (name, url) in vm.OnlineLinks())
            {
                var button = new Button { Content = name, Style = (Style)Application.Current.Resources["SoftButtonStyle"] };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "score-online-" + name.Replace(" ", "").ToLowerInvariant());
                button.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
                OnlineLinks.Children.Add(button);
            }
        };
        Unloaded += (_, _) => { previewTimer.Stop(); if (DataContext is LotusScoreViewModel vm) vm.PropertyChanged -= OnVmChanged; };
    }

    // Scores count up when a test finishes; otherwise they are just shown.
    private bool wasBusy;
    private void ShowNumbers(LotusScoreViewModel vm, bool animate)
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo(vm.Text.Language);
        void Show(TextBlock block, int? value)
        {
            if (value is not { } v) { block.Text = "—"; return; }
            if (animate) Motion.CountTo(block, v, n => n.ToString("N0", culture)); else block.Text = v.ToString("N0", culture);
        }
        Show(TotalText, vm.TotalValue); Show(Cpu1Text, vm.Cpu1Value); Show(CpuAllText, vm.CpuAllValue); Show(GpuText, vm.GpuValue);
    }

    // The preview timer runs only while the CPU scene is visible.
    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LotusScoreViewModel.Busy) && sender is LotusScoreViewModel s)
        {
            if (wasBusy && !s.Busy) ShowNumbers(s, animate: true);
            wasBusy = s.Busy;
            return;
        }
        if (e.PropertyName == nameof(LotusScoreViewModel.Total) && sender is LotusScoreViewModel t && !t.Busy && !wasBusy) ShowNumbers(t, animate: false);
        if (e.PropertyName != nameof(LotusScoreViewModel.ShowCpuPreview) || sender is not LotusScoreViewModel vm) return;
        if (vm.ShowCpuPreview) previewTimer.Start(); else { previewTimer.Stop(); DrawPreview(); }
    }

    private void DrawPreview()
    {
        if (DataContext is not LotusScoreViewModel { Preview: { } p }) return;
        var version = p.Version;
        if (version == shownVersion) return;
        shownVersion = version;
        if (bitmap is null || bitmap.PixelWidth != p.Width) { bitmap = new WriteableBitmap(p.Width, p.Height); CpuPreview.Source = bitmap; }
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            var bytes = new byte[p.Pixels.Length * 4];
            Buffer.BlockCopy(p.Pixels, 0, bytes, 0, bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }
        bitmap.Invalidate();
    }

    private async void OnToggle(object sender, RoutedEventArgs e)
    {
        if (DataContext is LotusScoreViewModel vm) await vm.ToggleAsync();
    }
}
