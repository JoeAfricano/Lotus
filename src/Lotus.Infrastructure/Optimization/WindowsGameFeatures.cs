using System.Runtime.Versioning;
using Lotus.Core.Optimization;
using Microsoft.Win32;

namespace Lotus.Infrastructure.Optimization;

// Read-only registry queries for per-user gaming settings exposed in Windows Settings > Gaming / Display > Graphics.
[SupportedOSPlatform("windows")]
public sealed class WindowsGameFeatures(IDisplayModes? display = null) : IGameFeatureInventory
{
    public IReadOnlyList<GameFeature> Read() =>
    [
        new("gamemode", Dword(Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", on: 1), "ms-settings:gaming-gamemode"),
        new("background-recording", Dword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled", on: 1), "ms-settings:gaming-gamedvr"),
        new("windowed-optimizations", WindowedOptimizations(), "ms-settings:display-advancedgraphics"),
        new("hags", Dword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", on: 2), "ms-settings:display-advancedgraphics"),
        new("display-refresh", Refresh(), "ms-settings:display-advanced")
    ];

    // "On" means the primary display already runs at the highest rate available at its resolution.
    private FeatureState Refresh()
    {
        try { return display?.Primary() is not { } d ? FeatureState.Unavailable : d.MaxHz - d.CurrentHz <= 1 ? FeatureState.On : FeatureState.Off; }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return FeatureState.Unavailable; }
    }

    private static FeatureState Dword(RegistryKey hive, string path, string name, int on)
    {
        try
        {
            using var key = hive.OpenSubKey(path, writable: false);
            return key?.GetValue(name) switch { int v when v == on => FeatureState.On, int => FeatureState.Off, null => FeatureState.SystemDefault, _ => FeatureState.Unavailable };
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return FeatureState.Unavailable; }
    }

    // Stored as "SwapEffectUpgradeEnable=1;" inside DirectXUserGlobalSettings.
    private static FeatureState WindowedOptimizations()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences", writable: false);
            if (key?.GetValue("DirectXUserGlobalSettings") is not string settings) return FeatureState.SystemDefault;
            // Same parser as the reversible action, so detection and action always agree.
            return WindowedOptimizationsAction.IsEnabled(settings) ? FeatureState.On
                : settings.Contains("SwapEffectUpgradeEnable", StringComparison.OrdinalIgnoreCase) ? FeatureState.Off : FeatureState.SystemDefault;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return FeatureState.Unavailable; }
    }
}
