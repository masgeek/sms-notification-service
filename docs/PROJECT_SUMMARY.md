# Project Summary — SmsNotificationService

> **Purpose:** Comprehensive reference for any AI agent or developer resuming work on this project. Covers architecture, design decisions, file layout, conventions, known issues, and deployment pipeline.

---

## 1. What This Project Is

A **.NET 10 background worker service** that:

1. Listens to a SQL Server table (`sms_notifications`) for new `PENDING` rows using `SqlDependency` (Service Broker)
2. Sends SMS via an external HTTP API on notification insert
3. Retries failed sends with exponential backoff (±20% jitter)
4. Retired notifications are marked `CANCELLED` after exhausting `max_retries`
5. Ships with a **WPF system tray app** for monitoring and management
6. Deploys via **Inno Setup** installer (self-contained or framework-dependent variants)

---

## 2. Solution Structure

```
SmsNotificationService.slnx
├── SmsNotificationService.csproj              # Main worker service (net10.0)
├── SmsNotificationService.Shared/             # Shared class library (net10.0)
├── SmsNotificationService.Tray/               # WPF tray app (net10.0-windows)
├── SmsNotificationService.Console/            # Console monitor app (net10.0)
├── tests/SmsNotificationService.Tests/        # xUnit unit tests
├── installer/                                 # Inno Setup (two variants)
├── .github/workflows/                         # CI/CD pipelines
├── docs/                                      # Documentation
├── publish.ps1                                # Self-contained publish script
├── publish-framework.ps1                      # Framework-dependent publish script
└── Directory.Build.props                      # Centralized version (auto-updated by CI)
```

### Key Relationships

| Project | References | Notes |
|---------|-----------|-------|
| `SmsNotificationService` (main) | Shared | `DefaultItemExcludes` excludes `SmsNotificationService.Shared\**` to avoid duplicate assembly attributes |
| `SmsNotificationService.Tray` | Shared | WPF, `net10.0-windows`, `UseWPF`, `WinExe` |
| `SmsNotificationService.Console` | Shared | `net10.0`, `OutputType: Exe`, references Shared only |
| `SmsNotificationService.Shared` | Dapper, SqlClient, ServiceController | `NoWarn: CA1416` (Windows-only APIs used from net10.0 TFM) |
| `SmsNotificationService.Tests` | main, Moq, FluentAssertions, xUnit | |

---

## 3. Architecture

### Worker Service Components (src/)

```
Program.cs
  └─ Host.CreateApplicationBuilder(args)
       ├─ AddProductionConfig(environment)         # Loads config: ProgramData (Prod only) → app dir
       ├─ FileLoggerProvider                        # ProgramData\...\logs\, daily rotation, size rotation
       ├─ DapperMapper.Register()                   # snake_case ↔ PascalCase mapping
       ├─ AddSmsNotificationServices(config)        # DI registration (ServiceCollectionExtensions.cs)
       │    ├─ INotificationRepository → NotificationRepository (Singleton)
       │    ├─ SqlDependencyListener (Singleton)
       │    ├─ ISmsSender → SmsApiService (Singleton, named HttpClient "SmsApi")
       │    ├─ NotificationProcessor (Singleton)
       │    ├─ TableChangeListener (HostedService)
       │    └─ RetryPoller (HostedService)
       ├─ ValidateSmsServiceOptions()               # Startup validation
       └─ DatabaseConnectionCheck.RunAsync()        # 10s timeout
```

### Data Flow

```
SQL Server table change → SqlDependency → TableChangeListener → NotificationProcessor.ProcessPendingAsync()
                                                                    ↓
                                                              INotificationRepository.GetPendingAsync()
                                                                    ↓
                                                              ISmsSender.SendAsync(notification)
                                                                    ↓
                                                           On success: status → PROCESSED
                                                           On failure: retry_count++, retry_after set
                                                           Max retries exceeded: status → CANCELLED
                                                           Non-retryable error: status → CANCELLED immediately
```

### NotificationProcessor Thread Safety

`SemaphoreSlim(1, 1)` ensures only one batch runs at a time. If the lock is already held, `ProcessPendingAsync` returns immediately (fire-and-forget from SqlDependency callback).

### RetryPoller

Uses `PeriodicTimer` — waits for the first tick BEFORE processing. No pending notifications are processed during the initial wait interval. The `TableChangeListener` handles startup catch-up.

### SqlDependency Listener

- Uses schema-qualified table name: `dbo.sms_notifications` (required by SqlDependency)
- Re-registers after each change event (one-shot subscription pattern)
- Retries registration up to 5 times with exponential backoff

---

## 4. Configuration

### Config File Location

Config lives in the **app directory** (`{app}\appsettings.Production.json`), NOT in ProgramData. Logs remain in `ProgramData\Munywele\SmsNotificationService\logs\`.

### Config Loading Order (Program.cs → ConfigurationExtensions.AddProductionConfig)

1. `ProgramData\Munywele\SmsNotificationService\appsettings.Production.json` — ONLY if `environment == "Production"`
2. `{appDir}\appsettings.Development.json` — always checked
3. `{appDir}\appsettings.Production.json` — always checked

**Last writer wins** (app dir settings override ProgramData).

### SmsService Section (SmsServiceOptions)

```json
{
  "SmsService": {
    "ConnectionString": "Server=...;Database=...;User Id=...;Password=...;TrustServerCertificate=True;",
    "SmsApiUrl": "https://fees.munywele.co.ke/api/v1/notifications",
    "AuthorizationToken": "your-bearer-token",
    "RetryBackoffSeconds": 30,
    "RetryPollIntervalSeconds": 30,
    "LogRetentionDays": 7,
    "MaxLogFileSizeMb": 10
  }
}
```

| Key | Default | Notes |
|-----|---------|-------|
| `ConnectionString` | — | Required. `TrustServerCertificate=True` must be present (no Encrypt dropdown in installer) |
| `SmsApiUrl` | — | Required. Valid absolute URI |
| `AuthorizationToken` | — | Required. Sent as `Authorization: Bearer <token>` header |
| `RetryBackoffSeconds` | `30` | Must be > 0 |
| `RetryPollIntervalSeconds` | `30` | Must be > 0 |
| `LogRetentionDays` | `7` | Must be > 0 |
| `MaxLogFileSizeMb` | `10` | Must be > 0 |

### Environment Variables (Fallback)

`SmsService__ConnectionString`, `SmsService__SmsApiUrl`, etc. (use `__` separator).

---

## 5. Database Schema

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

- `retry_after` column is `DATETIME` (SQL Server local time, no timezone info)
- `created_at`/`updated_at` are `DATETIMEOFFSET`
- External apps reset notifications (status, retry_count, retry_after) directly via SQL
- Service Broker must be enabled: `ALTER DATABASE school SET ENABLE_BROKER;`

### Status Enum (NotificationStatus.cs)

| Value | Description |
|-------|-------------|
| `PENDING` | Initial state |
| `PROCESSED` | SMS sent successfully |
| `FAILED` | Reserved (not currently used) |
| `CANCELLED` | Exceeded max_retries or non-retryable error |

---

## 6. Key Design Decisions & Gotchas

### Dapper Mapping (src/Data/DapperMapper.cs)

- `CustomPropertyTypeMap` maps `snake_case` DB columns to `PascalCase` C# properties
- `admission_no` → `AdmNo` (explicit mapping)
- Call `DapperMapper.Register()` once at startup before any Dapper queries

### SqlDependency Requires Schema-Qualified Names

- `dbo.sms_notifications` — always use `dbo.` prefix
- Non-qualified names cause SqlDependency registration failures

### SendResult Pattern

```csharp
public sealed class SendResult
{
    public bool Success { get; private init; }
    public string? ErrorMessage { get; private init; }
    public bool Retryable { get; private init; }

    public static SendResult Ok() => new() { Success = true };
    public static SendResult Fail(string? errorMessage, bool retryable = false) =>
        new() { Success = false, ErrorMessage = errorMessage, Retryable = retryable };
}
```

- `ISmsSender.SendAsync()` is **single-attempt** — retry/backoff is the caller's responsibility
- `CalculateRetryAfter(int retryCount)` helper on `ISmsSender` for callers
- Non-retryable errors (e.g., 400 Bad Request) → status set to `CANCELLED` immediately
- Transient errors (5xx, 408) → `retryable: true`

### Error Logging

API error responses are saved to `description_json` column as JSON for debugging.

### Connection String

- Built manually in `ConfigReader.BuildConnectionString(server, database, userId, password)` — NOT via `SqlConnectionStringBuilder`
- Sets `TrustServerCertificate=True` without spaces (avoids SSL issues)
- No `Encrypt` dropdown in installer — was removed because `Mandatory` default broke SSL with untrusted certs

### API Connection Validation

`ConnectionValidator.ValidateApiAsync()` sends `Authorization: Bearer <token>` header.

---

## 7. Shared Project (SmsNotificationService.Shared)

Class library referenced by both main worker and tray app.

| File | Purpose |
|------|---------|
| `Constants.cs` | `ServiceName`, `TableName`, `SubDir`, `ConfigFileName` |
| `ConfigPathResolver.cs` | `GetProgramDataDir()`, `GetAppDir()`, `FindConfigFile()` (prioritizes app dir), `GetLogDir()` (ProgramData) |
| `VersionHelper.cs` | `GetCurrentVersion()` from assembly |
| `ConfigReader.cs` | `LoadConnectionString()` (reads `SmsService.ConnectionString`), `LoadApiUrl()`, `LoadAuthorizationToken()`, `ParseConnectionString()`, `BuildConnectionString(server, database, userId, password)` |
| `StatusHelper.cs` | `FormatStatus()`, `FormatUptime()`, `FormatDetection()` |

---

## 8. Tray App (SmsNotificationService.Tray)

WPF application (`net10.0-windows`) with system tray icon.

### Entry Point

- `App.xaml` — `ShutdownMode="OnExplicitShutdown"` (stays in tray when windows close)
- `Application.Current.Shutdown()` called from tray Exit menu item
- **No `Program.cs`** — entry point is auto-generated from `App.xaml` (WPF convention)

### Components

| File | Purpose |
|------|---------|
| `TrayIcon.cs` | TaskbarIcon, ContextMenu, GDI+ anti-aliased circle icons (green=running, red=stopped, yellow=unknown) |
| `ServiceMonitor.cs` | 3-tier detection: ServiceController (SCM) → Process.GetProcessesByName → NotRunning. Kills non-service instances on stop. |
| `UpdateChecker.cs` | Polls GitHub Releases API every 4 hours. Compares `tag_name` (v-prefix stripped) to assembly version. |
| `ConnectionValidator.cs` | Parallel DB/API/Broker checks. API uses Bearer token. |
| `StatusWindow.xaml/.cs` | Shows "Detected via" row indicating detection method |
| `LogViewer.xaml/.cs` | Log tailing with `FileShare.ReadWrite` (non-exclusive) |
| `ConfigEditor.xaml/.cs` | Reads/writes all `SmsService` settings. Individual fields (server, db, user, password). Token uses `TextBox` (not `PasswordBox`) for reliable Show/Hide toggle. |
| `SendNotificationDialog.xaml/.cs` | Manual SMS insert with `adm_no = "MANUAL"` sentinel |

### Key Libraries

- `H.NotifyIcon.Wpf` 2.2.0 — use `TaskbarIcon.ForceCreate()` for programmatic creation
- `TaskbarIcon.ShowNotification(title, message, NotificationIcon.xxx)` — NOT `ShowBalloonTip`
- `NotificationIcon` enum in `H.NotifyIcon.Core`: `None`, `Info`, `Warning`, `Error`
- `ContextMenu` from `System.Windows.Controls` (fully qualified in TrayIcon.cs)

### Known Gotchas

- **ImplicitUsings not working** with `net10.0-windows` TFM — all files use explicit `using` directives
- **Windows XAML warnings** — `WINDOWSXAML_ENABLE` may be missing from environment
- **WPF XAML elements** — null in constructors; defer XAML-dependent init to `Loaded` event
- **`SqlConnectionEncryptOption`** in SqlClient 7.x is a struct with `Mandatory`, `Optional`, `Strict` — cannot use in switch expressions

---

## 9. Installer

### Two Variants

| Installer | Output | AppId | Bundles From | Runtime Check |
|-----------|--------|-------|-------------|---------------|
| `installer.iss` | `SmsNotificationService-Setup-{ver}.exe` | `{B8E3F2A1-...}` | `build/service/` + `build/tray/` + `build/console/` | None (self-contained) |
| `installer-framework.iss` | `SmsNotificationService-Framework-Setup-{ver}.exe` | `{A1F2E3B4-...}` | `build/service-framework/` + `build/tray-framework/` + `build/console-framework/` | `CheckDotNetRuntime` (checks `dotnet --list-runtimes` for `Microsoft.NETCore.App 10`) |

### Modular Code Structure (installer/code/)

| File | Purpose | Dependencies |
|------|---------|--------------|
| `globals.iss` | Global variables, `InitializeSetup`, `ShouldInstallTrayApp`, `ShouldInstallConsoleApp` | — |
| `utils.iss` | `RunCmd`, `BoolToStr`, `JsonEscape` | — |
| `services.iss` | `ServiceExists`, `StopService`, `StartService`, `WaitForServiceState`, `DeleteService`, `ConfigureServiceDescription`, `ConfigureRecovery`, `ConfigureDelayedAutoStart`, `CheckDotNetRuntime` | `utils.iss` |
| `eventlog.iss` | `RegisterEventLog`, `RemoveEventLog` | — |
| `config.iss` | `WriteConfigurationFile` (writes to `{app}` dir, NOT ProgramData) | `utils.iss` |
| `wizard.iss` | `InitializeWizard`, `ShouldSkipPage`, `NextButtonClick` (config prompt, DB inputs, API inputs, tray app checkbox, console app checkbox, start-after-install checkbox) | `globals.iss`, `config.iss` |
| `install.iss` | `DoFreshInstall`, `DoUpgrade`, `DoPostUpgrade`, `CurStepChanged`, `MaybeStartTrayApp`, `MaybeStartConsoleApp`. `#ifdef FrameworkInstall` gates .NET runtime check. | All above |
| `uninstall.iss` | `DoUninstall`, `CurUninstallStepChanged`. Kills tray app and console app via `taskkill` before uninstall. | `services.iss`, `eventlog.iss` |

### Key Installer Details

- `PrivilegesRequired=admin` + `PrivilegesRequiredOverridesAllowed=dialog` → UAC elevation prompt
- `CloseApplications=force` — kills running instances during install
- Tray app and console app are **optional** — wizard pages with checkboxes; `[Files]` always copies binaries; `[Icons]` and `[Registry]` use `Check: ShouldInstallTrayApp`; console app launched via `MaybeStartConsoleApp` if selected
- `appsettings.Development.json` excluded from installer bundles (`Excludes` flag)
- Config file written to `{app}\appsettings.Production.json` (NOT ProgramData)
- **Inno Setup `#include` files must NOT have `;` comment headers** — causes "BEGIN expected" compilation error

### Build Commands

```bash
# Self-contained
./publish.ps1
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DMyAppVersion=1.2.3 installer\installer.iss

# Framework-dependent
./publish-framework.ps1
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DMyAppVersion=1.2.3 installer\installer-framework.iss
```

---

## 10. CI/CD Pipeline

### workflows/tests.yml (all branches)

```
.NET Tests (build, format check, unit tests, vulnerability scan)
     ↓
Build Tray App (publish, verify binary exists) + Build Console App (publish, verify binary exists)
     ↓
Validate Self-Contained Installer + Validate Framework-Dependent Installer (parallel)
     ↓
All Checks Passed (summary gate)
```

- Test results cached by SHA (`.test-passed` sentinel file)
- `actions/checkout@v7`, `actions/setup-dotnet@v6`, `actions/cache/restore@v6`, `actions/upload-artifact@v7`
- Both installer validation jobs create dummy `build/` folders and compile ISS scripts
- `Minionguyjpro/Inno-Setup-Action@v1.2.9` for CI validation

### workflows/release.yml (after tests pass on main)

```
Generate Version Tag (masgeek/github-tag-action@release, tag_prefix: "")
     ↓
Build and Package
  ├─ Update Directory.Build.props with version
  ├─ dotnet restore → build → publish.ps1 → publish-framework.ps1
  ├─ Zip build/ directory (service + tray + console)
  ├─ Build both installers via ISCC.exe
  ├─ Verify version matches (build\service\SmsNotificationService.exe --version)
  └─ Upload artifact
     ↓
Publish GitHub Release (ncipollo/release-action@v1.21.0)
  ├─ Uploads: zip, self-contained installer, framework-dependent installer
  └─ allowUpdates: true (idempotent)
```

### Other Workflows

| File | Purpose |
|------|---------|
| `create-release-pr.yml` | Triggers on `workflow_run` (Tests success on `develop`). Auto-creates PR `develop` → `main`. |
| `auto-review.yml` | Auto-approves PRs after tests pass |

---

## 11. Testing

```bash
dotnet test
```

**12 unit tests** in two files:

| File | Tests |
|------|-------|
| `WorkerTests.cs` | NotificationProcessor: pending processing, success/failure flows, retry scheduling, concurrency (SemaphoreSlim) |
| `SmsApiServiceTests.cs` | SmsApiService: HTTP retry logic, success/failure, `CalculateRetryAfter` backoff with ±20% jitter |

Test tolerance must account for jitter: e.g., 30s base → tolerance ≥15s.

---

## 12. Build & Publish

### publish.ps1 (Self-Contained)

```
./publish.ps1          # Publishes to build/service/, build/tray/, build/console/
./publish.ps1 -Clean   # Removes bin/obj/build first
```

### publish-framework.ps1 (Framework-Dependent)

```
./publish-framework.ps1   # Publishes to build/service-framework/, build/tray-framework/, build/console-framework/
```

### Version Support

```bash
publish\SmsNotificationService.exe --version   # Prints version from Directory.Build.props
```

`--version` / `-v` flag handled in Program.cs using `Shared.VersionHelper.GetCurrentVersion()`.

---

## 13. Package Versions

| Package | Version | Used In |
|---------|---------|---------|
| `Dapper` | 2.1.79 | Main, Shared, Tray |
| `Microsoft.Data.SqlClient` | 7.0.2 | Main, Shared, Tray |
| `Microsoft.Extensions.Hosting` | 10.0.10 | Main |
| `Microsoft.Extensions.Hosting.WindowsServices` | 10.0.10 | Main |
| `Microsoft.Extensions.Http` | 10.0.10 | Main |
| `H.NotifyIcon.Wpf` | 2.2.0 | Tray |
| `System.ServiceProcess.ServiceController` | 10.0.10 | Shared, Tray |
| `Microsoft.Extensions.Configuration.Json` | 10.0.10 | Tray |

All projects have `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>` with committed `packages.lock.json` files.

---

## 14. Git & Branching

- **Default branch:** `develop`
- **Main branch:** `main` (triggers release)
- PRs required (branch protection rules enforced)
- "All Checks Passed" status check required before merge
- Auto-tagging: `masgeek/github-tag-action@release` with `tag_prefix: ""` (no `v` prefix)
- Conventional commits drive version bumps

---

## 15. Known Issues / TODOs

All known issues have been resolved.

### Resolved

- ~~Config in ProgramData~~ → moved to app directory
- ~~PasswordBox toggle unreliable~~ → replaced with TextBox (read-only)
- ~~DB connection testing in installer~~ → removed (unreliable in Inno Setup)
- ~~Encrypt dropdown~~ → removed (Mandatory default broke SSL with untrusted certs)
- ~~Missing framework-dependent installer~~ → added `installer-framework.iss` + `publish-framework.ps1`
- ~~Shared project causing duplicate assembly attributes~~ → `DefaultItemExcludes` in main csproj
- ~~File sharing violations~~ → `FileShare.ReadWrite` on FileLogger and LogViewer
- ~~Startup pending notifications not processing~~ → resolved

---

## 16. File Index

### Main Worker Service

| File | Purpose |
|------|---------|
| `Program.cs` | Slim entry point, DI, config loading, DapperMapper.Register() |
| `src/Configuration/SmsServiceOptions.cs` | Typed config class |
| `src/Configuration/ConfigurationExtensions.cs` | AddProductionConfig(), ValidateSmsServiceOptions() |
| `src/ServiceCollectionExtensions.cs` | DI registration, named HttpClient "SmsApi" |
| `src/Data/DapperMapper.cs` | snake_case ↔ PascalCase mapping |
| `src/Data/INotificationRepository.cs` | Data access interface |
| `src/Data/NotificationRepository.cs` | Sealed, DB ops |
| `src/Data/SqlDependencyListener.cs` | Sealed, IDisposable, Service Broker listener |
| `src/Workers/NotificationProcessor.cs` | Shared logic, SemaphoreSlim, honors Retryable flag |
| `src/Workers/TableChangeListener.cs` | SqlDependency listener, startup catch-up |
| `src/Workers/RetryPoller.cs` | PeriodicTimer-based polling |
| `src/Services/ISmsSender.cs` | Sealed SendResult, SendAsync, CalculateRetryAfter |
| `src/Services/SmsApiService.cs` | Sealed, single-attempt, named HttpClient "SmsApi" |
| `src/Models/SmsNotification.cs` | Entity (PascalCase, Dapper-mapped) |
| `src/Models/NotificationStatus.cs` | Enum: PENDING, PROCESSED, FAILED, CANCELLED |
| `src/Checks/DatabaseConnectionCheck.cs` | Startup DB check (10s timeout) |
| `src/Logging/FileLoggerProvider.cs` | File logging, rotation, FileShare.ReadWrite |

### Shared Project

| File | Purpose |
|------|---------|
| `SmsNotificationService.Shared/Constants.cs` | ServiceName, TableName, SubDir, ConfigFileName |
| `SmsNotificationService.Shared/ConfigPathResolver.cs` | FindConfigFile (app dir first), GetProgramDataDir, GetAppDir, GetLogDir |
| `SmsNotificationService.Shared/VersionHelper.cs` | GetCurrentVersion from assembly |
| `SmsNotificationService.Shared/ConfigReader.cs` | LoadConnectionString (SmsService.ConnectionString), LoadApiUrl, LoadAuthorizationToken, ParseConnectionString, BuildConnectionString |
| `SmsNotificationService.Shared/StatusHelper.cs` | FormatStatus, FormatUptime, FormatDetection |

### Tray App

| File | Purpose |
|------|---------|
| `SmsNotificationService.Tray/App.xaml` + `App.xaml.cs` | WPF entry, ShutdownMode OnExplicitShutdown |
| `SmsNotificationService.Tray/TrayIcon.cs` | TaskbarIcon, ContextMenu, GDI+ icons |
| `SmsNotificationService.Tray/ServiceMonitor.cs` | 3-tier detection, KillProcesses on stop |
| `SmsNotificationService.Tray/UpdateChecker.cs` | GitHub Releases polling, uses Shared.VersionHelper |
| `SmsNotificationService.Tray/ConnectionValidator.cs` | Parallel DB/API/Broker checks |
| `SmsNotificationService.Tray/StatusWindow.xaml` + `.cs` | Status display |
| `SmsNotificationService.Tray/LogViewer.xaml` + `.cs` | Log tailing |
| `SmsNotificationService.Tray/ConfigEditor.xaml` + `.cs` | Edit SmsService settings |
| `SmsNotificationService.Tray/SendNotificationDialog.xaml` + `.cs` | Manual SMS insert |

### Installer

| File | Purpose |
|------|---------|
| `installer/installer.iss` | Self-contained installer main file |
| `installer/installer-framework.iss` | Framework-dependent installer main file |
| `installer/code/globals.iss` | Global variables, StartTrayPage, StartTrayAfter |
| `installer/code/utils.iss` | RunCmd, BoolToStr, JsonEscape |
| `installer/code/services.iss` | Windows Service management + CheckDotNetRuntime |
| `installer/code/eventlog.iss` | Event Log helpers |
| `installer/code/config.iss` | WriteConfigurationFile (writes to {app}) |
| `installer/code/wizard.iss` | Wizard pages: config prompt, DB inputs, API inputs, tray checkbox, start-after-install |
| `installer/code/install.iss` | Install/upgrade logic, MaybeStartTrayApp, #ifdef FrameworkInstall |
| `installer/code/uninstall.iss` | Kills tray app via taskkill, cleans logs in ProgramData |

### CI/CD

| File | Purpose |
|------|---------|
| `.github/workflows/tests.yml` | Tests + build tray app + validate both installers |
| `.github/workflows/release.yml` | Auto-tag, build both installers, GitHub Release |
| `.github/workflows/create-release-pr.yml` | Auto PR develop→main |
| `.github/workflows/auto-review.yml` | Auto-approve after checks pass |

### Documentation

| File | Purpose |
|------|---------|
| `README.md` | Full documentation (architecture, setup, features) |
| `docs/deployment.md` | Deployment guide (both installers, troubleshooting) |
| `docs/PROJECT_SUMMARY.md` | This file |
| `docs/fix-checklist.md` | Historical audit checklist (all items completed) |
| `docs/database-migration.md` | Multi-database migration guide |
| `installer/README.md` | Installer modular structure docs |
