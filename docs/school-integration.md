# School Integration Agent

The school integration agent runs as the separate `FeeSyncer.Agent`
Windows service. It does not run inside the SMS notification service.

It is enabled by default. The service must be enrolled and configured with a
school-scoped token before the worker can complete central work.

## Central Connection

All agents connect to `https://fees.munywele.co.ke/`. The school is determined by
the school-scoped bearer API key, not by a school hostname.

The agent sends heartbeats, uses bounded long polling, uploads resumable student
and fee pages, and performs approved payment write-back. Its process can be
restarted independently of SMS notification processing.

When `MqttEnabled` is true, the agent also subscribes to its per-agent MQTT
topic. `work_available` messages wake the agent so it can use the existing HTTP
lease endpoint immediately. MQTT is optional and HTTP polling remains the
fallback for broker or network outages.

## Local Connection

The worker calls only the fixed loopback API configured by `Agent:LocalApiBaseUrl`,
which defaults to `http://127.0.0.1:8001/api/`. Credentials remain local to the
school service configuration. Login tokens are refreshed before their expiry.

## Enrollment

An operator generates a single-use enrollment code in the central fee-syncer
admin interface. Exchange it once at:

```text
POST https://fees.munywele.co.ke/api/agent/enroll
```

Store the returned bearer token in protected service configuration as
`Agent:AgentToken`. Never put the token in source control, installer arguments,
logs, or student fixtures.

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
    "LongPollSeconds": 25,
    "HeartbeatSeconds": 60,
    "MqttEnabled": false,
    "MqttBrokerHost": "127.0.0.1",
    "MqttBrokerPort": 1883,
    "MqttUseTls": false,
    "MqttUsername": "",
    "MqttPassword": "",
    "MqttTopicPrefix": "fee-syncer/agent",
    "MqttKeepAliveSeconds": 30,
    "MqttReconnectMinSeconds": 1,
    "MqttReconnectMaxSeconds": 60
  }
}
```

Student and fee records use only the approved minimal fields. The worker does not
delete records, emit deletion markers, or infer deletion from missing pages.
