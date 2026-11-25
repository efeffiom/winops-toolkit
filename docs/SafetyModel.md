# Safety model

WinOps Toolkit treats every state-changing operation as a controlled administrative action. The UI cannot directly run a mutation; it calls an application service, and that service submits the action to `ActionSafetyEngine`.

## Core rules

1. **Central classification.** `ActionCatalog` is the single source of truth for action IDs, titles, descriptions, risk levels, elevation requirements, confirmation requirements, rollback expectations, logging requirements and validation requirements.
2. **Fail-closed confirmation.** If a mutating action has no confirmation handler, the engine denies it and writes an audit record. The WPF app installs `ConfirmationDialogService` at startup, before any window is shown.
3. **No silent destructive operations.** Destructive and high-risk actions show a dialog with the action title, description, risk level, administrator requirement and rollback information.
4. **Least privilege by default.** The app manifest uses `asInvoker`. The app detects administrator status but does not auto-elevate. Actions that need elevation are denied with a clear message until the user restarts elevated.
5. **Cancellation and timeout.** All operations accept a `CancellationToken`; command execution kills the process tree on cancellation and supports explicit timeouts.
6. **Evidence and verification.** Commands capture stdout, stderr, exit code and duration. Actions with a verification delegate are marked unverified and failed if the post-condition cannot be confirmed.
7. **Audit everything.** Success, failure, cancellation, declined confirmation and elevation denial are recorded in the SQLite audit trail.

## Risk levels

| Level | Meaning | Confirmation | Examples |
| --- | --- | --- | --- |
| `Level0ReadOnly` | Diagnostic or inspection only | No | update scan, CIM queries, event log queries |
| `Level2Reversible` | Change with a practical recovery path | Yes | stop process, create restore point, flush DNS, enable user |
| `Level3HighRisk` | Can disrupt the machine or account | Yes | service state, adapter reset, password reset, security profile, remote script |
| `Level4Destructive` | Permanent or hard to reverse | Yes | temporary-file cleanup, disabling a security control |

## Representative action IDs

| Area | Action IDs |
| --- | --- |
| Processes/services/tasks | `process.stop`, `service.state`, `service.startup`, `task.state`, `task.run` |
| Network | `network.flushdns`, `network.registerdns`, `network.release`, `network.renew`, `network.adapter`, `network.winsock`, `network.tcpip`, `network.setdns`, `network.setstaticip`, `network.restoredhcp` |
| Maintenance | `update.scan`, `update.install`, `repair.sfc`, `repair.dism`, `repair.chkdsk`, `repair.general` |
| Security | `security.apply`, `security.disable` |
| Accounts | `user.create`, `user.enable`, `user.disable`, `user.password`, `user.group`, `user.bulk` |
| Active Directory | `ad.enable`, `ad.disable`, `ad.unlock`, `ad.password` |
| Storage | `storage.cleanup` |
| Automation/remote | `script.execute`, `remote.script`, `remote.command` |

## Confirmation dialog

The dialog displays:

- action title and risk level
- plain-language description
- administrator requirement
- rollback availability
- logging and validation requirements

The user must explicitly approve. Declining records `ConfirmationDeclined = true` and performs no work.

## Audit fields

`AuditEntry` records:

- UTC timestamp
- action ID and title
- risk level
- success/failure
- cancelled/declined flags
- verification result
- message and error
- executed command
- output
- affected paths/details where supplied
- duration and exit code

The audit trail is visible in **Settings & Audit** and can be exported to CSV.

## Rollback strategy

Where a native rollback exists, the action definition records `HasRollback` and the UI informs the user. Examples:

- DNS changes: original server list is captured before applying new values
- DHCP/static IP changes: `network.restoredhcp` provides a recovery path
- User/group changes: prior membership/state is available from the confirmation context
- Restore points: available before high-risk system changes, subject to Windows restore-point policy

Some operations have `HasRollback = false` by design: power state changes, update installation, SFC/DISM/CHKDSK and permanent file deletion. These require explicit approval and are documented as irreversible.

## Command execution safety

`CommandRunner`:

- uses `ProcessStartInfo.ArgumentList`, so arguments are not shell-concatenated
- never uses `cmd /c` with user strings
- redirects and captures stdout/stderr as UTF-8
- supports timeouts and process-tree cancellation
- records the executable and arguments in the result

PowerShell uses `-EncodedCommand` with base64 UTF-16LE, avoiding quoting injection. Parameters are validated as `Name='value'` assignments before being prepended to a script.

## Validation limits

The current non-elevated development environment can verify:

- elevation denial paths
- confirmation and cancellation paths
- audit persistence
- read-only diagnostics
- script execution with Windows PowerShell fallback

It cannot verify actual state changes for administrator-only actions. Those operations must be tested on an isolated VM or lab image before release.
