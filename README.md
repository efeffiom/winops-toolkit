# WinOps Toolkit

WinOps Toolkit is a native Windows administration and troubleshooting desktop application built with C#, .NET 10, WPF and MVVM. It combines live system evidence, guarded administrative actions, PowerShell automation, diagnostics and reporting in one installable application.

> Status: the application, MSI installer, xUnit tests and Pester tests build and pass in the development environment. Administrator-only and destructive actions still require validation in an isolated VM before production release. See [PROJECT_STATUS.md](PROJECT_STATUS.md).

## What is included

- **16 working modules**: Dashboard, Process Manager, Services, Inventory, Network Toolkit, Windows Maintenance, Security Center, Local Users, Active Directory, Storage, Event Logs, PowerShell Center, Remote Administration, Troubleshooting, Reports, and Settings & Audit.
- **Real data and real operations**: CIM/WMI, Windows services, event logs, PowerShell 7, Windows Update history, network diagnostics, local accounts, AD queries, storage analysis and remote connectivity.
- **Central safety engine**: every state-changing operation is classified, confirmed, elevation-checked, cancellation-aware, audited and verified where possible.
- **Self-contained x64 MSI**: installs to `Program Files\WinOps\WinOps Toolkit`, creates Start menu and desktop shortcuts, supports clean uninstall and in-place major upgrades.
- **Light/dark Material-inspired UI**, reusable cards/styles, LiveCharts2 dashboard, global search and keyboard shortcuts.

## Quick start

### Run from source

Prerequisites:

- Windows 10/11 x64 or Windows Server with desktop experience
- .NET 10 SDK
- PowerShell 7 (recommended; Windows PowerShell 5.1 fallback is used only where compatible)

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build 'WinOpsToolkit.slnx' -v:minimal
& 'C:\Program Files\dotnet\dotnet.exe' run --project 'src\WinOps.App\WinOps.App.csproj'
```

### Build a release MSI

```powershell
powershell.exe -NoProfile -File 'tools\Build\Build-Release.ps1' -Version 1.0.0
```

The script builds and tests the solution, publishes a self-contained `win-x64` payload, builds the WiX MSI, validates the MSI database and writes the final package to:

```text
artifacts\installer\WinOpsToolkitSetup-<version>-x64.msi
```

See [docs/Installation.md](docs/Installation.md) for install, upgrade and uninstall commands.

## Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| `Ctrl+K` | Focus module search |
| `Ctrl+D` | Toggle light/dark theme |
| `F5` | Refresh the current module |
| `Esc` | Clear module search when the search box has focus |

## Data and logs

WinOps Toolkit does not store user data in the installation directory.

| Path | Contents |
| --- | --- |
| `%APPDATA%\WinOps Toolkit\winops.db` | Settings, audit trail, diagnostic runs |
| `%APPDATA%\WinOps Toolkit\Logs\` | Serilog daily rolling logs |
| `%APPDATA%\WinOps Toolkit\scripts.json` | Saved PowerShell templates |
| `%APPDATA%\WinOps Toolkit\script-history.json` | Last 200 PowerShell executions |
| `%APPDATA%\WinOps Toolkit\endpoints.json` | Saved remote endpoints |
| `Documents\WinOps Toolkit\` | Exported reports and CSV files |

Uninstalling the MSI does not delete these user files. Remove them manually if required.

## Documentation

- [Architecture](docs/Architecture.md)
- [Installation and packaging](docs/Installation.md)
- [Safety model](docs/SafetyModel.md)
- [Operations guide](docs/Operations.md)
- [Testing guide](docs/Testing.md)
- [Project status](PROJECT_STATUS.md)

## Known limitations

- Administrator-gated actions were verified only through denial paths in the development session. Test all high-risk actions in an isolated VM.
- The MSI is not Authenticode-signed. Sign it with a trusted certificate before broad deployment.
- LiveCharts2 2.2.0 pulls legacy WPF/SkiaSharp transitive packages and emits NU1701 warnings. Runtime smoke testing is used to confirm compatibility.
- PowerShell 7 is recommended. Windows PowerShell 5.1 is used only as a fallback and does not support every modern cmdlet used by the built-in templates.
- Remote administration must only be used on systems you are authorized to manage.
