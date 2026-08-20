# FeeSyncer Roadmap

## Implemented Foundation

- Separate SMS, Agent, Tray, Console, and Shared projects
- SQL Server Service Broker listener and retry poller
- Serialized SMS processing with retryable/non-retryable outcomes
- Exponential retry scheduling with jitter
- Startup configuration and database validation
- Standalone MQTT/HTTP school integration Agent
- Resumable student and fee snapshots
- Approved payment write-back
- Tray-based configuration, enrollment, diagnostics, and service management
- Scheduled and manual Fee Processor deployment support
- Self-contained and framework-dependent installers

## Reliability Priorities

### 1. Cross-Process SMS Claims

Add a database-backed claim/lease or `IN_PROGRESS` transition so two service
instances cannot send the same notification.

### 2. Rate Limiting

Add a configured request-rate policy before introducing parallel SMS sending.
Current batches are sequential, so bulk load affects latency rather than causing
concurrent API floods.

### 3. Circuit Breaker

Pause SMS calls after repeated transient provider failures and probe recovery
after a cooldown.

### 4. MQTT-Independent Agent Operation

Either reject `MqttEnabled=false` explicitly or implement a supported bounded
HTTP polling mode. Keep MQTT outages from delaying heartbeats if practical.

### 5. Credential Protection

Protect SQL, SMS, Agent, local API, MQTT, and updater secrets at rest and apply
explicit NTFS permissions during installation.

## Product Improvements

- Validate and normalize phone numbers before sending
- Add throughput, failure, retry, heartbeat, and upload metrics with an exporter
- Add Authenticode signing or a cryptographically signed update manifest
- Add notification history and richer diagnostics
- Validate all tray fields before saving
- Complete manual notification Amount handling
- Add configurable message templates only if message construction moves locally
- Add provider fallback only after retry/circuit-breaker behavior is defined

## Engineering Priorities

- Make the console installer option meaningful or remove it
- Verify both .NET and Windows Desktop runtimes in framework installation
- Align CI pull-request triggers with auto-review requirements
- Reconcile student contract JSON schema and serializer output
- Add SQL Server integration tests
- Add Agent worker/gateway orchestration tests
- Add installer and tray workflow tests
