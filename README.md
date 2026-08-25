# FeeSyncer

FeeSyncer is a .NET 10 Windows application suite for SMS notification delivery
and school-system synchronization.

## Components

| Project | Purpose |
|---|---|
| `FeeSyncer.Sms` | Watches SQL Server for pending notifications and sends them to the central HTTP API |
| `FeeSyncer.Agent` | Synchronizes students and fees, records approved payments, and can update the local fee processor |
| `FeeSyncer.Tray` | Manages both Windows services, configuration, enrollment, logs, and diagnostics |
| `FeeSyncer.Console` | Console-based service monitor |
| `FeeSyncer.Shared` | Shared configuration, service-control, validation, update, and deployment support |

The SMS and school-agent workloads run as separate processes. A failure in one
does not stop the other.

## Architecture

### SMS delivery

```text
SQL Server sms_notifications
  -> SqlDependency (Service Broker) or retry timer
  -> NotificationProcessor
  -> HTTP POST /api/v1/notifications
```

`TableChangeListener` performs startup catch-up and receives one-shot
`SqlDependency` notifications. `RetryPoller` checks time-eligible retries.
`NotificationProcessor` serializes in-process batches with `SemaphoreSlim` and
processes at most 100 eligible rows per query.

### School integration

```text
MQTT work hint
  -> HTTP lease from the central gateway
  -> loopback school API
  -> resumable HTTP page uploads or payment completion
```

MQTT carries metadata-only wake hints. HTTP remains authoritative for leases,
records, completion, and failure reporting. The Agent polls HTTP periodically
whether MQTT is connected or not; MQTT only accelerates the next work check.
The same persistent MQTT connection publishes retained presence and bounded
operational events back to the central service.

## Requirements

- Windows x64
- SQL Server with Service Broker enabled
- .NET 10 SDK for development
- .NET 10 Runtime and Desktop Runtime for framework-dependent installation
- Access to `https://fees.munywele.co.ke/`
- A local school API at a loopback address for school integration

Enable Service Broker on the SMS database:

```sql
ALTER DATABASE school SET ENABLE_BROKER;
```

## Database Schema

The SMS service expects this SQL Server shape:

```sql
CREATE TABLE dbo.sms_notifications (
    id               BIGINT IDENTITY(1,1) PRIMARY KEY,
    phone_number     NVARCHAR(50) NOT NULL,
    mpesa_code       NVARCHAR(100) NOT NULL,
    adm_no           NVARCHAR(50) NOT NULL,
    stud_names       NVARCHAR(200) NULL,
    amount           DECIMAL(18,2) NULL,
    receipt_no       NVARCHAR(100) NULL,
    dated            DATETIME NULL,
    description_json NVARCHAR(MAX) NULL,
    status           NVARCHAR(20) NOT NULL DEFAULT 'PENDING',
    max_retries      INT NOT NULL DEFAULT 5,
    retry_count      INT NOT NULL DEFAULT 0,
    retry_after      DATETIME NULL,
    created_at       DATETIMEOFFSET NULL,
    updated_at       DATETIMEOFFSET NULL
);
```

The active statuses are `PENDING`, `PROCESSED`, and `CANCELLED`. `FAILED` is
reserved. Non-retryable HTTP failures are cancelled immediately; retryable
failures use exponential backoff with random +/-20 percent jitter.

## Configuration

Installed configuration is machine-wide:

| Component | Production file |
|---|---|
| SMS and tray | `C:\ProgramData\Munywele\FeeSyncer\appsettings.Production.json` |
| Agent | `C:\ProgramData\Munywele\FeeSyncer\agentsettings.json` |
| Logs | `C:\ProgramData\Munywele\FeeSyncer\logs\` |

Release builds load packaged `appsettings.json`, environment-specific JSON,
environment variables, command-line arguments, and then the ProgramData file.
The later ProgramData values win. Debug builds use development configuration
instead of the machine file. This Debug/Release choice is compile-time; it is
not controlled solely by `DOTNET_ENVIRONMENT`.

Use the tray application's **Settings** screen for normal configuration. The
files contain credentials as plain JSON, so restrict access to ProgramData and
never commit production values.

### SMS settings

```json
{
  "FeeSyncer": {
    "BaseUrl": "https://fees.munywele.co.ke/",
    "ApiEndpoints": {
      "SmsNotifications": "api/v1/notifications"
    }
  },
  "SmsService": {
    "ConnectionString": "Server=127.0.0.1;Database=school;User Id=sa;Password=...;TrustServerCertificate=True;",
    "AuthorizationToken": "...",
    "RetryBackoffSeconds": 30,
    "RetryPollIntervalSeconds": 30,
    "LogRetentionDays": 7,
    "MaxLogFileSizeMb": 10
  }
}
```

`SmsService:SmsApiUrl` is derived from `FeeSyncer:BaseUrl` and the configured
SMS endpoint when a base URL is present.

### Agent settings

```json
{
  "Agent": {
    "Enabled": true,
    "AgentToken": "fsk_...",
    "LocalApiBaseUrl": "http://127.0.0.1:8001/api/",
    "LocalApiUsername": "...",
    "LocalApiPassword": "...",
    "RequestTimeoutSeconds": 30,
    "IdleDelaySeconds": 5,
    "WorkPollSeconds": 30,
    "LongPollSeconds": 10,
    "HeartbeatSeconds": 60,
    "LeaseRenewalSeconds": 30,
    "MqttEnabled": true,
    "MqttBrokerHost": "wss://mqtt.munywele.co.ke/mqtt",
    "MqttBrokerPort": 443,
    "MqttBrokerPath": "/mqtt",
    "MqttUseTls": true,
    "MqttUsername": "",
    "MqttPassword": "",
    "MqttTopicPrefix": "fee-syncer/agent",
    "MqttClientId": "",
    "MqttSessionExpirySeconds": 86400,
    "MqttHealthSeconds": 60,
    "MqttKeepAliveSeconds": 30,
    "MqttReconnectMinSeconds": 1,
    "MqttReconnectMaxSeconds": 60
  }
}
```

See [School Integration Agent](docs/school-integration.md) for enrollment,
gateway operations, local API requirements, and MQTT details.

## Enrollment

1. Generate a single-use `enroll_...` code for the school in the central admin interface.
2. Open **Settings > School Agent** in the tray application.
3. Enter the enrollment code, agent name, and local API credentials.
4. Select **Enroll / Re-enroll**.

The tray exchanges the code at `POST /api/agent/enroll`, stores the returned
`fsk_...` token in `agentsettings.json`, and restarts the Agent service. The
installer does not handle enrollment. Enrollment codes expire after 15 minutes
and are not runtime credentials.

## Development

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet run --project src/Sms/FeeSyncer.Sms.csproj
dotnet run --project src/Agent/FeeSyncer.Agent.csproj
dotnet run --project src/Tray/FeeSyncer.Tray.csproj
```

The repository currently contains 69 xUnit tests:

| Test project | Count |
|---|---:|
| SMS | 18 |
| Agent | 33 |
| Tray | 18 |

Tests do not require a live SQL Server, SMS provider, MQTT broker, or school API.

## Build and Install

```powershell
./publish.ps1
./publish-framework.ps1
```

The scripts publish SMS, Agent, Tray, and Console applications. Build the Inno
Setup installers with:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DMyAppVersion=1.2.3 installer\installer.iss
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DMyAppVersion=1.2.3 installer\installer-framework.iss
```

The installer places binaries under `C:\Program Files\FeeSyncer`, creates both
Windows services as manual and stopped, and can launch the tray with `--setup`.
Configure and start services from the Control Panel after installation.

See [Deployment Guide](docs/deployment.md) for layouts, service commands,
upgrades, logging, and troubleshooting.

## Tray Application

The tray application provides:

- A combined Control Panel for `FeeSyncer.Sms` and `FeeSyncer.Agent`
- Start, stop, restart, install, and uninstall controls
- SMS and agent configuration
- Agent enrollment and MQTT diagnostics
- Fee Processor update configuration and manual update execution
- Log viewing and manual SMS insertion
- Connection validation and release checks
- Hash-verified installer download and elevated self-update with service-state restoration
- An About dialog available from the Control Panel and tray menu

The current installer uses an all-users Startup-folder shortcut when tray
startup is selected.

## CI/CD

- `.github/workflows/tests.yml` runs on non-documentation pushes and manual dispatch.
- `.github/workflows/agent-tests.yml` runs Agent tests on pushes to `main`/`develop` and on pull requests.
- Successful Tests runs on `develop` can create or update a release PR to `main`.
- Successful Tests runs on `main` can publish four ZIPs, two installers, and an update manifest to the public GitHub release, while the two installer executables are also published to the public S3 update channel.

Public update metadata is available at
`https://s3.munywele.co.ke/fee-syncer/latest.json`. Versioned artifacts are
limited to the two installer executables under
`https://s3.munywele.co.ke/fee-syncer/<version>/`. S3 write
credentials are repository secrets and are never embedded in the application.
If the S3 manifest is unavailable or invalid, clients fall back to the public
GitHub release `latest.json`; both channels require exact size and SHA-256
verification before installation.

Versions are generated from conventional commits during release. The release
workflow updates `Directory.Build.props` only in its build workspace.

## Documentation

- [Deployment Guide](docs/deployment.md)
- [School Integration Agent](docs/school-integration.md)
- [Project Reference](docs/PROJECT_SUMMARY.md)
- [Tray Application](docs/tray-app-plan.md)
- [Database Migration Notes](docs/database-migration.md)
