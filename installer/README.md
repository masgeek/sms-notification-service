# FeeSyncer Installer

The Inno Setup definitions package four applications and install two Windows
services.

## Variants

| Script | Output | Runtime requirement |
|---|---|---|
| `installer.iss` | `FeeSyncer-Setup-<version>.exe` | None; application runtimes are bundled |
| `installer-framework.iss` | `FeeSyncer-Framework-Setup-<version>.exe` | .NET 10 Runtime and Desktop Runtime |

## Installed Layout

```text
C:\Program Files\FeeSyncer\
|-- FeeSyncer.Sms.exe
|-- Agent\FeeSyncer.Agent.exe
|-- Tray\FeeSyncer.Tray.exe
`-- Console\FeeSyncer.Console.exe
```

Both `FeeSyncer.Sms` and `FeeSyncer.Agent` are installed under `LocalSystem` as
manual (`demand`) services and are left stopped. Service recovery restarts after
5 minutes, then after 5 seconds for the next two failures.

Configuration is application-managed, not installer-managed:

```text
C:\ProgramData\Munywele\FeeSyncer\appsettings.Production.json
C:\ProgramData\Munywele\FeeSyncer\agentsettings.json
C:\ProgramData\Munywele\FeeSyncer\logs\
```

The installer never accepts enrollment codes or API credentials. When selected,
it launches `Tray\FeeSyncer.Tray.exe --setup` so the operator can configure and
enroll the services.

## Wizard Choices

- Install tray shortcuts and all-users Startup entry, selected by default
- Select the Console Monitor option, currently informational because binaries are always copied
- Launch the tray setup screen after installation, selected by default

Tray startup uses the all-users Startup folder. The old `HKCU\...\Run` approach
is not used by current installers.

## Structure

```text
installer/
|-- installer.iss
|-- installer-framework.iss
|-- code/
|   |-- globals.iss
|   |-- utils.iss
|   |-- services.iss
|   |-- eventlog.iss
|   |-- wizard.iss
|   |-- install.iss
|   `-- uninstall.iss
`-- output/
```

Included Pascal files share one `[Code]` scope. Define dependencies before
dependents and do not place semicolon comment headers at the top of include
files; Inno Setup can report `BEGIN expected`.

## Build

```powershell
./publish.ps1
./publish-framework.ps1

& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DMyAppVersion=1.2.3 installer\installer.iss
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DMyAppVersion=1.2.3 installer\installer-framework.iss
```

Expected publish inputs:

| Variant | Folders |
|---|---|
| Self-contained | `build/service`, `build/agent`, `build/tray`, `build/console` |
| Framework-dependent | `build/service-framework`, `build/agent-framework`, `build/tray-framework`, `build/console-framework` |

## Uninstall

Uninstall stops and removes both services, terminates tray and console processes,
and removes the Event Log source. It asks whether ProgramData configuration and
logs should be preserved. Read that prompt carefully because removing the data
deletes credentials and operational logs.

See [Deployment Guide](../docs/deployment.md) for operator instructions.
