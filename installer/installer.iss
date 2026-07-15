; SmsNotificationService - Inno Setup Script
; Build the project first: dotnet publish -c Release -r win-x64 --self-contained
; Then compile this script with Inno Setup 6+

#define MyAppName "SmsNotificationService"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Munywele"
#define ServiceName "SmsNotificationService"
#define EventLogSource "SmsNotificationService"

[Setup]
AppId={{B8E3F2A1-7C4D-4E6F-8A2B-1D3C5E7F9A0B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=installer\output
OutputBaseFilename=SmsNotificationService-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallMode=x64
ArchitecturesAllowed=x64
PrivilegesRequired=admin
SetupIconFile=..\favicon.ico
UninstallDisplayIcon={app}\SmsNotificationService.exe
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{app}"; Permissions: everyone-readexec

[Run]
; Register event log source
Filename: "powershell.exe"; Parameters: "-Command ""New-EventLog -LogName Application -Source '{#EventLogSource}' -ErrorAction SilentlyContinue"""; StatusMsg: "Registering event log source..."; Flags: runhidden waituntilterminated

; Create Windows Service
Filename: "sc.exe"; Parameters: "create {#ServiceName} binPath="""{app}\SmsNotificationService.exe"" start=auto DisplayName=""{#MyAppName}"""; StatusMsg: "Creating Windows service..."; Flags: runhidden waituntilterminated

; Set environment variables from user input
Filename: "powershell.exe"; Parameters: "-Command ""[Environment]::SetEnvironmentVariable('SmsService__ConnectionString', '{code:GetConnectionString}', 'Machine')"""; StatusMsg: "Setting database connection string..."; Flags: runhidden waituntilterminated
Filename: "powershell.exe"; Parameters: "-Command ""[Environment]::SetEnvironmentVariable('SmsService__SmsApiUrl', '{code:GetSmsApiUrl}', 'Machine')"""; StatusMsg: "Setting SMS API URL..."; Flags: runhidden waituntilterminated
Filename: "powershell.exe"; Parameters: "-Command ""[Environment]::SetEnvironmentVariable('SmsService__AuthorizationToken', '{code:GetAuthToken}', 'Machine')"""; StatusMsg: "Setting authorization token..."; Flags: runhidden waituntilterminated

; Start the service
Filename: "sc.exe"; Parameters: "start {#ServiceName}"; StatusMsg: "Starting service..."; Flags: runhidden waituntilterminated postinstall skipifsilent

[UninstallRun]
; Stop and remove service
Filename: "sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden waituntilterminated
Filename: "sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden waituntilterminated

; Remove environment variables
Filename: "powershell.exe"; Parameters: "-Command ""[Environment]::SetEnvironmentVariable('SmsService__ConnectionString', `$null, 'Machine')"""; Flags: runhidden waituntilterminated
Filename: "powershell.exe"; Parameters: "-Command ""[Environment]::SetEnvironmentVariable('SmsService__SmsApiUrl', `$null, 'Machine')"""; Flags: runhidden waituntilterminated
Filename: "powershell.exe"; Parameters: "-Command ""[Environment]::SetEnvironmentVariable('SmsService__AuthorizationToken', `$null, 'Machine')"""; Flags: runhidden waituntilterminated

; Remove event log source
Filename: "powershell.exe"; Parameters: "-Command ""Remove-EventLog -Source '{#EventLogSource}' -ErrorAction SilentlyContinue"""; Flags: runhidden waituntilterminated

[Code]
var
  ConnStrPage: TInputQueryWizardPage;
  ApiUrlPage: TInputQueryWizardPage;
  AuthPage: TInputQueryWizardPage;

procedure InitializeWizard;
begin
  ConnStrPage := CreateInputQueryPage(wpSelectDir,
    'Database Connection',
    'Enter the SQL Server connection string.',
    'Connection String:');
  ConnStrPage.Add('Server=127.0.0.1;Database=school;User Id=sa;Password=YOUR_PASSWORD;TrustServerCertificate=True;', False);

  ApiUrlPage := CreateInputQueryPage(ConnStrPage.ID,
    'SMS API',
    'Enter the SMS API endpoint URL.',
    'API URL:');
  ApiUrlPage.Add('https://api.munywele.co.ke/v1/send', False);

  AuthPage := CreateInputQueryPage(ApiUrlPage.ID,
    'Authorization',
    'Enter the bearer token for the SMS API.',
    'Token:');
  AuthPage.Add('', False);
end;

function GetConnectionString(Param: String): String;
begin
  Result := ConnStrPage.Values[0];
end;

function GetSmsApiUrl(Param: String): String;
begin
  Result := ApiUrlPage.Values[0];
end;

function GetAuthToken(Param: String): String;
begin
  Result := AuthPage.Values[0];
end;
