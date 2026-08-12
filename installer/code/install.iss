procedure MaybeStartTrayApp;
var
  ResultCode: Integer;
begin
  if InstallTrayApp and StartTrayAfter then
  begin
    Log('Starting tray app...');
    ShellExec('', ExpandConstant('{app}\{#TrayDir}\{#TrayAppName}.exe'), '--setup', ExpandConstant('{app}\{#TrayDir}'), SW_SHOWNORMAL, ewNoWait, ResultCode);
    Log('Tray app launched.');
  end;
end;

procedure DoFreshInstall;
begin
  Log('=== Fresh install started ===');

  RegisterEventLog;
  Log('Configuration is managed by the application; installer will not write credentials.');

  Log('Creating Windows service...');
  ExecuteOrFail(
    'sc.exe',
    'create {#ServiceName} binPath= "' + ExpandConstant('{app}') + '\FeeSyncer.Sms.exe" start= demand DisplayName= "{#ServiceDisplay}" obj= LocalSystem',
    'Failed to create Windows service.'
  );
  Log('Service created.');

  ExecuteOrFail(
    'sc.exe',
    'create {#AgentServiceName} binPath= "' + ExpandConstant('{app}') + '\{#AgentDir}\FeeSyncer.Agent.exe" start= demand DisplayName= "{#AgentServiceDisplay}" obj= LocalSystem',
    'Failed to create the school integration agent service.'
  );
  ConfigureServiceDescription('{#AgentServiceName}', '{#AgentServiceDesc}');
  ConfigureRecovery('{#AgentServiceName}');

  ConfigureServiceDescription('{#ServiceName}', '{#ServiceDesc}');
  ConfigureRecovery('{#ServiceName}');

  Log('Services installed but left stopped until configuration and connection checks pass.');

  Log('=== Fresh install completed ===');
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

  Log('Configuration is managed by the application; existing configuration was not changed.');

  Log('=== Upgrade pre-install completed (files will be replaced, service restarted in post-install) ===');
end;

procedure DoPostUpgrade;
begin
  Log('Upgrade completed; services remain stopped until configuration is verified.');

  Log('=== Upgrade completed ===');
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
