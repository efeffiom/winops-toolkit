[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Path,

    [string]$ExpectedVersion = '1.0.0',

    [string]$ExpectedUpgradeCode = '{8B4E7A2F-3C19-4D6E-9A5B-2F0D1C7E6A21}'
)

$ErrorActionPreference = 'Stop'

function Get-MsiColumn
{
    param(
        [Parameter(Mandatory)]$Database,
        [Parameter(Mandatory)][string]$Sql
    )

    $view = $Database.OpenView($Sql)
    $values = @()
    try
    {
        [void]$view.Execute()
        while ($null -ne ($record = $view.Fetch()))
        {
            $values += [string]$record.StringData(1)
            [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($record)
        }
    }
    finally
    {
        [void]$view.Close()
        [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($view)
    }
    return $values
}

function Get-MsiProperty
{
    param([Parameter(Mandatory)]$Database, [Parameter(Mandatory)][string]$Name)
    $escaped = $Name.Replace("'", "''")
    $values = @(Get-MsiColumn -Database $Database -Sql "SELECT Value FROM Property WHERE Property = '$escaped'")
    if ($values.Count -eq 0) { return $null }
    return [string]$values[0]
}

if (-not (Test-Path -LiteralPath $Path))
{
    throw "MSI not found: $Path"
}

$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase((Resolve-Path -LiteralPath $Path).Path, 0)

try
{
    $failures = @()
    $checks = @()

    $productName = Get-MsiProperty -Database $database -Name 'ProductName'
    $productVersion = Get-MsiProperty -Database $database -Name 'ProductVersion'
    $productCode = Get-MsiProperty -Database $database -Name 'ProductCode'
    $upgradeCode = Get-MsiProperty -Database $database -Name 'UpgradeCode'
    $allUsers = Get-MsiProperty -Database $database -Name 'ALLUSERS'

    $shortcuts = @(Get-MsiColumn -Database $database -Sql 'SELECT Shortcut FROM Shortcut')
    $files = @(Get-MsiColumn -Database $database -Sql 'SELECT File FROM File')
    $upgrades = @(Get-MsiColumn -Database $database -Sql 'SELECT UpgradeCode FROM Upgrade')
    $launchConditions = @(Get-MsiColumn -Database $database -Sql 'SELECT Condition FROM LaunchCondition')
    $media = @(Get-MsiColumn -Database $database -Sql 'SELECT DiskId FROM Media')
    $fileNames = @(Get-MsiColumn -Database $database -Sql 'SELECT FileName FROM File')
    $mainExe = @($fileNames | Where-Object { $_ -match '(^|\|)WinOps\.Toolkit\.exe$' })

    if ($productName -ne 'WinOps Toolkit') { $failures += "ProductName is '$productName'." }
    if ($productVersion -ne $ExpectedVersion) { $failures += "ProductVersion is '$productVersion', expected '$ExpectedVersion'." }
    if ($productCode -notmatch '^\{[0-9A-Fa-f-]{36}\}$') { $failures += "ProductCode is missing or invalid: '$productCode'." }
    if (($upgradeCode -replace '[{}]', '') -ine ($ExpectedUpgradeCode -replace '[{}]', ''))
    {
        $failures += "UpgradeCode is '$upgradeCode', expected '$ExpectedUpgradeCode'."
    }
    if ([string]::IsNullOrWhiteSpace($allUsers)) { $failures += 'ALLUSERS is not set; the package is not per-machine.' }
    if ($shortcuts.Count -lt 2) { $failures += "Only $($shortcuts.Count) shortcut row(s) found; expected at least 2." }
    if ($files.Count -lt 50) { $failures += "Only $($files.Count) file rows found; the payload looks incomplete." }
    if ($mainExe.Count -eq 0) { $failures += 'WinOps.Toolkit.exe was not found in the File table.' }
    if ($upgrades.Count -eq 0) { $failures += 'No Upgrade rows found; major upgrades will not work.' }
    if ($launchConditions.Count -eq 0) { $failures += 'No launch condition found.' }
    if ($media.Count -eq 0) { $failures += 'No media rows found; the MSI has no cabinet.' }

    $checks += "ProductName: $productName"
    $checks += "ProductVersion: $productVersion"
    $checks += "ProductCode: $productCode"
    $checks += "UpgradeCode: $upgradeCode"
    $checks += "ALLUSERS: $allUsers"
    $checks += "Shortcut rows: $($shortcuts.Count)"
    $checks += "File rows: $($files.Count)"
    $checks += "Upgrade rows: $($upgrades.Count)"
    $checks += "LaunchCondition rows: $($launchConditions.Count)"
    $checks += "Media rows: $($media.Count)"

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    $checks += "Signature: $($signature.Status)"

    $checks | ForEach-Object { Write-Host $_ }

    if ($failures.Count -gt 0)
    {
        Write-Error ($failures -join [Environment]::NewLine)
        exit 1
    }

    Write-Host "MSI validation passed: $Path"
}
finally
{
    [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($database)
    [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($installer)
}
