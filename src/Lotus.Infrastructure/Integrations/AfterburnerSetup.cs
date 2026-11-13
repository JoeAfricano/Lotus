namespace Lotus.Infrastructure.Integrations;

// Read-only look at MSI Afterburner's settings for the "Ajuste da placa" assistant (nothing is ever written):
// MSIAfterburner.cfg [Settings] UnlockVoltageControl, and Profiles\MSIAfterburner.cfg Profile2D / Profile3D (-1 = off).
public static class AfterburnerSetup
{
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "MSI Afterburner");
    public static string Exe => Path.Combine(Dir, "MSIAfterburner.exe");
    public static bool Installed => File.Exists(Exe);

    public static bool VoltageUnlocked(string? dir = null) => Value(Path.Combine(dir ?? Dir, "MSIAfterburner.cfg"), "UnlockVoltageControl") is { } v && v != "0";
    public static bool AutoProfiles(string? dir = null) =>
        int.TryParse(Value(Path.Combine(dir ?? Dir, "Profiles", "MSIAfterburner.cfg"), "Profile3D"), out var p3) && p3 > 0;

    // "Key = value" or "Key=value" (Afterburner uses both, with tabs).
    private static string? Value(string path, string key)
    {
        try
        {
            if (!File.Exists(path)) return null;
            foreach (var line in File.ReadLines(path))
            {
                var eq = line.IndexOf('=');
                if (eq > 0 && line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return line[(eq + 1)..].Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Serilog.Log.Debug(ex, "Afterburner settings"); }
        return null;
    }
}
