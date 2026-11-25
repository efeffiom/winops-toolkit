using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using WinOps.Core.Abstractions;
using WinOps.Core.Models;
using WinOps.Persistence;

namespace WinOps.App.ViewModels;

public sealed partial class DashboardViewModel : ViewModelBase
{
    private readonly ISystemInformationProvider _system;
    private readonly IInventoryService _inventory;
    private readonly DispatcherTimer _timer;

    [ObservableProperty] private SystemInfoSnapshot _info = new();
    [ObservableProperty] private MetricSample _metrics = new(DateTimeOffset.Now, null, null, null, null, null);
    [ObservableProperty] private string _restorePointDescription = "WinOps Toolkit checkpoint";
    [ObservableProperty] private int _refreshSeconds = 5;

    public ObservableCollection<double> CpuHistory { get; } = [];
    public ObservableCollection<double> MemoryHistory { get; } = [];
    public ObservableCollection<double> DiskHistory { get; } = [];

    public ISeries[] UsageSeries { get; }
    public Axis[] YAxes { get; } = [new Axis { MinLimit = 0, MaxLimit = 100, Name = "Percent" }];
    public Axis[] XAxes { get; } = [new Axis { Name = "Samples" }];

    public DashboardViewModel(ISystemInformationProvider system, IInventoryService inventory,
        IConfirmationService confirmation, SettingsRepository settings)
        : base(confirmation)
    {
        _system = system;
        _inventory = inventory;
        UsageSeries =
        [
            new LineSeries<double> { Name = "CPU %", Values = CpuHistory, Fill = null, GeometrySize = 0 },
            new LineSeries<double> { Name = "Memory %", Values = MemoryHistory, Fill = null, GeometrySize = 0 },
            new LineSeries<double> { Name = "Disk %", Values = DiskHistory, Fill = null, GeometrySize = 0 }
        ];

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += async (_, _) => await RefreshMetricsAsync();
        _refreshSeconds = int.TryParse(settings.GetAsync("RefreshIntervalSeconds", "5").GetAwaiter().GetResult(), out var seconds) ? seconds : 5;
        _timer.Interval = TimeSpan.FromSeconds(Math.Clamp(_refreshSeconds, 2, 300));
        _ = RefreshAllAsync();
        _timer.Start();
    }

    partial void OnRefreshSecondsChanged(int value)
    {
        _timer.Interval = TimeSpan.FromSeconds(Math.Clamp(value, 2, 300));
    }

    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        await RunAsync(async ct =>
        {
            Info = await _system.GetSystemInfoAsync(ct);
            await RefreshMetricsAsync();
            return true;
        }, "Collecting system information");
    }

    private async Task RefreshMetricsAsync()
    {
        if (IsBusy) return;
        var sample = await _system.GetCurrentMetricsAsync();
        Metrics = sample;
        Append(CpuHistory, sample.CpuUsagePercent);
        Append(MemoryHistory, sample.MemoryUsagePercent);
        Append(DiskHistory, sample.DiskUsagePercent);
        StatusMessage = $"Metrics updated {sample.Timestamp:t}";
    }

    private static void Append(ObservableCollection<double> collection, double? value)
    {
        if (!value.HasValue) return;
        collection.Add(Math.Clamp(value.Value, 0, 100));
        while (collection.Count > 60) collection.RemoveAt(0);
    }

    [RelayCommand]
    private async Task CreateRestorePointAsync()
    {
        var result = await RunAsync(async ct =>
            await _inventory.CreateRestorePointAsync(RestorePointDescription, Confirm, ct), "Creating restore point");
        if (result is not null) await NotifyAsync(result);
    }

    [RelayCommand]
    private async Task PowerAsync(string action)
    {
        var result = await RunAsync(async ct =>
            await _inventory.SetPowerStateAsync(action, Confirm, ct), $"Requesting {action}");
        if (result is not null) await NotifyAsync(result);
    }
}
