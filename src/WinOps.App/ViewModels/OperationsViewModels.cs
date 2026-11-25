using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinOps.App.Helpers;
using WinOps.Core.Abstractions;
using WinOps.Core.Models;

namespace WinOps.App.ViewModels;

public sealed partial class NetworkViewModel : ViewModelBase
{
    private readonly INetworkService _network;

    [ObservableProperty] private ObservableCollection<NetworkAdapterInfo> _adapters = [];
    [ObservableProperty] private ObservableCollection<RouteEntry> _routes = [];
    [ObservableProperty] private ObservableCollection<ArpEntry> _arpEntries = [];
    [ObservableProperty] private ObservableCollection<NetworkConnectionInfo> _connections = [];
    [ObservableProperty] private ObservableCollection<DnsLookupResult> _dnsResults = [];
    [ObservableProperty] private ObservableCollection<TracerouteHop> _hops = [];
    [ObservableProperty] private ObservableCollection<string> _commandOutput = [];
    [ObservableProperty] private NetworkAdapterInfo? _selectedAdapter;
    [ObservableProperty] private PingResult? _pingResult;
    [ObservableProperty] private WifiInfo? _wifi;
    [ObservableProperty] private string _publicIp = "Not checked";
    [ObservableProperty] private string _host = "www.microsoft.com";
    [ObservableProperty] private int _pingCount = 4;
    [ObservableProperty] private int _port = 443;
    [ObservableProperty] private int _maxHops = 30;
    [ObservableProperty] private string _dnsServers = "1.1.1.1, 8.8.8.8";
    [ObservableProperty] private string _staticIp = string.Empty;
    [ObservableProperty] private string _prefixLength = "24";
    [ObservableProperty] private string _gateway = string.Empty;

    public NetworkViewModel(INetworkService network, IConfirmationService confirmation) : base(confirmation)
        => _network = network;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RunAsync(async ct =>
        {
            var adaptersTask = _network.GetAdaptersAsync(ct);
            var routesTask = _network.GetRoutesAsync(ct);
            var arpTask = _network.GetArpEntriesAsync(ct);
            var connectionsTask = _network.GetConnectionsAsync(ct);
            var wifiTask = _network.GetWifiInfoAsync(ct);
            var publicIpTask = _network.GetPublicIpAsync(ct);
            await Task.WhenAll(adaptersTask, routesTask, arpTask, connectionsTask, wifiTask, publicIpTask);
            Adapters = new ObservableCollection<NetworkAdapterInfo>(await adaptersTask);
            Routes = new ObservableCollection<RouteEntry>(await routesTask);
            ArpEntries = new ObservableCollection<ArpEntry>(await arpTask);
            Connections = new ObservableCollection<NetworkConnectionInfo>(await connectionsTask);
            Wifi = await wifiTask;
            PublicIp = await publicIpTask;
            SelectedAdapter = Adapters.FirstOrDefault();
            StatusMessage = $"Network snapshot: {Adapters.Count} adapter(s), {Connections.Count} connection(s)";
        }, "Collecting network information");
    }

    [RelayCommand]
    private async Task PingAsync()
    {
        var result = await RunAsync(ct => _network.PingAsync(Host, PingCount, ct), $"Pinging {Host}");
        if (result is not null) PingResult = result;
    }

    [RelayCommand]
    private async Task TracerouteAsync()
    {
        var result = await RunAsync(async ct => (await _network.TracerouteAsync(Host, MaxHops, ct)).ToList(), $"Tracing route to {Host}");
        if (result is not null) Hops = new ObservableCollection<TracerouteHop>(result);
    }

    [RelayCommand]
    private async Task ResolveDnsAsync()
    {
        var result = await RunAsync(async ct => (await _network.ResolveDnsAsync(Host, ct)).ToList(), $"Resolving {Host}");
        if (result is not null) DnsResults = new ObservableCollection<DnsLookupResult>(result);
    }

    [RelayCommand]
    private async Task TestPortAsync()
    {
        var result = await RunAsync(ct => _network.TestPortAsync(Host, Port, 4000, ct), $"Testing {Host}:{Port}");
        if (result is null) return;
        StatusMessage = result.Open
            ? $"TCP {result.Port} is open on {result.Host} ({result.DurationMs} ms)."
            : $"TCP {result.Port} is closed or unreachable. {result.Error}";
    }

    [RelayCommand]
    private async Task RunCommandAsync(string action)
    {
        if (string.IsNullOrWhiteSpace(action)) return;
        var adapter = SelectedAdapter?.Name;
        var result = await RunAsync(ct => _network.RunNetworkCommandAsync(action, adapter, Confirm, CreateProgress(), ct), $"Running network action: {action}");
        if (result is not null) await NotifyAsync(result);
        if (result?.Success == true) await RefreshAsync();
    }

    [RelayCommand]
    private async Task ApplyDnsAsync()
    {
        if (SelectedAdapter is null) { StatusMessage = "Select an adapter first."; return; }
        var servers = DnsServers.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var result = await RunAsync(ct => _network.SetDnsServersAsync(SelectedAdapter.Name, servers, Confirm, ct), $"Setting DNS on {SelectedAdapter.Name}");
        if (result is not null) await NotifyAsync(result);
        if (result?.Success == true) await RefreshAsync();
    }

    [RelayCommand]
    private async Task ApplyStaticIpAsync()
    {
        if (SelectedAdapter is null) { StatusMessage = "Select an adapter first."; return; }
        var result = await RunAsync(ct => _network.SetStaticIpAsync(SelectedAdapter.Name, StaticIp, PrefixLength, Gateway, Confirm, ct),
            $"Configuring static IP on {SelectedAdapter.Name}");
        if (result is not null) await NotifyAsync(result);
        if (result?.Success == true) await RefreshAsync();
    }

    [RelayCommand]
    private async Task RestoreDhcpAsync()
    {
        if (SelectedAdapter is null) { StatusMessage = "Select an adapter first."; return; }
        var result = await RunAsync(ct => _network.RestoreDhcpAsync(SelectedAdapter.Name, Confirm, ct), $"Restoring DHCP on {SelectedAdapter.Name}");
        if (result is not null) await NotifyAsync(result);
        if (result?.Success == true) await RefreshAsync();
    }

    [RelayCommand]
    private void Export()
    {
        var path = CsvExport.Write(Adapters, "network-adapters");
        StatusMessage = $"Exported {Adapters.Count} adapters to {path}";
    }
}

public sealed partial class MaintenanceViewModel : ViewModelBase
{
    private readonly IMaintenanceService _maintenance;

    [ObservableProperty] private ObservableCollection<WindowsUpdateInfo> _history = [];
    [ObservableProperty] private ObservableCollection<WindowsUpdateInfo> _pending = [];
    [ObservableProperty] private ObservableCollection<RepairOperationInfo> _repairs = [];
    [ObservableProperty] private RepairOperationInfo? _selectedRepair;

    public MaintenanceViewModel(IMaintenanceService maintenance, IConfirmationService confirmation) : base(confirmation)
        => _maintenance = maintenance;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RunAsync(async ct =>
        {
            var historyTask = _maintenance.GetUpdateHistoryAsync(ct);
            var pendingTask = _maintenance.GetPendingUpdatesAsync(ct);
            await Task.WhenAll(historyTask, pendingTask);
            History = new ObservableCollection<WindowsUpdateInfo>(await historyTask);
            Pending = new ObservableCollection<WindowsUpdateInfo>(await pendingTask);
            Repairs = new ObservableCollection<RepairOperationInfo>(await _maintenance.GetRepairOperationsAsync());
            SelectedRepair = Repairs.FirstOrDefault();
            StatusMessage = $"{History.Count} installed update(s), {Pending.Count} pending update(s)";
        }, "Loading Windows Update information");
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        var result = await RunAsync(ct => _maintenance.ScanUpdatesAsync(Confirm, CreateProgress(), ct), "Scanning for updates");
        if (result is not null) await NotifyAsync(result);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task InstallUpdatesAsync()
    {
        var result = await RunAsync(ct => _maintenance.InstallUpdatesAsync(Confirm, CreateProgress(), ct), "Installing Windows updates");
        if (result is not null) await NotifyAsync(result);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RunRepairAsync()
    {
        if (SelectedRepair is null) { StatusMessage = "Select a repair operation first."; return; }
        var result = await RunAsync(ct => _maintenance.RunRepairAsync(SelectedRepair.Id, Confirm, CreateProgress(), ct), $"Running {SelectedRepair.Title}");
        if (result is not null) await NotifyAsync(result);
    }
}

public sealed partial class SecurityViewModel : ViewModelBase
{
    private readonly ISecurityService _security;

    [ObservableProperty] private ObservableCollection<SecurityProfile> _profiles = [];
    [ObservableProperty] private ObservableCollection<SecurityCheck> _checks = [];
    [ObservableProperty] private SecurityAssessmentResult? _assessment;
    [ObservableProperty] private SecurityProfile? _selectedProfile;

    public SecurityViewModel(ISecurityService security, IConfirmationService confirmation) : base(confirmation)
        => _security = security;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RunAsync(async ct =>
        {
            Profiles = new ObservableCollection<SecurityProfile>(await _security.GetProfilesAsync(ct));
            SelectedProfile = Profiles.FirstOrDefault();
            StatusMessage = $"{Profiles.Count} security profile(s) available";
        }, "Loading security profiles");
    }

    [RelayCommand]
    private async Task AssessAsync()
    {
        if (SelectedProfile is null) { StatusMessage = "Select a profile first."; return; }
        var result = await RunAsync(ct => _security.AssessAsync(SelectedProfile.Name, ct), $"Assessing {SelectedProfile.Name}");
        if (result is not null)
        {
            Assessment = result;
            Checks = new ObservableCollection<SecurityCheck>(result.Checks);
            StatusMessage = $"Assessment complete: {result.Passed} passed, {result.Warnings} warnings, {result.Failures} failures";
        }
    }

    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (SelectedProfile is null) { StatusMessage = "Select a profile first."; return; }
        var result = await RunAsync(async ct => (await _security.PreviewProfileAsync(SelectedProfile.Name, ct)).ToList(), $"Previewing {SelectedProfile.Name}");
        if (result is not null)
        {
            Checks = new ObservableCollection<SecurityCheck>(result);
            StatusMessage = $"Preview shows {result.Count} check(s).";
        }
    }

    [RelayCommand]
    private async Task ApplyProfileAsync()
    {
        if (SelectedProfile is null) { StatusMessage = "Select a profile first."; return; }
        var checkIds = Checks.Where(c => c.Status != CheckStatus.Passed).Select(c => c.Id).ToList();
        var result = await RunAsync(ct => _security.ApplyProfileAsync(SelectedProfile.Name, checkIds, Confirm, CreateProgress(), ct),
            $"Applying {SelectedProfile.Name}");
        if (result is not null) await NotifyAsync(result);
        await AssessAsync();
    }
}

public sealed partial class UsersViewModel : ViewModelBase
{
    private readonly ILocalUserService _users;

    [ObservableProperty] private ObservableCollection<LocalAccount> _accounts = [];
    [ObservableProperty] private ObservableCollection<LocalGroupInfo> _groups = [];
    [ObservableProperty] private ObservableCollection<ActionExecutionResult> _bulkResults = [];
    [ObservableProperty] private LocalAccount? _selectedAccount;
    [ObservableProperty] private LocalGroupInfo? _selectedGroup;
    [ObservableProperty] private string _newUserName = string.Empty;
    [ObservableProperty] private string _newUserFullName = string.Empty;
    [ObservableProperty] private string _newPassword = string.Empty;
    [ObservableProperty] private string _resetPassword = string.Empty;
    [ObservableProperty] private string _bulkCsv = "Name,FullName,Password,Groups,Enabled\n";

    public UsersViewModel(ILocalUserService users, IConfirmationService confirmation) : base(confirmation)
        => _users = users;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RunAsync(async ct =>
        {
            var usersTask = _users.GetUsersAsync(ct);
            var groupsTask = _users.GetGroupsAsync(ct);
            await Task.WhenAll(usersTask, groupsTask);
            Accounts = new ObservableCollection<LocalAccount>(await usersTask);
            Groups = new ObservableCollection<LocalGroupInfo>(await groupsTask);
            SelectedAccount = Accounts.FirstOrDefault();
            SelectedGroup = Groups.FirstOrDefault();
            StatusMessage = $"{Accounts.Count} local account(s), {Groups.Count} group(s)";
        }, "Loading local accounts");
    }

    [RelayCommand]
    private async Task CreateUserAsync()
    {
        var result = await RunAsync(ct => _users.CreateUserAsync(NewUserName, NewPassword, NewUserFullName, Confirm, ct), $"Creating user {NewUserName}");
        if (result is not null) await NotifyAsync(result);
        if (result?.Success == true) { NewUserName = string.Empty; NewUserFullName = string.Empty; NewPassword = string.Empty; }
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task SetAccountStateAsync(bool enable)
    {
        if (SelectedAccount is null) { StatusMessage = "Select an account first."; return; }
        var result = await RunAsync(ct => _users.SetUserStateAsync(SelectedAccount.Name, enable, Confirm, ct),
            $"{(enable ? "Enabling" : "Disabling")} {SelectedAccount.Name}");
        if (result is not null) await NotifyAsync(result);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ResetPasswordAsync()
    {
        if (SelectedAccount is null) { StatusMessage = "Select an account first."; return; }
        var result = await RunAsync(ct => _users.ResetPasswordAsync(SelectedAccount.Name, ResetPassword, Confirm, ct), $"Resetting password for {SelectedAccount.Name}");
        if (result is not null) await NotifyAsync(result);
        ResetPassword = string.Empty;
    }

    [RelayCommand]
    private async Task ChangeGroupMembershipAsync(bool add)
    {
        if (SelectedAccount is null || SelectedGroup is null) { StatusMessage = "Select an account and a group first."; return; }
        var result = await RunAsync(ct => _users.SetGroupMembershipAsync(SelectedGroup.Name, SelectedAccount.Name, add, Confirm, ct),
            $"{(add ? "Adding" : "Removing")} {SelectedAccount.Name} {(add ? "to" : "from")} {SelectedGroup.Name}");
        if (result is not null) await NotifyAsync(result);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ApplyBulkAsync()
    {
        var results = await RunAsync(async ct => (await _users.RunBulkOperationAsync(BulkCsv, true, Confirm, ct)).ToList(), "Applying bulk user changes");
        if (results is null) return;
        BulkResults = new ObservableCollection<ActionExecutionResult>(results);
        StatusMessage = $"Bulk operation completed: {results.Count(r => r.Success)} succeeded, {results.Count(r => !r.Success)} failed";
        await RefreshAsync();
    }

    [RelayCommand]
    private void Export()
    {
        var path = CsvExport.Write(Accounts, "local-users");
        StatusMessage = $"Exported {Accounts.Count} accounts to {path}";
    }
}

public sealed partial class ActiveDirectoryViewModel : ViewModelBase
{
    private readonly IActiveDirectoryService _directory;

    [ObservableProperty] private ObservableCollection<AdUser> _users = [];
    [ObservableProperty] private ObservableCollection<AdGroup> _groups = [];
    [ObservableProperty] private ObservableCollection<OrganizationalUnit> _units = [];
    [ObservableProperty] private ObservableCollection<DiagnosticFinding> _findings = [];
    [ObservableProperty] private DomainInfo? _domain;
    [ObservableProperty] private AdUser? _selectedUser;
    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private string _newPassword = string.Empty;
    [ObservableProperty] private string _prerequisiteMessage;

    public bool IsAvailable { get; }

    public ActiveDirectoryViewModel(IActiveDirectoryService directory, IConfirmationService confirmation) : base(confirmation)
    {
        _directory = directory;
        IsAvailable = directory.IsAvailable;
        _prerequisiteMessage = directory.PrerequisiteMessage;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!IsAvailable) { StatusMessage = PrerequisiteMessage; return; }
        await RunAsync(async ct =>
        {
            var domainTask = _directory.GetDomainInfoAsync(ct);
            var usersTask = _directory.FindUsersAsync(Search, 100, ct);
            var groupsTask = _directory.GetGroupsAsync(Search, ct);
            var unitsTask = _directory.GetOrganizationalUnitsAsync(ct);
            await Task.WhenAll(domainTask, usersTask, groupsTask, unitsTask);
            Domain = await domainTask;
            Users = new ObservableCollection<AdUser>(await usersTask);
            Groups = new ObservableCollection<AdGroup>(await groupsTask);
            Units = new ObservableCollection<OrganizationalUnit>(await unitsTask);
            SelectedUser = Users.FirstOrDefault();
            StatusMessage = $"Directory query returned {Users.Count} user(s) and {Groups.Count} group(s).";
        }, "Querying Active Directory");
    }

    [RelayCommand]
    private async Task RunDiagnosticsAsync()
    {
        if (!IsAvailable) { StatusMessage = PrerequisiteMessage; return; }
        var result = await RunAsync(async ct => (await _directory.RunDomainDiagnosticsAsync(ct)).ToList(), "Running domain diagnostics");
        if (result is not null) Findings = new ObservableCollection<DiagnosticFinding>(result);
    }

    [RelayCommand]
    private async Task SetUserStateAsync(bool enable)
    {
        if (SelectedUser is null) { StatusMessage = "Select a domain user first."; return; }
        var result = await RunAsync(ct => _directory.SetUserStateAsync(SelectedUser.DistinguishedName, enable, Confirm, ct),
            $"{(enable ? "Enabling" : "Disabling")} {SelectedUser.SamAccountName}");
        if (result is not null) await NotifyAsync(result);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task UnlockUserAsync()
    {
        if (SelectedUser is null) { StatusMessage = "Select a domain user first."; return; }
        var result = await RunAsync(ct => _directory.UnlockUserAsync(SelectedUser.DistinguishedName, Confirm, ct), $"Unlocking {SelectedUser.SamAccountName}");
        if (result is not null) await NotifyAsync(result);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ResetPasswordAsync()
    {
        if (SelectedUser is null) { StatusMessage = "Select a domain user first."; return; }
        var result = await RunAsync(ct => _directory.ResetPasswordAsync(SelectedUser.DistinguishedName, NewPassword, Confirm, ct),
            $"Resetting password for {SelectedUser.SamAccountName}");
        if (result is not null) await NotifyAsync(result);
        NewPassword = string.Empty;
    }
}

public sealed partial class StorageViewModel : ViewModelBase
{
    private readonly IStorageService _storage;

    [ObservableProperty] private ObservableCollection<PhysicalDiskInfo> _disks = [];
    [ObservableProperty] private ObservableCollection<VolumeInfo> _volumes = [];
    [ObservableProperty] private ObservableCollection<CleanupSuggestion> _suggestions = [];
    [ObservableProperty] private ObservableCollection<FolderSizeEntry> _folders = [];
    [ObservableProperty] private ObservableCollection<LargeFileInfo> _largeFiles = [];
    [ObservableProperty] private CleanupSuggestion? _selectedSuggestion;
    [ObservableProperty] private string _analysisPath = "C:\\";
    [ObservableProperty] private int _minimumFileSizeMb = 500;
    [ObservableProperty] private string _approvedPaths = string.Empty;

    public StorageViewModel(IStorageService storage, IConfirmationService confirmation) : base(confirmation)
        => _storage = storage;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RunAsync(async ct =>
        {
            var disksTask = _storage.GetPhysicalDisksAsync(ct);
            var volumesTask = _storage.GetVolumesAsync(ct);
            var suggestionsTask = _storage.GetCleanupSuggestionsAsync(ct);
            await Task.WhenAll(disksTask, volumesTask, suggestionsTask);
            Disks = new ObservableCollection<PhysicalDiskInfo>(await disksTask);
            Volumes = new ObservableCollection<VolumeInfo>(await volumesTask);
            Suggestions = new ObservableCollection<CleanupSuggestion>(await suggestionsTask);
            SelectedSuggestion = Suggestions.FirstOrDefault();
            StatusMessage = $"Storage snapshot: {Disks.Count} disk(s), {Volumes.Count} volume(s)";
        }, "Loading storage information");
    }

    [RelayCommand]
    private async Task AnalyzeFolderAsync()
    {
        var result = await RunAsync(async ct => (await _storage.AnalyzeFolderAsync(AnalysisPath, 50, CreateProgress(), ct)).ToList(), $"Analyzing {AnalysisPath}");
        if (result is not null) Folders = new ObservableCollection<FolderSizeEntry>(result);
    }

    [RelayCommand]
    private async Task FindLargeFilesAsync()
    {
        var result = await RunAsync(async ct => (await _storage.FindLargeFilesAsync(AnalysisPath, Math.Max(1, MinimumFileSizeMb) * 1024L * 1024L, 100, CreateProgress(), ct)).ToList(),
            $"Searching for files larger than {MinimumFileSizeMb} MB");
        if (result is not null) LargeFiles = new ObservableCollection<LargeFileInfo>(result);
    }

    [RelayCommand]
    private async Task CleanupAsync()
    {
        if (SelectedSuggestion is null) { StatusMessage = "Select a cleanup suggestion first."; return; }
        var paths = ApprovedPaths.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (paths.Count == 0) paths.Add(SelectedSuggestion.Path);
        var result = await RunAsync(ct => _storage.CleanupAsync(SelectedSuggestion.Id, paths, Confirm, CreateProgress(), ct), $"Running cleanup: {SelectedSuggestion.Title}");
        if (result is not null) await NotifyAsync(result);
        await RefreshAsync();
    }

    [RelayCommand]
    private void Export()
    {
        var path = CsvExport.Write(Volumes, "volumes");
        StatusMessage = $"Exported {Volumes.Count} volumes to {path}";
    }
}
