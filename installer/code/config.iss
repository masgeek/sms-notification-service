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

  ConnStr := 'Server=' + JsonEscape(Server) + ';Database=' + JsonEscape(Database) + ';User Id=' + JsonEscape(Username) + ';Password=' + JsonEscape(Password) + ';TrustServerCertificate=True;';

  Log('CfgDir resolved to: ' + CfgDir);
  Log('CfgPath resolved to: ' + CfgPath);

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
    '    "ConnectionString": "' + JsonEscape(ConnStr) + '",' + #13#10 +
    '    "SmsApiUrl": "' + JsonEscape(ApiUrl) + '",' + #13#10 +
    '    "AuthorizationToken": "' + JsonEscape(Token) + '",' + #13#10 +
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
    '"' + CfgDir + '" /inheritance:r /grant:r "Administrators:(OI)(CI)F" "SYSTEM:(OI)(CI)F" "Users:(OI)(CI)(R,W)"',
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode
  );
  if ExitCode <> 0 then
    Log('Warning: icacls on directory returned exit code ' + IntToStr(ExitCode));

  Exec(
    'icacls.exe',
    '"' + CfgPath + '" /inheritance:r /grant:r "Administrators:(OI)(CI)F" "SYSTEM:(OI)(CI)F" "Users:(OI)(CI)(R,W)"',
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode
  );
  if ExitCode <> 0 then
    Log('Warning: icacls on file returned exit code ' + IntToStr(ExitCode));
end;
