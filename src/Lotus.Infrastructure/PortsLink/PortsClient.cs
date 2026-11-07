using System.IO.Pipes;
using System.Runtime.Versioning;
using Lotus.Integration.Contracts;

namespace Lotus.Infrastructure.PortsLink;

/// <summary>
/// Lotus asking Lotus Ports (Ports.ListGames, Ports.Launch, Ports.Open). One short connection per question: Lotus
/// Ports is found through its registry entry, and the pipe's server process must be that registered executable.
/// Any failure (not installed, closed, refused, too slow) is just "not available": Lotus never waits on it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PortsClient(Serilog.ILogger log)
{
    private int nextId;

    /// <summary>Lotus Ports is installed with the integration (it may still be closed).</summary>
    public static bool IsInstalled => IntegrationRegistry.FindPorts() is not null;

    public Task<PortsListGamesResult?> ListGamesAsync(CancellationToken cancellationToken = default) =>
        AskAsync<PortsListGamesResult>(Methods.PortsListGames, new { }, cancellationToken);

    public Task<PortsLaunchResult?> LaunchAsync(string gameId, CancellationToken cancellationToken = default) =>
        AskAsync<PortsLaunchResult>(Methods.PortsLaunch, new PortsLaunchParams(gameId), cancellationToken);

    public async Task<bool> OpenAsync(string? gameId, CancellationToken cancellationToken = default) =>
        await AskAsync<System.Text.Json.JsonElement?>(Methods.PortsOpen, new PortsOpenParams(gameId), cancellationToken).ConfigureAwait(false) is not null;

    private async Task<T?> AskAsync<T>(string method, object parameters, CancellationToken cancellationToken)
    {
        if (IntegrationRegistry.FindPorts() is not { } ports) return default;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Protocol.RequestTimeout + TimeSpan.FromSeconds(1));
        try
        {
            await using var pipe = new NamedPipeClientStream(".", ports.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            if (!PipePeer.ServerIsPorts(pipe, ports.ExePath))
            {
                log.Warning("Lotus Ports pipe is served by another program: not used");
                return default;
            }
            var hello = await SendAsync(pipe, JsonRpcMessage.Request(Id(), Methods.Handshake,
                new HandshakeParams("Lotus", "1", Protocol.Version, LotusIntegrationServer.Capabilities)), timeout.Token).ConfigureAwait(false);
            if (hello?.ResultAs<HandshakeResult>() is not { } answer || !answer.Capabilities.Contains(method)) return default;
            var reply = await SendAsync(pipe, JsonRpcMessage.Request(Id(), method, parameters), timeout.Token).ConfigureAwait(false);
            if (reply?.Error is { } error)
            {
                log.Information("Lotus Ports refused {Method}: {Code} {Message}", method, error.Code, error.Message);
                return default;
            }
            return reply is null ? default : reply.ResultAs<T>();
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException or MessageFramingException or System.Text.Json.JsonException)
        {
            log.Debug(ex, "Lotus Ports not available for {Method}", method);
            return default;
        }
    }

    private string Id() => Interlocked.Increment(ref nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<JsonRpcMessage?> SendAsync(Stream pipe, JsonRpcMessage request, CancellationToken cancellationToken)
    {
        await MessageFraming.WriteAsync(pipe, request, cancellationToken).ConfigureAwait(false);
        return await MessageFraming.ReadAsync(pipe, cancellationToken).ConfigureAwait(false);
    }
}
