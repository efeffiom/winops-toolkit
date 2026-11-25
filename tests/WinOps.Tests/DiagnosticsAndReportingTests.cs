using WinOps.Core.Abstractions;
using WinOps.Core.Diagnostics;
using WinOps.Core.Models;
using WinOps.Core.Reporting;
using WinOps.Services.Diagnostics;
using Xunit;

namespace WinOps.Tests;

public sealed class DiagnosticsAndReportingTests
{
    [Fact]
    public async Task DiagnosticEngine_RunsStepsAndAggregatesFindings()
    {
        var engine = new DiagnosticEngine();
        engine.Register(new DiagnosticScenario(
            "unit", "Unit scenario", "Tests", "Verifies step aggregation.",
            [
                new DelegateDiagnosticStep("one", _ => Task.FromResult<IReadOnlyList<DiagnosticFinding>>(
                [
                    new DiagnosticFinding("one.ok", "Check one", Severity.Info, CheckStatus.Passed, "Passed", "evidence", "none", "", false, "")
                ])),
                new DelegateDiagnosticStep("two", _ => Task.FromResult<IReadOnlyList<DiagnosticFinding>>(
                [
                    new DiagnosticFinding("two.warn", "Check two", Severity.Medium, CheckStatus.Warning, "Warning", "evidence", "review", "", false, "")
                ]))
            ]));

        var progress = new List<OperationProgress>();
        var report = await engine.RunAsync("unit", "PC01", "tech", "Lab", new Progress<OperationProgress>(progress.Add));

        Assert.Equal(2, report.Findings.Count);
        Assert.Equal(Severity.Medium, report.OverallSeverity);
        Assert.Contains(progress, p => p.Percent == 100);
    }

    [Fact]
    public async Task DiagnosticEngine_ConvertsStepFailuresIntoEvidence()
    {
        var engine = new DiagnosticEngine();
        engine.Register(new DiagnosticScenario(
            "broken", "Broken scenario", "Tests", "Verifies fault isolation.",
            [new DelegateDiagnosticStep("throws", _ => throw new InvalidOperationException("simulated"))]));

        var report = await engine.RunAsync("broken", "PC01", "tech", "Lab");

        var finding = Assert.Single(report.Findings);
        Assert.Equal(CheckStatus.Failed, finding.Status);
        Assert.Contains("simulated", finding.Evidence);
    }

    [Fact]
    public async Task ReportExporter_WritesAllSupportedFormats()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winops-report-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var report = CreateReport();
            var results = new List<ExportResult>();
            foreach (var format in new[] { "json", "csv", "html", "pdf" })
                results.Add(await ReportExporter.ExportAsync(report, format, directory, "tech"));

            Assert.Equal(4, results.Count);
            Assert.All(results, r => Assert.True(File.Exists(r.FilePath)));
            Assert.All(results, r => Assert.True(r.BytesWritten > 0));
            Assert.Contains("json", results.Select(r => r.Format));
            Assert.Contains("pdf", results.Select(r => r.Format));

            var html = await File.ReadAllTextAsync(results.Single(r => r.Format == "html").FilePath);
            Assert.Contains("WinOps Toolkit", html);
            Assert.Contains("DNS resolution", html);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ReportExporter_PdfStartsWithPdfHeader()
    {
        var bytes = ReportExporter.BuildPdf(CreateReport());
        var header = System.Text.Encoding.ASCII.GetString(bytes, 0, 8);
        Assert.StartsWith("%PDF-1.4", header);
    }

    [Fact]
    public void DiagnosticFinding_VerifiedSetsAuditFields()
    {
        var finding = new DiagnosticFinding("id", "title", Severity.High, CheckStatus.Failed, "detail", "evidence", "recommendation", "action", false, "");
        var verified = finding.Verified("service running after restart");
        Assert.True(verified.RemediationApplied);
        Assert.Equal("service running after restart", verified.Verification);
        Assert.False(finding.RemediationApplied);
    }

    private static DiagnosticReport CreateReport() => new(
        "dns",
        "DNS failure",
        "PC01",
        "tech",
        "Lab",
        DateTimeOffset.Now,
        [
            new DiagnosticFinding("dns.resolve", "DNS resolution", Severity.High, CheckStatus.Failed,
                "Name could not be resolved.", "lookup failed", "Check DNS servers.", "network.flushdns", false, "")
        ],
        []);
}
