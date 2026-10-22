using System.Text.Json;
using Lotus.Core;
using Serilog;

namespace Lotus.Infrastructure;

public sealed class JsonPreferencesStore(string path, ILogger logger) : IPreferencesStore
{
    private const int MaxBytes = 65_536;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public PreferencesLoadResult Load()
    {
        try
        {
            if (!File.Exists(path)) return new(new(), "Missing");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxBytes) return Fallback("Oversized");
            var value = JsonSerializer.Deserialize<InterfacePreferences>(stream);
            if (value is null || value.SchemaVersion != 1) return Fallback("UnsupportedSchema");
            return new(value.Normalize(), value == value.Normalize() ? "Loaded" : "Normalized");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.Warning(ex, "Preferences load failed {ReasonCode}", "InvalidOrUnavailable");
            return new(new(), "InvalidOrUnavailable");
        }
    }
    private PreferencesLoadResult Fallback(string reason)
    {
        logger.Warning("Preferences defaults used {ReasonCode}", reason);
        return new(new(), reason);
    }
    public bool TrySave(InterfacePreferences preferences)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, preferences.Normalize(), Options);
                stream.Flush(flushToDisk: true);
            }
            // A failed replacement never truncates the last complete settings file.
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
            logger.Information("Preferences saved {Language} {Theme}", preferences.Language, preferences.Theme);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Warning(ex, "Preferences save failed");
            return false;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { logger.Warning(ex, "Preferences temporary cleanup failed"); }
        }
    }
}
