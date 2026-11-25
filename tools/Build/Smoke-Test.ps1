[CmdletBinding()]
param(
    [string]$Executable,
    [ValidateRange(2, 120)]
    [int]$Seconds = 12
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Executable))
{
    $root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
    $Executable = Join-Path $root 'artifacts\publish\win-x64\WinOps.Toolkit.exe'
}

if (-not (Test-Path -LiteralPath $Executable))
{
    throw "Executable not found: $Executable"
}

$logDirectory = Join-Path $env:APPDATA 'WinOps Toolkit\Logs'
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$existingLog = Get-ChildItem -LiteralPath $logDirectory -Filter 'winops-*.log' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
$startOffset = if ($existingLog) { (Get-Item -LiteralPath $existingLog.FullName).Length } else { 0 }

Write-Host "Starting smoke test: $Executable"
$process = Start-Process -FilePath $Executable -PassThru

try
{
    Start-Sleep -Seconds $Seconds

    if ($process.HasExited)
    {
        throw "Application exited early with code $($process.ExitCode)."
    }

    $logFile = Get-ChildItem -LiteralPath $logDirectory -Filter 'winops-*.log' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $logFile)
    {
        throw 'No startup log file was created.'
    }

    $allText = Get-Content -LiteralPath $logFile.FullName -Raw
    $newText = if ($allText.Length -gt $startOffset) { $allText.Substring($startOffset) } else { '' }

    if ($newText -notmatch 'started')
    {
        throw 'Startup log does not contain a started event.'
    }

    if ($newText -match 'Unhandled|Failed:')
    {
        throw "Startup log contains an error:$([Environment]::NewLine)$newText"
    }

    Write-Host 'Application started, remained running and logged a clean startup.'
    Write-Host '--- new log entries ---'
    Write-Host $newText.Trim()
}
finally
{
    if (-not $process.HasExited)
    {
        [void]$process.CloseMainWindow()
        if (-not $process.WaitForExit(5000))
        {
            $process.Kill($true)
        }
    }
    $process.Dispose()
}
