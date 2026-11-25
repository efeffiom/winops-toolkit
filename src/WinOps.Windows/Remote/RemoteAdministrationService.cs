using System.Runtime.Versioning;
using System.Text.Json;
using WinOps.Core.Abstractions;
using WinOps.Core.Models;
using WinOps.Core.Safety;
using WinOps.Windows.Integration;

namespace WinOps.Windows.Remote;

[SupportedOSPlatform("windows")]
public sealed class RemoteAdministrationService : IRemoteAdministrationService
{
    private readonly ActionSafetyEngine _safety;
    private readonly CommandRunner _commands;
    private readonly string _storePath;

    public RemoteAdministrationService(ActionSafetyEngine safety, CommandRunner commands)
    {
        _safety = safety;
        _commands = commands;
        _storePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinOps Toolkit", "endpoints.json");
    }

    public async Task<IReadOnlyList<RemoteEndpoint>> GetEndpointsAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_storePath)) return [];
        try
        {
            var json = await File.ReadAllTextAsync(_storePath, ct);
            return JsonSerializer.Deserialize<List<RemoteEndpoint>>(json) ?? [];
        }
        catch { return []; }
    }

    public async Task SaveEndpointAsync(RemoteEndpoint endpoint, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(endpoint.HostName)) throw new ArgumentException("Host name is required.", nameof(endpoint));
        var endpoints = (await GetEndpointsAsync(ct)).Where(e => e.Id != endpoint.Id).ToList();
        endpoints.Add(endpoint);
        Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
        await File.WriteAllTextAsync(_storePath, JsonSerializer.Serialize(endpoints, new JsonSerializerOptions { WriteIndented = true }), ct);
    }

    public async Task DeleteEndpointAsync(string id, CancellationToken ct = default)
    {
        var endpoints = (await GetEndpointsAsync(ct)).Where(e => e.Id != id).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
        await File.WriteAllTextAsync(_storePath, JsonSerializer.Serialize(endpoints, new JsonSerializerOptions { WriteIndented = true }), ct);
    }

    public async Task<ActionExecutionResult> TestConnectionAsync(string hostName, CancellationToken ct = default)
    {
        if (!IsValidHost(hostName)) return ActionExecutionResult.Fail("Invalid host name or IP address.", "Validation failed");
        var wsman = await _commands.RunPowerShellAsync($"Test-WSMan -ComputerName {Quote(hostName)} -ErrorAction Stop | Select-Object -First 1 | Format-List *", ct, timeout: TimeSpan.FromSeconds(30));
        var ping = await _commands.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ping.exe"),
            [hostName, "-n", "1", "-w", "2000"], ct, timeout: TimeSpan.FromSeconds(10));
        var success = wsman.Success;
        return success
            ? ActionExecutionResult.Ok($"WinRM is reachable on {hostName}.", $"{wsman.StandardOutput}\nPing: {(ping.Success ? "succeeded" : "failed")}", "Test-WSMan", verified: true)
            : ActionExecutionResult.Fail($"WinRM is not reachable on {hostName}.", wsman.StandardError, wsman.StandardOutput + Environment.NewLine + ping.StandardOutput, "Test-WSMan");
    }

    public Task<RemoteCommandResult> GetRemoteSystemInfoAsync(string hostName, CancellationToken ct = default) =>
        RunRemoteAsync(hostName,
            "Get-CimInstance Win32_OperatingSystem | Select-Object CSName,Caption,Version,BuildNumber,LastBootUpTime,TotalVisibleMemorySize,FreePhysicalMemory | ConvertTo-Json -Compress",
            ct);

    public Task<RemoteCommandResult> GetRemoteServicesAsync(string hostName, CancellationToken ct = default) =>
        RunRemoteAsync(hostName,
            "Get-Service | Select-Object Name,DisplayName,Status,StartType | Sort-Object DisplayName | ConvertTo-Json -Compress -Depth 3",
            ct);

    public Task<RemoteCommandResult> GetRemoteProcessesAsync(string hostName, CancellationToken ct = default) =>
        RunRemoteAsync(hostName,
            "Get-Process | Select-Object -First 200 Id,ProcessName,CPU,WorkingSet64,Path | Sort-Object ProcessName | ConvertTo-Json -Compress -Depth 3",
            ct);

    public Task<RemoteCommandResult> GetRemoteEventsAsync(string hostName, int maxEvents, CancellationToken ct = default) =>
        RunRemoteAsync(hostName,
            $"Get-WinEvent -LogName System -MaxEvents {Math.Clamp(maxEvents, 1, 500)} | Select-Object TimeCreated,Id,ProviderName,LevelDisplayName,Message | ConvertTo-Json -Compress -Depth 3",
            ct);

    private async Task<RemoteCommandResult> RunRemoteAsync(string hostName, string remoteScript, CancellationToken ct)
    {
        if (!IsValidHost(hostName)) return new RemoteCommandResult(hostName, false, string.Empty, "Invalid host name.", string.Empty, TimeSpan.Zero);
        var script = $"Invoke-Command -ComputerName {Quote(hostName)} -ScriptBlock {{ {remoteScript} }} -ErrorAction Stop";
        var result = await _commands.RunPowerShellAsync(script, ct, timeout: TimeSpan.FromMinutes(2));
        return new RemoteCommandResult(hostName, result.Success, result.StandardOutput, result.StandardError, script, result.Duration);
    }

    public async Task<RemoteCommandResult> RunRemoteScriptAsync(string hostName, string script, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default)
    {
        var execution = await _safety.ExecuteAsync(
            "remote.script",
            async token =>
            {
                if (!IsValidHost(hostName)) return ActionExecutionResult.Fail("Invalid host name.", "Validation failed");
                var remote = $"Invoke-Command -ComputerName {Quote(hostName)} -ScriptBlock {{ [scriptblock]::Create({Quote(script)}) | Invoke-Expression }} -ErrorAction Stop";
                var result = await _commands.RunPowerShellAsync(remote, token, timeout: TimeSpan.FromMinutes(5));
                return result.Success
                    ? ActionExecutionResult.Ok($"Remote script completed on {hostName}.", result.StandardOutput, remote, verified: false)
                    : ActionExecutionResult.Fail($"Remote script failed on {hostName}.", result.StandardError, result.StandardOutput, remote);
            },
            confirmAsync,
            ct: ct);
        return new RemoteCommandResult(hostName, execution.Success, execution.Output, execution.Error, execution.ExecutedCommand, execution.Duration);
    }

    private static bool IsValidHost(string host) =>
        !string.IsNullOrWhiteSpace(host) && host.Length <= 255 && host.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_');

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
}
