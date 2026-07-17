# Tray App — Implementation Checklist

Temporary working document. Delete after implementation is complete.

---

## Phase 1: Project Scaffold ✅

- [x] Create `SmsNotificationService.Tray/SmsNotificationService.Tray.csproj`
  - SDK: `Microsoft.NET.Sdk`
  - OutputType: `WinExe`
  - TargetFramework: `net10.0-windows`
  - UseWPF: `true`
  - RuntimeIdentifiers: `win-x64`
  - Nullable: `enable`
  - RestorePackagesWithLockFile: `true`
  - Version from `Directory.Build.props`
- [x] Add NuGet packages:
  - `H.NotifyIcon.Wpf` 2.2.0 (tray icon)
  - `System.ServiceProcess.ServiceController` 10.0.10 (service control)
  - `Dapper` 2.1.79 (DB access — same version as main project)
  - `Microsoft.Data.SqlClient` 7.0.2 (DB access — same version as main project)
  - `Microsoft.Extensions.Configuration.Json` 10.0.10 (config loading)
  - ~~`CommunityToolkit.Mvvm`~~ — not needed, using manual DelegateCommand
- [x] ~~Create `Properties/AssemblyInfo.cs`~~ — version inherited from `Directory.Build.props`
- [x] Add tray project to `SmsNotificationService.slnx`
- [x] Create directory structure:
  ```
  SmsNotificationService.Tray/
  ├── App.xaml
  ├── App.xaml.cs
  ├── TrayIcon.cs
  ├── ServiceMonitor.cs
  ├── UpdateChecker.cs
  ├── VersionHelper.cs
  ├── ConnectionValidator.cs
  ├── StatusWindow.xaml / .cs
  ├── LogViewer.xaml / .cs
  ├── SendNotificationDialog.xaml / .cs
  ├── ConfigEditor.xaml / .cs
  ├── Models/
  │   └── ServiceStatusInfo.cs
  ├── Helpers/
  │   └── Paths.cs
  └── SmsNotificationService.Tray.csproj
  ```

---

## Phase 2: Core Infrastructure ✅

### 2a. Entry Point & WPF App

- [x] ~~`Program.cs`~~ — removed; WPF auto-generates entry point from App.xaml
- [x] `App.xaml` — WPF application, `ShutdownMode: OnExplicitShutdown` (stay in tray on window close)
- [x] `App.xaml.cs` — create `TrayIcon` on startup, exit cleanly on `OnExit`

### 2b. Paths Helper

- [x] `Helpers/Paths.cs` — centralized path constants:
  - `AppDataDir` → `CommonApplicationData\Munywele\SmsNotificationService`
  - `ConfigFile` → `appsettings.Production.json`
  - `LogDir` → `logs\`
  - `ServiceExe` → `SmsNotificationService.exe`

### 2c. Service Monitor

- [x] `ServiceMonitor.cs` — poll `ServiceController` every 10s:
  - Properties: `Status`, `Version`, `Uptime`, `LastCheck`
  - Event: `StatusChanged(ServiceStatusInfo)`
  - Methods: `StartService()`, `StopService()`, `RestartService()`
  - Uses `sc.exe` for Start/Stop (ServiceController doesn't work well for service control)
  - Restart is async with `Task.Delay(2000)` between stop/start
  - Tracks uptime from status transitions
- [x] `Models/ServiceStatusInfo.cs` — status DTO

---

## Phase 3: Tray Icon & Context Menu ✅

- [x] `TrayIcon.cs` — uses `H.NotifyIcon.Wpf.TaskbarIcon`:
  - Icon states: green (running), red (stopped), yellow (unknown/paused) — generated programmatically
  - ~~Embed icon resources~~ — using `Graphics.Clear(color)` bitmap generation instead
  - Tooltip: `SmsNotificationService — {status} (v{version})`
  - Context menu items:
    - Status → open `StatusWindow`
    - View Logs → open `LogViewer`
    - Send Notification → open `SendNotificationDialog`
    - Validate Connections → run `ConnectionValidator`
    - Settings → open `ConfigEditor`
    - Start / Stop / Restart Service
    - Check for Updates → manual update check
    - Exit → `Application.Current.Shutdown()`
  - Subscribe to `ServiceMonitor.StatusChanged` to update icon + tooltip
  - Subscribe to `UpdateChecker.UpdateAvailable` for notification
  - Balloon notification on service stopped unexpectedly
  - `DelegateCommand` implementation for ICommand binding

---

## Phase 4: Update Checker ✅

- [x] `UpdateChecker.cs`:
  - Poll GitHub Releases API every 4 hours: `https://api.github.com/repos/masgeek/sms-notification-service/releases/latest`
  - Compare `tag_name` with local version from `Assembly`
  - Event: `UpdateAvailable(string currentVersion, string latestVersion)`
  - Method: `CheckAsync()` (manual trigger)
  - Uses `HttpClient` with `User-Agent` header
  - Parses `tag_name` from JSON via `System.Text.Json`
  - Handles network errors silently
  - Stores last notified version to avoid repeated notifications

---

## Phase 5: Status Window ✅

- [x] `StatusWindow.xaml` + `StatusWindow.xaml.cs`:
  - Displays: status, uptime, version, last check time
  - Buttons: Start, Stop, Restart (enabled/disabled based on state)
  - Auto-refresh every 2 seconds via `DispatcherTimer`
  - Close button hides window (doesn't close app — tray stays)
  - Clean minimal layout

---

## Phase 6: Log Viewer ✅

- [x] `LogViewer.xaml` + `LogViewer.xaml.cs`:
  - Reads log files from `Paths.LogDir`
  - Displays last 500 lines, newest at bottom
  - Filter dropdown: All, App, Config, DB, Listener, Queue, SMS
  - Auto-refresh every 5 seconds via `DispatcherTimer`
  - Buttons: Refresh, Clear (display only), Export (clipboard), Open Folder
  - Reads most recent `.log` file

---

## Phase 7: Send Notification Dialog ✅

- [x] `SendNotificationDialog.xaml` + `SendNotificationDialog.xaml.cs`:
  - Fields: Phone Number, M-Pesa Code, Receipt No, Student Name, Amount, Admission No
  - Inserts row directly into `sms_notifications` table via Dapper
  - `adm_no` defaults to "MANUAL" sentinel
  - Validation: phone number required, mpesa code required
  - Shows success/failure message after insert
  - Service picks it up automatically via `TableChangeListener`
  - Loads connection string from `Paths.ConfigFile`

---

## Phase 8: Config Editor ✅

- [x] `ConfigEditor.xaml` + `ConfigEditor.xaml.cs`:
  - Loads `appsettings.Production.json` from `Paths.ConfigFile`
  - Editable fields:
    - **Database**: Server, Database, User ID, Password, Encrypt (dropdown: Mandatory/Optional/Strict)
    - **SMS API**: URL, Authorization Token (masked, Show/Hide toggle)
    - **Retry**: Backoff Seconds, Poll Interval Seconds
    - **Logging**: Retention Days, Max File Size (MB)
  - Uses `SqlConnectionStringBuilder` to parse/build connection string
  - Buttons: Test Connection, Save, Cancel
  - Saves with `System.Text.Json` (indented)
  - Offers service restart after save (Yes/No dialog)
  - Displays config file path at bottom
  - Defers XAML-dependent init to `Loaded` event (prevents null refs)

---

## Phase 9: Connection Validator ✅

- [x] `ConnectionValidator.cs`:
  - 3 parallel checks using `Task.WhenAll`:
    - **DB**: Open connection, measure latency, check `sms_notifications` table exists
    - **API**: HTTP GET to configured URL, measure latency, check status code
    - **Service Broker**: Query `sys.databases.is_broker_enabled`
  - Results cached for 30 seconds
- [x] `ValidationResult.cs` + `CheckResult.cs`:
  - `DbStatus`, `ApiStatus`, `BrokerStatus` (each `CheckResult`)
  - `AllPassed` computed property
  - `Summary` formatted string
- [x] Results displayed via balloon notification on tray icon

---

## Phase 10: Icons ✅

- [x] ~~Create/source icon files~~ — Icons generated programmatically at runtime
  - `TrayIcon.cs` uses `Graphics.Clear(Color)` to render green/red/yellow icons
  - No `.ico` files needed — saves 3 files, avoids static resource management
  - Icon refreshes automatically on status changes

---

## Phase 11: Installer Integration ✅

- [x] Update `installer/installer.iss`:
  - Added `TrayAppName` and `TrayAppDisplay` constants
  - Added `[Files]` entry for `publish-tray\*`
  - Added `[Icons]` Start Menu shortcut for tray app
  - Added `[Registry]` auto-start entry (`HKCU\...\Run`)
- [x] Update `installer/code/uninstall.iss`:
  - Added `RegDeleteValue` to remove auto-start registry entry
- [x] Installer now deploys both service and tray app together

---

## Phase 12: Build & Publish ✅

- [x] Publish commands documented in `installer/installer.iss` header:
  ```bash
  dotnet publish SmsNotificationService.csproj -c Release -r win-x64 --self-contained -o publish
  dotnet publish SmsNotificationService.Tray\SmsNotificationService.Tray.csproj -c Release -r win-x64 --self-contained -o publish-tray
  ```
- [x] Solution builds cleanly: 0 warnings, 0 errors
- [x] All three projects build successfully (main, tray, tests)
- [ ] Manual testing (verify tray app runs, service control, config editor) — requires Windows VM/device

---

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Target framework | `net10.0-windows` | Match main project |
| Tray icon library | `H.NotifyIcon.Wpf` 2.2.0 | Mature WPF tray support |
| Service control | `sc.exe` via `Process.Start` | `ServiceController` doesn't support Start/Stop for non-admin |
| Config path | `CommonApplicationData\Munywele\SmsNotificationService\appsettings.Production.json` | Same as main service |
| Log path | `CommonApplicationData\Munywele\SmsNotificationService\logs\` | Same as main service |
| Exit behavior | `ShutdownMode.OnExplicitShutdown` | Stay in tray when windows close |
| Manual notification `adm_no` | `"MANUAL"` sentinel | Consistent with existing pattern |
| Icon rendering | `Graphics.Clear(color)` bitmap | No need for .ico files at runtime |
| Installer auto-start | `[Registry]` section with `uninsdeletevalue` flag | Clean auto-start + auto-cleanup on uninstall |
| MVVM | Manual `DelegateCommand` | No extra dependency needed |
| Entry point | WPF auto-generated from App.xaml | Avoids CS0017 double-entry-point |

---

## Conventions Followed

- All classes `sealed` unless public surface required by WPF XAML
- File-scoped namespaces (`namespace X;`)
- PascalCase for properties and methods
- `_camelCase` for private fields
- `CancellationToken` propagation in async methods
- No `Thread.Sleep` — use `await Task.Delay`
- Dapper for DB access (same pattern as main project)
- `System.Text.Json` for JSON (not Newtonsoft)
- Explicit usings (no `ImplicitUsings` — SDK compatibility)

---

## Dependencies Reuse

Shared between main project and tray app (same NuGet versions):

| Package | Main Project | Tray App |
|---------|-------------|----------|
| Dapper | 2.1.79 | 2.1.79 |
| Microsoft.Data.SqlClient | 7.0.2 | 7.0.2 |
| H.NotifyIcon.Wpf | — | 2.2.0 |
| System.ServiceProcess.ServiceController | — | 10.0.10 |
| Microsoft.Extensions.Configuration.Json | — | 10.0.10 |

---

## Risk / Notes

- `ServiceController.Status` may throw if service does not exist — wrapped in try/catch
- GitHub API has rate limits (60/hr unauthenticated) — 4hr polling interval is safe
- Config editor must not write invalid JSON — validated before save
- Tray app runs as regular user (not admin) — service control uses `sc.exe` (requires permissions)
- `H.NotifyIcon.Wpf` 2.2.0 targets net9.0 — works on net10.0 via fallback
- Balloon notifications use `ShowNotification()` (not `ShowBalloonTip`) — H.NotifyIcon v2 API
