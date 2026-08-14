procedure MaybeStartTrayApp;
var
  ResultCode: Integer;
  TrayPath: String;
  CommandLine: String;
begin
  if InstallTrayApp and StartTrayAfter then
  begin
    TrayPath := ExpandConstant('{app}\{#TrayDir}\{#TrayAppName}.exe');
    CommandLine := '/c timeout /t 2 /nobreak >nul & start "" "' + TrayPath + '" --setup';
    Log('Scheduling tray app to start after installer exit...');
    ShellExec('', ExpandConstant('{cmd}'), CommandLine, ExpandConstant('{app}'), SW_HIDE, ewNoWait, ResultCode);
    Log('Tray app startup scheduled.');
  end;
end;

procedure DoFreshInstall;
begin
  Log('=== Fresh install started ===');

  RegisterEventLog;
  Log('Configuration is managed by the application; installer will not write credentials.');

  Log('Creating or updating Windows services...');
  EnsureService('{#ServiceName}', '{#ServiceDisplay}', ExpandConstant('{app}') + '\FeeSyncer.Sms.exe');
  EnsureService('{#AgentServiceName}', '{#AgentServiceDisplay}', ExpandConstant('{app}') + '\{#AgentDir}\FeeSyncer.Agent.exe');
  ConfigureServiceDescription('{#AgentServiceName}', '{#AgentServiceDesc}');
  ConfigureRecovery('{#AgentServiceName}');

  ConfigureServiceDescription('{#ServiceName}', '{#ServiceDesc}');
  ConfigureRecovery('{#ServiceName}');

  Log('Services installed but left stopped until configuration and connection checks pass.');

  Log('=== Fresh install completed; only the tray monitor will be started ===');
  MaybeStartTrayApp;
end;

procedure DoUpgrade;
begin
  Log('=== Upgrade started ===');

  Log('Stopping service for upgrade...');
  StopService('{#ServiceName}');
  StopService('{#AgentServiceName}');
  WaitForServiceState('{#AgentServiceName}', 'STOPPED', 30000);
  if WaitForServiceState('{#ServiceName}', 'STOPPED', 30000) then
    Log('Service stopped for upgrade.')
  else
    RaiseException('Failed to stop the {#ServiceName} service. Please stop it manually and try again.');

  EnsureService('{#ServiceName}', '{#ServiceDisplay}', ExpandConstant('{app}') + '\FeeSyncer.Sms.exe');
  EnsureService('{#AgentServiceName}', '{#AgentServiceDisplay}', ExpandConstant('{app}') + '\{#AgentDir}\FeeSyncer.Agent.exe');

  Log('Configuration is managed by the application; existing configuration was not changed.');

  Log('=== Upgrade pre-install completed (files will be replaced, service restarted in post-install) ===');
end;

procedure DoPostUpgrade;
begin
  Log('Upgrade completed; services remain stopped until configuration is verified.');

  Log('=== Upgrade completed; only the tray monitor will be started ===');
  MaybeStartTrayApp;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  case CurStep of
    ssInstall:
      begin
        #ifdef FrameworkInstall
        if not CheckDotNetRuntime then
        begin
          MsgBox('.NET 10 Runtime is required but was not detected.' + #13#10 +
                 'Please install the .NET 10 Runtime and try again.' + #13#10#13#10 +
                 'https://dotnet.microsoft.com/download/dotnet/10.0',
                 mbError, MB_OK);
          Abort;
        end;
        #endif
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
