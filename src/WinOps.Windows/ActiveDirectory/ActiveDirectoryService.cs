using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.DirectoryServices.ActiveDirectory;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using WinOps.Core.Abstractions;
using WinOps.Core.Models;
using WinOps.Core.Safety;
using WinOps.Windows.Integration;

namespace WinOps.Windows.ActiveDirectory;

[SupportedOSPlatform("windows")]
public sealed class ActiveDirectoryService : IActiveDirectoryService
{
    private readonly ActionSafetyEngine _safety;
    private readonly CommandRunner _commands;
    private readonly Lazy<bool> _domainJoined;

    public ActiveDirectoryService(ActionSafetyEngine safety, CommandRunner commands)
    {
        _safety = safety;
        _commands = commands;
        _domainJoined = new Lazy<bool>(() =>
        {
            try { _ = Domain.GetComputerDomain(); return true; }
            catch { return false; }
        });
    }

    public bool IsAvailable => _domainJoined.Value;

    public string PrerequisiteMessage => IsAvailable
        ? "Active Directory domain detected. Domain operations use the current Windows identity."
        : "This computer is not domain joined, or the domain is unreachable. Join a domain and install RSAT: Active Directory Domain Services Tools to enable these features. Local account management remains available.";

    public async Task<DomainInfo> GetDomainInfoAsync(CancellationToken ct = default) =>
        await Task.Run(() =>
        {
            if (!IsAvailable) return new DomainInfo("Not joined", string.Empty, string.Empty, "Unavailable", false, "Unavailable");
            try
            {
                using var domain = Domain.GetComputerDomain();
                var controller = domain.DomainControllers.Cast<DomainController>().FirstOrDefault()?.Name ?? "Unavailable";
                return new DomainInfo(domain.Name, domain.Forest.Name, controller, domain.DomainMode.ToString(), true,
                    "Unavailable");
            }
            catch (Exception ex)
            {
                return new DomainInfo("Unavailable", string.Empty, string.Empty, ex.Message, false, "Unavailable");
            }
        }, ct);

    public async Task<IReadOnlyList<AdUser>> FindUsersAsync(string search, int maxResults, CancellationToken ct = default) =>
        await Task.Run(() =>
        {
            var result = new List<AdUser>();
            if (!IsAvailable) return result;
            try
            {
                using var context = new PrincipalContext(ContextType.Domain);
                using var prototype = new UserPrincipal(context)
                {
                    SamAccountName = $"*{search}*",
                    Enabled = null
                };
                // SamAccountName wildcards are not supported; use display name search when provided.
                prototype.SamAccountName = null;
                prototype.DisplayName = string.IsNullOrWhiteSpace(search) ? null : $"*{search}*";
                using var searcher = new PrincipalSearcher(prototype);
                foreach (var found in searcher.FindAll())
                {
                    ct.ThrowIfCancellationRequested();
                    using var principal = (UserPrincipal)found;
                    var groups = new List<string>();
                    try { groups.AddRange(principal.GetGroups().Select(g => g.Name)); } catch { /* nested group lookup can be denied */ }
                    result.Add(new AdUser(
                        principal.DistinguishedName ?? string.Empty,
                        principal.SamAccountName ?? string.Empty,
                        principal.DisplayName ?? principal.Name ?? string.Empty,
                        principal.EmailAddress ?? string.Empty,
                        principal.Enabled ?? false,
                        IsLocked(principal),
                        principal.LastLogon,
                        null,
                        principal.AccountExpirationDate,
                        groups));
                    if (result.Count >= Math.Clamp(maxResults, 1, 1000)) break;
                }
            }
            catch (PrincipalServerDownException) { /* domain offline */ }
            catch (Exception) { /* query failed */ }
            return result.OrderBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }, ct);

    private static bool IsLocked(UserPrincipal user)
    {
        try { return user.IsAccountLockedOut(); } catch { return false; }
    }

    public Task<ActionExecutionResult> SetUserStateAsync(string distinguishedName, bool enable, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default) =>
        ExecuteUserAction(enable ? "ad.enable" : "ad.disable", distinguishedName, confirmAsync, ct, user =>
        {
            user.Enabled = enable;
            user.Save();
        }, $"Domain account {(enable ? "enabled" : "disabled")}.",
        async token => (await FindUsersAsync(DnName(distinguishedName), 50, token)).FirstOrDefault(u => u.DistinguishedName == distinguishedName)?.Enabled == enable);

    public Task<ActionExecutionResult> UnlockUserAsync(string distinguishedName, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default) =>
        ExecuteUserAction("ad.unlock", distinguishedName, confirmAsync, ct, user =>
        {
            user.UnlockAccount();
            user.Save();
        }, "Domain account unlocked.", async token =>
            (await FindUsersAsync(DnName(distinguishedName), 50, token)).FirstOrDefault(u => u.DistinguishedName == distinguishedName)?.LockedOut == false);

    public Task<ActionExecutionResult> ResetPasswordAsync(string distinguishedName, string password, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(password)) return Task.FromResult(ActionExecutionResult.Fail("A password is required.", "Empty password"));
        return ExecuteUserAction("ad.password", distinguishedName, confirmAsync, ct, user =>
        {
            user.SetPassword(password);
            user.Save();
        }, "Domain account password reset.", verification: null);
    }

    private Task<ActionExecutionResult> ExecuteUserAction(
        string actionId,
        string distinguishedName,
        Func<ActionDefinition, Task<bool>> confirmAsync,
        CancellationToken ct,
        Action<UserPrincipal> mutation,
        string successMessage,
        Func<CancellationToken, Task<bool>>? verification = null) =>
        _safety.ExecuteAsync(
            actionId,
            async token =>
            {
                if (!IsAvailable) return ActionExecutionResult.Fail(PrerequisiteMessage, "Active Directory unavailable");
                await Task.Run(() =>
                {
                    using var context = new PrincipalContext(ContextType.Domain);
                    using var user = UserPrincipal.FindByIdentity(context, IdentityType.DistinguishedName, distinguishedName)
                        ?? throw new InvalidOperationException("Domain user was not found.");
                    mutation(user);
                }, token);
                return ActionExecutionResult.Ok(successMessage, distinguishedName, "Active Directory " + actionId, verified: verification is null);
            },
            confirmAsync,
            ct: ct,
            verification: verification);

    public async Task<IReadOnlyList<AdGroup>> GetGroupsAsync(string search, CancellationToken ct = default) =>
        await Task.Run(() =>
        {
            var result = new List<AdGroup>();
            if (!IsAvailable) return result;
            try
            {
                using var context = new PrincipalContext(ContextType.Domain);
                using var prototype = new GroupPrincipal(context) { Name = string.IsNullOrWhiteSpace(search) ? null : $"*{search}*" };
                using var searcher = new PrincipalSearcher(prototype);
                foreach (var found in searcher.FindAll())
                {
                    ct.ThrowIfCancellationRequested();
                    using var group = (GroupPrincipal)found;
                    result.Add(new AdGroup(group.Name ?? string.Empty, group.DistinguishedName ?? string.Empty,
                        group.GroupScope?.ToString() ?? string.Empty, group.Members.Select(m => m.Name).ToList()));
                }
            }
            catch (Exception) { /* query failed */ }
            return result.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }, ct);

    public async Task<IReadOnlyList<OrganizationalUnit>> GetOrganizationalUnitsAsync(CancellationToken ct = default) =>
        await Task.Run(() =>
        {
            var result = new List<OrganizationalUnit>();
            if (!IsAvailable) return result;
            try
            {
                using var root = new DirectoryEntry("LDAP://RootDSE");
                var namingContext = root.Properties["defaultNamingContext"].Value?.ToString();
                if (string.IsNullOrWhiteSpace(namingContext)) return result;
                using var domain = new DirectoryEntry($"LDAP://{namingContext}");
                using var searcher = new DirectorySearcher(domain)
                {
                    Filter = "(objectClass=organizationalUnit)",
                    PageSize = 500,
                    PropertiesToLoad = { "name", "distinguishedName", "ou" }
                };
                using var collection = searcher.FindAll();
                foreach (SearchResult item in collection)
                {
                    ct.ThrowIfCancellationRequested();
                    result.Add(new OrganizationalUnit(
                        item.Properties["name"].Count > 0 ? item.Properties["name"][0].ToString() ?? string.Empty : string.Empty,
                        item.Properties["distinguishedName"].Count > 0 ? item.Properties["distinguishedName"][0].ToString() ?? string.Empty : string.Empty));
                }
            }
            catch (DirectoryServicesCOMException) { /* access denied or domain offline */ }
            return result.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }, ct);

    public async Task<IReadOnlyList<DiagnosticFinding>> RunDomainDiagnosticsAsync(CancellationToken ct = default)
    {
        var findings = new List<DiagnosticFinding>();
        if (!IsAvailable)
        {
            findings.Add(new DiagnosticFinding("ad.domain", "Domain membership", Severity.High, CheckStatus.Failed,
                "No domain membership detected.", PrerequisiteMessage, "Join the domain or install RSAT tools.", string.Empty, false, string.Empty));
            return findings;
        }
        var info = await GetDomainInfoAsync(ct);
        findings.Add(new DiagnosticFinding("ad.domain", "Domain membership", Severity.Info, CheckStatus.Passed,
            $"Joined to {info.Name}.", $"DC: {info.DomainController}", "No action required.", string.Empty, false, string.Empty));

        try
        {
            var addresses = await System.Net.Dns.GetHostAddressesAsync(info.Name, ct);
            findings.Add(new DiagnosticFinding("ad.dns", "Domain DNS", addresses.Length > 0 ? Severity.Info : Severity.High,
                addresses.Length > 0 ? CheckStatus.Passed : CheckStatus.Failed,
                $"{addresses.Length} address(es) resolved for {info.Name}.", string.Join(", ", addresses.Select(a => a.ToString())),
                "Verify DNS server settings.", string.Empty, false, string.Empty));
        }
        catch (Exception ex)
        {
            findings.Add(new DiagnosticFinding("ad.dns", "Domain DNS", Severity.High, CheckStatus.Failed, "Domain name could not be resolved.", ex.Message, "Check adapter DNS settings and domain controller health.", string.Empty, false, string.Empty));
        }

        using var ping = new Ping();
        var reply = await ping.SendPingAsync(info.DomainController, TimeSpan.FromSeconds(3), Array.Empty<byte>(), new PingOptions(), ct);
        findings.Add(new DiagnosticFinding("ad.dc", "Domain controller connectivity", reply.Status == IPStatus.Success ? Severity.Info : Severity.High,
            reply.Status == IPStatus.Success ? CheckStatus.Passed : CheckStatus.Failed,
            $"Ping to {info.DomainController}: {reply.Status}.", reply.Status == IPStatus.Success ? $"{reply.RoundtripTime} ms" : string.Empty,
            "Check network connectivity and firewall rules.", string.Empty, false, string.Empty));

        var secureChannel = await _commands.RunPowerShellAsync("try { Test-ComputerSecureChannel -Verbose } catch { $_ }", ct, timeout: TimeSpan.FromSeconds(30));
        findings.Add(new DiagnosticFinding("ad.securechannel", "Secure channel", secureChannel.StandardOutput.Contains("True", StringComparison.OrdinalIgnoreCase) ? Severity.Info : Severity.High,
            secureChannel.StandardOutput.Contains("True", StringComparison.OrdinalIgnoreCase) ? CheckStatus.Passed : CheckStatus.Failed,
            "Secure channel test result.", secureChannel.CombinedOutput, "Reset the computer account if the secure channel is broken.", string.Empty, false, string.Empty));

        var time = await _commands.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "w32tm.exe"), ["/query", "/status"], ct, timeout: TimeSpan.FromSeconds(20));
        findings.Add(new DiagnosticFinding("ad.time", "Time synchronization", time.Success ? Severity.Info : Severity.Medium,
            time.Success ? CheckStatus.Passed : CheckStatus.Warning,
            "Windows Time status collected.", time.CombinedOutput, "Kerberos requires clocks within five minutes of the domain.", string.Empty, false, string.Empty));
        return findings;
    }

    private static string DnName(string distinguishedName) =>
        distinguishedName.Split(',').FirstOrDefault()?.Split('=').ElementAtOrDefault(1) ?? string.Empty;
}
