# Installation and packaging

## Installer technology

WinOps Toolkit uses a **WiX v5 MSI** package. WiX is restored from NuGet by the setup project; no separate WiX installation is required.

Rationale:

- Native Windows Installer transactions, rollback and repair
- Reliable per-machine installation under `Program Files`
- Start menu and desktop shortcuts
- Clean uninstall through Windows Installer
- Deterministic major upgrades through a fixed `UpgradeCode`

The application is published **self-contained** for `win-x64`, so end users do not need a separate .NET 10 runtime installation.

## Prerequisites on target machines

- 64-bit Windows 10/11 or Windows Server with desktop experience
- Administrative rights for installation, upgrade and uninstall (per-machine MSI)
- PowerShell 7 recommended for the full PowerShell Center template set; Windows PowerShell 5.1 is used as a fallback where cmdlets are compatible
- Windows Defender/antivirus may need to allow unsigned administrative tooling during evaluation

## Build the package

```powershell
powershell.exe -NoProfile -File 'tools\Build\Build-Release.ps1' -Version 1.0.0
```

The script performs:

1. Release build of `WinOpsToolkit.slnx`
2. xUnit tests
3. Pester tests
4. Self-contained `win-x64` publish to `artifacts\publish\win-x64`
5. WiX MSI build
6. MSI database validation with `tools\Build\Test-Installer.ps1`
7. Final copy to `artifacts\installer\WinOpsToolkitSetup-<version>-x64.msi`

To skip tests during a rapid packaging iteration, add `-SkipTests`. To remove build caches first, add `-Clean`.

## Install

Interactive:

```powershell
msiexec.exe /i "artifacts\installer\WinOpsToolkitSetup-1.0.0-x64.msi"
```

Silent with log:

```powershell
msiexec.exe /i "artifacts\installer\WinOpsToolkitSetup-1.0.0-x64.msi" /qn ALLUSERS=1 /L*v "$env:TEMP\winops-install.log"
```

Installed files:

```text
C:\Program Files\WinOps\WinOps Toolkit\
```

Shortcuts:

- Start menu: **WinOps Toolkit**
- Start menu: **Uninstall WinOps Toolkit**
- Desktop: **WinOps Toolkit**

## Upgrade

Run the newer MSI directly. `MajorUpgrade` removes the previous version after `InstallInitialize` and installs the new version while preserving the fixed upgrade code:

```text
{8B4E7A2F-3C19-4D6E-9A5B-2F0D1C7E6A21}
```

Do not change this GUID for subsequent releases. Changing it causes side-by-side installs instead of upgrades.

Downgrades are blocked with a clear message.

## Repair

```powershell
msiexec.exe /f "artifacts\installer\WinOpsToolkitSetup-1.0.0-x64.msi" /qn /L*v "$env:TEMP\winops-repair.log"
```

## Uninstall

Apps & Features / Settings > Apps:

1. Search for **WinOps Toolkit**
2. Select **Uninstall**

Or from the command line:

```powershell
msiexec.exe /x "artifacts\installer\WinOpsToolkitSetup-1.0.0-x64.msi" /qn /L*v "$env:TEMP\winops-uninstall.log"
```

Application data under `%APPDATA%\WinOps Toolkit` and reports under `Documents\WinOps Toolkit` are deliberately preserved on uninstall. Delete them manually if required.

## Validation

The release script validates the MSI through the Windows Installer COM API:

- product name and version
- generated product code
- fixed upgrade code
- `ALLUSERS` per-machine scope
- Start menu and desktop shortcut rows
- payload file count and presence of `WinOps.Toolkit.exe`
- upgrade rows for major upgrades
- launch condition
- embedded media
- Authenticode status (reported, not required)

Run validation standalone:

```powershell
powershell.exe -NoProfile -File 'tools\Build\Test-Installer.ps1' -Path 'artifacts\installer\WinOpsToolkitSetup-1.0.0-x64.msi' -ExpectedVersion 1.0.0
```

## Signing for production

The generated MSI is not code-signed. Sign it with a trusted certificate before broad distribution. Use the repository script, which locates `signtool.exe`, applies a SHA256 signature with an RFC3161 timestamp, and verifies the result:

```powershell
powershell.exe -NoProfile -File 'tools\Build\Sign-Installer.ps1' -Path 'artifacts\installer\WinOpsToolkitSetup-1.0.0-x64.msi' -Thumbprint '<CERT-THUMBPRINT>' -OutputPath 'artifacts\installer\WinOpsToolkitSetup-1.0.0-x64-signed.msi'
```

From a PFX file:

```powershell
$secure = Read-Host -AsSecureString 'PFX password'
powershell.exe -NoProfile -File 'tools\Build\Sign-Installer.ps1' -Path 'artifacts\installer\WinOpsToolkitSetup-1.0.0-x64.msi' -PfxPath 'C:\Secure\winops-code-signing.pfx' -PfxPassword $secure -OutputPath 'artifacts\installer\WinOpsToolkitSetup-1.0.0-x64-signed.msi'
```

Verify with:

```powershell
Get-AuthenticodeSignature -LiteralPath 'artifacts\installer\WinOpsToolkitSetup-1.0.0-x64-signed.msi' | Format-List *
```

See `docs/ReleaseValidation.md` for the complete signing and release-validation checklist.

## Current verification status

Build-time MSI structural validation is automated. An actual interactive install, upgrade and uninstall cycle requires administrator privileges and should be performed on a clean VM before release; the development session is not elevated.
