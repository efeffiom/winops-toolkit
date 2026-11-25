using System.Runtime.Versioning;
using WinOps.Core.Abstractions;
using WinOps.Core.Models;
using WinOps.Core.Safety;
using WinOps.Windows.Integration;

namespace WinOps.Windows.Maintenance;

[SupportedOSPlatform("windows")]
public sealed class MaintenanceService : IMaintenanceService
{
    private readonly ActionSafetyEngine _safety;
    private readonly CommandRunner _commands;

    private static readonly IReadOnlyList<RepairOperationInfo> Repairs =
    [
        new("sfc", "System File Checker", "Scans protected Windows files and repairs corrupted versions from the component store.", RiskLevel.Level3HighRisk, "sfc /scannow", true),
        new("dism", "DISM component store repair", "Repairs the Windows component store from Windows Update or a local source.", RiskLevel.Level3HighRisk, "DISM /Online /Cleanup-Image /RestoreHealth", true),
        new("chkdsk", "Check Disk (read-only scan)", "Scans the system volume for file system errors without modifying data.", RiskLevel.Level2Reversible, "chkdsk C: /scan", true),
        new("print-spooler", "Print spooler repair", "Clears stuck print jobs and restarts the print spooler service.", RiskLevel.Level2Reversible, "Stop-Service Spooler; Remove-Item C:\\Windows\\System32\\spool\\PRINTERS\\*; Start-Service Spooler", true),
        new("search", "Windows Search repair", "Restarts Windows Search and triggers a rebuild of the search index.", RiskLevel.Level2Reversible, "Restart-Service WSearch", true),
        new("audio", "Audio service repair", "Restarts Windows Audio and Audio Endpoint Builder services.", RiskLevel.Level2Reversible, "Restart-Service Audiosrv; Restart-Service AudioEndpointBuilder", true),
        new("network", "Network stack repair", "Resets Winsock, TCP/IP and flushes DNS. Connectivity may be interrupted.", RiskLevel.Level3HighRisk, "netsh winsock reset; netsh int ip reset; ipconfig /flushdns", true),
        new("wu-troubleshoot", "Windows Update components reset", "Stops update services and clears the update cache so a new scan can be attempted.", RiskLevel.Level3HighRisk, "Stop-Service wuauserv,cryptsvc,bits; Remove-Item C:\\Windows\\SoftwareDistribution\\Download\\*; Start-Service wuauserv,cryptsvc,bits", true)
    ];

    public MaintenanceService(ActionSafetyEngine safety, CommandRunner commands)
    {
        _safety = safety;
        _commands = commands;
    }

    public async Task<IReadOnlyList<WindowsUpdateInfo>> GetUpdateHistoryAsync(CancellationToken ct = default) =>
        await Task.Run(() => CimQuery.Query("SELECT HotFixID,Description,InstalledOn,Caption FROM Win32_QuickFixEngineering")
            .Select(row => new WindowsUpdateInfo(
                CimQuery.Text(row, "HotFixID", string.Empty),
                "Installed",
                CimQuery.Date(row, "InstalledOn")?.ToString("yyyy-MM-dd") ?? "Unknown",
                CimQuery.Text(row, "Caption", string.Empty),
                "Security/Update"))
            .Where(u => !string.IsNullOrWhiteSpace(u.Title))
            .OrderByDescending(u => u.Date)
            .ToList(), ct);

    public Task<IReadOnlyList<WindowsUpdateInfo>> GetPendingUpdatesAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<WindowsUpdateInfo>>(() =>
        {
            var updates = new List<WindowsUpdateInfo>();
            try
            {
                var type = Type.GetTypeFromProgID("Microsoft.Update.Session");
                if (type is null) return updates;
                dynamic session = Activator.CreateInstance(type)!;
                dynamic searcher = session.CreateUpdateSearcher();
                dynamic result = searcher.Search("IsInstalled=0 and Type='Software' and IsHidden=0");
                foreach (dynamic update in result.Updates)
                {
                    var classifications = new List<string>();
                    try { foreach (dynamic c in update.CategoryNames) classifications.Add((string)c); } catch { /* optional */ }
                    updates.Add(new WindowsUpdateInfo(
                        (string)update.Title,
                        "Pending",
                        update.LastDeploymentChangeTime?.ToString() ?? "Unknown",
                        (string)update.Identity.UpdateID,
                        string.Join(", ", classifications)));
                }
            }
            catch { /* Windows Update COM API unavailable */ }
            return updates;
        }, ct);

    public Task<ActionExecutionResult> ScanUpdatesAsync(Func<ActionDefinition, Task<bool>> confirmAsync, IProgress<OperationProgress>? progress = null, CancellationToken ct = default) =>
        _safety.ExecuteAsync(
            "update.scan",
            async token =>
            {
                var result = await Task.Run(() =>
                {
                    var type = Type.GetTypeFromProgID("Microsoft.Update.Session");
                    if (type is null) return ActionExecutionResult.Fail("Windows Update API is unavailable on this computer.", "Microsoft.Update.Session not registered");
                    dynamic session = Activator.CreateInstance(type)!;
                    dynamic searcher = session.CreateUpdateSearcher();
                    dynamic searchResult = searcher.Search("IsInstalled=0 and Type='Software' and IsHidden=0");
                    var count = searchResult.Updates.Count;
                    return ActionExecutionResult.Ok($"Update scan complete. {count} update(s) available.", $"Updates found: {count}", "Microsoft.Update.Session.Search");
                }, token);
                return result;
            },
            confirmAsync,
            progress,
            ct,
            verification: async token => (await GetPendingUpdatesAsync(token)).Count >= 0);

    public Task<ActionExecutionResult> InstallUpdatesAsync(Func<ActionDefinition, Task<bool>> confirmAsync, IProgress<OperationProgress>? progress = null, CancellationToken ct = default) =>
        _safety.ExecuteAsync(
            "update.install",
            async token =>
            {
                progress?.Report(new OperationProgress("Downloading updates", 40, "Contacting Windows Update"));
                var result = await Task.Run(() =>
                {
                    var type = Type.GetTypeFromProgID("Microsoft.Update.Session");
                    if (type is null) return ActionExecutionResult.Fail("Windows Update API is unavailable.", "Microsoft.Update.Session not registered");
                    dynamic session = Activator.CreateInstance(type)!;
                    dynamic searcher = session.CreateUpdateSearcher();
                    dynamic found = searcher.Search("IsInstalled=0 and Type='Software' and IsHidden=0");
                    if (found.Updates.Count == 0) return ActionExecutionResult.Ok("No pending updates were found.", "No updates available.");

                    dynamic toDownload = session.CreateUpdateCollection();
                    foreach (dynamic update in found.Updates) if (!update.IsDownloaded) toDownload.Add(update);
                    if (toDownload.Count > 0)
                    {
                        dynamic downloader = session.CreateUpdateDownloader();
                        downloader.Updates = toDownload;
                        downloader.Download();
                    }
                    dynamic toInstall = session.CreateUpdateCollection();
                    foreach (dynamic update in found.Updates) if (update.IsDownloaded) toInstall.Add(update);
                    dynamic installer = session.CreateUpdateInstaller();
                    installer.Updates = toInstall;
                    dynamic installResult = installer.Install();
                    var code = installResult.ResultCode;
                    return code == 2
                        ? ActionExecutionResult.Ok($"Installed {toInstall.Count} update(s). A restart may be required.", $"Result code: {code}", "Microsoft.Update.Session.Install", verified: false)
                        : ActionExecutionResult.Fail($"Update installation returned code {code}.", $"Result code: {code}", string.Empty, "Microsoft.Update.Session.Install", code);
                }, token);
                return result;
            },
            confirmAsync,
            progress,
            ct);

    public Task<IReadOnlyList<RepairOperationInfo>> GetRepairOperationsAsync() => Task.FromResult(Repairs);

    public Task<ActionExecutionResult> RunRepairAsync(string operationId, Func<ActionDefinition, Task<bool>> confirmAsync, IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
    {
        var repair = Repairs.FirstOrDefault(r => r.Id.Equals(operationId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Unknown repair operation '{operationId}'.");
        return _safety.ExecuteAsync(
            repair.RiskLevel >= RiskLevel.Level3HighRisk ? "repair." + (repair.Id is "sfc" or "dism" or "chkdsk" ? repair.Id : "general") : "repair.general",
            async token =>
            {
                var (executable, args) = BuildRepairCommand(repair);
                var result = await _commands.RunAsync(executable, args, token, progress, TimeSpan.FromMinutes(30));
                var output = result.CombinedOutput;
                var hasErrors = !result.Success && !string.IsNullOrWhiteSpace(result.StandardError);
                return hasErrors
                    ? ActionExecutionResult.Fail($"{repair.Title} reported errors.", result.StandardError, output, $"{executable} {string.Join(' ', args)}")
                    : ActionExecutionResult.Ok($"{repair.Title} completed. Review the output for details.", output, $"{executable} {string.Join(' ', args)}", verified: false);
            },
            confirmAsync,
            progress,
            ct);
    }

    private static (string Executable, string[] Arguments) BuildRepairCommand(RepairOperationInfo repair)
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var scripts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sfc"] = "sfc /scannow",
            ["dism"] = "DISM.exe /Online /Cleanup-Image /RestoreHealth",
            ["chkdsk"] = $"chkdsk {Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\"} /scan",
            ["print-spooler"] = "Stop-Service Spooler -ErrorAction SilentlyContinue; Get-ChildItem 'C:\\Windows\\System32\\spool\\PRINTERS' -Force -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue; Start-Service Spooler",
            ["search"] = "Restart-Service WSearch -ErrorAction SilentlyContinue; Get-Process SearchIndexer -ErrorAction SilentlyContinue | Stop-Process -Force",
            ["audio"] = "Restart-Service AudioEndpointBuilder -ErrorAction SilentlyContinue; Restart-Service Audiosrv -ErrorAction SilentlyContinue",
            ["network"] = "netsh winsock reset; netsh int ip reset; ipconfig /flushdns",
            ["wu-troubleshoot"] = "Stop-Service wuauserv,cryptsvc,bits -ErrorAction SilentlyContinue; Get-ChildItem 'C:\\Windows\\SoftwareDistribution\\Download' -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue; Start-Service wuauserv,cryptsvc,bits -ErrorAction SilentlyContinue"
        };
        var script = scripts.TryGetValue(repair.Id, out var value) ? value : repair.Command;
        return (CommandRunner.ResolvePowerShell(), ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script]);
    }
}
