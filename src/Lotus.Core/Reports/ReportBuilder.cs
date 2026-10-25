using System.Net;
using System.Text;

namespace Lotus.Core.Reports;

// Custom report: the player picks the sections; Lotus writes one self-contained HTML page (opens in any browser,
// prints to PDF). Only facts Lotus already has on screen; nothing is sent anywhere.
public sealed record ReportRow(string Label, string Value);
public sealed record ReportSection(string Title, IReadOnlyList<ReportRow> Rows, string? Note = null);

public static class ReportBuilder
{
    public static string Html(string title, string subtitle, IReadOnlyList<ReportSection> sections, string footer)
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
          .Append("<title>").Append(E(title)).Append("</title><style>")
          .Append(":root{--bg:#12090f;--card:#1f0f19;--line:#3a2230;--text:#f8f0f3;--muted:#b3a1ac;--rose:#f27ba8;--gold:#eec1a8}")
          .Append("*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:15px/1.5 'Segoe UI',system-ui,sans-serif;padding:40px 16px}")
          .Append("main{max-width:880px;margin:0 auto}header{border-bottom:1px solid var(--line);padding-bottom:20px;margin-bottom:28px}")
          .Append(".brand{color:var(--rose);letter-spacing:.2em;font-weight:700;font-size:12px}h1{margin:6px 0 4px;font-size:30px}")
          .Append(".sub{color:var(--muted)}section{background:var(--card);border:1px solid var(--line);border-radius:16px;padding:20px 24px;margin:0 0 18px;break-inside:avoid}")
          .Append("h2{margin:0 0 12px;font-size:17px;color:var(--gold)}table{width:100%;border-collapse:collapse}td{padding:7px 0;border-top:1px solid var(--line);vertical-align:top}")
          .Append("tr:first-child td{border-top:0}td:first-child{color:var(--muted);width:38%;padding-right:16px}td:last-child{font-weight:600}")
          .Append(".note{color:var(--muted);font-size:13px;margin-top:10px}footer{color:var(--muted);font-size:12px;margin-top:28px;text-align:center}")
          .Append("@media print{body{background:#fff;color:#111}section{background:#fff;border-color:#ddd}td:first-child,.sub,.note,footer{color:#555}h2{color:#a8124f}}")
          .Append("</style></head><body><main><header><div class=\"brand\">LOTUS</div><h1>").Append(E(title)).Append("</h1><div class=\"sub\">").Append(E(subtitle)).Append("</div></header>");
        foreach (var s in sections.Where(s => s.Rows.Count > 0 || s.Note is not null))
        {
            sb.Append("<section><h2>").Append(E(s.Title)).Append("</h2>");
            if (s.Rows.Count > 0)
            {
                sb.Append("<table>");
                foreach (var r in s.Rows) sb.Append("<tr><td>").Append(E(r.Label)).Append("</td><td>").Append(E(r.Value)).Append("</td></tr>");
                sb.Append("</table>");
            }
            if (s.Note is { Length: > 0 } n) sb.Append("<div class=\"note\">").Append(E(n)).Append("</div>");
            sb.Append("</section>");
        }
        sb.Append("<footer>").Append(E(footer)).Append("</footer></main></body></html>");
        return sb.ToString();
    }

    // Report file name from the title, safe for Windows.
    public static string FileName(string title, DateTime when)
    {
        var safe = new string(title.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray()).Trim();
        if (safe.Length == 0) safe = "Lotus";
        if (safe.Length > 60) safe = safe[..60];
        return $"{safe} {when:yyyy-MM-dd HHmm}.html";
    }
}
