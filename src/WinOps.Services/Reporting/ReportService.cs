using WinOps.Core.Abstractions;
using WinOps.Core.Diagnostics;
using WinOps.Core.Models;
using WinOps.Core.Reporting;
using WinOps.Persistence;

namespace WinOps.Services.Reporting;

public sealed class ReportService
{
    private readonly DiagnosticEngine _engine;
    private readonly DiagnosticRunRepository _runs;
    private readonly SettingsRepository _settings;
    private readonly ISystemInformationProvider _system;
    private readonly string _reportDirectory;

    public ReportService(
        DiagnosticEngine engine,
        DiagnosticRunRepository runs,
        SettingsRepository settings,
        ISystemInformationProvider system)
    {
        _engine = engine;
        _runs = runs;
        _settings = settings;
        _system = system;
        _reportDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WinOps Toolkit", "Reports");
    }

    public string ReportDirectory => _reportDirectory;

    public async Task<DiagnosticReport> RunAsync(string scenarioId, IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        var info = await _system.GetSystemInfoAsync(ct);
        var technician = await _settings.GetAsync("TechnicianName", Environment.UserName, ct);
        var environment = await _settings.GetAsync("Environment", info.Domain, ct);
        var report = await _engine.RunAsync(scenarioId, info.ComputerName, technician, environment, progress, ct);
        await _runs.SaveAsync(report, ct);
        return report;
    }

    public Task<ExportResult> ExportAsync(DiagnosticReport report, string format, CancellationToken ct = default) =>
        ReportExporter.ExportAsync(report, format, _reportDirectory, report.Technician);

    public Task<IReadOnlyList<DiagnosticRunEntry>> GetRecentRunsAsync(int count, CancellationToken ct = default) =>
        _runs.GetRecentAsync(count, ct);

    public Task<DiagnosticReport?> GetRunAsync(long id, CancellationToken ct = default) => _runs.GetAsync(id, ct);
}

public sealed class ConfirmationService : IConfirmationService
{
    public Func<ActionDefinition, Task<bool>> ConfirmationHandler { get; set; } =
        _ => Task.FromResult(false); // Fail closed until the UI installs its dialog handler.
}
