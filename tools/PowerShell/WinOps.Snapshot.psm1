# WinOps Toolkit field snapshot helpers.
# Compatible with Windows PowerShell 5.1 and PowerShell 7+.

function Get-WinOpsSnapshot {
    [CmdletBinding()]
    param(
        [string]$ComputerName = $env:COMPUTERNAME
    )

    $os = Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop
    $processor = Get-CimInstance -ClassName Win32_Processor -ErrorAction SilentlyContinue | Select-Object -First 1
    $adapters = @(Get-CimInstance -ClassName Win32_NetworkAdapterConfiguration -Filter 'IPEnabled = True' -ErrorAction SilentlyContinue)

    [pscustomobject]@{
        ComputerName  = $ComputerName
        OsCaption     = $os.Caption
        OsVersion     = $os.Version
        LastBootTime  = $os.LastBootUpTime
        Processor     = if ($processor) { $processor.Name.Trim() } else { 'Unavailable' }
        MemoryGb      = [math]::Round($os.TotalVisibleMemorySize / 1MB, 2)
        EnabledAdapters = $adapters.Count
        CollectedAt   = [datetime]::Now
    }
}

function Export-WinOpsSnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ValueFromPipeline)]
        [psobject]$Snapshot,

        [Parameter(Mandatory)]
        [string]$Path,

        [ValidateSet('Json', 'Csv')]
        [string]$Format = 'Json'
    )

    process {
        $directory = Split-Path -Parent $Path
        if ($directory -and -not (Test-Path -LiteralPath $directory)) {
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
        }

        if ($Format -eq 'Csv') {
            if ($PSVersionTable.PSVersion.Major -ge 6) {
                $Snapshot | Export-Csv -LiteralPath $Path -Encoding UTF8
            }
            else {
                $Snapshot | Export-Csv -LiteralPath $Path -NoTypeInformation -Encoding UTF8
            }
        }
        else {
            $Snapshot | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Path -Encoding UTF8
        }

        Get-Item -LiteralPath $Path
    }
}

Export-ModuleMember -Function Get-WinOpsSnapshot, Export-WinOpsSnapshot
