# WinOps Toolkit - Project Status

Last updated: 2026-11-25

## Objective

Build a real, installable, production-quality native Windows administration and troubleshooting desktop application. No frontend-only mockups, fake data, dead buttons, or placeholder modules.

## Environment

- Windows 11 Pro (x64); current shell/session is **not elevated**.
- .NET SDK 10.0.404 installed; use `& 'C:\Program Files\dotnet\dotnet.exe'` because `dotnet` is not reliably on the session PATH.
- PowerShell 7 and Windows PowerShell 5.1 are both present. Pester tests run under PowerShell 7.
- Local Git repository initialized. The private GitHub repository is `https://github.com/efeffiom/winops-toolkit`; branch `main` tracks `origin/main` and commits `5fa555e` and `2b22f0c` have been pushed.
- LiveCharts2 2.2.0 is retained for WPF charting. NuGet emits NU1701 warnings for legacy transitive WPF/SkiaSharp packages; runtime smoke testing is used to confirm compatibility.

## Implemented

### Foundation

- Solution: `WinOpsToolkit.slnx` with `WinOps.Core`, `WinOps.Windows`, `WinOps.PowerShell`, `WinOps.Persistence`, `WinOps.Services`, `WinOps.App`, `tests/WinOps.Tests`. Installer project is intentionally separate: `installer/WinOps.Toolkit.Setup`.
- Domain models and service abstractions for all planned modules.
- Central `ActionSafetyEngine` and `ActionCatalog`: risk classification, elevation checks, explicit confirmation, cancellation, verification hooks, fail-closed confirmation, and SQLite audit logging.
- Serilog file logging under `%APPDATA%\WinOps Toolkit\Logs`.
- EF Core SQLite persistence for settings, audit trail and saved diagnostic runs.
- Process-based PowerShell execution using `pwsh.exe -EncodedCommand`, built-in templates, saved scripts, execution history and validated parameter injection. Windows PowerShell 5.1 fallback is available.
- Shared versioning through `Directory.Build.props` (`Version=1.0.0`).

### Windows integrations

- Native command execution helper, privilege detection, CIM/WMI queries, system information and live metrics.
- Process management; service management; inventory of startup apps, tasks, software, features, devices, drivers, environment variables, restore points and power state.
- Network adapters, routes, ARP, connections, Wi-Fi, ping, traceroute, DNS, port tests, public IP, DNS/IP/DHCP/adapter/Winsock/TCP-IP changes.
- Windows Update history, pending updates, scan/install, SFC, DISM, CHKDSK and targeted print/search/audio/network repairs.
- Security assessment, profiles, previews and guarded profile application.
- Local user/group management including create, enable/disable, password reset, group membership and bulk CSV operations.
- Active Directory domain info, user/group/OU queries, account state, unlock, password reset and domain diagnostics.
- Storage disks, volumes, folder analysis, large-file search, cleanup suggestions and guarded cleanup.
- Event log listing and filtered querying.
- Remote endpoint persistence, connectivity tests, remote system/service/process/event collection and remote script execution.
- Evidence-based diagnostic scenarios and JSON, CSV, HTML and basic PDF report export.

### WPF application shell

- Material-inspired light/dark theme resources and reusable styles.
- DI bootstrap in `App.xaml.cs`, global exception handling, theme service, navigation service, confirmation dialog service.
- All 16 module viewmodels and views, plus `MainWindow`, `ConfirmWindow` and `RestorePointDialog`.
- Pages are hosted in a WPF `Frame` (a `ContentControl` caused `Page can have only Window or Frame as parent` and was fixed).
- LiveCharts2 dashboard utilization chart, live metric polling, system details and guarded power/restore-point actions.
- Reusable converters for visibility, byte formatting and list formatting.
- Global keyboard shortcuts: `Ctrl+K` search focus, `Ctrl+D` theme toggle, `F5` refresh, `Esc` clear search.
- Fixed process filter refresh bug and removed `CS0108` refresh-method warnings by renaming the shell hook to `RefreshCurrentAsync()`.

### Installer and tooling

- WiX v5 per-machine MSI with fixed UpgradeCode `{8B4E7A2F-3C19-4D6E-9A5B-2F0D1C7E6A21}`, `MajorUpgrade`, launch condition, Start menu and desktop shortcuts.
- Self-contained `win-x64` publish harvested into the MSI; no separate .NET runtime required on target machines.
- `tools/Build/Build-Release.ps1`: end-to-end build, tests, publish, MSI build, MSI validation and artifact copy.
- `tools/Build/Test-Installer.ps1`: Windows Installer COM validator for product identity, version, upgrade code, `ALLUSERS`, shortcuts, payload count, main executable, upgrade rows, launch condition and media.
- `tools/Build/Smoke-Test.ps1`: real WPF executable launch/crash/log verification.
- `tools/Build/New-WinOpsIcon.ps1`: generated installer icon.
- `tools/Build/Sign-Installer.ps1`: locates `signtool.exe`, applies a SHA256 Authenticode signature with RFC3161 timestamping, optionally signs a copy, and verifies the result.

### Documentation

- `README.md`
- `docs/Architecture.md`
- `docs/Installation.md`
- `docs/SafetyModel.md`
- `docs/Operations.md`
- `docs/Testing.md`
- `docs/ReleaseValidation.md`
- This `PROJECT_STATUS.md`

## Verification results

- `dotnet build WinOpsToolkit.slnx -c Release`: **succeeded**, 0 errors, 9 NU1701 warnings from LiveCharts2 transitive packages; no `CS0108` warnings remain.
- xUnit (`tests/WinOps.Tests`): **20 passed, 0 failed, 0 skipped**.
- Pester (`tools/Pester/Run-Pester.ps1`): **3 passed, 0 failed, 0 skipped**.
- WiX MSI build: **succeeded**, 0 warnings, 0 errors.
- MSI COM validation: **passed**. ProductName `WinOps Toolkit`, version `1.0.0`, per-machine `ALLUSERS=1`, fixed UpgradeCode verified, 3 shortcuts, 451 file rows, 2 upgrade rows, 2 launch conditions, 2 media rows, main executable present, signature reported as `NotSigned`.
- Desktop smoke test: **passed** twice, including after the final scripted publish. The published executable stayed running and logged a clean `started` event with no unhandled exceptions.
- End-to-end `tools\Build\Build-Release.ps1 -Version 1.0.0`: **completed successfully**. It built the solution, ran xUnit (20/20) and Pester (3/3), published the self-contained app, built the MSI with 0 warnings/0 errors, copied it to `artifacts\installer\WinOpsToolkitSetup-1.0.0-x64.msi` and validated it.
- The WPF `Page`-in-`ContentControl` startup crash was found by the smoke test, fixed by hosting pages in a `Frame`, and re-verified with a clean smoke test.
- `tools\Build\Sign-Installer.ps1`: PowerShell parser syntax check passed and the missing-certificate error path was exercised; actual signing was not performed because no code-signing certificate is available in this session.

## Remaining work / limitations

- Administrator-only and destructive actions cannot be fully verified in the current non-elevated session; elevation-denial paths are expected and were exercised where applicable.
- A real interactive install → upgrade → uninstall cycle still requires an elevated clean VM.
- High-risk operations (`update.install`, `repair.sfc`, `repair.dism`, `repair.chkdsk`, cleanup, security profile application, remote scripts) require isolated VM validation before release claims.
- The MSI is **not Authenticode-signed**. Sign it with a trusted certificate before broad deployment.
- NU1701 compatibility warnings remain for `OpenTK 3.3.1`, `OpenTK.GLWpfControl 3.3.0` and `SkiaSharp.Views.WPF 3.119.2`. Runtime startup is verified; full chart interaction should be exercised during manual VM validation.
- PowerShell 7 is recommended. Windows PowerShell 5.1 is used only as a fallback and does not support every modern cmdlet used by built-in templates.
- Running the WPF app requires an interactive desktop session.
- The private GitHub repository contains the pushed `main` branch; the release gates below still require an elevated clean VM and a signing certificate.

## Next validation gate

1. Follow `docs/ReleaseValidation.md` on an elevated clean VM: install `WinOpsToolkitSetup-1.0.0-x64.msi`, run as administrator, exercise all guarded actions, upgrade to a newer version, repair, uninstall and reinstall.
2. Sign the MSI with `tools\Build\Sign-Installer.ps1` using a trusted code-signing certificate, then verify with `Get-AuthenticodeSignature` and `Test-Installer.ps1`.
3. Exercise the LiveCharts dashboard, theme switching and keyboard shortcuts interactively during that VM pass and attach the evidence checklist to the release record.
