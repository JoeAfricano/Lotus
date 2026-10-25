namespace Lotus.Core.Score;

public enum PartKind { Cpu, Gpu }
public sealed record PartScore(string Name, PartKind Kind, int Single = 0, int Multi = 0, int Gpu = 0);

// Estimated LOTUS points per part, scaled from public relative-performance data (single/multi-core CPU, 3D GPU)
// and anchored on the calibrated PC (Ryzen 5 5500 = 1 400 / 10 500, RTX 5060 = 11 500). Guidance, not measurements.
public static class ScoreParts
{
    private static PartScore C(string name, int single, int multi) => new(name, PartKind.Cpu, single, multi);
    private static PartScore G(string name, int gpu) => new(name, PartKind.Gpu, Gpu: gpu);

    public static IReadOnlyList<PartScore> All { get; } =
    [
        C("Intel Core i3-10100", 1200, 5400), C("Intel Core i3-12100F", 1700, 8800), C("Intel Core i5-10400", 1200, 8500),
        C("Intel Core i5-11400", 1500, 10300), C("Intel Core i5-12400", 1750, 12300), C("Intel Core i5-13400", 1800, 16000),
        C("Intel Core i5-12600K", 1900, 17500), C("Intel Core i5-13600K", 2000, 24000), C("Intel Core i5-14600K", 2100, 25000),
        C("Intel Core i7-12700K", 1950, 22500), C("Intel Core i7-13700K", 2100, 30500), C("Intel Core i7-14700K", 2150, 35000),
        C("Intel Core i9-12900K", 2000, 27500), C("Intel Core i9-13900K", 2250, 38500), C("Intel Core i9-14900K", 2300, 39500),
        C("Intel Core Ultra 5 245K", 2150, 23500), C("Intel Core Ultra 7 265K", 2250, 35500), C("Intel Core Ultra 9 285K", 2300, 42000),
        C("AMD Ryzen 5 2600", 1050, 7200), C("AMD Ryzen 5 3600", 1270, 9300), C("AMD Ryzen 5 5500", 1400, 10500),
        C("AMD Ryzen 5 5600", 1500, 11000), C("AMD Ryzen 5 5600X", 1550, 11300), C("AMD Ryzen 7 5700X", 1500, 13800),
        C("AMD Ryzen 7 5800X", 1550, 15200), C("AMD Ryzen 7 5800X3D", 1450, 14500), C("AMD Ryzen 9 5900X", 1600, 21500),
        C("AMD Ryzen 9 5950X", 1650, 28500), C("AMD Ryzen 5 7600", 1850, 14500), C("AMD Ryzen 5 7600X", 1950, 15000),
        C("AMD Ryzen 7 7700X", 2000, 19700), C("AMD Ryzen 7 7800X3D", 1800, 18000), C("AMD Ryzen 9 7900X", 2000, 29000),
        C("AMD Ryzen 9 7950X", 2050, 38500), C("AMD Ryzen 5 9600X", 2150, 16500), C("AMD Ryzen 7 9700X", 2150, 20500),
        C("AMD Ryzen 7 9800X3D", 2050, 23000), C("AMD Ryzen 9 9950X", 2250, 42000),

        G("NVIDIA GeForce GTX 1050 Ti", 2400), G("NVIDIA GeForce GTX 1650", 3500), G("NVIDIA GeForce GTX 1650 Super", 5000),
        G("NVIDIA GeForce GTX 1660", 5400), G("NVIDIA GeForce GTX 1660 Super", 6000), G("NVIDIA GeForce RTX 2060", 7600),
        G("NVIDIA GeForce RTX 2060 Super", 8800), G("NVIDIA GeForce RTX 2070 Super", 10000), G("NVIDIA GeForce RTX 2080 Ti", 13500),
        G("NVIDIA GeForce RTX 3050", 6200), G("NVIDIA GeForce RTX 3060", 9000), G("NVIDIA GeForce RTX 3060 Ti", 11500),
        G("NVIDIA GeForce RTX 3070", 13500), G("NVIDIA GeForce RTX 3080", 17500), G("NVIDIA GeForce RTX 3090", 19500),
        G("NVIDIA GeForce RTX 4060", 10500), G("NVIDIA GeForce RTX 4060 Ti", 13500), G("NVIDIA GeForce RTX 4070", 17500),
        G("NVIDIA GeForce RTX 4070 Super", 20000), G("NVIDIA GeForce RTX 4070 Ti Super", 24000), G("NVIDIA GeForce RTX 4080", 28000),
        G("NVIDIA GeForce RTX 4090", 36000), G("NVIDIA GeForce RTX 5060", 11500), G("NVIDIA GeForce RTX 5060 Ti", 15000),
        G("NVIDIA GeForce RTX 5070", 22000), G("NVIDIA GeForce RTX 5070 Ti", 27000), G("NVIDIA GeForce RTX 5080", 32000),
        G("NVIDIA GeForce RTX 5090", 48000),
        G("AMD Radeon RX 580", 4500), G("AMD Radeon RX 5600 XT", 7700), G("AMD Radeon RX 5700 XT", 9300), G("AMD Radeon RX 6600", 8100),
        G("AMD Radeon RX 6650 XT", 10000), G("AMD Radeon RX 6700 XT", 12500), G("AMD Radeon RX 6800", 16000), G("AMD Radeon RX 6800 XT", 19000),
        G("AMD Radeon RX 6900 XT", 20500), G("AMD Radeon RX 7600", 11000), G("AMD Radeon RX 7700 XT", 17000), G("AMD Radeon RX 7800 XT", 20000),
        G("AMD Radeon RX 7900 GRE", 21500), G("AMD Radeon RX 7900 XT", 26500), G("AMD Radeon RX 7900 XTX", 30000),
        G("AMD Radeon RX 9070", 26000), G("AMD Radeon RX 9070 XT", 29000), G("AMD Radeon 780M (integrada)", 2900),
        G("Intel Arc A750", 13000), G("Intel Arc B580", 14500), G("Intel UHD Graphics 770 (integrada)", 900),
    ];

    public static string Normalize(string s) => string.Join(' ', s.ToLowerInvariant().Replace("geforce", "").Replace("radeon", "")
        .Replace("(tm)", "").Replace("(r)", "").Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries));
    public static string[] Terms(string query) => Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    // The part whose model number is contained in the Windows name (longest match wins: "5060 Ti" over "5060").
    public static PartScore? Find(string windowsName, PartKind kind)
    {
        var name = " " + Normalize(windowsName) + " ";
        return All.Where(p => p.Kind == kind && name.Contains(" " + Model(p.Name) + " ")).OrderByDescending(p => Model(p.Name).Length).FirstOrDefault();
    }
    private static string Model(string name)
    {
        var n = Normalize(name.Replace("(integrada)", ""));
        foreach (var prefix in new[] { "nvidia ", "amd ", "intel core ", "intel " }) if (n.StartsWith(prefix)) n = n[prefix.Length..];
        return n;
    }
}
