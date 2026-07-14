# SmsNotificationService — Improvement Plan

## 1. Concurrency Guard

**Problem:** Two simultaneous `SqlDependency` change events can trigger `ProcessPendingNotifications` concurrently, causing duplicate SMS sends for the same notification.

**Solution:** Use a `SemaphoreSlim` to serialize notification processing.

**Files:** `Worker.cs`

```
- Add private readonly SemaphoreSlim _processingLock = new(1, 1);
- Wrap ProcessPendingNotifications with await _processingLock.WaitAsync()
- Release in finally block
```

**Priority:** High

---

## 2. Retry with Backoff

**Problem:** If the SMS API returns an error or times out, the notification is marked `FAILED` permanently with no recovery.

**Solution:** Implement retry logic with exponential backoff before marking as `FAILED`.

**Files:** `Worker.cs`

```
- Add retry loop (3 attempts) in SendSmsNotification
- Exponential backoff: 2s, 4s, 8s between retries
- Only mark as FAILED after all retries exhausted
- Log each retry attempt
```

**Priority:** High

---

## 3. Config Validation

**Problem:** Using `!` (null-forgiving) on config values means the app crashes at runtime with unhelpful errors if config is missing.

**Solution:** Validate configuration at startup before the service begins.

**Files:** `Program.cs`, `Worker.cs`

```
- Add config validation in Program.cs after host build
- Check ConnectionStrings:Default is not empty
- Check SmsApi:Url is not empty and is a valid URI
- Throw descriptive InvalidOperationException on missing config
- Remove ! operators from Worker constructor
```

**Priority:** Medium

---

## 4. SqlDependency Re-registration Resilience

**Problem:** If `RegisterQuery` fails in its catch block, the listener dies silently with no recovery. No re-attempt is made.

**Solution:** Wrap re-registration in a retry loop with backoff.

**Files:** `Worker.cs`

```
- On failure in RegisterQuery, retry up to 5 times with exponential backoff
- Log each retry attempt with attempt number
- After max retries, log critical and stop the service gracefully
- Use Task.Delay between retries (respecting CancellationToken)
```

**Priority:** Medium

---

## 5. Health Checks

**Problem:** No way to monitor if the service is alive and actively processing notifications.

**Solution:** Add ASP.NET Core health checks endpoint.

**Files:** `Program.cs`, `Worker.cs`

```
- Add Microsoft.Extensions.Diagnostics.HealthChecks NuGet package
- Register health checks in Program.cs
- Add Kestrel endpoint on a dedicated port (e.g., 5000)
- Add custom health check that verifies:
  - Database connectivity
  - SqlDependency listener is active
- Expose /health endpoint
```

**Priority:** Medium

---

## 6. Graceful Shutdown

**Problem:** In-flight SMS sends get killed mid-request when the service stops.

**Solution:** Track active operations and wait for them to complete during shutdown.

**Files:** `Worker.cs`

```
- Add a counter or Activity tracking for in-flight operations
- Override StopAsync to wait for active sends to complete
- Use a timeout (e.g., 30s) to avoid blocking indefinitely
- Log shutdown progress
```

**Priority:** Low

---

## Implementation Order

1. Concurrency Guard — prevents duplicate sends (quick win)
2. Retry with Backoff — recovers from transient API failures
3. Config Validation — fails fast with clear messages
4. SqlDependency Resilience — keeps listener alive
5. Health Checks — enables monitoring
6. Graceful Shutdown — clean process termination
