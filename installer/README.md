# Installer

Inno Setup installer for SmsNotificationService.

## Structure

```
installer/
├── installer.iss          # Main file - includes all modules
├── code/
│   ├── globals.iss        # Global variables and InitializeSetup
│   ├── utils.iss          # RunCmd, BoolToStr, JsonEscape
│   ├── services.iss       # Windows Service management
│   ├── eventlog.iss       # Event Log helpers
│   ├── config.iss         # DB connection test, config writer
│   ├── wizard.iss         # Wizard pages and validation
│   ├── install.iss        # Fresh install, upgrade logic
│   └── uninstall.iss      # Uninstall logic
├── favicon.ico            # Installer icon
└── output/                # Built installers
```

## Build

```bash
# 1. Publish the .NET app
dotnet publish SmsNotificationService.csproj -c Release -r win-x64 --self-contained -o ../publish

# 2. Compile installer (requires Inno Setup 6.4+)
iscc installer.iss /DMyAppVersion=1.2.3
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

### 2. Include in `installer.iss`

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
| `config.iss` | Config, DB test | `utils.iss` (JsonEscape, BoolToStr) |
| `globals.iss` | Variables, InitSetup | `services.iss` |
| `wizard.iss` | UI pages, validation | `globals.iss`, `config.iss` |
| `install.iss` | Install logic | All above |
| `uninstall.iss` | Uninstall logic | `services.iss`, `eventlog.iss` |

## Rules

1. **No comment headers** in `#include` files — causes "BEGIN expected" error
2. **Order matters** — include dependencies before dependents
3. **Functions are global** — all `#include` files share the same `[Code]` scope
4. **Test connectivity** — `TestDbConnection` in `config.iss` uses ADO
