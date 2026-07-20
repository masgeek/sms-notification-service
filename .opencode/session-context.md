# Session Context — 2026-07-19

## Current State

- **Branch:** `develop` at `c47084d` (behind origin by 2)
- **All known issues resolved**

## What We Did Today

### 1. Console app integration into installer + CI/CD + publish scripts
- `publish.ps1` / `publish-framework.ps1` — added console app publish
- `installer.iss` / `installer-framework.iss` — added ConsoleAppName, ConsoleDir, [Files] entries
- `installer/code/globals.iss` — InstallConsoleApp, ShouldInstallConsoleApp
- `installer/code/wizard.iss` — console app checkbox wizard page
- `installer/code/install.iss` — MaybeStartConsoleApp procedure
- `installer/code/uninstall.iss` — kills console app via taskkill
- Commits: `0b5cf05`, `fe3b413`

### 2. Build directory restructuring (publish → build/)
- Output now goes to `build/service/`, `build/tray/`, `build/console/`
- Framework-dependent: `build/service-framework/`, etc.
- `.gitignore` — replaced `publish*` with `build/`
- Installer [Files] sources updated to `../build/...` paths
- CI dummy folders updated
- Commits: `fe3b413`

### 3. Zip splitting (fix duplicate filename clash)
- Each component zipped separately to avoid DLL clashes
- `SmsNotificationService-service-win-x64.zip`
- `SmsNotificationService-tray-win-x64.zip`
- `SmsNotificationService-console-win-x64.zip`
- Commits: `bcf8e53`, `b7014e5`

### 4. Server 2016 compatibility fix
- **Root cause:** `H.NotifyIcon.Wpf` `TaskbarIcon.ForceCreate()` defaults to enabling EcoQoS mode, which calls `SetProcessQualityOfServiceLevel` — unavailable on Server 2016 (build 14393)
- **Fix:** `_icon.ForceCreate(enablesEfficiencyMode: false);`
- Commit: `c514ea5` (on `fix/tray-compatibility`, now merged)

### 5. Guard test for ForceCreate
- Source-text check in `TrayIconForceCreateTests` ensuring efficiency mode stays disabled
- Commit: `c1ff9e1` (merged via PR #45)

## CI Status
- `fix/tray-compatibility` merged into `develop` via PR #45
- `develop` is behind origin by 2 commits — pull before working

## Key Files Modified Today
- `publish.ps1`, `publish-framework.ps1`
- `installer/installer.iss`, `installer/installer-framework.iss`
- `installer/code/globals.iss`, `wizard.iss`, `install.iss`, `uninstall.iss`
- `.github/workflows/tests.yml`, `.github/workflows/release.yml`
- `.gitignore`
- `SmsNotificationService.Tray/TrayIcon.cs` (ForceCreate fix)
- `tests/SmsNotificationService.Tests/TrayIconForceCreateTests.cs` (new)
- `docs/PROJECT_SUMMARY.md`, `docs/deployment.md`
- `docs/tray-app-checklist.md`, `docs/tray-app-plan.md`
