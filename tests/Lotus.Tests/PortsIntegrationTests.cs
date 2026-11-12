using System.IO.Pipes;
using Lotus.Core.Benchmark;
using Lotus.Infrastructure.Optimization;
using Lotus.Infrastructure.PortsLink;
using Lotus.Integration.Contracts;
using Xunit;

namespace Lotus.Tests;

/// <summary>Lotus' side of the Lotus Ports integration, over a real named pipe with the same framing Lotus Ports uses.</summary>
public sealed class PortsIntegrationTests : IAsyncLifetime
{
    private sealed class NoCapture : IFrameCaptureProvider
    {
        public Task<BenchmarkSession> CaptureAsync(CaptureRequest request, CancellationToken stop) => throw new InvalidOperationException("not used");
    }

    private readonly string pipe = "lotus.integration.test." + Guid.NewGuid().ToString("N");
    private readonly string dbFolder = Path.Combine(Path.GetTempPath(), "lotus-ports-link-" + Guid.NewGuid().ToString("N"));
    private LotusIntegrationServer? server;
    private bool trusted = true;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(dbFolder);
        var handler = new LotusIntegrationHandler(new SqliteOptimizationJournal(Path.Combine(dbFolder, "journal.db")), new NoCapture(), Serilog.Core.Logger.None);
        server = new LotusIntegrationServer(pipe, handler, _ => trusted, Serilog.Core.Logger.None, "9.9.9");
        server.Start();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (server is not null) await server.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(dbFolder, true); } catch (IOException) { }
    }

    private async Task<NamedPipeClientStream> ConnectAsync()
    {
        var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        return client;
    }

    private static async Task<JsonRpcMessage> AskAsync(Stream stream, JsonRpcMessage request)
    {
        await MessageFraming.WriteAsync(stream, request);
        return (await MessageFraming.ReadAsync(stream))!;
    }

    private static JsonRpcMessage Hello(int version = Protocol.Version) =>
        JsonRpcMessage.Request("1", Methods.Handshake, new HandshakeParams("Lotus Ports", "0.1.0", version, [Events.GameInstalled]));

    [Theory]
    [InlineData(false, false, false)] // off by default: never asks for administrator
    [InlineData(true, false, true)]   // opted in: Windows asks each game
    [InlineData(true, true, false)]   // "Performance Log Users" measures without asking
    public void Elevation_only_when_opted_in_and_not_in_group(bool optedIn, bool inGroup, bool expected) =>
        Assert.Equal(expected, LotusIntegrationHandler.NeedsElevation(optedIn, inGroup));

    [Fact]
    public async Task Handshake_announces_only_what_Lotus_answers()
    {
        await using var client = await ConnectAsync();
        var reply = await AskAsync(client, Hello());
        var hello = reply.ResultAs<HandshakeResult>()!;
        Assert.Equal("Lotus", hello.AppName);
        Assert.Equal("9.9.9", hello.AppVersion);
        Assert.Contains(Methods.DiagnosticsRunSystemChecks, hello.Capabilities);
        Assert.Contains(Methods.SessionGetReport, hello.Capabilities);
        Assert.DoesNotContain(Methods.RecommendationsForGame, hello.Capabilities); // nothing measured to recommend yet
        Assert.DoesNotContain(Methods.DependenciesRequestInstall, hello.Capabilities);
    }

    [Fact]
    public async Task Requests_before_the_handshake_or_with_another_version_are_refused()
    {
        await using (var client = await ConnectAsync())
            Assert.Equal(JsonRpcErrorCodes.InvalidRequest, (await AskAsync(client, JsonRpcMessage.Request("1", Methods.SystemGetHardwareProfile, new { }))).Error!.Code);
        await using (var client = await ConnectAsync())
            Assert.Equal(JsonRpcErrorCodes.IncompatibleVersion, (await AskAsync(client, Hello(version: 99))).Error!.Code);
    }

    [Fact]
    public async Task Unknown_methods_and_bad_parameters_get_the_protocol_errors()
    {
        await using var client = await ConnectAsync();
        await AskAsync(client, Hello());
        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, (await AskAsync(client, JsonRpcMessage.Request("2", "Process.Start", new { path = "cmd.exe" }))).Error!.Code);
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, (await AskAsync(client, JsonRpcMessage.Request("3", Methods.DependenciesCheck, new { ids = Array.Empty<string>() }))).Error!.Code);
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, (await AskAsync(client, JsonRpcMessage.Request("4", Methods.SessionGetReport, new SessionGetReportParams("nope")))).Error!.Code);
    }

    [Fact]
    public async Task System_checks_dependencies_journal_and_profile_answer_with_what_this_PC_has()
    {
        await using var client = await ConnectAsync();
        await AskAsync(client, Hello());
        var checks = (await AskAsync(client, JsonRpcMessage.Request("2", Methods.DiagnosticsRunSystemChecks, new SystemChecksParams(["Vulkan"], Environment.ProcessPath)))).ResultAs<SystemChecksResult>()!;
        Assert.Contains(checks.Items, i => i.Id == "gpu-driver");
        Assert.Contains(checks.Items, i => i.Id == "vulkan");
        Assert.All(checks.Items, i => Assert.False(string.IsNullOrWhiteSpace(i.TitlePtBr)));

        var deps = (await AskAsync(client, JsonRpcMessage.Request("3", Methods.DependenciesCheck, new DependenciesCheckParams([.. DependencyIds.All, "made-up"])))).ResultAs<DependenciesCheckResult>()!;
        Assert.Equal(DependencyIds.All.Count + 1, deps.Items.Count);
        Assert.Equal(CheckStatus.NotVerified, deps.Items.Single(d => d.Id == "made-up").State);

        var journal = (await AskAsync(client, JsonRpcMessage.Request("4", Methods.JournalQueryRecent, new JournalQueryRecentParams(7)))).ResultAs<JournalQueryRecentResult>()!;
        Assert.NotNull(journal.Changes);

        var profile = (await AskAsync(client, JsonRpcMessage.Request("5", Methods.SystemGetHardwareProfile, new { }))).ResultAs<HardwareProfile>()!;
        Assert.False(string.IsNullOrWhiteSpace(profile.Cpu));
    }

    [Fact]
    public async Task A_session_without_measurement_reports_no_numbers()
    {
        await using var client = await ConnectAsync();
        await AskAsync(client, Hello());
        var start = new SessionStartParams("zelda64", "Zelda", "zelda64-recompiled", "1.2.0", Environment.ProcessPath!, Environment.ProcessId, null);
        var session = (await AskAsync(client, JsonRpcMessage.Request("2", Methods.SessionStart, start))).ResultAs<SessionStartResult>()!;
        await AskAsync(client, JsonRpcMessage.Request("3", Methods.SessionEnd, new SessionEndParams(session.SessionId, 0, 125.5)));
        var report = (await AskAsync(client, JsonRpcMessage.Request("4", Methods.SessionGetReport, new SessionGetReportParams(session.SessionId)))).ResultAs<SessionReport>()!;
        Assert.False(report.Measured);
        Assert.Equal(125.5, report.DurationSeconds);
        Assert.Null(report.AverageFps);
        Assert.Null(report.OnePercentLowFps);
    }

    [Fact]
    public void Sessions_tell_the_app_when_a_game_opens_and_closes_once()
    {
        var handler = new LotusIntegrationHandler(new SqliteOptimizationJournal(Path.Combine(dbFolder, "events.db")), new NoCapture(), Serilog.Core.Logger.None);
        var started = new List<string>(); var ended = new List<(string Key, double Seconds)>();
        handler.SessionStarted += s => started.Add(s.GameTitle);
        handler.SessionEnded += (s, _, seconds) => ended.Add((s.LotusGameKey, seconds));
        var id = handler.StartSession(new SessionStartParams("ports:mm", "Majora's Mask", "2s2h", "1.0", @"C:\Ports\2ship.exe", 999999, null)).SessionId;
        handler.EndSession(new SessionEndParams(id, 0, 1800));
        handler.EndSession(new SessionEndParams(id, 0, 1900));                       // a repeated end is ignored
        handler.EndSession(new SessionEndParams("unknown", 0, 10));                  // unknown sessions too
        Assert.Equal(["Majora's Mask"], started);
        Assert.Equal([("ports:mm", 1800.0)], ended);
        var negative = handler.StartSession(new SessionStartParams("ports:x", "X", "p", null, @"C:\x.exe", 999999, null)).SessionId;
        handler.EndSession(new SessionEndParams(negative, 1, -5));
        Assert.Equal(0, ended[^1].Seconds);                                          // never a negative duration
    }

    [Fact]
    public async Task A_program_that_is_not_the_registered_Lotus_Ports_is_disconnected()
    {
        trusted = false;
        await using var client = await ConnectAsync();
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await MessageFraming.WriteAsync(client, Hello());
            if (await MessageFraming.ReadAsync(client) is null) throw new IOException("closed");
        });
    }
}
