using System.Diagnostics;
using System.Runtime.Versioning;
using Lotus.Core.Games;

namespace Lotus.Infrastructure.Games;

// Runs the user's installed SCSKiller CLI (separate GPL-3.0 program) hidden, with fixed arguments only.
[SupportedOSPlatform("windows")]
public sealed class ScsKillerCli(string? cliPath = null) : IShaderPrecompiler
{
    private string Path => cliPath ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCSKiller.App", "current", "cli", "scskiller.exe");
    public bool IsAvailable => File.Exists(Path);
    private static string AppPath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCSKiller.App", "SCSKiller.exe");

    public bool OpenApp()
    {
        if (!File.Exists(AppPath)) return false;
        Process.Start(new ProcessStartInfo(AppPath) { UseShellExecute = true })?.Dispose();
        return true;
    }

    public async Task<IReadOnlyList<ShaderStatus>> StatusAsync(CancellationToken token)
    {
        if (!IsAvailable) return [];
        var output = new System.Text.StringBuilder();
        var code = await RunAsync(["status"], line => output.AppendLine(line), token);
        return code == 0 ? ScsKillerOutput.ParseStatus(output.ToString()) : [];
    }

    // Background priority (--idle) so the PC stays usable while the driver compiles.
    public Task<int> CompileAsync(string id, IProgress<string> progress, CancellationToken token)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, @"^[a-z]+:[A-Za-z0-9._\-]+$")) throw new ArgumentException("Invalid game id.", nameof(id));
        return RunAsync(["compile", id, "--idle"], progress.Report, token);
    }

    private async Task<int> RunAsync(string[] args, Action<string> onLine, CancellationToken token)
    {
        var info = new ProcessStartInfo(Path) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("SCSKiller could not be started.");
        process.OutputDataReceived += (_, e) => { if (e.Data is { Length: > 0 } d) onLine(d); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 } d) onLine(d); };
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } throw; }
        return process.ExitCode;
    }
}
