using System.Management;
using System.Runtime.Versioning;
using System.ServiceProcess;
using WinOps.Core.Abstractions;
using WinOps.Core.Models;
using WinOps.Core.Safety;
using WinOps.Windows.Integration;

namespace WinOps.Windows.Administration;

[SupportedOSPlatform("windows")]
public sealed class ProcessManager : IProcessManager
{
    private static readonly HashSet<string> CriticalProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "system idle process", "svchost.exe", "lsass.exe", "wininit.exe", "services.exe",
        "winlogon.exe", "csrss.exe", "smss.exe", "explorer.exe", "dwm.exe", "winDefend.exe"
    };

    private readonly ActionSafetyEngine _safety;

    public ProcessManager(ActionSafetyEngine safety) => _safety = safety;

    public async Task<IReadOnlyList<ProcessInfo>> GetProcessesAsync(CancellationToken ct = default) =>
        await Task.Run(CollectProcesses, ct);

    private static IReadOnlyList<ProcessInfo> CollectProcesses()
    {
        var result = new List<ProcessInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ProcessId,Name,CommandLine,ParentProcessId,ExecutablePath,KernelModeTime,UserModeTime,WorkingSetSize,CreationDate FROM Win32_Process");
            foreach (var item in searcher.Get())
            {
                using var process = (ManagementObject)item;
                var id = Convert.ToInt32(process["ProcessId"]);
                var name = Convert.ToString(process["Name"]) ?? "unknown";
                var owner = "Unavailable";
                try
                {
                    using var outParams = process.InvokeMethod("GetOwner", null!, null);
                    if (outParams is not null)
                        owner = $"{outParams["Domain"]}\\{outParams["User"]}";
                }
                catch { /* owner information is often privileged */ }

                var kernelTicks = Convert.ToInt64(process["KernelModeTime"] ?? 0L);
                var userTicks = Convert.ToInt64(process["UserModeTime"] ?? 0L);

                result.Add(new ProcessInfo(
                    id,
                    name,
                    (kernelTicks + userTicks) / 10_000_000.0,
                    Convert.ToInt64(process["WorkingSetSize"] ?? 0L),
                    Convert.ToString(process["ExecutablePath"]) ?? string.Empty,
                    owner,
                    Convert.ToString(process["CommandLine"]) ?? string.Empty,
                    process["ParentProcessId"] is null ? null : Convert.ToInt32(process["ParentProcessId"]),
                    "See Task Manager",
                    Convert.ToString(process["CreationDate"]) ?? string.Empty)
                {
                    IsCritical = CriticalProcesses.Contains(name)
                });
            }
        }
        catch (ManagementException)
        {
            return result;
        }
        return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<ProcessInfo>> GetProcessTreeAsync(int processId, CancellationToken ct = default)
    {
        var all = await GetProcessesAsync(ct);
        var tree = new List<ProcessInfo>();
        var pending = new Queue<int>();
        pending.Enqueue(processId);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            var process = all.FirstOrDefault(p => p.Id == current);
            if (process is null) continue;
            tree.Add(process);
            foreach (var child in all.Where(p => p.ParentId == current)) pending.Enqueue(child.Id);
        }
        return tree;
    }

    public Task<ActionExecutionResult> StopProcessAsync(
        int processId,
        bool force,
        Func<ActionDefinition, Task<bool>> confirmAsync,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default) =>
        _safety.ExecuteAsync(
            "process.stop",
            async token =>
            {
                var process = await Task.Run(() => System.Diagnostics.Process.GetProcessById(processId), token);
                var name = process.ProcessName;
                if (CriticalProcesses.Contains(name))
                    return ActionExecutionResult.Fail($" '{name}' is protected and cannot be stopped from WinOps Toolkit.", "Critical process safeguard");
                await Task.Run(() => process.Kill(entireProcessTree: force), token);
                return ActionExecutionResult.Ok($"Stopped {name} (PID {processId}).", command: $"Stop-Process -Id {processId}{(force ? " -Force" : string.Empty)}");
            },
            confirmAsync,
            progress,
            ct,
            async token => await Task.Run(() => !System.Diagnostics.Process.GetProcesses().Any(p => p.Id == processId), token));
}
