namespace WinOps.Core.Models;

/// <summary>Risk classification used by the centralized Action Safety Engine.</summary>
public enum RiskLevel
{
    Level0ReadOnly = 0,
    Level1LowRisk = 1,
    Level2Reversible = 2,
    Level3HighRisk = 3,
    Level4Destructive = 4
}

public enum Severity
{
    Info = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum CheckStatus
{
    Passed,
    Warning,
    Failed,
    Skipped,
    NotApplicable
}

public enum ServiceStartupType
{
    Automatic,
    AutomaticDelayedStart,
    Manual,
    Disabled,
    Unknown
}

public sealed record ActionDefinition(
    string Id,
    string Title,
    string Description,
    RiskLevel RiskLevel,
    bool RequiresAdministrator,
    bool RequiresConfirmation,
    bool HasRollback,
    string LoggingRequirement,
    string ValidationRequirement)
{
    public static ActionDefinition ReadOnly(string id, string title, string description) =>
        new(id, title, description, RiskLevel.Level0ReadOnly, false, false, true, "Standard diagnostic logging", "Read-only action");

    public static ActionDefinition Reversible(string id, string title, string description, bool admin = true, bool rollback = true) =>
        new(id, title, description, RiskLevel.Level2Reversible, admin, true, rollback, "Full audit record with before/after state", "Verify after execution");

    public static ActionDefinition HighRisk(string id, string title, string description, bool rollback = true) =>
        new(id, title, description, RiskLevel.Level3HighRisk, true, true, rollback, "Full audit record and explicit approval", "Verify service/system state after execution");

    public static ActionDefinition Destructive(string id, string title, string description) =>
        new(id, title, description, RiskLevel.Level4Destructive, true, true, false, "Mandatory audit record with affected paths", "Verify result and report to user");
}

public sealed record ActionExecutionResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string Output { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public int ExitCode { get; init; }
    public TimeSpan Duration { get; init; }
    public string ExecutedCommand { get; init; } = string.Empty;
    public bool Verified { get; init; }
    public bool WasCancelled { get; init; }
    public bool ConfirmationDeclined { get; init; }
    public RiskLevel RiskLevel { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }

    public static ActionExecutionResult Ok(string message, string output = "", string command = "", bool verified = true, int exitCode = 0) =>
        new()
        {
            Success = true,
            Message = message,
            Output = output,
            ExecutedCommand = command,
            Verified = verified,
            ExitCode = exitCode,
            StartedAt = DateTimeOffset.Now,
            CompletedAt = DateTimeOffset.Now
        };

    public static ActionExecutionResult Fail(string message, string error = "", string output = "", string command = "", int exitCode = -1) =>
        new()
        {
            Success = false,
            Message = message,
            Error = error,
            Output = output,
            ExecutedCommand = command,
            ExitCode = exitCode,
            StartedAt = DateTimeOffset.Now,
            CompletedAt = DateTimeOffset.Now
        };
}

public sealed record OperationProgress(string Activity, int Percent, string? Detail = null);

public sealed record SystemInfoSnapshot
{
    public string ComputerName { get; init; } = "Unavailable";
    public string Domain { get; init; } = "Unavailable";
    public string OsCaption { get; init; } = "Unavailable";
    public string OsVersion { get; init; } = "Unavailable";
    public string BuildNumber { get; init; } = "Unavailable";
    public string Architecture { get; init; } = "Unavailable";
    public string Manufacturer { get; init; } = "Unavailable";
    public string Model { get; init; } = "Unavailable";
    public string BiosVersion { get; init; } = "Unavailable";
    public string SerialNumber { get; init; } = "Unavailable";
    public string ProcessorName { get; init; } = "Unavailable";
    public int ProcessorCores { get; init; }
    public long TotalMemoryBytes { get; init; }
    public TimeSpan Uptime { get; init; }
    public DateTimeOffset? LastBootTime { get; init; }
    public bool IsAdministrator { get; init; }
    public string ActivationStatus { get; init; } = "Unavailable";
    public string DefenderStatus { get; init; } = "Unavailable";
    public string FirewallStatus { get; init; } = "Unavailable";
    public string WindowsUpdateStatus { get; init; } = "Unavailable";
    public string BatteryStatus { get; init; } = "Not present";
    public string CollectedAt { get; init; } = DateTimeOffset.Now.ToString("u");
}

public sealed record MetricSample(
    DateTimeOffset Timestamp,
    double? CpuUsagePercent,
    double? MemoryUsagePercent,
    double? DiskUsagePercent,
    double? NetworkSentBytesPerSecond,
    double? NetworkReceivedBytesPerSecond);

public sealed record ProcessInfo(
    int Id,
    string Name,
    double? CpuSeconds,
    long WorkingSetBytes,
    string ExecutablePath,
    string User,
    string CommandLine,
    int? ParentId,
    string Responding,
    string StartTime)
{
    public bool IsCritical { get; init; }
}

public sealed record ServiceInfo(
    string Name,
    string DisplayName,
    string Status,
    ServiceStartupType StartupType,
    string Description,
    string ExecutablePath,
    string UserAccount,
    IReadOnlyList<string> Dependencies)
{
    public bool IsCritical { get; init; }
}

public sealed record StartupEntry(string Name, string Command, string Location, string User, bool Enabled);

public sealed record ScheduledTaskInfo(
    string Name,
    string Path,
    string State,
    string Triggers,
    string Actions,
    string LastRunTime,
    string LastResult);

public sealed record InstalledSoftwareInfo(
    string Name,
    string Version,
    string Publisher,
    string InstallDate,
    string InstallLocation,
    string Source);

public sealed record DeviceInfo(string Name, string Class, string Status, string Manufacturer, string DeviceId);

public sealed record DriverInfo(string Name, string Provider, string Version, string Date, string DeviceClass);

public sealed record EnvironmentVariableEntry(string Name, string Value, string Scope);

public sealed record RestorePointInfo(string Description, string CreationTime, string Type, string SequenceNumber);

public sealed record NetworkAdapterInfo(
    string Name,
    string InterfaceType,
    string Status,
    long SpeedBitsPerSecond,
    string MacAddress,
    IReadOnlyList<string> Ipv4Addresses,
    IReadOnlyList<string> Ipv6Addresses,
    IReadOnlyList<string> DnsServers,
    string DefaultGateway,
    string DhcpEnabled,
    string DnsSuffix)
{
    public string IpConfigurationScript { get; init; } = string.Empty;
}

public sealed record RouteEntry(string Destination, string Mask, string Gateway, string Interface, int Metric);

public sealed record ArpEntry(string IpAddress, string MacAddress, string Interface);

public sealed record NetworkConnectionInfo(string Protocol, string LocalAddress, string LocalPort, string RemoteAddress, string RemotePort, string State, int ProcessId);

public sealed record PingResult(string Host, string Address, bool Success, int PacketsSent, int PacketsReceived, double? MinimumMs, double? MaximumMs, double? AverageMs, string Error);

public sealed record TracerouteHop(int Hop, string Address, double? LatencyMs, string Error);

public sealed record DnsLookupResult(string Name, string Type, IReadOnlyList<string> Records, string Error);

public sealed record PortTestResult(string Host, int Port, bool Open, string Error, long DurationMs);

public sealed record WifiInfo(string Ssid, string SignalStrengthPercent, string Authentication, string Cipher, string Channel, string State);

public sealed record WindowsUpdateInfo(string Title, string Status, string Date, string UpdateId, string Classification);

public sealed record RepairOperationInfo(string Id, string Title, string Purpose, RiskLevel RiskLevel, string Command, bool RequiresAdministrator);

public sealed record SecurityCheck(
    string Id,
    string Category,
    string Title,
    CheckStatus Status,
    string CurrentValue,
    string ExpectedValue,
    Severity Severity,
    string Explanation,
    string Recommendation,
    string RemediationActionId = "");

public sealed record SecurityAssessmentResult(
    DateTimeOffset CollectedAt,
    IReadOnlyList<SecurityCheck> Checks,
    string ProfileName,
    int Passed,
    int Warnings,
    int Failures)
{
    public Severity OverallSeverity => Failures > 0 ? Severity.High : Warnings > 0 ? Severity.Medium : Severity.Low;
}

public sealed record SecurityProfile(string Name, string Description, IReadOnlyDictionary<string, string> ExpectedValues, bool IsBuiltIn);

public sealed record LocalAccount(
    string Name,
    string FullName,
    string Description,
    bool Enabled,
    bool AccountLocked,
    bool PasswordRequired,
    DateTimeOffset? LastLogon,
    DateTimeOffset? PasswordLastSet,
    DateTimeOffset? Expires,
    IReadOnlyList<string> Groups)
{
    public bool IsBuiltInAdministrator { get; init; }
}

public sealed record LocalGroupInfo(string Name, string Description, IReadOnlyList<string> Members, bool IsBuiltIn);

public sealed record DomainInfo(string Name, string Forest, string DomainController, string FunctionalLevel, bool IsDomainJoined, string Site);

public sealed record AdUser(
    string DistinguishedName,
    string SamAccountName,
    string DisplayName,
    string Email,
    bool Enabled,
    bool LockedOut,
    DateTimeOffset? LastLogon,
    DateTimeOffset? PasswordLastSet,
    DateTimeOffset? Expires,
    IReadOnlyList<string> Groups);

public sealed record AdGroup(string Name, string DistinguishedName, string Scope, IReadOnlyList<string> Members);

public sealed record OrganizationalUnit(string Name, string DistinguishedName);

public sealed record PhysicalDiskInfo(
    string DeviceId,
    string Model,
    string MediaType,
    string InterfaceType,
    long SizeBytes,
    string HealthStatus,
    string OperationalStatus);

public sealed record VolumeInfo(
    string Drive,
    string Label,
    string FileSystem,
    long CapacityBytes,
    long FreeBytes,
    string HealthStatus,
    string BitLockerStatus);

public sealed record FolderSizeEntry(string Path, long SizeBytes, int FileCount, int FolderCount);

public sealed record LargeFileInfo(string Path, long SizeBytes, DateTimeOffset LastModified);

public sealed record CleanupSuggestion(string Id, string Title, string Path, long EstimatedBytes, string Reason, RiskLevel RiskLevel, IReadOnlyList<string> Exclusions);

public sealed record EventLogEntryInfo(
    string LogName,
    long EventId,
    string Provider,
    string Level,
    DateTimeOffset TimeCreated,
    string Message,
    string Xml);

public sealed record ScriptTemplateInfo(string Id, string Name, string Category, string Description, string Content, string RequiredPrivilege);

public sealed record ScriptExecutionRecord(
    DateTimeOffset StartedAt,
    string ScriptName,
    string Command,
    int ExitCode,
    TimeSpan Duration,
    string Output,
    string Error,
    bool Success);

public sealed record RemoteEndpoint(string Id, string HostName, string DisplayName, string Group, string Description, bool UsesCurrentCredentials);

public sealed record RemoteCommandResult(string HostName, bool Success, string Output, string Error, string Command, TimeSpan Duration);

public sealed record DiagnosticFinding(
    string Id,
    string Title,
    Severity Severity,
    CheckStatus Status,
    string Detail,
    string Evidence,
    string Recommendation,
    string RemediationActionId,
    bool RemediationApplied,
    string Verification)
{
    public DiagnosticFinding Verified(string verification) => this with { RemediationApplied = true, Verification = verification };
}

public sealed record DiagnosticReport(
    string ScenarioId,
    string ScenarioTitle,
    string ComputerName,
    string Technician,
    string Environment,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<DiagnosticFinding> Findings,
    IReadOnlyList<string> ActionsPerformed)
{
    public Severity OverallSeverity
    {
        get
        {
            if (Findings.Any(f => f.Severity == Severity.Critical && f.Status != CheckStatus.Passed)) return Severity.Critical;
            if (Findings.Any(f => f.Severity == Severity.High && f.Status != CheckStatus.Passed)) return Severity.High;
            if (Findings.Any(f => f.Severity == Severity.Medium && f.Status != CheckStatus.Passed)) return Severity.Medium;
            return Findings.Any(f => f.Status == CheckStatus.Failed) ? Severity.Low : Severity.Info;
        }
    }
}

public sealed record ExportResult(string Format, string FilePath, long BytesWritten);
