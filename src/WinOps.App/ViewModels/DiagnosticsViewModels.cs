using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinOps.App.Helpers;
using WinOps.App.Services;
using WinOps.Core.Abstractions;
using WinOps.Core.Diagnostics;
using WinOps.Core.Models;
using WinOps.Persistence;
using WinOps.PowerShell.Execution;
using WinOps.Services.Reporting;

namespace WinOps.App.ViewModels;

public sealed partial class EventsViewModel : ViewModelBase
{
    private readonly IEventLogService _events;

    [ObservableProperty] private ObservableCollection<string> _logs = [];
    [ObservableProperty] private ObservableCollection<EventLogEntryInfo> _entries = [];
    [ObservableProperty] private EventLogEntryInfo? _selectedEntry;
    [ObservableProperty] private string _selectedLog = "System";
    [ObservableProperty] private string _level = string.Empty;
    [ObservableProperty] private string _eventId = string.Empty;
    [ObservableProperty] private string _provider = string.Empty;
    [ObservableProperty] private string _keyword = string.Empty;
    [ObservableProperty] private int _hoursBack = 72;
    [ObservableProperty] private int _maxEvents = 300;

    public IReadOnlyList<string> Levels { get; } = ["", "critical", "error", "warning", "information", "verbose"];

    public EventsViewModel(IEventLogService events, IConfirmationService confirmation) : base(confirmation)
        => _events = events;

    [RelayCommand]
    private async Task RefreshLogsAsync()
    {
        var result = await RunAsync(async ct => (await _events.GetLogsAsync(ct)).ToList(), "Loading event logs");
        if (result is null) return;
        Logs = new ObservableCollection<string>(result);
        if (!result.Contains(SelectedLog, StringComparer.OrdinalIgnoreCase)) SelectedLog = result.FirstOrDefault() ?? "System";
        await QueryAsync();
    }

    [RelayCommand]
    private async Task QueryAsync()
    {
        long? id = long.TryParse(EventId, out var parsed) ? parsed : null;
        var result = await RunAsync(async ct => (await _events.QueryAsync(
            SelectedLog,
            string.IsNullOrWhiteSpace(Level) ? null : Level,
            id,
            string.IsNullOrWhiteSpace(Provider) ? null : Provider,
            DateTimeOffset.Now.AddHours(-Math.Max(1, HoursBack)),
            string.IsNullOrWhiteSpace(Keyword) ? null : Keyword,
            Math.Clamp(MaxEvents, 1, 5000),
            ct)).ToList(), $"Querying {SelectedLog}");
        if (result is null) return;
        Entries = new ObservableCollection<EventLogEntryInfo>(result);
        SelectedEntry = Entries.FirstOrDefault();
        StatusMessage = $"{Entries.Count} event(s) returned from {SelectedLog}";
    }

    [RelayCommand]
    private void Export()
    {
        var path = CsvExport.Write(Entries, "event-log");
        StatusMessage = $"Exported {Entries.Count} events to {path}";
    }
}

public sealed partial class AutomationViewModel : ViewModelBase
{
    private readonly IPowerShellService _powershell;

    [ObservableProperty] private ObservableCollection<ScriptTemplateInfo> _templates = [];
    [ObservableProperty] private ObservableCollection<ScriptExecutionRecord> _history = [];
    [ObservableProperty] private ScriptTemplateInfo? _selectedTemplate;
    [ObservableProperty] private string _script = string.Empty;
    [ObservableProperty] private string _parameters = string.Empty;
    [ObservableProperty] private string _newTemplateName = string.Empty;
    [ObservableProperty] private string _newTemplateCategory = "Custom";
    [ObservableProperty] private string _newTemplateDescription = string.Empty;

    public string ExecutablePath => _powershell.ExecutablePath;
    public bool RequiresElevation => PowerShellExecutor.RequiresElevation(Script);

    public AutomationViewModel(IPowerShellService powershell, IConfirmationService confirmation) : base(confirmation)
        => _powershell = powershell;

    partial void OnScriptChanged(string value) => OnPropertyChanged(nameof(RequiresElevation));

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RunAsync(async ct =>
        {
            var templatesTask = _powershell.GetTemplatesAsync(ct);
            var historyTask = _powershell.GetHistoryAsync(ct);
            await Task.WhenAll(templatesTask, historyTask);
            Templates = new ObservableCollection<ScriptTemplateInfo>(await templatesTask);
            History = new ObservableCollection<ScriptExecutionRecord>(await historyTask);
            StatusMessage = $"{Templates.Count} script template(s), {History.Count} execution record(s)";
        }, "Loading PowerShell automation library");
    }

    [RelayCommand]
    private void LoadTemplate()
    {
        if (SelectedTemplate is null) return;
        Script = SelectedTemplate.Content;
        StatusMessage = $"Loaded template '{SelectedTemplate.Name}'. Review the script before execution.";
    }

    [RelayCommand]
    private async Task ExecuteAsync()
    {
        var result = await RunAsync(ct => _powershell.ExecuteAsync(Script, Parameters, Confirm, CreateProgress(), ct), "Executing PowerShell script");
        if (result is not null) await NotifyAsync(result);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task SaveTemplateAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTemplateName) || string.IsNullOrWhiteSpace(Script))
        {
            StatusMessage = "Provide a template name and script content.";
            return;
        }
        var template = new ScriptTemplateInfo(
            Guid.NewGuid().ToString("N"),
            NewTemplateName.Trim(),
            string.IsNullOrWhiteSpace(NewTemplateCategory) ? "Custom" : NewTemplateCategory.Trim(),
            NewTemplateDescription.Trim(),
            Script,
            RequiresElevation ? "Administrator" : "Standard user");
        await RunAsync(ct => _powershell.SaveTemplateAsync(template, ct), $"Saving template {template.Name}");
        NewTemplateName = string.Empty;
        NewTemplateDescription = string.Empty;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeleteTemplateAsync()
    {
        if (SelectedTemplate is null) { StatusMessage = "Select a template first."; return; }
        if (!SelectedTemplate.Id.StartsWith("custom-", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "Built-in templates cannot be deleted.";
            return;
        }
        await RunAsync(ct => _powershell.DeleteTemplateAsync(SelectedTemplate.Id, ct), $"Deleting template {SelectedTemplate.Name}");
        await RefreshAsync();
    }

    [RelayCommand]
    private void ClearScript()
    {
        Script = string.Empty;
        Parameters = string.Empty;
    }
}

public sealed partial class RemoteViewModel : ViewModelBase
{
    private readonly IRemoteAdministrationService _remote;

    [ObservableProperty] private ObservableCollection<RemoteEndpoint> _endpoints = [];
    [ObservableProperty] private ObservableCollection<string> _outputLines = [];
    [ObservableProperty] private RemoteEndpoint? _selectedEndpoint;
    [ObservableProperty] private string _hostName = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _group = "Servers";
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private bool _usesCurrentCredentials = true;
    [ObservableProperty] private string _remoteScript = "Get-CimInstance Win32_OperatingSystem | Select-Object CSName,Version | Format-List";
    [ObservableProperty] private int _eventCount = 50;

    public RemoteViewModel(IRemoteAdministrationService remote, IConfirmationService confirmation) : base(confirmation)
        => _remote = remote;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var result = await RunAsync(async ct => (await _remote.GetEndpointsAsync(ct)).ToList(), "Loading remote endpoints");
        if (result is null) return;
        Endpoints = new ObservableCollection<RemoteEndpoint>(result);
        SelectedEndpoint = Endpoints.FirstOrDefault();
        StatusMessage = $"{Endpoints.Count} saved endpoint(s)";
    }

    [RelayCommand]
    private async Task SaveEndpointAsync()
    {
        if (string.IsNullOrWhiteSpace(HostName)) { StatusMessage = "Enter a host name."; return; }
        var endpoint = new RemoteEndpoint(
            SelectedEndpoint?.Id ?? Guid.NewGuid().ToString("N"),
            HostName.Trim(),
            string.IsNullOrWhiteSpace(DisplayName) ? HostName.Trim() : DisplayName.Trim(),
            string.IsNullOrWhiteSpace(Group) ? "Servers" : Group.Trim(),
            Description.Trim(),
            UsesCurrentCredentials);
        await RunAsync(ct => _remote.SaveEndpointAsync(endpoint, ct), $"Saving endpoint {endpoint.HostName}");
        HostName = string.Empty;
        DisplayName = string.Empty;
        Description = string.Empty;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeleteEndpointAsync()
    {
        if (SelectedEndpoint is null) { StatusMessage = "Select an endpoint first."; return; }
        await RunAsync(ct => _remote.DeleteEndpointAsync(SelectedEndpoint.Id, ct), $"Deleting endpoint {SelectedEndpoint.HostName}");
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (SelectedEndpoint is null) { StatusMessage = "Select an endpoint first."; return; }
        var result = await RunAsync(ct => _remote.TestConnectionAsync(SelectedEndpoint.HostName, ct), $"Testing {SelectedEndpoint.HostName}");
        if (result is not null) await NotifyAsync(result);
    }

    [RelayCommand]
    private async Task GetRemoteAsync(string operation)
    {
        if (SelectedEndpoint is null) { StatusMessage = "Select an endpoint first."; return; }
        var result = await RunAsync(ct => operation switch
        {
            "info" => _remote.GetRemoteSystemInfoAsync(SelectedEndpoint.HostName, ct),
            "services" => _remote.GetRemoteServicesAsync(SelectedEndpoint.HostName, ct),
            "processes" => _remote.GetRemoteProcessesAsync(SelectedEndpoint.HostName, ct),
            "events" => _remote.GetRemoteEventsAsync(SelectedEndpoint.HostName, EventCount, ct),
            _ => Task.FromResult(new RemoteCommandResult(SelectedEndpoint.HostName, false, string.Empty, $"Unknown remote operation '{operation}'.", string.Empty, TimeSpan.Zero))
        }, $"Collecting {operation} from {SelectedEndpoint.HostName}");
        if (result is null) return;
        OutputLines = new ObservableCollection<string>((result.Output + Environment.NewLine + result.Error)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        StatusMessage = result.Success
            ? $"{operation} collected from {result.HostName} in {result.Duration.TotalSeconds:0.#}s"
            : $"{operation} failed: {result.Error}";
    }

    [RelayCommand]
    private async Task RunScriptAsync()
    {
        if (SelectedEndpoint is null) { StatusMessage = "Select an endpoint first."; return; }
        var result = await RunAsync(ct => _remote.RunRemoteScriptAsync(SelectedEndpoint.HostName, RemoteScript, Confirm, ct),
            $"Running script on {SelectedEndpoint.HostName}");
        if (result is null) return;
        OutputLines = new ObservableCollection<string>((result.Output + Environment.NewLine + result.Error)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        StatusMessage = result.Success ? $"Remote script completed on {result.HostName}." : $"Remote script failed: {result.Error}";
    }
}

public sealed partial class TroubleshootingViewModel : ViewModelBase
{
    private readonly DiagnosticEngine _engine;
    private readonly ReportService _reports;
    private readonly NavigationService _navigation;

    [ObservableProperty] private ObservableCollection<DiagnosticScenario> _scenarios = [];
    [ObservableProperty] private ObservableCollection<DiagnosticFinding> _findings = [];
    [ObservableProperty] private DiagnosticScenario? _selectedScenario;
    [ObservableProperty] private DiagnosticFinding? _selectedFinding;
    [ObservableProperty] private DiagnosticReport? _report;

    public IReadOnlyList<string> Categories => Scenarios.Select(s => s.Category).Distinct().Order().ToList();

    public TroubleshootingViewModel(DiagnosticEngine engine, ReportService reports, NavigationService navigation, IConfirmationService confirmation)
        : base(confirmation)
    {
        _engine = engine;
        _reports = reports;
        _navigation = navigation;
        Scenarios = new ObservableCollection<DiagnosticScenario>(_engine.Scenarios.OrderBy(s => s.Category).ThenBy(s => s.Title));
        SelectedScenario = Scenarios.FirstOrDefault();
    }

    [RelayCommand]
    private async Task RunDiagnosticAsync()
    {
        if (SelectedScenario is null) { StatusMessage = "Select a diagnostic scenario first."; return; }
        var result = await RunAsync(ct => _reports.RunAsync(SelectedScenario.Id, CreateProgress(), ct), $"Running {SelectedScenario.Title}");
        if (result is null) return;
        Report = result;
        Findings = new ObservableCollection<DiagnosticFinding>(result.Findings);
        StatusMessage = $"Diagnostic complete: {result.Findings.Count(f => f.Status == CheckStatus.Failed)} failed check(s), severity {result.OverallSeverity}";
    }

    [RelayCommand]
    private async Task ExportReportAsync(string format)
    {
        if (Report is null) { StatusMessage = "Run a diagnostic first."; return; }
        var result = await RunAsync(ct => _reports.ExportAsync(Report, format, ct), $"Exporting {format.ToUpperInvariant()} report");
        if (result is not null) StatusMessage = $"Exported {result.Format.ToUpperInvariant()} report to {result.FilePath}";
    }

    [RelayCommand]
    private void GoToRemediation(DiagnosticFinding? finding)
    {
        if (finding is null || string.IsNullOrWhiteSpace(finding.RemediationActionId))
        {
            StatusMessage = "This finding has no built-in remediation module.";
            return;
        }
        var id = finding.RemediationActionId;
        var target = id.StartsWith("network.", StringComparison.OrdinalIgnoreCase) ? "network"
            : id.StartsWith("update.", StringComparison.OrdinalIgnoreCase) || id.StartsWith("repair.", StringComparison.OrdinalIgnoreCase) ? "maintenance"
            : id.StartsWith("security.", StringComparison.OrdinalIgnoreCase) ? "security"
            : id.StartsWith("user.", StringComparison.OrdinalIgnoreCase) ? "users"
            : id.StartsWith("ad.", StringComparison.OrdinalIgnoreCase) ? "ad"
            : id.StartsWith("storage.", StringComparison.OrdinalIgnoreCase) ? "storage"
            : id.Equals("service.state", StringComparison.OrdinalIgnoreCase) ? "services"
            : id.Equals("process.stop", StringComparison.OrdinalIgnoreCase) ? "processes"
            : string.Empty;
        if (string.IsNullOrWhiteSpace(target))
        {
            StatusMessage = $"Remediation '{id}' is available only after manual review.";
            return;
        }
        _navigation.Navigate(target);
        StatusMessage = $"Opened {_navigation.Current?.Title} for remediation of '{finding.Title}'.";
    }
}

public sealed partial class ReportsViewModel : ViewModelBase
{
    private readonly ReportService _reports;

    [ObservableProperty] private ObservableCollection<DiagnosticRunEntry> _runs = [];
    [ObservableProperty] private DiagnosticReport? _report;
    [ObservableProperty] private string _reportDirectory;
    [ObservableProperty] private long? _selectedRunId;

    public ReportsViewModel(ReportService reports, IConfirmationService confirmation) : base(confirmation)
    {
        _reports = reports;
        _reportDirectory = reports.ReportDirectory;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var result = await RunAsync(async ct => (await _reports.GetRecentRunsAsync(200, ct)).ToList(), "Loading saved reports");
        if (result is null) return;
        Runs = new ObservableCollection<DiagnosticRunEntry>(result);
        SelectedRunId = Runs.FirstOrDefault()?.Id;
        StatusMessage = $"{Runs.Count} saved diagnostic run(s)";
    }

    [RelayCommand]
    private async Task LoadSelectedAsync()
    {
        if (SelectedRunId is null) { StatusMessage = "Select a saved report first."; return; }
        var result = await RunAsync(ct => _reports.GetRunAsync(SelectedRunId.Value, ct), "Loading report details");
        if (result is null) return;
        Report = result;
        StatusMessage = result is null ? "Report could not be loaded." : $"Loaded '{result.ScenarioTitle}' with {result.Findings.Count} finding(s).";
    }

    [RelayCommand]
    private async Task ExportAsync(string format)
    {
        if (Report is null) { StatusMessage = "Load a report first."; return; }
        var result = await RunAsync(ct => _reports.ExportAsync(Report, format, ct), $"Exporting {format} report");
        if (result is not null) StatusMessage = $"Exported to {result.FilePath}";
    }

    [RelayCommand]
    private void OpenDirectory()
    {
        Directory.CreateDirectory(ReportDirectory);
        Process.Start(new ProcessStartInfo { FileName = ReportDirectory, UseShellExecute = true });
    }
}

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsRepository _settings;
    private readonly AuditRepository _audit;
    private readonly IPrivilegeService _privileges;
    private readonly ThemeService _theme;

    [ObservableProperty] private ObservableCollection<AuditEntry> _auditEntries = [];
    [ObservableProperty] private string _technicianName = string.Empty;
    [ObservableProperty] private string _environment = string.Empty;
    [ObservableProperty] private int _refreshIntervalSeconds = 5;
    [ObservableProperty] private string _selectedTheme = "Light";

    public IReadOnlyList<string> Themes { get; } = ["Light", "Dark"];
    public string VersionText => $"WinOps Toolkit {App.AppVersion}";
    public string DataDirectory => App.DataDirectory;
    public string DatabasePath => Path.Combine(App.DataDirectory, "winops.db");
    public string LogDirectory => Path.Combine(App.DataDirectory, "Logs");
    public string UserName => _privileges.CurrentUserName;
    public string PrivilegeLabel => _privileges.IsAdministrator() ? "Administrator" : "Standard user";
    public IReadOnlyList<string> Capabilities => _privileges.DetectedCapabilities;

    public SettingsViewModel(SettingsRepository settings, AuditRepository audit, IPrivilegeService privileges,
        ThemeService theme, IConfirmationService confirmation) : base(confirmation)
    {
        _settings = settings;
        _audit = audit;
        _privileges = privileges;
        _theme = theme;
        SelectedTheme = theme.CurrentTheme;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        await RunAsync(async ct =>
        {
            TechnicianName = await _settings.GetAsync("TechnicianName", System.Environment.UserName, ct);
            Environment = await _settings.GetAsync("Environment", "Default", ct);
            var refresh = await _settings.GetAsync("RefreshIntervalSeconds", "5", ct);
            RefreshIntervalSeconds = int.TryParse(refresh, out var seconds) ? seconds : 5;
            AuditEntries = new ObservableCollection<AuditEntry>(await _audit.GetRecentAsync(500, ct));
            StatusMessage = $"{AuditEntries.Count} audit entries loaded";
        }, "Loading settings and audit trail");
    }

    partial void OnSelectedThemeChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        _theme.Apply(value);
        _ = _settings.SetAsync("Theme", value);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await RunAsync(async ct =>
        {
            await _settings.SetAsync("TechnicianName", TechnicianName, ct);
            await _settings.SetAsync("Environment", Environment, ct);
            await _settings.SetAsync("RefreshIntervalSeconds", Math.Clamp(RefreshIntervalSeconds, 2, 300).ToString(), ct);
            await _settings.SetAsync("Theme", SelectedTheme, ct);
        }, "Saving settings");
        StatusMessage = "Settings saved.";
    }

    [RelayCommand]
    private async Task RefreshAuditAsync()
    {
        var result = await RunAsync(async ct => (await _audit.GetRecentAsync(500, ct)).ToList(), "Refreshing audit trail");
        if (result is not null) AuditEntries = new ObservableCollection<AuditEntry>(result);
    }

    [RelayCommand]
    private void ExportAudit()
    {
        var path = CsvExport.Write(AuditEntries, "audit-trail");
        StatusMessage = $"Exported {AuditEntries.Count} audit entries to {path}";
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        Directory.CreateDirectory(App.DataDirectory);
        Process.Start(new ProcessStartInfo { FileName = App.DataDirectory, UseShellExecute = true });
    }
}
