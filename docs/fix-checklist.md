# Fix Checklist — Codebase Audit

Based on the audit findings, here are the fixes to apply in priority order. Commit after each fix.

---

## CRITICAL

- [ ] **Fix `description_json` column mismatch**
  - File: `src/Data/DapperMapper.cs`
  - Add mapping for `description_json` → `Description`
  - Verify SQL in `NotificationRepository.cs` uses correct column name

- [ ] **Honor `Retryable` flag in NotificationProcessor**
  - File: `src/Workers/NotificationProcessor.cs`
  - Check `result.Retryable` before scheduling retry
  - Non-retryable failures should go straight to `CANCELLED`

---

## HIGH

- [ ] **Move config validation before `builder.Build()`**
  - File: `Program.cs`
  - Call `ValidateSmsServiceOptions` before line 44

- [ ] **Add numeric config bounds validation**
  - File: `src/Configuration/ConfigurationExtensions.cs`
  - Validate: `RetryBackoffSeconds > 0`, `RetryPollIntervalSeconds > 0`, `LogRetentionDays > 0`, `MaxLogFileSizeMb > 0`

- [ ] **Escape installer connection string and JSON values**
  - File: `installer/installer.iss`
  - Apply `JsonEscape` to all user inputs before writing config

- [ ] **Restrict config file permissions**
  - File: `installer/installer.iss`
  - Change from `everyone-readexec` to `admins-full system-full` only

- [ ] **Replace `Thread.Sleep` with `await Task.Delay`**
  - File: `src/Data/SqlDependencyListener.cs`
  - Make `RegisterQueryWithRetry` async
  - Use `await Task.Delay(delay, stoppingToken)` instead of `Thread.Sleep`

- [ ] **Fix `.GetAwaiter().GetResult()` blocking**
  - File: `src/Workers/TableChangeListener.cs`
  - Make the `onChanges` callback async or use `Task.Run`

- [ ] **Add `TOP 100` to pending query**
  - File: `src/Data/NotificationRepository.cs`
  - Add `TOP (@Limit)` with a configurable batch size

---

## MEDIUM

- [ ] **Use named HttpClient instead of creating per-request**
  - File: `src/Services/SmsApiService.cs`
  - Register a named client in `ServiceCollectionExtensions.cs`
  - Use `IHttpClientFactory.CreateClient("SmsApi")` instead of `CreateClient()`

- [ ] **Make `SqlDependencyListener` implement `IDisposable`**
  - File: `src/Data/SqlDependencyListener.cs`
  - Clean up event handlers and connections on dispose

- [ ] **Remove unused `Retryable` property or use it**
  - File: `src/Services/ISmsSender.cs`
  - Either remove or document that it's used by `NotificationProcessor`

- [ ] **Add DB connectivity check in installer**
  - File: `installer/installer.iss`
  - Test connection before creating the service

- [ ] **Validate SMS API URL uses HTTPS**
  - File: `src/Configuration/ConfigurationExtensions.cs`
  - Reject `http://` URLs in validation

- [ ] **Seal remaining classes without inheritance**
  - Files: `src/Data/NotificationRepository.cs`, `src/Data/SqlDependencyListener.cs`
  - Add `sealed` keyword

---

## LOW

- [ ] **Reduce log rotation filesystem calls**
  - File: `src/Logging/FileLoggerProvider.cs`
  - Cache file size checks with a timer instead of checking every log line

- [ ] **Add `Encrypt=True` check for connection string**
  - File: `src/Configuration/ConfigurationExtensions.cs`
  - Warn if not present in production
