procedure InitializeWizard;
var
  PrevPageID: Integer;
begin
  InstallTrayApp := True;
  InstallConsoleApp := True;
  StartTrayAfter := True;
  PrevPageID := wpSelectDir;

  TrayPage := CreateInputOptionPage(PrevPageID,
    'System Tray App',
    'Optional: Install the system tray management app.',
    'The tray app provides service monitoring, log viewing, and a config editor from your taskbar.' + #13#10 +
    'It can be installed or removed later by re-running the installer.',
    True, False);
  TrayPage.Add('Install system tray app (recommended)');
  TrayPage.Values[0] := True;

  ConsolePage := CreateInputOptionPage(TrayPage.ID,
    'Console Monitor App',
    'Optional: Install the console monitor app.',
    'The console app displays live service status in a terminal window.' + #13#10 +
    'It can be installed or removed later by re-running the installer.',
    True, False);
  ConsolePage.Add('Install console monitor app');
  ConsolePage.Values[0] := True;

  StartTrayPage := CreateInputOptionPage(ConsolePage.ID,
    'Start Tray App',
     'Start the SMS tray monitor after installation?',
     'The SMS tray monitor will open the Control Panel and configuration screen.',
    True, False);
  StartTrayPage.Add('Start SMS tray monitor and open configuration');
  StartTrayPage.Values[0] := True;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;

  if (UpgradeMode or SelfUpdateMode) and (PageID = TrayPage.ID) then
    Result := True;
  if (UpgradeMode or SelfUpdateMode) and (PageID = ConsolePage.ID) then
    Result := True;
  if SelfUpdateMode and (PageID = StartTrayPage.ID) then
    Result := True
  else if (PageID = StartTrayPage.ID) then
    Result := not InstallTrayApp;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if CurPageID = TrayPage.ID then
  begin
    InstallTrayApp := TrayPage.Values[0];
    Log('Tray app install: ' + BoolToStr(InstallTrayApp));
  end;

  if CurPageID = ConsolePage.ID then
  begin
    InstallConsoleApp := ConsolePage.Values[0];
    Log('Console app install: ' + BoolToStr(InstallConsoleApp));
  end;

  if CurPageID = StartTrayPage.ID then
  begin
    StartTrayAfter := StartTrayPage.Values[0];
    Log('Start tray after install: ' + BoolToStr(StartTrayAfter));
  end;
end;
