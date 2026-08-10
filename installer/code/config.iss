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
    '  },' + #13#10 +
    '  "Agent": {' + #13#10 +
    '    "Enabled": true,' + #13#10 +
    '    "ServerUrl": "https://fees.munywele.co.ke/",' + #13#10 +
    '    "AgentToken": "replace-with-a-provisioned-agent-token",' + #13#10 +
    '    "LocalApiBaseUrl": "http://127.0.0.1:8001/api/",' + #13#10 +
    '    "LocalApiUsername": "",' + #13#10 +
    '    "LocalApiPassword": "",' + #13#10 +
    '    "LongPollSeconds": 25,' + #13#10 +
    '    "HeartbeatSeconds": 60,' + #13#10 +
    '    "MqttEnabled": false,' + #13#10 +
    '    "MqttBrokerHost": "127.0.0.1",' + #13#10 +
    '    "MqttBrokerPort": 1883,' + #13#10 +
    '    "MqttUseTls": false,' + #13#10 +
    '    "MqttUsername": "",' + #13#10 +
    '    "MqttPassword": "",' + #13#10 +
    '    "MqttTopicPrefix": "fee-syncer/agent",' + #13#10 +
    '    "MqttKeepAliveSeconds": 30,' + #13#10 +
    '    "MqttReconnectMinSeconds": 1,' + #13#10 +
    '    "MqttReconnectMaxSeconds": 60' + #13#10 +
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
