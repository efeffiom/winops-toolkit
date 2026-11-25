using System.Management;
using System.Runtime.Versioning;
using System.Security.Principal;
using WinOps.Core.Abstractions;

namespace WinOps.Windows.Integration;

[SupportedOSPlatform("windows")]
public sealed class PrivilegeService : IPrivilegeService
{
    public bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public string CurrentUserName
    {
        get
        {
            try { return WindowsIdentity.GetCurrent().Name; }
            catch { return Environment.UserName; }
        }
    }

    public IReadOnlyList<string> DetectedCapabilities
    {
        get
        {
            var list = new List<string>
            {
                IsAdministrator() ? "Administrator" : "Standard user",
                $"OS: {Environment.OSVersion.Version}",
                $"PowerShell: {CommandRunner.PowerShellVersion}",
                IsDomainJoined() ? "Domain joined" : "Workgroup or domain not detected",
                IsWinRmRunning() ? "WinRM running" : "WinRM not running",
                HasActiveDirectoryModule() ? "Active Directory module available" : "Active Directory module unavailable"
            };
            return list;
        }
    }

    private static bool IsDomainJoined()
    {
        try
        {
            var domain = Environment.GetEnvironmentVariable("USERDOMAIN");
            var machine = Environment.MachineName;
            return !string.IsNullOrWhiteSpace(domain) && !domain.Equals(machine, StringComparison.OrdinalIgnoreCase) && domain != "WORKGROUP";
        }
        catch { return false; }
    }

    private static bool IsWinRmRunning()
    {
        try
        {
            using var service = new System.ServiceProcess.ServiceController("WinRM");
            return service.Status == System.ServiceProcess.ServiceControllerStatus.Running;
        }
        catch { return false; }
    }

    private static bool HasActiveDirectoryModule()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "WindowsPowerShell", "Modules", "ActiveDirectory", "ActiveDirectory.psd1");
        return File.Exists(path);
    }
}
