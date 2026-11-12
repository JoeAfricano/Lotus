using System.Globalization;

namespace Lotus.Core.Tools;

// "Saiu driver de vídeo novo": checked quietly once a week (never while a game runs), told once per version.
// NVIDIA: the newest Game Ready driver from NVIDIA's own lookup, compared with the installed one. AMD and Intel offer no
// public lookup, so Lotus only notes a driver older than six months and points to the maker's page. Lotus never
// downloads or installs a driver.
public enum DriverNoticeKind { None, Newer, Old }
public sealed record DriverNoticeResult(DriverNoticeKind Kind, string Installed, string? Latest, string Url, string Key);

public static class DriverNotice
{
    public static readonly TimeSpan CheckEvery = TimeSpan.FromDays(7);
    public static readonly TimeSpan OldAfter = TimeSpan.FromDays(183);
    public const string AmdUrl = "https://www.amd.com/pt/support/download/drivers.html", IntelUrl = "https://www.intel.com.br/content/www/br/pt/download-center/home.html";

    // "581.57" > "572.83": compares the numbers players see (NVIDIA's friendly version).
    public static bool IsNewer(string latest, string installed)
    {
        static decimal? N(string v) => decimal.TryParse(v.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
        return N(latest) is { } l && N(installed) is { } i && l > i;
    }

    // lastKey: the notice already shown (so each version, or each old driver, is told once).
    public static DriverNoticeResult Decide(GpuDriver? gpu, LatestDriver? latest, string? lastKey, DateTime today)
    {
        if (gpu is null) return new(DriverNoticeKind.None, "", null, "", "");
        var installed = gpu.FriendlyVersion;
        if (gpu.Vendor == "NVIDIA")
        {
            if (latest is null || !IsNewer(latest.Version, installed)) return new(DriverNoticeKind.None, installed, latest?.Version, "", "");
            var key = "nvidia:" + latest.Version;
            return key == lastKey ? new(DriverNoticeKind.None, installed, latest.Version, latest.DetailsUrl, key) : new(DriverNoticeKind.Newer, installed, latest.Version, latest.DetailsUrl, key);
        }
        if (gpu.Vendor is "AMD" or "Intel" && gpu.Date is { } date && today - date > OldAfter)
        {
            var key = gpu.Vendor.ToLowerInvariant() + ":" + gpu.Version;
            return key == lastKey ? new(DriverNoticeKind.None, installed, null, "", key)
                : new(DriverNoticeKind.Old, installed, null, gpu.Vendor == "AMD" ? AmdUrl : IntelUrl, key);
        }
        return new(DriverNoticeKind.None, installed, null, "", "");
    }
}
