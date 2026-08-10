# School Integration Worker

The school integration worker runs inside the existing `SmsNotificationService`
Windows service. It is not a second process or Windows service.

It is enabled by default. The service must be enrolled and configured with a
school-scoped token before the worker can complete central work.

## Central Connection

All agents connect to `https://fees.munywele.co.ke/`. The school is determined by
the school-scoped bearer API key, not by a school hostname.

The worker sends heartbeats, uses bounded long polling, uploads resumable student
and fee pages, and performs approved payment write-back. Its failures are isolated
inside the worker loop and do not terminate SMS notification processing.

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
    "HeartbeatSeconds": 60
  }
}
```

Student and fee records use only the approved minimal fields. The worker does not
delete records, emit deletion markers, or infer deletion from missing pages.
