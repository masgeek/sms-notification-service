# Audit Follow-Up Checklist

This replaces the historical checklist whose completed states no longer matched
the current implementation.

## Security

- [ ] Remove and rotate any real credentials from development configuration
- [ ] Protect ProgramData configuration files with explicit NTFS ACLs
- [ ] Encrypt or externally store persisted secrets
- [ ] Require HTTPS for production SMS API URLs
- [ ] Require an `fsk_...` runtime Agent token, not length alone
- [ ] Avoid logging raw phone numbers and full provider error bodies where sensitive

## SMS

- [x] Map `description_json` to `SmsNotification.Description`
- [x] Cancel non-retryable failures immediately
- [x] Validate required settings and positive numeric values
- [x] Use a named `HttpClient`
- [x] Use asynchronous listener retry delays
- [x] Bound a repository read to 100 rows
- [ ] Make batch size configurable and add deterministic ordering
- [ ] Add a database-backed claim to prevent duplicate multi-instance sends
- [ ] Pass cancellation tokens through repository and HTTP operations
- [ ] Treat HTTP 429 according to provider retry semantics
- [ ] Detach listener handlers and dispose all listener resources deterministically

## Agent

- [ ] Support or explicitly reject MQTT-disabled operation
- [ ] Decouple heartbeats from long jobs and broker waits
- [ ] Validate MQTT host/scheme consistently
- [ ] Validate reconnect minimum does not exceed maximum
- [ ] Observe lease-renewal failures before completing jobs
- [ ] Reconcile the student JSON schema with serialized records
- [ ] Add worker, gateway, heartbeat, and updater tests

## Tray

- [x] Provide combined SMS and Agent management
- [x] Perform tray-based enrollment
- [x] Add About access from the Control Panel and tray menu
- [ ] Validate all settings before save
- [ ] Persist central Agent endpoint edits to Agent configuration
- [ ] Insert the manual Amount field
- [ ] Prevent repeated stopped-service notifications
- [ ] Surface service command exit codes
- [ ] Add tests for enrollment, config round-trips, logs, and About

## Installer and CI

- [ ] Preserve detected upgrade mode during wizard initialization
- [ ] Make the console selection meaningful or remove it
- [ ] Check `Microsoft.WindowsDesktop.App 10` in the framework installer
- [ ] Harden ProgramData and log directory permissions
- [ ] Make Agent service cleanup as robust as SMS cleanup
- [ ] Run the main Tests workflow on pull requests or adjust auto-review
- [ ] Replace shell-specific cache sentinel commands with PowerShell-native code
