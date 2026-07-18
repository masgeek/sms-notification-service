# SmsNotificationService

A .NET 10 background worker service that listens to a SQL Server table for new SMS notifications and sends them via an external HTTP API.

## Architecture

```
SQL Server (sms_notifications table)
    |
    | SqlDependency (Service Broker)
    v
SqlDependencyListener / RetryPoller
    |
    v
NotificationProcessor (shared logic, thread-safe)
    |
    | INotificationRepository    ISmsSender
    v                            v
NotificationRepository          SmsApiService
    |                            |
    | Dapper                     | HttpClient
    v                            v
SQL Server                     SMS API
```

## Tech Stack

- .NET 10 Worker Service
- `Microsoft.Data.SqlClient` — SQL Server connectivity
- `Dapper` — lightweight ORM
- `SqlDependency` — real-time change notifications via Service Broker
- WPF System Tray App — service management and monitoring
- `H.NotifyIcon.Wpf` — tray icon library
- `xUnit` + `Moq` + `FluentAssertions` — unit testing

## Prerequisites

- .NET 10 SDK
- SQL Server (local or remote)
- SQL Server **Service Broker** enabled on the target database
- Access to an SMS API endpoint

## Setup

### Windows SmartScreen Warning

When you first run the installer, Windows SmartScreen may show a warning because the application doesn't have an established reputation yet. This is expected for new software.

**To run the installer:**

1. Click **"More info"**
2. Click **"Run anyway"**

> This warning disappears after the application builds download reputation, or can be eliminated by purchasing a code signing certificate (~$70/year from SSL.com).

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
    adm_no          NVARCHAR(50)    NOT NULL,
    stud_names      NVARCHAR(200)   NULL,
    amount          DECIMAL(18,2)   NULL,
    receipt_no      NVARCHAR(100)   NULL,
    dated           DATETIME        NULL,
    description     NVARCHAR(MAX)   NULL,
    status          NVARCHAR(20)    NOT NULL DEFAULT 'PENDING',
    max_retries     INT             NOT NULL DEFAULT 5,
    retry_count     INT             NOT NULL DEFAULT 0,
    retry_after     DATETIME        NULL,
    created_at      DATETIMEOFFSET  NULL,
    updated_at      DATETIMEOFFSET  NULL
);
```

### 3. Configure

Edit `appsettings.Development.json`:

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

| Config Key | Default | Description |
|---|---|---|
| `ConnectionString` | — | SQL Server connection string |
| `SmsApiUrl` | — | SMS API endpoint URL |
| `AuthorizationToken` | — | Bearer token for API auth |
| `RetryBackoffSeconds` | `30` | Base retry backoff in seconds |
| `RetryPollIntervalSeconds` | `30` | How often the retry poller checks for eligible notifications |
| `LogRetentionDays` | `7` | Days to keep log files before cleanup |
| `MaxLogFileSizeMb` | `10` | Max log file size before rotation |

### 4. Run

```bash
dotnet run
```

### 5. Install as Windows Service

**Installer (recommended):**

Download the latest release from [GitHub Releases](../../releases). Two installer variants are available:

- `SmsNotificationService-Setup-<version>.exe` — self-contained (no .NET runtime needed)
- `SmsNotificationService-Framework-Setup-<version>.exe` — framework-dependent (requires .NET 10 runtime)

Run the installer as Administrator. It will:

- Install files to `C:\Program Files\SmsNotificationService\`
- Prompt for database connection, API URL, and auth token
- Create the Windows Service (delayed auto-start)
- Write config to `C:\Program Files\SmsNotificationService\appsettings.Production.json`
- Register an Event Log source
- Configure service recovery (restart on failure)
- Optionally install the system tray app

**Manual:**

```bash
dotnet publish SmsNotificationService.csproj -c Release -r win-x64 --self-contained
sc create SmsNotificationService binPath="C:\path\to\publish\SmsNotificationService.exe" start=delayed-auto
sc start SmsNotificationService
```

> Full deployment guide: [docs/deployment.md](docs/deployment.md)

## How It Works

1. **Startup** — Validates configuration and database connectivity (10s timeout)
2. **Catch-up** — Processes any existing `PENDING` notifications before starting the listener
3. **SqlDependency listener** — Registers a schema-qualified SELECT query on `dbo.sms_notifications`
4. **Retry poller** — Periodically checks for notifications where `retry_after` has passed
5. **Process pending** — Fetches all `PENDING` notifications (externally re-queued notifications are always picked up)
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

- **SOLID architecture** — Interfaces (`INotificationRepository`, `ISmsSender`) enable testing and swapping implementations
- **3-component worker** — `NotificationProcessor` (shared logic), `TableChangeListener` (SqlDependency), `RetryPoller` (periodic polling)
- **Concurrency guard** — `SemaphoreSlim` prevents duplicate processing
- **Retry with backoff** — Configurable exponential backoff per notification
- **External re-queue support** — Notifications reset by external apps are always picked up
- **Startup catch-up** — Processes missed notifications on restart
- **Listener resilience** — Retries `SqlDependency` registration up to 5 times
- **DB connection timeout** — 10-second timeout on startup check
- **Graceful shutdown** — Waits up to 30s for in-flight sends
- **Typed configuration** — `IOptions<SmsServiceOptions>` with startup validation
- **File logging** — Daily rotation, configurable retention and max size
- **Error logging** — API error responses saved to `description` column for debugging
- **Null safety** — Nullable enabled with warnings-as-errors
- **Structured logging** — `[Tag]` prefixed logs for quick filtering

## CI/CD

Fully automated pipeline. No manual tagging required.

```
Tests (all branches)  ──>  Release (main only)
                              ├── Auto-generate tag from conventional commits
                              ├── Build win-x64 + Inno Setup installer
                              ├── Create/update GitHub Release
                              └── Upload both self-contained and framework-dependent installers
```

| Workflow | Trigger | What |
|---|---|---|
| `tests.yml` | All pushes | Build + unit tests + validate both installer scripts |
| `release.yml` | After tests pass on `main` | Auto-tag, build both installers, GitHub Release |

**Idempotent:** Re-running on the same commit republishes the existing release with updated artifacts.

## Versioning

Automatic. Versions are generated from conventional commits when tests pass on `main`:

- Commit messages following [Conventional Commits](https://www.conventionalcommits.org/) (`fix:`, `feat:`, `BREAKING CHANGE:`) drive version bumps
- The tag action creates an annotated tag (e.g., `1.2.3`)
- `Directory.Build.props` is updated automatically during the release build
- The installer receives the version via `/DMyAppVersion=<version>` at compile time

To manually trigger a release:

1. Go to **Actions > Release > Run workflow**
2. Select the `main` branch

## Testing

```bash
dotnet test
```

12 unit tests covering:
- `WorkerTests` — pending processing, success/failure flows, retry scheduling, concurrency
- `SmsApiServiceTests` — HTTP retry logic, success/failure, `CalculateRetryAfter` backoff

## Project Structure

```
SmsNotificationService/
├── Program.cs                              # Entry point, DI, config, file logging
├── Directory.Build.props                   # Centralized versioning (auto-updated by CI)
├── appsettings.json                        # Production config template
├── appsettings.Development.json            # Dev config
├── src/
│   ├── Workers/
│   │   ├── NotificationProcessor.cs        # Shared processing logic (thread-safe)
│   │   ├── TableChangeListener.cs          # SqlDependency real-time listener
│   │   └── RetryPoller.cs                  # Periodic polling for retry-eligible notifications
│   ├── Data/
│   │   ├── INotificationRepository.cs      # Data access contract
│   │   ├── NotificationRepository.cs       # DB reads/writes (Dapper)
│   │   └── SqlDependencyListener.cs        # Service Broker listener
│   ├── Services/
│   │   ├── ISmsSender.cs                   # SMS sending contract
│   │   └── SmsApiService.cs               # HTTP calls with retry
│   ├── Models/
│   │   ├── SmsNotification.cs              # Entity (PascalCase, Dapper-mapped)
│   │   └── NotificationStatus.cs           # Status enum
│   ├── Configuration/
│   │   └── SmsServiceOptions.cs            # Typed config
│   ├── Checks/
│   │   └── DatabaseConnectionCheck.cs      # Startup DB check (10s timeout)
│   └── Logging/
│       └── FileLoggerProvider.cs           # File logging with daily rotation
├── SmsNotificationService.Shared/
│   ├── SmsNotificationService.Shared.csproj
│   ├── Constants.cs                        # Service name, table name, paths
│   ├── ConfigPathResolver.cs               # Find config file (app dir → ProgramData)
│   ├── VersionHelper.cs                    # Assembly version info
│   ├── ConfigReader.cs                     # Load config values
│   └── StatusHelper.cs                     # Format status strings
├── SmsNotificationService.Tray/
│   ├── SmsNotificationService.Tray.csproj  # WPF WinExe
│   ├── App.xaml / App.xaml.cs              # WPF app entry, ShutdownMode
│   ├── TrayIcon.cs                         # GDI+ icons, context menu
│   ├── ServiceMonitor.cs                   # 3-tier service detection, control
│   ├── UpdateChecker.cs                    # GitHub Releases polling
│   ├── ConnectionValidator.cs              # DB/API/Broker connectivity checks
│   ├── StatusWindow.xaml / .cs             # Service status display
│   ├── LogViewer.xaml / .cs                # Log file tailing
│   ├── ConfigEditor.xaml / .cs             # Edit all SmsService settings
│   └── SendNotificationDialog.xaml / .cs   # Manual SMS insert
├── tests/
│   └── SmsNotificationService.Tests/
│       ├── WorkerTests.cs                  # Worker unit tests
│       └── SmsApiServiceTests.cs           # SMS service unit tests
├── installer/
│   ├── installer.iss                       # Self-contained installer
│   ├── installer-framework.iss             # Framework-dependent installer
│   └── code/
│       ├── globals.iss                     # Global variables, wizard pages
│       ├── utils.iss                       # RunCmd, BoolToStr, JsonEscape
│       ├── services.iss                    # Windows Service management
│       ├── eventlog.iss                    # Event Log helpers
│       ├── config.iss                      # Config writer
│       ├── wizard.iss                      # UI pages, validation
│       ├── install.iss                     # Install/upgrade logic
│       └── uninstall.iss                   # Uninstall logic
├── .github/workflows/
│   ├── tests.yml                           # Tests + validate both installers
│   ├── release.yml                         # Build both installers + GitHub Release
│   ├── create-release-pr.yml               # Auto PR develop→main
│   └── auto-review.yml                     # Auto-approve after checks pass
├── docs/
│   ├── deployment.md                       # Deployment guide
│   └── plan.md                             # Feature plan
├── publish.ps1                             # Self-contained publish script
├── publish-framework.ps1                   # Framework-dependent publish script
└── SmsNotificationService.slnx             # Solution file
```

## Tray App

The system tray app (`SmsNotificationService.Tray.exe`) provides real-time service management:

- **Status monitoring** — real-time service status, uptime, version, detection method
- **Service control** — start, stop, restart from the tray menu
- **Log viewer** — view and filter service log files
- **Send notification** — insert test notifications directly into the database
- **Config editor** — edit all settings with individual fields (server, database, user, password, API URL, token)
- **Connection validator** — test DB, API, and Service Broker connectivity in parallel
- **Update checker** — polls GitHub Releases every 4 hours for new versions
- **GDI+ styled icons** — anti-aliased circles: green (running), red (stopped), yellow (unknown)

The tray app is optional during installation and auto-starts on login via `HKCU\...\Run`.

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

**File logs** are written to `ProgramData\Munywele\SmsNotificationService\logs\` with daily rotation and configurable retention (default 7 days).

**Config location:** `C:\Program Files\SmsNotificationService\appsettings.Production.json` (app directory, not ProgramData).

**Console output:**

```
[App]      SmsNotificationService starting (Environment: Development)
[Config]   Configuration validated — API: https://api.munywele.co.ke/v1/send
[DB]       Connected to school on 127.0.0.1 (16.0.1000) in 42ms
[App]      SmsNotificationService ready
[Queue]    Found 3 pending notification(s)
[SMS]      Sending notification 1 to 07130000000 (attempt 1/3)
[SMS]      Sent notification 1 to 07130000000 — status updated to PROCESSED
[Listener] Query registered successfully. Waiting for table changes...
```

Log tags: `[App]`, `[Config]`, `[DB]`, `[Listener]`, `[Queue]`, `[SMS]`, `[Shutdown]`
