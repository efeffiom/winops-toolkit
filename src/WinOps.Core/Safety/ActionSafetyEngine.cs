using WinOps.Core.Abstractions;
using WinOps.Core.Models;

namespace WinOps.Core.Safety;

/// <summary>
/// Central catalog of every state-changing operation exposed by the toolkit.
/// Risk levels and confirmation requirements are declared here, never by UI code.
/// </summary>
public static class ActionCatalog
{
    private static readonly Dictionary<string, ActionDefinition> Definitions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["process.stop"] = ActionDefinition.Reversible("process.stop", "Stop process", "Terminate the selected process. Unsaved work in that process will be lost."),
        ["service.state"] = ActionDefinition.HighRisk("service.state", "Change service state", "Start, stop or restart a Windows service."),
        ["service.startup"] = ActionDefinition.HighRisk("service.startup", "Change service startup type", "Modify how a Windows service starts with Windows."),
        ["task.state"] = ActionDefinition.Reversible("task.state", "Change scheduled task state", "Enable or disable a scheduled task."),
        ["task.run"] = ActionDefinition.Reversible("task.run", "Run scheduled task", "Start the scheduled task immediately."),
        ["restorepoint.create"] = ActionDefinition.Reversible("restorepoint.create", "Create restore point", "Create a system restore point before making changes.", rollback: false),
        ["power.state"] = ActionDefinition.HighRisk("power.state", "Shutdown, restart or log off", "Change the power state of this computer. Unsaved work may be lost.", false),
        ["network.flushdns"] = ActionDefinition.Reversible("network.flushdns", "Flush DNS cache", "Clear the local DNS resolver cache."),
        ["network.registerdns"] = ActionDefinition.Reversible("network.registerdns", "Register DNS", "Re-register this computer in DNS."),
        ["network.release"] = ActionDefinition.Reversible("network.release", "Release DHCP lease", "Release the current DHCP address. Connectivity will be interrupted."),
        ["network.renew"] = ActionDefinition.Reversible("network.renew", "Renew DHCP lease", "Obtain a fresh DHCP address."),
        ["network.adapter"] = ActionDefinition.HighRisk("network.adapter", "Enable or disable adapter", "Changing network adapter state can immediately disconnect this computer."),
        ["network.winsock"] = ActionDefinition.HighRisk("network.winsock", "Reset Winsock", "Reset Winsock catalog to defaults. Network connectivity may be interrupted."),
        ["network.tcpip"] = ActionDefinition.HighRisk("network.tcpip", "Reset TCP/IP stack", "Reset the TCP/IP stack to defaults. A restart may be required."),
        ["network.setdns"] = ActionDefinition.HighRisk("network.setdns", "Set DNS servers", "Replace the DNS server list on the selected adapter.", rollback: true),
        ["network.setstaticip"] = ActionDefinition.HighRisk("network.setstaticip", "Configure static IP", "Replace DHCP configuration with a static address. Validate all values before applying."),
        ["network.restoredhcp"] = ActionDefinition.Reversible("network.restoredhcp", "Restore DHCP", "Return the selected adapter to automatic address configuration."),
        ["update.scan"] = ActionDefinition.ReadOnly("update.scan", "Scan for updates", "Check Microsoft Update for available updates."),
        ["update.install"] = ActionDefinition.HighRisk("update.install", "Install Windows updates", "Download and install pending Windows updates. A restart may be required.", false),
        ["repair.sfc"] = ActionDefinition.HighRisk("repair.sfc", "System File Checker", "Scan and repair protected Windows system files.", false),
        ["repair.dism"] = ActionDefinition.HighRisk("repair.dism", "DISM component store repair", "Repair the Windows component store using known-good sources.", false),
        ["repair.chkdsk"] = ActionDefinition.HighRisk("repair.chkdsk", "Check Disk", "Check a volume for file system errors. Requires a restart to repair system volumes.", false),
        ["repair.general"] = ActionDefinition.Reversible("repair.general", "Repair Windows component", "Run a targeted repair command for a Windows component."),
        ["security.apply"] = ActionDefinition.HighRisk("security.apply", "Apply security profile", "Apply selected security hardening settings to this computer."),
        ["security.disable"] = ActionDefinition.Destructive("security.disable", "Disable a security control", "Explicitly disable a security control. This reduces protection until re-enabled."),
        ["user.create"] = ActionDefinition.Reversible("user.create", "Create local user", "Create a new local user account."),
        ["user.enable"] = ActionDefinition.Reversible("user.enable", "Enable local user", "Enable a local user account."),
        ["user.disable"] = ActionDefinition.HighRisk("user.disable", "Disable local user", "Disable a local user account. The user will no longer be able to sign in."),
        ["user.password"] = ActionDefinition.HighRisk("user.password", "Reset local password", "Reset a local account password."),
        ["user.group"] = ActionDefinition.Reversible("user.group", "Change group membership", "Add or remove an account from a local group."),
        ["user.bulk"] = ActionDefinition.HighRisk("user.bulk", "Bulk user operation", "Apply multiple account changes from a CSV file.", false),
        ["ad.enable"] = ActionDefinition.Reversible("ad.enable", "Enable domain user", "Enable an Active Directory user account.", true),
        ["ad.disable"] = ActionDefinition.HighRisk("ad.disable", "Disable domain user", "Disable an Active Directory user account."),
        ["ad.unlock"] = ActionDefinition.Reversible("ad.unlock", "Unlock domain user", "Unlock an Active Directory user account.", true),
        ["ad.password"] = ActionDefinition.HighRisk("ad.password", "Reset domain password", "Reset an Active Directory user password."),
        ["storage.cleanup"] = ActionDefinition.Destructive("storage.cleanup", "Delete temporary files", "Permanently delete the approved files. This cannot be undone."),
        ["script.execute"] = new ActionDefinition("script.execute", "Execute PowerShell script", "Execute user-supplied PowerShell code with the current user's privileges. Review the displayed script before approving.", RiskLevel.Level3HighRisk, false, true, false, "Record full script, output and exit code", "Check exit code and output"),
        ["remote.script"] = ActionDefinition.HighRisk("remote.script", "Execute remote script", "Execute PowerShell code on an authorized remote computer.", true),
        ["remote.command"] = ActionDefinition.Reversible("remote.command", "Remote administrative command", "Run an administrative command on an authorized remote computer.", true)
    };

    public static ActionDefinition Get(string id) =>
        Definitions.TryGetValue(id, out var definition)
            ? definition
            : ActionDefinition.ReadOnly(id, id, "Unclassified action - treated as read-only.");

    public static IReadOnlyCollection<ActionDefinition> All => Definitions.Values;
}

/// <summary>
/// Enforces risk level, elevation, explicit confirmation, cancellation, error handling and auditing.
/// Every administrative mutation in the application passes through this engine.
/// </summary>
public sealed class ActionSafetyEngine
{
    private readonly IPrivilegeService _privileges;
    private readonly IAuditSink _audit;

    public ActionSafetyEngine(IPrivilegeService privileges, IAuditSink audit)
    {
        _privileges = privileges;
        _audit = audit;
    }

    public ActionDefinition Describe(string actionId) => ActionCatalog.Get(actionId);

    public bool CanExecute(ActionDefinition action) =>
        !action.RequiresAdministrator || _privileges.IsAdministrator();

    public async Task<ActionExecutionResult> ExecuteAsync(
        string actionId,
        Func<CancellationToken, Task<ActionExecutionResult>> operation,
        Func<ActionDefinition, Task<bool>>? confirmation = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default,
        Func<CancellationToken, Task<bool>>? verification = null,
        string? auditDetails = null)
    {
        var action = ActionCatalog.Get(actionId);
        progress?.Report(new OperationProgress($"Preparing: {action.Title}", 5, action.Description));

        if (!CanExecute(action))
        {
            var denied = ActionExecutionResult.Fail(
                $"'{action.Title}' requires administrator privileges. Restart WinOps Toolkit as administrator.",
                "Elevation required");
            await _audit.RecordAsync(action, denied, auditDetails, ct);
            return denied with { RiskLevel = action.RiskLevel };
        }

        if (action.RequiresConfirmation || action.RiskLevel >= RiskLevel.Level2Reversible)
        {
            if (confirmation is null)
            {
                var denied = ActionExecutionResult.Fail($"No confirmation handler was provided for '{action.Title}'.", "Confirmation required");
                await _audit.RecordAsync(action, denied, auditDetails, ct);
                return denied with { RiskLevel = action.RiskLevel };
            }

            var approved = await confirmation(action);
            if (!approved)
            {
                var declined = new ActionExecutionResult
                {
                    Success = false,
                    Message = "Action cancelled by user.",
                    ConfirmationDeclined = true,
                    RiskLevel = action.RiskLevel,
                    StartedAt = DateTimeOffset.Now,
                    CompletedAt = DateTimeOffset.Now
                };
                await _audit.RecordAsync(action, declined, auditDetails, ct);
                return declined;
            }
        }

        progress?.Report(new OperationProgress(action.Title, 25, "Executing"));
        var started = DateTimeOffset.Now;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        ActionExecutionResult result;

        try
        {
            ct.ThrowIfCancellationRequested();
            result = await operation(ct);
        }
        catch (OperationCanceledException)
        {
            result = new ActionExecutionResult
            {
                Success = false,
                WasCancelled = true,
                Message = "Operation cancelled.",
                StartedAt = started,
                CompletedAt = DateTimeOffset.Now,
                Duration = clock.Elapsed,
                RiskLevel = action.RiskLevel
            };
        }
        catch (Exception ex)
        {
            result = ActionExecutionResult.Fail($"Failed: {action.Title}", ex.Message, string.Empty, string.Empty, ex.HResult);
        }

        clock.Stop();
        result = result with
        {
            RiskLevel = action.RiskLevel,
            StartedAt = started,
            CompletedAt = DateTimeOffset.Now,
            Duration = clock.Elapsed
        };

        if (result.Success && verification is not null)
        {
            progress?.Report(new OperationProgress($"Verifying: {action.Title}", 85, "Checking result"));
            try
            {
                var verified = await verification(ct);
                result = result with { Verified = verified };
                if (!verified)
                {
                    result = result with
                    {
                        Success = false,
                        Message = result.Message + " Verification failed.",
                        Error = string.IsNullOrWhiteSpace(result.Error) ? "Post-action verification did not confirm the change." : result.Error
                    };
                }
            }
            catch (Exception ex)
            {
                result = result with { Success = false, Verified = false, Error = $"Verification error: {ex.Message}" };
            }
        }

        progress?.Report(new OperationProgress(result.Success ? "Completed" : "Finished with errors", 100, result.Message));
        await _audit.RecordAsync(action, result, auditDetails, ct);
        return result;
    }
}
