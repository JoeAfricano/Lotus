using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core.Profile;

// Programs that pair well with Lotus. Installing happens only after the player clicks and confirms, through winget
// (the Windows package manager, official sources). Programs without a winget package open their official page.
public sealed record RecommendedApp(string Id, string Name, string Glyph, string? WingetId, string Page, string[] DetectNames);

public interface IAppInstaller
{
    bool IsInstalled(RecommendedApp app);
    bool WingetAvailable();
    Task<bool> InstallAsync(RecommendedApp app, CancellationToken token); // true when the program is installed afterwards
}

public sealed partial class RecommendedAppRow(RecommendedApp app, string description) : ObservableObject
{
    public RecommendedApp App { get; } = app;
    public string Name => App.Name;
    public string Glyph => App.Glyph;
    public string Description { get; } = description;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanInstall), nameof(ShowInstalled))] private bool installed;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanInstall))] private bool busy;
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string actionLabel = "";
    public bool CanInstall => !Installed && !Busy;
    public bool ShowInstalled => Installed;
}

public static class RecommendedApps
{
    public const string ScsKillerPage = "https://github.com/BlueHeisenberg/SCSKiller/releases/latest";
    public static readonly IReadOnlyList<RecommendedApp> All =
    [
        new("afterburner", "MSI Afterburner", "", "Guru3D.Afterburner", "https://www.msi.com/Landing/afterburner/graphics-cards", ["MSI Afterburner"]),
        new("rtss", "RivaTuner Statistics Server", "", "Guru3D.RTSS", "https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/", ["RivaTuner Statistics Server"]),
        new("scskiller", "SCSKiller", "", null, ScsKillerPage, ["SCSKiller"]),
        new("hwinfo", "HWiNFO", "", "REALiX.HWiNFO", "https://www.hwinfo.com/download/", ["HWiNFO"]),
        new("nvidiaapp", "NVIDIA App", "", null, "https://www.nvidia.com/pt-br/software/nvidia-app/", ["NVIDIA app"]),
        new("discord", "Discord", "", "Discord.Discord", "https://discord.com/download", ["Discord"]),
    ];
}

// Rows for the welcome step: detection on open, install on click (the view asks for confirmation first).
public sealed class RecommendedAppsViewModel(IAppInstaller installer, ITextCatalog text, Action<Exception> log)
{
    public ITextCatalog Text { get; } = text;
    public IReadOnlyList<RecommendedAppRow> Rows { get; } = [.. RecommendedApps.All.Select(a => new RecommendedAppRow(a, text["RecApp_" + a.Id]))];

    public void Refresh()
    {
        var winget = Safe(installer.WingetAvailable);
        foreach (var r in Rows)
        {
            r.Installed = Safe(() => installer.IsInstalled(r.App));
            r.Status = r.Installed ? Text["RecInstalled"] : "";
            r.ActionLabel = Text[r.App.WingetId is not null && winget ? "RecInstall" : "RecOpenSite"];
        }
    }

    // True when the program should be installed by Lotus; false means "open the official page".
    public bool InstallsDirectly(RecommendedAppRow row) => row.App.WingetId is not null && Safe(installer.WingetAvailable);

    public async Task InstallAsync(RecommendedAppRow row, CancellationToken token = default)
    {
        if (!row.CanInstall || !InstallsDirectly(row)) return;
        row.Busy = true; row.Status = Text["RecInstalling"];
        try
        {
            var ok = await installer.InstallAsync(row.App, token);
            row.Installed = ok || Safe(() => installer.IsInstalled(row.App));
            row.Status = Text[row.Installed ? "RecInstalled" : "RecFailed"];
        }
        catch (Exception ex) { log(ex); row.Status = Text["RecFailed"]; }
        finally { row.Busy = false; }
    }

    private bool Safe(Func<bool> f) { try { return f(); } catch (Exception ex) { log(ex); return false; } }
}
