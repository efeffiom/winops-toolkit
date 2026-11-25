# Testing guide

WinOps Toolkit uses three complementary test layers: xUnit for managed logic, Pester for PowerShell/script contracts, and a desktop smoke test for the real WPF executable.

## 1. xUnit tests

Project: `tests/WinOps.Tests`

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test 'tests\WinOps.Tests\WinOps.Tests.csproj' -c Release -v:minimal
```

Coverage includes:

- `ActionCatalog` risk levels, confirmation and administrator requirements
- `ActionSafetyEngine` denial, cancellation, success and audit behavior
- PowerShell parameter injection validation
- Command runner argument handling and timeout/cancellation paths where portable
- Diagnostic scenario registration and evidence shape
- Report exporter output for JSON, CSV, HTML and PDF
- Repository/settings persistence with temporary SQLite databases

Current result: **20 passed, 0 failed, 0 skipped**.

## 2. Pester tests

Script: `tools/Pester/Run-Pester.ps1`
Tests: `tools/Pester/WinOps.Snapshot.Tests.ps1`

Prerequisite: PowerShell 7 and Pester 5+ (`Install-Module Pester -Scope CurrentUser`).

```powershell
& 'C:\Users\efeffiom\AppData\Local\Microsoft\WindowsApps\pwsh.exe' -NoProfile -File 'tools\Pester\Run-Pester.ps1'
```

The tests verify script-template syntax, PowerShell 7/5.1 command compatibility boundaries and report output contracts without mutating the machine.

Current result: **3 passed, 0 failed, 0 skipped**.

## 3. Desktop smoke test

Script: `tools/Build/Smoke-Test.ps1`

```powershell
& 'C:\Users\efeffiom\AppData\Local\Microsoft\WindowsApps\pwsh.exe' -NoProfile -File 'tools\Build\Smoke-Test.ps1'
```

The script:

1. Launches `artifacts\publish\win-x64\WinOps.Toolkit.exe`
2. Waits 12 seconds (configurable with `-Seconds`)
3. Verifies the process stayed alive
4. Checks the new Serilog entries for a clean `started` event
5. Fails if `Unhandled` or `Failed:` appears
6. Closes the window and, if needed, kills the process tree

The smoke test caught and verified the fix for a WPF hosting bug where `Page` instances were placed in a `ContentControl`; the shell now hosts pages in a `Frame`.

## 4. MSI validation

Script: `tools/Build/Test-Installer.ps1`

```powershell
& 'C:\Users\efeffiom\AppData\Local\Microsoft\WindowsApps\pwsh.exe' -NoProfile -File 'tools\Build\Test-Installer.ps1' -Path 'artifacts\installer\WinOpsToolkitSetup-1.0.0-x64.msi' -ExpectedVersion 1.0.0
```

Uses the Windows Installer COM database API to verify product identity, version, fixed upgrade code, per-machine scope, shortcuts, payload count, main executable, upgrade rows, launch condition and embedded media. Authenticode status is reported but not required.

## 5. End-to-end release build

```powershell
powershell.exe -NoProfile -File 'tools\Build\Build-Release.ps1' -Version 1.0.0
```

The script runs all managed and PowerShell tests, publishes the self-contained app, builds the MSI, copies it to `artifacts\installer` and validates it. Use `-SkipTests` only for rapid packaging iterations and `-Clean` to remove caches first.

## 6. Manual validation still required

The development workstation is not elevated, so these checks must be performed in an isolated VM before production release. See `docs/ReleaseValidation.md` for the full runbook, evidence checklist and signing procedure. In summary:

1. Install the MSI from an elevated prompt; verify Start menu and desktop shortcuts.
2. Launch the app and confirm it starts with administrator privileges.
3. Exercise every administrator-gated action and confirm the approval dialog, execution, audit entry and post-condition.
4. Test install → upgrade → uninstall → reinstall using successive MSI versions.
5. Test restore-point creation and rollback for high-risk maintenance actions.
6. Test AD operations against a disposable domain/OU.
7. Test remote administration only against authorized lab systems.
8. Sign the MSI with a trusted code-signing certificate and verify the signature.

Never run destructive cleanup, update installation, SFC/DISM/CHKDSK or security-profile changes on production systems as part of routine verification.
