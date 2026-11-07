namespace Lotus.Integration.Contracts;

/// <summary>
/// Parameters and results of each method (master prompt, section 12). Records only: both apps serialize them with
/// <see cref="ContractJson.Options"/>. A capability is the method or event name the side announces in the handshake.
/// </summary>
public sealed record DependenciesCheckParams(IReadOnlyList<string> Ids);

public sealed record DependencyState(string Id, CheckStatus State, string? InstalledVersion, string? Details);

public sealed record DependenciesCheckResult(IReadOnlyList<DependencyState> Items);

public sealed record DependenciesRequestInstallParams(IReadOnlyList<string> Ids, string? Reason);

public sealed record SystemChecksParams(IReadOnlyList<string> GraphicsApis, string? ExePath);

/// <summary>One system check from Lotus, in the same OK / AVISO / FALHA / NÃO VERIFICADO scale. Texts come in pt-BR.</summary>
public sealed record SystemCheckItem(string Id, CheckStatus State, string TitlePtBr, string DetailsPtBr);

public sealed record SystemChecksResult(IReadOnlyList<SystemCheckItem> Items);

public sealed record JournalQueryRecentParams(int Days);

/// <summary>A change Lotus recorded on the PC (driver update, setting applied…), shown as a clue in "Resolver".</summary>
public sealed record SystemChange(DateTimeOffset At, string Kind, string DescriptionPtBr);

public sealed record JournalQueryRecentResult(IReadOnlyList<SystemChange> Changes);

public sealed record SessionStartParams(string LotusGameKey, string GameTitle, string PortId, string? PortVersion, string ExePath, int ProcessId, string? Preset);

public sealed record SessionStartResult(string SessionId);

public sealed record SessionEndParams(string SessionId, int ExitCode, double DurationSeconds);

/// <summary>Body of the events; each event uses only the fields it needs.</summary>
public sealed record GameEvent(string GameId, string PortId, string? Version);

public sealed record DriverChangedEvent(string Device, string? Version, DateTimeOffset At);

// ---- Phase 6: Lotus asks Lotus Ports (section 12.7 and 12.8) ---------------------------------------------------

public enum PortsGameState
{
    NotInstalled,
    Ready,
    UpdateAvailable,
    Problem,
}

/// <summary>A game Lotus may show in its own library. Only facts Lotus Ports has; no paths.</summary>
public sealed record PortsGame(string GameId, string Title, string LotusGameKey, string Platform, PortsGameState State, string? Version, string? CoverUrl, bool Favorite, DateTimeOffset? LastPlayed);

public sealed record PortsListGamesResult(IReadOnlyList<PortsGame> Games);

/// <summary>Only a catalog game id: never a path or a program (section 12.3).</summary>
public sealed record PortsLaunchParams(string GameId);

/// <summary>What happened: started, or the game page was opened because the player must confirm something first.</summary>
public sealed record PortsLaunchResult(bool Started, string? ReasonPtBr);

public sealed record PortsOpenParams(string? GameId);

public sealed record JournalQueryParams(int Limit);

public sealed record JournalEntryDto(long Id, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string Kind, string? PortId, string? Outcome, string? Details, string CorrelationId);

public sealed record JournalQueryResult(IReadOnlyList<JournalEntryDto> Entries);

// ---- Phase 6: Lotus Ports asks Lotus (section 12.6) ------------------------------------------------------------

public sealed record SessionGetReportParams(string SessionId);

/// <summary>
/// Only measured values: when <see cref="Measured"/> is false, no number is shown. Optional fields stay null when Lotus
/// did not measure them.
/// </summary>
public sealed record SessionReport(string SessionId, bool Measured, double DurationSeconds, double? AverageFps, double? OnePercentLowFps, int? Stutters, double? MaxCpuTemperature, double? MaxGpuTemperature);

public sealed record RecommendationsForGameParams(string LotusGameKey, IReadOnlyList<string> LastSessionIds);

/// <summary>A suggestion from measurements. <see cref="Style"/> ("Original"/"Remastered") is applied through the style flow; <see cref="ComparisonPtBr"/> is Lotus's before/after once a session ran with it.</summary>
public sealed record Recommendation(string Id, string TextPtBr, string EvidencePtBr, string? Style, string? ComparisonPtBr);

public sealed record RecommendationsResult(IReadOnlyList<Recommendation> Items);

public sealed record HardwareProfile(string? Cpu, string? Gpu, double? RamGb, double? VramGb, string? Tier);
