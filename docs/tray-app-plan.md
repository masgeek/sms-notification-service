now # System Tray Application — Plan

## Overview

A lightweight WPF system tray application that monitors the SmsNotificationService and provides a UI for status, logs, and update notifications.

## Architecture

```
SmsNotificationService.Tray/
├── Program.cs                    # Entry point
├── App.xaml                      # WPF app (hidden window)
├── TrayIcon.cs                   # System tray icon + context menu
├── ServiceMonitor.cs             # Polls Windows Service status
├── UpdateChecker.cs              # Checks GitHub Releases for new versions
├── StatusWindow.cs               # Click-to-open status panel
├── LogViewer.cs                  # Tail log files from ProgramData
├── SendNotificationDialog.cs     # Manual SMS send form
├── ConfigEditor.cs               # Edit appsettings.Production.json
├── ConnectionValidator.cs        # Test DB and API connectivity
└── SmsNotificationService.Tray.csproj
```

## Features

### 1. Tray Icon

- Green icon = service running
- Red icon = service stopped
- Yellow icon = service paused/unknown
- Tooltip: `SmsNotificationService — Running (v1.2.3)`
- Right-click context menu:
  - `Status` — opens status window
  - `View Logs` — opens log viewer
  - `Send Notification` — opens send form
  - `Validate Connections` — runs DB/API checks
  - `Settings` — opens config editor
  - `Start Service` — starts the service
  - `Stop Service` — stops the service
  - `Restart Service` — stops then starts
  - `Check for Updates` — manual update check
  - `Exit` — minimize to tray (or close)

### 2. Service Status Monitor

```
┌─────────────────────────────────────────┐
│ SmsNotificationService                  │
│                                         │
│ Status:     Running                     │
│ Uptime:     3 days 14 hours             │
│ Version:    1.2.3                       │
│ Last Check: 2026-07-16 14:32:05         │
│                                         │
│ [Start]  [Stop]  [Restart]             │
└─────────────────────────────────────────┘
```

- Polls `sc query SmsNotificationService` every 10 seconds
- Shows status, uptime, version
- Start/Stop/Restart buttons

### 3. Update Checker

- Polls GitHub Releases API every 4 hours
- Compares remote tag with local `--version` output
- Shows tray balloon notification when update available:
  ```
  Update Available
  New version 1.3.0 is available.
  Current version: 1.2.3
  [View] [Dismiss]
  ```
- "View" opens the releases page in browser
- "Download" saves the installer to a temp folder and runs it

### 4. Log Viewer

```
┌─────────────────────────────────────────┐
│ Logs — SmsNotificationService           │
│                                         │
│ [2026-07-16 14:30:01] [App] Service...  │
│ [2026-07-16 14:30:02] [DB] Connected... │
│ [2026-07-16 14:30:03] [SMS] Sending...  │
│ [2026-07-16 14:30:04] [SMS] Sent...     │
│                                         │
│ [Refresh]  [Clear]  [Export]            │
└─────────────────────────────────────────┘
```

- Reads log files from `ProgramData\Munywele\SmsNotificationService\logs\`
- Auto-refresh every 5 seconds
- Filter by tag: `[App]`, `[Config]`, `[DB]`, `[Listener]`, `[Queue]`, `[SMS]`
- Export to clipboard or file

### 5. Send Notification

```
╔══════════════════════════════════════════════╗
║  Send Notification                          ║
║                                             ║
║  Phone Number:  [07130000000            ]   ║
║  M-Pesa Code:   [SHK1ABCD1234          ]   ║
║  Receipt No:    [RCPT-2026-001         ]   ║
║  Student Name:  [John Doe              ]   ║
║  Amount:        [1500                   ]   ║
║                                             ║
║              [Send]  [Cancel]               ║
╚══════════════════════════════════════════════╝
```

- Opens from context menu: `Send Notification`
- Inserts a row directly into the `sms_notifications` table via the same DB connection
- Fields: `phone_number`, `mpesa_code`, `receipt_no`, `stud_names`, `adm_no`, `amount`
- Sets `status = PENDING`, `created_at = NOW()`, `retry_count = 0`
- Shows success/failure toast after insert
- The service picks it up automatically via the TableChangeListener

### 6. Config Editor

```
╔══════════════════════════════════════════════╗
║  Configuration — SmsNotificationService     ║
║─────────────────────────────────────────────║
║                                             ║
║  Database                                    ║
║  Connection String:                          ║
║  ┌─────────────────────────────────────────┐║
║  │ Server=localhost;Database=school;...    │║
║  └─────────────────────────────────────────┘║
║                                             ║
║  SMS API                                    ║
║  Base URL:  [https://api.sms-provider.com]  ║
║  API Key:   [****]  [Show]                  ║
║                                             ║
║  Retry Settings                             ║
║  Backoff Seconds:    [30]                   ║
║  Poll Interval (s):  [10]                   ║
║  Max Retries:        [3]                    ║
║                                             ║
║  Logging                                    ║
║  Retention Days:     [7]                    ║
║  Max File Size (MB): [50]                   ║
║                                             ║
║  [Test Connection]  [Save]  [Cancel]        ║
╚══════════════════════════════════════════════╝
```

- Opens from context menu: `Settings`
- Loads current `appsettings.Production.json` from `ProgramData\Munywele\SmsNotificationService\`
- Editable fields with validation (required, numeric, URL format)
- API Key field masked by default, "Show" toggle
- `Test Connection` button validates DB connectivity before save
- `Save` writes to file, shows confirmation
- Service must be restarted after config changes (offers restart option)
- Config file path displayed at bottom for reference

### 7. Connection Validator

```
╔══════════════════════════════════════════════╗
║  Connection Validation                      ║
║─────────────────────────────────────────────║
║                                             ║
║  ✅ Database         Connected (12ms)       ║
║     Server: localhost                       ║
║     Database: school                        ║
║     Tables: sms_notifications (exists)      ║
║                                             ║
║  ✅ SMS API          Reachable (245ms)      ║
║     Endpoint: https://api.sms-provider.com  ║
║     Status Code: 200 OK                     ║
║                                             ║
║  ⏳ Service Broker   Active                 ║
║     Queue: sms_notifications_queue          ║
║                                             ║
║  [Retry]  [Close]                           ║
╚══════════════════════════════════════════════╝
```

- Opens from context menu: `Validate Connections`
- Runs 3 independent checks in parallel:

#### DB Check
```
✅ Connected (12ms)
Server: localhost
Database: school
Tables: sms_notifications (exists)
```

#### API Check
```
✅ Reachable (245ms)
Endpoint: https://api.sms-provider.com
Status Code: 200 OK
```

#### Service Broker Check
```
✅ Active
Queue: sms_notifications_queue
Message Count: 0
```

- Each check shows response time
- Retry button re-runs all checks
- Results cached for 30 seconds to avoid spamming

## Tech Stack

| Component | Technology |
|-----------|-----------|
| UI Framework | WPF (.NET 8+) |
| Tray Icon | `H.NotifyIcon.Wpf` (NuGet) |
| Service Control | `System.ServiceProcess.ServiceController` |
| Update Check | `HttpClient` + GitHub Releases API |
| DB Access | `Dapper` + `Microsoft.Data.SqlClient` (same as service) |
| Config | `Microsoft.Extensions.Configuration` + `System.Text.Json` |
| MVVM | `CommunityToolkit.Mvvm` (optional, for data binding) |

## Dependencies

```xml
<ItemGroup>
  <PackageReference Include="H.NotifyIcon.Wpf" Version="2.*" />
  <PackageReference Include="System.ServiceProcess.ServiceController" Version="8.*" />
  <PackageReference Include="Dapper" Version="2.*" />
  <PackageReference Include="Microsoft.Data.SqlClient" Version="5.*" />
  <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="8.*" />
</ItemGroup>
```

## Project Setup

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <TrimmerRootAssembly Include="H.NotifyIcon.Wpf" />
  </PropertyGroup>
</Project>
```

## Key Classes

### ServiceMonitor.cs

```csharp
public class ServiceMonitor
{
    private readonly string _serviceName;
    private readonly Timer _timer;

    public ServiceStatus Status { get; private set; }
    public event Action<ServiceStatus>? StatusChanged;

    public ServiceMonitor(string serviceName)
    {
        _serviceName = serviceName;
        _timer = new Timer(Poll, null, 0, 10_000); // 10 seconds
    }

    private void Poll(object? state)
    {
        using var controller = new ServiceController(_serviceName);
        var newStatus = controller.Status;
        if (newStatus != Status)
        {
            Status = newStatus;
            StatusChanged?.Invoke(newStatus);
        }
    }

    public void Start() => Execute("start");
    public void Stop() => Execute("stop");
    public void Restart() { Stop(); Thread.Sleep(2000); Start(); }

    private void Execute(string action)
    {
        Process.Start("sc.exe", $"{action} {_serviceName}");
    }
}
```

### UpdateChecker.cs

```csharp
public class UpdateChecker
{
    private readonly string _repoUrl;
    private readonly Timer _timer;

    public event Action<string, string>? UpdateAvailable; // (currentVersion, latestVersion)

    public UpdateChecker(string repoUrl)
    {
        _repoUrl = repoUrl;
        _timer = new Timer(Check, null, 0, 4 * 60 * 60 * 1000); // 4 hours
    }

    private async void Check(object? state)
    {
        var current = GetCurrentVersion(); // from --version
        var latest = await GetLatestVersion(); // from GitHub API
        if (latest != null && latest != current)
            UpdateAvailable?.Invoke(current, latest);
    }

    private string GetCurrentVersion()
    {
        var psi = new ProcessStartInfo("SmsNotificationService.exe", "--version")
        {
            RedirectStandardOutput = true, UseShellExecute = false
        };
        return Process.Start(psi)!.StandardOutput.ReadToEnd().Trim();
    }

    private async Task<string?> GetLatestVersion()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new("app", "SmsNotificationService"));
        var json = await http.GetStringAsync($"{_repoUrl}/releases/latest");
        // parse tag_name from JSON
        return ExtractVersion(json);
    }
}
```

### TrayIcon.cs

```csharp
public class TrayIcon
{
    private readonly TaskbarIcon _icon;
    private readonly ServiceMonitor _monitor;
    private readonly UpdateChecker _updater;
    private readonly ConnectionValidator _validator;

    public TrayIcon()
    {
        _monitor = new ServiceMonitor("SmsNotificationService");
        _updater = new UpdateChecker("https://github.com/masgeek/sms-notification-service");
        _validator = new ConnectionValidator(config);

        _icon = new TaskbarIcon
        {
            Icon = GetIcon(_monitor.Status),
            ToolTipText = "SmsNotificationService"
        };

        _icon.ContextMenu = BuildMenu();
        _monitor.StatusChanged += OnStatusChanged;
        _updater.UpdateAvailable += OnUpdateAvailable;
        _validator.ValidationCompleted += OnValidationCompleted;
    }

    private void OnStatusChanged(ServiceStatus status)
    {
        _icon.Icon = GetIcon(status);
        _icon.ToolTipText = $"SmsNotificationService — {status}";
    }

    private void OnUpdateAvailable(string current, string latest)
    {
        _icon.ShowBalloonTip("Update Available",
            $"New version {latest} is available.\nCurrent: {current}",
            BalloonIcon.Info);
    }

    private void OnValidationCompleted(ValidationResult result)
    {
        var level = result.AllPassed ? BalloonIcon.Info : BalloonIcon.Warning;
        _icon.ShowBalloonTip("Connection Validation",
            result.Summary, level);
    }
}
```

### SendNotificationDialog.cs

```csharp
public class SendNotificationDialog
{
    private readonly string _connectionString;

    public SendNotificationDialog(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<bool> SendAsync(string phone, string mpesaCode,
        string receiptNo, string studentName, decimal amount)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"INSERT INTO sms_notifications
            (phone_number, mpesa_code, receipt_no, stud_names, adm_no,
             status, created_at, retry_count)
            VALUES (@Phone, @MpesaCode, @ReceiptNo, @StudentName, @AdmNo,
                    'PENDING', GETDATE(), 0)";

        var rows = await connection.ExecuteAsync(sql, new
        {
            Phone = phone,
            MpesaCode = mpesaCode,
            ReceiptNo = receiptNo,
            StudentName = studentName,
            AdmNo = "MANUAL" // sentinel for manual inserts
        });

        return rows > 0;
    }
}
```

### ConfigEditor.cs

```csharp
public class ConfigEditor
{
    private readonly string _configPath;
    private Dictionary<string, object?> _config = new();

    public ConfigEditor(string configPath)
    {
        _configPath = configPath;
        Load();
    }

    public void Load()
    {
        if (File.Exists(_configPath))
        {
            var json = File.ReadAllText(_configPath);
            _config = JsonSerializer.Deserialize<Dictionary<string, object?>>(json) ?? new();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_configPath, json);
    }

    public string GetConnectionString() =>
        _config["ConnectionStrings"]?.ToString() ?? "";

    public void SetConnectionString(string value) =>
        _config["ConnectionStrings"] = value;
}
```

### ConnectionValidator.cs

```csharp
public class ConnectionValidator
{
    private readonly ConfigEditor _config;
    private DateTime _lastValidation = DateTime.MinValue;
    private ValidationResult? _lastResult;

    public event Action<ValidationResult>? ValidationCompleted;

    public ConnectionValidator(ConfigEditor config)
    {
        _config = config;
    }

    public async Task<ValidationResult> ValidateAsync()
    {
        if (DateTime.UtcNow - _lastValidation < TimeSpan.FromSeconds(30)
            && _lastResult != null)
            return _lastResult;

        var result = new ValidationResult();

        // DB check
        try
        {
            var sw = Stopwatch.StartNew();
            using var conn = new SqlConnection(_config.GetConnectionString());
            await conn.OpenAsync();
            sw.Stop();
            result.DbStatus = new CheckResult
            {
                Passed = true,
                ResponseTime = sw.ElapsedMilliseconds,
                Details = $"Connected ({sw.ElapsedMilliseconds}ms)"
            };
        }
        catch (Exception ex)
        {
            result.DbStatus = new CheckResult
            {
                Passed = false,
                Details = ex.Message
            };
        }

        // API check
        try
        {
            var sw = Stopwatch.StartNew();
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var baseUrl = _config.GetSetting("SmsService:BaseUrl");
            var response = await http.GetAsync(baseUrl);
            sw.Stop();
            result.ApiStatus = new CheckResult
            {
                Passed = response.IsSuccessStatusCode,
                ResponseTime = sw.ElapsedMilliseconds,
                Details = $"{(int)response.StatusCode} {response.ReasonPhrase}"
            };
        }
        catch (Exception ex)
        {
            result.ApiStatus = new CheckResult
            {
                Passed = false,
                Details = ex.Message
            };
        }

        // Service Broker check
        try
        {
            using var conn = new SqlConnection(_config.GetConnectionString());
            await conn.OpenAsync();
            var brokerActive = await conn.QuerySingleAsync<bool>(
                "SELECT is_broker_enabled FROM sys.databases WHERE name = DB_NAME()");
            result.BrokerStatus = new CheckResult
            {
                Passed = brokerActive,
                Details = brokerActive ? "Active" : "Disabled"
            };
        }
        catch (Exception ex)
        {
            result.BrokerStatus = new CheckResult
            {
                Passed = false,
                Details = ex.Message
            };
        }

        _lastValidation = DateTime.UtcNow;
        _lastResult = result;
        ValidationCompleted?.Invoke(result);
        return result;
    }
}
```

### ValidationResult.cs

```csharp
public class ValidationResult
{
    public CheckResult DbStatus { get; set; } = new();
    public CheckResult ApiStatus { get; set; } = new();
    public CheckResult BrokerStatus { get; set; } = new();

    public bool AllPassed =>
        DbStatus.Passed && ApiStatus.Passed && BrokerStatus.Passed;

    public string Summary => string.Join("\n", new[]
    {
        $"Database: {(DbStatus.Passed ? "✅" : "❌")} {DbStatus.Details}",
        $"SMS API: {(ApiStatus.Passed ? "✅" : "❌")} {ApiStatus.Details}",
        $"Broker: {(BrokerStatus.Passed ? "✅" : "❌")} {BrokerStatus.Details}"
    });
}

public class CheckResult
{
    public bool Passed { get; set; }
    public long ResponseTime { get; set; }
    public string Details { get; set; } = "";
}
```

## UI Screens

### Status Window
```
╔══════════════════════════════════════════════╗
║  SmsNotificationService                     ║
║                                             ║
║  ● Status:    Running                       ║
║  ⏱  Uptime:    3 days 14 hours              ║
║  v Version:    1.2.3                        ║
║  🕐 Last Check: 14:32:05                    ║
║                                             ║
║  [▶ Start]  [■ Stop]  [↻ Restart]          ║
╚══════════════════════════════════════════════╝
```

### Log Viewer
```
╔══════════════════════════════════════════════╗
║  Logs — SmsNotificationService    [▼ Filter]║
║─────────────────────────────────────────────║
║ 14:30:01 [App]     Service starting...      ║
║ 14:30:02 [DB]      Connected to school      ║
║ 14:30:03 [SMS]     Sending to 07130000000   ║
║ 14:30:04 [SMS]     Sent — PROCESSED         ║
║ 14:30:05 [Listener] Waiting for changes...  ║
║─────────────────────────────────────────────║
║ [Refresh]  [Clear]  [Export]  [Open Folder] ║
╚══════════════════════════════════════════════╝
```

### Update Notification (balloon)
```
╔══════════════════════════════════════╗
║  📦 Update Available                ║
║                                     ║
║  New version 1.3.0 is available.    ║
║  Current version: 1.2.3             ║
║                                     ║
║  [View]  [Download]  [Dismiss]      ║
╚══════════════════════════════════════╝
```

### Send Notification
```
╔══════════════════════════════════════╗
║  Send Notification                  ║
║─────────────────────────────────────║
║  Phone:    [07130000000          ]  ║
║  M-Pesa:   [SHK1ABCD1234        ]  ║
║  Receipt:  [RCPT-2026-001       ]  ║
║  Student:  [John Doe            ]  ║
║  Amount:   [1500                 ]  ║
║─────────────────────────────────────║
║        [Send]  [Cancel]             ║
╚══════════════════════════════════════╝
```

### Config Editor
```
╔══════════════════════════════════════╗
║  Settings                           ║
║─────────────────────────────────────║
║  DB: [Server=localhost;Database=..] ║
║  API URL: [https://api.sms...    ]  ║
║  API Key: [****] [Show]             ║
║  Backoff:  [30]  Retries: [3]      ║
║  Log Days: [7]   Max Size: [50]    ║
║─────────────────────────────────────║
║  [Test]  [Save]  [Cancel]           ║
╚══════════════════════════════════════╝
```

### Connection Validator
```
╔══════════════════════════════════════╗
║  Connection Validation              ║
║─────────────────────────────────────║
║  ✅ Database     Connected (12ms)  ║
║  ✅ SMS API      Reachable (245ms) ║
║  ✅ Broker       Active            ║
║─────────────────────────────────────║
║  [Retry]  [Close]                   ║
╚══════════════════════════════════════╝
```

## Behavior

| Scenario | Action |
|----------|--------|
| Service stops unexpectedly | Red icon + balloon notification |
| Service starts | Green icon |
| Update available | Balloon notification every 4 hours |
| User clicks tray icon | Open status window |
| User right-clicks tray icon | Context menu |
| User clicks "Exit" | Minimize to tray (service keeps running) |
| User clicks "Download Update" | Download installer, prompt to run |
| User sends notification | Insert row into DB, service processes it |
| User saves config | Write to ProgramData, offer service restart |
| Connection fails | Show red X with error details in validator |

## Build & Publish

```bash
dotnet publish SmsNotificationService.Tray.csproj -c Release -r win-x64 --self-contained -o publish-tray
```

The tray app can be:
1. **Standalone** — user runs it manually
2. **Bundled with installer** — Inno Setup installs both service + tray
3. **Startup entry** — added to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

## Effort Estimate

| Task | Days |
|------|------|
| Project setup + tray icon | 0.5 |
| Service monitor (polling + control) | 1 |
| Update checker (GitHub API) | 0.5 |
| Status window UI | 1 |
| Log viewer UI | 1 |
| Send notification form + DB insert | 0.5 |
| Config editor (load/edit/save JSON) | 1 |
| Connection validator (DB/API/Broker) | 1 |
| Installer integration | 0.5 |
| Testing | 0.5 |
| **Total** | **7.5 days** |

## Future Enhancements

- **Notification toast** — Windows 10/11 toast notifications for SMS sent/failed
- **CPU/memory graph** — real-time resource usage
- **Multi-service support** — monitor multiple services
- **Auto-update** — download and install updates silently
- **Notification history** — query and filter past notifications
- **Batch send** — import CSV of notifications to send
