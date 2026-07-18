procedure WriteConfigurationFile(const Server, Database, Username, Password, ApiUrl, Token: String);
var
  CfgPath: String;
  ConnStr: String;
  JsonContent: AnsiString;
begin
  CfgPath := ExpandConstant('{app}\{#ConfigFile}');

  ConnStr := 'Server=' + JsonEscape(Server) + ';Database=' + JsonEscape(Database) + ';User Id=' + JsonEscape(Username) + ';Password=' + JsonEscape(Password) + ';TrustServerCertificate=True;';

  Log('CfgPath resolved to: ' + CfgPath);

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
end;
