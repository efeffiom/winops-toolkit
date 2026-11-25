using Microsoft.EntityFrameworkCore;
using WinOps.Core.Models;

namespace WinOps.Persistence;

public sealed class AuditEntry
{
    public long Id { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public string ActionId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int RiskLevel { get; set; }
    public bool RequiresAdministrator { get; set; }
    public bool Success { get; set; }
    public bool WasCancelled { get; set; }
    public bool ConfirmationDeclined { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ExecutedCommand { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

public sealed class SettingEntry
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class DiagnosticRunEntry
{
    public long Id { get; set; }
    public string ScenarioId { get; set; } = string.Empty;
    public string ScenarioTitle { get; set; } = string.Empty;
    public string ComputerName { get; set; } = string.Empty;
    public string Technician { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public string ReportJson { get; set; } = string.Empty;
}

public sealed class AppDbContext : DbContext
{
    private readonly string _databasePath;

    public AppDbContext(string databasePath)
    {
        _databasePath = databasePath;
    }

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<SettingEntry> Settings => Set<SettingEntry>();
    public DbSet<DiagnosticRunEntry> DiagnosticRuns => Set<DiagnosticRunEntry>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={_databasePath};Pooling=False");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TimestampUtc);
            entity.Property(e => e.ActionId).HasMaxLength(128);
        });
        modelBuilder.Entity<SettingEntry>().HasKey(e => e.Key);
        modelBuilder.Entity<DiagnosticRunEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.GeneratedAtUtc);
        });
    }

    public void EnsureCreated() => Database.EnsureCreated();
}
