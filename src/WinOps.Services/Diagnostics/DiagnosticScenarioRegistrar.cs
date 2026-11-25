using WinOps.Core.Abstractions;
using WinOps.Core.Diagnostics;
using WinOps.Core.Models;
using WinOps.Windows.Administration;

namespace WinOps.Services.Diagnostics;

public sealed record DelegateDiagnosticStep(
    string Id,
    Func<CancellationToken, Task<IReadOnlyList<DiagnosticFinding>>> Action) : IDiagnosticStep
{
    public Task<IReadOnlyList<DiagnosticFinding>> RunAsync(CancellationToken ct = default) => Action(ct);
}

/// <summary>Registers real, evidence-based troubleshooting scenarios.</summary>
public sealed class DiagnosticScenarioRegistrar
{
    private readonly ISystemInformationProvider _system;
    private readonly INetworkService _network;
    private readonly IProcessManager _processes;
    private readonly IServiceManager _services;
    private readonly IMaintenanceService _maintenance;
    private readonly IStorageService _storage;
    private readonly IEventLogService _events;
    private readonly IInventoryService _inventory;
    private readonly IActiveDirectoryService _activeDirectory;
    private readonly ISecurityService _security;
    private readonly ILocalUserService _users;

    public DiagnosticScenarioRegistrar(
        ISystemInformationProvider system,
        INetworkService network,
        IProcessManager processes,
        IServiceManager services,
        IMaintenanceService maintenance,
        IStorageService storage,
        IEventLogService events,
        IInventoryService inventory,
        IActiveDirectoryService activeDirectory,
        ISecurityService security,
        ILocalUserService users)
    {
        _system = system;
        _network = network;
        _processes = processes;
        _services = services;
        _maintenance = maintenance;
        _storage = storage;
        _events = events;
        _inventory = inventory;
        _activeDirectory = activeDirectory;
        _security = security;
        _users = users;
    }

    public void Register(DiagnosticEngine engine)
    {
        engine.Register(Scenario("internet", "Internet connectivity", "Network",
            Step("net.dns", async ct =>
            {
                var dns = await _network.ResolveDnsAsync("www.microsoft.com", ct);
                var addresses = dns.FirstOrDefault(d => d.Type == "A/AAAA")?.Records ?? [];
                return [Finding("net.dns", "DNS resolution", addresses.Count > 0 ? Severity.Info : Severity.High,
                    addresses.Count > 0 ? CheckStatus.Passed : CheckStatus.Failed,
                    addresses.Count > 0 ? $"Resolved {addresses.Count} address(es)." : "Name could not be resolved.",
                    string.Join(", ", addresses), "Check DNS server configuration and network connectivity.", "network.flushdns")];
            }),
            Step("net.ping", async ct =>
            {
                var ping = await _network.PingAsync("1.1.1.1", 4, ct);
                return [Finding("net.ping", "ICMP connectivity", ping.Success ? Severity.Info : Severity.High,
                    ping.Success ? CheckStatus.Passed : CheckStatus.Failed,
                    ping.Success ? $"Average latency {ping.AverageMs:0.##} ms, {ping.PacketsReceived}/{ping.PacketsSent} replies." : "No ICMP replies.",
                    ping.Error, "Check gateway, firewall and ISP connectivity.", string.Empty)];
            }),
            Step("net.https", async ct =>
            {
                var port = await _network.TestPortAsync("www.microsoft.com", 443, 4000, ct);
                return [Finding("net.https", "HTTPS connectivity", port.Open ? Severity.Info : Severity.High,
                    port.Open ? CheckStatus.Passed : CheckStatus.Failed,
                    port.Open ? $"TCP 443 reachable in {port.DurationMs} ms." : "TCP 443 unreachable.",
                    port.Error, "Check proxy, firewall and network settings.", string.Empty)];
            }),
            Step("net.publicip", async ct =>
            {
                var ip = await _network.GetPublicIpAsync(ct);
                return [Finding("net.publicip", "Public address", Severity.Info, CheckStatus.Passed,
                    $"Public IP: {ip}", ip, "No action required.", string.Empty)];
            })));

        engine.Register(Scenario("dns", "DNS failure", "Network",
            Step("dns.adapters", async ct =>
            {
                var adapters = await _network.GetAdaptersAsync(ct);
                var active = adapters.Where(a => a.Status == "Up" && a.DnsServers.Count > 0).ToList();
                return [Finding("dns.adapters", "Configured DNS servers", active.Count > 0 ? Severity.Info : Severity.High,
                    active.Count > 0 ? CheckStatus.Passed : CheckStatus.Failed,
                    active.Count == 0 ? "No active adapter has DNS servers configured." : $"{active.Count} active adapter(s) have DNS servers.",
                    string.Join("; ", active.Select(a => $"{a.Name}: {string.Join(", ", a.DnsServers)}")),
                    "Configure a reliable DNS server or restore DHCP.", "network.setdns")];
            }),
            Step("dns.resolve", async ct =>
            {
                var results = await _network.ResolveDnsAsync("www.microsoft.com", ct);
                var a = results.FirstOrDefault(r => r.Type == "A/AAAA");
                return [Finding("dns.resolve", "Forward lookup", a?.Records.Count > 0 ? Severity.Info : Severity.High,
                    a?.Records.Count > 0 ? CheckStatus.Passed : CheckStatus.Failed,
                    a?.Records.Count > 0 ? $"{a.Records.Count} record(s) returned." : "Lookup failed.",
                    a is null ? "No result" : string.Join(", ", a.Records.Concat(string.IsNullOrWhiteSpace(a.Error) ? [] : [a.Error])),
                    "Flush DNS cache and verify DNS servers.", "network.flushdns")];
            })));

        engine.Register(Scenario("slow-pc", "Slow computer", "Performance",
            Step("perf.metrics", async ct =>
            {
                var metrics = await _system.GetCurrentMetricsAsync(ct);
                var findings = new List<DiagnosticFinding>();
                findings.Add(Finding("perf.cpu", "CPU utilization", metrics.CpuUsagePercent > 85 ? Severity.High : Severity.Info,
                    metrics.CpuUsagePercent > 85 ? CheckStatus.Failed : CheckStatus.Passed,
                    metrics.CpuUsagePercent.HasValue ? $"CPU: {metrics.CpuUsagePercent:0}%" : "CPU utilization unavailable.",
                    metrics.Timestamp.ToString("u"), "Investigate high-CPU processes.", "process.stop"));
                findings.Add(Finding("perf.memory", "Memory utilization", metrics.MemoryUsagePercent > 85 ? Severity.High : Severity.Info,
                    metrics.MemoryUsagePercent > 85 ? CheckStatus.Failed : CheckStatus.Passed,
                    metrics.MemoryUsagePercent.HasValue ? $"Memory: {metrics.MemoryUsagePercent:0}%" : "Memory utilization unavailable.",
                    metrics.Timestamp.ToString("u"), "Close unused applications or add memory.", string.Empty));
                return findings;
            }),
            Step("perf.processes", async ct =>
            {
                var processes = await _processes.GetProcessesAsync(ct);
                var top = processes.OrderByDescending(p => p.CpuSeconds).Take(10).ToList();
                return [Finding("perf.top", "Top processes by CPU time", top.Count > 0 ? Severity.Info : Severity.Low, CheckStatus.Passed,
                    "Collected process CPU and memory data.", string.Join(Environment.NewLine, top.Select(p => $"{p.Name} (PID {p.Id}): {p.CpuSeconds:0.##}s CPU, {p.WorkingSetBytes / 1024 / 1024:N0} MB")),
                    "Stop only processes you have identified as unnecessary.", "process.stop")];
            }),
            Step("perf.startup", async ct =>
            {
                var startup = await _inventory.GetStartupEntriesAsync(ct);
                return [Finding("perf.startup", "Startup applications", startup.Count > 20 ? Severity.Medium : Severity.Info,
                    startup.Count > 20 ? CheckStatus.Warning : CheckStatus.Passed,
                    $"{startup.Count} startup entry/entries found.", string.Join(Environment.NewLine, startup.Take(20).Select(s => s.Name)),
                    "Disable unnecessary startup entries through Task Manager or Settings.", string.Empty)];
            }),
            Step("perf.updates", async ct =>
            {
                var updates = await _maintenance.GetPendingUpdatesAsync(ct);
                return [Finding("perf.updates", "Pending updates", updates.Count > 0 ? Severity.Low : Severity.Info,
                    updates.Count > 0 ? CheckStatus.Warning : CheckStatus.Passed,
                    $"{updates.Count} update(s) pending.", string.Join(Environment.NewLine, updates.Take(10).Select(u => u.Title)),
                    "Install pending updates when convenient.", "update.install")];
            })));

        engine.Register(Scenario("high-cpu", "High CPU", "Performance",
            Step("cpu.processes", async ct =>
            {
                var processes = await _processes.GetProcessesAsync(ct);
                var top = processes.OrderByDescending(p => p.CpuSeconds).Take(15).ToList();
                var suspicious = top.Where(p => !p.IsCritical).Take(5).ToList();
                return [Finding("cpu.top", "CPU consumers", suspicious.Count > 0 ? Severity.Medium : Severity.Info,
                    suspicious.Count > 0 ? CheckStatus.Warning : CheckStatus.Passed,
                    $"{top.Count} processes sampled by cumulative CPU time.",
                    string.Join(Environment.NewLine, top.Select(p => $"{p.Name} (PID {p.Id}), {p.CpuSeconds:0.##}s")),
                    "Confirm the process is not required before stopping it.", "process.stop")];
            })));

        engine.Register(Scenario("low-disk", "Low disk space", "Storage",
            Step("disk.volumes", async ct =>
            {
                var volumes = await _storage.GetVolumesAsync(ct);
                var low = volumes.Where(v => v.CapacityBytes > 0 && v.FreeBytes / (double)v.CapacityBytes < 0.10).ToList();
                return [Finding("disk.volumes", "Volume free space", low.Count > 0 ? Severity.High : Severity.Info,
                    low.Count > 0 ? CheckStatus.Failed : CheckStatus.Passed,
                    low.Count > 0 ? $"{low.Count} volume(s) below 10% free." : "All volumes have at least 10% free space.",
                    string.Join(Environment.NewLine, volumes.Select(v => $"{v.Drive} {v.Label}: {v.FreeBytes / 1024 / 1024 / 1024:0.##} GB free of {v.CapacityBytes / 1024 / 1024 / 1024:0.##} GB")),
                    "Review temporary files and large files before deleting anything.", "storage.cleanup")];
            }),
            Step("disk.largefiles", async ct =>
            {
                var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                var files = await _storage.FindLargeFilesAsync(root, 500 * 1024 * 1024, 25, null, ct);
                return [Finding("disk.large", "Large files", files.Count > 0 ? Severity.Low : Severity.Info,
                    files.Count > 0 ? CheckStatus.Warning : CheckStatus.Passed,
                    $"{files.Count} file(s) larger than 500 MB on {root}.",
                    string.Join(Environment.NewLine, files.Select(f => $"{f.Path} ({f.SizeBytes / 1024 / 1024:N0} MB)")),
                    "Verify ownership before removing large files.", "storage.cleanup")];
            })));

        engine.Register(Scenario("updates", "Windows Update failures", "Maintenance",
            Step("update.services", async ct =>
            {
                var services = await _services.GetServicesAsync(ct);
                var names = new[] { "wuauserv", "cryptsvc", "bits", "trustedinstaller" };
                var found = services.Where(s => names.Contains(s.Name, StringComparer.OrdinalIgnoreCase)).ToList();
                var stopped = found.Where(s => s.Status != "Running").ToList();
                return [Finding("update.services", "Update-related services", stopped.Count > 0 ? Severity.High : Severity.Info,
                    stopped.Count > 0 ? CheckStatus.Failed : CheckStatus.Passed,
                    stopped.Count == 0 ? "All update services are running." : $"{stopped.Count} update service(s) are not running.",
                    string.Join(Environment.NewLine, found.Select(s => $"{s.Name}: {s.Status}")),
                    "Start stopped services or use the Windows Update repair operation.", "service.state")];
            }),
            Step("update.history", async ct =>
            {
                var history = await _maintenance.GetUpdateHistoryAsync(ct);
                var events = await _events.QueryAsync("System", "error", null, null, DateTimeOffset.Now.AddDays(-14), "Windows Update", 50, ct);
                return [Finding("update.events", "Recent update errors", events.Count > 0 ? Severity.High : Severity.Info,
                    events.Count > 0 ? CheckStatus.Failed : CheckStatus.Passed,
                    $"{events.Count} Windows Update error event(s) in the last 14 days.",
                    string.Join(Environment.NewLine, events.Take(10).Select(e => $"{e.TimeCreated:u} {e.EventId} {e.Message}")),
                    "Review the event details and run Windows Update repair.", "repair.general")];
            })));

        engine.Register(Scenario("printer", "Printer problems", "Hardware",
            Step("print.spooler", async ct =>
            {
                var services = await _services.GetServicesAsync(ct);
                var spooler = services.FirstOrDefault(s => s.Name.Equals("Spooler", StringComparison.OrdinalIgnoreCase));
                return [Finding("print.spooler", "Print spooler", spooler?.Status == "Running" ? Severity.Info : Severity.High,
                    spooler?.Status == "Running" ? CheckStatus.Passed : CheckStatus.Failed,
                    $"Spooler status: {spooler?.Status ?? "Unavailable"}", spooler?.ExecutablePath ?? string.Empty,
                    "Start or restart the print spooler.", "service.state")];
            }),
            Step("print.devices", async ct =>
            {
                var devices = await _inventory.GetDevicesAsync(ct);
                var printers = devices.Where(d => d.Class.Equals("Printer", StringComparison.OrdinalIgnoreCase)).ToList();
                return [Finding("print.devices", "Printer devices", printers.Count > 0 ? Severity.Info : Severity.Medium,
                    printers.Count > 0 ? CheckStatus.Passed : CheckStatus.Warning,
                    $"{printers.Count} printer device(s) detected.", string.Join(Environment.NewLine, printers.Select(p => $"{p.Name} ({p.Status})")),
                    "Install or reinstall the printer driver if no printer is listed.", string.Empty)];
            })));

        engine.Register(Scenario("audio", "Audio problems", "Hardware",
            Step("audio.services", async ct =>
            {
                var services = await _services.GetServicesAsync(ct);
                var audio = services.Where(s => s.Name.Equals("Audiosrv", StringComparison.OrdinalIgnoreCase) || s.Name.Equals("AudioEndpointBuilder", StringComparison.OrdinalIgnoreCase)).ToList();
                var stopped = audio.Where(s => s.Status != "Running").ToList();
                return [Finding("audio.services", "Audio services", stopped.Count > 0 ? Severity.High : Severity.Info,
                    stopped.Count > 0 ? CheckStatus.Failed : CheckStatus.Passed,
                    $"{audio.Count} audio service(s), {stopped.Count} not running.",
                    string.Join(Environment.NewLine, audio.Select(s => $"{s.Name}: {s.Status}")),
                    "Restart audio services using the repair operation.", "repair.general")];
            })));

        engine.Register(Scenario("crashes", "Application crashes", "Applications",
            Step("crash.events", async ct =>
            {
                var events = await _events.QueryAsync("Application", "error", null, "Application Error", DateTimeOffset.Now.AddDays(-7), null, 100, ct);
                return [Finding("crash.events", "Application Error events", events.Count > 0 ? Severity.Medium : Severity.Info,
                    events.Count > 0 ? CheckStatus.Warning : CheckStatus.Passed,
                    $"{events.Count} Application Error event(s) in the last 7 days.",
                    string.Join(Environment.NewLine, events.Take(10).Select(e => $"{e.TimeCreated:u} {e.EventId}: {e.Message}")),
                    "Check application updates and review the event details.", string.Empty)];
            })));

        engine.Register(Scenario("bluescreen", "Blue screens", "Stability",
            Step("bsod.events", async ct =>
            {
                var events = new List<EventLogEntryInfo>();
                foreach (var id in new long[] { 1001, 41, 6008 })
                    events.AddRange(await _events.QueryAsync("System", null, id, null, DateTimeOffset.Now.AddDays(-30), null, 30, ct));
                return [Finding("bsod.events", "Bugcheck and unexpected shutdown events", events.Count > 0 ? Severity.High : Severity.Info,
                    events.Count > 0 ? CheckStatus.Warning : CheckStatus.Passed,
                    $"{events.Count} relevant event(s) in the last 30 days.",
                    string.Join(Environment.NewLine, events.Take(10).Select(e => $"{e.TimeCreated:u} {e.EventId}: {e.Message}")),
                    "Analyze minidump files and update drivers.", string.Empty)];
            }),
            Step("bsod.dumps", async ct =>
            {
                var dumpDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Minidump");
                var count = Directory.Exists(dumpDir) ? Directory.GetFiles(dumpDir, "*.dmp").Length : 0;
                return [Finding("bsod.dumps", "Minidump files", count > 0 ? Severity.Medium : Severity.Info,
                    count > 0 ? CheckStatus.Warning : CheckStatus.Passed,
                    $"{count} minidump file(s) found.", dumpDir, "Use WinDbg to analyze the most recent dump.", string.Empty)];
            })));

        engine.Register(Scenario("login", "Login problems", "Accounts",
            Step("login.failed", async ct =>
            {
                var events = await _events.QueryAsync("Security", "error", 4625, null, DateTimeOffset.Now.AddDays(-7), null, 100, ct);
                return [Finding("login.failed", "Failed logons", events.Count > 0 ? Severity.Medium : Severity.Info,
                    events.Count > 0 ? CheckStatus.Warning : CheckStatus.Passed,
                    $"{events.Count} failed logon event(s) in the last 7 days.",
                    string.Join(Environment.NewLine, events.Take(10).Select(e => $"{e.TimeCreated:u} {e.Message}")),
                    "Check for account lockouts or brute-force attempts.", "user.enable")];
            }),
            Step("login.locked", async ct =>
            {
                var users = await _users.GetUsersAsync(ct);
                var locked = users.Where(u => u.AccountLocked).ToList();
                return [Finding("login.locked", "Locked accounts", locked.Count > 0 ? Severity.Medium : Severity.Info,
                    locked.Count > 0 ? CheckStatus.Warning : CheckStatus.Passed,
                    $"{locked.Count} locked local account(s).", string.Join(", ", locked.Select(u => u.Name)),
                    "Unlock verified accounts through Local User Management.", "user.enable")];
            })));

        engine.Register(Scenario("domain", "Domain connectivity", "Active Directory",
            Step("domain.checks", async ct => await _activeDirectory.RunDomainDiagnosticsAsync(ct))));

        engine.Register(Scenario("services", "Service failures", "System",
            Step("services.stopped", async ct =>
            {
                var services = await _services.GetServicesAsync(ct);
                var stopped = services.Where(s => s.StartupType == ServiceStartupType.Automatic && s.Status != "Running").ToList();
                return [Finding("services.stopped", "Automatic services not running", stopped.Count > 0 ? Severity.Medium : Severity.Info,
                    stopped.Count > 0 ? CheckStatus.Warning : CheckStatus.Passed,
                    $"{stopped.Count} automatic service(s) are not running.",
                    string.Join(Environment.NewLine, stopped.Take(30).Select(s => $"{s.Name}: {s.Status}")),
                    "Start required services after confirming dependencies.", "service.state")];
            })));

        engine.Register(Scenario("security", "Security assessment", "Security",
            Step("security.assessment", async ct =>
            {
                var assessment = await _security.AssessAsync("Corporate Workstation", ct);
                return assessment.Checks.Select(c => Finding("security." + c.Id, c.Title, c.Severity,
                    c.Status == CheckStatus.Passed ? CheckStatus.Passed : c.Status == CheckStatus.NotApplicable ? CheckStatus.NotApplicable : CheckStatus.Failed,
                    $"Current: {c.CurrentValue}. Expected: {c.ExpectedValue}.", c.Explanation,
                    c.Recommendation, c.RemediationActionId)).ToList();
            })));
    }

    private static DiagnosticScenario Scenario(string id, string title, string category, params IDiagnosticStep[] steps) =>
        new(id, title, category, $"Evidence-based checks for {title.ToLowerInvariant()}.", steps);

    private static DelegateDiagnosticStep Step(string id, Func<CancellationToken, Task<IReadOnlyList<DiagnosticFinding>>> action) => new(id, action);

    private static DiagnosticFinding Finding(string id, string title, Severity severity, CheckStatus status, string detail, string evidence, string recommendation, string remediation) =>
        new(id, title, severity, status, detail, evidence, recommendation, remediation, false, string.Empty);
}
