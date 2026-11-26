# WinOps Toolkit - Project Status

Last updated: 2026-11-26

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
- Navigating to a module now invokes its `RefreshAsync`, so pages populate immediately instead of opening empty until `F5`.
- Sidebar navigation has a selected-state indicator (accent bar, selected background/text) bound through `NavigationItemViewModel.IsSelected`.
- Sidebar colors are theme resources instead of hardcoded hex values; button hover colors, ComboBox/ListBox items and DataGrid cells are theme-aware for dark mode.
- PowerShell Center shows execution summary and full stdout/stderr in a dedicated output pane and history column, with a Cancel command, and no longer overwrites the result with a refresh status.

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
- End-to-end `tools\Build\Build-Release.ps1 -Version 1.0.1`: **completed successfully**; xUnit 20/20, Pester 3/3, MSI build 0 warnings/0 errors, MSI validation passed, `WinOpsToolkitSetup-1.0.1-x64.msi` produced.
- Elevated VMware Windows 11 Pro VM validation, with snapshot `pre-winops-validation`: clean install of `1.0.0` **passed** (exit 0), correct registry entry/version, install folder, both Start menu shortcuts and desktop shortcut verified; installed executable launched as administrator and logged `WinOps Toolkit 1.0.0.0 started (administrator: true)` with no errors.
- VM upgrade `1.0.0` → `1.0.1` **passed** (exit 0), version and product code updated, fixed UpgradeCode unchanged, saved scripts/endpoints markers preserved. Downgrade attempt with `1.0.0` was **blocked** with exit 1603 and the message `A newer version of WinOps Toolkit is already installed. Setup will now exit.`
- VM repair, uninstall and reinstall of `1.0.1` **passed** (all exit 0). After uninstall the entry, folder, executable and shortcuts were absent while user data remained; after reinstall all install artifacts were present with version `1.0.1`.
- MSI structural validation **passed on the VM copy** of `1.0.1` with the same product identity, fixed UpgradeCode, 3 shortcuts, 451 file rows and `NotSigned` status.
- End-to-end `tools\Build\Build-Release.ps1 -Version 1.0.2`: **completed successfully** (xUnit 20/20, Pester 3/3, MSI validation passed). The navigation auto-refresh fix was verified on the host by UI Automation: Process Manager populated `303 processes loaded` immediately after navigation, without `F5`. `1.0.2` was deployed to the VM (upgrade exit 0).
- Three VM GUI defects reported after `1.0.2` were fixed and built as `1.0.3`: end-to-end build **passed** (xUnit 20/20, Pester 3/3, MSI 0 warnings/0 errors, MSI validation passed at version `1.0.3`).
- Host UI Automation verification of `1.0.3`: the selected navigation row renders `#2563EB` (`NavSelectedBrush`) and reverts to the nav background when another item is selected; dark mode renders the theme surface `#172033` with all DynamicResource keys resolved; PowerShell Center loads 15 templates, loads a template into the editor, executes after the Level 3 confirmation, shows `Exit code 0 in 4s - Script completed successfully.`, displays script output, writes `script-history.json` and increments the audit count.
- Final interactive VM pass on `1.0.3`: tester confirmed **"everything works"** - all 16 modules populate immediately, navigation indicator visible, light/dark themes legible, PowerShell Center functional. Recorded in `docs/ReleaseValidation.md`.

## Remaining work / limitations

- The elevated VM installer lifecycle and the final interactive GUI pass on `1.0.3` are verified and confirmed by the tester.
- Individual state-changing/high-risk operations (`update.install`, `repair.sfc`, `repair.dism`, `repair.chkdsk`, cleanup, security profile application, remote scripts) and the standard-user denial path were not separately evidenced with audit exports or post-condition screenshots; those checklist rows remain open in `docs/ReleaseValidation.md`.
- Exported report artifacts (JSON, CSV, HTML, PDF) were not individually collected during the VM pass, although the report service is implemented and unit-tested.
- The MSI is **not Authenticode-signed**. Sign it with a trusted certificate before broad deployment.
- NU1701 compatibility warnings remain for `OpenTK 3.3.1`, `OpenTK.GLWpfControl 3.3.0` and `SkiaSharp.Views.WPF 3.119.2`. Runtime startup is verified; full chart interaction should be exercised during manual VM validation.
- PowerShell 7 is recommended. Windows PowerShell 5.1 is used only as a fallback and does not support every modern cmdlet used by built-in templates.
- Running the WPF app requires an interactive desktop session.
- The private GitHub repository contains the pushed `main` branch through the `1.0.3` GUI fixes; the remaining gate is code signing (plus optional per-action VM evidence).

## Next validation gate

1. Sign `WinOpsToolkitSetup-1.0.3-x64.msi` with `tools\Build\Sign-Installer.ps1` using a trusted code-signing certificate, then verify with `Get-AuthenticodeSignature` and `Test-Installer.ps1`.
2. Optionally close the remaining evidence rows in `docs/ReleaseValidation.md` by capturing audit exports/post-conditions for high-risk actions, standard-user denial and report exports on the snapshot-protected VM.
