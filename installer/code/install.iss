procedure DoFreshInstall;
var
  ExitCode: Integer;
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

  ConfigureServiceDescription('{#ServiceName}', '{#ServiceDesc}');
  ConfigureRecovery('{#ServiceName}');

  Log('Starting service...');
  StopService('{#ServiceName}');
  StartService('{#ServiceName}');
  if WaitForServiceState('{#ServiceName}', 'RUNNING', 15000) then
    Log('Service started successfully.')
  else
    MsgBox('The service was created but may not have started.' + #13#10 +
           'Check Windows Event Log for details.', mbInformation, MB_OK);

  Log('=== Fresh install completed ===');
end;

procedure DoUpgrade;
begin
  Log('=== Upgrade started ===');

  Log('Stopping service for upgrade...');
  StopService('{#ServiceName}');
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
  if WaitForServiceState('{#ServiceName}', 'RUNNING', 15000) then
    Log('Service restarted successfully after upgrade.')
  else
    MsgBox('The service was updated but may not have restarted.' + #13#10 +
           'Check Windows Event Log for details.', mbInformation, MB_OK);

  Log('=== Upgrade completed ===');
end;

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
