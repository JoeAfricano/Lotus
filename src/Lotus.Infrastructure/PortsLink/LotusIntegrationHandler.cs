using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Lotus.Core.Benchmark;
using Lotus.Core.Optimization;
using Lotus.Infrastructure.Benchmark;
using Lotus.Infrastructure.Optimization;
using Lotus.Infrastructure.Tools;
using Lotus.Integration.Contracts;
using Microsoft.Win32;

namespace Lotus.Infrastructure.PortsLink;

/// <summary>
/// Lotus' answers to Lotus Ports. Only facts Lotus read or measured on this PC: the graphics driver, installed
/// runtimes, free memory and disk, Lotus' own journal of changes, and frame times captured with PresentMon during
/// a game session. When something could not be measured it says so (NotVerified, Measured = false).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LotusIntegrationHandler(SqliteOptimizationJournal journal, IFrameCaptureProvider capture, Serilog.ILogger log) : ILotusIntegrationHandler
{
    /// <summary>How long after the game opens the frame capture starts (loading screens are not the game), and how long it lasts.</summary>
    internal static readonly TimeSpan CaptureDelay = TimeSpan.FromSeconds(20);
    internal const int CaptureSeconds = 60;

    private sealed class Session(SessionStartParams start)
    {
        public SessionStartParams Start { get; } = start;
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
        public double? DurationSeconds { get; set; }
        public CancellationTokenSource Stop { get; } = new();
        public Task<BenchmarkSession?>? Capture { get; set; }
    }

    private readonly ConcurrentDictionary<string, Session> sessions = new();

    /// <summary>A game opened or closed through Lotus Ports (raised on the pipe thread; the app moves to its own).</summary>
    public event Action<SessionStartParams>? SessionStarted;
    /// <summary>Game, start time (UTC) and seconds played.</summary>
    public event Action<SessionStartParams, DateTimeOffset, double>? SessionEnded;

    // ---- Diagnostics.RunSystemChecks ------------------------------------------------------------------------

    public SystemChecksResult RunSystemChecks(SystemChecksParams request)
    {
        var items = new List<SystemCheckItem>();
        if (new WindowsDriverInfo().Read() is { } driver)
        {
            var age = driver.Date is { } date ? (int)(DateTime.Today - date.Date).TotalDays : (int?)null;
            items.Add(new("gpu-driver", age is > 365 ? CheckStatus.Warning : age is null ? CheckStatus.NotVerified : CheckStatus.Ok,
                $"Driver de vídeo: {driver.Name}",
                age is null ? $"Versão {driver.Version}; a data do driver não pôde ser lida."
                    : age > 365 ? $"Versão {driver.Version}, de {driver.Date:dd/MM/yyyy}: tem mais de um ano. Atualizar pode resolver falhas gráficas."
                    : $"Versão {driver.Version}, de {driver.Date:dd/MM/yyyy}."));
        }
        else items.Add(new("gpu-driver", CheckStatus.NotVerified, "Driver de vídeo", "O Lotus não conseguiu ler o driver da placa de vídeo."));

        foreach (var api in request.GraphicsApis.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (api.Equals("Vulkan", StringComparison.OrdinalIgnoreCase))
                items.Add(SystemFile("vulkan-1.dll") ? new("vulkan", CheckStatus.Ok, "Vulkan", "O Vulkan está instalado no Windows.")
                    : new("vulkan", CheckStatus.Failure, "Vulkan", "O Vulkan não foi encontrado. Ele vem com o driver de vídeo: atualizar o driver costuma resolver."));
        }

        if (MemoryAvailableBytes() is { } free)
        {
            var gb = free / 1_073_741_824d;
            items.Add(new("memory", gb < 2 ? CheckStatus.Warning : CheckStatus.Ok, "Memória livre",
                gb < 2 ? $"Só {gb:0.0} GB livres agora. Fechar outros programas antes de jogar ajuda." : $"{gb:0.0} GB livres agora."));
        }

        if (request.ExePath is { Length: > 3 } exe && Path.IsPathFullyQualified(exe))
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(exe)!);
                var gb = drive.AvailableFreeSpace / 1_073_741_824d;
                items.Add(new("disk", gb < 2 ? CheckStatus.Warning : CheckStatus.Ok, $"Espaço no disco {drive.Name.TrimEnd('\\')}",
                    gb < 2 ? $"Só {gb:0.0} GB livres: jogos podem falhar ao salvar ou ao criar o cache." : $"{gb:0.0} GB livres."));
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }
        }
        return new SystemChecksResult(items);
    }

    // ---- Dependencies.Check ----------------------------------------------------------------------------------

    public DependenciesCheckResult CheckDependencies(DependenciesCheckParams request) =>
        new(request.Ids.Distinct().Select(Dependency).ToList());

    private static DependencyState Dependency(string id) => id switch
    {
        DependencyIds.VcRedistX64_2015_2022 => VcRedist(),
        DependencyIds.VulkanRuntime => SystemFile("vulkan-1.dll") ? new(id, CheckStatus.Ok, null, null) : new(id, CheckStatus.Failure, null, "vulkan-1.dll não encontrado."),
        DependencyIds.DirectXLegacyRuntime => SystemFile("d3dx9_43.dll") ? new(id, CheckStatus.Ok, null, null) : new(id, CheckStatus.Failure, null, "Bibliotecas antigas do DirectX (d3dx9) não encontradas."),
        DependencyIds.DotNetDesktopRuntime => DotNetDesktop(),
        _ => new(id, CheckStatus.NotVerified, null, "O Lotus não conhece esta dependência."),
    };

    private static DependencyState VcRedist()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");
        return key?.GetValue("Installed") is int installed && installed == 1
            ? new(DependencyIds.VcRedistX64_2015_2022, CheckStatus.Ok, key.GetValue("Version") as string, null)
            : new(DependencyIds.VcRedistX64_2015_2022, CheckStatus.Failure, null, "Visual C++ 2015–2022 (x64) não instalado.");
    }

    private static DependencyState DotNetDesktop()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.WindowsDesktop.App");
        var versions = Directory.Exists(root) ? Directory.GetDirectories(root).Select(Path.GetFileName).OfType<string>().ToList() : [];
        var newest = versions.Select(v => (Text: v, Parsed: Version.TryParse(v.Split('-')[0], out var p) ? p : null)).Where(v => v.Parsed is not null).MaxBy(v => v.Parsed);
        return newest.Text is null
            ? new(DependencyIds.DotNetDesktopRuntime, CheckStatus.Failure, null, ".NET Desktop Runtime não instalado.")
            : new(DependencyIds.DotNetDesktopRuntime, CheckStatus.Ok, newest.Text, null);
    }

    private static bool SystemFile(string name) => File.Exists(Path.Combine(Environment.SystemDirectory, name));

    private static ulong? MemoryAvailableBytes()
    {
        var status = new MemoryStatus { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref status) ? status.AvailPhys : null;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MemoryStatus { public uint Length, Load; public ulong TotalPhys, AvailPhys, TotalPage, AvailPage, TotalVirtual, AvailVirtual, AvailExtended; }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    // ---- Journal.QueryRecent ---------------------------------------------------------------------------------

    public JournalQueryRecentResult RecentChanges(int days)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-days);
        var changes = new List<SystemChange>();
        if (new WindowsDriverInfo().Read() is { Date: { } date } driver && date >= since.LocalDateTime.Date)
            changes.Add(new(new DateTimeOffset(date, TimeSpan.Zero), "Driver", $"Driver de vídeo {driver.Name} na versão {driver.Version} (de {date:dd/MM/yyyy})."));
        try
        {
            foreach (var plan in journal.List().Where(p => p.CreatedUtc >= since && p.Stage is OperationStage.Applied or OperationStage.Reverted))
                changes.Add(new(plan.CreatedUtc, "Lotus", plan.Stage == OperationStage.Applied
                    ? $"O Lotus aplicou o ajuste \"{ActionName(plan.ActionId)}\"."
                    : $"O Lotus desfez o ajuste \"{ActionName(plan.ActionId)}\"."));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            log.Warning(ex, "Journal read for Lotus Ports failed");
        }
        return new JournalQueryRecentResult(changes.OrderByDescending(c => c.At).Take(50).ToList());
    }

    private static string ActionName(string actionId) => actionId switch
    {
        var a when a.Contains("power", StringComparison.OrdinalIgnoreCase) => "plano de energia",
        var a when a.Contains("windowed", StringComparison.OrdinalIgnoreCase) => "otimizações para jogos em janela",
        var a when a.Contains("gamemode", StringComparison.OrdinalIgnoreCase) || a.Contains("game-mode", StringComparison.OrdinalIgnoreCase) => "Modo de Jogo",
        var a when a.Contains("hags", StringComparison.OrdinalIgnoreCase) => "agendamento de GPU por hardware",
        _ => actionId,
    };

    // ---- Sessions ---------------------------------------------------------------------------------------------

    public SessionStartResult StartSession(SessionStartParams request)
    {
        var id = Guid.NewGuid().ToString("N");
        var session = new Session(request);
        sessions[id] = session;
        log.Information("Lotus Ports session {Session} started: {Game} ({Port})", id, request.GameTitle, request.PortId);
        // Frame times are measured only where Lotus' PresentMon is already installed, without asking for admin rights.
        if (File.Exists(PresentMonCaptureProvider.ToolPath))
            session.Capture = CaptureLaterAsync(session);
        try { SessionStarted?.Invoke(request); } catch (Exception ex) { log.Warning(ex, "Lotus Ports session start handler"); }
        return new SessionStartResult(id);
    }

    private async Task<BenchmarkSession?> CaptureLaterAsync(Session session)
    {
        try
        {
            await Task.Delay(CaptureDelay, session.Stop.Token).ConfigureAwait(false);
            using var process = System.Diagnostics.Process.GetProcessById(session.Start.ProcessId);
            if (process.HasExited) return null;
            var target = new CaptureTarget(process.Id, session.Start.ExePath, process.StartTime.ToUniversalTime());
            return await capture.CaptureAsync(new CaptureRequest(target, CaptureSeconds, Elevated: false, $"Lotus Ports: {session.Start.GameTitle}", "{}"), session.Stop.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            log.Information(ex, "Frame capture for a Lotus Ports session did not run");
            return null;
        }
    }

    public void EndSession(SessionEndParams request)
    {
        if (!sessions.TryGetValue(request.SessionId, out var session) || session.DurationSeconds is not null) return; // ended once
        session.DurationSeconds = Math.Max(0, request.DurationSeconds);
        try { SessionEnded?.Invoke(session.Start, session.StartedAt, session.DurationSeconds.Value); } catch (Exception ex) { log.Warning(ex, "Lotus Ports session end handler"); }
        // A capture still waiting to start is cancelled; one already running finishes its window (the game closed anyway).
        if (session.Capture is null || !session.Capture.IsCompleted) session.Stop.CancelAfter(TimeSpan.FromSeconds(CaptureSeconds + 15));
        log.Information("Lotus Ports session {Session} ended after {Seconds:0} s, exit code {Code}", request.SessionId, request.DurationSeconds, request.ExitCode);
    }

    public SessionReport? Report(string sessionId)
    {
        if (!sessions.TryGetValue(sessionId, out var session)) return null;
        var duration = session.DurationSeconds ?? (DateTimeOffset.UtcNow - session.StartedAt).TotalSeconds;
        var captured = session.Capture is { IsCompletedSuccessfully: true } done ? done.Result : null;
        if (captured is { MathematicallyValid: true, Data.Statistics: { } stats })
            return new SessionReport(sessionId, true, duration, Math.Round(stats.AverageFps, 1), Math.Round(stats.Low1Fps, 1), stats.SpikeCount, null, null);
        return new SessionReport(sessionId, false, duration, null, null, null, null, null);
    }

    // ---- System.GetHardwareProfile ------------------------------------------------------------------------------

    public HardwareProfile HardwareProfile()
    {
        var (cpu, gpu, ram) = QuickHardwareFacts.Read();
        return new HardwareProfile(cpu, gpu, ram is { } r ? Math.Round(r / 1_073_741_824d, 1) : null, null, null);
    }

    public void OnEvent(JsonRpcMessage message) =>
        log.Information("Lotus Ports event {Event}: {Body}", message.Method, message.Params?.ToString());
}
