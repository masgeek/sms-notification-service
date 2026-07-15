# Deployment Guide

## Build the Executable

### Publish (self-contained — no .NET runtime needed on target)

```bash
dotnet publish SmsNotificationService.csproj -c Release -r win-x64 --self-contained
```

Output: `publish\`

## Create Installer

### Prerequisites

- [Inno Setup 6+](https://jrsoftware.org/isinfo.php) installed on your build machine

### Build

1. Publish the project (above)
2. Open `installer/installer.iss` in Inno Setup Compiler
3. Click **Build > Compile**
4. Output: `installer/output/SmsNotificationService-Setup.exe`

The installer will:
- Install files to `C:\Program Files\SmsNotificationService\`
- Prompt for database connection, API URL, and auth token
- Create the Windows Service (auto-start)
- Set Machine-scope environment variables
- Register an Event Log source

## Install as Windows Service

### Option A: Use the Installer

Run `SmsNotificationService-Setup.exe` as Administrator. Follow the prompts.

### Option B: Manual Install

```powershell
# Copy published folder
C:\Services\SmsNotificationService\

# Create service
sc create SmsNotificationService binPath="C:\Services\SmsNotificationService\SmsNotificationService.exe" start=auto
sc description SmsNotificationService "Listens to SQL Server for SMS notifications and sends them via HTTP API"

# Set env vars
[Environment]::SetEnvironmentVariable("SmsService__ConnectionString", "Server=127.0.0.1;Database=school;User Id=sa;Password=YOUR_PASSWORD;TrustServerCertificate=True;", "Machine")
[Environment]::SetEnvironmentVariable("SmsService__SmsApiUrl", "https://api.munywele.co.ke/v1/send", "Machine")
[Environment]::SetEnvironmentVariable("SmsService__AuthorizationToken", "your-bearer-token-here", "Machine")

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

## Logs

Windows Event Log:
```
Applications and Services Logs > SmsNotificationService
```

Or view via PowerShell:
```powershell
Get-EventLog -LogName Application -Source "SmsNotificationService" -Newest 20
```

## Troubleshooting

### Service won't start

1. Check env vars are set (restart terminal after setting Machine-scope vars):
   ```powershell
   [Environment]::GetEnvironmentVariable("SmsService__ConnectionString", "Machine")
   ```
2. Check Service Broker is enabled:
   ```sql
   SELECT name, is_broker_enabled FROM sys.databases WHERE name = 'school';
   ```
3. Check the SQL connection string is valid

### Notifications not triggering

1. Verify the listener started (check logs for `[Listener] Query registered successfully`)
2. Ensure `dbo.sms_notifications` table has a PRIMARY KEY
3. Test with the diagnostic script in `docs/`

### SMS sends failing

1. Check API URL and token are correct
2. Check network connectivity to the API endpoint
3. Look for `[SMS]` logs with HTTP status codes

## Updating the Service

```powershell
sc stop SmsNotificationService
dotnet publish -c Release -r win-x64 --self-contained
# Copy new files to C:\Services\SmsNotificationService\
sc start SmsNotificationService
```
