# FeeSyncer Project Reference

This document describes the implementation as of August 2026. Use source code
as the authority when behavior changes.

## Solution

`FeeSyncer.slnx` contains five product projects and three test projects:

```text
src/
|-- Sms/       SQL Server notification worker
|-- Agent/     School integration worker
|-- Shared/    Shared support library
|-- Tray/      WPF management application
`-- Console/   Console monitor
tests/
|-- Sms/
|-- Agent/
`-- Tray/
```

All product projects target .NET 10. Tray targets `net10.0-windows` and uses
WPF. The current repository version is defined in `Directory.Build.props`.

## SMS Service

Startup in `src/Sms/Program.cs`:

1. Handle `--version` and `-v`.
2. Rebuild configuration providers.
3. Derive `SmsService:SmsApiUrl` from `FeeSyncer:BaseUrl` and its endpoint.
4. Register file logging, Dapper mapping, services, and validation.
5. Open the database with a 10-second startup timeout.
6. Run as `FeeSyncer.Sms` under Windows Service Control Manager when applicable.

Runtime registrations:

```text
INotificationRepository -> NotificationRepository
ISmsSender               -> SmsApiService
SqlDependencyListener
NotificationProcessor
TableChangeListener      -> hosted service
RetryPoller              -> hosted service
```

`TableChangeListener` performs startup catch-up and listens to
`dbo.sms_notifications`. `RetryPoller` waits for its first timer tick and then
checks retry eligibility. Both call a processor guarded by a zero-wait
`SemaphoreSlim`; overlapping in-process triggers are skipped.

The repository selects an unordered `TOP 100` eligible rows. Rows are processed
sequentially. There is no database claim state, so running multiple SMS service
instances can duplicate sends.

### HTTP and retries

`SmsApiService.SendAsync` performs one HTTP request. Success is any 2xx response.
HTTP 408 and 5xx responses and transport exceptions are retryable. Other
non-success responses, including 429, are cancelled immediately.

Retry delay is:

```text
RetryBackoffSeconds * 2^(retryCount - 1), with +/-20% jitter
```

`retry_count` represents scheduled retries, not every attempted request. API
error bodies are stored in `description_json`.

### SMS payload

```json
{
  "id": 4,
  "phone_number": "07130000000",
  "mpesa_code": "KA470213XK",
  "admission_no": "5551",
  "student_name": "Student Name",
  "amount": 2979.75,
  "receipt_no": "RCPT-1",
  "dated": "2026-07-02T18:54:26"
}
```

## Agent Service

`src/Agent/Program.cs` registers Windows Service lifetime as `FeeSyncer.Agent`.
When the Agent section binds with `Enabled=true`, registrations include:

```text
GatewayClient
SchoolApiClient
IStudentAdapter -> SchoolApiStudentAdapter
SchoolIntegrationWorker
FeeProcessorUpdateWorker
MqttAgentConnection (when MqttEnabled is true)
```

The work loop heartbeats, waits for MQTT connection, requests an HTTP lease,
executes a supported operation, and renews the lease in parallel. MQTT signals
provide immediate wake-up; a connected idle timeout also checks HTTP. A broker
outage pauses discovery.

Supported operations:

- `students.snapshot.v1`
- `fees.snapshot.v1`
- `payments.record.v1`

Snapshot uploads are paged, SHA-256 hashed, and resumable against confirmed page
hashes. Payment completion uses a payment-specific completion route.

The separate Fee Processor updater is locally scheduled. It is not a gateway
work operation and is not advertised as an Agent capability.

See [School Integration Agent](school-integration.md) for endpoints and options.

## Configuration

Production files:

```text
C:\ProgramData\Munywele\FeeSyncer\appsettings.Production.json
C:\ProgramData\Munywele\FeeSyncer\agentsettings.json
```

Logs:

```text
C:\ProgramData\Munywele\FeeSyncer\logs\
```

Both service entry points clear the host defaults and add packaged JSON,
environment-specific JSON, environment variables, command-line values, and a
final Debug/Release-specific file. Release builds add ProgramData last, so its
values override environment and command-line values. Debug builds add
development JSON instead. This distinction is compile-time.

When `FeeSyncer:BaseUrl` is present, it overrides direct SMS/Agent URL values
through post-configuration. Endpoint paths also come from `FeeSyncer` settings.

Machine JSON currently stores secrets in plaintext. There is no DPAPI or
Credential Manager integration.

## Tray Application

`FeeSyncer.Tray` has no `Program.cs`; WPF generates its entry point from
`App.xaml`. Closing the Control Panel hides it. The tray **Exit** command shuts
down the process.

Key UI:

| File | Purpose |
|---|---|
| `TrayIcon.cs` | Icon, context menu, SMS background monitor, update notifications |
| `ControlPanel.xaml` | SMS and Agent service cards, Settings and Logs tabs |
| `ConfigEditor.xaml` | SMS, Agent, enrollment, MQTT, and Fee Processor settings |
| `StatusWindow.xaml` | Detailed SMS monitor status |
| `LogViewer.xaml` | Shared ProgramData log display and filtering |
| `SendNotificationDialog.xaml` | Direct SMS table insertion |
| `AboutWindow.xaml` | Version, components, and project links |

The Control Panel can install both services with delayed-auto startup. This is
different from the Inno installer, which creates manual services.

The tray enrollment client validates `enroll_...`, posts to the central endpoint,
requires an `fsk_...` response token, saves it, and restarts the Agent.

The update checker polls GitHub Releases at startup and every four hours. It
notifies only; it does not download or install releases.

## Shared Library

`FeeSyncer.Shared` is referenced by SMS, Agent, Tray, and Console. It contains:

- Constants, paths, configuration readers, and URL composition
- Version and executable-path helpers
- Service monitoring and service-control commands
- Database/API/Service Broker validation
- GitHub release checks
- Fee Processor interval parsing, Git updates, deployment, backup, and logging
- Shared status models

## Installer

Both installers copy four publish trees. Installed paths are:

```text
{app}\FeeSyncer.Sms.exe
{app}\Agent\FeeSyncer.Agent.exe
{app}\Tray\FeeSyncer.Tray.exe
{app}\Console\FeeSyncer.Console.exe
```

The installer creates both services as `LocalSystem`, manual, and stopped. It
does not write configuration. Tray selection controls Start Menu and all-users
Startup shortcuts. Console selection currently has no behavioral effect beyond
the binaries that are always copied.

Known installer limitations:

- Wizard initialization resets the previously detected upgrade flag.
- Framework runtime detection checks `Microsoft.NETCore.App 10` but not the WPF Desktop Runtime.
- Agent service registry cleanup is less defensive than SMS cleanup.
- The ProgramData/log directory permissions are not hardened by the installer.

## Build and Release

```powershell
./publish.ps1             # self-contained, four outputs
./publish-framework.ps1   # framework-dependent, four outputs
dotnet test -c Release
```

Release output:

- Four self-contained ZIPs: SMS, Agent, Tray, Console
- Self-contained installer
- Framework-dependent installer
- Public S3 manifest and versioned artifacts under `https://s3.munywele.co.ke/fee-syncer/`

`tests.yml` runs on non-documentation pushes and manual dispatch, not ordinary
pull-request events. `agent-tests.yml` runs on pull requests. The release flow
uses conventional commits to generate a no-prefix tag. Public clients read the
S3 `latest.json` manifest rather than the private GitHub Releases API.

## Packages

Important direct product dependencies:

| Package | Version |
|---|---:|
| Dapper | 2.1.79 |
| Microsoft.Data.SqlClient | 7.0.2 |
| Microsoft.Extensions.Hosting | 10.0.10 |
| Microsoft.Extensions.Hosting.WindowsServices | 10.0.10 |
| H.NotifyIcon.Wpf | 2.4.1 |
| MQTTnet | 5.2.0.1603 |
| System.ServiceProcess.ServiceController | 10.0.10 |
| CliWrap | 3.10.0 |
| LibGit2Sharp | 0.27.2 |

## Tests

There are 37 xUnit facts in nine source files:

| Project | Facts | Main coverage |
|---|---:|---|
| SMS | 18 | Sender results/backoff, processor flows, tray compatibility source checks |
| Agent | 15 | Contracts/hashing, school API mapping, MQTT gate/topic, wake signal |
| Tray | 4 | Tray icon construction and disposal |

There are no live database, broker, installer, or end-to-end integration tests.
Operational UI and Agent orchestration coverage remains limited.

## Known Implementation Risks

- Secrets are plain JSON and development settings must never contain production credentials.
- SMS processing has no cross-process database lease.
- Agent work discovery cannot operate as HTTP-only when MQTT is disabled.
- Heartbeats can be delayed by jobs and MQTT outages.
- The student JSON schema fixture must be reconciled with the current serializer fields.
- The tray service monitor can repeatedly notify while SMS remains stopped.
- The manual notification Amount input is currently not inserted.
- Main CI and auto-review expect different pull-request checks.
