namespace Lotus.Core;

public sealed record MetricPlaceholder(string Title, string Status);

public sealed class SectionViewModel
{
    public Section Section { get; }
    public ShellViewModel? Shell { get; }
    public ExperienceViewModel? Experience { get; }
    public Benchmark.BenchmarkViewModel? Benchmark { get; }
    public Optimization.OptimizationViewModel? Optimizer { get; }
    public Optimization.PowerProfilesViewModel? PowerProfiles { get; }
    public Diagnostics.HardwareDiagnosticsViewModel? Hardware { get; }
    public Games.GamesViewModel? Games { get; }
    public Integrations.IntegrationsViewModel? Integrations { get; }
    public Score.LotusScoreViewModel? Score { get; }
    public Tools.ToolsViewModel? Tools { get; }
    public ITextCatalog Text { get; }
    public PreferencesViewModel Preferences { get; }
    public Telemetry.MonitoringViewModel? Monitoring { get; }
    public Diagnostics.MemoryAnalysisViewModel? MemoryAnalysis { get; }
    public string Title => Section.Id == "home" ? Text["Greeting"] : Section.Title;
    public string Subtitle => Section.Description;
    public string Glyph => Section.Glyph;
    public string NextStep => Section.NextStep;
    public string LogLocation => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "Logs");
    public string AppVersion => "Lotus · " + (System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "—");
    public string Planned => Text["Planned"];
    public bool MotionEnabled { get; }
    public IReadOnlyList<MetricPlaceholder> Metrics { get; }
    public SectionViewModel(Section section, ITextCatalog text, PreferencesViewModel preferences, bool motionEnabled = true, Telemetry.MonitoringViewModel? monitoring = null, Diagnostics.MemoryAnalysisViewModel? memoryAnalysis = null, ShellViewModel? shell = null, ExperienceViewModel? experience = null, Benchmark.BenchmarkViewModel? benchmark = null, Optimization.OptimizationViewModel? optimizer = null, Optimization.PowerProfilesViewModel? powerProfiles = null, Diagnostics.HardwareDiagnosticsViewModel? hardware = null, Games.GamesViewModel? games = null, Integrations.IntegrationsViewModel? integrations = null, Score.LotusScoreViewModel? score = null, Tools.ToolsViewModel? tools = null)
    {
        Section = section;
        Shell = shell; Experience = experience; Benchmark = benchmark; Optimizer = optimizer; PowerProfiles = powerProfiles; Hardware = hardware; Games = games; Integrations = integrations; Score = score; Tools = tools;
        Text = text;
        Preferences = preferences;
        Monitoring = monitoring;
        MemoryAnalysis = memoryAnalysis;
        MotionEnabled = motionEnabled;
        Metrics = new[] { "CPU", "GPU", "Memory", "Storage" }.Select(key => new MetricPlaceholder(text[key], text["NoReading"])).ToArray();
    }
}
