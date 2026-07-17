; ============================================================================
; SmsNotificationService - Production Inno Setup Installer
; ============================================================================
; Requires: Inno Setup 6.4+
; Build:    dotnet publish SmsNotificationService.csproj -c Release -r win-x64 --self-contained -o publish
; Compile:  Open in Inno Setup Compiler -> Build -> Compile
; Output:   installer\output\SmsNotificationService-Setup-<version>.exe
; ============================================================================
;
; Modular structure:
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
#define EventLogSource   "SmsNotificationService"
#define ConfigDir        "Munywele\SmsNotificationService"
#define ConfigFile       "appsettings.Production.json"
#define LogRetentionDays "7"
#define MaxLogFileSizeMb "10"

; ============================================================================
; [Setup] - Installer metadata, UI, compression, logging
; ============================================================================
[Setup]
AppId={{B8E3F2A1-7C4D-4E6F-8A2B-1D3C5E7F9A0B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://munywele.co.ke
AppSupportURL=https://github.com/masgeek/sms-notification-service/issues
AppUpdatesURL=https://github.com/masgeek/sms-notification-service/releases
AppCopyright={#MyAppCopyright}
AppVerName={#MyAppName} {#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoCopyright={#MyAppCopyright}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=output
OutputBaseFilename=SmsNotificationService-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64os
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
SetupIconFile=..\favicon.ico
UninstallDisplayIcon={app}\SmsNotificationService.exe
UninstallDisplayName={#MyAppName} {#MyAppVersion}
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
Name: "{commonappdata}\{#ConfigDir}"; Permissions: admins-full system-full everyone-readexec
Name: "{commonappdata}\{#ConfigDir}\logs"; Permissions: admins-full system-full everyone-readexec
Name: "{commonappdata}\{#ConfigDir}\data"; Permissions: admins-full system-full everyone-readexec

; ============================================================================
; [Files] - Application binaries (always overwrite)
; ============================================================================
[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; ============================================================================
; [Icons] - Start Menu shortcut
; ============================================================================
[Icons]
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
