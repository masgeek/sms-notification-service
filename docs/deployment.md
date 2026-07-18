# Deployment Guide

## Quick Start

Download the latest installer from [GitHub Releases](../../releases). Two variants are available:

- `SmsNotificationService-Setup-<version>.exe` — self-contained (no .NET runtime needed)
- `SmsNotificationService-Framework-Setup-<version>.exe` — framework-dependent (requires .NET 10 runtime on target machine)

Run the installer as Administrator.

The installer deploys:
- **SmsNotificationService** — background worker (Windows Service)
- **SmsNotificationService.Tray** — system tray management app (optional, auto-starts on login if selected)

## Windows SmartScreen Warning

When you first run the installer, Windows SmartScreen may show a warning: **"Windows protected your PC"**. This is expected for new software without an established download reputation.

### How to Bypass

1. Click **"More info"**
2. Click **"Run anyway"**

![SmartScreen Bypass](https://learn.microsoft.com/en-us/windows/win32/secauthn/images/smartscreen-more-info.png)

### Why This Happens

- The application is not signed with a code signing certificate
- Windows SmartScreen builds reputation based on download counts
- New applications trigger warnings until they establish reputation

### To Avoid This Warning Permanently

Purchase a code signing certificate:

| Provider | Cost | Notes |
|----------|------|-------|
| SSL.com | ~$70/year | Cheapest legitimate option |
| Certum | ~€20/year | Polish CA, good prices |
| DigiCert | ~$200+/year | Industry standard |

> Even with a certificate, new signings trigger SmartScreen temporarily until reputation builds.

## Build from Source

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Inno Setup 6+](https://jrsoftware.org/isinfo.php) (for installer only)

### Publish Both Projects

```bash
# Single command — publishes both projects
./publish.ps1

# With clean (removes bin/obj/publish first)
./publish.ps1 -Clean
```

Output:
- `publish\` — service binaries (SmsNotificationService.exe + dependencies)
- `publish-tray\` — tray app binaries (SmsNotificationService.Tray.exe + dependencies)

Both are self-contained — no .NET runtime needed on the target machine.

Or publish as framework-dependent:

```bash
./publish-framework.ps1
```

Output:
- `publish-framework\` — service binaries (requires .NET 10 runtime)
- `publish-tray-framework\` — tray app binaries (requires .NET 10 runtime)

Or publish individually:

```bash
dotnet publish SmsNotificationService.csproj -c Release -r win-x64 --self-contained -o publish
dotnet publish SmsNotificationService.Tray/SmsNotificationService.Tray.csproj -c Release -r win-x64 --self-contained -o publish-tray
```

### Build Installer

```bash
# Self-contained installer (bundles .NET runtime)
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DMyAppVersion=1.2.3 installer\installer.iss

# Framework-dependent installer (requires .NET 10 runtime on target)
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DMyAppVersion=1.2.3 /DFrameworkInstall installer\installer-framework.iss
```

**Requirements:** 
- Self-contained: `publish\` and `publish-tray\` directories must exist
- Framework-dependent: `publish-framework\` and `publish-tray-framework\` directories must exist

Output:
- `installer/output/SmsNotificationService-Setup-<version>.exe` (self-contained)
- `installer/output/SmsNotificationService-Framework-Setup-<version>.exe` (framework-dependent)

The installer version is set dynamically via `/DMyAppVersion=<version>`. If omitted, defaults to `1.0.0`.

### Run Tests

```bash
# Run all unit tests
dotnet test -c Release

# Run with verbose output
dotnet test -c Release --verbosity normal

# Run specific test class
dotnet test -c Release --filter "FullyQualifiedName~NotificationProcessorTests"
dotnet test -c Release --filter "FullyQualifiedName~SmsApiServiceTests"
```

### Validate Installer Scripts (without building)

Create dummy publish folders and compile both ISS scripts:

```bash
# Self-contained installer
mkdir publish && echo placeholder > publish\SmsNotificationService.exe
mkdir publish-tray && echo placeholder > publish-tray\SmsNotificationService.Tray.exe
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\installer.iss

# Framework-dependent installer
mkdir publish-framework && echo placeholder > publish-framework\SmsNotificationService.exe
mkdir publish-tray-framework && echo placeholder > publish-tray-framework\SmsNotificationService.Tray.exe
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\installer-framework.iss
```

## What the Installer Does

1. Detects fresh install vs upgrade (checks for existing service)
2. Prompts to keep or update existing configuration
3. Writes `appsettings.Production.json` to `C:\Program Files\SmsNotificationService\`
4. Copies both service and tray app binaries to `C:\Program Files\SmsNotificationService\`
5. Creates Windows Service (`delayed-auto`, `LocalSystem`)
6. Configures service recovery (restart on failure: 5min, 5s, 5s)
7. Registers Event Log source
8. Creates Start Menu shortcuts (service + uninstall; tray app if selected)
9. Adds tray app to Windows auto-start if selected (`HKCU\...\Run`)
10. Optionally starts the service and tray app

> The tray app is optional — the installer includes a "System Tray App" page where you can toggle it on/off. The binaries are always copied but the shortcut and auto-start are only created if selected.

## Configuration

Config is stored in `C:\Program Files\SmsNotificationService\appsettings.Production.json`:

```json
{
  "SmsService": {
    "ConnectionString": "Server=127.0.0.1;Database=school;User Id=sa;Password=YOUR_PASSWORD;TrustServerCertificate=True;",
    "SmsApiUrl": "https://fees.munywele.co.ke/api/v1/notifications",
    "AuthorizationToken": "your-bearer-token",
    "RetryBackoffSeconds": 30,
    "RetryPollIntervalSeconds": 30,
    "LogRetentionDays": 7,
    "MaxLogFileSizeMb": 10
  }
}
```

Edit the file directly, use the tray app's Config Editor, or reinstall with "Enter new configuration" selected.

### Environment Variables (Fallback)

If the config file is missing, environment variables are used as a fallback:

```powershell
# Set (run as Administrator — persists across reboots)
[Environment]::SetEnvironmentVariable("SmsService__ConnectionString", "Server=127.0.0.1;Database=school;User Id=sa;Password=YourPassword123;TrustServerCertificate=True;", "Machine")
[Environment]::SetEnvironmentVariable("SmsService__SmsApiUrl", "https://fees.munywele.co.ke/api/v1/notifications", "Machine")
[Environment]::SetEnvironmentVariable("SmsService__AuthorizationToken", "your-bearer-token-here", "Machine")
[Environment]::SetEnvironmentVariable("SmsService__RetryBackoffSeconds", "30", "Machine")

# Verify
[Environment]::GetEnvironmentVariable("SmsService__ConnectionString", "Machine")

# Remove
[Environment]::SetEnvironmentVariable("SmsService__ConnectionString", $null, "Machine")
[Environment]::SetEnvironmentVariable("SmsService__SmsApiUrl", $null, "Machine")
[Environment]::SetEnvironmentVariable("SmsService__AuthorizationToken", $null, "Machine")
[Environment]::SetEnvironmentVariable("SmsService__RetryBackoffSeconds", $null, "Machine")
```

| Config Key | Env Variable | Default | Description |
|---|---|---|---|
| `SmsService:ConnectionString` | `SmsService__ConnectionString` | — | SQL Server connection string |
| `SmsService:SmsApiUrl` | `SmsService__SmsApiUrl` | — | SMS API endpoint URL |
| `SmsService:AuthorizationToken` | `SmsService__AuthorizationToken` | — | Bearer token for API auth |
| `SmsService:RetryBackoffSeconds` | `SmsService__RetryBackoffSeconds` | `30` | Base retry backoff in seconds |

> **Priority:** Config file (`appsettings.Production.json`) > Environment variables > Defaults

## Manual Install

```powershell
# Copy published folder
C:\Services\SmsNotificationService\

# Create service
sc create SmsNotificationService binPath="C:\Services\SmsNotificationService\SmsNotificationService.exe" start=delayed-auto
sc description SmsNotificationService "Listens to SQL Server for SMS notifications and sends them via HTTP API"
sc failure SmsNotificationService reset= 86400 actions= restart/300000/restart/5000/restart/5000

# Start
sc start SmsNotificationService
```

## Service Management

```powershell
# Check status
sc query SmsNotificationService

# Stop
sc stop SmsNotificationService

# Restart
sc stop SmsNotificationService
sc start SmsNotificationService

# Remove
sc stop SmsNotificationService
sc delete SmsNotificationService
```

If `sc delete` fails, the installer also cleans up the registry key:
`HKLM\SYSTEM\CurrentControlSet\Services\SmsNotificationService`

## Tray App

The tray app (`SmsNotificationService.Tray.exe`) provides:

- **Status monitoring** — real-time service status, uptime, version
- **Service control** — start, stop, restart from the tray menu
- **Log viewer** — view and filter service log files
- **Send notification** — insert test notifications directly into the database
- **Config editor** — edit all settings with individual DB fields (server, database, user, password, encrypt)
- **Connection validator** — test DB, API, and Service Broker connectivity
- **Update checker** — polls GitHub Releases every 4 hours for new versions

The tray app auto-starts on login via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

### Running the Tray App Manually

```powershell
# From published folder
.\SmsNotificationService.Tray.exe

# Or from installed location
& "C:\Program Files\SmsNotificationService\SmsNotificationService.Tray.exe"
```

## Logs

**File logs** (primary):

```
C:\ProgramData\Munywele\SmsNotificationService\logs\
```

Daily rotation, 7-day retention (configurable). Old files are cleaned up on startup.

**Windows Event Log:**

```
Applications and Services Logs > SmsNotificationService
```

```powershell
Get-EventLog -LogName Application -Source "SmsNotificationService" -Newest 20
```

## Upgrading

Run the new installer. It will:

1. Detect the existing installation
2. Stop the service
3. Prompt to keep or update configuration
4. Replace binaries (service + tray app)
5. Restart the service

Or manually:

```powershell
sc stop SmsNotificationService
./publish.ps1
# Copy new files
sc start SmsNotificationService
```

## CI/CD Pipeline

Fully automated. No manual tagging required.

```
Push to any branch
    │
    v
Tests ──> .NET Tests
    │     (build solution, format check, unit tests, vulnerability scan)
    │
    ├──> Build Tray App
    │     (publish tray app, verify binary exists)
    │
    ├──> Validate Self-Contained Installer
    │     (compile ISS with dummy publish folders)
    │
    ├──> Validate Framework-Dependent Installer
    │     (compile framework ISS with dummy publish folders)
    │
    └──> All Checks Passed (summary)
              │
              v
Release (main branch only, after tests pass)
    ├── Generate version tag from conventional commits
    ├── Build win-x64 publish (both self-contained and framework-dependent)
    ├── Build both Inno Setup installers
    ├── Create zip archive (service + tray app binaries)
    └── Create/update GitHub Release with all artifacts
```

### Workflow Files

| File | Purpose | Trigger |
|------|---------|---------|
| `tests.yml` | Build validation, unit tests, both installer script checks | Push, PR, manual |
| `release.yml` | Version tag, build both installers, publish release | After tests pass on `main` |
| `create-release-pr.yml` | Auto-create PR from `develop` to `main` | After tests pass on `develop` |
| `auto-review.yml` | Auto-approve PRs after checks pass | After tests pass |

### Release Artifacts

Each release produces:

| Artifact | Description |
|----------|-------------|
| `SmsNotificationService-Setup-<version>.exe` | Self-contained installer (bundles .NET runtime) |
| `SmsNotificationService-Framework-Setup-<version>.exe` | Framework-dependent installer (requires .NET 10 runtime) |
| `SmsNotificationService-win-x64.zip` | Zip of `publish/` + `publish-tray/` directories |

## Troubleshooting

### Service won't start

1. Check config file exists:
   ```powershell
   Test-Path "C:\Program Files\SmsNotificationService\appsettings.Production.json"
   ```
2. Check Service Broker is enabled:
   ```sql
   SELECT name, is_broker_enabled FROM sys.databases WHERE name = 'school';
   ```
3. Check Event Log for startup errors:
   ```powershell
   Get-EventLog -LogName Application -Source "SmsNotificationService" -EntryType Error -Newest 10
   ```

### Tray app not starting

1. Check the executable exists:
   ```powershell
   Test-Path "C:\Program Files\SmsNotificationService\SmsNotificationService.Tray.exe"
   ```
2. Check auto-start registry entry:
   ```powershell
   Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "SmsNotificationService.Tray" -ErrorAction SilentlyContinue
   ```
3. Run manually from command line to see errors:
   ```powershell
   & "C:\Program Files\SmsNotificationService\SmsNotificationService.Tray.exe"
   ```

### Notifications not triggering

1. Verify the listener started (check logs for `[Listener] Query registered successfully`)
2. Ensure `dbo.sms_notifications` table has a PRIMARY KEY
3. Check the retry poller is running (check logs for `[RetryPoller]` entries)

### SMS sends failing

1. Check API URL and token in config
2. Check network connectivity to the API endpoint
3. Look for `[SMS]` logs with HTTP status codes

### File logs not appearing

1. Check `ProgramData\Munywele\SmsNotificationService\logs\` exists
2. Ensure the service account (LocalSystem) has write access
3. Check `LogRetentionDays` — logs older than this are auto-deleted on startup
