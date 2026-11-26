[CmdletBinding(DefaultParameterSetName = 'Thumbprint')]
param(
    [string]$Path,

    [string]$SignToolPath,

    [Parameter(ParameterSetName = 'Thumbprint')]
    [string]$Thumbprint,

    [Parameter(ParameterSetName = 'Pfx')]
    [string]$PfxPath,

    [Parameter(ParameterSetName = 'Pfx')]
    [SecureString]$PfxPassword,

    [string]$OutputPath,

    [string]$TimestampServer = 'http://timestamp.digicert.com',

    [ValidateSet('SHA256', 'SHA384', 'SHA512')]
    [string]$Algorithm = 'SHA256',

    [switch]$SkipTimestamp,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'

function Resolve-SignTool
{
    param([string]$ToolPath)

    if (-not [string]::IsNullOrWhiteSpace($ToolPath))
    {
        if (-not (Test-Path -LiteralPath $ToolPath))
        {
            throw "signtool.exe was not found at: $ToolPath"
        }
        return (Resolve-Path -LiteralPath $ToolPath).Path
    }

    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $kitRoots = @()
    if (${env:ProgramFiles(x86)}) { $kitRoots += (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin') }
    if ($env:ProgramFiles) { $kitRoots += (Join-Path $env:ProgramFiles 'Windows Kits\10\bin') }

    foreach ($kitRoot in $kitRoots)
    {
        if (-not (Test-Path -LiteralPath $kitRoot)) { continue }

        $candidates = Get-ChildItem -LiteralPath $kitRoot -Filter 'signtool.exe' -Recurse -File -ErrorAction SilentlyContinue
        if (-not $candidates) { continue }

        $preferred = $candidates |
            Where-Object { $_.FullName -match '\\x64\\' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($preferred) { return $preferred.FullName }

        $fallback = $candidates | Sort-Object FullName -Descending | Select-Object -First 1
        if ($fallback) { return $fallback.FullName }
    }

    throw 'signtool.exe was not found. Install the Windows SDK and retry.'
}

function Resolve-Certificate
{
    param(
        [string]$CertificateThumbprint,
        [string]$CertificatePfxPath,
        [SecureString]$Password
    )

    if ($CertificatePfxPath)
    {
        if (-not (Test-Path -LiteralPath $CertificatePfxPath))
        {
            throw "PFX file not found: $CertificatePfxPath"
        }

        if (-not $Password) { $Password = [SecureString]::new() }

        $imported = Import-PfxCertificate -FilePath $CertificatePfxPath -CertStoreLocation 'Cert:\CurrentUser\My' -Password $Password
        Write-Host "Imported signing certificate into CurrentUser\My: $($imported.Subject)"
        return $imported
    }

    if ([string]::IsNullOrWhiteSpace($CertificateThumbprint))
    {
        throw 'Specify -Thumbprint or -PfxPath, or omit both to select the newest MSI only.'
    }

    $normalized = ($CertificateThumbprint -replace '\s', '').ToUpperInvariant()
    $certificate = Get-ChildItem -LiteralPath 'Cert:\CurrentUser\My' -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -eq $normalized } |
        Select-Object -First 1

    if (-not $certificate)
    {
        $certificate = Get-ChildItem -LiteralPath 'Cert:\LocalMachine\My' -ErrorAction SilentlyContinue |
            Where-Object { $_.Thumbprint -eq $normalized } |
            Select-Object -First 1
    }

    if (-not $certificate)
    {
        throw "Certificate with thumbprint $normalized was not found in CurrentUser\My or LocalMachine\My."
    }

    return $certificate
}

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path

if ([string]::IsNullOrWhiteSpace($Path))
{
    $installerDir = Join-Path $root 'artifacts\installer'
    if (-not (Test-Path -LiteralPath $installerDir))
    {
        throw "No MSI found. Build a release first, or pass -Path explicitly. Expected folder: $installerDir"
    }

    $newest = Get-ChildItem -LiteralPath $installerDir -Filter '*.msi' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $newest)
    {
        throw "No MSI found in $installerDir. Pass -Path explicitly."
    }

    $Path = $newest.FullName
    Write-Host "No -Path supplied; using newest MSI: $Path"
}

if (-not (Test-Path -LiteralPath $Path))
{
    throw "MSI not found: $Path"
}

$source = (Resolve-Path -LiteralPath $Path).Path
if ([System.IO.Path]::GetExtension($source) -ine '.msi')
{
    Write-Warning "Target does not have an .msi extension: $source"
}

$target = $source
if ($OutputPath)
{
    $parent = Split-Path -Parent $OutputPath
    if ($parent -and -not (Test-Path -LiteralPath $parent))
    {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    Copy-Item -LiteralPath $source -Destination $OutputPath -Force
    $target = (Resolve-Path -LiteralPath $OutputPath).Path
}

$currentSignature = Get-AuthenticodeSignature -LiteralPath $target
if ($currentSignature.Status -eq [System.Management.Automation.SignatureStatus]::Valid -and -not $Force)
{
    Write-Warning "MSI is already validly signed: $target"
    Write-Warning 'Use -Force to replace the existing signature.'
    return
}

if (-not $Thumbprint -and -not $PfxPath)
{
    throw 'Specify -Thumbprint <thumbprint> or -PfxPath <file> to sign the MSI.'
}

$certificate = Resolve-Certificate -CertificateThumbprint $Thumbprint -CertificatePfxPath $PfxPath -Password $PfxPassword
if (-not $certificate.HasPrivateKey)
{
    throw "Certificate does not have a private key and cannot sign: $($certificate.Subject)"
}

$signTool = Resolve-SignTool -ToolPath $SignToolPath
$arguments = @('sign', '/fd', $Algorithm, '/sha1', ($certificate.Thumbprint -replace '\s', ''))
if (-not $SkipTimestamp)
{
    $arguments += @('/tr', $TimestampServer, '/td', $Algorithm)
}
$arguments += $target

Write-Host ''
Write-Host "Signing: $target"
Write-Host "Signer:  $($certificate.Subject)"
Write-Host "Hash:    $Algorithm"
if ($SkipTimestamp)
{
    Write-Host 'Timestamp: skipped'
}
else
{
    Write-Host "Timestamp: $TimestampServer"
}

& $signTool @arguments
if ($LASTEXITCODE -ne 0)
{
    throw "signtool.exe failed with exit code $LASTEXITCODE."
}

$signature = Get-AuthenticodeSignature -LiteralPath $target
if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid)
{
    throw "Signature verification failed: $($signature.Status). $($signature.StatusMessage)"
}

$timeStamper = $null
if ($signature.TimeStamperCertificate)
{
    $timeStamper = $signature.TimeStamperCertificate.Subject
}

Write-Host ''
Write-Host 'Signing complete.'
[pscustomobject]@{
    Path           = $target
    Status         = $signature.Status
    Signer         = $signature.SignerCertificate.Subject
    Thumbprint     = $signature.SignerCertificate.Thumbprint
    TimeStamper    = $timeStamper
} | Format-List
