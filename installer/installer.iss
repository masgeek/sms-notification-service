; ============================================================================
; SmsNotificationService - Production Inno Setup Installer
; ============================================================================
; Requires: Inno Setup 6.4+
; Build:    dotnet publish SmsNotificationService.csproj -c Release -r win-x64 --self-contained -o publish
; Compile:  Open in Inno Setup Compiler -> Build -> Compile
; Output:   installer\output\SmsNotificationService-Setup-<version>.exe
; ============================================================================

#define MyAppName        "SmsNotificationService"
#define MyAppVersion     "1.0.0"
#define MyAppPublisher   "Munywele"
#define ServiceName      "SmsNotificationService"
#define ServiceDisplay   "SmsNotificationService"
#define ServiceDesc      "Listens to SQL Server for SMS notifications and sends them via HTTP API"
#define EventLogSource   "SmsNotificationService"
#define ConfigDir        "Munywele\SmsNotificationService"
#define ConfigFile       "appsettings.Production.json"

; ============================================================================
; [Setup] - Installer metadata, UI, compression, logging
; ============================================================================
[Setup]
AppId={{B8E3F2A1-7C4D-4E6F-8A2B-1D3C5E7F9A0B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppVerName={#MyAppName} {#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=installer\output
OutputBaseFilename=SmsNotificationService-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64
PrivilegesRequired=admin
SetupIconFile=..\favicon.ico
UninstallDisplayIcon={app}\SmsNotificationService.exe
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
; [UninstallRun] - Cleanup on uninstall
; ============================================================================
[UninstallRun]
Filename: "sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden waituntilterminated
Filename: "{cmd}"; Parameters: "/c timeout /t 3 /nobreak >nul"; Flags: runhidden waituntilterminated
Filename: "sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden waituntilterminated
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""try {{ [System.Diagnostics.EventLog]::DeleteEventSource('{#EventLogSource}') }} catch {{}}"""; Flags: runhidden waituntilterminated

; ============================================================================
; [Code] - Pascal Script
; ============================================================================
[Code]

// ============================================================================
// Global state
// ============================================================================
var
  ConnStrPage      : TInputQueryWizardPage;
  ApiUrlPage       : TInputQueryWizardPage;
  AuthPage         : TInputQueryWizardPage;
  UpgradeMode      : Boolean;  // True if existing installation detected

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
  // reset=86400 (1 day), actions=restart/5s x3
  Exec('sc.exe', 'failure ' + SvcName + ' reset= 86400 actions= restart/5000/restart/5000/restart/5000', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Log('Failure recovery configured.');
end;

// ============================================================================
// Configuration file helper
// ============================================================================

// Writes appsettings.Production.json to ProgramData.
// Uses manual JSON construction to avoid external dependencies.
procedure WriteConfigurationFile(const ConnStr, ApiUrl, Token: String);
var
  CfgPath: String;
  JsonContent: String;
  ExitCode: Integer;
begin
  CfgPath := ExpandConstant('{commonappdata}\{#ConfigDir}\{#ConfigFile}');

  // Build JSON content
  JsonContent :=
    '{' + #13#10 +
    '  "SmsService": {' + #13#10 +
    '    "ConnectionString": "' + ConnStr + '",' + #13#10 +
    '    "SmsApiUrl": "' + ApiUrl + '",' + #13#10 +
    '    "AuthorizationToken": "' + Token + '"' + #13#10 +
    '  }' + #13#10 +
    '}';

  // Ensure directory exists
  ForceDirectories(ExpandConstant('{commonappdata}\{#ConfigDir}'));

  // Write the file
  if not SaveStringToFile(CfgPath, JsonContent, False) then
    RaiseException('Failed to write configuration file to: ' + CfgPath);

  Log('Configuration written to: ' + CfgPath);

  // Set NTFS permissions: admin full, system full, everyone read-only
  Exec(
    'icacls.exe',
    '"' + CfgPath + '" /inheritance:r /grant:r "Administrators:(OI)(CI)F" "SYSTEM:(OI)(CI)F" "Everyone:(OI)(CI)R"',
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode
  );
  Log('NTFS permissions set on configuration file.');
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

  // 2. Write configuration file (only on fresh install)
  WriteConfigurationFile(ConnStrPage.Values[0], ApiUrlPage.Values[0], AuthPage.Values[0]);

  // 3. Create service
  Log('Creating Windows service...');
  ExecuteOrFail(
    'sc.exe',
    'create {#ServiceName} binPath= "' + ExpandConstant('{app}') + '\SmsNotificationService.exe" start= auto DisplayName= "{#ServiceDisplay}" obj= LocalSystem',
    'Failed to create Windows service.'
  );
  Log('Service created.');

  // 4. Configure service
  ConfigureServiceDescription('{#ServiceName}', '{#ServiceDesc}');
  ConfigureDelayedAutoStart('{#ServiceName}');
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
  DeleteService('{#ServiceName}');

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

  Log('=== Uninstall completed ===');
end;

// ============================================================================
// Wizard page initialization
// ============================================================================

procedure InitializeWizard;
begin
  UpgradeMode := False;

  // --- Database Connection Page ---
  ConnStrPage := CreateInputQueryPage(wpSelectDir,
    'Database Connection',
    'Enter the SQL Server connection string.',
    'Connection String:');
  ConnStrPage.Add('Server=127.0.0.1;Database=school;User Id=sa;Password=YOUR_PASSWORD;TrustServerCertificate=True;', False);

  // --- SMS API URL Page ---
  ApiUrlPage := CreateInputQueryPage(ConnStrPage.ID,
    'SMS API Configuration',
    'Enter the SMS API endpoint URL.',
    'API URL:');
  ApiUrlPage.Add('https://api.munywele.co.ke/v1/send', False);

  // --- Authorization Token Page (masked) ---
  AuthPage := CreateInputQueryPage(ApiUrlPage.ID,
    'Authorization',
    'Enter the bearer token for the SMS API.',
    'Token:');
  AuthPage.Add('', True);  // True = password/masked input
end;

// ============================================================================
// Hide config pages on upgrade (existing config is preserved)
// ============================================================================

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  // Skip all three config pages if this is an upgrade
  if UpgradeMode then
  begin
    Result := (PageID = ConnStrPage.ID) or (PageID = ApiUrlPage.ID) or (PageID = AuthPage.ID);
  end
  else
    Result := False;
end;

// ============================================================================
// Validation on Next click
// ============================================================================

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if CurPageID = ConnStrPage.ID then
  begin
    if Trim(ConnStrPage.Values[0]) = '' then
    begin
      MsgBox('The SQL Server connection string cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if Pos('Server=', ConnStrPage.Values[0]) = 0 then
    begin
      MsgBox('The connection string must contain a Server parameter.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if Pos('Database=', ConnStrPage.Values[0]) = 0 then
    begin
      MsgBox('The connection string must contain a Database parameter.', mbError, MB_OK);
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
  if UpgradeMode then
    Log('Existing installation detected — upgrade mode enabled.');
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
