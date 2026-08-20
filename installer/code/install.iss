function StartTrayAsOriginalUser(const Arguments: String): Boolean;
var
  ResultCode: Integer;
  TrayPath: String;
begin
  TrayPath := ExpandConstant('{app}\{#TrayDir}\{#TrayAppName}.exe');
  Result := ExecAsOriginalUser(TrayPath, Arguments, ExtractFileDir(TrayPath), SW_SHOWNORMAL,
    ewNoWait, ResultCode);
  if not Result then
    Log('ExecAsOriginalUser failed; tray will not be started with the elevated installer token.');
end;

procedure MaybeStartTrayApp;
var
  ResultCode: Integer;
  TrayPath: String;
  CommandLine: String;
begin
  if SelfUpdateMode then
  begin
    if RestartTrayAfterSelfUpdate then
    begin
      Log('Restarting tray app as the original user after self-update...');
      if not StartTrayAsOriginalUser('--updated') then
        Log('Failed to restart the tray app after self-update.');
    end;
    Exit;
  end;

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

  if SelfUpdateMode then
    SelfUpdateSucceeded := True;
  Log('=== Fresh install completed; only the tray monitor will be started ===');
  MaybeStartTrayApp;
end;

procedure DoUpgrade;
begin
  Log('=== Upgrade started ===');

  SmsServiceWasRunning := ServiceIsRunning('{#ServiceName}');
  AgentServiceWasRunning := ServiceIsRunning('{#AgentServiceName}');
  UpgradeShutdownStarted := True;
  StopServiceForUpgrade('{#ServiceName}');
  StopServiceForUpgrade('{#AgentServiceName}');

  EnsureService('{#ServiceName}', '{#ServiceDisplay}', ExpandConstant('{app}') + '\FeeSyncer.Sms.exe');
  EnsureService('{#AgentServiceName}', '{#AgentServiceDisplay}', ExpandConstant('{app}') + '\{#AgentDir}\FeeSyncer.Agent.exe');

  Log('Configuration is managed by the application; existing configuration was not changed.');

  Log('=== Upgrade pre-install completed (files will be replaced, service restarted in post-install) ===');
end;

procedure DoPostUpgrade;
var
  SmsServiceRestarted: Boolean;
  AgentServiceRestarted: Boolean;
begin
  SmsServiceRestarted := RestartServiceAfterUpgrade('{#ServiceName}', SmsServiceWasRunning);
  AgentServiceRestarted := RestartServiceAfterUpgrade('{#AgentServiceName}', AgentServiceWasRunning);

  if not SmsServiceRestarted then
    RaiseException('The {#ServiceName} service did not start successfully after the upgrade.');
  if not AgentServiceRestarted then
    RaiseException('The {#AgentServiceName} service did not start successfully after the upgrade.');

  SelfUpdateSucceeded := True;
  Log('=== Upgrade completed; prior service running state restored ===');
  MaybeStartTrayApp;
end;

procedure DeinitializeSetup;
begin
  if UpgradeShutdownStarted and not SelfUpdateSucceeded then
  begin
    Log('Upgrade did not complete; restoring prior service running state.');
    RestartServiceAfterUpgrade('{#ServiceName}', SmsServiceWasRunning);
    RestartServiceAfterUpgrade('{#AgentServiceName}', AgentServiceWasRunning);
  end;

  if SelfUpdateMode and RestartTrayAfterSelfUpdate and not SelfUpdateSucceeded then
  begin
    Log('Self-update did not complete; restarting tray with failure status.');
    if not StartTrayAsOriginalUser('--update-failed') then
      Log('Failed to restart the tray app after an unsuccessful self-update.');
  end;
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
