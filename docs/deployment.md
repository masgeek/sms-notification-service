# Deployment Guide

## Quick Start

Download the latest installer from [GitHub Releases](../../releases) and run `SmsNotificationService-Setup-<version>.exe` as Administrator.

## Build from Source

### Publish (self-contained — no .NET runtime needed on target)

```bash
dotnet publish SmsNotificationService.csproj -c Release -r win-x64 --self-contained -o publish
```

Output: `publish\`

### Build Installer

**Prerequisites:** [Inno Setup 6+](https://jrsoftware.org/isinfo.php)

```bash
# Command line
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DMyAppVersion=1.2.3 installer\installer.iss

# Or open installer/installer.iss in Inno Setup Compiler > Build > Compile
```

Output: `installer/output/SmsNotificationService-Setup-<version>.exe`

The installer version is set dynamically via `/DMyAppVersion=<version>`. If omitted, defaults to `1.0.0`.

## What the Installer Does

1. Detects fresh install vs upgrade (checks for existing service)
2. Prompts to keep or update existing configuration
3. Writes `appsettings.Production.json` to `ProgramData\Munywele\SmsNotificationService\`
4. Creates Windows Service (`delayed-auto`, `LocalSystem`)
5. Configures service recovery (restart on failure: 5min, 5s, 5s)
6. Registers Event Log source
7. Starts the service

## Configuration

Config is stored in `ProgramData\Munywele\SmsNotificationService\appsettings.Production.json` (not environment variables):

```json
{
  "SmsService": {
    "ConnectionString": "Server=127.0.0.1;Database=school;User Id=sa;Password=YOUR_PASSWORD;TrustServerCertificate=True;",
    "SmsApiUrl": "https://api.munywele.co.ke/v1/send",
    "AuthorizationToken": "your-bearer-token",
    "RetryBackoffSeconds": 30,
    "RetryPollIntervalSeconds": 30,
    "LogRetentionDays": 7,
    "MaxLogFileSizeMb": 10
  }
}
```

Edit the file directly or reinstall with "Enter new configuration" selected.

### Environment Variables (Fallback)

If the config file is missing, environment variables are used as a fallback:

```powershell
# Set (run as Administrator — persists across reboots)
[Environment]::SetEnvironmentVariable("SmsService__ConnectionString", "Server=127.0.0.1;Database=school;User Id=sa;Password=YourPassword123;TrustServerCertificate=True;", "Machine")
[Environment]::SetEnvironmentVariable("SmsService__SmsApiUrl", "https://api.munywele.co.ke/v1/send", "Machine")
[Environment]::SetEnvironmentVariable("SmsService__AuthorizationToken", "your-bearer-token-here", "Machine")
[Environment]::SetEnvironmentVariable("SmsService__RetryBackoffSeconds", "30", "Machine")

# Verify
[Environment]::GetEnvironmentVariable("SmsService__ConnectionString", "Machine")
[Environment]::GetEnvironmentVariable("SmsService__SmsApiUrl", "Machine")

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
4. Replace binaries
5. Restart the service

Or manually:

```powershell
sc stop SmsNotificationService
dotnet publish SmsNotificationService.csproj -c Release -r win-x64 --self-contained -o publish
# Copy new files
sc start SmsNotificationService
```

## CI/CD Pipeline

Fully automated. No manual tagging required.

```
Push to any branch
    │
    v
Tests ──> All tests pass
    │
    v
Release (main branch only)
    ├── Auto-generate version tag from conventional commits
    ├── Build win-x64 self-contained publish
    ├── Build Inno Setup installer (version passed via /DMyAppVersion)
    ├── Create zip archive
    └── Create/update GitHub Release with artifacts
```

Re-running on the same commit republishes the existing release.

## Troubleshooting

### Service won't start

1. Check config file exists:
   ```powershell
   Test-Path "C:\ProgramData\Munywele\SmsNotificationService\appsettings.Production.json"
   ```
2. Check Service Broker is enabled:
   ```sql
   SELECT name, is_broker_enabled FROM sys.databases WHERE name = 'school';
   ```
3. Check Event Log for startup errors:
   ```powershell
   Get-EventLog -LogName Application -Source "SmsNotificationService" -EntryType Error -Newest 10
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
