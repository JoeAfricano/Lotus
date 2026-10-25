using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lotus.Core.Reports;

// Problems Lotus wrote to its own log (warnings and errors), newest first, for the end of the report.
public sealed record LogEntry(DateTimeOffset When, string Level, string Message, string? Detail);

public static partial class LogDigest
{
    public static IReadOnlyList<LogEntry> Read(string folder, DateTimeOffset since, int max = 50)
    {
        var list = new List<LogEntry>();
        if (!Directory.Exists(folder)) return list;
        foreach (var file in Directory.EnumerateFiles(folder, "lotus-*.jsonl"))
        {
            try
            {
                // The running app keeps the current file open for writing.
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                while (reader.ReadLine() is { } line)
                    if (line.Contains("\"Level\":\"Warning\"") || line.Contains("\"Level\":\"Error\"") || line.Contains("\"Level\":\"Fatal\""))
                        if (Parse(line) is { } e && e.When >= since) list.Add(e);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return [.. list.OrderByDescending(e => e.When).Take(max)];
    }

    public static LogEntry? Parse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            if (!r.TryGetProperty("Timestamp", out var ts) || !ts.TryGetDateTimeOffset(out var when)) return null;
            var level = r.TryGetProperty("Level", out var l) ? l.GetString() ?? "" : "";
            var template = r.TryGetProperty("MessageTemplate", out var m) ? m.GetString() ?? "" : "";
            var props = r.TryGetProperty("Properties", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
            var message = Hole().Replace(template, h =>
                props.ValueKind == JsonValueKind.Object && props.TryGetProperty(h.Groups[1].Value, out var v)
                    ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()) ?? "" : h.Value);
            string? detail = null;
            if (r.TryGetProperty("Exception", out var ex) && ex.GetString() is { Length: > 0 } text)
                detail = text.Split('\n', 2)[0].Trim(); // first line: type and message, no stack trace
            return new(when, level, message, detail);
        }
        catch (JsonException) { return null; }
    }

    [GeneratedRegex(@"\{@?([A-Za-z0-9_]+)(?::[^}]*)?\}")] private static partial Regex Hole();
}
