[CmdletBinding()]
param(
    [string]$Version = '1.0.0',
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [switch]$SkipTests,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

function Resolve-DotNet
{
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    foreach ($candidate in @('C:\Program Files\dotnet\dotnet.exe', 'C:\Program Files (x86)\dotnet\dotnet.exe'))
    {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    throw 'The .NET SDK was not found. Install .NET 10 SDK or add dotnet.exe to PATH.'
}

function Resolve-PowerShell7
{
    $command = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $candidate = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\pwsh.exe'
    if (Test-Path -LiteralPath $candidate) { return $candidate }
    throw 'PowerShell 7 (pwsh.exe) was not found. It is required for Pester and icon generation.'
}

function Invoke-Tool
{
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ToolArguments = @(),
        [Parameter(Mandatory)][string]$Description
    )

    Write-Host ''
    Write-Host "== $Description =="
    & $FilePath @ToolArguments
    if ($LASTEXITCODE -ne 0)
    {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$dotnet = Resolve-DotNet
$pwsh = Resolve-PowerShell7
$solution = Join-Path $root 'WinOpsToolkit.slnx'
$testProject = Join-Path $root 'tests\WinOps.Tests\WinOps.Tests.csproj'
$pesterRunner = Join-Path $root 'tools\Pester\Run-Pester.ps1'
$appProject = Join-Path $root 'src\WinOps.App\WinOps.App.csproj'
$wixProject = Join-Path $root 'installer\WinOps.Toolkit.Setup\WinOps.Toolkit.Setup.wixproj'
$iconPath = Join-Path $root 'installer\WinOps.Toolkit.Setup\WinOps.ico'
$iconScript = Join-Path $PSScriptRoot 'New-WinOpsIcon.ps1'
$validator = Join-Path $PSScriptRoot 'Test-Installer.ps1'

$artifacts = Join-Path $root 'artifacts'
$publishDir = Join-Path $artifacts 'publish\win-x64'
$installerDir = Join-Path $artifacts 'installer'
$builtMsi = Join-Path $root "installer\WinOps.Toolkit.Setup\bin\$Configuration\WinOpsToolkitSetup.msi"
$finalMsi = Join-Path $installerDir "WinOpsToolkitSetup-$Version-x64.msi"

if ($Clean)
{
    foreach ($directory in @((Join-Path $root 'artifacts'), (Join-Path $root 'installer\WinOps.Toolkit.Setup\bin'), (Join-Path $root 'installer\WinOps.Toolkit.Setup\obj')))
    {
        if (Test-Path -LiteralPath $directory) { Remove-Item -LiteralPath $directory -Recurse -Force }
    }
}

New-Item -ItemType Directory -Force -Path $publishDir, $installerDir | Out-Null
Get-ChildItem -LiteralPath $publishDir -Force | Remove-Item -Recurse -Force

Invoke-Tool -FilePath $dotnet -ToolArguments @('build', $solution, '-c', $Configuration, '-v:minimal') -Description 'Build solution'

if (-not $SkipTests)
{
    Invoke-Tool -FilePath $dotnet -ToolArguments @('test', $testProject, '-c', $Configuration, '--no-build', '-v:minimal') -Description 'Run xUnit tests'
    Invoke-Tool -FilePath $pwsh -ToolArguments @('-NoProfile', '-File', $pesterRunner) -Description 'Run Pester tests'
}

Invoke-Tool -FilePath $dotnet -ToolArguments @(
    'publish', $appProject,
    '-c', $Configuration,
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishSingleFile=false',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    "-p:Version=$Version",
    '-o', $publishDir,
    '-v:minimal'
) -Description 'Publish self-contained win-x64 application'

if (-not (Test-Path -LiteralPath $iconPath))
{
    Invoke-Tool -FilePath $pwsh -ToolArguments @('-NoProfile', '-File', $iconScript, '-Path', $iconPath) -Description 'Generate installer icon'
}

try
{
    Invoke-Tool -FilePath $dotnet -ToolArguments @('restore', $wixProject, '--disable-parallel', '-v:minimal') -Description 'Restore WiX toolset'
}
catch
{
    Write-Warning "First WiX restore failed; retrying once. $($_.Exception.Message)"
    Invoke-Tool -FilePath $dotnet -ToolArguments @('restore', $wixProject, '--disable-parallel', '-v:minimal') -Description 'Restore WiX toolset (retry)'
}

Invoke-Tool -FilePath $dotnet -ToolArguments @(
    'build', $wixProject,
    '-c', $Configuration,
    '--no-restore',
    "-p:ProductVersion=$Version",
    '-v:minimal'
) -Description 'Build MSI installer'

if (-not (Test-Path -LiteralPath $builtMsi))
{
    throw "Expected MSI was not produced: $builtMsi"
}

Copy-Item -LiteralPath $builtMsi -Destination $finalMsi -Force
Invoke-Tool -FilePath $pwsh -ToolArguments @('-NoProfile', '-File', $validator, '-Path', $finalMsi, '-ExpectedVersion', $Version) -Description 'Validate MSI'

Write-Host ''
Write-Host 'Release build complete.'
Write-Host "MSI: $finalMsi"
Write-Host "Publish: $publishDir"
