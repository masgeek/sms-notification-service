# FeeSyncer Installer

Inno Setup installer for FeeSyncer. Two variants available:

- **Self-contained** (`installer.iss`) — bundles .NET runtime, no dependencies needed
- **Framework-dependent** (`installer-framework.iss`) — requires .NET 10 runtime on target machine

The installer installs separate Windows services for SMS notification processing
and school integration. Both services are installed stopped with manual startup;
the SMS tray monitor opens the maximized Control Panel and application settings
after installation. The agent is enabled by default. The SMS service reads
the root `appsettings.Production.json`; the agent reads
`Agent\appsettings.Production.json`. Enrollment and agent configuration are
managed after installation from the FeeSyncer tray app; the installer does not
handle enrollment codes or bearer tokens.

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
| `globals.iss` | Variables, InitSetup | `services.iss` |
| `wizard.iss` | UI pages and install choices | `globals.iss` |
| `install.iss` | Install logic | All above |
| `uninstall.iss` | Uninstall logic | `services.iss`, `eventlog.iss` |

## Rules

1. **No comment headers** in `#include` files — causes "BEGIN expected" error
2. **Order matters** — include dependencies before dependents
3. **Functions are global** — all `#include` files share the same `[Code]` scope
4. **Both installers share the same code modules** — changes to `code/` affect both installers

After installation, open the FeeSyncer tray app's **Settings** screen. Enter
the central enrollment code and local fee-processor credentials, then click
**Enroll / Re-enroll**. The tray app exchanges the single-use `enroll_...` code,
writes the returned `fsk_...` token to the agent configuration, and can restart
the agent service. The enrollment code expires after 15 minutes and is not
used for runtime requests. See
[`docs/school-integration.md`](../docs/school-integration.md).

Configure MQTT-first work discovery with `Agent:MqttEnabled`, broker
host, WebSocket path, TLS, and broker credentials. Production uses secure
WebSockets (`wss://`) with EMQX, normally on port 443 and path `/mqtt`. Keep
`Agent:MqttPassword` and the agent token out of installer arguments and source
control. Work discovery pauses if MQTT is unavailable.
