namespace Lotus.Core.Score;

public enum PartKind { Cpu, Gpu }
// RequirementsOnly: an old part kept only to read game requirements (Steam base); left out of the "beats X%" ranking.
public sealed record PartScore(string Name, PartKind Kind, int Single = 0, int Multi = 0, int Gpu = 0, bool RequirementsOnly = false);

// Estimated LOTUS points per part, scaled from public relative-performance data (single/multi-core CPU, 3D GPU)
// and anchored on the calibrated PC (Ryzen 5 5500 = 1 400 / 10 500, RTX 5060 = 11 500). Guidance, not measurements.
public static class ScoreParts
{
    private static PartScore C(string name, int single, int multi) => new(name, PartKind.Cpu, single, multi);
    private static PartScore G(string name, int gpu) => new(name, PartKind.Gpu, Gpu: gpu);
    private static PartScore OldC(string name, int single, int multi) => new(name, PartKind.Cpu, single, multi, RequirementsOnly: true);
    private static PartScore OldG(string name, int gpu) => new(name, PartKind.Gpu, Gpu: gpu, RequirementsOnly: true);

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
        // Older parts that game requirements often cite (estimates on the same scale)
        C("Intel Core i5-2500K", 800, 3000), C("Intel Core i7-3770", 900, 4200), C("Intel Core i5-4460", 900, 3300), C("Intel Core i7-4770", 1000, 4800),
        C("Intel Core i5-6600K", 1050, 4100), C("Intel Core i7-6700K", 1150, 6000), C("Intel Core i5-8400", 1100, 6500), C("Intel Core i7-8700K", 1250, 8800),
        C("Intel Core i5-9600K", 1250, 6800), C("Intel Core i7-9700K", 1300, 9300), C("AMD Ryzen 3 1200", 700, 3000), C("AMD Ryzen 5 1600", 800, 5600),
        C("AMD Ryzen 7 1700", 800, 7000), C("AMD Ryzen 7 2700X", 950, 8500), C("AMD Ryzen 7 3700X", 1250, 11500),

        G("NVIDIA GeForce GTX 1050 Ti", 2400), G("NVIDIA GeForce GTX 960", 2600), G("NVIDIA GeForce GTX 970", 4100), G("NVIDIA GeForce GTX 1060", 4800),
        G("NVIDIA GeForce GTX 1070", 6300), G("NVIDIA GeForce GTX 1070 Ti", 7000), G("NVIDIA GeForce GTX 1080", 7600), G("NVIDIA GeForce GTX 1080 Ti", 10000),
        G("NVIDIA GeForce RTX 2070", 9300), G("NVIDIA GeForce RTX 2080", 11000), G("AMD Radeon RX 470", 3800), G("AMD Radeon RX 570", 4100),
        G("AMD Radeon RX 5500 XT", 4800), G("AMD Radeon RX Vega 56", 6300), G("AMD Radeon RX Vega 64", 7200), G("NVIDIA GeForce GTX 1650", 3500), G("NVIDIA GeForce GTX 1650 Super", 5000),
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

        // Parts that Steam requirements of older or lighter games cite (19/11/2026, for the Steam base). Same scale,
        // placed from public relative-performance charts against neighbours already in this table; rough guidance only.
        OldG("NVIDIA GeForce GT 1030", 1100), OldG("NVIDIA GeForce GTX 1050", 1900), OldG("NVIDIA GeForce GTX 750 Ti", 1550), OldG("NVIDIA GeForce GTX 750", 1300),
        OldG("NVIDIA GeForce GTX 950", 2050), OldG("NVIDIA GeForce GTX 650 Ti", 1250), OldG("NVIDIA GeForce GTX 660", 1900), OldG("NVIDIA GeForce GTX 660 Ti", 2150),
        OldG("NVIDIA GeForce GTX 670", 2300), OldG("NVIDIA GeForce GTX 680", 2500), OldG("NVIDIA GeForce GTX 760", 2200), OldG("NVIDIA GeForce GTX 770", 2600),
        OldG("NVIDIA GeForce GTX 780", 3200), OldG("NVIDIA GeForce GTX 780 Ti", 3700), OldG("NVIDIA GeForce GTX 980", 4600), OldG("NVIDIA GeForce GTX 980 Ti", 5600),
        OldG("NVIDIA GeForce GTX 1060 3GB", 4400), OldG("NVIDIA GeForce GTX 1630", 2000), OldG("NVIDIA GeForce GTX 1660 Ti", 6100),
        OldG("AMD Radeon HD 7770", 1100), OldG("AMD Radeon HD 7850", 1800), OldG("AMD Radeon HD 7870", 2100), OldG("AMD Radeon HD 7950", 2500), OldG("AMD Radeon HD 7970", 2900),
        OldG("AMD Radeon R7 260X", 1400), OldG("AMD Radeon R7 370", 1900), OldG("AMD Radeon R9 270", 2000), OldG("AMD Radeon R9 270X", 2200), OldG("AMD Radeon R9 280", 2600),
        OldG("AMD Radeon R9 280X", 2900), OldG("AMD Radeon R9 285", 2800), OldG("AMD Radeon R9 290", 3700), OldG("AMD Radeon R9 290X", 4000), OldG("AMD Radeon R9 380", 2800),
        OldG("AMD Radeon R9 380X", 3100), OldG("AMD Radeon R9 390", 4000), OldG("AMD Radeon R9 390X", 4200), OldG("AMD Radeon R9 Fury", 4900),
        OldG("AMD Radeon RX 460", 1700), OldG("AMD Radeon RX 550", 1200), OldG("AMD Radeon RX 560", 1900), OldG("AMD Radeon RX 480", 4300), OldG("AMD Radeon RX 590", 4900),
        OldG("NVIDIA GeForce GTX 460", 900), OldG("NVIDIA GeForce GTX 550 Ti", 750), OldG("NVIDIA GeForce GTX 560", 1100), OldG("NVIDIA GeForce GTX 560 Ti", 1250),
        OldG("NVIDIA GeForce GTX 650", 850), OldG("NVIDIA GeForce GTX 260", 450), OldG("NVIDIA GeForce 8800 GT", 400), OldG("NVIDIA GeForce 9800 GT", 420),
        OldG("AMD Radeon HD 5450", 60), OldG("AMD Radeon HD 5770", 650), OldG("AMD Radeon HD 6870", 1000), OldG("AMD Radeon HD 7800", 1900), OldG("AMD Radeon HD 2900 XT", 350),
        OldG("Intel HD 4000", 250),
        OldG("AMD Radeon RX 6400", 2800), OldG("AMD Radeon RX 6500 XT", 3700), OldG("Intel HD Graphics 4000", 250), OldG("Intel HD Graphics 530", 380),
        OldG("Intel HD Graphics 620", 400), OldG("Intel UHD Graphics 620", 420), OldG("Intel UHD Graphics 630", 450), OldG("Intel Iris Xe Graphics", 1100),
        OldC("Intel Core i5-2400", 750, 2700), OldC("Intel Core i5-3470", 800, 2900), OldC("Intel Core i5-3570K", 850, 3200), OldC("Intel Core i7-2600K", 800, 3900),
        OldC("Intel Core i7-3770K", 950, 4400), OldC("Intel Core i5-4590", 920, 3400), OldC("Intel Core i5-4670K", 950, 3500), OldC("Intel Core i5-4690K", 980, 3600),
        OldC("Intel Core i7-4790", 1050, 5000), OldC("Intel Core i7-4790K", 1100, 5300), OldC("Intel Core i5-6400", 900, 3000), OldC("Intel Core i5-6600", 1000, 3800),
        OldC("Intel Core i7-6700", 1100, 5600), OldC("Intel Core i5-7400", 1000, 3600), OldC("Intel Core i5-7600K", 1150, 4300), OldC("Intel Core i7-7700", 1150, 5800),
        OldC("Intel Core i7-7700K", 1220, 6100), OldC("Intel Core i3-8100", 1050, 3800), OldC("Intel Core i5-8600K", 1250, 6800), OldC("Intel Core i3-9100", 1150, 4100),
        OldC("AMD FX-4300", 520, 1800), OldC("AMD FX-6300", 550, 2800), OldC("AMD FX-8320", 570, 3700), OldC("AMD FX-8350", 600, 4000),
        OldC("AMD Ryzen 3 1300X", 760, 2900), OldC("AMD Ryzen 5 1400", 760, 4600), OldC("AMD Ryzen 5 1500X", 800, 5000), OldC("AMD Ryzen 5 2600X", 1100, 7600),
        OldC("AMD Ryzen 3 3100", 1150, 5300), OldC("AMD Ryzen 3 3300X", 1250, 5900), OldC("AMD Ryzen 5 3600X", 1300, 9500),
    ];

    public static string Normalize(string s) => string.Join(' ', s.ToLowerInvariant().Replace("geforce", "").Replace("radeon", "")
        .Replace("(tm)", "").Replace("(r)", "").Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries));
    public static string[] Terms(string query) => Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    // The part whose model number is contained in the Windows name (longest match wins: "5060 Ti" over "5060").
    public static PartScore? Find(string windowsName, PartKind kind)
    {
        var name = " " + Normalize(windowsName) + " ";
        return All.Where(p => p.Kind == kind && name.Contains(" " + Model(p.Name) + " ")).OrderByDescending(p => Model(p.Name).Length).FirstOrDefault()
            // Requirement texts often drop the brand ("GTX 1060", "RX 580"): match the bare model then.
            ?? All.Where(p => p.Kind == kind && Bare(p.Name).Length > 3 && name.Contains(" " + Bare(p.Name) + " ")).OrderByDescending(p => Bare(p.Name).Length).FirstOrDefault();
    }
    private static string Bare(string name) { var m = Model(name); foreach (var b in new[] { "geforce ", "radeon " }) if (m.StartsWith(b)) m = m[b.Length..]; return m; }
    private static string Model(string name)
    {
        var n = Normalize(name.Replace("(integrada)", ""));
        foreach (var prefix in new[] { "nvidia ", "amd ", "intel core ", "intel " }) if (n.StartsWith(prefix)) n = n[prefix.Length..];
        return n;
    }
}
