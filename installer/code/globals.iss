var
  DbPage           : TInputQueryWizardPage;
  ApiUrlPage       : TInputQueryWizardPage;
  TrayPage         : TInputOptionWizardPage;
  ConsolePage      : TInputOptionWizardPage;
  StartTrayPage    : TInputOptionWizardPage;
  UpgradeMode      : Boolean;
  ConfigExists     : Boolean;
  KeepExistingCfg  : Boolean;
  InstallTrayApp   : Boolean;
  InstallConsoleApp : Boolean;
  StartTrayAfter   : Boolean;
  ConfigPromptPage : TInputOptionWizardPage;

function InitializeSetup: Boolean;
begin
  Result := True;
  UpgradeMode := ServiceExists('{#ServiceName}');
  ConfigExists := FileExists(ExpandConstant('{commonappdata}\{#ConfigDir}\{#ConfigFile}'));
  if UpgradeMode then
    Log('Existing installation detected — upgrade mode enabled.');
  if ConfigExists then
    Log('Existing configuration file detected.');
end;

function ShouldInstallTrayApp: Boolean;
begin
  Result := InstallTrayApp;
end;

function ShouldInstallConsoleApp: Boolean;
begin
  Result := InstallConsoleApp;
end;
