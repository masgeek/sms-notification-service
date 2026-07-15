# SmsNotificationService

A .NET 10 background worker service that listens to a SQL Server table for new SMS notifications and sends them via an external HTTP API.

## Architecture

```
SQL Server (sms_notifications table)
    |
    | SqlDependency (Service Broker)
    v
Worker (background service)
    |
    | HTTP POST (JSON payload)
    v
SMS API (e.g., api.munywele.co.ke)
    |
    | Response
    v
Update status → PROCESSED / CANCELLED
```

## Tech Stack

- .NET 10 Worker Service
- `Microsoft.Data.SqlClient` 7.0.2 — SQL Server connectivity
- `Dapper` 2.1.79 — lightweight ORM
- `SqlDependency` — real-time change notifications via Service Broker

## Prerequisites

- .NET 10 SDK
- SQL Server (local or remote)
- SQL Server **Service Broker** enabled on the target database
- Access to an SMS API endpoint

## Setup

### 1. Enable Service Broker

```sql
ALTER DATABASE school SET ENABLE_BROKER;
```

### 2. Create the Table

```sql
CREATE TABLE sms_notifications (
    id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    phone_number    NVARCHAR(50)    NOT NULL,
    mpesa_code      NVARCHAR(100)   NOT NULL,
    admission_no    NVARCHAR(50)    NOT NULL,
    student_name    NVARCHAR(200)   NULL,
    amount          DECIMAL(18,2)   NULL,
    receipt_no      NVARCHAR(100)   NULL,
    dated           DATETIME        NULL,
    status          NVARCHAR(20)    NOT NULL DEFAULT 'PENDING',
    max_retries     INT             NOT NULL DEFAULT 5,
    retry_count     INT             NOT NULL DEFAULT 0,
    retry_after     DATETIME        NULL,
    created_at      DATETIMEOFFSET  NULL,
    updated_at      DATETIMEOFFSET  NULL
);
```

### 3. Configure

Edit `appsettings.Development.json` or set environment variables:

**appsettings.Development.json:**

```json
{
  "SmsService": {
    "ConnectionString": "Server=127.0.0.1;Database=school;User Id=sa;Password=YOUR_PASSWORD;TrustServerCertificate=True;",
    "SmsApiUrl": "https://api.munywele.co.ke/v1/send",
    "RetryBackoffSeconds": 30
  }
}
```

**Environment variables:**

```powershell
# Set (run as Administrator — persists across reboots)
[Environment]::SetEnvironmentVariable("SmsService__ConnectionString", "Server=127.0.0.1;Database=school;User Id=sa;Password=YourPassword123;TrustServerCertificate=True;", "Machine")
[Environment]::SetEnvironmentVariable("SmsService__SmsApiUrl", "https://api.munywele.co.ke/v1/send", "Machine")
[Environment]::SetEnvironmentVariable("SmsService__AuthorizationToken", "your-bearer-token-here", "Machine")
[Environment]::SetEnvironmentVariable("SmsService__RetryBackoffSeconds", "30", "Machine")

# Verify
[Environment]::GetEnvironmentVariable("SmsService__ConnectionString", "Machine")
[Environment]::GetEnvironmentVariable("SmsService__SmsApiUrl", "Machine")
[Environment]::GetEnvironmentVariable("SmsService__AuthorizationToken", "Machine")
[Environment]::GetEnvironmentVariable("SmsService__RetryBackoffSeconds", "Machine")

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

### 4. Run

```bash
dotnet run
```

### 5. Run as Windows Service

```bash
dotnet publish -c Release
sc create SmsNotificationService binPath="C:\path\to\publish\SmsNotificationService.exe"
sc start SmsNotificationService
```

> Full deployment guide: [docs/deployment.md](docs/deployment.md)

## How It Works

1. **Startup** — Validates configuration and database connectivity
2. **Catch-up** — Processes any existing `PENDING` notifications before starting the listener
3. **SqlDependency listener** — Registers a SELECT query on `sms_notifications`
4. **Change detected** — When rows change, `OnChange` fires
5. **Process pending** — Fetches all `PENDING` notifications where `retry_count < max_retries` and `retry_after` has passed (or is null)
6. **Send SMS** — POSTs raw data payload to the configured API
7. **On success** — Status → `PROCESSED`
8. **On failure** — Increments `retry_count`, sets `retry_after` with exponential backoff
9. **Max retries exceeded** — Status → `CANCELLED`

## Status Enum

| Value | Description |
|---|---|
| `PENDING` | Initial state, waiting to be sent |
| `PROCESSED` | SMS sent successfully |
| `FAILED` | Reserved (not currently used) |
| `CANCELLED` | Exceeded `max_retries`, no more attempts |

## Retry Backoff

Exponential backoff starting from `RetryBackoffSeconds` (default 30s):

| Retry | Delay | Cumulative |
|---|---|---|
| 1 | 30s | 30s |
| 2 | 1m | 1m 30s |
| 3 | 2m | 3m 30s |
| 4 | 4m | 7m 30s |
| 5 | — | CANCELLED |

Each notification has its own `max_retries` (DB column, default 5) and `retry_count` (tracks attempts).

## Features

- **Concurrency guard** — `SemaphoreSlim` prevents duplicate processing
- **Retry with backoff** — Configurable exponential backoff per notification
- **Startup catch-up** — Processes missed notifications on restart
- **Listener resilience** — Retries `SqlDependency` registration up to 5 times
- **Graceful shutdown** — Waits up to 30s for in-flight sends
- **Typed configuration** — `IOptions<SmsServiceOptions>` with startup validation
- **Null safety** — Nullable enabled with warnings-as-errors
- **Structured logging** — `[Tag]` prefixed logs for quick filtering

## Versioning

This project uses **Git tag-based versioning**. The version in `Directory.Build.props` is automatically updated when a release tag is pushed.

```bash
# Create and push a version tag
git tag v1.2.0
git push origin v1.2.0
```

This triggers the GitHub Actions release workflow which:
1. Updates the version in `Directory.Build.props`
2. Builds self-contained executables for win-x64, win-arm64, linux-x64
3. Creates a GitHub Release with the zip artifacts

## Project Structure

```
SmsNotificationService/
├── Program.cs                          # Entry point, DI, config validation
├── appsettings.json                    # Production config (empty values)
├── appsettings.Development.json        # Dev config
├── src/
│   ├── Workers/
│   │   └── Worker.cs                   # Orchestration — startup, shutdown, queue
│   ├── Data/
│   │   └── NotificationRepository.cs   # DB reads/writes
│   ├── Services/
│   │   └── SmsApiService.cs            # HTTP calls with retry and backoff
│   ├── Models/
│   │   ├── SmsNotification.cs          # Entity model
│   │   └── NotificationStatus.cs       # Status enum
│   ├── Configuration/
│   │   └── SmsServiceOptions.cs        # Typed config
│   └── Checks/
│       └── DatabaseConnectionCheck.cs  # Startup DB check
├── Properties/
│   └── launchSettings.json
└── docs/
    └── plan.md                         # Feature plan
```

## API Payload

The SMS API receives raw data fields (snake_case):

```json
{
  "id": 4,
  "phone_number": "07130000000",
  "mpesa_code": "KA470213XK",
  "admission_no": "5551",
  "student_name": "Bryan Castillo",
  "amount": 2979.75,
  "receipt_no": "AGKO3X3FQ4",
  "dated": "2026-07-02T18:54:26"
}
```

## Logging

```
[App]      SmsNotificationService starting (Environment: Development)
[Config]   Configuration validated — API: https://api.munywele.co.ke/v1/send
[DB]       Connected to school on 127.0.0.1 (16.0.1000) in 42ms
[App]      SmsNotificationService ready
[Queue]    Found 3 pending notification(s)
[SMS]      Sending notification 1 to 07130000000 (attempt 1/3)
[SMS]      Sent notification 1 to 07130000000 — status updated to PROCESSED
[SMS]      Sending notification 2 to 07130000001 (attempt 1/3)
[SMS]      Notification 2 to 07130000001 failed — HTTP 503: Service Unavailable (attempt 1/3)
[SMS]      Retrying notification 2 in 2s...
[SMS]      Notification 2 to 07130000001 failed — retry 1/5 scheduled at 2026-07-14T06:05:00Z
[Listener] Query registered successfully. Waiting for table changes...
```

Log tags: `[App]`, `[Config]`, `[DB]`, `[Listener]`, `[Queue]`, `[SMS]`, `[Shutdown]`
