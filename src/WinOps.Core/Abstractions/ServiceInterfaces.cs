using WinOps.Core.Models;

namespace WinOps.Core.Abstractions;

public interface IPrivilegeService
{
    bool IsAdministrator();
    string CurrentUserName { get; }
    IReadOnlyList<string> DetectedCapabilities { get; }
}

public interface ISystemInformationProvider
{
    Task<SystemInfoSnapshot> GetSystemInfoAsync(CancellationToken ct = default);
    Task<MetricSample> GetCurrentMetricsAsync(CancellationToken ct = default);
}

public interface IProcessManager
{
    Task<IReadOnlyList<ProcessInfo>> GetProcessesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ProcessInfo>> GetProcessTreeAsync(int processId, CancellationToken ct = default);
    Task<ActionExecutionResult> StopProcessAsync(int processId, bool force, Func<ActionDefinition, Task<bool>> confirmAsync, IProgress<OperationProgress>? progress = null, CancellationToken ct = default);
}

public interface IServiceManager
{
    Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(CancellationToken ct = default);
    Task<ActionExecutionResult> SetServiceStateAsync(string serviceName, string action, Func<ActionDefinition, Task<bool>> confirmAsync, IProgress<OperationProgress>? progress = null, CancellationToken ct = default);
    Task<ActionExecutionResult> SetStartupTypeAsync(string serviceName, ServiceStartupType startupType, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default);
}

public interface IInventoryService
{
    Task<IReadOnlyList<StartupEntry>> GetStartupEntriesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ScheduledTaskInfo>> GetScheduledTasksAsync(CancellationToken ct = default);
    Task<ActionExecutionResult> SetTaskStateAsync(string taskPath, bool enable, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default);
    Task<ActionExecutionResult> RunTaskAsync(string taskPath, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default);
    Task<IReadOnlyList<InstalledSoftwareInfo>> GetInstalledSoftwareAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DriverInfo>> GetDriversAsync(CancellationToken ct = default);
    Task<IReadOnlyList<EnvironmentVariableEntry>> GetEnvironmentVariablesAsync(string scope, CancellationToken ct = default);
    Task<IReadOnlyList<RestorePointInfo>> GetRestorePointsAsync(CancellationToken ct = default);
    Task<ActionExecutionResult> CreateRestorePointAsync(string description, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default);
    Task<ActionExecutionResult> SetPowerStateAsync(string action, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default);
}

public interface INetworkService
{
    Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<RouteEntry>> GetRoutesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ArpEntry>> GetArpEntriesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<NetworkConnectionInfo>> GetConnectionsAsync(CancellationToken ct = default);
    Task<WifiInfo?> GetWifiInfoAsync(CancellationToken ct = default);
    Task<PingResult> PingAsync(string host, int count = 4, CancellationToken ct = default);
    Task<IReadOnlyList<TracerouteHop>> TracerouteAsync(string host, int maxHops = 30, CancellationToken ct = default);
    Task<IReadOnlyList<DnsLookupResult>> ResolveDnsAsync(string host, CancellationToken ct = default);
    Task<PortTestResult> TestPortAsync(string host, int port, int timeoutMs = 3000, CancellationToken ct = default);
    Task<string> GetPublicIpAsync(CancellationToken ct = default);
    Task<ActionExecutionResult> RunNetworkCommandAsync(string action, string? adapterName, Func<ActionDefinition, Task<bool>> confirmAsync, IProgress<OperationProgress>? progress = null, CancellationToken ct = default);
    Task<ActionExecutionResult> SetDnsServersAsync(string adapterName, IReadOnlyList<string> dnsServers, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default);
    Task<ActionExecutionResult> SetStaticIpAsync(string adapterName, string ipAddress, string prefixLength, string gateway, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default);
    Task<ActionExecutionResult> RestoreDhcpAsync(string adapterName, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default);
}

public interface IMaintenanceService
{
    Task<IReadOnlyList<WindowsUpdateInfo>> GetUpdateHistoryAsync(CancellationToken ct = default);
    Task<IReadOnlyList<WindowsUpdateInfo>> GetPendingUpdatesAsync(CancellationToken ct = default);
    Task<ActionExecutionResult> ScanUpdatesAsync(Func<ActionDefinition, Task<bool>> confirmAsync, IProgress<OperationProgress>? progress = null, CancellationToken ct = default);
    Task<ActionExecutionResult> InstallUpdatesAsync(Func<ActionDefinition, Task<bool>> confirmAsync, IProgress<OperationProgress>? progress = null, CancellationToken ct = default);
    Task<IReadOnlyList<RepairOperationInfo>> GetRepairOperationsAsync();
    Task<ActionExecutionResult> RunRepairAsync(string operationId, Func<ActionDefinition, Task<bool>> confirmAsync, IProgress<OperationProgress>? progress = null, CancellationToken ct = default);
}

public interface ISecurityService
{
    Task<SecurityAssessmentResult> AssessAsync(string profileName, CancellationToken ct = default);
    Task<IReadOnlyList<SecurityProfile>> GetProfilesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SecurityCheck>> PreviewProfileAsync(string profileName, CancellationToken ct = default);
    Task<ActionExecutionResult> ApplyProfileAsync(string profileName, IReadOnlyList<string> checkIds, Func<ActionDefinition, Task<bool>> confirmAsync, IProgress<OperationProgress>? progress = null, CancellationToken ct = default);
}

public interface ILocalUserService
{
    Task<IReadOnlyList<LocalAccount>> GetUsersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LocalGroupInfo>> GetGroupsAsync(CancellationToken ct = default);
    Task<ActionExecutionResult> CreateUserAsync(string name, string password, string fullName, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default);
    Task<ActionExecutionResult> SetUserStateAsync(string name, bool enable, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default);
    Task<ActionExecutionResult> ResetPasswordAsync(string name, string password, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default);
    Task<ActionExecutionResult> SetGroupMembershipAsync(string group, string user, bool add, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default);
    Task<IReadOnlyList<ActionExecutionResult>> RunBulkOperationAsync(string csvContent, bool apply, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default);
}

public interface IActiveDirectoryService
{
    bool IsAvailable { get; }
    string PrerequisiteMessage { get; }
    Task<DomainInfo> GetDomainInfoAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AdUser>> FindUsersAsync(string search, int maxResults, CancellationToken ct = default);
    Task<ActionExecutionResult> SetUserStateAsync(string distinguishedName, bool enable, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default);
    Task<ActionExecutionResult> UnlockUserAsync(string distinguishedName, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default);
    Task<ActionExecutionResult> ResetPasswordAsync(string distinguishedName, string password, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default);
    Task<IReadOnlyList<AdGroup>> GetGroupsAsync(string search, CancellationToken ct = default);
    Task<IReadOnlyList<OrganizationalUnit>> GetOrganizationalUnitsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DiagnosticFinding>> RunDomainDiagnosticsAsync(CancellationToken ct = default);
}

public interface IStorageService
{
    Task<IReadOnlyList<PhysicalDiskInfo>> GetPhysicalDisksAsync(CancellationToken ct = default);
    Task<IReadOnlyList<VolumeInfo>> GetVolumesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<FolderSizeEntry>> AnalyzeFolderAsync(string path, int topFolders, IProgress<OperationProgress>? progress = null, CancellationToken ct = default);
    Task<IReadOnlyList<LargeFileInfo>> FindLargeFilesAsync(string path, long minimumBytes, int maxResults, IProgress<OperationProgress>? progress = null, CancellationToken ct = default);
    Task<IReadOnlyList<CleanupSuggestion>> GetCleanupSuggestionsAsync(CancellationToken ct = default);
    Task<ActionExecutionResult> CleanupAsync(string suggestionId, IReadOnlyList<string> approvedPaths, Func<ActionDefinition, Task<bool>> confirmAsync, IProgress<OperationProgress>? progress = null, CancellationToken ct = default);
}

public interface IEventLogService
{
    Task<IReadOnlyList<string>> GetLogsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<EventLogEntryInfo>> QueryAsync(
        string logName,
        string? level,
        long? eventId,
        string? provider,
        DateTimeOffset? from,
        string? keyword,
        int maxEvents,
        CancellationToken ct = default);
}

public interface IPowerShellService
{
    string ExecutablePath { get; }
    Task<IReadOnlyList<ScriptTemplateInfo>> GetTemplatesAsync(CancellationToken ct = default);
    Task<ActionExecutionResult> ExecuteAsync(
        string script,
        string? parameters,
        Func<ActionDefinition, Task<bool>> confirmAsync,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default);
    Task<IReadOnlyList<ScriptExecutionRecord>> GetHistoryAsync(CancellationToken ct = default);
    Task SaveTemplateAsync(ScriptTemplateInfo template, CancellationToken ct = default);
    Task DeleteTemplateAsync(string id, CancellationToken ct = default);
}

public interface IRemoteAdministrationService
{
    Task<IReadOnlyList<RemoteEndpoint>> GetEndpointsAsync(CancellationToken ct = default);
    Task<ActionExecutionResult> TestConnectionAsync(string hostName, CancellationToken ct = default);
    Task<RemoteCommandResult> GetRemoteSystemInfoAsync(string hostName, CancellationToken ct = default);
    Task<RemoteCommandResult> GetRemoteServicesAsync(string hostName, CancellationToken ct = default);
    Task<RemoteCommandResult> GetRemoteProcessesAsync(string hostName, CancellationToken ct = default);
    Task<RemoteCommandResult> GetRemoteEventsAsync(string hostName, int maxEvents, CancellationToken ct = default);
    Task<RemoteCommandResult> RunRemoteScriptAsync(string hostName, string script, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default);
    Task SaveEndpointAsync(RemoteEndpoint endpoint, CancellationToken ct = default);
    Task DeleteEndpointAsync(string id, CancellationToken ct = default);
}

public interface IDiagnosticStep
{
    string Id { get; }
    Task<IReadOnlyList<DiagnosticFinding>> RunAsync(CancellationToken ct = default);
}

public interface IAuditSink
{
    Task RecordAsync(ActionDefinition action, ActionExecutionResult result, string? details = null, CancellationToken ct = default);
}

public interface IConfirmationService
{
    Func<ActionDefinition, Task<bool>> ConfirmationHandler { get; set; }
}
