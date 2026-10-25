using System.Diagnostics;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lotus.Core.Tools;
using Microsoft.Win32;

namespace Lotus.Infrastructure.Tools;

// DNS benchmark (UDP queries from this PC) and IPv4 DNS change on the internet adapter only, through the elevated helper.
// Only the providers below can be set; the previous setting (DHCP or the old servers) is saved and restored exactly.
public static partial class DnsProviders
{
    public static readonly IReadOnlyList<DnsProvider> All =
    [
        new("cloudflare", "Cloudflare", "1.1.1.1", "1.0.0.1"),
        new("google", "Google", "8.8.8.8", "8.8.4.4"),
        new("quad9", "Quad9", "9.9.9.9", "149.112.112.112"),
        new("opendns", "OpenDNS", "208.67.222.222", "208.67.220.220"),
    ];
    [GeneratedRegex(@"^\{[0-9A-Fa-f\-]{36}\}$")] public static partial Regex AdapterId();
}

public sealed record DnsBackup(string AdapterId, string[]? Servers); // null = automatic (DHCP)

[SupportedOSPlatform("windows")]
public sealed class WindowsDnsService(string? dataRoot = null, Func<string[], Task<int>>? elevatedRunner = null) : IDnsService
{
    private string DataRoot => dataRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus");
    private string BackupPath => Path.Combine(DataRoot, "DnsBackup.json");
    private static readonly string[] Domains = ["www.google.com", "store.steampowered.com", "www.epicgames.com", "www.youtube.com", "discord.com"];

    public DnsState? Current()
    {
        if (InternetAdapter() is not { } a) return null;
        return new(a.Description, a.Servers, a.Automatic, HasBackup());
    }
    public bool HasBackup() => File.Exists(BackupPath);

    // Median response time of several real lookups per resolver; the current one is measured too.
    public async Task<IReadOnlyList<DnsResult>> BenchmarkAsync(CancellationToken token, IProgress<double>? progress = null)
    {
        var targets = new List<(string Id, string Name, string Ip)>();
        if (Current() is { Servers.Count: > 0 } c) targets.Add(("current", c.Servers[0], c.Servers[0]));
        targets.AddRange(DnsProviders.All.Select(p => (p.Id, p.Name, p.Primary)));
        var results = new List<DnsResult>();
        var done = 0; var total = targets.Count * Domains.Length * 3;
        foreach (var t in targets)
        {
            var times = new List<double>(); var failed = 0;
            for (var round = 0; round < 3; round++)
                foreach (var d in Domains)
                {
                    token.ThrowIfCancellationRequested();
                    if (await QueryAsync(t.Ip, d, token) is { } ms) times.Add(ms); else failed++;
                    progress?.Report(++done * 100.0 / total);
                }
            times.Sort();
            results.Add(new(t.Id, t.Name, t.Ip, times.Count > 0 ? times[times.Count / 2] : null, failed * 100.0 / (Domains.Length * 3)));
        }
        return results;
    }

    // One A-record query over UDP; null on timeout or error.
    public static async Task<double?> QueryAsync(string server, string name, CancellationToken token)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        var id = (ushort)Random.Shared.Next(ushort.MaxValue);
        var packet = new List<byte> { (byte)(id >> 8), (byte)id, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 };
        foreach (var label in name.Split('.')) { packet.Add((byte)label.Length); packet.AddRange(System.Text.Encoding.ASCII.GetBytes(label)); }
        packet.AddRange(new byte[] { 0, 0, 1, 0, 1 });
        var sw = Stopwatch.StartNew();
        try
        {
            await udp.SendAsync(packet.ToArray(), new IPEndPoint(IPAddress.Parse(server), 53), token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(1000);
            var reply = await udp.ReceiveAsync(timeout.Token);
            return reply.Buffer.Length > 3 && reply.Buffer[0] == (byte)(id >> 8) && reply.Buffer[1] == (byte)id && (reply.Buffer[3] & 0x0F) == 0 ? sw.Elapsed.TotalMilliseconds : null;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        catch (SocketException) { return null; }
    }

    public async Task<bool> ApplyAsync(string providerId)
    {
        if (InternetAdapter() is not { } a || DnsProviders.All.All(p => p.Id != providerId)) return false;
        var code = await RunElevated(["--dns-set", a.Id, providerId]);
        var provider = DnsProviders.All.First(p => p.Id == providerId);
        return code == 0 && InternetAdapter()?.Servers.FirstOrDefault() == provider.Primary;
    }
    public async Task<bool> RestoreAsync() => HasBackup() && await RunElevated(["--dns-restore"]) == 0 && !HasBackup();

    private Task<int> RunElevated(string[] args) => elevatedRunner?.Invoke(args) ?? Task.Run(() =>
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
        foreach (var a in args) info.ArgumentList.Add(a);
        info.ArgumentList.Add("--lotus-data"); info.ArgumentList.Add(DataRoot);
        try { using var p = Process.Start(info)!; p.WaitForExit(); return p.ExitCode; }
        catch (System.ComponentModel.Win32Exception) { return 1223; } // UAC declined
    });

    public sealed record Adapter(string Id, string Description, IReadOnlyList<string> Servers, bool Automatic);
    // The enabled IPv4 adapter with a default gateway (the one games use).
    public static Adapter? InternetAdapter()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT SettingID, Description, DNSServerSearchOrder, DefaultIPGateway FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");
            foreach (ManagementObject m in searcher.Get())
                using (m)
                {
                    if (m["DefaultIPGateway"] is not string[] { Length: > 0 } || m["SettingID"] is not string id) continue;
                    var servers = (m["DNSServerSearchOrder"] as string[] ?? []).Where(s => IPAddress.TryParse(s, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork).ToList();
                    return new(id, m["Description"]?.ToString() ?? "?", servers, StaticServers(id) is null);
                }
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException) { }
        return null;
    }
    // Static servers set by the user, or null when Windows takes them from the router (DHCP).
    public static string[]? StaticServers(string adapterId)
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + adapterId);
        return k?.GetValue("NameServer") is string s && s.Trim().Length > 0 ? s.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries) : null;
    }
}

// Elevated side: "--dns-set <adapter> <provider>" and "--dns-restore". Fixed allow-list; backup kept once in the user's Lotus folder.
[SupportedOSPlatform("windows")]
public static class DnsElevatedHelper
{
    [DllImport("dnsapi.dll")] private static extern bool DnsFlushResolverCache();

    public static int? TryRun(string[] args)
    {
        var i = Array.FindIndex(args, a => a is "--dns-set" or "--dns-restore");
        if (i < 0) return null;
        var backupPath = Path.Combine(Integrations.RtssElevatedHelper.DataRoot(args), "DnsBackup.json");
        try
        {
            if (args[i] == "--dns-set")
            {
                if (i + 2 >= args.Length || !DnsProviders.AdapterId().IsMatch(args[i + 1])) return 3;
                var provider = DnsProviders.All.FirstOrDefault(p => p.Id == args[i + 2]);
                if (provider is null) return 3;
                if (!File.Exists(backupPath))
                    File.WriteAllText(backupPath, JsonSerializer.Serialize(new DnsBackup(args[i + 1], WindowsDnsService.StaticServers(args[i + 1]))));
                if (!Set(args[i + 1], [provider.Primary, provider.Secondary])) return 2;
            }
            else
            {
                if (!File.Exists(backupPath)) return 4;
                var backup = JsonSerializer.Deserialize<DnsBackup>(File.ReadAllText(backupPath));
                if (backup is null || !DnsProviders.AdapterId().IsMatch(backup.AdapterId)) return 3;
                if (backup.Servers?.Any(s => !IPAddress.TryParse(s, out _)) == true) return 3;
                if (!Set(backup.AdapterId, backup.Servers)) return 2;
                File.Delete(backupPath);
            }
            DnsFlushResolverCache();
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ManagementException or COMException) { return 2; }
    }

    // Win32_NetworkAdapterConfiguration.SetDNSServerSearchOrder: null servers = back to automatic (DHCP).
    private static bool Set(string adapterId, string[]? servers)
    {
        using var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_NetworkAdapterConfiguration WHERE SettingID = '{adapterId}'");
        foreach (ManagementObject m in searcher.Get())
            using (m)
            {
                using var input = m.GetMethodParameters("SetDNSServerSearchOrder");
                input["DNSServerSearchOrder"] = servers;
                using var output = m.InvokeMethod("SetDNSServerSearchOrder", input, null);
                return Convert.ToUInt32(output["ReturnValue"]) == 0;
            }
        return false;
    }
}
