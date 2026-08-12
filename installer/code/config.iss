procedure WriteConfigurationFile(const Server, Database, Username, Password, ApiUrl, Token, EnrolledAgentToken, LocalApiUrl, LocalApiUsername, LocalApiPassword: String);
var
  CfgPath: String;
  AgentCfgPath: String;
  ConnStr: String;
  JsonContent: AnsiString;
  AgentJsonContent: AnsiString;
begin
  CfgPath := ExpandConstant('{app}\{#ConfigFile}');
  AgentCfgPath := ExpandConstant('{app}\{#AgentDir}\{#ConfigFile}');

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

  AgentJsonContent :=
    '{' + #13#10 +
    '  "Agent": {' + #13#10 +
    '    "Enabled": true,' + #13#10 +
    '    "ServerUrl": "https://fees.munywele.co.ke/",' + #13#10 +
     '    "AgentToken": "' + JsonEscape(EnrolledAgentToken) + '",' + #13#10 +
     '    "LocalApiBaseUrl": "' + JsonEscape(LocalApiUrl) + '",' + #13#10 +
     '    "LocalApiUsername": "' + JsonEscape(LocalApiUsername) + '",' + #13#10 +
     '    "LocalApiPassword": "' + JsonEscape(LocalApiPassword) + '",' + #13#10 +
     '    "RequestTimeoutSeconds": 30,' + #13#10 +
     '    "IdleDelaySeconds": 5,' + #13#10 +
     '    "HeartbeatSeconds": 60,' + #13#10 +
     '    "LeaseRenewalSeconds": 30,' + #13#10 +
     '    "MqttEnabled": true,' + #13#10 +
     '    "MqttBrokerHost": "mqtt.munywele.co.ke",' + #13#10 +
     '    "MqttBrokerPort": 8883,' + #13#10 +
     '    "MqttUseTls": true,' + #13#10 +
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

  if not SaveStringToFile(AgentCfgPath, AgentJsonContent, False) then
  begin
    Log('SaveStringToFile FAILED for agent configuration.');
    RaiseException('Failed to write agent configuration file to: ' + AgentCfgPath);
  end;

  Log('Agent configuration written to: ' + AgentCfgPath);
end;
