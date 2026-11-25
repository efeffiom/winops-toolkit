using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using WinOps.App.Services;
using WinOps.App.ViewModels;
using WinOps.Core.Abstractions;
using WinOps.Core.Diagnostics;
using WinOps.Core.Safety;
using WinOps.Persistence;
using WinOps.PowerShell.Execution;
using WinOps.Services.Diagnostics;
using WinOps.Services.Reporting;
using WinOps.Windows.Accounts;
using WinOps.Windows.ActiveDirectory;
using WinOps.Windows.Administration;
using WinOps.Windows.Diagnostics;
using WinOps.Windows.Integration;
using WinOps.Windows.Maintenance;
using WinOps.Windows.Monitoring;
using WinOps.Windows.Networking;
using WinOps.Windows.Remote;
using WinOps.Windows.Security;
using WinOps.Windows.Storage;
using AdService = WinOps.Windows.ActiveDirectory.ActiveDirectoryService;
using EventLogSvc = WinOps.Windows.Diagnostics.EventLogService;
using SecuritySvc = WinOps.Windows.Security.SecurityService;

namespace WinOps.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static string DataDirectory { get; private set; } = string.Empty;
    public static Version AppVersion => typeof(App).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinOps Toolkit");
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(Path.Combine(DataDirectory, "Logs"));

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(Path.Combine(DataDirectory, "Logs", "winops-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .CreateLogger();

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception");
            MessageBox.Show($"An unexpected error occurred:\n\n{args.Exception.Message}\n\nDetails are logged in {DataDirectory}\\Logs.",
                "WinOps Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error(args.ExceptionObject as Exception ?? new Exception("Unhandled domain exception"), "Unhandled domain exception");

        var services = new ServiceCollection();
        var databasePath = Path.Combine(DataDirectory, "winops.db");

        services.AddSingleton(_ => Log.Logger);
        services.AddSingleton<Func<AppDbContext>>(_ => () => new AppDbContext(databasePath));
        services.AddSingleton<AuditRepository>();
        services.AddSingleton<IAuditSink>(sp => sp.GetRequiredService<AuditRepository>());
        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<DiagnosticRunRepository>();

        services.AddSingleton<IPrivilegeService, PrivilegeService>();
        services.AddSingleton<ActionSafetyEngine>();
        services.AddSingleton<CommandRunner>();

        services.AddSingleton<ISystemInformationProvider, SystemInformationProvider>();
        services.AddSingleton<IProcessManager, ProcessManager>();
        services.AddSingleton<IServiceManager, ServiceManager>();
        services.AddSingleton<IInventoryService, InventoryService>();
        services.AddSingleton<INetworkService, NetworkService>();
        services.AddSingleton<IMaintenanceService, MaintenanceService>();
        services.AddSingleton<ISecurityService, SecuritySvc>();
        services.AddSingleton<ILocalUserService, LocalUserService>();
        services.AddSingleton<IActiveDirectoryService, AdService>();
        services.AddSingleton<IStorageService, StorageService>();
        services.AddSingleton<IEventLogService, EventLogSvc>();
        services.AddSingleton<IPowerShellService, PowerShellExecutor>();
        services.AddSingleton<IRemoteAdministrationService, RemoteAdministrationService>();

        services.AddSingleton<DiagnosticEngine>();
        services.AddSingleton<DiagnosticScenarioRegistrar>();
        services.AddSingleton<ReportService>();
        services.AddSingleton<ConfirmationService>();
        services.AddSingleton<IConfirmationService>(sp => sp.GetRequiredService<ConfirmationService>());
        services.AddSingleton<ConfirmationDialogService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<NavigationService>(sp => new NavigationService(sp));

        RegisterViewModels(services);
        RegisterViews(services);

        Services = services.BuildServiceProvider();
        Services.GetRequiredService<DiagnosticScenarioRegistrar>().Register(Services.GetRequiredService<DiagnosticEngine>());
        var confirmation = Services.GetRequiredService<ConfirmationService>();
        var dialog = Services.GetRequiredService<ConfirmationDialogService>();
        confirmation.ConfirmationHandler = action => dialog.ConfirmAsync(action);
        Services.GetRequiredService<ThemeService>().ApplySavedTheme();

        Log.Information("WinOps Toolkit {Version} started (administrator: {Admin})",
            AppVersion, Services.GetRequiredService<IPrivilegeService>().IsAdministrator());
    }

    private static void RegisterViewModels(IServiceCollection services)
    {
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ProcessesViewModel>();
        services.AddSingleton<ServicesViewModel>();
        services.AddSingleton<InventoryViewModel>();
        services.AddSingleton<NetworkViewModel>();
        services.AddSingleton<MaintenanceViewModel>();
        services.AddSingleton<SecurityViewModel>();
        services.AddSingleton<UsersViewModel>();
        services.AddSingleton<ActiveDirectoryViewModel>();
        services.AddSingleton<StorageViewModel>();
        services.AddSingleton<EventsViewModel>();
        services.AddSingleton<AutomationViewModel>();
        services.AddSingleton<RemoteViewModel>();
        services.AddSingleton<TroubleshootingViewModel>();
        services.AddSingleton<ReportsViewModel>();
        services.AddSingleton<SettingsViewModel>();
    }

    private static void RegisterViews(IServiceCollection services)
    {
        foreach (var item in NavigationService.Items)
            services.AddTransient(item.PageType);
        foreach (var type in new[] { typeof(Views.ConfirmWindow), typeof(Views.RestorePointDialog) })
            services.AddTransient(type);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
