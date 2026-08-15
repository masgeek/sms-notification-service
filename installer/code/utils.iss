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
  ResultCode: Integer;
  Res: Boolean;
begin
  ResultCode := -1;
  Res := Exec(Exe, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if not Res then
  begin
    MsgBox(FailureMsg + ' (could not start the process)', mbError, MB_OK);
    Abort;
  end;
  if ResultCode <> 0 then
  begin
    MsgBox(FailureMsg + ' (exit code: ' + IntToStr(ResultCode) + ')', mbError, MB_OK);
    Abort;
  end;
end;

function JsonEscape(const S: String): String;
begin
  Result := S;
  StringChangeEx(Result, '\', '\\', True);
  StringChangeEx(Result, '"', '\"', True);
end;
