using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WinOps.Core.Abstractions;
using WinOps.Core.Models;

namespace WinOps.Persistence;

public sealed class AuditRepository : IAuditSink
{
    private readonly Func<AppDbContext> _factory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AuditRepository(Func<AppDbContext> factory) => _factory = factory;

    public async Task RecordAsync(ActionDefinition action, ActionExecutionResult result, string? details = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var db = _factory();
            await db.Database.EnsureCreatedAsync(ct);
            db.AuditEntries.Add(new AuditEntry
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                ActionId = action.Id,
                Title = action.Title,
                RiskLevel = (int)action.RiskLevel,
                RequiresAdministrator = action.RequiresAdministrator,
                Success = result.Success,
                WasCancelled = result.WasCancelled,
                ConfirmationDeclined = result.ConfirmationDeclined,
                Message = Truncate(result.Message, 2000),
                ExecutedCommand = Truncate(result.ExecutedCommand, 4000),
                Details = Truncate(details ?? string.Empty, 8000)
            });
            await db.SaveChangesAsync(ct);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<AuditEntry>> GetRecentAsync(int count, CancellationToken ct = default)
    {
        await using var db = _factory();
        await db.Database.EnsureCreatedAsync(ct);
        return await db.AuditEntries.OrderByDescending(e => e.Id).Take(Math.Clamp(count, 1, 1000)).ToListAsync(ct);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + " ... [truncated]";
}

public sealed class SettingsRepository
{
    private readonly Func<AppDbContext> _factory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SettingsRepository(Func<AppDbContext> factory) => _factory = factory;

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var db = _factory();
        await db.Database.EnsureCreatedAsync(ct);
        return (await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct))?.Value;
    }

    public async Task<string> GetAsync(string key, string defaultValue, CancellationToken ct = default) =>
        await GetAsync(key, ct) ?? defaultValue;

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var db = _factory();
            await db.Database.EnsureCreatedAsync(ct);
            var entry = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
            if (entry is null) db.Settings.Add(new SettingEntry { Key = key, Value = value });
            else entry.Value = value;
            await db.SaveChangesAsync(ct);
        }
        finally { _gate.Release(); }
    }
}

public sealed class DiagnosticRunRepository
{
    private readonly Func<AppDbContext> _factory;

    public DiagnosticRunRepository(Func<AppDbContext> factory) => _factory = factory;

    public async Task<long> SaveAsync(DiagnosticReport report, CancellationToken ct = default)
    {
        await using var db = _factory();
        await db.Database.EnsureCreatedAsync(ct);
        var entity = new DiagnosticRunEntry
        {
            ScenarioId = report.ScenarioId,
            ScenarioTitle = report.ScenarioTitle,
            ComputerName = report.ComputerName,
            Technician = report.Technician,
            Environment = report.Environment,
            GeneratedAtUtc = report.GeneratedAt,
            ReportJson = JsonSerializer.Serialize(report)
        };
        db.DiagnosticRuns.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task<IReadOnlyList<DiagnosticRunEntry>> GetRecentAsync(int count, CancellationToken ct = default)
    {
        await using var db = _factory();
        await db.Database.EnsureCreatedAsync(ct);
        return await db.DiagnosticRuns.AsNoTracking().OrderByDescending(e => e.Id).Take(Math.Clamp(count, 1, 500)).ToListAsync(ct);
    }

    public async Task<DiagnosticReport?> GetAsync(long id, CancellationToken ct = default)
    {
        await using var db = _factory();
        var entity = await db.DiagnosticRuns.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return entity is null ? null : JsonSerializer.Deserialize<DiagnosticReport>(entity.ReportJson);
    }
}
