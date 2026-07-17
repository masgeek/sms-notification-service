procedure InitializeWizard;
var
  PrevPageID: Integer;
begin
  UpgradeMode := False;
  KeepExistingCfg := False;
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

  DbPage := CreateInputQueryPage(PrevPageID,
    'Database Server',
    'Enter the SQL Server connection details.',
    '');
  DbPage.Add('Server (e.g. 127.0.0.1):', False);
  DbPage.Add('Database (e.g. school):', False);
  DbPage.Add('Username (e.g. sa):', False);
  DbPage.Add('Password:', True);
  DbPage.Values[0] := '127.0.0.1';
  DbPage.Values[2] := 'sa';

  ApiUrlPage := CreateInputQueryPage(DbPage.ID,
    'SMS API Configuration',
    'Enter the SMS API endpoint URL.',
    'API URL:');
  ApiUrlPage.Add('https://fees.munywele.co.ke/api/v1/notifications', False);

  AuthPage := CreateInputQueryPage(ApiUrlPage.ID,
    'Authorization',
    'Enter the bearer token for the SMS API.',
    'Token:');
  AuthPage.Add('', True);
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;

  if not ConfigExists then
    Exit;

  if KeepExistingCfg then
  begin
    Result := (PageID = DbPage.ID) or (PageID = ApiUrlPage.ID) or (PageID = AuthPage.ID);
    Exit;
  end;
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

    if not TestDbConnection(DbPage.Values[0], DbPage.Values[1], DbPage.Values[2], DbPage.Values[3]) then
    begin
      if MsgBox('Could not connect to the database. Do you want to continue anyway?' + #13#10 + #13#10 +
                'The service may fail to start if the connection is invalid.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDNO then
      begin
        Result := False;
        Exit;
      end;
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
  end;

  if CurPageID = AuthPage.ID then
  begin
    if Trim(AuthPage.Values[0]) = '' then
    begin
      MsgBox('The authorization token cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;
end;
