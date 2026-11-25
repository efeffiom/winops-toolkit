using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using WinOps.Core.Models;

namespace WinOps.Windows.Integration;

public sealed record CommandResult(
    string Executable,
    string Arguments,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    bool TimedOut)
{
    public bool Success => ExitCode == 0 && !TimedOut;
    public string CombinedOutput => StandardOutput + (string.IsNullOrWhiteSpace(StandardError) ? string.Empty : Environment.NewLine + StandardError);
}

/// <summary>
/// Executes native commands without a shell string, so arguments cannot be injected.
/// Supports cancellation, timeouts and full output capture.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CommandRunner
{
    public static string ResolvePowerShell()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7-preview", "pwsh.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")
        };
        return candidates.FirstOrDefault(File.Exists)
            ?? "powershell.exe";
    }

    public static string PowerShellVersion => File.Exists(ResolvePowerShell()) && ResolvePowerShell().EndsWith("pwsh.exe", StringComparison.OrdinalIgnoreCase) ? "PowerShell 7+" : "Windows PowerShell 5.1";

    public async Task<CommandResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        CancellationToken ct = default,
        IProgress<OperationProgress>? progress = null,
        TimeSpan? timeout = null,
        string? workingDirectory = null)
    {
        var args = arguments.ToArray();
        var display = $"{executable} {string.Join(' ', args)}";
        progress?.Report(new OperationProgress("Running command", 40, display));
        var start = DateTimeOffset.Now;
        var sw = Stopwatch.StartNew();
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (!string.IsNullOrWhiteSpace(workingDirectory)) startInfo.WorkingDirectory = workingDirectory;
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var timedOut = false;
        try
        {
            if (!process.Start())
                return new CommandResult(executable, display, -1, string.Empty, "Could not start process.", sw.Elapsed, false);

            using var registration = ct.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* already exited */ }
            });

            var outTask = process.StandardOutput.ReadToEndAsync();
            var errTask = process.StandardError.ReadToEndAsync();
            if (timeout.HasValue)
            {
                var waitTask = process.WaitForExitAsync(ct);
                var completed = await Task.WhenAny(waitTask, Task.Delay(timeout.Value, ct));
                if (completed != waitTask)
                {
                    timedOut = true;
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* already exited */ }
                    await waitTask;
                }
            }
            else
            {
                await process.WaitForExitAsync(ct);
            }
            stdout.Append(await outTask);
            stderr.Append(await errTask);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stderr.AppendLine(ex.Message);
        }
        finally
        {
            sw.Stop();
        }

        progress?.Report(new OperationProgress("Command finished", 75, display));
        return new CommandResult(executable, display, timedOut ? -2 : SafeExitCode(process), stdout.ToString(), stderr.ToString(), sw.Elapsed, timedOut);
    }

    public async Task<CommandResult> RunPowerShellAsync(
        string script,
        CancellationToken ct = default,
        IProgress<OperationProgress>? progress = null,
        TimeSpan? timeout = null)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var args = new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
            "-EncodedCommand", encoded
        };
        return await RunAsync(ResolvePowerShell(), args, ct, progress, timeout);
    }

    public static async Task<T?> RunPowerShellJsonAsync<T>(
        CommandRunner runner,
        string script,
        CancellationToken ct = default,
        TimeSpan? timeout = null)
    {
        var result = await runner.RunPowerShellAsync(script + "\n| ConvertTo-Json -Depth 5 -Compress", ct, timeout: timeout);
        if (!result.Success || string.IsNullOrWhiteSpace(result.StandardOutput)) return default;
        try { return JsonSerializer.Deserialize<T>(result.StandardOutput); }
        catch { return default; }
    }

    private static int SafeExitCode(Process process)
    {
        try { return process.ExitCode; } catch { return -1; }
    }
}
