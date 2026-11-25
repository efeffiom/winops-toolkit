using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using WinOps.Core.Abstractions;
using WinOps.Core.Models;
using WinOps.Core.Safety;
using WinOps.Windows.Integration;

namespace WinOps.Windows.Security;

[SupportedOSPlatform("windows")]
public sealed class SecurityService : ISecurityService
{
    private readonly CommandRunner _commands;
    private readonly ActionSafetyEngine _safety;

    private static readonly IReadOnlyList<SecurityProfile> Profiles =
    [
        new("Basic Workstation", "Essential protections for a personal or small-office Windows computer.", new Dictionary<string, string>
        {
            ["defender-realtime"] = "Enabled", ["firewall-domain"] = "On", ["firewall-private"] = "On", ["firewall-public"] = "On",
            ["uac"] = "Enabled", ["smbv1"] = "Disabled", ["autoadminlogon"] = "Disabled", ["password-minimum"] = "Minimum password length: 12"
        }, true),
        new("Corporate Workstation", "Baseline for domain-joined managed workstations.", new Dictionary<string, string>
        {
            ["defender-realtime"] = "Enabled", ["firewall-domain"] = "On", ["firewall-private"] = "On", ["firewall-public"] = "On",
            ["uac"] = "Enabled", ["smbv1"] = "Disabled", ["autoadminlogon"] = "Disabled", ["rdp"] = "Disabled",
            ["password-minimum"] = "Minimum password length: 14", ["builtin-admin"] = "Disabled", ["secureboot"] = "Enabled"
        }, true),
        new("Administrator Workstation", "Stricter baseline for privileged administrative workstations.", new Dictionary<string, string>
        {
            ["defender-realtime"] = "Enabled", ["firewall-domain"] = "On", ["firewall-private"] = "On", ["firewall-public"] = "On",
            ["uac"] = "Enabled", ["smbv1"] = "Disabled", ["autoadminlogon"] = "Disabled", ["rdp"] = "Disabled",
            ["password-minimum"] = "Minimum password length: 16", ["builtin-admin"] = "Disabled", ["secureboot"] = "Enabled",
            ["bitlocker"] = "Protection On"
        }, true),
        new("Windows Server Baseline", "Server-focused baseline. RDP remains available for authorized administration.", new Dictionary<string, string>
        {
            ["defender-realtime"] = "Enabled", ["firewall-domain"] = "On", ["firewall-private"] = "On", ["firewall-public"] = "On",
            ["uac"] = "Enabled", ["smbv1"] = "Disabled", ["autoadminlogon"] = "Disabled",
            ["password-minimum"] = "Minimum password length: 14", ["builtin-admin"] = "Disabled"
        }, true),
        new("Custom", "Select individual checks to preview and apply.", new Dictionary<string, string>(), true)
    ];

    public SecurityService(CommandRunner commands, ActionSafetyEngine safety)
    {
        _commands = commands;
        _safety = safety;
    }

    public Task<IReadOnlyList<SecurityProfile>> GetProfilesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SecurityProfile>>(Profiles);

    public async Task<SecurityAssessmentResult> AssessAsync(string profileName, CancellationToken ct = default)
    {
        var profile = Profiles.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase)) ?? Profiles[0];
        var checks = await CollectChecksAsync(ct);
        var evaluated = Evaluate(checks, profile);
        return new SecurityAssessmentResult(
            DateTimeOffset.Now,
            evaluated,
            profile.Name,
            evaluated.Count(c => c.Status == CheckStatus.Passed),
            evaluated.Count(c => c.Status == CheckStatus.Warning),
            evaluated.Count(c => c.Status == CheckStatus.Failed));
    }

    public async Task<IReadOnlyList<SecurityCheck>> PreviewProfileAsync(string profileName, CancellationToken ct = default)
    {
        var profile = Profiles.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase)) ?? Profiles[0];
        return Evaluate(await CollectChecksAsync(ct), profile);
    }

    private static IReadOnlyList<SecurityCheck> Evaluate(IReadOnlyList<SecurityCheck> checks, SecurityProfile profile)
    {
        if (profile.Name.Equals("Custom", StringComparison.OrdinalIgnoreCase)) return checks;
        return checks.Select(check =>
        {
            if (!profile.ExpectedValues.TryGetValue(check.Id, out var expected)) return check with { Status = CheckStatus.NotApplicable };
            var status = ValuesMatch(check.CurrentValue, expected)
                ? CheckStatus.Passed
                : check.CurrentValue.Equals("Unavailable", StringComparison.OrdinalIgnoreCase) ? CheckStatus.NotApplicable : CheckStatus.Failed;
            return check with { ExpectedValue = expected, Status = status };
        }).ToList();
    }

    private static bool ValuesMatch(string current, string expected)
    {
        if (current.Contains(expected, StringComparison.OrdinalIgnoreCase)) return true;
        return expected switch
        {
            "Enabled" => current.Contains("enabled", StringComparison.OrdinalIgnoreCase) || current.Contains("running", StringComparison.OrdinalIgnoreCase),
            "Disabled" => current.Contains("disabled", StringComparison.OrdinalIgnoreCase) || current.Contains("off", StringComparison.OrdinalIgnoreCase),
            "On" => current.Equals("on", StringComparison.OrdinalIgnoreCase),
            "Protection On" => current.Contains("protection on", StringComparison.OrdinalIgnoreCase),
            _ => current.Equals(expected, StringComparison.OrdinalIgnoreCase)
        };
    }

    private async Task<IReadOnlyList<SecurityCheck>> CollectChecksAsync(CancellationToken ct) =>
        await Task.Run<IReadOnlyList<SecurityCheck>>(() =>
        {
            return new List<SecurityCheck>
            {
            Check("defender-realtime", "Endpoint protection", "Microsoft Defender real-time protection",
                ReadRegistryInt(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows Defender\Real-Time Protection", "DisableRealtimeMonitoring") == 0 ? "Enabled" : "Disabled",
                Severity.High, "Real-time protection detects malware as files are accessed.", "Enable real-time protection in Microsoft Defender."),
            Check("firewall-domain", "Firewall", "Domain network firewall", ReadFirewall("DomainProfile"), Severity.High, "A domain firewall protects against unsolicited inbound traffic.", "Enable the domain firewall profile."),
            Check("firewall-private", "Firewall", "Private network firewall", ReadFirewall("StandardProfile"), Severity.Medium, "A private firewall protects trusted networks.", "Enable the private firewall profile."),
            Check("firewall-public", "Firewall", "Public network firewall", ReadFirewall("PublicProfile"), Severity.High, "Public networks are untrusted and require filtering.", "Enable the public firewall profile."),
            Check("uac", "Access control", "User Account Control", ReadRegistryInt(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA") == 1 ? "Enabled" : "Disabled",
                Severity.High, "UAC prevents silently elevating processes.", "Enable UAC (EnableLUA=1)."),
            Check("autoadminlogon", "Access control", "Automatic sign-on", ReadRegistryString(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "AutoAdminLogon") == "1" ? "Enabled" : "Disabled",
                Severity.Critical, "Automatic sign-on stores credentials and bypasses authentication.", "Disable AutoAdminLogon and remove stored credentials."),
            Check("smbv1", "Protocols", "Legacy SMBv1", ReadRegistryInt(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\mrxsmb10", "Start") == 4 ? "Disabled" : "Enabled or default",
                Severity.High, "SMBv1 is obsolete and vulnerable to exploitation.", "Disable SMBv1 after confirming no legacy dependency."),
            Check("rdp", "Remote access", "Remote Desktop", ReadRegistryInt(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections") == 1 ? "Disabled" : "Enabled",
                Severity.Medium, "RDP expands the remote attack surface.", "Disable RDP when not required, or restrict it with firewall rules."),
            Check("bitlocker", "Data protection", "BitLocker system volume", ReadBitLocker(), Severity.Medium, "BitLocker protects data at rest.", "Enable BitLocker with TPM protection."),
            Check("tpm", "Data protection", "Trusted Platform Module", CimQuery.Query("SELECT PresentStatus FROM Win32_Tpm", @"root\cimv2\security\microsofttpm").Count > 0 ? "Present" : "Not detected",
                Severity.Low, "A TPM protects encryption keys and measured boot.", "Enable the TPM in firmware if available."),
            Check("builtin-admin", "Accounts", "Built-in Administrator account", ReadBuiltInAdministrator(), Severity.High,
                "A disabled built-in Administrator reduces the number of privileged accounts.", "Keep the built-in Administrator disabled; use a named admin account."),
            Check("password-minimum", "Accounts", "Minimum password length", ReadPasswordPolicy(), Severity.Medium,
                "Longer passwords resist brute-force attempts.", "Increase the minimum password length through security policy."),
            Check("secureboot", "Platform", "Secure Boot", ReadSecureBoot(ct).GetAwaiter().GetResult(), Severity.Medium,
                "Secure Boot blocks unsigned boot loaders.", "Enable Secure Boot in UEFI firmware."),
            Check("security-log", "Auditing", "Security event log", EventLogExists("Security") ? "Present" : "Not available", Severity.Low,
                "Security logs support incident investigation.", "Ensure the Security log is enabled and sized appropriately.")
            };
        }, ct);

    private static SecurityCheck Check(string id, string category, string title, string current, Severity severity, string explanation, string recommendation, string actionId = "security.apply") =>
        new(id, category, title, CheckStatus.Warning, current, string.Empty, severity, explanation, recommendation, actionId);

    private static int ReadRegistryInt(RegistryKey hive, string path, string name)
    {
        try { using var key = hive.OpenSubKey(path); return key?.GetValue(name) is int value ? value : -1; }
        catch { return -1; }
    }

    private static string ReadRegistryString(RegistryKey hive, string path, string name)
    {
        try { using var key = hive.OpenSubKey(path); return key?.GetValue(name)?.ToString() ?? "Unavailable"; }
        catch { return "Unavailable"; }
    }

    private static string ReadFirewall(string profile)
    {
        var value = ReadRegistryInt(Registry.LocalMachine, $@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{profile}", "EnableFirewall");
        return value switch { 1 => "On", 0 => "Off", _ => "Unavailable" };
    }

    private static string ReadBitLocker()
    {
        var output = CimQuery.Query("SELECT ProtectionStatus,DriveLetter FROM Win32_EncryptableVolume", @"root\cimv2\security\microsoftvolumeencryption");
        if (output.Count == 0) return "Unavailable";
        var drive = output.FirstOrDefault(r => CimQuery.Text(r, "DriveLetter", string.Empty).Equals("C:", StringComparison.OrdinalIgnoreCase));
        return CimQuery.Number(drive ?? output[0], "ProtectionStatus") == 1 ? "Protection On" : "Protection Off";
    }

    private static string ReadBuiltInAdministrator()
    {
        var admins = CimQuery.Query("SELECT Name,Enabled,SID FROM Win32_UserAccount WHERE LocalAccount=True");
        var builtIn = admins.FirstOrDefault(row => CimQuery.Text(row, "SID", string.Empty).EndsWith("-500", StringComparison.Ordinal));
        if (builtIn is null) return "Unavailable";
        return CimQuery.Flag(builtIn, "Enabled") ? "Enabled" : "Disabled";
    }

    private static string ReadPasswordPolicy()
    {
        var result = CimQuery.Query("SELECT MinimumPasswordLength,MaximumPasswordAge FROM Win32_AccountPolicy").FirstOrDefault();
        var length = CimQuery.Number(result, "MinimumPasswordLength", -1);
        return length >= 0 ? $"Minimum password length: {length}" : "Unavailable";
    }

    private async Task<string> ReadSecureBoot(CancellationToken ct)
    {
        var result = await _commands.RunPowerShellAsync("try { if (Confirm-SecureBootUEFI -ErrorAction Stop) { 'Enabled' } else { 'Disabled' } } catch { 'Unavailable' }", ct, timeout: TimeSpan.FromSeconds(20));
        var value = result.StandardOutput.Trim();
        return value.Contains("Enabled", StringComparison.OrdinalIgnoreCase) ? "Enabled"
            : value.Contains("Disabled", StringComparison.OrdinalIgnoreCase) ? "Disabled" : "Unavailable";
    }

    private static bool EventLogExists(string name)
    {
        try { return EventLog.GetEventLogs().Any(l => l.Log.Equals(name, StringComparison.OrdinalIgnoreCase)); }
        catch { return false; }
    }

    public Task<ActionExecutionResult> ApplyProfileAsync(
        string profileName,
        IReadOnlyList<string> checkIds,
        Func<ActionDefinition, Task<bool>> confirmAsync,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default) =>
        _safety.ExecuteAsync(
            "security.apply",
            async token =>
            {
                var messages = new List<string>();
                var failures = new List<string>();
                for (var i = 0; i < checkIds.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    progress?.Report(new OperationProgress($"Applying {checkIds[i]}", (i * 100) / Math.Max(1, checkIds.Count), profileName));
                    var result = await ApplyCheckAsync(checkIds[i], token);
                    (result.Success ? messages : failures).Add($"{checkIds[i]}: {result.Message}");
                }
                var output = string.Join(Environment.NewLine, messages.Concat(failures));
                return failures.Count == 0
                    ? ActionExecutionResult.Ok($"Applied {messages.Count} security setting(s).", output, "Security profile preview", verified: false)
                    : ActionExecutionResult.Fail($"Applied {messages.Count}, failed {failures.Count} security setting(s).", string.Join("; ", failures), output, "Security profile preview", -1);
            },
            confirmAsync,
            progress,
            ct,
            verification: async token =>
            {
                var after = await AssessAsync(profileName, token);
                return after.Checks.Where(c => checkIds.Contains(c.Id)).All(c => c.Status == CheckStatus.Passed);
            });

    private Task<ActionExecutionResult> ApplyCheckAsync(string checkId, CancellationToken ct) =>
        checkId switch
        {
            "defender-realtime" => SetRegistryAsync(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows Defender\Real-Time Protection", "DisableRealtimeMonitoring", 0, "Defender real-time protection enabled"),
            "firewall-domain" => SetFirewallAsync("DomainProfile", "Domain firewall enabled"),
            "firewall-private" => SetFirewallAsync("StandardProfile", "Private firewall enabled"),
            "firewall-public" => SetFirewallAsync("PublicProfile", "Public firewall enabled"),
            "uac" => SetRegistryAsync(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", 1, "UAC enabled"),
            "autoadminlogon" => SetRegistryAsync(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "AutoAdminLogon", "0", "Automatic sign-on disabled"),
            "smbv1" => RunScriptAsync("Set-Service -Name mrxsmb10 -StartupType Disabled -ErrorAction SilentlyContinue", "SMBv1 disabled", ct),
            "rdp" => SetRegistryAsync(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections", 1, "Remote Desktop disabled"),
            "builtin-admin" => RunScriptAsync("Get-LocalUser -SID 'S-1-5-21-*-500' -ErrorAction SilentlyContinue | Disable-LocalUser -ErrorAction SilentlyContinue; Disable-LocalUser -Name 'Administrator' -ErrorAction SilentlyContinue", "Built-in Administrator disabled", ct),
            _ => Task.FromResult(ActionExecutionResult.Fail($"No safe automated remediation is defined for '{checkId}'. Follow the recommendation.", "Unsupported remediation"))
        };

    private static Task<ActionExecutionResult> SetRegistryAsync(RegistryKey hive, string path, string name, object value, string message) =>
        Task.Run(() =>
        {
            using var key = hive.OpenSubKey(path, writable: true);
            if (key is null) return ActionExecutionResult.Fail($"Registry key {path} is unavailable.", "Registry key not found");
            key.SetValue(name, value);
            return ActionExecutionResult.Ok(message, $"{path}\\{name} = {value}", verified: false);
        });

    private static Task<ActionExecutionResult> SetFirewallAsync(string profile, string message) =>
        SetRegistryAsync(Registry.LocalMachine, $@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{profile}", "EnableFirewall", 1, message);

    private async Task<ActionExecutionResult> RunScriptAsync(string script, string message, CancellationToken ct)
    {
        var result = await _commands.RunPowerShellAsync(script, ct, timeout: TimeSpan.FromMinutes(2));
        return result.Success
            ? ActionExecutionResult.Ok(message, result.CombinedOutput, script, verified: false)
            : ActionExecutionResult.Fail($"{message} could not be applied.", result.StandardError, result.CombinedOutput, script);
    }
}
