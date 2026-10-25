using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lotus.Core.Telemetry;

namespace Lotus.Core.Diagnostics;

public sealed record HardwareFindingRow(string Title, string Detail, string Level, string Glyph, bool IsAttention, bool IsOk, string? SettingsUri)
{
    public bool HasSettings => SettingsUri is not null;
}

// Read-only hardware check: display refresh, RAM speed/channels, CPU frequency limit, GPU limits and adapters.
public sealed class HardwareDiagnosticsViewModel : ObservableObject
{
    private readonly IHardwareFactsSource source;
    private readonly MonitoringViewModel? monitoring;
    private readonly Action<Exception> log;
    private IReadOnlyList<HardwareFinding> findings = [];
    private HardwareFacts? facts;
    private bool busy;
    public ITextCatalog Text { get; }
    public ObservableCollection<HardwareFindingRow> Findings { get; } = [];
    public string Summary { get; private set; } = "";
    public string LastChecked { get; private set; } = "";
    public bool IsBusy => busy;
    public bool HasFindings => Findings.Count > 0;
    public IAsyncRelayCommand CheckCommand { get; }

    public HardwareDiagnosticsViewModel(IHardwareFactsSource source, MonitoringViewModel? monitoring, ITextCatalog text, Action<Exception> log)
    {
        this.source = source; this.monitoring = monitoring; Text = text; this.log = log;
        Summary = text["HwIntro"];
        CheckCommand = new AsyncRelayCommand(CheckAsync, () => !busy);
    }

    public async Task CheckAsync()
    {
        if (busy) return;
        busy = true; OnPropertyChanged(nameof(IsBusy)); CheckCommand.NotifyCanExecuteChanged();
        try
        {
            facts = await Task.Run(source.Read);
            Build();
            LastChecked = Text.Format("HwLastChecked", DateTime.Now.ToString("t"));
        }
        catch (Exception ex) { log(ex); Summary = Text["HwFailed"]; }
        finally
        {
            busy = false;
            foreach (var p in new[] { nameof(IsBusy), nameof(Summary), nameof(LastChecked), nameof(HasFindings) }) OnPropertyChanged(p);
            CheckCommand.NotifyCanExecuteChanged();
        }
    }

    // GPU limits come from the most recent monitoring sample, so no second NVML session is opened.
    private uint? GpuReasons()
    {
        var reading = monitoring?.Snapshot?.Gpu.Devices.FirstOrDefault()?.Get(GpuSensor.ClockEventReasons);
        return reading is { Status: TelemetryStatus.Available, Value: { } v } ? (uint)v : null;
    }

    private void Build()
    {
        if (facts is null) return;
        findings = HardwareRules.Evaluate(facts, GpuReasons(), Text);
        Findings.Clear();
        foreach (var f in findings)
            Findings.Add(new(f.Title, f.Detail, Text["HwLevel" + f.Level], f.Level switch
            {
                FindingLevel.Attention => "", FindingLevel.Ok => "", FindingLevel.Info => "", _ => ""
            }, f.Level == FindingLevel.Attention, f.Level == FindingLevel.Ok, f.SettingsUri));
        var attention = findings.Count(f => f.Level == FindingLevel.Attention);
        Summary = attention == 0 ? Text["HwSummaryOk"] : Text.Format("HwSummaryAttention", attention);
    }

    public void RefreshLanguage()
    {
        if (facts is not null) Build(); else Summary = Text["HwIntro"];
        OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(HasFindings));
    }
}
