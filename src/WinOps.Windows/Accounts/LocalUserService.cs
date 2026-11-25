using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using WinOps.Core.Abstractions;
using WinOps.Core.Models;
using WinOps.Core.Safety;
using WinOps.Windows.Integration;

namespace WinOps.Windows.Accounts;

[SupportedOSPlatform("windows")]
public sealed class LocalUserService : ILocalUserService
{
    private readonly ActionSafetyEngine _safety;
    private readonly CommandRunner _commands;

    public LocalUserService(ActionSafetyEngine safety, CommandRunner commands)
    {
        _safety = safety;
        _commands = commands;
    }

    public async Task<IReadOnlyList<LocalAccount>> GetUsersAsync(CancellationToken ct = default) =>
        await Task.Run(CollectUsers, ct);

    private static IReadOnlyList<LocalAccount> CollectUsers()
    {
        var memberships = ReadMemberships();
        var rows = CimQuery.Query("SELECT Name,FullName,Description,Disabled,Lockout,PasswordRequired,LastLogon,PasswordLastSet,AccountExpires,SID FROM Win32_UserAccount WHERE LocalAccount=True");
        return rows.Select(row =>
        {
            var name = CimQuery.Text(row, "Name", string.Empty);
            var sid = CimQuery.Text(row, "SID", string.Empty);
            return new LocalAccount(
                name,
                CimQuery.Text(row, "FullName", string.Empty),
                CimQuery.Text(row, "Description", string.Empty),
                !CimQuery.Flag(row, "Disabled"),
                CimQuery.Flag(row, "Lockout"),
                CimQuery.Flag(row, "PasswordRequired"),
                CimQuery.Date(row, "LastLogon"),
                CimQuery.Date(row, "PasswordLastSet"),
                CimQuery.Date(row, "AccountExpires"),
                memberships.TryGetValue(name, out var groups) ? groups : [])
            {
                // SID ending in -500 is the built-in Administrator account.
                IsBuiltInAdministrator = sid.EndsWith("-500", StringComparison.OrdinalIgnoreCase)
            };
        }).OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<LocalGroupInfo>> GetGroupsAsync(CancellationToken ct = default) =>
        await Task.Run(() =>
        {
            var memberships = ReadMemberships();
            return CimQuery.Query("SELECT Name,Description,SID FROM Win32_Group WHERE LocalAccount=True")
                .Select(row =>
                {
                    var name = CimQuery.Text(row, "Name", string.Empty);
                    var members = memberships.Where(kv => kv.Value.Contains(name, StringComparer.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList();
                    return new LocalGroupInfo(name, CimQuery.Text(row, "Description", string.Empty), members,
                        CimQuery.Text(row, "SID", string.Empty).Contains("-", StringComparison.Ordinal));
                })
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, ct);

    private static Dictionary<string, List<string>> ReadMemberships()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in CimQuery.Query("SELECT GroupComponent,PartComponent FROM Win32_GroupUser"))
        {
            var user = ParseRef(CimQuery.Text(row, "PartComponent", string.Empty), "Win32_UserAccount");
            var group = ParseRef(CimQuery.Text(row, "GroupComponent", string.Empty), "Win32_Group");
            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(group)) continue;
            if (!result.TryGetValue(user, out var groups)) result[user] = groups = [];
            if (!groups.Contains(group, StringComparer.OrdinalIgnoreCase)) groups.Add(group);
        }
        return result;
    }

    private static string ParseRef(string reference, string className)
    {
        if (!reference.Contains(className, StringComparison.OrdinalIgnoreCase)) return string.Empty;
        var match = Regex.Match(reference, "Name=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    public Task<ActionExecutionResult> CreateUserAsync(string name, string password, string fullName, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default)
    {
        if (!IsValidUserName(name)) return Task.FromResult(ActionExecutionResult.Fail("Invalid account name.", "Use 1-20 characters without / \\ [ ] : ; | = , + * ? < >"));
        if (string.IsNullOrWhiteSpace(password)) return Task.FromResult(ActionExecutionResult.Fail("A password is required.", "Empty password"));
        var args = new List<string> { "user", name, password, "/add", "/passwordchg:yes" };
        if (!string.IsNullOrWhiteSpace(fullName)) args.Add($"/fullname:\"{fullName}\"");
        return ExecuteNetAsync("user.create", args, $"Local user '{name}' created.", confirmAsync, ct,
            verification: async token => (await GetUsersAsync(token)).Any(u => u.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<ActionExecutionResult> SetUserStateAsync(string name, bool enable, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default)
    {
        if (IsBuiltInAdmin(name) && !enable)
            return Task.FromResult(ActionExecutionResult.Fail("The built-in Administrator account is protected and cannot be disabled here.", "Protected account"));
        return ExecuteNetAsync(enable ? "user.enable" : "user.disable", ["user", name, $"/active:{(enable ? "yes" : "no")}"],
            $"Account '{name}' {(enable ? "enabled" : "disabled")}.", confirmAsync, ct,
            verification: async token => (await GetUsersAsync(token)).FirstOrDefault(u => u.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Enabled == enable);
    }

    public Task<ActionExecutionResult> ResetPasswordAsync(string name, string password, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(password)) return Task.FromResult(ActionExecutionResult.Fail("A password is required.", "Empty password"));
        return ExecuteNetAsync("user.password", ["user", name, password], $"Password reset for '{name}'.", confirmAsync, ct);
    }

    public Task<ActionExecutionResult> SetGroupMembershipAsync(string group, string user, bool add, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default) =>
        ExecuteNetAsync("user.group", ["localgroup", group, user, add ? "/add" : "/delete"],
            $"{(add ? "Added" : "Removed")} '{user}' {(add ? "to" : "from")} '{group}'.", confirmAsync, ct,
            verification: async token =>
            {
                var groups = await GetGroupsAsync(token);
                var target = groups.FirstOrDefault(g => g.Name.Equals(group, StringComparison.OrdinalIgnoreCase));
                return target is not null && target.Members.Contains(user, StringComparer.OrdinalIgnoreCase) == add;
            });

    public async Task<IReadOnlyList<ActionExecutionResult>> RunBulkOperationAsync(string csvContent, bool apply, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default)
    {
        var results = new List<ActionExecutionResult>();
        var lines = csvContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return [ActionExecutionResult.Fail("CSV must contain a header row and at least one data row.", "Empty CSV")];

        foreach (var raw in lines.Skip(1))
        {
            var fields = ParseCsv(raw);
            var action = fields.ElementAtOrDefault(0)?.Trim() ?? string.Empty;
            var user = fields.ElementAtOrDefault(1)?.Trim() ?? string.Empty;
            var password = fields.ElementAtOrDefault(2)?.Trim() ?? string.Empty;
            var fullName = fields.ElementAtOrDefault(3)?.Trim() ?? string.Empty;
            var enabled = bool.TryParse(fields.ElementAtOrDefault(4), out var value) && value;

            if (!apply)
            {
                results.Add(new ActionExecutionResult
                {
                    Success = true,
                    Message = $"Preview: {action} '{user}'{(string.IsNullOrWhiteSpace(fullName) ? string.Empty : $" ({fullName})")}{(action.Contains("disable", StringComparison.OrdinalIgnoreCase) ? " [PROTECTED CHECK]" : string.Empty)}",
                    Verified = false,
                    RiskLevel = RiskLevel.Level2Reversible
                });
                continue;
            }

            results.Add(action.ToLowerInvariant() switch
            {
                "create" => await CreateUserAsync(user, password, fullName, confirmAsync, ct),
                "enable" => await SetUserStateAsync(user, true, confirmAsync, ct),
                "disable" => await SetUserStateAsync(user, false, confirmAsync, ct),
                "password" => await ResetPasswordAsync(user, password, confirmAsync, ct),
                _ => ActionExecutionResult.Fail($"Unknown bulk action '{action}'.", "CSV validation")
            });
        }
        return results;
    }

    private Task<ActionExecutionResult> ExecuteNetAsync(string actionId, IReadOnlyList<string> arguments, string successMessage, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct, Func<CancellationToken, Task<bool>>? verification = null) =>
        _safety.ExecuteAsync(
            actionId,
            async token =>
            {
                var result = await _commands.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "net.exe"), arguments, token, timeout: TimeSpan.FromMinutes(1));
                return result.Success
                    ? ActionExecutionResult.Ok(successMessage, result.CombinedOutput, "net " + string.Join(' ', arguments), verified: verification is null)
                    : ActionExecutionResult.Fail($"Command failed for '{arguments.ElementAtOrDefault(1)}'.", result.StandardError, result.CombinedOutput, "net " + string.Join(' ', arguments));
            },
            confirmAsync,
            ct: ct,
            verification: verification);

    private static bool IsValidUserName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 20 && !name.Any(c => "/\\[]:;|=,+*?<>".Contains(c));

    private static bool IsBuiltInAdmin(string name)
    {
        var user = CollectUsers().FirstOrDefault(u => u.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return user?.IsBuiltInAdministrator == true;
    }

    private static IReadOnlyList<string> ParseCsv(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var c in line)
        {
            if (c == '"') quoted = !quoted;
            else if (c == ',' && !quoted) { result.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        result.Add(current.ToString());
        return result;
    }
}
