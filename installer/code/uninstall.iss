procedure DoUninstall;
var
  PreserveCfg: Integer;
begin
  Log('=== Uninstall started ===');

  Log('Stopping service...');
  StopService('{#ServiceName}');
  WaitForServiceState('{#ServiceName}', 'STOPPED', 30000);

  Log('Deleting service...');
  if ServiceExists('{#ServiceName}') then
  begin
    DeleteService('{#ServiceName}');
    Sleep(1000);
  end;

  if ServiceExists('{#ServiceName}') then
  begin
    Log('Service still exists. Retrying...');
    DeleteService('{#ServiceName}');
    Sleep(2000);
  end;

  if ServiceExists('{#ServiceName}') then
    Log('Service could not be deleted via sc. Registry cleanup will handle it.')
  else
    Log('Service deleted successfully.');

  RemoveEventLog;

  PreserveCfg := MsgBox(
    'Do you want to preserve the configuration file?' + #13#10 + #13#10 +
    'Location: ' + ExpandConstant('{commonappdata}\{#ConfigDir}') + #13#10 + #13#10 +
    'Click Yes to keep configuration for future reinstall.' + #13#10 +
    'Click No to remove all configuration files.',
    mbConfirmation,
    MB_YESNO or MB_DEFBUTTON2
  );

  if PreserveCfg = IDNO then
  begin
    Log('Removing configuration directory...');
    DelTree(ExpandConstant('{commonappdata}\{#ConfigDir}'), True, True, True);
  end
  else
    Log('Preserving configuration directory.');

  RegDeleteKeyIncludingSubkeys(HKLM, 'SYSTEM\CurrentControlSet\Services\{#ServiceName}');
  Log('Registry cleanup completed.');

  Log('=== Uninstall completed ===');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    DoUninstall;
end;
