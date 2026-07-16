; ============================================================================
; SmsNotificationService - Production Inno Setup Installer
; ============================================================================
; Requires: Inno Setup 6.4+
; Build:    dotnet publish SmsNotificationService.csproj -c Release -r win-x64 --self-contained -o publish
; Compile:  Open in Inno Setup Compiler -> Build -> Compile
; Output:   installer\output\SmsNotificationService-Setup-<version>.exe
; ============================================================================

#define MyAppName        "SmsNotificationService"
#ifndef MyAppVersion
  #define MyAppVersion     "1.0.0"
#endif
#define MyAppPublisher   "Munywele Consulting LTD"
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
AppCopyright=Copyright (C) 2026 Munywele Consulting LTD
AppVerName={#MyAppName} {#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoCopyright={#AppCopyright}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=output
OutputBaseFilename=SmsNotificationService-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64os
PrivilegesRequired=admin
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
; [Run] - Not used; all post-install logic is in Pascal Script (CurStepChanged)
; ============================================================================

; ============================================================================
; [UninstallRun] - Handled entirely in Pascal (CurUninstallStepChanged)
; ============================================================================

; ============================================================================
; [Code] - Pascal Script
; ============================================================================
[Code]

// ============================================================================
// Global state
// ============================================================================
var
  DbPage           : TInputQueryWizardPage;
  ApiUrlPage       : TInputQueryWizardPage;
  AuthPage         : TInputQueryWizardPage;
  UpgradeMode      : Boolean;
  ConfigExists     : Boolean;
  KeepExistingCfg  : Boolean;
  ConfigPromptPage : TInputOptionWizardPage;

// ============================================================================
// Utility: Run a command and return the exit code.
// ============================================================================
function RunCmd(const Exe, Params: String): Integer;
var
  Res: Boolean;
  ExitCode: Integer;
begin
  Res := Exec(Exe, Params, '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  if Res then
    Result := ExitCode
  else
    Result := -1;
end;

// ============================================================================
// Service helpers
// ============================================================================

// Import the Win32 GetTickCount API from kernel32.dll.
// Inno Setup's Pascal Script has no built-in tick-count function,
// so it must be declared as an external function before use.
// Returns: number of milliseconds elapsed since system startup,
// as a DWORD (32-bit unsigned integer).
// Note: wraps around to 0 after ~49.7 days of uptime — fine for
// short-lived polling loops (e.g. during install/uninstall),
// but avoid for long-duration timing. Use GetTickCount64 instead
// if wraparound safety is ever needed.
function GetTickCount: DWORD;
  external 'GetTickCount@kernel32.dll stdcall';
  
// Returns True if a Windows service with the given name exists.
function ServiceExists(const SvcName: String): Boolean;
begin
  Result := (RunCmd('sc.exe', 'query ' + SvcName) = 0);
end;

// Stops a Windows service. Returns True on success.
function StopService(const SvcName: String): Boolean;
begin
  Result := (RunCmd('sc.exe', 'stop ' + SvcName) = 0);
end;

// Polls service state until it matches TargetState or timeout expires.
// TargetState: 'STOPPED' or 'RUNNING'
function WaitForServiceState(const SvcName: String; const TargetState: String; TimeoutMs: Integer): Boolean;
  var
    StartTick: Cardinal;
    OutputFile: String;
    Cmd: String;
    Content: AnsiString;   // LoadStringFromFile requires an AnsiString var param, not String
    ExitCode: Integer;
  begin
    Result := False;       // default outcome if we time out without a match
    StartTick := GetTickCount;
    OutputFile := ExpandConstant('{tmp}\svcstate.txt');

    while (GetTickCount - StartTick) < Cardinal(TimeoutMs) do
    begin
      // Quote SvcName in case the service name ever contains spaces
      Cmd := 'sc query "' + SvcName + '" | findstr /C:"STATE"';

      // Redirect both stdout and stderr into OutputFile.
      // Closing quote must wrap the path only, not "2>&1".
      Exec('cmd.exe', '/C ' + Cmd + ' > "' + OutputFile + '" 2>&1',
        '', SW_HIDE, ewWaitUntilTerminated, ExitCode);

      if FileExists(OutputFile) then
      begin
        // LoadStringFromFile fills Content via var param and returns
        // a Boolean success flag — it does not work as a direct assignment.
        if LoadStringFromFile(OutputFile, Content) then
        begin
          if Pos(UpperCase(TargetState), UpperCase(Content)) > 0 then
          begin
            Result := True;
            Exit; // early return as soon as the target state is found
          end;
        end;
      end;

      Sleep(500); // throttle polling interval to avoid busy-looping
    end;

  // Loop exhausted TimeoutMs without finding TargetState
  Log('WaitForServiceState: timed out waiting for ' + TargetState);
  Result := False;
end;

// Starts a Windows service. Returns True on success.
function StartService(const SvcName: String): Boolean;
begin
  Result := (RunCmd('sc.exe', 'start ' + SvcName) = 0);
end;

// Deletes a Windows service. Returns True on success.
function DeleteService(const SvcName: String): Boolean;
begin
  Result := (RunCmd('sc.exe', 'delete ' + SvcName) = 0);
end;

function JsonEscape(const S: String): String;
begin
  Result := S;
  StringChangeEx(Result, '\', '\\', True);  // escape backslashes first
  StringChangeEx(Result, '"', '\"', True);  // then escape quotes
end;

[Code]
function BoolToStr(B: Boolean): String;
begin
  if B then
    Result := 'True'
  else
    Result := 'False';
end;

// Executes a command. Raises an exception on failure.
procedure ExecuteOrFail(const Exe, Params, FailureMsg: String);
var
  ExitCode: Integer;
  Res: Boolean;
begin
  Res := Exec(Exe, Params, '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  if (not Res) or (ExitCode <> 0) then
    RaiseException(FailureMsg + ' (exit code: ' + IntToStr(ExitCode) + ')');
end;

// ============================================================================
// Event Log helper
// ============================================================================

// Registers the Event Log source. Idempotent - ignores if already exists.
procedure RegisterEventLog;
var
  ExitCode: Integer;
begin
  Exec(
    'powershell.exe',
    '-NoProfile -ExecutionPolicy Bypass -Command "try { [System.Diagnostics.EventLog]::CreateEventSource(''' +
      '{#EventLogSource}' + ''', ''Application'') } catch {}"',
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode
  );
  Log('Event log source registered: {#EventLogSource}');
end;

// Removes the Event Log source. Idempotent.
procedure RemoveEventLog;
var
  ExitCode: Integer;
begin
  Exec(
    'powershell.exe',
    '-NoProfile -ExecutionPolicy Bypass -Command "try { [System.Diagnostics.EventLog]::DeleteEventSource(''' +
      '{#EventLogSource}' + ''') } catch {}"',
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode
  );
  Log('Event log source removed: {#EventLogSource}');
end;

// ============================================================================
// Service configuration helpers
// ============================================================================

procedure ConfigureServiceDescription(const SvcName, Description: String);
var
  ExitCode: Integer;
begin
  Exec('sc.exe', 'description ' + SvcName + ' "' + Description + '"', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Log('Service description configured.');
end;

procedure ConfigureDelayedAutoStart(const SvcName: String);
var
  ExitCode: Integer;
begin
  Exec('sc.exe', 'config ' + SvcName + ' start= delayed-auto', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Log('Delayed auto-start configured.');
end;

procedure ConfigureRecovery(const SvcName: String);
var
  ExitCode: Integer;
begin
  // reset=86400 (1 day), actions=restart/5min, restart/5s, restart/5s
  Exec('sc.exe', 'failure ' + SvcName + ' reset= 86400 actions= restart/300000/restart/5000/restart/5000', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Log('Failure recovery configured (5min, 5s, 5s).');
end;

// ============================================================================
// Configuration file helper
// ============================================================================

// Writes appsettings.Production.json to ProgramData.
// Builds connection string from individual fields.
procedure WriteConfigurationFile(const Server, Database, Username, Password, ApiUrl, Token: String);
var
  CfgDir: String;
  CfgPath: String;
  ConnStr: String;
  JsonContent: AnsiString;
  ExitCode: Integer;
begin
  CfgDir := ExpandConstant('{commonappdata}\{#ConfigDir}');
  CfgPath := ExpandConstant('{commonappdata}\{#ConfigDir}\{#ConfigFile}');

  // Build connection string from individual fields
  ConnStr := 'Server=' + Server + ';Database=' + Database + ';User Id=' + Username + ';Password=' + Password + ';TrustServerCertificate=True;';

  Log('CfgDir resolved to: ' + CfgDir);
  Log('CfgPath resolved to: ' + CfgPath);

  // Check BEFORE acting — is something already there, and what is it?
  if DirExists(CfgDir) then
    Log('CfgDir already exists as a directory.')
  else if FileExists(CfgDir) then
    Log('WARNING: CfgDir path exists but is a FILE, not a directory — this will block ForceDirectories.')
  else
    Log('CfgDir does not exist yet.');

  if not ForceDirectories(CfgDir) then
  begin
    Log('ForceDirectories FAILED. DirExists now: ' + BoolToStr(DirExists(CfgDir)));
    RaiseException('Failed to create configuration directory: ' + CfgDir);
  end;

  JsonContent :=
    '{' + #13#10 +
    '  "SmsService": {' + #13#10 +
    '    "ConnectionString": "' + ConnStr + '",' + #13#10 +
    '    "SmsApiUrl": "' + ApiUrl + '",' + #13#10 +
    '    "AuthorizationToken": "' + Token + '",' + #13#10 +
    '    "RetryBackoffSeconds": 30,' + #13#10 +
    '    "LogRetentionDays": ' + '{#LogRetentionDays}' + ',' + #13#10 +
    '    "MaxLogFileSizeMb": ' + '{#MaxLogFileSizeMb}' + #13#10 +
    '  }' + #13#10 +
    '}';

  Log('Attempting to write ' + IntToStr(Length(JsonContent)) + ' bytes to ' + CfgPath);

  if not SaveStringToFile(CfgPath, JsonContent, False) then
  begin
    Log('SaveStringToFile FAILED. FileExists now: ' + BoolToStr(FileExists(CfgPath)));
    RaiseException('Failed to write configuration file to: ' + CfgPath);
  end;

  Log('Configuration written to: ' + CfgPath);

  Exec(
    'icacls.exe',
    '"' + CfgPath + '" /inheritance:r /grant:r "Administrators:(OI)(CI)F" "SYSTEM:(OI)(CI)F" "Everyone:(OI)(CI)R"',
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode
  );
  if ExitCode <> 0 then
    Log('Warning: icacls returned exit code ' + IntToStr(ExitCode));
end;

// ============================================================================
// Fresh install: full setup sequence
// ============================================================================
procedure DoFreshInstall;
var
  ExitCode: Integer;
begin
  Log('=== Fresh install started ===');

  // 1. Register event log source
  RegisterEventLog;

  // 2. Write configuration file (skip if user chose to keep existing)
  if not KeepExistingCfg then
    WriteConfigurationFile(DbPage.Values[0], DbPage.Values[1], DbPage.Values[2], DbPage.Values[3], ApiUrlPage.Values[0], AuthPage.Values[0])
  else
    Log('Skipping configuration write — keeping existing file.');

  // 3. Create service
  Log('Creating Windows service...');
  ExecuteOrFail(
    'sc.exe',
    'create {#ServiceName} binPath= "' + ExpandConstant('{app}') + '\SmsNotificationService.exe" start= delayed-auto DisplayName= "{#ServiceDisplay}" obj= LocalSystem',
    'Failed to create Windows service.'
  );
  Log('Service created.');

  // 4. Configure service
  ConfigureServiceDescription('{#ServiceName}', '{#ServiceDesc}');
  ConfigureRecovery('{#ServiceName}');

  // 5. Start service
  Log('Starting service...');
  StopService('{#ServiceName}'); // ensure clean state
  StartService('{#ServiceName}');
  if WaitForServiceState('{#ServiceName}', 'RUNNING', 15000) then
    Log('Service started successfully.')
  else
    MsgBox('The service was created but may not have started.' + #13#10 +
           'Check Windows Event Log for details.', mbInformation, MB_OK);

  Log('=== Fresh install completed ===');
end;

// ============================================================================
// Upgrade: stop, replace files (done by Inno), restart
// ============================================================================
procedure DoUpgrade;
begin
  Log('=== Upgrade started ===');

  // Stop service
  Log('Stopping service for upgrade...');
  StopService('{#ServiceName}');
  if WaitForServiceState('{#ServiceName}', 'STOPPED', 30000) then
    Log('Service stopped for upgrade.')
  else
    RaiseException('Failed to stop the {#ServiceName} service. Please stop it manually and try again.');

  // Update config if user chose to enter new settings
  if not KeepExistingCfg then
  begin
    Log('Writing updated configuration...');
    WriteConfigurationFile(DbPage.Values[0], DbPage.Values[1], DbPage.Values[2], DbPage.Values[3], ApiUrlPage.Values[0], AuthPage.Values[0]);
  end
  else
    Log('Keeping existing configuration.');

  Log('=== Upgrade pre-install completed (files will be replaced, service restarted in post-install) ===');
end;

// ============================================================================
// Post-install: restart service after upgrade
// ============================================================================
procedure DoPostUpgrade;
begin
  Log('Restarting service after upgrade...');
  StartService('{#ServiceName}');
  if WaitForServiceState('{#ServiceName}', 'RUNNING', 15000) then
    Log('Service restarted successfully after upgrade.')
  else
    MsgBox('The service was updated but may not have restarted.' + #13#10 +
           'Check Windows Event Log for details.', mbInformation, MB_OK);

  Log('=== Upgrade completed ===');
end;

// ============================================================================
// Uninstall sequence
// ============================================================================
procedure DoUninstall;
var
  PreserveCfg: Integer;
begin
  Log('=== Uninstall started ===');

  // Stop service
  Log('Stopping service...');
  StopService('{#ServiceName}');
  WaitForServiceState('{#ServiceName}', 'STOPPED', 30000);

  // Delete service
  Log('Deleting service...');
  if ServiceExists('{#ServiceName}') then
  begin
    DeleteService('{#ServiceName}');
    Sleep(1000);
  end;

  // Retry once
  if ServiceExists('{#ServiceName}') then
  begin
    Log('Service still exists. Retrying...');
    DeleteService('{#ServiceName}');
    Sleep(2000);
  end;

  if ServiceExists('{#ServiceName}') then
    Log('Service could not be deleted via sc. Registry cleanup will handle it.')
  else
    Log('Service deleted successfully.');

  // Remove event log source
  RemoveEventLog;

  // Ask whether to preserve configuration
  PreserveCfg := MsgBox(
    'Do you want to preserve the configuration file?' + #13#10 + #13#10 +
    'Location: ' + ExpandConstant('{commonappdata}\{#ConfigDir}') + #13#10 + #13#10 +
    'Click Yes to keep configuration for future reinstall.' + #13#10 +
    'Click No to remove all configuration files.',
    mbConfirmation,
    MB_YESNO or MB_DEFBUTTON2
  );

  if PreserveCfg = IDNO then
  begin
    Log('Removing configuration directory...');
    DelTree(ExpandConstant('{commonappdata}\{#ConfigDir}'), True, True, True);
  end
  else
    Log('Preserving configuration directory.');

  // Final registry cleanup — always runs regardless of sc delete outcome
  RegDeleteKeyIncludingSubkeys(HKLM, 'SYSTEM\CurrentControlSet\Services\{#ServiceName}');
  Log('Registry cleanup completed.');

  Log('=== Uninstall completed ===');
end;

// ============================================================================
// Wizard page initialization
// ============================================================================

procedure InitializeWizard;
var
  PrevPageID: Integer;
begin
  UpgradeMode := False;
  KeepExistingCfg := False;
  PrevPageID := wpSelectDir;

  // --- Config exists? Ask user what to do ---
  if ConfigExists then
  begin
    ConfigPromptPage := CreateInputOptionPage(PrevPageID,
      'Configuration Found',
      'An existing appsettings.Production.json was detected.',
      'What would you like to do?',
      True, False);
    ConfigPromptPage.Add('Keep existing configuration');
    ConfigPromptPage.Add('Enter new configuration');
    ConfigPromptPage.Values[0] := True;
    PrevPageID := ConfigPromptPage.ID;
  end;

  // --- Database fields (single page with 4 inputs) ---
  DbPage := CreateInputQueryPage(PrevPageID,
    'Database Server',
    'Enter the SQL Server connection details.',
    '');
  DbPage.Add('Server (e.g. 127.0.0.1):', False);
  DbPage.Add('Database (e.g. school):', False);
  DbPage.Add('Username (e.g. sa):', False);
  DbPage.Add('Password:', True);
  DbPage.Values[0] := '127.0.0.1';
  DbPage.Values[2] := 'sa';

  ApiUrlPage := CreateInputQueryPage(DbPage.ID,
    'SMS API Configuration',
    'Enter the SMS API endpoint URL.',
    'API URL:');
  ApiUrlPage.Add('https://api.munywele.co.ke/v1/send', False);

  AuthPage := CreateInputQueryPage(ApiUrlPage.ID,
    'Authorization',
    'Enter the bearer token for the SMS API.',
    'Token:');
  AuthPage.Add('', True);
end;

// ============================================================================
// Hide config pages on upgrade (existing config is preserved)
// ============================================================================

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;

  // No existing config: nothing to skip (config pages show as normal)
  if not ConfigExists then
    Exit;

  // Existing config (fresh or upgrade): if user chose to keep, skip config pages
  if KeepExistingCfg then
  begin
    Result := (PageID = DbPage.ID) or (PageID = ApiUrlPage.ID) or (PageID = AuthPage.ID);
    Exit;
  end;
end;

// ============================================================================
// Validation on Next click
// ============================================================================

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  // Capture config prompt choice
  if ConfigExists and (CurPageID = ConfigPromptPage.ID) then
  begin
    KeepExistingCfg := ConfigPromptPage.Values[0];
    if KeepExistingCfg then
      Log('User chose to keep existing configuration.');
  end;

  if CurPageID = DbPage.ID then
  begin
    if Trim(DbPage.Values[0]) = '' then
    begin
      MsgBox('The server name cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if Trim(DbPage.Values[1]) = '' then
    begin
      MsgBox('The database name cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if Trim(DbPage.Values[2]) = '' then
    begin
      MsgBox('The username cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if Trim(DbPage.Values[3]) = '' then
    begin
      MsgBox('The password cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  if CurPageID = ApiUrlPage.ID then
  begin
    if Trim(ApiUrlPage.Values[0]) = '' then
    begin
      MsgBox('The SMS API URL cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if (Pos('http://', ApiUrlPage.Values[0]) = 0) and (Pos('https://', ApiUrlPage.Values[0]) = 0) then
    begin
      MsgBox('The API URL must start with http:// or https://', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  if CurPageID = AuthPage.ID then
  begin
    if Trim(AuthPage.Values[0]) = '' then
    begin
      MsgBox('The authorization token cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;
end;

// ============================================================================
// Installer initialization — detect upgrade before wizard starts
// ============================================================================

function InitializeSetup: Boolean;
begin
  Result := True;
  UpgradeMode := ServiceExists('{#ServiceName}');
  ConfigExists := FileExists(ExpandConstant('{commonappdata}\{#ConfigDir}\{#ConfigFile}'));
  if UpgradeMode then
    Log('Existing installation detected — upgrade mode enabled.');
  if ConfigExists then
    Log('Existing configuration file detected.');
end;

// ============================================================================
// Install step handler — orchestrate fresh install vs upgrade
// ============================================================================

procedure CurStepChanged(CurStep: TSetupStep);
begin
  case CurStep of
    ssInstall:
      begin
        if UpgradeMode then
          DoUpgrade;
      end;

    ssPostInstall:
      begin
        if UpgradeMode then
          DoPostUpgrade
        else
          DoFreshInstall;
      end;
  end;
end;

// ============================================================================
// Uninstall step handler
// ============================================================================

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    DoUninstall;
end;
