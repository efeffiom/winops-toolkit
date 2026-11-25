using System.Management;
using System.Runtime.Versioning;
using System.ServiceProcess;
using WinOps.Core.Abstractions;
using WinOps.Core.Models;
using WinOps.Core.Safety;
using WinOps.Windows.Integration;

namespace WinOps.Windows.Administration;

[SupportedOSPlatform("windows")]
public sealed class ServiceManager : IServiceManager
{
    private static readonly HashSet<string> CriticalServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "rpcss", "lsass", "winmgmt", "winDefend", "wuauserv", "trustedinstaller", "cryptsvc", "dhcp", "dnscache", "eventlog", "samss"
    };

    private readonly ActionSafetyEngine _safety;

    public ServiceManager(ActionSafetyEngine safety) => _safety = safety;

    public async Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(CancellationToken ct = default) =>
        await Task.Run(Collect, ct);

    private static IReadOnlyList<ServiceInfo> Collect()
    {
        var dependencies = ReadDependencies();
        var result = new List<ServiceInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name,DisplayName,State,StartMode,Description,PathName,StartName FROM Win32_Service");
            foreach (var item in searcher.Get())
            {
                using var service = (ManagementObject)item;
                var name = Convert.ToString(service["Name"]) ?? string.Empty;
                result.Add(new ServiceInfo(
                    name,
                    Convert.ToString(service["DisplayName"]) ?? name,
                    Convert.ToString(service["State"]) ?? "Unknown",
                    Convert.ToString(service["StartMode"])?.Replace("Delayed Auto", "AutomaticDelayedStart") switch
                    {
                        "Auto" => ServiceStartupType.Automatic,
                        "AutomaticDelayedStart" => ServiceStartupType.AutomaticDelayedStart,
                        "Manual" => ServiceStartupType.Manual,
                        "Disabled" => ServiceStartupType.Disabled,
                        _ => ServiceStartupType.Unknown
                    },
                    Convert.ToString(service["Description"]) ?? string.Empty,
                    Convert.ToString(service["PathName"]) ?? string.Empty,
                    Convert.ToString(service["StartName"]) ?? string.Empty,
                    dependencies.TryGetValue(name, out var depends) ? depends : [])
                {
                    IsCritical = CriticalServices.Contains(name)
                });
            }
        }
        catch (ManagementException) { /* partial inventory is still useful */ }
        return result.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static Dictionary<string, List<string>> ReadDependencies()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in CimQuery.Query("SELECT Antecedent,Dependent FROM Win32_ServiceDependency"))
        {
            var dependent = ParseServiceName(CimQuery.Text(row, "Dependent", string.Empty));
            var antecedent = ParseServiceName(CimQuery.Text(row, "Antecedent", string.Empty));
            if (string.IsNullOrEmpty(dependent) || string.IsNullOrEmpty(antecedent)) continue;
            if (!map.TryGetValue(dependent, out var list)) map[dependent] = list = [];
            if (!list.Contains(antecedent, StringComparer.OrdinalIgnoreCase)) list.Add(antecedent);
        }
        return map;
    }

    private static string ParseServiceName(string reference)
    {
        var start = reference.IndexOf("Name=\"", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return string.Empty;
        start += 6;
        var end = reference.IndexOf('"', start);
        return end > start ? reference[start..end] : string.Empty;
    }

    public Task<ActionExecutionResult> SetServiceStateAsync(
        string serviceName,
        string action,
        Func<ActionDefinition, Task<bool>> confirmAsync,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (CriticalServices.Contains(serviceName))
            return Task.FromResult(ActionExecutionResult.Fail($"'{serviceName}' is a critical service and cannot be changed from WinOps Toolkit.", "Critical service safeguard"));

        return _safety.ExecuteAsync(
            "service.state",
            async token =>
            {
                await Task.Run(() =>
                {
                    using var service = new ServiceController(serviceName);
                    service.Refresh();
                    switch (action.ToLowerInvariant())
                    {
                        case "start":
                            if (service.Status != ServiceControllerStatus.Running) service.Start();
                            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(45));
                            break;
                        case "stop":
                            if (service.Status != ServiceControllerStatus.Stopped) service.Stop();
                            service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(45));
                            break;
                        case "restart":
                            if (service.Status != ServiceControllerStatus.Stopped) service.Stop();
                            service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(45));
                            service.Start();
                            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(45));
                            break;
                        default:
                            throw new InvalidOperationException($"Unsupported service action '{action}'.");
                    }
                }, token);
                return ActionExecutionResult.Ok($"{action} completed for {serviceName}.", command: $"Set-ServiceState -Name {serviceName} -Action {action}");
            },
            confirmAsync,
            progress,
            ct,
            async token => await Task.Run(() =>
            {
                using var service = new ServiceController(serviceName);
                service.Refresh();
                var expected = action.ToLowerInvariant() switch
                {
                    "start" or "restart" => ServiceControllerStatus.Running,
                    "stop" => ServiceControllerStatus.Stopped,
                    _ => service.Status
                };
                return service.Status == expected;
            }, token));
    }

    public Task<ActionExecutionResult> SetStartupTypeAsync(
        string serviceName,
        ServiceStartupType startupType,
        Func<ActionDefinition, Task<bool>> confirmAsync,
        CancellationToken ct = default) =>
        _safety.ExecuteAsync(
            "service.startup",
            async token =>
            {
                var mode = startupType switch
                {
                    ServiceStartupType.Automatic => "Automatic",
                    ServiceStartupType.AutomaticDelayedStart => "Automatic Delayed Start",
                    ServiceStartupType.Manual => "Manual",
                    ServiceStartupType.Disabled => "Disabled",
                    _ => throw new InvalidOperationException("Unknown startup type.")
                };
                await Task.Run(() =>
                {
                    using var searcher = new ManagementObjectSearcher($"SELECT Name FROM Win32_Service WHERE Name='{Escape(serviceName)}'");
                    var service = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                    if (service is null) throw new InvalidOperationException($"Service '{serviceName}' was not found.");
                    using (service)
                    {
                        var result = service.InvokeMethod("ChangeStartMode", new object?[] { mode });
                        if (Convert.ToUInt32(result ?? 2) != 0) throw new InvalidOperationException("ChangeStartMode was rejected by Windows.");
                    }
                }, token);
                return ActionExecutionResult.Ok($"Startup type for {serviceName} set to {mode}.", command: $"Set-Service -Name {serviceName} -StartupType {mode}");
            },
            confirmAsync,
            ct: ct,
            verification: async token => await Task.Run(() =>
            {
                var row = CimQuery.First($"SELECT StartMode FROM Win32_Service WHERE Name='{Escape(serviceName)}'");
                return CimQuery.Text(row, "StartMode", string.Empty).Equals(startupType.ToString(), StringComparison.OrdinalIgnoreCase)
                    || CimQuery.Text(row, "StartMode", string.Empty).Contains(startupType.ToString().Replace("DelayedStart", "Delayed"), StringComparison.OrdinalIgnoreCase);
            }, token));

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");
}
