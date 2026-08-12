function RunCmd(const Exe, Params: String): Integer;
var
  Res: Boolean;
  ExitCode: Integer;
begin
  Res := Exec(Exe, Params, '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  if Res then
    Result := ExitCode
  else
    Result := -1;
end;

function BoolToStr(B: Boolean): String;
begin
  if B then
    Result := 'True'
  else
    Result := 'False';
end;

procedure ExecuteOrFail(const Exe, Params, FailureMsg: String);
var
  ExitCode: Integer;
  Res: Boolean;
begin
  Res := Exec(Exe, Params, '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  if (not Res) or (ExitCode <> 0) then
    RaiseException(FailureMsg + ' (exit code: ' + IntToStr(ExitCode) + ')');
end;

function JsonEscape(const S: String): String;
begin
  Result := S;
  StringChangeEx(Result, '\', '\\', True);
  StringChangeEx(Result, '"', '\"', True);
end;

function EnrollAgent(const EnrollmentCode, AgentName: String): String;
var
  Http: Variant;
  Response: String;
  Body: String;
  TokenMarker: String;
  TokenStart: Integer;
  TokenEnd: Integer;
  Status: Integer;
begin
  Result := '';
  Body := '{"enrollment_code":"' + JsonEscape(EnrollmentCode) + '","agent_name":"' + JsonEscape(AgentName) + '"}';

  try
    Http := CreateOleObject('WinHttp.WinHttpRequest.5.1');
    Http.Open('POST', 'https://fees.munywele.co.ke/api/agent/enroll', False);
    Http.SetRequestHeader('Content-Type', 'application/json');
    Http.SetTimeouts(5000, 5000, 10000, 10000);
    Http.Send(Body);
    Status := Http.Status;
    Response := Http.ResponseText;
  except
    MsgBox('Could not connect to FeeSyncer for enrollment.' + #13#10 +
           'Check the internet connection and try again.', mbError, MB_OK);
    Exit;
  end;

  if Status <> 201 then
  begin
    if Status = 422 then
      MsgBox('The enrollment code is invalid, expired, or already used.' + #13#10 +
             'Generate a fresh code in the FeeSyncer admin interface.', mbError, MB_OK)
    else
      MsgBox('FeeSyncer enrollment failed (HTTP ' + IntToStr(Status) + ').' + #13#10 +
             'Try again or contact your administrator.', mbError, MB_OK);
    Exit;
  end;

  TokenMarker := '"token":"';
  TokenStart := Pos(TokenMarker, Response);
  if TokenStart = 0 then
    TokenMarker := '"token": "';
  TokenStart := Pos(TokenMarker, Response);
  if TokenStart = 0 then
  begin
    MsgBox('FeeSyncer returned an invalid enrollment response.', mbError, MB_OK);
    Exit;
  end;

  TokenStart := TokenStart + Length(TokenMarker);
  TokenEnd := Pos('"', Copy(Response, TokenStart, Length(Response)));
  if TokenEnd <= 1 then
  begin
    MsgBox('FeeSyncer returned an invalid agent token.', mbError, MB_OK);
    Exit;
  end;

  Result := Copy(Response, TokenStart, TokenEnd - 1);
  if Pos('fsk_', Result) <> 1 then
  begin
    Result := '';
    MsgBox('FeeSyncer returned an invalid agent token.', mbError, MB_OK);
  end;
end;
