# Architecture

WinOps Toolkit is a layered WPF application. The goal is to keep UI code free of PowerShell strings, Windows API calls and safety policy.

## Solution layout

| Project | Target | Responsibility |
| --- | --- | --- |
| `WinOps.Core` | `net10.0` | Domain models, service interfaces, safety engine, diagnostic engine, report export |
| `WinOps.Windows` | `net10.0-windows` | Command execution, privileges, CIM/WMI, processes, services, network, maintenance, security, accounts, AD, storage, event logs, remote administration |
| `WinOps.PowerShell` | `net10.0-windows` | Process-based PowerShell executor, built-in templates, saved scripts and execution history |
| `WinOps.Persistence` | `net10.0` | EF Core SQLite context and repositories for settings, audit and diagnostic runs |
| `WinOps.Services` | `net10.0-windows` | Application services: diagnostic scenario registration and reporting |
| `WinOps.App` | `net10.0-windows` | WPF shell, views, viewmodels, converters, themes, navigation, dialogs and DI bootstrap |
| `tests/WinOps.Tests` | `net10.0-windows` | xUnit unit and integration-style tests |
| `installer/WinOps.Toolkit.Setup` | WiX v5 | MSI package definition and harvested self-contained payload |

## Dependency injection

`App.xaml.cs` builds the service provider at startup and registers:

- Serilog logger and repositories
- `ActionSafetyEngine`, `CommandRunner` and `IPrivilegeService`
- All Windows integration services behind interfaces
- PowerShell, diagnostics, reporting, confirmation, theme and navigation services
- All viewmodels and page types

Views resolve their viewmodels through `NavigationService`. `ViewModelBase` provides shared busy/progress/status handling and a reflection-based refresh hook used by `F5`.

## Async, cancellation and progress

- Long-running operations use `async`/`await` and never block the UI thread.
- `ViewModelBase.RunAsync` creates a `CancellationTokenSource`, sets busy state and exposes progress.
- `CommandRunner.RunAsync` redirects stdout/stderr, supports timeouts, kills the process tree on cancellation and reports progress through `IProgress<OperationProgress>`.
- `ActionSafetyEngine.ExecuteAsync` checks cancellation before the operation and supports post-action verification.

## Safety boundary

UI code never executes a mutation directly. It calls a service method, and the service calls `ActionSafetyEngine.ExecuteAsync` with a catalog action ID. The engine enforces:

1. Action description and risk level from `ActionCatalog`
2. Administrator/elevation requirement
3. Explicit confirmation dialog (fail-closed if no handler is installed)
4. Cancellation and timeout behavior
5. Structured error capture
6. Optional post-action verification
7. SQLite audit record with command, output, duration and outcome

See [SafetyModel.md](SafetyModel.md) for details.

## PowerShell integration

`PowerShellExecutor` uses `pwsh.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand <base64>`. This avoids in-process PowerShell SDK coupling and keeps script execution isolated. `CommandRunner.ResolvePowerShell()` prefers PowerShell 7 and falls back to Windows PowerShell 5.1.

Scripts are injected with validated `Name='value'` parameters. User-supplied scripts are always classified as `script.execute`, displayed for review and recorded in history and the audit trail.

## Persistence

`AppDbContext` uses EF Core with SQLite and `Pooling=False`. The database is created under `%APPDATA%\WinOps Toolkit`. Repositories cover:

- `Setting` key/value pairs
- `AuditEntry` records for every safety-engine result
- `DiagnosticRun` JSON snapshots

## Reporting

`ReportExporter` produces JSON, CSV, HTML and a basic PDF from diagnostic results. `ReportService` coordinates export and discovery. Exports are written to `Documents\WinOps Toolkit`.

## UI/UX

- Custom Material-inspired resource dictionaries for light and dark themes
- Reusable card, button, label, list and status styles
- Segmented module navigation with search filtering
- LiveCharts2 CPU/memory utilization chart on the dashboard
- Confirmation and restore-point dialogs
- Global keyboard shortcuts and status/progress footer

## Packaging

The application is published self-contained for `win-x64` and harvested by WiX into a per-machine MSI. The installer uses a fixed upgrade code and `MajorUpgrade` so running a newer MSI replaces the previous version. See [Installation.md](Installation.md).
