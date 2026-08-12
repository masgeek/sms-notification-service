procedure InitializeWizard;
var
  PrevPageID: Integer;
begin
  UpgradeMode := False;
  KeepExistingCfg := False;
  InstallTrayApp := True;
  InstallConsoleApp := True;
  PrevPageID := wpSelectDir;

  if ConfigExists then
  begin
    ConfigPromptPage := CreateInputOptionPage(PrevPageID,
      'Configuration Found',
      'An existing appsettings.Production.json was detected.',
      'What would you like to do?',
      True, False);
    ConfigPromptPage.Add('Keep existing configuration');
    ConfigPromptPage.Add('Enter new configuration');
    ConfigPromptPage.Values[0] := True;
    PrevPageID := ConfigPromptPage.ID;
  end;

  AgentPage := CreateInputQueryPage(PrevPageID,
    'School Agent Enrollment',
    'Connect this installation to a school in FeeSyncer.',
    'Generate a single-use enrollment code in the FeeSyncer admin interface, then enter it below. The installer exchanges it for the permanent agent token automatically.' + #13#10 +
    'The code expires after 15 minutes and can be used once.');
  AgentPage.Add('Enrollment code (enroll_...):', True);
  AgentPage.Add('Agent name:', False);
  AgentPage.Add('Local fee-processor API URL:', False);
  AgentPage.Add('Local API username:', False);
  AgentPage.Add('Local API password:', True);
  AgentPage.Values[1] := 'School Agent';
  AgentPage.Values[2] := 'http://127.0.0.1:8001/api/';
  PrevPageID := AgentPage.ID;

  DbPage := CreateInputQueryPage(PrevPageID,
    'Database Server',
    'Enter the SQL Server connection details.',
    '');
  DbPage.Add('Server (e.g. 127.0.0.1):', False);
  DbPage.Add('Database (e.g. school):', False);
  DbPage.Add('Username (e.g. sa):', False);
  DbPage.Add('Password:', True);
  DbPage.Values[0] := '127.0.0.1';
  DbPage.Values[1] := 'school';
  DbPage.Values[2] := 'sa';

  ApiUrlPage := CreateInputQueryPage(DbPage.ID,
    'SMS API Configuration',
    'Enter the SMS API endpoint URL and authorization token.',
    '');
  ApiUrlPage.Add('API URL:', False);
  ApiUrlPage.Add('Bearer Token:', True);
  ApiUrlPage.Values[0] := 'https://fees.munywele.co.ke/api/v1/notifications';

  TrayPage := CreateInputOptionPage(ApiUrlPage.ID,
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
    'Start the tray app after installation?',
    'The tray app will appear in your system tray notification area.',
    True, False);
  StartTrayPage.Add('Start tray app now');
  StartTrayPage.Values[0] := True;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;

  if not ConfigExists then
  begin
    if UpgradeMode and (PageID = TrayPage.ID) then
      Result := True;
    if UpgradeMode and (PageID = ConsolePage.ID) then
      Result := True;
    if (PageID = StartTrayPage.ID) then
      Result := not InstallTrayApp;
    Exit;
  end;

  if KeepExistingCfg then
  begin
    Result := (PageID = DbPage.ID) or (PageID = ApiUrlPage.ID) or (PageID = AgentPage.ID) or (PageID = TrayPage.ID) or (PageID = ConsolePage.ID) or (PageID = StartTrayPage.ID);
    Exit;
  end;

  if UpgradeMode and (PageID = TrayPage.ID) then
    Result := True;
  if UpgradeMode and (PageID = ConsolePage.ID) then
    Result := True;
  if (PageID = StartTrayPage.ID) then
    Result := not InstallTrayApp;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if ConfigExists and (CurPageID = ConfigPromptPage.ID) then
  begin
    KeepExistingCfg := ConfigPromptPage.Values[0];
    if KeepExistingCfg then
      Log('User chose to keep existing configuration.');
  end;

  if CurPageID = DbPage.ID then
  begin
    if Trim(DbPage.Values[0]) = '' then
    begin
      MsgBox('The server name cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if Trim(DbPage.Values[1]) = '' then
    begin
      MsgBox('The database name cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if Trim(DbPage.Values[2]) = '' then
    begin
      MsgBox('The username cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if Trim(DbPage.Values[3]) = '' then
    begin
      MsgBox('The password cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  if CurPageID = ApiUrlPage.ID then
  begin
    if Trim(ApiUrlPage.Values[0]) = '' then
    begin
      MsgBox('The SMS API URL cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if (Pos('http://', ApiUrlPage.Values[0]) = 0) and (Pos('https://', ApiUrlPage.Values[0]) = 0) then
    begin
      MsgBox('The API URL must start with http:// or https://', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if Trim(ApiUrlPage.Values[1]) = '' then
    begin
      MsgBox('The authorization token cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  if CurPageID = AgentPage.ID then
  begin
    if (Pos('enroll_', Trim(AgentPage.Values[0])) <> 1) then
    begin
      MsgBox('Enter the enrollment code beginning with enroll_.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if Trim(AgentPage.Values[1]) = '' then
    begin
      MsgBox('The agent name cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if Trim(AgentPage.Values[2]) = '' then
    begin
      MsgBox('The local fee-processor API URL cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if Trim(AgentPage.Values[3]) = '' then
    begin
      MsgBox('The local API username cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if Trim(AgentPage.Values[4]) = '' then
    begin
      MsgBox('The local API password cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    if AgentToken <> '' then
      Exit;

    AgentToken := EnrollAgent(Trim(AgentPage.Values[0]), Trim(AgentPage.Values[1]));
    if AgentToken = '' then
    begin
      Result := False;
      Exit;
    end;
    Log('School agent enrollment completed. Token was not logged.');
  end;

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
