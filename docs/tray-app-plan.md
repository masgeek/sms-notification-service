# Tray Application Reference

This file replaces the original implementation proposal. The tray application
is implemented under `src/Tray` with reusable support in `src/Shared`.

## Current Features

- Generated green/red/yellow service-state tray icon
- Combined Control Panel for SMS and Agent services
- Start, stop, restart, install, uninstall, and console launch actions
- SMS status details and connection validation
- Shared log viewer with filtering and clipboard export
- Manual SMS row insertion
- SMS and Agent configuration editor
- Agent enrollment and MQTT diagnostics
- Fee Processor updater settings and immediate deployment execution
- Public S3 release notifications with GitHub Releases fallback
- About dialog from the Control Panel and tray context menu

## Navigation

Left-click and double-click open the Control Panel. The tray menu contains:

1. Open Control Panel
2. Status Details
3. View Logs
4. Launch Console Monitor
5. Send Notification
6. Validate Connections
7. Settings
8. SMS start, stop, and restart
9. Check for Updates
10. About FeeSyncer
11. Exit

Agent service controls are in the Control Panel. **Exit** terminates the tray;
closing or minimizing the Control Panel hides it.

## Main Files

```text
src/Tray/
|-- App.xaml
|-- TrayIcon.cs
|-- ControlPanel.xaml
|-- StatusWindow.xaml
|-- LogViewer.xaml
|-- SendNotificationDialog.xaml
|-- ConfigEditor.xaml
|-- AboutWindow.xaml
`-- FeeSyncer.Tray.csproj
```

`ServiceMonitor`, `UpdateChecker`, `ConnectionValidator`, `ConfigPathResolver`,
and related utilities live in `src/Shared`.

## Production Data

```text
C:\ProgramData\Munywele\FeeSyncer\appsettings.Production.json
C:\ProgramData\Munywele\FeeSyncer\agentsettings.json
C:\ProgramData\Munywele\FeeSyncer\logs\
```

The editor saves credentials as plain JSON. Password controls mask display only;
they do not encrypt stored values.

## Behavior Notes

- The background tray icon monitors SMS; the Control Panel queries both services.
- Displayed uptime is time since the monitor observed the current state.
- Scheduled checks use the interval configured under **SMS Service > Tray behavior**, read the public Munywele S3 update manifest, and fall back to the public GitHub release manifest if S3 is unavailable or invalid.
- Manual update checks from either the Control Panel or tray menu open a blocking progress dialog with installed/latest versions, S3 source, cancellation, detailed outcomes, and an installer download when an update exists.
- **Download and Install** validates HTTPS origin, exact size, and SHA-256 before launching the matching elevated installer and shutting down the tray.
- Successful self-updates restore prior service states and relaunch the tray; unsigned installers still produce an Unknown publisher UAC warning.
- Log **Clear** clears the display; refresh reloads files.
- Log **Export** copies visible text to the clipboard.
- Manual notification Amount is shown in the UI but not currently inserted.
- Tray-based service installation uses delayed-auto startup.
- Inno-based installation uses manual startup and leaves services stopped.
- Tray autostart is installed through the all-users Startup folder.
- Agent console launches preserve failed startup output and wait for a key press before closing.

## Build

```powershell
dotnet build src/Tray/FeeSyncer.Tray.csproj -c Release
dotnet test tests/Tray/FeeSyncer.Tray.Tests.csproj -c Release
dotnet publish src/Tray/FeeSyncer.Tray.csproj -c Release -r win-x64 --self-contained -o build/tray
```

## Follow-Up Work

- Add UI tests for Control Panel, enrollment, settings, About, and logs
- Add service-command completion/error feedback
- Prevent repeated stopped-service notifications
- Encrypt or otherwise protect stored credentials
- Insert and validate Amount in manual notifications
- Persist all Agent endpoint edits into Agent machine configuration
- Add semantic-version comparison and release links to update notifications
