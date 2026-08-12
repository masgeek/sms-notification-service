# FeeSyncer Installer

Inno Setup installer for FeeSyncer. Two variants available:

- **Self-contained** (`installer.iss`) — bundles .NET runtime, no dependencies needed
- **Framework-dependent** (`installer-framework.iss`) — requires .NET 10 runtime on target machine

The installer installs separate Windows services for SMS notification processing
and school integration. The agent is enabled by default. The SMS service reads
the root `appsettings.Production.json`; the agent reads
`Agent\appsettings.Production.json`. During setup, the wizard accepts a
short-lived `enroll_...` code, exchanges it over HTTPS for the permanent
`fsk_...` token, and writes the token directly to protected agent configuration.
The enrollment code and returned token are never passed as process arguments or
written to installer logs.

## Structure

```
installer/
├── installer.iss              # Self-contained installer
├── installer-framework.iss    # Framework-dependent installer
├── code/
│   ├── globals.iss            # Global variables and InitializeSetup
│   ├── utils.iss              # RunCmd, BoolToStr, JsonEscape
│   ├── services.iss           # Windows Service management
│   ├── eventlog.iss           # Event Log helpers
│   ├── config.iss             # Config writer
│   ├── wizard.iss             # Wizard pages and validation
│   ├── install.iss            # Fresh install, upgrade logic
│   └── uninstall.iss          # Uninstall logic
├── favicon.ico                # Installer icon
└── output/                    # Built installers
```

## Build

```bash
# 1. Publish the .NET app

# Self-contained (bundles .NET runtime)
./publish.ps1

# Framework-dependent (requires .NET 10 runtime on target)
./publish-framework.ps1

# 2. Compile installer (requires Inno Setup 6.4+)

# Self-contained installer
iscc installer.iss /DMyAppVersion=1.2.3

# Framework-dependent installer
iscc installer-framework.iss /DMyAppVersion=1.2.3 /DFrameworkInstall
```

## Adding New Code

### 1. Create or edit a module in `code/`

Each file must contain only Pascal Script — no comment headers (`;` comments) at the top.

```pascal
// code/utils.iss

function MyNewFunction(const Input: String): String;
begin
  Result := Input;
end;
```

### 2. Include in `installer.iss` and `installer-framework.iss`

Add `#include` in the `[Code]` section (order matters for dependencies):

```pascal
[Code]
#include "code\utils.iss"        # Functions used by other modules
#include "code\services.iss"     # Depends on utils
#include "code\eventlog.iss"
#include "code\config.iss"
#include "code\globals.iss"      # Variables used by wizard
#include "code\wizard.iss"       # Depends on globals, config
#include "code\install.iss"      # Depends on all above
#include "code\uninstall.iss"
```

### 3. File placement guidelines

| File | Purpose | Dependencies |
|------|---------|--------------|
| `utils.iss` | Utility functions | None |
| `services.iss` | Service management | `utils.iss` (RunCmd) |
| `eventlog.iss` | Event Log | None |
| `config.iss` | Config writer | `utils.iss` (JsonEscape, BoolToStr) |
| `globals.iss` | Variables, InitSetup | `services.iss` |
| `wizard.iss` | UI pages, validation | `globals.iss`, `config.iss` |
| `install.iss` | Install logic | All above |
| `uninstall.iss` | Uninstall logic | `services.iss`, `eventlog.iss` |

## Rules

1. **No comment headers** in `#include` files — causes "BEGIN expected" error
2. **Order matters** — include dependencies before dependents
3. **Functions are global** — all `#include` files share the same `[Code]` scope
4. **Both installers share the same code modules** — changes to `code/` affect both installers

## Agent Configuration

The installer writes these safe defaults into the generated configuration:

```json
"Agent": {
  "Enabled": true,
  "ServerUrl": "https://fees.munywele.co.ke/",
  "AgentToken": "replace-with-a-provisioned-agent-token",
  "LocalApiBaseUrl": "http://127.0.0.1:8001/api/",
  "LocalApiUsername": "",
  "LocalApiPassword": "",
  "RequestTimeoutSeconds": 30,
  "IdleDelaySeconds": 5,
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
```

Before running the installer, open the target school in the central fee-syncer
admin interface and generate a single-use `enroll_...` code. The installer asks
for that code, the agent name, and the local fee-processor credentials, then
calls `POST https://fees.munywele.co.ke/api/agent/enroll`, stores the returned
`fsk_...` token as `Agent:AgentToken`, and starts the services. The enrollment
code expires after 15 minutes and is not used for runtime requests. See
[`docs/school-integration.md`](../docs/school-integration.md).

Configure MQTT-first work discovery with `Agent:MqttEnabled`, broker
host and port, TLS, and broker credentials. Keep `Agent:MqttPassword` and the
agent token out of installer arguments and source control. HTTP polling remains
work discovery channel. Work discovery pauses if MQTT is unavailable.
