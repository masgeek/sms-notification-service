function GetTickCount: DWORD;
  external 'GetTickCount@kernel32.dll stdcall';

function ServiceExists(const SvcName: String): Boolean;
begin
  Result := (RunCmd('sc.exe', 'query ' + SvcName) = 0);
end;

function StopService(const SvcName: String): Boolean;
begin
  Result := (RunCmd('sc.exe', 'stop ' + SvcName) = 0);
end;

function WaitForServiceState(const SvcName: String; const TargetState: String; TimeoutMs: Integer): Boolean;
var
  StartTick: Cardinal;
  OutputFile: String;
  Cmd: String;
  Content: AnsiString;
  ExitCode: Integer;
begin
  Result := False;
  StartTick := GetTickCount;
  OutputFile := ExpandConstant('{tmp}\svcstate.txt');

  while (GetTickCount - StartTick) < Cardinal(TimeoutMs) do
  begin
    Cmd := 'sc query "' + SvcName + '" | findstr /C:"STATE"';
    Exec('cmd.exe', '/C ' + Cmd + ' > "' + OutputFile + '" 2>&1',
      '', SW_HIDE, ewWaitUntilTerminated, ExitCode);

    if FileExists(OutputFile) then
    begin
      if LoadStringFromFile(OutputFile, Content) then
      begin
        if Pos(UpperCase(TargetState), UpperCase(Content)) > 0 then
        begin
          Result := True;
          Exit;
        end;
      end;
    end;

    Sleep(500);
  end;

  Log('WaitForServiceState: timed out waiting for ' + TargetState);
  Result := False;
end;

function StartService(const SvcName: String): Boolean;
begin
  Result := (RunCmd('sc.exe', 'start ' + SvcName) = 0);
end;

function DeleteService(const SvcName: String): Boolean;
begin
  Result := (RunCmd('sc.exe', 'delete ' + SvcName) = 0);
end;

procedure ConfigureServiceDescription(const SvcName, Description: String);
var
  ExitCode: Integer;
begin
  Exec('sc.exe', 'description ' + SvcName + ' "' + Description + '"', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Log('Service description configured.');
end;

procedure ConfigureDelayedAutoStart(const SvcName: String);
var
  ExitCode: Integer;
begin
  Exec('sc.exe', 'config ' + SvcName + ' start= delayed-auto', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Log('Delayed auto-start configured.');
end;

procedure ConfigureRecovery(const SvcName: String);
var
  ExitCode: Integer;
begin
  Exec('sc.exe', 'failure ' + SvcName + ' reset= 86400 actions= restart/300000/restart/5000/restart/5000', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Log('Failure recovery configured (5min, 5s, 5s).');
end;
