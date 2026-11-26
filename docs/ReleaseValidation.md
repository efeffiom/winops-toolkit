# Release validation runbook

This runbook covers the gates that cannot be completed in the development session: an elevated clean-VM install/upgrade/uninstall cycle, interactive administrator-gated operation checks, UI verification, and Authenticode signing.

Do **not** run these checks on a production machine. Use a disposable VM and take a checkpoint before starting.

## Validation results - 2026-11-26

Tester: user at the VMware Windows 11 Pro VM console; snapshot `pre-winops-validation` retained.

- Phase 1 clean install of `1.0.0`: **pass** (exit 0, correct shortcuts and Apps entry, clean started log as administrator).
- Phase 4 upgrade `1.0.0` → `1.0.1`: **pass** (exit 0); saved scripts and endpoints preserved; downgrade to `1.0.0` blocked with exit 1603 and a clear message.
- Phase 5 repair, uninstall and reinstall of `1.0.1`: **pass** (all exit 0); user data preserved across uninstall.
- Final interactive pass on `1.0.3` (installed on the VM with exit 0; SHA256 matched the host build): **pass**. The tester confirmed all modules populate immediately, the selected navigation item is visible, light/dark themes are legible, and PowerShell Center loads templates and executes scripts with visible output. Tester statement: "everything works".
- Individual state-changing/high-risk operations (Phase 2) and the standard-user denial path were not separately evidenced with audit exports or post-condition screenshots in this session; those checklist rows remain open.
- Phase 6 Authenticode signing is blocked pending a code-signing certificate.

## Prerequisites

- Clean 64-bit Windows 11 Pro or Windows Server VM with desktop experience.
- An account with local administrator rights.
- A second, standard (non-admin) account for denial-path checks.
- Both release MSIs available on the VM:
  - current version, for example `WinOpsToolkitSetup-1.0.0-x64.msi`
  - successor version, for example `WinOpsToolkitSetup-1.0.1-x64.msi`
- A code-signing certificate and timestamp server for the signing gate.
- Optional: disposable AD domain/OU and an authorized lab endpoint for AD and remote-administration checks.

Build the MSIs on the development machine first:

```powershell
powershell.exe -NoProfile -File 'tools\Build\Build-Release.ps1' -Version 1.0.0
powershell.exe -NoProfile -File 'tools\Build\Build-Release.ps1' -Version 1.0.1
```

Create an evidence folder on the VM, for example `C:\WinOpsValidation\1.0.1`, and store MSI logs, screenshots, exported reports and the completed checklist there.

## Phase 1 - clean install

1. Create a VM checkpoint named `pre-winops-validation`.
2. From an elevated command prompt, install the current MSI with a verbose log:

   ```powershell
   msiexec.exe /i "C:\WinOpsValidation\WinOpsToolkitSetup-1.0.0-x64.msi" /qn ALLUSERS=1 /L*v "C:\WinOpsValidation\install-1.0.0.log"
   ```

3. Confirm exit code `0` and that the log contains no `error 1...`/`error 2...` installation failures.
4. Verify:
   - Application files exist under `C:\Program Files\WinOps\WinOps Toolkit\`
   - Start menu shortcut **WinOps Toolkit** is present
   - Start menu shortcut **Uninstall WinOps Toolkit** is present
   - Desktop shortcut **WinOps Toolkit** is present
   - **WinOps Toolkit** appears in Settings > Apps with version `1.0.0`
5. Launch the app from the desktop shortcut and from the Start menu shortcut.
6. Confirm the app starts, shows administrator privileges, and writes a clean `started` event to `%APPDATA%\WinOps Toolkit\Logs\`.
7. Run the MSI structural validator on the VM:

   ```powershell
   powershell.exe -NoProfile -File 'tools\Build\Test-Installer.ps1' -Path 'C:\WinOpsValidation\WinOpsToolkitSetup-1.0.0-x64.msi' -ExpectedVersion 1.0.0
   ```

## Phase 2 - administrator-gated operations

For every state-changing action below, verify the full safety contract:

1. The action is classified with the expected risk level.
2. A confirmation dialog appears and names the target.
3. Cancelling produces no change and no executed-action audit entry.
4. Confirming executes, reports success or a clear failure, and writes an audit entry.
5. The post-condition is checked on the VM.
6. Rollback information is available where the action supports it.

| Area | Validation checks |
| --- | --- |
| Dashboard | Live metrics populate; utilization chart renders and updates; power/restore-point actions show confirmation and audit entries. |
| Process Manager | Process list populates; stopping a disposable test process is confirmed, audited and verified; filter search works. |
| Services | Service list populates; start/stop of a disposable test service is confirmed, audited and verified. |
| Inventory | Startup apps, tasks, installed software, features, devices, drivers, environment variables, restore points and power state populate from the VM. |
| Network Toolkit | Adapters, routes, ARP and connections populate; ping, traceroute, DNS and port tests return real results; non-destructive configuration changes are tested only on the disposable VM. |
| Windows Maintenance | Update history and pending updates populate; create a restore point; run SFC, DISM and CHKDSK only on the VM; verify audit entries and post-conditions. |
| Security Center | Assessment populates; preview a profile; only apply a profile if the VM checkpoint has been verified. |
| Local Users | Create a test user; enable/disable; reset password; change group membership; run a bulk CSV operation using a disposable CSV; clean up test accounts. |
| Active Directory | Query users, groups and OUs in a disposable domain; test unlock/reset only on test accounts. |
| Storage | Disk/volume/folder scan and large-file search populate; review cleanup suggestions; run cleanup only after a checkpoint. |
| Event Logs | Log list and filtered queries return real events. |
| Remote Administration | Save an endpoint; test connectivity and collection against an authorized lab system; run only approved remote scripts. |
| PowerShell Center | Run a built-in read-only template; run a saved script; cancel a long-running script and confirm cancellation is honored. |
| Reports | Export JSON, CSV, HTML and PDF from a diagnostic run; open each file and confirm evidence is present. |

Run the standard-account pass separately:

1. Sign in as the non-admin account.
2. Open the same guarded actions.
3. Confirm elevation checks deny execution with a clear message and no silent state change.

## Phase 3 - interactive UI verification

- Switch between light and dark themes using the setting and `Ctrl+D`.
- Focus module search with `Ctrl+K`, refresh with `F5`, and clear search with `Esc`.
- Confirm the LiveCharts dashboard chart renders, updates with live polling, and remains readable in both themes.
- Resize the window and confirm layouts do not clip or overlap.
- Confirm navigation to all 16 modules works and no module shows placeholder data.

## Phase 4 - upgrade

1. With `1.0.0` installed, create distinctive state to verify preservation:
   - set `TechnicianName` and `Environment` in settings
   - save a PowerShell template
   - save a remote endpoint
2. Upgrade with the successor MSI:

   ```powershell
   msiexec.exe /i "C:\WinOpsValidation\WinOpsToolkitSetup-1.0.1-x64.msi" /qn /L*v "C:\WinOpsValidation\upgrade-1.0.1.log"
   ```

3. Confirm:
   - exit code `0`
   - only one **WinOps Toolkit** entry appears in Settings > Apps
   - the installed version is `1.0.1`
   - the fixed upgrade code is unchanged: `{8B4E7A2F-3C19-4D6E-9A5B-2F0D1C7E6A21}`
   - settings, saved scripts and endpoints were preserved
   - the app launches cleanly after upgrade
4. Attempt to reinstall `1.0.0` over `1.0.1` and confirm downgrade is blocked with a clear message.

## Phase 5 - repair, uninstall and reinstall

1. Repair the installed package:

   ```powershell
   msiexec.exe /f "C:\WinOpsValidation\WinOpsToolkitSetup-1.0.1-x64.msi" /qn /L*v "C:\WinOpsValidation\repair-1.0.1.log"
   ```

2. Confirm the app launches after repair.
3. Uninstall:

   ```powershell
   msiexec.exe /x "C:\WinOpsValidation\WinOpsToolkitSetup-1.0.1-x64.msi" /qn /L*v "C:\WinOpsValidation\uninstall-1.0.1.log"
   ```

4. Confirm:
   - the app entry is gone from Settings > Apps
   - installed files and all three shortcuts are removed
   - `%APPDATA%\WinOps Toolkit` and `Documents\WinOps Toolkit` are preserved
5. Reinstall `1.0.1` and confirm a clean first-run experience with preserved user data.

## Phase 6 - Authenticode signing

Sign the release MSI on a machine that has the code-signing certificate and the Windows SDK `signtool.exe`:

```powershell
powershell.exe -NoProfile -File 'tools\Build\Sign-Installer.ps1' -Path 'artifacts\installer\WinOpsToolkitSetup-1.0.1-x64.msi' -Thumbprint '<CERT-THUMBPRINT>' -OutputPath 'artifacts\installer\WinOpsToolkitSetup-1.0.1-x64-signed.msi'
```

Or from a PFX file:

```powershell
$secure = Read-Host -AsSecureString 'PFX password'
powershell.exe -NoProfile -File 'tools\Build\Sign-Installer.ps1' -Path 'artifacts\installer\WinOpsToolkitSetup-1.0.1-x64.msi' -PfxPath 'C:\Secure\winops-code-signing.pfx' -PfxPassword $secure -OutputPath 'artifacts\installer\WinOpsToolkitSetup-1.0.1-x64-signed.msi'
```

Then verify:

```powershell
Get-AuthenticodeSignature -LiteralPath 'artifacts\installer\WinOpsToolkitSetup-1.0.1-x64-signed.msi' | Format-List *
powershell.exe -NoProfile -File 'tools\Build\Test-Installer.ps1' -Path 'artifacts\installer\WinOpsToolkitSetup-1.0.1-x64-signed.msi' -ExpectedVersion 1.0.1
```

Accept the signed package only when `Get-AuthenticodeSignature` reports `Valid`, the signer chain is trusted, and the timestamp certificate is present.

## Evidence checklist

| Item | Evidence | Pass / Fail | Notes |
| --- | --- | --- | --- |
| Clean install | `install-1.0.0.log`, shortcuts and Apps entry verified on VM | Pass | Also main executable launched as administrator with a clean log. |
| Admin operation pass | Audit export, post-condition screenshots | Open | Modules and guarded actions confirmed working interactively; per-action post-condition evidence not captured separately. |
| Standard-user denial pass | Screenshot of denial message | Open | Standard-user startup was logged, but the denial path was not separately evidenced. |
| Dashboard/chart/themes | VM UI pass on `1.0.3`, light and dark themes | Pass | Tester confirmed themes legible and selected navigation indicator visible. |
| Reports | JSON, CSV, HTML and PDF exports | Open | Report code path is implemented and unit-tested; exports not individually collected on the VM. |
| Upgrade | `upgrade-1.0.1.log`, Apps entry showing `1.0.1` | Pass | Settings, saved scripts and endpoints preserved. |
| Downgrade blocked | `downgrade-block.log` | Pass | Exit 1603 with "A newer version ... is already installed". |
| Repair | `repair-1.0.1.log` | Pass | Exit 0. |
| Uninstall | `uninstall-1.0.1.log`, shortcut/file absence | Pass | Exit 0; user data preserved. |
| Reinstall | `reinstall-1.0.1.log` | Pass | Exit 0; all install artifacts restored. |
| PowerShell Center / nav / dark mode | VM UI pass on `1.0.3`, host UI Automation | Pass | Templates load, scripts execute with visible output and audit entries; three reported GUI defects fixed. |
| Signature | `Get-AuthenticodeSignature` output | Blocked | No code-signing certificate provided. |
| Post-sign MSI validation | Validator output | Blocked | Depends on signing gate. |

## Sign-off

Only after every checklist item passes:

1. Record the VM OS build, test date, tester, MSI versions and signing certificate thumbprint.
2. Update `PROJECT_STATUS.md` with the verified version and remove the corresponding gate from **Remaining work / limitations**.
3. Keep the evidence folder with the release record.
