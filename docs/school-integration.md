# School Integration Agent

`FeeSyncer.Agent` is a standalone .NET 10 worker and Windows service. It handles
student and fee snapshots, approved payment write-back, heartbeats, MQTT wake
hints, and optional scheduled Fee Processor deployments. It does not run inside
`FeeSyncer.Sms`.

## Enrollment

Enrollment bootstraps a permanent school-scoped token:

1. Generate a single-use `enroll_...` code for the target school in the central admin interface.
2. Open the FeeSyncer tray application and select **Settings > School Agent**.
3. Enter the code and an agent name, then select **Enroll / Re-enroll**.
4. The tray posts `enrollment_code` and `agent_name` to `/api/agent/enroll`.
5. The tray saves the returned `fsk_...` token and restarts `FeeSyncer.Agent`.

The code expires after 15 minutes and is never used after exchange. The Agent
executable has no enrollment client; the tray performs the exchange. The
installer does not accept enrollment credentials.

## Security

The Agent token, local API credentials, MQTT credentials, and updater credentials
are currently stored as plain JSON in the machine configuration. Restrict access
to `C:\ProgramData\Munywele\FeeSyncer`, do not place secrets in source control or
logs, and rotate any exposed credential. MQTT payloads contain notification
metadata only, not student, fee, payment, lease-token, or job data.

The tray requires an enrollment response token beginning with `fsk_`. Agent
startup currently validates only that the configured token has at least 32
characters, so operators must still ensure the correct token type is used.

## Configuration

Production overrides are read from:

```text
C:\ProgramData\Munywele\FeeSyncer\agentsettings.json
```

The Agent loads packaged defaults, environment-specific defaults, the shared
machine settings, and then `agentsettings.json`. Environment variables and
command-line arguments remain explicit final overrides. Both Windows service and
interactive console runs therefore use the same ProgramData settings regardless
of working directory or Debug/Release build. The tray stores the gateway URL and
endpoint paths beside the enrolled token so they cannot target different servers.

Example:

```json
{
  "FeeSyncer": {
    "BaseUrl": "https://fees.munywele.co.ke/"
  },
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

`FeeSyncer:BaseUrl` is the effective central URL when present. Central URLs must
use HTTPS unless they are loopback. The local API must use HTTP or HTTPS on a
loopback address. Timing values are range-validated at startup.

## MQTT

Production uses MQTT over secure WebSockets, normally:

```text
wss://mqtt.munywele.co.ke/mqtt
```

The subscription topic is derived from the configured prefix and a SHA-256 hash
of the Agent token:

```text
fee-syncer/agent/key/<token-hash>/work
```

If `MqttUsername` is empty, the Agent token is used as the MQTT username. The
configured password is sent as the MQTT password.

Accepted notifications must be version 1 `work_available` messages with a valid
event ID, job ID, operation, and timestamp. Duplicate, stale, future-dated,
wrong-topic, malformed, and oversized notifications are ignored.

On connection or notification, MQTT wakes the HTTP lease loop immediately. HTTP
polling continues on the server-advertised cadence while MQTT is disconnected or
disabled. MQTT never provides executable work and never gates work discovery.

The Agent uses MQTT 5 with a stable client ID, clean start disabled, and a
bounded persistent session. If `MqttClientId` is blank, a nonsecret identifier is
derived from the enrolled token hash. The connection publishes:

```text
fee-syncer/agent/key/<token-hash>/events
fee-syncer/agent/key/<token-hash>/presence
```

Presence is retained at QoS 1 with an offline last will. Events include hello,
HTTP-heartbeat acknowledgement, health, and coarse work progress. They never
include records, job IDs, lease values, page hashes, local paths, or credentials.

### WSS Troubleshooting

The WebSocket handshake must return HTTP `101 Switching Protocols`. HTTP `200`
usually means `/mqtt` reached a normal HTTP handler or the EMQX dashboard. Route
`/mqtt` to the EMQX WebSocket listener, commonly port 8083 behind the TLS proxy,
before dashboard fallback routes.

## Gateway Operations

All runtime gateway calls use `Authorization: Bearer <AgentToken>`.

| Purpose | Method | Default route |
|---|---|---|
| Lease work | GET | `api/agent/work?wait=10` |
| Heartbeat | POST | `api/agent/heartbeat` |
| Renew lease | POST | `api/agent/sync-jobs/{jobId}/renew` |
| Upload page | PUT | `api/agent/sync-jobs/{jobId}/pages/{page}` |
| Complete snapshot | POST | `api/agent/sync-jobs/{jobId}/complete` |
| Complete payment | POST | `api/agent/payment-jobs/{jobId}/complete` |
| Report failure | POST | `api/agent/sync-jobs/{jobId}/fail` |

Supported operations are:

- `students.snapshot.v1`
- `fees.snapshot.v1`
- `payments.record.v1`

Snapshot pages are serialized once as unescaped UTF-8 JSON and hashed with
SHA-256 over those exact bytes. Upload, renewal, completion, payment completion,
and failure calls send both the lease token and generation. Snapshot completion
normally returns `202 uploaded`; this means server materialization was accepted,
not that tenant activation has completed. Heartbeats advertise all three
capabilities and provide the polling cadence and maximum long-poll duration.

## Local API

The default base URL is `http://127.0.0.1:8001/api/`. The Agent uses:

| Purpose | Route |
|---|---|
| Login | `POST v1/users/login` |
| Students | `GET v1/students?page=N&per_page=P` |
| Fee balances | `GET v1/fees?page=N&per_page=P` |
| Record payment | `POST v1/payments` |

Local access tokens are cached in memory and refreshed before expiry. HTTP 422
with the known duplicate M-Pesa-code response is reported as `duplicate`; other
validation failures are rejected.

The current student serializer emits the approved adapter record fields defined
in `Contracts.cs`. The checked-in JSON schema under `tests/contracts` should be
kept synchronized whenever that contract changes.

## Fee Processor Updates

When enabled, `FeeProcessorUpdateWorker` runs independently of gateway jobs. It
can back up `.env` and the previous Git commit, stop the queue service and IIS
site, update a configured Git branch or tag, run `pnpm install`, `pnpm build`,
Composer, and Laravel migration/cache commands, and restart local services.
Missing pnpm or a failed pnpm install/build is logged but does not stop the
remaining deployment steps. Configure and test this feature from the tray before
enabling its schedule.

Detailed deployment output is written separately to
`C:\ProgramData\Munywele\FeeSyncer\logs\fee-processor-updates.log`. Scheduled
updates, including configuration failures and exceptions, do not write to the
Agent console or Agent HTTP log category.

## Diagnostics

The Agent defines process-local .NET metrics for MQTT connections,
notifications, work checks, and lease latency. No exporter is configured by
default. Operational logs are written under:

```text
C:\ProgramData\Munywele\FeeSyncer\logs\
```

When the `FeeSyncer.Agent.SchoolIntegration.AgentHttpLoggingHandler` category is
set to `Debug`, both gateway and local-school HTTP clients log every request and
response with method, URL, status, duration, correlation ID, and header/body
metadata. Authorization, lease, cookie, password, token, and payload values are
never logged. Windows Event Log deployments must also allow `Debug` for the
`Logging:EventLog:LogLevel` provider.

Student page uploads also log up to three redacted sample records at `Debug`.
Class, enrollment, term, and other non-identifying structural fields remain
visible, while admission/source IDs, names, phone numbers, parent names, and
balances are replaced with `[redacted]`.

The global level can be set under **Settings > Logging**.
Saving writes the selected `Logging:LogLevel:Default` and
`Logging:EventLog:LogLevel:Default` values to both SMS and Agent machine
configuration files. Restart the services when prompted to apply the new level.
