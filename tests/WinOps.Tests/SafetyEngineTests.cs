using WinOps.Core.Abstractions;
using WinOps.Core.Models;
using WinOps.Core.Safety;
using Xunit;

namespace WinOps.Tests;

public sealed class SafetyEngineTests
{
    [Fact]
    public async Task ExecuteAsync_GrantsStandardUserAction_WhenConfirmationApproved()
    {
        var audit = new RecordingAuditSink();
        var engine = new ActionSafetyEngine(new FakePrivileges(false, "tester"), audit);
        var sawConfirmation = false;

        var result = await engine.ExecuteAsync(
            "script.execute",
            _ => Task.FromResult(ActionExecutionResult.Ok("completed", command: "pwsh -EncodedCommand ...")),
            _ => { sawConfirmation = true; return Task.FromResult(true); });

        Assert.True(result.Success);
        Assert.True(sawConfirmation);
        Assert.Equal(RiskLevel.Level3HighRisk, result.RiskLevel);
        Assert.Single(audit.Records);
        Assert.Equal("script.execute", audit.Records[0].action.Id);
    }

    [Fact]
    public async Task ExecuteAsync_DeniesElevatedAction_ForStandardUser()
    {
        var audit = new RecordingAuditSink();
        var engine = new ActionSafetyEngine(new FakePrivileges(false, "tester"), audit);
        var executed = false;

        var result = await engine.ExecuteAsync(
            "service.state",
            _ => { executed = true; return Task.FromResult(ActionExecutionResult.Ok("changed")); },
            _ => Task.FromResult(true));

        Assert.False(result.Success);
        Assert.Contains("administrator", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(executed);
        Assert.Single(audit.Records);
        Assert.False(audit.Records[0].result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_RecordsDeclinedConfirmation_WithoutRunningOperation()
    {
        var audit = new RecordingAuditSink();
        var engine = new ActionSafetyEngine(new FakePrivileges(true, "admin"), audit);
        var executed = false;

        var result = await engine.ExecuteAsync(
            "network.flushdns",
            _ => { executed = true; return Task.FromResult(ActionExecutionResult.Ok("flushed")); },
            _ => Task.FromResult(false));

        Assert.False(result.Success);
        Assert.True(result.ConfirmationDeclined);
        Assert.False(executed);
        Assert.Single(audit.Records);
    }

    [Fact]
    public async Task ExecuteAsync_MarksUnverifiedChangesAsFailures()
    {
        var audit = new RecordingAuditSink();
        var engine = new ActionSafetyEngine(new FakePrivileges(true, "admin"), audit);

        var result = await engine.ExecuteAsync(
            "network.setdns",
            _ => Task.FromResult(ActionExecutionResult.Ok("applied")),
            _ => Task.FromResult(true),
            verification: _ => Task.FromResult(false));

        Assert.False(result.Success);
        Assert.Contains("Verification failed", result.Message);
        Assert.Single(audit.Records);
    }

    [Fact]
    public async Task ExecuteAsync_RecordsCancellation()
    {
        var audit = new RecordingAuditSink();
        var engine = new ActionSafetyEngine(new FakePrivileges(true, "admin"), audit);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await engine.ExecuteAsync(
            "update.scan",
            async token =>
            {
                await Task.Delay(1, token);
                return ActionExecutionResult.Ok("scanned");
            },
            ct: cts.Token);

        Assert.False(result.Success);
        Assert.True(result.WasCancelled);
        Assert.Single(audit.Records);
    }

    [Fact]
    public void ActionCatalog_ContainsAllStateChangingActionIds()
    {
        var expected = new[]
        {
            "process.stop", "service.state", "service.startup", "task.state", "task.run",
            "restorepoint.create", "power.state", "network.setdns", "network.setstaticip",
            "update.install", "repair.sfc", "repair.dism", "repair.chkdsk", "security.apply",
            "user.create", "user.password", "ad.unlock", "storage.cleanup", "script.execute",
            "remote.script"
        };

        foreach (var id in expected)
            Assert.Contains(ActionCatalog.All, a => a.Id == id);

        Assert.Equal(RiskLevel.Level4Destructive, ActionCatalog.Get("storage.cleanup").RiskLevel);
        Assert.False(ActionCatalog.Get("script.execute").RequiresAdministrator);
        Assert.True(ActionCatalog.Get("script.execute").RequiresConfirmation);
    }

    private sealed class FakePrivileges(bool isAdmin, string user) : IPrivilegeService
    {
        public bool IsAdministrator() => isAdmin;
        public string CurrentUserName { get; } = user;
        public IReadOnlyList<string> DetectedCapabilities { get; } = ["Unit test privileges"];
    }

    private sealed class RecordingAuditSink : IAuditSink
    {
        public List<(ActionDefinition action, ActionExecutionResult result, string? details)> Records { get; } = [];

        public Task RecordAsync(ActionDefinition action, ActionExecutionResult result, string? details = null, CancellationToken ct = default)
        {
            Records.Add((action, result, details));
            return Task.CompletedTask;
        }
    }
}
