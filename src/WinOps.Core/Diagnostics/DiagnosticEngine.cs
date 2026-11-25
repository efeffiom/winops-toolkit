using WinOps.Core.Abstractions;
using WinOps.Core.Models;

namespace WinOps.Core.Diagnostics;

public sealed record DiagnosticScenario(
    string Id,
    string Title,
    string Category,
    string Description,
    IReadOnlyList<IDiagnosticStep> Steps);

/// <summary>
/// Runs evidence-gathering steps and produces findings. It never performs remediation;
/// remediation is offered separately through the Action Safety Engine.
/// </summary>
public sealed class DiagnosticEngine
{
    private readonly List<DiagnosticScenario> _scenarios = [];

    public void Register(DiagnosticScenario scenario)
    {
        if (_scenarios.Any(s => s.Id.Equals(scenario.Id, StringComparison.OrdinalIgnoreCase)))
            _scenarios.RemoveAll(s => s.Id.Equals(scenario.Id, StringComparison.OrdinalIgnoreCase));
        _scenarios.Add(scenario);
    }

    public IReadOnlyList<DiagnosticScenario> Scenarios => _scenarios.AsReadOnly();

    public DiagnosticScenario? GetScenario(string id) =>
        _scenarios.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public async Task<DiagnosticReport> RunAsync(
        string scenarioId,
        string computerName,
        string technician,
        string environment,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        var scenario = GetScenario(scenarioId)
            ?? throw new InvalidOperationException($"Unknown diagnostic scenario '{scenarioId}'.");

        var findings = new List<DiagnosticFinding>();
        for (var i = 0; i < scenario.Steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var step = scenario.Steps[i];
            progress?.Report(new OperationProgress($"Running: {step.Id}", (i * 100) / Math.Max(1, scenario.Steps.Count), scenario.Title));
            try
            {
                var stepFindings = await step.RunAsync(ct);
                findings.AddRange(stepFindings);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                findings.Add(new DiagnosticFinding(
                    step.Id + ".error",
                    $"Diagnostic step '{step.Id}' failed",
                    Severity.Medium,
                    CheckStatus.Failed,
                    "The check could not be completed.",
                    ex.Message,
                    "Confirm prerequisites and run the diagnostic again.",
                    string.Empty,
                    false,
                    string.Empty));
            }
        }

        progress?.Report(new OperationProgress("Complete", 100, scenario.Title));
        return new DiagnosticReport(
            scenario.Id,
            scenario.Title,
            computerName,
            technician,
            environment,
            DateTimeOffset.Now,
            findings,
            []);
    }
}
