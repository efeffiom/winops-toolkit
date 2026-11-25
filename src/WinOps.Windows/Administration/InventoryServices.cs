using System.Management;
using System.Runtime.Versioning;
using Microsoft.Win32;
using WinOps.Core.Abstractions;
using WinOps.Core.Models;
using WinOps.Core.Safety;
using WinOps.Windows.Integration;

namespace WinOps.Windows.Administration;

[SupportedOSPlatform("windows")]
public sealed class InventoryService : IInventoryService
{
    private readonly ActionSafetyEngine _safety;
    private readonly CommandRunner _commands;

    public InventoryService(ActionSafetyEngine safety, CommandRunner commands)
    {
        _safety = safety;
        _commands = commands;
    }

    public async Task<IReadOnlyList<StartupEntry>> GetStartupEntriesAsync(CancellationToken ct = default) =>
        await Task.Run(CollectStartup, ct);

    private static IReadOnlyList<StartupEntry> CollectStartup()
    {
        var entries = new List<StartupEntry>();
        foreach (var (hive, subKey, user) in new[]
        {
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "All users"),
            (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Current user"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", "All users (once)"),
            (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", "Current user (once)")
        })
        {
            try
            {
                using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Default).OpenSubKey(subKey);
                if (key is null) continue;
                foreach (var name in key.GetValueNames())
                {
                    var value = key.GetValue(name)?.ToString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    entries.Add(new StartupEntry(name, value, $"{hive}\\{subKey}", user, !name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)));
                }
            }
            catch { /* registry key may be restricted */ }
        }

        foreach (var (folder, user) in new[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Current user"),
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "All users")
        })
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder))
                    entries.Add(new StartupEntry(Path.GetFileNameWithoutExtension(file), file, folder, user, true));
            }
            catch { /* startup folder may not exist */ }
        }
        return entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<ScheduledTaskInfo>> GetScheduledTasksAsync(CancellationToken ct = default) =>
        await Task.Run(CollectTasks, ct);

    private async Task<IReadOnlyList<ScheduledTaskInfo>> CollectTasks()
    {
        var result = new List<ScheduledTaskInfo>();
        var command = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");
        var output = await _commands.RunAsync(command, ["/query", "/fo", "CSV", "/v"], timeout: TimeSpan.FromSeconds(45));
        if (!output.Success && string.IsNullOrWhiteSpace(output.StandardOutput)) return result;
        var lines = output.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return result;
        var headers = ParseCsvLine(lines[0]).Select(h => h.Trim()).ToArray();
        foreach (var line in lines.Skip(1))
        {
            var cells = ParseCsvLine(line);
            string Get(string header)
            {
                var index = Array.FindIndex(headers, h => h.Equals(header, StringComparison.OrdinalIgnoreCase));
                return index >= 0 && index < cells.Count ? cells[index] : string.Empty;
            }
            result.Add(new ScheduledTaskInfo(
                Get("TaskName"),
                Get("TaskName"),
                Get("Scheduled Task State"),
                Get("Schedule Type"),
                Get("Task To Run"),
                Get("Last Run Time"),
                Get("Last Result")));
        }
        return result;
    }

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted) { cells.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        cells.Add(current.ToString());
        return cells;
    }

    public Task<ActionExecutionResult> SetTaskStateAsync(string taskPath, bool enable, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default) =>
        _safety.ExecuteAsync(
            "task.state",
            async token =>
            {
                var command = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");
                var result = await _commands.RunAsync(command, ["/Change", "/TN", taskPath, enable ? "/ENABLE" : "/DISABLE"], token, timeout: TimeSpan.FromSeconds(60));
                return result.Success
                    ? ActionExecutionResult.Ok($"Task {(enable ? "enabled" : "disabled")}: {taskPath}", result.CombinedOutput, string.Join(' ', command, "/Change", "/TN", taskPath))
                    : ActionExecutionResult.Fail($"Could not change task {taskPath}.", result.StandardError, result.StandardOutput);
            },
            confirmAsync,
            ct: ct,
            verification: async token =>
            {
                var tasks = await GetScheduledTasksAsync(token);
                var task = tasks.FirstOrDefault(t => t.Path.Equals(taskPath, StringComparison.OrdinalIgnoreCase));
                return task is not null && task.State.Equals(enable ? "Enabled" : "Disabled", StringComparison.OrdinalIgnoreCase);
            });

    public Task<ActionExecutionResult> RunTaskAsync(string taskPath, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default) =>
        _safety.ExecuteAsync(
            "task.run",
            async token =>
            {
                var command = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");
                var result = await _commands.RunAsync(command, ["/Run", "/TN", taskPath], token, timeout: TimeSpan.FromSeconds(60));
                return result.Success
                    ? ActionExecutionResult.Ok($"Started task: {taskPath}", result.CombinedOutput)
                    : ActionExecutionResult.Fail($"Could not start task {taskPath}.", result.StandardError);
            },
            confirmAsync,
            ct: ct);

    public async Task<IReadOnlyList<InstalledSoftwareInfo>> GetInstalledSoftwareAsync(CancellationToken ct = default) =>
        await Task.Run(CollectSoftware, ct);

    private static IReadOnlyList<InstalledSoftwareInfo> CollectSoftware()
    {
        var result = new List<InstalledSoftwareInfo>();
        var locations = new[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM 64-bit"),
            (RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM 32-bit"),
            (RegistryHive.CurrentUser, RegistryView.Default, "HKCU")
        };
        const string uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        foreach (var (hive, view, source) in locations)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(uninstall);
                if (key is null) continue;
                foreach (var subName in key.GetSubKeyNames())
                {
                    using var item = key.OpenSubKey(subName);
                    if (item is null) continue;
                    var name = item.GetValue("DisplayName")?.ToString();
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    result.Add(new InstalledSoftwareInfo(
                        name.Trim(),
                        item.GetValue("DisplayVersion")?.ToString() ?? string.Empty,
                        item.GetValue("Publisher")?.ToString() ?? string.Empty,
                        item.GetValue("InstallDate")?.ToString() ?? string.Empty,
                        item.GetValue("InstallLocation")?.ToString() ?? string.Empty,
                        source));
                }
            }
            catch { /* registry view unavailable */ }
        }
        return result.DistinctBy(s => (s.Name, s.Version)).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync(CancellationToken ct = default) =>
        await Task.Run(() => CimQuery.Query("SELECT Name,PNPClass,Status,Manufacturer,DeviceID FROM Win32_PnPEntity")
            .Select(row => new DeviceInfo(
                CimQuery.Text(row, "Name", string.Empty),
                CimQuery.Text(row, "PNPClass", "Unknown"),
                CimQuery.Text(row, "Status", "Unknown"),
                CimQuery.Text(row, "Manufacturer", string.Empty),
                CimQuery.Text(row, "DeviceID", string.Empty)))
            .Where(d => !string.IsNullOrWhiteSpace(d.Name))
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList(), ct);

    public async Task<IReadOnlyList<DriverInfo>> GetDriversAsync(CancellationToken ct = default) =>
        await Task.Run(() => CimQuery.Query("SELECT Name,Provider,Version,Date,DeviceClass FROM Win32_PnPSignedDriver")
            .Select(row => new DriverInfo(
                CimQuery.Text(row, "Name", string.Empty),
                CimQuery.Text(row, "Provider", string.Empty),
                CimQuery.Text(row, "Version", string.Empty),
                CimQuery.Text(row, "Date", string.Empty),
                CimQuery.Text(row, "DeviceClass", string.Empty)))
            .Where(d => !string.IsNullOrWhiteSpace(d.Name))
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList(), ct);

    public async Task<IReadOnlyList<EnvironmentVariableEntry>> GetEnvironmentVariablesAsync(string scope, CancellationToken ct = default) =>
        await Task.Run(() =>
        {
            var target = scope.ToLowerInvariant() switch
            {
                "machine" or "system" => EnvironmentVariableTarget.Machine,
                "user" => EnvironmentVariableTarget.User,
                _ => EnvironmentVariableTarget.Process
            };
            return Environment.GetEnvironmentVariables(target)
                .Cast<System.Collections.DictionaryEntry>()
                .Select(e => new EnvironmentVariableEntry(e.Key.ToString() ?? string.Empty, e.Value?.ToString() ?? string.Empty, target.ToString()))
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, ct);

    public async Task<IReadOnlyList<RestorePointInfo>> GetRestorePointsAsync(CancellationToken ct = default) =>
        await Task.Run(() => CimQuery.Query("SELECT Description,CreationDate,RestorePointType,SequenceNumber FROM SystemRestore", @"root\default")
            .Select(row => new RestorePointInfo(
                CimQuery.Text(row, "Description", "Restore point"),
                CimQuery.Date(row, "CreationDate")?.ToString("u") ?? "Unknown",
                CimQuery.Number(row, "RestorePointType") switch { 0 => "Application install", 1 => "Automatic update", 10 => "Device driver", 11 => "Application uninstall", 12 => "Modification", _ => "Other" },
                CimQuery.Text(row, "SequenceNumber", string.Empty)))
            .OrderByDescending(r => r.CreationTime)
            .ToList(), ct);

    public Task<ActionExecutionResult> CreateRestorePointAsync(string description, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default) =>
        _safety.ExecuteAsync(
            "restorepoint.create",
            async token =>
            {
                var script = $"Checkpoint-Computer -Description '{EscapePowerShell(description)}' -RestorePointType 'MODIFY_SETTINGS'";
                var result = await _commands.RunPowerShellAsync(script, token, timeout: TimeSpan.FromMinutes(5));
                return result.Success
                    ? ActionExecutionResult.Ok($"Restore point '{description}' created.", result.CombinedOutput, script)
                    : ActionExecutionResult.Fail("Restore point creation failed.", result.StandardError, result.CombinedOutput, script);
            },
            confirmAsync,
            ct: ct,
            verification: async token => (await GetRestorePointsAsync(token)).Count > 0);

    public Task<ActionExecutionResult> SetPowerStateAsync(string action, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default) =>
        _safety.ExecuteAsync(
            "power.state",
            async token =>
            {
                var args = action.ToLowerInvariant() switch
                {
                    "shutdown" => new[] { "/s", "/t", "15", "/c", "WinOps Toolkit: shutdown requested" },
                    "restart" => new[] { "/r", "/t", "15", "/c", "WinOps Toolkit: restart requested" },
                    "logoff" => new[] { "/l" },
                    _ => throw new InvalidOperationException($"Unsupported power action '{action}'.")
                };
                var result = await _commands.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe"), args, token, timeout: TimeSpan.FromSeconds(30));
                return ActionExecutionResult.Ok($"Power action '{action}' requested.", result.CombinedOutput, "shutdown.exe " + string.Join(' ', args));
            },
            confirmAsync,
            ct: ct);

    private static string EscapePowerShell(string value) => value.Replace("'", "''");
}
