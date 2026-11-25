using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinOps.App.Services;
using WinOps.Core.Abstractions;
using WinOps.Core.Models;
using WinOps.Persistence;

namespace WinOps.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly NavigationService _navigation;
    private readonly ThemeService _theme;
    private readonly IPrivilegeService _privileges;
    private readonly SettingsRepository _settings;
    private readonly AuditRepository _audit;
    private ViewModelBase? _observed;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private bool _progressVisible;
    [ObservableProperty] private string _currentTitle = "Dashboard";
    [ObservableProperty] private string _notification = string.Empty;

    public ObservableCollection<NavigationItem> NavigationItems { get; } = [.. NavigationService.Items];
    public IReadOnlyList<string> Categories { get; } = NavigationService.Items.Select(i => i.Category).Distinct().ToList();

    public bool IsAdministrator => _privileges.IsAdministrator();
    public string UserName => _privileges.CurrentUserName;
    public string VersionText => $"WinOps Toolkit v{App.AppVersion}";
    public string ThemeLabel => _theme.CurrentTheme == "Dark" ? "Light mode" : "Dark mode";
    public string PrivilegeLabel => IsAdministrator ? "Administrator" : "Standard user";
    public object? CurrentPage => _navigation.CurrentPage;
    public int AuditCount { get; private set; }

    public MainViewModel(NavigationService navigation, ThemeService theme, IPrivilegeService privileges,
        SettingsRepository settings, AuditRepository audit)
    {
        _navigation = navigation;
        _theme = theme;
        _privileges = privileges;
        _settings = settings;
        _audit = audit;
        _navigation.Navigated += OnNavigated;
        theme.ThemeChanged += () =>
        {
            OnPropertyChanged(nameof(ThemeLabel));
            _ = _settings.SetAsync("Theme", theme.CurrentTheme);
        };
        StatusBridge.Instance.StatusChanged += (message, percent) =>
        {
            StatusMessage = message;
            ProgressPercent = percent;
            ProgressVisible = percent is > 0 and < 100;
        };
        Navigate("dashboard");
        _ = RefreshAuditCountAsync();
    }

    public async Task RefreshAuditCountAsync()
    {
        AuditCount = (await _audit.GetRecentAsync(500)).Count;
        OnPropertyChanged(nameof(AuditCount));
    }

    partial void OnSearchTextChanged(string value)
    {
        var filtered = string.IsNullOrWhiteSpace(value)
            ? NavigationService.Items
            : NavigationService.Items.Where(i => i.Title.Contains(value, StringComparison.OrdinalIgnoreCase)
                || i.Category.Contains(value, StringComparison.OrdinalIgnoreCase)
                || i.Key.Contains(value, StringComparison.OrdinalIgnoreCase));
        NavigationItems.Clear();
        foreach (var item in filtered) NavigationItems.Add(item);
    }

    [RelayCommand]
    private void Navigate(string key) => _navigation.Navigate(key);

    [RelayCommand]
    private void ToggleTheme()
    {
        _theme.Toggle();
        OnPropertyChanged(nameof(ThemeLabel));
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_navigation.CurrentPage?.DataContext is ViewModelBase viewModel && !viewModel.IsBusy)
            await viewModel.RefreshCurrentAsync();
    }

    [RelayCommand]
    private void Notify(ActionExecutionResult result)
    {
        Notification = $"{result.Message}{(result.Success ? string.Empty : $" - {result.Error}")}";
        StatusMessage = Notification;
        _ = RefreshAuditCountAsync();
    }

    private void OnNavigated()
    {
        OnPropertyChanged(nameof(CurrentPage));
        CurrentTitle = _navigation.Current?.Title ?? "WinOps Toolkit";
        if (_observed is not null) _observed.PropertyChanged -= ObservedOnPropertyChanged;
        _observed = (_navigation.CurrentPage?.DataContext) as ViewModelBase;
        if (_observed is not null)
        {
            _observed.PropertyChanged += ObservedOnPropertyChanged;
            StatusMessage = _observed.StatusMessage;
        }
    }

    private void ObservedOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_observed is null) return;
        if (e.PropertyName == nameof(ViewModelBase.StatusMessage)) StatusMessage = _observed.StatusMessage;
        if (e.PropertyName == nameof(ViewModelBase.ProgressPercent)) ProgressPercent = _observed.ProgressPercent;
        if (e.PropertyName == nameof(ViewModelBase.ProgressVisible)) ProgressVisible = _observed.ProgressVisible;
    }
}
