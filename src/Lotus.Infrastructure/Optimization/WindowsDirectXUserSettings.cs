using System.Runtime.Versioning;
using Lotus.Core.Optimization;
using Microsoft.Win32;

namespace Lotus.Infrastructure.Optimization;

// HKCU\Software\Microsoft\DirectX\UserGpuPreferences\DirectXUserGlobalSettings — the value Windows Settings writes.
[SupportedOSPlatform("windows")]
public sealed class WindowsDirectXUserSettings : IDirectXUserSettings
{
    private const string Path = @"Software\Microsoft\DirectX\UserGpuPreferences", Name = "DirectXUserGlobalSettings";
    // The windowed-games option exists from Windows 11 (build 22000).
    public bool Supported => Environment.OSVersion.Version.Build >= 22000;
    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Path, writable: false);
        return key?.GetValue(Name) as string;
    }
    public void Write(string? value)
    {
        if (value is null)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(Path, writable: true);
            existing?.DeleteValue(Name, throwOnMissingValue: false);
            return;
        }
        using var key = Registry.CurrentUser.CreateSubKey(Path, writable: true);
        key.SetValue(Name, value, RegistryValueKind.String);
    }
}
