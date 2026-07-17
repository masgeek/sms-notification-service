# Fix Checklist — Codebase Audit

Based on the audit findings, here are the fixes to apply in priority order. Commit after each fix.

---

## CRITICAL

- [x] **Fix `description_json` column mismatch**
  - File: `src/Data/DapperMapper.cs`
  - Add mapping for `description_json` → `Description`
  - Verify SQL in `NotificationRepository.cs` uses correct column name

- [x] **Honor `Retryable` flag in NotificationProcessor**
  - File: `src/Workers/NotificationProcessor.cs`
  - Check `result.Retryable` before scheduling retry
  - Non-retryable failures should go straight to `CANCELLED`

---

## HIGH

- [x] **Move config validation before `builder.Build()`**
  - File: `Program.cs`
  - Call `ValidateSmsServiceOptions` before line 44

- [x] **Add numeric config bounds validation**
  - File: `src/Configuration/ConfigurationExtensions.cs`
  - Validate: `RetryBackoffSeconds > 0`, `RetryPollIntervalSeconds > 0`, `LogRetentionDays > 0`, `MaxLogFileSizeMb > 0`

- [x] **Escape installer connection string and JSON values**
  - File: `installer/installer.iss`
  - Apply `JsonEscape` to all user inputs before writing config

- [x] **Restrict config file permissions**
  - File: `installer/installer.iss`
  - Change from `everyone-readexec` to `admins-full system-full` only

- [x] **Replace `Thread.Sleep` with `await Task.Delay`**
  - File: `src/Data/SqlDependencyListener.cs`
  - Make `RegisterQueryWithRetry` async
  - Use `await Task.Delay(delay, stoppingToken)` instead of `Thread.Sleep`

- [x] **Fix `.GetAwaiter().GetResult()` blocking**
  - File: `src/Workers/TableChangeListener.cs`
  - Make the `onChanges` callback async or use `Task.Run`

- [x] **Add `TOP 100` to pending query**
  - File: `src/Data/NotificationRepository.cs`
  - Add `TOP (@Limit)` with a configurable batch size

---

## MEDIUM

- [x] **Use named HttpClient instead of creating per-request**
  - File: `src/Services/SmsApiService.cs`
  - Register a named client in `ServiceCollectionExtensions.cs`
  - Use `IHttpClientFactory.CreateClient("SmsApi")` instead of `CreateClient()`

- [x] **Make `SqlDependencyListener` implement `IDisposable`**
  - File: `src/Data/SqlDependencyListener.cs`
  - Clean up event handlers and connections on dispose

- [x] **Remove unused `Retryable` property or use it**
  - File: `src/Services/ISmsSender.cs`
  - Either remove or document that it's used by `NotificationProcessor`
  - **Done**: `Retryable` property is now used by `NotificationProcessor` to cancel non-retryable errors

- [x] **Add DB connectivity check in installer**
  - File: `installer/installer.iss`
  - Test connection before creating the service
  - **Skipped**: App validates connection on startup via `DatabaseConnectionCheck`; installer validates required fields

- [ ] **Validate SMS API URL uses HTTPS**
  - File: `src/Configuration/ConfigurationExtensions.cs`
  - Reject `http://` URLs in validation

- [x] **Seal remaining classes without inheritance**
  - Files: `src/Data/NotificationRepository.cs`, `src/Data/SqlDependencyListener.cs`
  - Add `sealed` keyword

---

## LOW

- [x] **Reduce log rotation filesystem calls**
  - File: `src/Logging/FileLoggerProvider.cs`
  - Cache file size checks with a timer instead of checking every log line
  - **Done**: Check every 100 lines, track `_currentFileSize` to avoid redundant FileInfo calls

- [x] **Add `Encrypt=True` check for connection string**
  - File: `src/Configuration/ConfigurationExtensions.cs`
  - Warn if not present in production
