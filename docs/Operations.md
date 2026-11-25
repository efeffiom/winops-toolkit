# Operations guide

## Starting the application

Run the Start menu shortcut **WinOps Toolkit**. The app starts with the current user's privileges and displays **Administrator** or **Standard user** in the header.

Use **File → Run as administrator** equivalent by right-clicking the shortcut and selecting **Run as administrator** when you intend to perform administrative actions. The application does not auto-elevate.

## First-run behavior

On first launch the app creates:

```text
%APPDATA%\WinOps Toolkit\
  winops.db
  scripts.json          (created when a template is saved)
  script-history.json   (created after the first script execution)
  endpoints.json        (created when a remote endpoint is saved)
  Logs\winops-yyyyMMdd.log
```

Reports are written to `Documents\WinOps Toolkit`.

## Settings & Audit

The **Settings & Audit** module provides:

- technician name and environment label used to annotate reports
- dashboard refresh interval in seconds
- light/dark theme selection
- version, current user, privilege level, data directory and database path
- audit trail viewer with CSV export
- detected capability list
- **Open data folder** shortcut

## Dashboard

The dashboard polls real system information on the configured interval:

- computer, OS, build, architecture, manufacturer/model/BIOS/serial where available
- CPU, memory, disks, adapters and IP configuration
- uptime and last boot
- administrator, Defender, firewall and update status where accessible
- CPU/memory utilization chart

Unavailable data is shown as unavailable; it is never reported as zero.

## System administration

- **Process Manager**: search, sort and stop processes. Critical system processes are protected by the safety engine confirmation path.
- **Services**: start/stop/restart and change startup type. Use the export function for inventories.
- **Inventory**: startup apps, scheduled tasks, installed software, Windows features, devices, drivers, environment variables, restore points and power state.
- Restore point creation prompts for a description and is routed through `restorepoint.create`.

## Network toolkit

Read-only diagnostics include adapters, routes, ARP, active connections, Wi-Fi, ping, traceroute, DNS lookup, port tests and public IP.

Configuration actions include flush/register DNS, release/renew DHCP, enable/disable adapter, set DNS, static IP, restore DHCP, reset Winsock and reset TCP/IP. Each action explains the connectivity impact before approval.

## Windows maintenance

- View Windows Update history, pending updates and failed updates
- Scan for updates; install only through `update.install`
- Run SFC, DISM, CHKDSK and targeted repairs for print spooler, search, audio and network components
- Every repair captures stdout/stderr, exit code and duration

## Security center

The security module assesses Defender, firewall, BitLocker, UAC, SmokeStack/EDR signals account policy and exposed services where available. Security profiles are previewed before application. `security.disable` requires explicit destructive-action confirmation.

## Local users and Active Directory

Local user operations: create, enable, disable, password reset, group membership and CSV bulk changes.

AD operations require domain connectivity and RSAT-capable cmdlets: domain/user/group/OU queries, enable/disable, unlock and password reset. Domain controllers and privileged accounts should be changed only through approved change processes.

## Storage and event logs

Storage provides disk/volume inventory, folder size analysis, large-file search and cleanup suggestions. Cleanup is destructive and limited to explicit approved paths.

Event Logs provides log listing and filtered queries by log name, level, ID, provider, keyword and time range.

## PowerShell Center

- Built-in templates cover networking, services, disk, Defender, firewall, AD, updates, events, boot configuration, licensing, scheduled tasks and process trees.
- Saved templates are stored in `scripts.json`.
- Parameters use `Name=value` syntax and are injected as validated string variables.
- Execution history stores the last 200 runs with output, error and exit code.
- `PowerShell 7+` is reported when `pwsh.exe` is found; otherwise the app falls back to Windows PowerShell 5.1.

Review the full script in the confirmation dialog before approving. Scripts run with the current user's privileges.

## Remote administration

Save endpoints with a friendly name and hostname. Connectivity, system info, services, processes and event logs can be collected from authorized remote systems. Remote script execution is `remote.script` and requires explicit confirmation; use only on systems you are authorized to administer.

## Troubleshooting engine

Evidence-based scenarios collect related facts from event logs, services, network state, storage and system configuration. Each scenario produces structured findings and recommendations rather than speculative guesses. Runs can be saved and exported.

## Reports

The Reports module exports diagnostic runs to:

- JSON (structured evidence)
- CSV (flat findings)
- HTML (human-readable)
- PDF (basic printable summary)

Reports include technician and environment labels from settings.

## Logs and diagnostics

If an operation fails:

1. Read the status bar and module error text.
2. Check **Settings & Audit** for the action result, command and output.
3. Open `%APPDATA%\WinOps Toolkit\Logs`.
4. Verify whether the action requires elevation.
5. Re-run the read-only diagnosis before retrying a change.

## Safe operational practice

- Take a restore point before batches of high-risk changes.
- Test configuration changes on one non-critical machine first.
- Keep the audit CSV as part of the change record.
- Do not use cleanup, SFC/DISM/CHKDSK, update installation or security profile application on production systems without a maintenance window and rollback plan.
