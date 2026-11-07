using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Lotus.Integration.Contracts;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Lotus.Infrastructure.PortsLink;

// Lotus side of the Lotus ↔ Lotus Ports integration (protocol in Lotus Ports' docs/INTEGRACAO_LOTUS.md).
// The contract files in Contracts/ are copies of Lotus Ports' Lotus.Integration.Contracts: change both together.

/// <summary>What Lotus Ports may ask Lotus. Each answer uses only what Lotus measured or read; nothing is guessed.</summary>
public interface ILotusIntegrationHandler
{
    SystemChecksResult RunSystemChecks(SystemChecksParams request);
    DependenciesCheckResult CheckDependencies(DependenciesCheckParams request);
    JournalQueryRecentResult RecentChanges(int days);
    SessionStartResult StartSession(SessionStartParams request);
    void EndSession(SessionEndParams request);
    SessionReport? Report(string sessionId);
    HardwareProfile HardwareProfile();

    /// <summary>Game.Installed, Game.Updated… from Lotus Ports (recorded in the log, shown nowhere yet).</summary>
    void OnEvent(JsonRpcMessage message);
}

/// <summary>
/// Lotus' pipe (\\.\pipe\lotus.integration.v1): only the current user, and only the Lotus Ports executable it
/// registered (HKCU\Software\LotusPorts\Integration\ExePath). The handshake comes first; then only the announced
/// methods are answered. Nothing else runs: unknown methods get -32601, invalid parameters -32602.
/// Nothing here changes the PC: these methods only read.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LotusIntegrationServer(string pipeName, ILotusIntegrationHandler handler, Func<NamedPipeServerStream, bool> isTrusted,
    Serilog.ILogger log, string appVersion) : IAsyncDisposable
{
    public static readonly IReadOnlyList<string> Capabilities =
    [
        Methods.DiagnosticsRunSystemChecks, Methods.DependenciesCheck, Methods.JournalQueryRecent,
        Methods.SessionStart, Methods.SessionEnd, Methods.SessionGetReport, Methods.SystemGetHardwareProfile,
        Events.GameInstalled, Events.GameUpdated, Events.GameRolledBack, Events.GameRemoved, Events.PresetApplied,
    ];

    private readonly CancellationTokenSource stop = new();
    private Task? accept;

    public void Start() => accept = Task.Run(() => AcceptLoopAsync(stop.Token));

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (IOException ex)
            {
                log.Information(ex, "Lotus Ports integration pipe already in use");
                return;
            }
            try { await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync().ConfigureAwait(false);
                return;
            }
            if (!isTrusted(server))
            {
                log.Warning("Refused an integration connection from a program that is not the registered Lotus Ports");
                await server.DisposeAsync().ConfigureAwait(false);
                continue;
            }
            _ = ServeAsync(server, cancellationToken);
        }
    }

    private async Task ServeAsync(NamedPipeServerStream stream, CancellationToken cancellationToken)
    {
        await using var _ = stream;
        var greeted = false;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await MessageFraming.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
                if (message is null) break;
                if (message.IsNotification)
                {
                    if (!greeted) break;
                    if (Capabilities.Contains(message.Method)) handler.OnEvent(message);
                    continue;
                }
                if (!message.IsRequest) continue;
                JsonRpcMessage reply;
                if (!greeted)
                {
                    if (message.Method != Methods.Handshake)
                    {
                        await MessageFraming.WriteAsync(stream, JsonRpcMessage.Failure(message.Id!, JsonRpcErrorCodes.InvalidRequest, "Handshake first."), cancellationToken).ConfigureAwait(false);
                        break;
                    }
                    if (Params<HandshakeParams>(message) is not { ProtocolVersion: Protocol.Version })
                    {
                        await MessageFraming.WriteAsync(stream, JsonRpcMessage.Failure(message.Id!, JsonRpcErrorCodes.IncompatibleVersion, "Protocol version not supported."), cancellationToken).ConfigureAwait(false);
                        break;
                    }
                    greeted = true;
                    reply = JsonRpcMessage.Success(message.Id!, new HandshakeResult("Lotus", appVersion, Protocol.Version, Capabilities));
                }
                else reply = await Task.Run(() => Handle(message), cancellationToken).ConfigureAwait(false);
                await MessageFraming.WriteAsync(stream, reply, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or MessageFramingException or OperationCanceledException)
        {
            log.Debug(ex, "Lotus Ports integration connection closed");
        }
    }

    internal JsonRpcMessage Handle(JsonRpcMessage message)
    {
        var id = message.Id!;
        var method = message.Method!;
        if (!Capabilities.Contains(method) || method.Contains('.') && method.Split('.')[0] == "Game")
            return JsonRpcMessage.Failure(id, JsonRpcErrorCodes.MethodNotFound, "Method not offered.");
        try
        {
            switch (method)
            {
                case Methods.DiagnosticsRunSystemChecks:
                    return JsonRpcMessage.Success(id, handler.RunSystemChecks(Params<SystemChecksParams>(message) ?? new SystemChecksParams([], null)));
                case Methods.DependenciesCheck:
                    if (Params<DependenciesCheckParams>(message) is not { Ids: { Count: > 0 and <= 20 } ids }) return Invalid(id);
                    return JsonRpcMessage.Success(id, handler.CheckDependencies(new DependenciesCheckParams(ids)));
                case Methods.JournalQueryRecent:
                    return JsonRpcMessage.Success(id, handler.RecentChanges(Math.Clamp(Params<JournalQueryRecentParams>(message)?.Days ?? 7, 1, 90)));
                case Methods.SessionStart:
                    if (Params<SessionStartParams>(message) is not { LotusGameKey.Length: > 0 and <= 200, ProcessId: > 0 } start) return Invalid(id);
                    return JsonRpcMessage.Success(id, handler.StartSession(start));
                case Methods.SessionEnd:
                    if (Params<SessionEndParams>(message) is not { SessionId.Length: > 0 } end) return Invalid(id);
                    handler.EndSession(end);
                    return JsonRpcMessage.Success(id, new { ended = true });
                case Methods.SessionGetReport:
                    if (Params<SessionGetReportParams>(message) is not { SessionId.Length: > 0 } ask) return Invalid(id);
                    return handler.Report(ask.SessionId) is { } report
                        ? JsonRpcMessage.Success(id, report)
                        : JsonRpcMessage.Failure(id, JsonRpcErrorCodes.InvalidParams, "Unknown session.");
                case Methods.SystemGetHardwareProfile:
                    return JsonRpcMessage.Success(id, handler.HardwareProfile());
                default:
                    return JsonRpcMessage.Failure(id, JsonRpcErrorCodes.MethodNotFound, "Method not offered.");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            log.Warning(ex, "Lotus Ports request {Method} failed", method);
            return JsonRpcMessage.Failure(id, JsonRpcErrorCodes.InternalError, "Lotus could not answer.");
        }
    }

    private static JsonRpcMessage Invalid(string id) => JsonRpcMessage.Failure(id, JsonRpcErrorCodes.InvalidParams, "Invalid parameters.");

    private static T? Params<T>(JsonRpcMessage message)
    {
        try { return message.ParamsAs<T>(); }
        catch (JsonException) { return default; }
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync().ConfigureAwait(false);
        if (accept is not null) await accept.ConfigureAwait(false);
        stop.Dispose();
    }
}

/// <summary>Reads and writes the registry entries that let each app find the other (HKCU only).</summary>
[SupportedOSPlatform("windows")]
public static class IntegrationRegistry
{
    /// <summary>Lets Lotus Ports find Lotus. Written by the installed (Release) Lotus only.</summary>
    public static void RegisterLotus(string appVersion, string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Protocol.LotusRegistryKey);
        key.SetValue(RegistryValueNames.PipeName, Protocol.LotusPipeName);
        key.SetValue(RegistryValueNames.ProtocolVersion, Protocol.Version, RegistryValueKind.DWord);
        key.SetValue(RegistryValueNames.AppVersion, appVersion);
        key.SetValue(RegistryValueNames.ExePath, exePath);
    }

    /// <summary>On exit: Lotus Ports then shows "Lotus fechado" instead of waiting on a pipe nobody serves.</summary>
    public static void UnregisterLotus()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Protocol.LotusRegistryKey, writable: true);
        key?.DeleteValue(RegistryValueNames.PipeName, throwOnMissingValue: false);
    }

    /// <summary>The Lotus Ports executable it registered, when it is installed and running with the integration.</summary>
    public static (string ExePath, string PipeName, string Version)? FindPorts()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Protocol.PortsRegistryKey);
        if (key?.GetValue(RegistryValueNames.ExePath) is not string exe || !File.Exists(exe)) return null;
        var pipe = key.GetValue(RegistryValueNames.PipeName) as string;
        return pipe is null ? null : (exe, pipe, key.GetValue(RegistryValueNames.AppVersion) as string ?? "");
    }
}

/// <summary>Who is on the other end of a pipe: the full path of its process, compared with the registered one.</summary>
[SupportedOSPlatform("windows")]
public static class PipePeer
{
    public static bool ClientIsPorts(NamedPipeServerStream pipe) =>
        IntegrationRegistry.FindPorts() is { } ports && GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid) && SamePath(pid, ports.ExePath);

    public static bool ServerIsPorts(NamedPipeClientStream pipe, string exePath) =>
        GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid) && SamePath(pid, exePath);

    private static bool SamePath(uint pid, string expected) =>
        ProcessPath(pid) is { } path && string.Equals(Path.GetFullPath(path), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase);

    public static string? ProcessPath(uint pid)
    {
        var process = OpenProcess(0x1000, false, pid); // query limited information
        if (process == IntPtr.Zero) return null;
        try
        {
            var buffer = new char[32768];
            var size = (uint)buffer.Length;
            return QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally { CloseHandle(process); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, [Out] char[] buffer, ref uint size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
