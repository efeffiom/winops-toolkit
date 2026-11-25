using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using WinOps.Core.Abstractions;
using WinOps.Core.Models;
using WinOps.Core.Safety;
using WinOps.Windows.Integration;

namespace WinOps.PowerShell.Execution;

[SupportedOSPlatform("windows")]
public sealed class PowerShellExecutor : IPowerShellService
{
    private readonly ActionSafetyEngine _safety;
    private readonly CommandRunner _commands;
    private readonly string _dataDirectory;
    private readonly string _scriptsPath;
    private readonly string _historyPath;

    private static readonly IReadOnlyList<ScriptTemplateInfo> BuiltInTemplates =
    [
        new("net-adapters", "Network adapter summary", "Networking", "Lists adapters with IP addresses, DNS servers and operational state.",
            "Get-NetAdapter | Select-Object Name,Status,LinkSpeed,MacAddress | Format-Table -AutoSize\nGet-DnsClientServerAddress -AddressFamily IPv4 | Select-Object InterfaceAlias,ServerAddresses | Format-Table -AutoSize", "Standard user"),
        new("net-connections", "Active TCP connections", "Networking", "Shows established TCP connections with owning process IDs.",
            "Get-NetTCPConnection -State Established | Select-Object LocalAddress,LocalPort,RemoteAddress,RemotePort,OwningProcess | Sort-Object RemotePort | Format-Table -AutoSize", "Standard user"),
        new("dns-diagnostics", "DNS diagnostics", "Troubleshooting", "Flushes resolver cache and resolves a named host.",
            "Clear-DnsClientCache\nResolve-DnsName -Name $HostName -ErrorAction Continue | Format-Table -AutoSize", "Standard user (HostName parameter)"),
        new("top-cpu", "Top CPU processes", "System administration", "Lists the ten processes consuming the most CPU time.",
            "Get-Process | Sort-Object CPU -Descending | Select-Object -First 10 Id,ProcessName,CPU,WorkingSet64 | Format-Table -AutoSize", "Standard user"),
        new("services-stopped", "Stopped automatic services", "System administration", "Finds automatic services that are not running.",
            "Get-Service | Where-Object { $_.StartType -eq 'Automatic' -and $_.Status -ne 'Running' } | Select-Object Name,DisplayName,Status | Format-Table -AutoSize", "Standard user"),
        new("disk-space", "Disk space report", "Storage", "Reports free space for all fixed drives.",
            "Get-PSDrive -PSProvider FileSystem | Where-Object Used -ne $null | Select-Object Name,@{n='FreeGB';e={[math]::Round($_.Free/1GB,2)}},@{n='UsedGB';e={[math]::Round($_.Used/1GB,2)}} | Format-Table -AutoSize", "Standard user"),
        new("defender-status", "Defender health", "Security", "Reports Microsoft Defender protection status.",
            "Get-MpComputerStatus | Select-Object AntivirusEnabled,RealTimeProtectionEnabled,AntispywareEnabled,QuickScanAge | Format-List", "Standard user"),
        new("firewall-profiles", "Firewall profiles", "Security", "Shows the state of all firewall profiles.",
            "Get-NetFirewallProfile | Select-Object Name,Enabled,DefaultInboundAction,DefaultOutboundAction | Format-Table -AutoSize", "Administrator for full detail"),
        new("ad-locked-users", "Locked domain accounts", "Active Directory", "Finds locked accounts in the current domain.",
            "Search-ADAccount -LockedOut | Select-Object SamAccountName,DistinguishedName,LastLogonDate | Format-Table -AutoSize", "Domain user with RSAT"),
        new("update-history", "Recent updates", "Windows repair", "Lists the twenty most recent installed updates.",
            "Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 20 HotFixID,InstalledOn,Description | Format-Table -AutoSize", "Standard user"),
        new("event-errors", "Recent system errors", "Troubleshooting", "Lists the last 25 error and critical events in the System log.",
            "Get-WinEvent -FilterHashtable @{LogName='System';Level=1,2} -MaxEvents 25 | Select-Object TimeCreated,Id,ProviderName,LevelDisplayName,Message | Format-Table -Wrap -AutoSize", "Standard user"),
        new("bcd-boot", "Boot configuration", "Servers", "Displays the current boot configuration.",
            "bcdedit /enum {current}", "Administrator"),
        new("m365-license", "Microsoft 365 license check", "Microsoft 365", "Checks whether a Microsoft 365 subscription is attached to Windows.",
            "$o = Get-CimInstance -ClassName SoftwareLicensingProduct -Filter \"PartialProductKey IS NOT NULL\"; $o | Select-Object Name,LicenseStatus,GracePeriodRemaining | Format-Table -AutoSize", "Standard user"),
        new("scheduled-tasks", "Scheduled task inventory", "Automation", "Lists scheduled tasks and their current state.",
            "Get-ScheduledTask | Select-Object TaskName,TaskPath,State,@{n='Actions';e={($_.Actions.Execute -join ', ')}} | Sort-Object TaskName | Format-Table -AutoSize", "Standard user"),
        new("process-tree", "Process tree", "Troubleshooting", "Shows processes with their parent process IDs.",
            "Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId,Name,CommandLine | Sort-Object ParentProcessId,Name | Format-Table -Wrap -AutoSize", "Standard user")
    ];

    public PowerShellExecutor(ActionSafetyEngine safety, CommandRunner commands)
    {
        _safety = safety;
        _commands = commands;
        _dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinOps Toolkit");
        _scriptsPath = Path.Combine(_dataDirectory, "scripts.json");
        _historyPath = Path.Combine(_dataDirectory, "script-history.json");
    }

    public string ExecutablePath => CommandRunner.ResolvePowerShell();

    public static bool RequiresElevation(string script) =>
        Regex.IsMatch(script, @"Checkpoint-Computer|DISM|sfc\s|chkdsk|Install-WindowsFeature|Add-LocalGroupMember|Set-Service|Stop-Service|Start-Service|netsh|bcdedit|Reset-ComputerMachinePassword|Set-DnsClientServerAddress", RegexOptions.IgnoreCase);

    public Task<IReadOnlyList<ScriptTemplateInfo>> GetTemplatesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ScriptTemplateInfo>>(BuiltInTemplates.Concat(ReadJson<ScriptTemplateInfo>(_scriptsPath)).ToList());

    public Task<IReadOnlyList<ScriptExecutionRecord>> GetHistoryAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ScriptExecutionRecord>>(ReadJson<ScriptExecutionRecord>(_historyPath).OrderByDescending(h => h.StartedAt).ToList());

    public Task SaveTemplateAsync(ScriptTemplateInfo template, CancellationToken ct = default)
    {
        var custom = ReadJson<ScriptTemplateInfo>(_scriptsPath);
        custom.RemoveAll(t => t.Id.Equals(template.Id, StringComparison.OrdinalIgnoreCase));
        custom.Add(template with { Id = "custom-" + template.Id });
        Directory.CreateDirectory(_dataDirectory);
        File.WriteAllText(_scriptsPath, JsonSerializer.Serialize(custom, new JsonSerializerOptions { WriteIndented = true }));
        return Task.CompletedTask;
    }

    public Task DeleteTemplateAsync(string id, CancellationToken ct = default)
    {
        var custom = ReadJson<ScriptTemplateInfo>(_scriptsPath);
        custom.RemoveAll(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        Directory.CreateDirectory(_dataDirectory);
        File.WriteAllText(_scriptsPath, JsonSerializer.Serialize(custom, new JsonSerializerOptions { WriteIndented = true }));
        return Task.CompletedTask;
    }

    public async Task<ActionExecutionResult> ExecuteAsync(
        string script,
        string? parameters,
        Func<ActionDefinition, Task<bool>> confirmAsync,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(script)) return ActionExecutionResult.Fail("The script is empty.", "No script supplied");
        var fullScript = CombineParameters(script, parameters);
        var result = await _safety.ExecuteAsync(
            "script.execute",
            async token =>
            {
                var command = await _commands.RunPowerShellAsync(fullScript, token, progress, TimeSpan.FromMinutes(10));
                return command.Success
                    ? ActionExecutionResult.Ok("Script completed successfully.", command.StandardOutput, $"{ExecutablePath} -EncodedCommand <script>", verified: true, exitCode: command.ExitCode)
                    : ActionExecutionResult.Fail("Script finished with an error.", command.StandardError, command.StandardOutput, $"{ExecutablePath} -EncodedCommand <script>", command.ExitCode);
            },
            confirmAsync,
            progress,
            ct,
            auditDetails: fullScript);

        var history = ReadJson<ScriptExecutionRecord>(_historyPath);
        history.Add(new ScriptExecutionRecord(result.StartedAt, "PowerShell script", fullScript, result.ExitCode, result.Duration, result.Output, result.Error, result.Success));
        Directory.CreateDirectory(_dataDirectory);
        File.WriteAllText(_historyPath, JsonSerializer.Serialize(history.TakeLast(200), new JsonSerializerOptions { WriteIndented = true }));
        return result;
    }

    private static string CombineParameters(string script, string? parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters)) return script;
        var assignments = new List<string>();
        foreach (var pair in parameters.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0) continue;
            var name = pair[..separator].Trim().TrimStart('$');
            var value = pair[(separator + 1)..].Trim().Trim('"', '\'');
            if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$")) continue;
            assignments.Add($"${name} = '{value.Replace("'", "''")}'");
        }
        return assignments.Count == 0 ? script : string.Join(Environment.NewLine, assignments) + Environment.NewLine + script;
    }

    private static List<T> ReadJson<T>(string path)
    {
        if (!File.Exists(path)) return [];
        try { return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path)) ?? []; }
        catch { return []; }
    }
}
