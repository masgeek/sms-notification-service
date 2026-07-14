# SmsNotificationService — Plan

## Completed

| # | Feature | Solution | Files |
|---|---|---|---|
| 1 | **Concurrency Guard** | `SemaphoreSlim` serializes processing, concurrent events skipped | `Worker.cs` |
| 2 | **Retry with Backoff** | Configurable exponential backoff, per-notification retry tracking | `Worker.cs`, `SmsServiceOptions.cs` |
| 3 | **Config Validation** | Typed `IOptions<SmsServiceOptions>`, fails fast at startup | `Program.cs`, `Worker.cs`, `SmsServiceOptions.cs` |
| 4 | **Listener Resilience** | `RegisterQueryWithRetry` retries 5 times on failure | `Worker.cs` |
| 5 | **Graceful Shutdown** | `Interlocked` counter, waits up to 30s for in-flight sends | `Worker.cs` |
| 6 | **Startup DB Check** | Validates database connectivity before service starts | `DatabaseConnectionCheck.cs` |
| 7 | **Startup Catch-up** | Processes existing PENDING notifications on restart | `Worker.cs` |
| 8 | **Retry Tracking** | `retry_count`, `max_retries`, `retry_after` columns with CANCELLED status | `Worker.cs`, `SmsNotification.cs`, `NotificationStatus.cs` |

---

## Proposed Features

### 1. Rate Limiting

**Why:** Bulk inserts flood the API with concurrent requests.

**How:** Use `SemaphoreSlim` with a count > 1 (e.g., 5) or a token bucket to cap concurrent SMS sends per second.

**Priority:** High

---

### 2. Circuit Breaker

**Why:** If the API is down, retries keep hammering it. Let it recover.

**How:** Track consecutive failures. After N failures, open the circuit and stop calling the API for a cooldown period. Auto-reset after cooldown.

**Priority:** High

---

### 3. Metrics Logging

**Why:** No visibility into throughput or failure rates.

**How:** Atomic counters for sent/failed/skipped. Log a summary every N minutes (e.g., "Last 5min: 42 sent, 3 failed, 0 skipped").

**Priority:** Medium

---

### 4. Phone Number Validation

**Why:** Invalid numbers waste API calls and create noise.

**How:** Validate format before sending (e.g., must start with country code, minimum length). Mark as `CANCELLED` instead of retrying.

**Priority:** Medium

---

### 5. Configurable Message Template

**Why:** Hardcoded message format requires code changes to modify.

**How:** Add a `MessageTemplate` field to `SmsServiceOptions`. Use `string.Format` or named placeholders.

**Priority:** Low

---

### 6. Multiple API Fallback

**Why:** Single point of failure if the primary SMS provider goes down.

**How:** Add a `FallbackSmsApiUrl` to config. If primary fails all retries, try fallback. Log which provider was used.

**Priority:** Low

---

## Priority Order

1. Rate Limiting — prevents API flooding
2. Circuit Breaker — protects against API downtime
3. Metrics Logging — operational visibility
4. Phone Number Validation — reduces wasted calls
5. Configurable Message Template — flexibility
6. Multiple API Fallback — redundancy
