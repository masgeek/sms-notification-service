function GetTickCount: DWORD;
  external 'GetTickCount@kernel32.dll stdcall';

function ServiceExists(const SvcName: String): Boolean;
begin
  Result := (RunCmd('sc.exe', 'query "' + SvcName + '"') = 0);
end;

function ServiceIsInState(const SvcName, TargetState: String): Boolean;
begin
  Result := (RunCmd(ExpandConstant('{cmd}'), '/C sc.exe query "' + SvcName +
    '" | findstr /C:"STATE" | findstr /C:"' + TargetState + '" >nul 2>&1') = 0);
end;

function ServiceIsRunning(const SvcName: String): Boolean;
begin
  Result := ServiceExists(SvcName) and ServiceIsInState(SvcName, 'RUNNING');
end;

function StopService(const SvcName: String): Boolean;
begin
  Result := (RunCmd('sc.exe', 'stop "' + SvcName + '"') = 0);
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
  Result := (RunCmd('sc.exe', 'start "' + SvcName + '"') = 0);
end;

function GetServyCliPath: String;
begin
  Result := FileSearch('servy-cli.exe', GetEnv('PATH'));
end;

function ServyCliAvailable: Boolean;
var
  ServyCliPath: String;
begin
  ServyCliPath := GetServyCliPath;
  Result := False;
  if ServyCliPath <> '' then
    Result := (RunCmd(ServyCliPath, 'version') = 0);
end;

function ServiceUsesServy(const SvcName: String): Boolean;
var
  OutputFile: String;
  Content: AnsiString;
  ExitCode: Integer;
begin
  Result := False;
  if not ServiceExists(SvcName) then
    Exit;

  OutputFile := ExpandConstant('{tmp}\svcconfig-' + SvcName + '.txt');
  Exec('cmd.exe', '/C sc.exe qc "' + SvcName + '" > "' + OutputFile + '" 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  if (ExitCode = 0) and LoadStringFromFile(OutputFile, Content) then
    Result := Pos('SERVY', UpperCase(Content)) > 0;
end;

function WaitForServiceDeleted(const SvcName: String; TimeoutMs: Integer): Boolean;
var
  StartTick: Cardinal;
begin
  StartTick := GetTickCount;
  while (GetTickCount - StartTick) < Cardinal(TimeoutMs) do
  begin
    if not ServiceExists(SvcName) then
    begin
      Result := True;
      Exit;
    end;
    Sleep(500);
  end;
  Result := not ServiceExists(SvcName);
end;

function DeleteService(const SvcName: String): Boolean;
begin
  if ServyCliAvailable and ServiceUsesServy(SvcName) then
  begin
    Result := (RunCmd(GetServyCliPath, 'uninstall --name="' + SvcName + '" --quiet') = 0);
    if Result then
      Exit;
    Log('Servy could not uninstall ' + SvcName + '; falling back to sc.exe.');
  end;
  Result := (RunCmd('sc.exe', 'delete "' + SvcName + '"') = 0);
end;

procedure StopServiceForUpgrade(const SvcName: String);
begin
  if not ServiceExists(SvcName) then
    Exit;

  if ServiceIsInState(SvcName, 'STOPPED') then
    Exit;

  Log('Stopping service ' + SvcName + ' for upgrade.');
  StopService(SvcName);
  if not WaitForServiceState(SvcName, 'STOPPED', 30000) then
    RaiseException('Failed to stop the ' + SvcName + ' service. Please stop it manually and try again.');
end;

function RestartServiceAfterUpgrade(const SvcName: String; WasRunning: Boolean): Boolean;
begin
  Result := True;
  if not WasRunning then
    Exit;

  Log('Restarting service ' + SvcName + ' after upgrade.');
  StartService(SvcName);
  Result := WaitForServiceState(SvcName, 'RUNNING', 30000);
  if not Result then
    Log('The ' + SvcName + ' service failed to reach the running state after the upgrade.');
end;

procedure EnsureNativeService(const SvcName, DisplayName, Description, BinaryPath: String);
var
  QuotedBinaryPath: String;
  ExitCode: Integer;
begin
  QuotedBinaryPath := '\"' + BinaryPath + '\"';
  if ServiceExists(SvcName) then
  begin
    Log('Service already exists; updating ' + SvcName + '.');
    ExecuteOrFail(
      'sc.exe',
      'config "' + SvcName + '" binPath= "' + QuotedBinaryPath + '" DisplayName= "' + DisplayName + '" obj= LocalSystem',
      'Failed to update existing service ' + SvcName + '.'
    );
  end
  else
  begin
    Log('Creating service ' + SvcName + '.');
    ExecuteOrFail(
      'sc.exe',
      'create "' + SvcName + '" binPath= "' + QuotedBinaryPath + '" start= demand DisplayName= "' + DisplayName + '" obj= LocalSystem',
      'Failed to create service ' + SvcName + '.'
    );
  end;
  Exec('sc.exe', 'description "' + SvcName + '" "' + Description + '"', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Exec('sc.exe', 'failure ' + SvcName + ' reset= 86400 actions= restart/300000/restart/5000/restart/5000', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Log('Native service fallback configured for ' + SvcName + '.');
end;

procedure EnsureService(const SvcName, DisplayName, Description, BinaryPath: String);
var
  Params: String;
  ExitCode: Integer;
  ExistingServiceUsesServy: Boolean;
  LogPrefix: String;
begin
  if ServyCliAvailable then
  begin
    ExistingServiceUsesServy := ServiceUsesServy(SvcName);
    if ServiceExists(SvcName) and not ExistingServiceUsesServy then
    begin
      Log('Migrating native service ' + SvcName + ' to Servy.');
      RunCmd('sc.exe', 'delete "' + SvcName + '"');
      if not WaitForServiceDeleted(SvcName, 30000) then
        RaiseException('Failed to remove the existing ' + SvcName + ' service before Servy migration.');
    end;

    LogPrefix := ExpandConstant('{commonappdata}\{#ConfigDir}\logs\servy-') + LowerCase(SvcName);
    Params := 'install --name="' + SvcName + '"' +
      ' --displayName="' + DisplayName + '"' +
      ' --description="' + Description + '"' +
      ' --path="' + BinaryPath + '"' +
      ' --startupDir="' + ExtractFileDir(BinaryPath) + '"' +
      ' --startupType="Manual"' +
      ' --stdout="' + LogPrefix + '-stdout.log"' +
      ' --stderr="' + LogPrefix + '-stderr.log"' +
      ' --enableSizeRotation --rotationSize=10' +
      ' --enableDateRotation --dateRotationType="Daily" --maxRotations=7 --useLocalTimeForRotation' +
      ' --enableHealth --heartbeatInterval=10 --maxFailedChecks=3' +
      ' --recoveryAction="RestartProcess" --maxRestartAttempts=3 --quiet';
    ExitCode := RunCmd(GetServyCliPath, Params);
    if ExitCode = 0 then
    begin
      Log('Servy service configured for ' + SvcName + '.');
      Exit;
    end;

    Log('Servy installation failed for ' + SvcName + ' (exit code ' + IntToStr(ExitCode) + '); falling back to sc.exe.');
    if ServiceExists(SvcName) and ServiceUsesServy(SvcName) then
    begin
      RunCmd(GetServyCliPath, 'uninstall --name="' + SvcName + '" --quiet');
      if ServiceExists(SvcName) then
        RunCmd('sc.exe', 'delete "' + SvcName + '"');
      if not WaitForServiceDeleted(SvcName, 30000) then
        RaiseException('Failed to clean up the Servy service ' + SvcName + ' before native fallback.');
    end;
  end
  else
    Log('servy-cli was not found; using native sc.exe for ' + SvcName + '.');

  EnsureNativeService(SvcName, DisplayName, Description, BinaryPath);
end;

function CheckDotNetRuntime: Boolean;
var
  ExitCode: Integer;
  OutputFile: String;
  Content: AnsiString;
begin
  Result := True;
  OutputFile := ExpandConstant('{tmp}\dotnet-runtimes.txt');

  Exec('cmd.exe', '/C dotnet --list-runtimes > "' + OutputFile + '" 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode);

  if ExitCode <> 0 then
  begin
    Log('dotnet --list-runtimes failed (exit code ' + IntToStr(ExitCode) + ').');
    Result := False;
    Exit;
  end;

  if not FileExists(OutputFile) then
  begin
    Log('dotnet runtime output file not found.');
    Result := False;
    Exit;
  end;

  if not LoadStringFromFile(OutputFile, Content) then
  begin
    Log('Could not read dotnet runtime output.');
    Result := False;
    Exit;
  end;

  if Pos('Microsoft.NETCore.App 10', Content) = 0 then
  begin
    Log('.NET 10 runtime not found in installed runtimes.');
    Result := False;
  end
  else
    Log('.NET 10 runtime detected.');
end;
