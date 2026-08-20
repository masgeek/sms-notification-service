var
  TrayPage         : TInputOptionWizardPage;
  ConsolePage      : TInputOptionWizardPage;
  StartTrayPage    : TInputOptionWizardPage;
  UpgradeMode      : Boolean;
  SelfUpdateMode   : Boolean;
  RestartTrayAfterSelfUpdate : Boolean;
  SelfUpdateSucceeded : Boolean;
  UpgradeShutdownStarted : Boolean;
  InstallTrayApp   : Boolean;
  InstallConsoleApp : Boolean;
  StartTrayAfter   : Boolean;
  SmsServiceWasRunning : Boolean;
  AgentServiceWasRunning : Boolean;

function InitializeSetup: Boolean;
begin
  Result := True;
  SelfUpdateMode := ExpandConstant('{param:SELFUPDATE|0}') = '1';
  RestartTrayAfterSelfUpdate := ExpandConstant('{param:RESTARTTRAY|0}') = '1';
  SelfUpdateSucceeded := False;
  UpgradeShutdownStarted := False;
  UpgradeMode := ServiceExists('{#ServiceName}') or ServiceExists('{#AgentServiceName}');
  if UpgradeMode then
    Log('Existing installation detected; upgrade mode enabled.');
  if SelfUpdateMode then
    Log('Tray-driven self-update mode enabled.');
end;

function ShouldCreateDataDirectories: Boolean;
begin
  Result := not UpgradeMode and not SelfUpdateMode;
end;

function ShouldInstallTrayApp: Boolean;
begin
  Result := InstallTrayApp;
end;

function ShouldInstallConsoleApp: Boolean;
begin
  Result := InstallConsoleApp;
end;
