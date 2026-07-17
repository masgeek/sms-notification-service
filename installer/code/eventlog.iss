procedure RegisterEventLog;
var
  ExitCode: Integer;
begin
  Exec(
    'powershell.exe',
    '-NoProfile -ExecutionPolicy Bypass -Command "try { [System.Diagnostics.EventLog]::CreateEventSource(''' +
      '{#EventLogSource}' + ''', ''Application'') } catch {}"',
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode
  );
  Log('Event log source registered: {#EventLogSource}');
end;

procedure RemoveEventLog;
var
  ExitCode: Integer;
begin
  Exec(
    'powershell.exe',
    '-NoProfile -ExecutionPolicy Bypass -Command "try { [System.Diagnostics.EventLog]::DeleteEventSource(''' +
      '{#EventLogSource}' + ''') } catch {}"',
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode
  );
  Log('Event log source removed: {#EventLogSource}');
end;
