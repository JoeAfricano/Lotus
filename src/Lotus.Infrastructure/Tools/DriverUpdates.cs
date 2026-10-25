using System.Net.Http;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Xml.Linq;
using Lotus.Core.Tools;

namespace Lotus.Infrastructure.Tools;

// Outdated drivers from trusted sources only, read-only (Lotus never downloads or installs):
//  - NVIDIA graphics: NVIDIA's own driver lookup service (the same one behind nvidia.com/drivers);
//  - everything else: Windows Update's driver catalog (Microsoft), searched without installing anything.
[SupportedOSPlatform("windows")]
public sealed class TrustedDriverUpdates(HttpClient? http = null) : IDriverUpdates
{
    private readonly HttpClient client = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    private const string ProductList = "https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3";
    private const string Lookup = "https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php?func=DriverManualLookup&psid={0}&pfid={1}&osID=57&languageCode=1033&isWHQL=1&dch=1&sort1=0&numberOfResults=1";

    public async Task<DriverCheck> CheckAsync(GpuDriver? gpu, CancellationToken token, IProgress<double>? progress = null)
    {
        progress?.Report(5);
        LatestDriver? latest = null; string? gpuError = null;
        if (gpu is { Vendor: "NVIDIA" })
        {
            try { latest = await NvidiaLatestAsync(gpu.Name, token); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or System.Xml.XmlException) { gpuError = ex.GetType().Name; }
        }
        progress?.Report(40);
        IReadOnlyList<PendingDriver> others = [];
        string? wuError = null;
        try { others = await Task.Run(WindowsUpdateDrivers, token); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException) { wuError = ex.GetType().Name; }
        progress?.Report(100);
        return new(latest, others, gpuError, wuError);
    }

    public async Task<LatestDriver?> NvidiaLatestAsync(string adapterName, CancellationToken token)
    {
        var xml = XDocument.Parse(await client.GetStringAsync(ProductList, token));
        var match = xml.Descendants("LookupValue").FirstOrDefault(v => string.Equals(v.Element("Name")?.Value.Trim(), adapterName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match?.Attribute("ParentID")?.Value is not { } series || match.Element("Value")?.Value is not { } product) return null;
        using var doc = JsonDocument.Parse(await client.GetStringAsync(string.Format(Lookup, series, product), token));
        if (!doc.RootElement.TryGetProperty("IDS", out var ids) || ids.GetArrayLength() == 0) return null;
        var info = ids[0].GetProperty("downloadInfo");
        string S(string k) => info.TryGetProperty(k, out var p) ? Uri.UnescapeDataString(p.GetString() ?? "") : "";
        var details = S("DetailsURL");
        // Only NVIDIA's own pages are offered as the place to update.
        if (!Uri.TryCreate(details, UriKind.Absolute, out var u) || u.Scheme != "https" || !u.Host.EndsWith("nvidia.com", StringComparison.OrdinalIgnoreCase)) details = "https://www.nvidia.com/pt-br/drivers/";
        DateTime? date = DateTime.TryParse(S("ReleaseDateTime").Replace(",", ""), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var d) ? d : null;
        return new(S("Name"), S("Version"), date, details);
    }

    // Windows Update Agent search for drivers that are offered but not installed (no download, no install).
    private static IReadOnlyList<PendingDriver> WindowsUpdateDrivers()
    {
        var type = Type.GetTypeFromProgID("Microsoft.Update.Session") ?? throw new InvalidOperationException("Windows Update unavailable.");
        dynamic session = Activator.CreateInstance(type)!;
        dynamic result = session.CreateUpdateSearcher().Search("IsInstalled=0 and Type='Driver'");
        var list = new List<PendingDriver>();
        foreach (dynamic u in result.Updates)
        {
            DateTime? date = null; try { date = (DateTime)u.DriverVerDate; } catch (Exception) { }
            list.Add(new((string)u.Title, ((string?)u.DriverManufacturer ?? "").Trim(), date));
        }
        return list;
    }
}
