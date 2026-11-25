using WinOps.PowerShell.Execution;
using Xunit;

namespace WinOps.Tests;

public sealed class PowerShellStaticTests
{
    [Theory]
    [InlineData("Get-NetAdapter | Select-Object Name,Status", false)]
    [InlineData("Get-Process | Sort-Object CPU", false)]
    [InlineData("sfc /scannow", true)]
    [InlineData("DISM /Online /Cleanup-Image /RestoreHealth", true)]
    [InlineData("Set-DnsClientServerAddress -InterfaceIndex 4 -ServerAddresses 1.1.1.1", true)]
    [InlineData("Checkpoint-Computer -Description 'test'", true)]
    public void RequiresElevation_DetectsPrivilegedCommands(string script, bool expected) =>
        Assert.Equal(expected, PowerShellExecutor.RequiresElevation(script));
}
