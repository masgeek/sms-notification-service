# Tray Application Checklist

This checklist reflects the current implementation rather than the original
prototype plan.

## Implemented

- [x] .NET 10 WPF tray project with explicit-shutdown lifetime
- [x] `H.NotifyIcon.Wpf` 2.4.1 and generated state icons
- [x] SMS background monitoring and service commands
- [x] Combined SMS/Agent Control Panel
- [x] Service install/uninstall actions
- [x] Settings and Logs tabs
- [x] SMS database/API/Service Broker validation
- [x] Agent gateway, local API, and MQTT diagnostics
- [x] Agent enrollment and service restart
- [x] Fee Processor update settings and execution
- [x] Manual SMS insertion dialog
- [x] GitHub release notifications
- [x] About dialog available from the main window and tray icon
- [x] Installer Start Menu and all-users Startup integration
- [x] Four WPF tray construction/disposal tests

## Known Gaps

- [ ] Protect machine credentials with DPAPI, Credential Manager, or hardened ACLs
- [ ] Validate all editor fields before saving
- [ ] Ensure Agent central endpoint edits are saved to `agentsettings.json`
- [ ] Insert the Amount field from the manual notification dialog
- [ ] Distinguish intentional service stops from unexpected failures
- [ ] Report `sc.exe` completion and errors in the UI
- [ ] Test MQTT-disabled behavior or add a supported HTTP-only mode
- [ ] Add Control Panel, enrollment, settings, About, and log-viewer tests
- [ ] Verify the framework installer checks the Windows Desktop Runtime

## Current Decisions

| Area | Implementation |
|---|---|
| Framework | `net10.0-windows` WPF |
| Tray library | `H.NotifyIcon.Wpf` 2.4.1 |
| MQTT library | `MQTTnet` 5.2.0.1603 |
| Production config | ProgramData machine JSON files |
| Service control | `ServiceController` for status, `sc.exe` for actions |
| Tray exit | Explicit application shutdown |
| Window close | Hide and remain in tray |
| Autostart | All-users Startup-folder shortcut |
| Status icons | Runtime-rendered circles; `favicon.ico` remains the application icon |
| Secret storage | Plain JSON; masking is UI-only |
