using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinOps.App.Helpers;
using WinOps.Core.Abstractions;
using WinOps.Core.Models;

namespace WinOps.App.ViewModels;

public sealed partial class ProcessesViewModel : ViewModelBase
{
    private readonly IProcessManager _processes;

    [ObservableProperty] private ObservableCollection<ProcessInfo> _items = [];
    [ObservableProperty] private ObservableCollection<ProcessInfo> _tree = [];
    [ObservableProperty] private ProcessInfo? _selected;
    [ObservableProperty] private string _filter = string.Empty;
    [ObservableProperty] private bool _forceStop;

    public int Count => Items.Count;

    public ProcessesViewModel(IProcessManager processes, IConfirmationService confirmation) : base(confirmation)
        => _processes = processes;

    partial void OnFilterChanged(string value) => ApplyFilter();

    private List<ProcessInfo> _all = [];

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var all = await RunAsync(async ct => (await _processes.GetProcessesAsync(ct)).ToList(), "Loading processes");
        if (all is null) return;
        _all = all;
        ApplyFilter();
        StatusMessage = $"{all.Count} processes loaded";
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(Filter)
            ? _all
            : _all.Where(p => p.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase)
                || p.Id.ToString().Contains(Filter)
                || p.ExecutablePath.Contains(Filter, StringComparison.OrdinalIgnoreCase)).ToList();
        Items = new ObservableCollection<ProcessInfo>(filtered);
        OnPropertyChanged(nameof(Count));
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        if (Selected is null) { StatusMessage = "Select a process first."; return; }
        var result = await RunAsync(async ct => await _processes.StopProcessAsync(Selected.Id, ForceStop, Confirm, CreateProgress(), ct),
            $"Stopping {Selected.Name}");
        if (result is null) return;
        await NotifyAsync(result);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ShowTreeAsync()
    {
        if (Selected is null) { StatusMessage = "Select a process first."; return; }
        var tree = await RunAsync(async ct => (await _processes.GetProcessTreeAsync(Selected.Id, ct)).ToList(), "Building process tree");
        if (tree is not null) Tree = new ObservableCollection<ProcessInfo>(tree);
    }

    [RelayCommand]
    private void Export()
    {
        var path = CsvExport.Write(Items, "processes");
        StatusMessage = $"Exported {Items.Count} processes to {path}";
    }
}

public sealed partial class ServicesViewModel : ViewModelBase
{
    private readonly IServiceManager _services;

    [ObservableProperty] private ObservableCollection<ServiceInfo> _items = [];
    [ObservableProperty] private ServiceInfo? _selected;
    [ObservableProperty] private string _filter = string.Empty;
    [ObservableProperty] private bool _showRunningOnly;
    [ObservableProperty] private ServiceStartupType _selectedStartupType = ServiceStartupType.Automatic;

    public IReadOnlyList<ServiceStartupType> StartupTypes { get; } = Enum.GetValues<ServiceStartupType>().ToList();

    public ServicesViewModel(IServiceManager services, IConfirmationService confirmation) : base(confirmation)
        => _services = services;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var all = await RunAsync(async ct => (await _services.GetServicesAsync(ct)).ToList(), "Loading services");
        if (all is null) return;
        var filtered = all.Where(s =>
                (!ShowRunningOnly || s.Status == "Running") &&
                (string.IsNullOrWhiteSpace(Filter) || s.DisplayName.Contains(Filter, StringComparison.OrdinalIgnoreCase) || s.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        Items = new ObservableCollection<ServiceInfo>(filtered);
        StatusMessage = $"{Items.Count} of {all.Count} services shown";
    }

    [RelayCommand]
    private async Task ChangeStateAsync(string action)
    {
        if (Selected is null) { StatusMessage = "Select a service first."; return; }
        var result = await RunAsync(async ct => await _services.SetServiceStateAsync(Selected.Name, action, Confirm, CreateProgress(), ct),
            $"{action} {Selected.DisplayName}");
        if (result is not null) await NotifyAsync(result);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ChangeStartupAsync(ServiceStartupType startupType)
    {
        if (Selected is null) { StatusMessage = "Select a service first."; return; }
        var result = await RunAsync(async ct => await _services.SetStartupTypeAsync(Selected.Name, startupType, Confirm, ct),
            $"Setting startup type for {Selected.DisplayName}");
        if (result is not null) await NotifyAsync(result);
        await RefreshAsync();
    }

    [RelayCommand]
    private void Export()
    {
        var path = CsvExport.Write(Items, "services");
        StatusMessage = $"Exported {Items.Count} services to {path}";
    }
}

public sealed partial class InventoryViewModel : ViewModelBase
{
    private readonly IInventoryService _inventory;

    [ObservableProperty] private ObservableCollection<StartupEntry> _startup = [];
    [ObservableProperty] private ObservableCollection<ScheduledTaskInfo> _tasks = [];
    [ObservableProperty] private ObservableCollection<InstalledSoftwareInfo> _software = [];
    [ObservableProperty] private ObservableCollection<DeviceInfo> _devices = [];
    [ObservableProperty] private ObservableCollection<DriverInfo> _drivers = [];
    [ObservableProperty] private ObservableCollection<EnvironmentVariableEntry> _variables = [];
    [ObservableProperty] private ObservableCollection<RestorePointInfo> _restorePoints = [];
    [ObservableProperty] private ScheduledTaskInfo? _selectedTask;
    [ObservableProperty] private string _environmentScope = "User";
    [ObservableProperty] private string _softwareFilter = string.Empty;

    public IReadOnlyList<string> Scopes { get; } = ["Process", "User", "Machine"];

    public InventoryViewModel(IInventoryService inventory, IConfirmationService confirmation) : base(confirmation)
        => _inventory = inventory;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RunAsync(async ct =>
        {
            var startupTask = _inventory.GetStartupEntriesAsync(ct);
            var tasksTask = _inventory.GetScheduledTasksAsync(ct);
            var softwareTask = _inventory.GetInstalledSoftwareAsync(ct);
            var devicesTask = _inventory.GetDevicesAsync(ct);
            var driversTask = _inventory.GetDriversAsync(ct);
            var variablesTask = _inventory.GetEnvironmentVariablesAsync(EnvironmentScope, ct);
            var restoreTask = _inventory.GetRestorePointsAsync(ct);
            await Task.WhenAll(startupTask, tasksTask, softwareTask, devicesTask, driversTask, variablesTask, restoreTask);
            Startup = new ObservableCollection<StartupEntry>(await startupTask);
            Tasks = new ObservableCollection<ScheduledTaskInfo>(await tasksTask);
            Software = new ObservableCollection<InstalledSoftwareInfo>(await softwareTask);
            Devices = new ObservableCollection<DeviceInfo>(await devicesTask);
            Drivers = new ObservableCollection<DriverInfo>(await driversTask);
            Variables = new ObservableCollection<EnvironmentVariableEntry>(await variablesTask);
            RestorePoints = new ObservableCollection<RestorePointInfo>(await restoreTask);
            StatusMessage = $"Inventory loaded: {Software.Count} applications, {Devices.Count} devices";
        }, "Loading system inventory");
    }

    partial void OnEnvironmentScopeChanged(string value) => _ = RefreshVariablesAsync();

    private async Task RefreshVariablesAsync()
    {
        var result = await RunAsync(async ct => (await _inventory.GetEnvironmentVariablesAsync(EnvironmentScope, ct)).ToList(), "Loading environment variables");
        if (result is not null) Variables = new ObservableCollection<EnvironmentVariableEntry>(result);
    }

    [RelayCommand]
    private async Task SetTaskStateAsync(bool enable)
    {
        if (SelectedTask is null) { StatusMessage = "Select a task first."; return; }
        var result = await RunAsync(async ct => await _inventory.SetTaskStateAsync(SelectedTask.Path, enable, Confirm, ct), $"{(enable ? "Enabling" : "Disabling")} task");
        if (result is not null) await NotifyAsync(result);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RunTaskAsync()
    {
        if (SelectedTask is null) { StatusMessage = "Select a task first."; return; }
        var result = await RunAsync(async ct => await _inventory.RunTaskAsync(SelectedTask.Path, Confirm, ct), "Running task");
        if (result is not null) await NotifyAsync(result);
    }

    [RelayCommand]
    private void Export()
    {
        var path = CsvExport.Write(Software, "installed-software");
        StatusMessage = $"Exported {Software.Count} applications to {path}";
    }
}
