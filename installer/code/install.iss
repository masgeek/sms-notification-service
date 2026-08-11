procedure MaybeStartTrayApp;
var
  ResultCode: Integer;
begin
  if InstallTrayApp and StartTrayAfter then
  begin
    Log('Starting tray app...');
    ShellExec('', ExpandConstant('{app}\{#TrayDir}\{#TrayAppName}.exe'), '', ExpandConstant('{app}\{#TrayDir}'), SW_SHOWNORMAL, ewNoWait, ResultCode);
    Log('Tray app launched.');
  end;
end;

procedure MaybeStartConsoleApp;
var
  ResultCode: Integer;
begin
  if InstallConsoleApp then
  begin
    Log('Starting console app...');
    ShellExec('', ExpandConstant('{app}\{#ConsoleDir}\{#ConsoleAppName}.exe'), '', ExpandConstant('{app}\{#ConsoleDir}'), SW_SHOWNORMAL, ewNoWait, ResultCode);
    Log('Console app launched.');
  end;
end;

procedure DoFreshInstall;
begin
  Log('=== Fresh install started ===');

  if not KeepExistingCfg then
  begin
    RegisterEventLog;
    WriteConfigurationFile(DbPage.Values[0], DbPage.Values[1], DbPage.Values[2], DbPage.Values[3], ApiUrlPage.Values[0], ApiUrlPage.Values[1]);
  end
  else
    Log('Skipping configuration write — keeping existing file.');

  Log('Creating Windows service...');
  ExecuteOrFail(
    'sc.exe',
    'create {#ServiceName} binPath= "' + ExpandConstant('{app}') + '\SmsNotificationService.exe" start= delayed-auto DisplayName= "{#ServiceDisplay}" obj= LocalSystem',
    'Failed to create Windows service.'
  );
  Log('Service created.');

  ExecuteOrFail(
    'sc.exe',
    'create {#AgentServiceName} binPath= "' + ExpandConstant('{app}') + '\{#AgentDir}\SmsNotificationService.Agent.exe" start= delayed-auto DisplayName= "{#AgentServiceDisplay}" obj= LocalSystem',
    'Failed to create the school integration agent service.'
  );
  ConfigureServiceDescription('{#AgentServiceName}', '{#AgentServiceDesc}');
  ConfigureRecovery('{#AgentServiceName}');

  ConfigureServiceDescription('{#ServiceName}', '{#ServiceDesc}');
  ConfigureRecovery('{#ServiceName}');

  Log('Starting service...');
  StopService('{#ServiceName}');
  StartService('{#ServiceName}');
  StartService('{#AgentServiceName}');
  if WaitForServiceState('{#ServiceName}', 'RUNNING', 15000) then
    Log('Service started successfully.')
  else
    MsgBox('The service was created but may not have started.' + #13#10 +
           'Check Windows Event Log for details.', mbInformation, MB_OK);

  if not WaitForServiceState('{#AgentServiceName}', 'RUNNING', 15000) then
    MsgBox('The school integration agent service may not have started.' + #13#10 +
           'Check Windows Event Log for details.', mbInformation, MB_OK);

  Log('=== Fresh install completed ===');
  MaybeStartTrayApp;
  MaybeStartConsoleApp;
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

  if not KeepExistingCfg then
  begin
    Log('Writing updated configuration...');
    WriteConfigurationFile(DbPage.Values[0], DbPage.Values[1], DbPage.Values[2], DbPage.Values[3], ApiUrlPage.Values[0], ApiUrlPage.Values[1]);
  end
  else
    Log('Keeping existing configuration.');

  Log('=== Upgrade pre-install completed (files will be replaced, service restarted in post-install) ===');
end;

procedure DoPostUpgrade;
begin
  Log('Restarting service after upgrade...');
  StartService('{#ServiceName}');
  StartService('{#AgentServiceName}');
  if WaitForServiceState('{#ServiceName}', 'RUNNING', 15000) then
    Log('Service restarted successfully after upgrade.')
  else
    MsgBox('The service was updated but may not have restarted.' + #13#10 +
           'Check Windows Event Log for details.', mbInformation, MB_OK);

  WaitForServiceState('{#AgentServiceName}', 'RUNNING', 15000);

  Log('=== Upgrade completed ===');
  MaybeStartTrayApp;
  MaybeStartConsoleApp;
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
