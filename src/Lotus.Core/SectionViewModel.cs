namespace Lotus.Core;

public sealed record MetricPlaceholder(string Title, string Status);

public sealed class SectionViewModel
{
    public Section Section { get; }
    public ITextCatalog Text { get; }
    public PreferencesViewModel Preferences { get; }
    public Telemetry.MonitoringViewModel? Monitoring { get; }
    public Diagnostics.MemoryAnalysisViewModel? MemoryAnalysis { get; }
    public string Title => Section.Id == "dashboard" ? Text["Greeting"] : Section.Title;
    public string Subtitle => Section.Description;
    public string Glyph => Section.Glyph;
    public string NextStep => Section.NextStep;
    public string Planned => Text["Planned"];
    public bool MotionEnabled { get; }
    public IReadOnlyList<MetricPlaceholder> Metrics { get; }
    public SectionViewModel(Section section, ITextCatalog text, PreferencesViewModel preferences, bool motionEnabled = true, Telemetry.MonitoringViewModel? monitoring = null, Diagnostics.MemoryAnalysisViewModel? memoryAnalysis = null)
    {
        Section = section;
        Text = text;
        Preferences = preferences;
        Monitoring = monitoring;
        MemoryAnalysis = memoryAnalysis;
        MotionEnabled = motionEnabled;
        Metrics = new[] { "CPU", "GPU", "Memory", "Storage" }.Select(key => new MetricPlaceholder(text[key], text["NoReading"])).ToArray();
    }
}
