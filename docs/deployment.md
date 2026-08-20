# Deployment Guide

## Installer Options

Download one of these assets from GitHub Releases:

| Installer | Use when |
|---|---|
| `FeeSyncer-Setup-<version>.exe` | The target should not require a preinstalled .NET runtime |
| `FeeSyncer-Framework-Setup-<version>.exe` | .NET 10 Runtime and Desktop Runtime are already installed |

Run the installer as Administrator. Unsigned builds can trigger Windows
SmartScreen; select **More info > Run anyway** only after verifying the source
and checksum of the installer.

## Installed Layout

```text
C:\Program Files\FeeSyncer\
|-- FeeSyncer.Sms.exe
|-- Agent\FeeSyncer.Agent.exe
|-- Tray\FeeSyncer.Tray.exe
`-- Console\FeeSyncer.Console.exe
```

Machine data is stored separately:

```text
C:\ProgramData\Munywele\FeeSyncer\appsettings.Production.json
C:\ProgramData\Munywele\FeeSyncer\agentsettings.json
C:\ProgramData\Munywele\FeeSyncer\logs\
```

The installer copies all four applications and creates `FeeSyncer.Sms` and
`FeeSyncer.Agent` under `LocalSystem`. Both services use manual startup and are
left stopped so they can be configured first. The installer does not prompt for
or write database passwords, API tokens, or enrollment codes.

If selected, the tray receives Start Menu and all-users Startup-folder shortcuts
and opens with `--setup` after installation.

## First-Time Setup

1. Open `C:\Program Files\FeeSyncer\Tray\FeeSyncer.Tray.exe --setup`.
2. Configure the SMS database, gateway URL, token, retry values, and logging.
3. Validate the SMS database, API, and Service Broker connections.
4. Configure local Agent API and MQTT values.
5. Generate an `enroll_...` code centrally and select **Enroll / Re-enroll**.
6. Start each service from the Control Panel after its validation succeeds.

Configuration files currently contain credentials as plain JSON. Apply suitable
NTFS access controls to the ProgramData directory and avoid sharing diagnostic
archives without reviewing them for secrets.

## Build From Source

Requirements:

- .NET 10 SDK
- Inno Setup 6
- Windows x64 build host for installer validation

Publish self-contained applications:

```powershell
./publish.ps1
./publish.ps1 -Clean
```

Outputs:

```text
build\service\
build\agent\
build\tray\
build\console\
```

Publish framework-dependent applications:

```powershell
./publish-framework.ps1
```

Outputs:

```text
build\service-framework\
build\agent-framework\
build\tray-framework\
build\console-framework\
```

Build installers:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DMyAppVersion=1.2.3 installer\installer.iss
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DMyAppVersion=1.2.3 installer\installer-framework.iss
```

Run validation:

```powershell
dotnet restore
dotnet build -c Release
dotnet format --verify-no-changes
dotnet test -c Release --no-build
```

## Manual Service Installation

```powershell
sc.exe create FeeSyncer.Sms binPath= "C:\Program Files\FeeSyncer\FeeSyncer.Sms.exe" start= demand
sc.exe create FeeSyncer.Agent binPath= "C:\Program Files\FeeSyncer\Agent\FeeSyncer.Agent.exe" start= demand

sc.exe description FeeSyncer.Sms "Listens for SMS notifications and sends them to the central API"
sc.exe description FeeSyncer.Agent "Synchronizes school data with the central gateway"

sc.exe failure FeeSyncer.Sms reset= 86400 actions= restart/300000/restart/5000/restart/5000
sc.exe failure FeeSyncer.Agent reset= 86400 actions= restart/300000/restart/5000/restart/5000
```

The tray Control Panel can also install services; that path creates delayed-auto
services, unlike the current Inno installer.

## Service Management

```powershell
sc.exe query FeeSyncer.Sms
sc.exe query FeeSyncer.Agent

sc.exe start FeeSyncer.Sms
sc.exe start FeeSyncer.Agent

sc.exe stop FeeSyncer.Sms
sc.exe stop FeeSyncer.Agent
```

## Logging

Application logs are written to:

```text
C:\ProgramData\Munywele\FeeSyncer\logs\
```

SMS logs rotate by category and day, with approximate size rotation and startup
retention cleanup. The installer registers a `FeeSyncer.Sms` source in the
standard Windows **Application** Event Log.

## Upgrades

The tray can install an available update directly. It downloads the installer to
the current user's local update directory, validates the HTTPS origin, exact
size, and SHA-256 checksum, requests administrator permission, then exits. The
installer preserves ProgramData configuration and logs, restores the previous
running state of both services, and relaunches the tray after success.

The installer is currently unsigned, so Windows displays **Unknown publisher**.
Operators should cancel if the tray reports any size or checksum mismatch.
Manual upgrades use the same service-state preservation behavior.

## Uninstall

The uninstaller stops and removes both services and asks whether ProgramData
configuration and logs should be retained. Preserve that directory if the
machine will be reinstalled or if credentials and diagnostics are still needed.

## CI/CD

| Workflow | Trigger | Purpose |
|---|---|---|
| `tests.yml` | Non-documentation pushes, manual | Build, format, all tests, publish checks, both installer checks |
| `agent-tests.yml` | `main`/`develop` pushes and pull requests | Agent test project |
| `create-release-pr.yml` | Successful Tests on `develop`, manual | Create/update `develop` to `main` release PR |
| `release.yml` | Successful Tests on `main`, manual | Tag, publish four ZIPs, build two installers, create release |
| `auto-review.yml` | Pull-request workflow events | Automated guarded review/approval |

Release assets are four self-contained ZIPs for SMS, Agent, Tray, and Console,
plus self-contained and framework-dependent installers and `latest.json`. The release workflow
also publishes only the two installer `.exe` files to the public `fee-syncer`
S3 bucket and publishes
`https://s3.munywele.co.ke/fee-syncer/latest.json` after all versioned objects.
The tray uses the public GitHub release manifest as a fallback only when the S3
manifest is unavailable or invalid. Both manifests declare exact installer sizes
and SHA-256 checksums.

Configure these private repository secrets before running a release:

```text
S3_ACCESS_KEY_ID
S3_SECRET_ACCESS_KEY
```

The S3 account needs write access to `s3://fee-syncer`, while anonymous users
need read-only access through `https://s3.munywele.co.ke/fee-syncer/`. The
administrative console at `https://s3-console.munywele.co.ke` is not used by the
application.

## Troubleshooting

### SMS service does not start

1. Verify `C:\ProgramData\Munywele\FeeSyncer\appsettings.Production.json`.
2. Confirm the connection string and bearer token are populated.
3. Confirm Service Broker is enabled.
4. Review ProgramData logs and the Windows Application log.

```sql
SELECT name, is_broker_enabled
FROM sys.databases
WHERE name = 'school';
```

### Notifications do not process

1. Confirm the table is `dbo.sms_notifications` with the documented columns.
2. Check for `[Listener]` registration messages.
3. Check `[Poll]`, `[Queue]`, and `[SMS]` messages.
4. Inspect `status`, `retry_count`, `retry_after`, and `description_json`.

### Agent does not discover work

1. Verify `agentsettings.json` and a valid `fsk_...` token.
2. Verify the gateway and loopback local API from the tray diagnostics.
3. Test the MQTT WebSocket endpoint and credentials.
4. Remember that discovery pauses while MQTT is disconnected.
5. Review `[Agent]` and MQTT-related logs.

### Enrollment fails

1. Generate a fresh code; codes expire after 15 minutes and are single-use.
2. Verify the central base URL and enrollment endpoint.
3. Ensure the response returns an `fsk_...` token.
4. Confirm the Agent service can be restarted by the current user.

### Tray does not start automatically

1. Verify `C:\Program Files\FeeSyncer\Tray\FeeSyncer.Tray.exe` exists.
2. Inspect the all-users Startup folder for the FeeSyncer shortcut.
3. Run the executable manually and inspect ProgramData logs.
