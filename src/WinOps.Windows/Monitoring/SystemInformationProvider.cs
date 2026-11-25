using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using Microsoft.Win32;
using WinOps.Core.Abstractions;
using WinOps.Core.Models;
using WinOps.Windows.Integration;

namespace WinOps.Windows.Monitoring;

[SupportedOSPlatform("windows")]
public sealed class SystemInformationProvider : ISystemInformationProvider
{
    private readonly IPrivilegeService _privileges;
    private (DateTimeOffset Time, long Sent, long Received)? _previousNetwork;

    public SystemInformationProvider(IPrivilegeService privileges) => _privileges = privileges;

    public async Task<SystemInfoSnapshot> GetSystemInfoAsync(CancellationToken ct = default) =>
        await Task.Run(() => Collect(), ct);

    private SystemInfoSnapshot Collect()
    {
        var os = CimQuery.First("SELECT Caption,Version,BuildNumber,OSArchitecture,TotalVisibleMemorySize,LastBootUpTime,LocalDateTime FROM Win32_OperatingSystem");
        var cs = CimQuery.First("SELECT Name,Domain,Manufacturer,Model,TotalPhysicalMemory,PCSystemType FROM Win32_ComputerSystem");
        var bios = CimQuery.First("SELECT SMBIOSBIOSVersion,Manufacturer,SerialNumber FROM Win32_BIOS");
        var cpu = CimQuery.First("SELECT Name,NumberOfCores,NumberOfLogicalProcessors FROM Win32_Processor");
        var battery = CimQuery.First("SELECT EstimatedChargeRemaining,BatteryStatus FROM Win32_Battery");
        var lastBoot = CimQuery.Date(os, "LastBootUpTime");
        var uptime = lastBoot.HasValue ? DateTimeOffset.Now - lastBoot.Value : TimeSpan.Zero;

        var totalMemory = CimQuery.Number(cs, "TotalPhysicalMemory");
        if (totalMemory <= 0) totalMemory = CimQuery.Number(os, "TotalVisibleMemorySize") * 1024;

        return new SystemInfoSnapshot
        {
            ComputerName = CimQuery.Text(cs, "Name", Environment.MachineName),
            Domain = CimQuery.Text(cs, "Domain", Environment.UserDomainName),
            OsCaption = CimQuery.Text(os, "Caption"),
            OsVersion = CimQuery.Text(os, "Version"),
            BuildNumber = CimQuery.Text(os, "BuildNumber"),
            Architecture = CimQuery.Text(os, "OSArchitecture", Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit"),
            Manufacturer = CimQuery.Text(cs, "Manufacturer", CimQuery.Text(bios, "Manufacturer")),
            Model = CimQuery.Text(cs, "Model"),
            BiosVersion = CimQuery.Text(bios, "SMBIOSBIOSVersion"),
            SerialNumber = CimQuery.Text(bios, "SerialNumber", "Not available"),
            ProcessorName = CimQuery.Text(cpu, "Name"),
            ProcessorCores = (int)(CimQuery.Number(cpu, "NumberOfCores") > 0 ? CimQuery.Number(cpu, "NumberOfCores") : CimQuery.Number(cpu, "NumberOfLogicalProcessors")),
            TotalMemoryBytes = totalMemory,
            Uptime = uptime,
            LastBootTime = lastBoot,
            IsAdministrator = _privileges.IsAdministrator(),
            ActivationStatus = ReadActivation(),
            DefenderStatus = ReadDefenderStatus(),
            FirewallStatus = ReadFirewallStatus(),
            WindowsUpdateStatus = ReadUpdateStatus(),
            BatteryStatus = ReadBattery(battery)
        };
    }

    public async Task<MetricSample> GetCurrentMetricsAsync(CancellationToken ct = default) =>
        await Task.Run(() => CollectMetrics(), ct);

    private MetricSample CollectMetrics()
    {
        var now = DateTimeOffset.Now;
        var cpuRow = CimQuery.First("SELECT PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'", timeoutMs: 5000);
        var osRow = CimQuery.First("SELECT TotalVisibleMemorySize,FreePhysicalMemory FROM Win32_OperatingSystem", timeoutMs: 5000);

        double? cpu = cpuRow is null ? null : CimQuery.Double(cpuRow, "PercentProcessorTime");
        double? memory = null;
        var totalKb = CimQuery.Number(osRow, "TotalVisibleMemorySize");
        if (totalKb > 0)
        {
            var freeKb = CimQuery.Number(osRow, "FreePhysicalMemory");
            memory = Math.Clamp((1.0 - freeKb / (double)totalKb) * 100.0, 0, 100);
        }

        double? disk = null;
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            var capacity = drive.TotalSize;
            if (capacity > 0) disk = Math.Clamp((1.0 - drive.AvailableFreeSpace / (double)capacity) * 100.0, 0, 100);
        }
        catch { /* unavailable */ }

        double? sent = null;
        double? received = null;
        try
        {
            long totalSent = 0, totalReceived = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                var stats = nic.GetIPv4Statistics();
                totalSent += stats.BytesSent;
                totalReceived += stats.BytesReceived;
            }
            if (_previousNetwork is { } previous)
            {
                var seconds = (now - previous.Time).TotalSeconds;
                if (seconds > 0)
                {
                    sent = Math.Max(0, (totalSent - previous.Sent) / seconds);
                    received = Math.Max(0, (totalReceived - previous.Received) / seconds);
                }
            }
            _previousNetwork = (now, totalSent, totalReceived);
        }
        catch { /* unavailable */ }

        return new MetricSample(now, cpu, memory, disk, sent, received);
    }

    private static string ReadActivation()
    {
        var licensed = CimQuery.Query("SELECT Name,LicenseStatus FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL AND LicenseStatus=1");
        return licensed.Count > 0 ? "Licensed" : "No active license detected or information unavailable";
    }

    private static string ReadDefenderStatus()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Defender\Real-Time Protection");
            var disabled = key?.GetValue("DisableRealtimeMonitoring") is int value && value == 1;
            using var service = new System.ServiceProcess.ServiceController("WinDefend");
            var running = service.Status == System.ServiceProcess.ServiceControllerStatus.Running;
            return disabled ? "Real-time protection disabled" : running ? "Enabled / running" : "Service not running";
        }
        catch { return "Unavailable"; }
    }

    private static string ReadFirewallStatus()
    {
        try
        {
            var items = new List<string>();
            foreach (var profile in new[] { "DomainProfile", "StandardProfile", "PublicProfile" })
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{profile}");
                var enabled = key?.GetValue("EnableFirewall") is int value && value == 1;
                items.Add($"{profile.Replace("Profile", string.Empty)}: {(enabled ? "on" : "off")}");
            }
            return string.Join(", ", items);
        }
        catch { return "Unavailable"; }
    }

    private static string ReadUpdateStatus()
    {
        var updates = CimQuery.Query("SELECT HotFixID,InstalledOn FROM Win32_QuickFixEngineering");
        var latest = updates
            .Select(u => new { Id = CimQuery.Text(u, "HotFixID", string.Empty), Date = CimQuery.Date(u, "InstalledOn") })
            .Where(u => !string.IsNullOrEmpty(u.Id))
            .OrderByDescending(u => u.Date)
            .FirstOrDefault();
        return latest is null ? "No update history available" : $"Last installed: {latest.Id} on {latest.Date?.ToString("yyyy-MM-dd") ?? "unknown date"}";
    }

    private static string ReadBattery(Dictionary<string, object?>? battery)
    {
        if (battery is null) return "Not present";
        var charge = CimQuery.Number(battery, "EstimatedChargeRemaining", -1);
        var status = CimQuery.Number(battery, "BatteryStatus", -1);
        return charge >= 0 ? $"{charge}% (status code {status})" : "Present, charge unavailable";
    }
}
