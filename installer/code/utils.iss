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
