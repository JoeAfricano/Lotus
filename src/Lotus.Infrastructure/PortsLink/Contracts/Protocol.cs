namespace Lotus.Integration.Contracts;

/// <summary>Names shared by Lotus and Lotus Ports. Changing any of these is a protocol change (new version).</summary>
public static class Protocol
{
    public const int Version = 1;
    public const string LotusPipeName = "lotus.integration.v1";
    public const string PortsPipeName = "lotusports.integration.v1";
    public const string LotusRegistryKey = @"Software\Lotus\Integration";
    public const string PortsRegistryKey = @"Software\LotusPorts\Integration";

    /// <summary>Largest accepted message body, in bytes.</summary>
    public const int MaxMessageBytes = 1024 * 1024;

    /// <summary>Default wait for a reply before treating the other app as unavailable.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);
}

/// <summary>Values written under the registry keys so each app can find the other.</summary>
public static class RegistryValueNames
{
    public const string PipeName = "PipeName";
    public const string ProtocolVersion = "ProtocolVersion";
    public const string AppVersion = "AppVersion";
    public const string ExePath = "ExePath";
}

public static class Methods
{
    public const string Handshake = "Handshake";
    public const string DependenciesCheck = "Dependencies.Check";
    public const string DependenciesRequestInstall = "Dependencies.RequestInstall";
    public const string DiagnosticsRunSystemChecks = "Diagnostics.RunSystemChecks";
    public const string JournalQueryRecent = "Journal.QueryRecent";
    public const string SessionStart = "Session.Start";
    public const string SessionEnd = "Session.End";
    public const string SessionGetReport = "Session.GetReport";
    public const string SystemGetHardwareProfile = "System.GetHardwareProfile";
    public const string RecommendationsForGame = "Recommendations.ForGame";
    public const string PortsListGames = "Ports.ListGames";
    public const string PortsLaunch = "Ports.Launch";
    public const string PortsOpen = "Ports.Open";
    public const string JournalQuery = "Journal.Query";
}

public static class Events
{
    public const string GameInstalled = "Game.Installed";
    public const string GameUpdated = "Game.Updated";
    public const string GameRolledBack = "Game.RolledBack";
    public const string GameRemoved = "Game.Removed";
    public const string PresetApplied = "Preset.Applied";
    public const string SessionStarted = "Session.Started";
    public const string SessionEnded = "Session.Ended";
    public const string DependenciesChanged = "Dependencies.Changed";
    public const string DriverChanged = "Driver.Changed";
    public const string ProfileChanged = "Profile.Changed";
    public const string LotusShuttingDown = "Lotus.ShuttingDown";
}

/// <summary>Dependency ids: the single source of truth for both apps.</summary>
public static class DependencyIds
{
    public const string VcRedistX64_2015_2022 = "vcredist-x64-2015-2022";
    public const string DirectXLegacyRuntime = "directx-legacy-runtime";
    public const string VulkanRuntime = "vulkan-runtime";
    public const string DotNetDesktopRuntime = "dotnet-desktop-runtime";

    public static IReadOnlyList<string> All { get; } = [VcRedistX64_2015_2022, DirectXLegacyRuntime, VulkanRuntime, DotNetDesktopRuntime];
}

/// <summary>Result of a check exchanged between the apps. Mirrors OK / AVISO / FALHA / NÃO VERIFICADO.</summary>
public enum CheckStatus
{
    NotVerified = 0,
    Ok,
    Warning,
    Failure,
}

public sealed record HandshakeParams(string AppName, string AppVersion, int ProtocolVersion, IReadOnlyList<string> Capabilities);

public sealed record HandshakeResult(string AppName, string AppVersion, int ProtocolVersion, IReadOnlyList<string> Capabilities);
