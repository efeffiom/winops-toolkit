using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using WinOps.App.ViewModels;
using WinOps.App.Views;

namespace WinOps.App.Services;

public sealed record NavigationItem(string Key, string Title, string Glyph, string Category, Type PageType, Type ViewModelType);

public sealed class NavigationService
{
    private readonly IServiceProvider _provider;

    public NavigationService(IServiceProvider provider)
    {
        _provider = provider;
    }

    public static IReadOnlyList<NavigationItem> Items { get; } =
    [
        new("dashboard", "Dashboard", "\uE10B", "Overview", typeof(DashboardView), typeof(DashboardViewModel)),
        new("processes", "Process Manager", "\uE3E7", "System", typeof(ProcessesView), typeof(ProcessesViewModel)),
        new("services", "Services", "\uE28D", "System", typeof(ServicesView), typeof(ServicesViewModel)),
        new("inventory", "Inventory", "\uE14F", "System", typeof(InventoryView), typeof(InventoryViewModel)),
        new("network", "Network Toolkit", "\uE17D", "Diagnostics", typeof(NetworkView), typeof(NetworkViewModel)),
        new("maintenance", "Windows Maintenance", "\uE1C8", "Diagnostics", typeof(MaintenanceView), typeof(MaintenanceViewModel)),
        new("security", "Security Center", "\uE1DE", "Security", typeof(SecurityView), typeof(SecurityViewModel)),
        new("users", "Local Users", "\uE13D", "Security", typeof(UsersView), typeof(UsersViewModel)),
        new("ad", "Active Directory", "\uE1F0", "Security", typeof(ActiveDirectoryView), typeof(ActiveDirectoryViewModel)),
        new("storage", "Storage", "\uE1BC", "Maintenance", typeof(StorageView), typeof(StorageViewModel)),
        new("events", "Event Logs", "\uE1C5", "Diagnostics", typeof(EventsView), typeof(EventsViewModel)),
        new("automation", "PowerShell Center", "\uE15D", "Automation", typeof(AutomationView), typeof(AutomationViewModel)),
        new("remote", "Remote Administration", "\uE17D", "Automation", typeof(RemoteView), typeof(RemoteViewModel)),
        new("troubleshoot", "Troubleshooting", "\uE1E0", "Diagnostics", typeof(TroubleshootingView), typeof(TroubleshootingViewModel)),
        new("reports", "Reports", "\uE14D", "Reporting", typeof(ReportsView), typeof(ReportsViewModel)),
        new("settings", "Settings & Audit", "\uE1A7", "Application", typeof(SettingsView), typeof(SettingsViewModel))
    ];

    public Page? CurrentPage { get; private set; }
    public string CurrentKey { get; private set; } = "dashboard";
    public event Action? Navigated;

    public Page Navigate(string key)
    {
        var item = Items.FirstOrDefault(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? Items[0];
        var page = (Page)_provider.GetRequiredService(item.PageType);
        page.DataContext = _provider.GetRequiredService(item.ViewModelType);
        CurrentPage = page;
        CurrentKey = item.Key;
        Navigated?.Invoke();
        return page;
    }

    public NavigationItem? Current => Items.FirstOrDefault(i => i.Key == CurrentKey);
}
