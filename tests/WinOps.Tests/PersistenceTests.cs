using WinOps.Core.Models;
using WinOps.Persistence;
using Xunit;

namespace WinOps.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task SettingsRepository_RoundTripsValues()
    {
        var path = NewDatabasePath();
        try
        {
            var repository = new SettingsRepository(() => new AppDbContext(path));
            await repository.SetAsync("TechnicianName", "Ada Lovelace");
            Assert.Equal("Ada Lovelace", await repository.GetAsync("TechnicianName"));
            Assert.Equal("fallback", await repository.GetAsync("Missing", "fallback"));

            await repository.SetAsync("TechnicianName", "Grace Hopper");
            Assert.Equal("Grace Hopper", await repository.GetAsync("TechnicianName"));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task AuditRepository_RecordsRecentActions()
    {
        var path = NewDatabasePath();
        try
        {
            var repository = new AuditRepository(() => new AppDbContext(path));
            var action = ActionDefinition.Reversible("unit.action", "Unit action", "Test audit record", admin: false);
            var result = ActionExecutionResult.Ok("done", command: "unit --run");

            await repository.RecordAsync(action, result, "details");
            var recent = await repository.GetRecentAsync(10);

            var entry = Assert.Single(recent);
            Assert.Equal("unit.action", entry.ActionId);
            Assert.True(entry.Success);
            Assert.Contains("unit --run", entry.ExecutedCommand);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task DiagnosticRunRepository_SavesAndLoadsReport()
    {
        var path = NewDatabasePath();
        try
        {
            var repository = new DiagnosticRunRepository(() => new AppDbContext(path));
            var report = new DiagnosticReport(
                "unit", "Unit scenario", "PC01", "tech", "Lab", DateTimeOffset.Now,
                [new DiagnosticFinding("unit.ok", "Check", Severity.Info, CheckStatus.Passed, "detail", "evidence", "none", "", false, "")],
                []);

            var id = await repository.SaveAsync(report);
            var loaded = await repository.GetAsync(id);

            Assert.NotNull(loaded);
            Assert.Equal("Unit scenario", loaded.ScenarioTitle);
            Assert.Single(loaded.Findings);
            Assert.NotEmpty((await repository.GetRecentAsync(5)).Select(r => r.ReportJson));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    private static string NewDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"winops-db-test-{Guid.NewGuid():N}.db");

    private static void DeleteDatabase(string path)
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var file = path + suffix;
            if (File.Exists(file)) File.Delete(file);
        }
    }
}
