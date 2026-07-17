var
  DbServerEdit    : TNewEdit;
  DbDatabaseEdit  : TNewEdit;
  DbUsernameEdit  : TNewEdit;
  DbPasswordEdit  : TNewEdit;
  DbTestButton    : TNewButton;
  DbTestResult    : TNewStaticText;

procedure InitializeWizard;
var
  PrevPageID: Integer;
  Lbl: TNewStaticText;
  TopY: Integer;
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

  DbPage := CreateCustomPage(PrevPageID, 'Database Server', 'Enter the SQL Server connection details.');

  TopY := ScaleY(16);

  Lbl := TNewStaticText.Create(DbPage);
  Lbl.Parent := DbPage;
  Lbl.Left := ScaleX(16);
  Lbl.Top := TopY;
  Lbl.AutoSize := True;
  Lbl.Caption := 'Server (e.g. 127.0.0.1):';
  TopY := TopY + Lbl.Height + ScaleY(4);

  DbServerEdit := TNewEdit.Create(DbPage);
  DbServerEdit.Parent := DbPage;
  DbServerEdit.Left := ScaleX(16);
  DbServerEdit.Top := TopY;
  DbServerEdit.Width := ScaleX(300);
  DbServerEdit.Text := '127.0.0.1';
  TopY := TopY + DbServerEdit.Height + ScaleY(8);

  Lbl := TNewStaticText.Create(DbPage);
  Lbl.Parent := DbPage;
  Lbl.Left := ScaleX(16);
  Lbl.Top := TopY;
  Lbl.AutoSize := True;
  Lbl.Caption := 'Database (e.g. school):';
  TopY := TopY + Lbl.Height + ScaleY(4);

  DbDatabaseEdit := TNewEdit.Create(DbPage);
  DbDatabaseEdit.Parent := DbPage;
  DbDatabaseEdit.Left := ScaleX(16);
  DbDatabaseEdit.Top := TopY;
  DbDatabaseEdit.Width := ScaleX(300);
  DbDatabaseEdit.Text := 'school';
  TopY := TopY + DbDatabaseEdit.Height + ScaleY(8);

  Lbl := TNewStaticText.Create(DbPage);
  Lbl.Parent := DbPage;
  Lbl.Left := ScaleX(16);
  Lbl.Top := TopY;
  Lbl.AutoSize := True;
  Lbl.Caption := 'Username (e.g. sa):';
  TopY := TopY + Lbl.Height + ScaleY(4);

  DbUsernameEdit := TNewEdit.Create(DbPage);
  DbUsernameEdit.Parent := DbPage;
  DbUsernameEdit.Left := ScaleX(16);
  DbUsernameEdit.Top := TopY;
  DbUsernameEdit.Width := ScaleX(300);
  DbUsernameEdit.Text := 'sa';
  TopY := TopY + DbUsernameEdit.Height + ScaleY(8);

  Lbl := TNewStaticText.Create(DbPage);
  Lbl.Parent := DbPage;
  Lbl.Left := ScaleX(16);
  Lbl.Top := TopY;
  Lbl.AutoSize := True;
  Lbl.Caption := 'Password:';
  TopY := TopY + Lbl.Height + ScaleY(4);

  DbPasswordEdit := TNewEdit.Create(DbPage);
  DbPasswordEdit.Parent := DbPage;
  DbPasswordEdit.Left := ScaleX(16);
  DbPasswordEdit.Top := TopY;
  DbPasswordEdit.Width := ScaleX(300);
  DbPasswordEdit.PasswordChar := '*';
  TopY := TopY + DbPasswordEdit.Height + ScaleY(12);

  DbTestButton := TNewButton.Create(DbPage);
  DbTestButton.Parent := DbPage;
  DbTestButton.Left := ScaleX(16);
  DbTestButton.Top := TopY;
  DbTestButton.Width := ScaleX(150);
  DbTestButton.Height := ScaleY(23);
  DbTestButton.Caption := 'Test Connection';
  DbTestButton.OnClick := @DbTestButtonClick;
  TopY := TopY + DbTestButton.Height + ScaleY(8);

  DbTestResult := TNewStaticText.Create(DbPage);
  DbTestResult.Parent := DbPage;
  DbTestResult.Left := ScaleX(16);
  DbTestResult.Top := TopY;
  DbTestResult.Width := ScaleX(350);
  DbTestResult.AutoSize := True;
  DbTestResult.WordWrap := True;
  DbTestResult.Caption := '';

  ApiUrlPage := CreateInputQueryPage(DbPage.ID,
    'SMS API Configuration',
    'Enter the SMS API endpoint URL and authorization token.',
    '');
  ApiUrlPage.Add('API URL:', False);
  ApiUrlPage.Add('Bearer Token:', True);
  ApiUrlPage.Values[0] := 'https://fees.munywele.co.ke/api/v1/notifications';
end;

procedure DbTestButtonClick(Sender: TObject);
var
  Connected: Boolean;
begin
  if Trim(DbServerEdit.Text) = '' then
  begin
    MsgBox('Please enter the database server name first.', mbError, MB_OK);
    Exit;
  end;
  if Trim(DbDatabaseEdit.Text) = '' then
  begin
    MsgBox('Please enter the database name first.', mbError, MB_OK);
    Exit;
  end;
  if Trim(DbUsernameEdit.Text) = '' then
  begin
    MsgBox('Please enter the database username first.', mbError, MB_OK);
    Exit;
  end;
  if Trim(DbPasswordEdit.Text) = '' then
  begin
    MsgBox('Please enter the database password first.', mbError, MB_OK);
    Exit;
  end;

  DbTestResult.Caption := 'Testing connection...';
  DbTestResult.Font.Color := clWindowText;

  Connected := TestDbConnection(DbServerEdit.Text, DbDatabaseEdit.Text, DbUsernameEdit.Text, DbPasswordEdit.Text);

  if Connected then
  begin
    DbTestResult.Caption := 'Connection successful!';
    DbTestResult.Font.Color := clGreen;
    Log('Database connection test: SUCCESS');
  end
  else
  begin
    DbTestResult.Caption := 'Connection failed. Please check your settings.';
    DbTestResult.Font.Color := clRed;
    Log('Database connection test: FAILED');
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;

  if not ConfigExists then
    Exit;

  if KeepExistingCfg then
  begin
    Result := (PageID = DbPage.ID) or (PageID = ApiUrlPage.ID);
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
    if Trim(DbServerEdit.Text) = '' then
    begin
      MsgBox('The server name cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if Trim(DbDatabaseEdit.Text) = '' then
    begin
      MsgBox('The database name cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if Trim(DbUsernameEdit.Text) = '' then
    begin
      MsgBox('The username cannot be empty.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if Trim(DbPasswordEdit.Text) = '' then
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
end;
