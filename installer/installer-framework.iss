; ============================================================================
; SmsNotificationService - Framework-Dependent Installer
; ============================================================================
; Requires: Inno Setup 6.4+ and .NET 10 Runtime on target machine
; Build:    ./publish-framework.ps1
; Compile:  Open in Inno Setup Compiler -> Build -> Compile
; Output:   installer\output\SmsNotificationService-Framework-Setup-<version>.exe
; ============================================================================
;
; Modular structure (shared with installer.iss):
;   code/globals.iss    - Global variables and initialization
;   code/utils.iss      - Utility functions (RunCmd, BoolToStr, JsonEscape)
;   code/services.iss   - Windows Service management
;   code/eventlog.iss   - Windows Event Log helpers
;   code/config.iss     - Configuration and database helpers
;   code/wizard.iss     - Wizard page initialization and validation
;   code/install.iss    - Install, upgrade, and post-install logic
;   code/uninstall.iss  - Uninstall logic
; ============================================================================

#define MyAppName        "SmsNotificationService"
#ifndef MyAppVersion
  #define MyAppVersion     "1.0.0"
#endif
#define MyAppPublisher   "Munywele Consulting LTD"
#define MyAppCopyright   "Copyright (C) 2026 Munywele Consulting LTD"
#define ServiceName      "SmsNotificationService"
#define ServiceDisplay   "SmsNotificationService"
#define ServiceDesc      "Listens to SQL Server for SMS notifications and sends them via HTTP API"
#define TrayAppName      "SmsNotificationService.Tray"
#define TrayAppDisplay   "SmsNotificationService Tray"
#define ConsoleAppName   "SmsNotificationService.Console"
#define ConsoleAppDisplay "SmsNotificationService Console"
#define EventLogSource   "SmsNotificationService"
#define ConfigDir        "Munywele\SmsNotificationService"
#define ConfigFile       "appsettings.Production.json"
#define LogRetentionDays "7"
#define MaxLogFileSizeMb "10"
#define TrayDir          "Tray"
#define ConsoleDir       "Console"
#define FrameworkInstall true

; ============================================================================
; [Setup] - Installer metadata, UI, compression, logging
; ============================================================================
[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName} (Framework-dependent)
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://munywele.co.ke
AppSupportURL=https://github.com/masgeek/sms-notification-service/issues
AppUpdatesURL=https://github.com/masgeek/sms-notification-service/releases
AppCopyright={#MyAppCopyright}
AppVerName={#MyAppName} {#MyAppVersion} (Framework)
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoCopyright={#MyAppCopyright}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=output
OutputBaseFilename=SmsNotificationService-Framework-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64os
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
SetupIconFile=..\favicon.ico
UninstallDisplayIcon={app}\SmsNotificationService.exe
UninstallDisplayName={#MyAppName} {#MyAppVersion} (Framework)
WizardStyle=modern
WizardSizePercent=110
DisableProgramGroupPage=yes
SetupLogging=yes
UninstallLogging=yes
CloseApplications=force
RestartApplications=no

; ============================================================================
; [Languages] - Localisation
; ============================================================================
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

; ============================================================================
; [Dirs] - Directories created during install
; ============================================================================
[Dirs]
Name: "{app}"; Permissions: everyone-readexec
Name: "{commonappdata}\{#ConfigDir}\logs"; Permissions: admins-full system-full everyone-readexec

; ============================================================================
; [Files] - Framework-dependent binaries
; ============================================================================
[Files]
Source: "..\build\service-framework\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "appsettings.Development.json"
Source: "..\build\tray-framework\*"; DestDir: "{app}\{#TrayDir}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "appsettings.Development.json"
Source: "..\build\console-framework\*"; DestDir: "{app}\{#ConsoleDir}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "appsettings.Development.json"

; ============================================================================
; [Icons] - Start Menu shortcut
; ============================================================================
[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#TrayDir}\{#TrayAppName}.exe"; Comment: "Open SMS Notification Service tray app"; Check: ShouldInstallTrayApp

; Startup folder (all-users — matches admin install mode)
Name: "{commonstartup}\{#TrayAppDisplay}"; \
    Filename: "{app}\{#TrayDir}\{#TrayAppName}.exe"; \
    WorkingDir: "{app}\{#TrayDir}"; \
    Comment: "Start SMS Notification Service Tray"; \
    Check: ShouldInstallTrayApp

Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

; ============================================================================
; [Code] - Pascal Script (modular includes)
; ============================================================================
[Code]
#include "code\utils.iss"
#include "code\services.iss"
#include "code\eventlog.iss"
#include "code\config.iss"
#include "code\globals.iss"
#include "code\wizard.iss"
#include "code\install.iss"
#include "code\uninstall.iss"
