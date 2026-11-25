using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using WinOps.Core.Models;

namespace WinOps.Core.Reporting;

/// <summary>Exports diagnostic results to professional, shareable formats.</summary>
public static class ReportExporter
{
    public static async Task<ExportResult> ExportAsync(DiagnosticReport report, string format, string directory, string? technician = null)
    {
        Directory.CreateDirectory(directory);
        var safeName = string.Concat(report.ScenarioId.Where(char.IsLetterOrDigit));
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "diagnostic";
        var stamp = report.GeneratedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var extension = format.ToLowerInvariant() switch
        {
            "csv" => "csv",
            "html" => "html",
            "pdf" => "pdf",
            _ => "json"
        };
        var path = Path.Combine(directory, $"WinOps-{safeName}-{stamp}.{extension}");
        var bytes = extension == "pdf"
            ? BuildPdf(report, technician)
            : Encoding.UTF8.GetBytes(extension switch
            {
                "csv" => BuildCsv(report, technician),
                "html" => BuildHtml(report, technician),
                _ => BuildJson(report, technician)
            });
        await File.WriteAllBytesAsync(path, bytes);
        return new ExportResult(extension, path, bytes.Length);
    }

    public static string BuildJson(DiagnosticReport report, string? technician = null) =>
        JsonSerializer.Serialize(new
        {
            report.ScenarioId,
            report.ScenarioTitle,
            report.ComputerName,
            Technician = technician ?? report.Technician,
            report.Environment,
            report.GeneratedAt,
            OverallSeverity = report.OverallSeverity.ToString(),
            report.Findings,
            report.ActionsPerformed
        }, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    public static string BuildCsv(DiagnosticReport report, string? technician = null)
    {
        static string Cell(string value) => "\"" + value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
        var sb = new StringBuilder();
        sb.AppendLine("Scenario,Computer,Technician,Environment,Generated (UTC),Finding,Title,Severity,Status,Detail,Evidence,Recommendation,Remediation,Verification");
        foreach (var f in report.Findings)
        {
            sb.Append(Cell(report.ScenarioTitle)).Append(',')
              .Append(Cell(report.ComputerName)).Append(',')
              .Append(Cell(technician ?? report.Technician)).Append(',')
              .Append(Cell(report.Environment)).Append(',')
              .Append(Cell(report.GeneratedAt.ToString("u"))).Append(',')
              .Append(Cell(f.Id)).Append(',')
              .Append(Cell(f.Title)).Append(',')
              .Append(Cell(f.Severity.ToString())).Append(',')
              .Append(Cell(f.Status.ToString())).Append(',')
              .Append(Cell(f.Detail)).Append(',')
              .Append(Cell(f.Evidence)).Append(',')
              .Append(Cell(f.Recommendation)).Append(',')
              .Append(Cell(f.RemediationActionId)).Append(',')
              .Append(Cell(f.Verification)).AppendLine();
        }
        return sb.ToString();
    }

    public static string BuildHtml(DiagnosticReport report, string? technician = null)
    {
        static string E(string? value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
        var rows = new StringBuilder();
        foreach (var f in report.Findings)
        {
            var css = f.Status == CheckStatus.Failed ? "failed" : f.Status == CheckStatus.Passed ? "passed" : "warning";
            rows.Append("<tr class='").Append(css).Append("'><td>").Append(E(f.Title)).Append("</td><td>")
                .Append(E(f.Severity.ToString())).Append("</td><td>").Append(E(f.Status.ToString())).Append("</td><td>")
                .Append(E(f.Detail)).Append("<br><small>").Append(E(f.Evidence)).Append("</small></td><td>")
                .Append(E(f.Recommendation)).Append("</td></tr>");
        }
        return $$"""
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>WinOps Toolkit - {{E(report.ScenarioTitle)}}</title>
<style>
body{font-family:'Segoe UI',Arial,sans-serif;margin:32px;color:#1b2430;background:#f6f8fb}
.card{background:#fff;border-radius:10px;padding:24px;box-shadow:0 2px 10px #00000012}
h1{margin:0 0 4px;font-size:24px}h2{font-size:16px;margin-top:24px}
table{width:100%;border-collapse:collapse;font-size:13px}th,td{padding:9px;border-bottom:1px solid #e3e8ef;text-align:left;vertical-align:top}
th{background:#eef2f7}.failed{color:#a4262c}.passed{color:#107c10}.warning{color:#9c6500}
small{color:#5a6878}footer{margin-top:24px;color:#5a6878;font-size:12px}
</style></head><body><div class="card">
<h1>WinOps Toolkit - {{E(report.ScenarioTitle)}}</h1>
<p>Computer: <b>{{E(report.ComputerName)}}</b> &nbsp;|&nbsp; Technician: <b>{{E(technician ?? report.Technician)}}</b> &nbsp;|&nbsp; Environment: <b>{{E(report.Environment)}}</b><br>
Generated: {{E(report.GeneratedAt.ToString("u"))}} &nbsp;|&nbsp; Overall severity: <b>{{E(report.OverallSeverity.ToString())}}</b></p>
<h2>Findings</h2><table><thead><tr><th>Check</th><th>Severity</th><th>Status</th><th>Detail / Evidence</th><th>Recommendation</th></tr></thead>
<tbody>{{rows}}</tbody></table>
<h2>Actions performed</h2><p>{{E(report.ActionsPerformed.Count == 0 ? "None" : string.Join("; ", report.ActionsPerformed))}}</p>
<footer>Generated by WinOps Toolkit. Findings reflect collected evidence at the time of the scan.</footer>
</div></body></html>
""";
    }

    public static byte[] BuildPdf(DiagnosticReport report, string? technician = null)
    {
        var lines = new List<string>
        {
            "WinOps Toolkit - Diagnostic Report",
            report.ScenarioTitle,
            $"Computer: {report.ComputerName}",
            $"Technician: {technician ?? report.Technician}",
            $"Environment: {report.Environment}",
            $"Generated: {report.GeneratedAt.ToString("u")}",
            $"Overall severity: {report.OverallSeverity}",
            string.Empty,
            "FINDINGS",
            string.Empty
        };
        foreach (var f in report.Findings)
        {
            lines.Add($"{f.Severity}/{f.Status}: {f.Title}");
            lines.Add("  " + Wrap(f.Detail, 92));
            if (!string.IsNullOrWhiteSpace(f.Evidence)) lines.Add("  Evidence: " + Wrap(f.Evidence, 86));
            if (!string.IsNullOrWhiteSpace(f.Recommendation)) lines.Add("  Action: " + Wrap(f.Recommendation, 88));
            lines.Add(string.Empty);
        }
        lines.Add("ACTIONS PERFORMED");
        lines.Add(report.ActionsPerformed.Count == 0 ? "None" : string.Join("; ", report.ActionsPerformed));

        var physicalPages = new List<List<string>>();
        var current = new List<string>();
        foreach (var line in lines)
        {
            foreach (var piece in line.Split('\n'))
            {
                if (current.Count >= 56) { physicalPages.Add(current); current = []; }
                current.Add(piece);
            }
        }
        if (current.Count > 0) physicalPages.Add(current);
        if (physicalPages.Count == 0) physicalPages.Add(["No findings"]);

        var objects = new List<string>();
        var pageIds = new List<int>();
        var contentIds = new List<int>();
        // 1 = catalog, 2 = pages, 3 = font, then alternating page/content objects.
        int nextId = 4;
        foreach (var _ in physicalPages)
        {
            pageIds.Add(nextId++);
            contentIds.Add(nextId++);
        }

        var fontId = 3;
        objects.Add(string.Empty); // placeholder for object 1
        objects.Add(string.Empty); // placeholder for object 2
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        for (var p = 0; p < physicalPages.Count; p++)
        {
            var stream = new StringBuilder("BT /F1 10 Tf 48 744 Td 13 TL\n");
            foreach (var line in physicalPages[p])
            {
                var escaped = line.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
                stream.Append('(').Append(escaped).Append(") Tj T*\n");
            }
            stream.Append("ET\n");
            var content = stream.ToString();
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {fontId} 0 R >> >> /Contents {contentIds[p]} 0 R >>");
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream");
        }

        objects[0] = "<< /Type /Catalog /Pages 2 0 R >>";
        objects[1] = $"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(id => $"{id} 0 R"))}] /Count {pageIds.Count} >>";

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append("xref\n0 ").Append(objects.Count + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets) pdf.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        pdf.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private static string Wrap(string text, int width)
    {
        var words = string.IsNullOrWhiteSpace(text) ? [""] : text.Split([' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        var lineLength = 0;
        foreach (var word in words)
        {
            if (lineLength + word.Length + 1 > width)
            {
                sb.Append("\n  ");
                lineLength = 2;
            }
            else if (lineLength > 0)
            {
                sb.Append(' ');
                lineLength++;
            }
            sb.Append(word);
            lineLength += word.Length;
        }
        return sb.ToString();
    }
}
