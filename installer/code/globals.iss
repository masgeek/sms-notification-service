var
  TrayPage         : TInputOptionWizardPage;
  ConsolePage      : TInputOptionWizardPage;
  StartTrayPage    : TInputOptionWizardPage;
  UpgradeMode      : Boolean;
  InstallTrayApp   : Boolean;
  InstallConsoleApp : Boolean;
  StartTrayAfter   : Boolean;

function InitializeSetup: Boolean;
begin
  Result := True;
  UpgradeMode := ServiceExists('{#ServiceName}');
  if UpgradeMode then
    Log('Existing installation detected — upgrade mode enabled.');
end;

function ShouldInstallTrayApp: Boolean;
begin
  Result := InstallTrayApp;
end;

function ShouldInstallConsoleApp: Boolean;
begin
  Result := InstallConsoleApp;
end;
