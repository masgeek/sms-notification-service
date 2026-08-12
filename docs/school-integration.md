# School Integration Agent

The school integration agent runs as the separate `FeeSyncer.Agent`
Windows service. It does not run inside the SMS notification service.

It is enabled by default. The service must be enrolled and configured with a
school-scoped token before the worker can complete central work.

## Central Connection

All agents connect to `https://fees.munywele.co.ke/`. The school is determined by
the school-scoped bearer API key, not by a school hostname.

The agent sends heartbeats, uses bounded HTTP lease checks, uploads resumable
student and fee pages, and performs approved payment write-back. Its process can
be restarted independently of SMS notification processing.

The agent is MQTT-first and subscribes to its per-agent MQTT topic.
`work_available` messages are wake-up hints only; the agent immediately calls
the existing HTTP lease endpoint, which remains authoritative. While MQTT is
healthy the agent does not continuously poll. During broker or network outages,
work discovery pauses until MQTT reconnects; there is no HTTP polling fallback.

## Local Connection

The worker calls only the fixed loopback API configured by `Agent:LocalApiBaseUrl`,
which defaults to `http://127.0.0.1:8001/api/`. Credentials remain local to the
school service configuration. Login tokens are refreshed before their expiry.

## Enrollment

Enrollment is a two-step bootstrap flow. The enrollment code is not the agent's
runtime credential.

1. A fee-syncer operator opens the target school in the central admin interface
   and chooses **Generate agent enrollment code**.
2. The admin displays a single-use code beginning with `enroll_`. It expires
   after 15 minutes and is shown only at generation time.
3. Exchange the code once at:

```text
POST https://fees.munywele.co.ke/api/agent/enroll
```

Include `enrollment_code` and an `agent_name` in the JSON body. The response
contains a permanent school-scoped bearer token beginning with `fsk_`.
Open the FeeSyncer tray app's **Settings** screen, enter the code and local API
credentials, and click **Enroll / Re-enroll**. The tray app stores the returned
token in protected agent configuration and restarts the agent service. Never
put either credential in source control, installer arguments, logs, or student
fixtures.

After this exchange, the agent never uses the `enroll_` code again. All runtime
requests use the returned `fsk_` token. If the code expires or is consumed,
generate a new code in the central admin interface.

## Configuration

The agent reads this configuration from its own application directory as
`Agent\appsettings.Development.json` during development or
`Agent\appsettings.Production.json` after installation.
The development file is loaded only when `DOTNET_ENVIRONMENT=Development`.

```json
{
  "Agent": {
    "Enabled": true,
    "ServerUrl": "https://fees.munywele.co.ke/",
    "AgentToken": "replace-with-a-provisioned-agent-token",
    "LocalApiBaseUrl": "http://127.0.0.1:8001/api/",
    "LocalApiUsername": "",
    "LocalApiPassword": "",
    "HeartbeatSeconds": 60,
    "MqttEnabled": true,
    "MqttBrokerHost": "mqtt.munywele.co.ke",
    "MqttBrokerPort": 8883,
    "MqttUseTls": true,
    "MqttUsername": "",
    "MqttPassword": "",
    "MqttTopicPrefix": "fee-syncer/agent",
    "MqttKeepAliveSeconds": 30,
    "MqttReconnectMinSeconds": 1,
    "MqttReconnectMaxSeconds": 60
  }
}
```

Production MQTT connections use TLS on port 8883. Development may use plaintext
MQTT on port 1883 when `DOTNET_ENVIRONMENT=Development`; do not expose that
listener publicly. `MqttUsername` and `MqttPassword`
are optional broker credentials; the bearer API token is never sent in MQTT
payloads and is only used to derive the topic key and default MQTT username.
The MQTT payload contains notification metadata only, never student, fee,
payment, lease-token, or job payload data.

Student and fee records use only the approved minimal fields. The worker does not
delete records, emit deletion markers, or infer deletion from missing pages.
