# Database Migration Guide

Future reference for supporting databases beyond SQL Server.

## Current Architecture

```
Program.cs
  └── ServiceCollectionExtensions
        ├── SqlDependencyListener (SQL Server Service Broker)
        ├── NotificationRepository (Dapper + SqlConnection)
        └── DatabaseConnectionCheck (SqlConnection + SqlException)
```

**Lock-in points:**
- `Microsoft.Data.SqlClient` — connection/command objects
- `SqlDependency` / Service Broker — real-time change notifications
- `SqlException` — error handling
- `DATETIMEOFFSET` columns — timezone-aware timestamps

**Already portable:**
- `INotificationRepository` — interface, no DB dependency
- `NotificationProcessor` — uses interface only
- `RetryPoller` — uses interface only
- All SQL queries — pure ANSI SQL
- Dapper column mapping — vendor-neutral

---

## Step 1: Abstract Connection Creation

**Current:** `new SqlConnection(connectionString)`

**Target:**

```csharp
// src/Data/IDbConnectionFactory.cs
public interface IDbConnectionFactory
{
    IDbConnection Create();
}

// src/Data/SqlConnectionFactory.cs (SQL Server)
public class SqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;
    public SqlConnectionFactory(string connectionString) => _connectionString = connectionString;
    public IDbConnection Create() => new SqlConnection(_connectionString);
}

// src/Data/PostgresConnectionFactory.cs
public class PostgresConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;
    public PostgresConnectionFactory(string connectionString) => _connectionString = connectionString;
    public IDbConnection Create() => new NpgsqlConnection(_connectionString);
}
```

**Files affected:**
- `src/Checks/DatabaseConnectionCheck.cs` — use `IDbConnectionFactory`
- `src/Data/NotificationRepository.cs` — use `IDbConnectionFactory`
- `src/Data/SqlDependencyListener.cs` — use `IDbConnectionFactory`
- `src/ServiceCollectionExtensions.cs` — register appropriate factory

**Effort:** ~2 hours

---

## Step 2: Abstract Change Notifications

**Current:** `SqlDependency` / Service Broker (SQL Server only)

**Target:**

```csharp
// src/Data/ITableChangeListener.cs
public interface ITableChangeListener : IHostedService
{
    event Func<Task>? OnChange;
}

// src/Data/SqlDependencyListener.cs (existing — SQL Server)
// Uses SqlDependency + Service Broker

// src/Data/PostgresNotifyListener.cs (PostgreSQL)
public class PostgresNotifyListener : ITableChangeListener, IHostedService
{
    public event Func<Task>? OnChange;

    public Task StartAsync(CancellationToken ct)
    {
        // LISTEN sms_notifications;
        // On NOTIFY → fire OnChange
    }

    public Task StopAsync(CancellationToken ct)
    {
        // UNLISTEN sms_notifications;
    }
}

// src/Data/PollingListener.cs (MySQL, SQLite, fallback)
public class PollingListener : ITableChangeListener, IHostedService
{
    private readonly TimeSpan _interval;
    public event Func<Task>? OnChange;

    public Task StartAsync(CancellationToken ct)
    {
        // Timer-based polling every N seconds
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

**SQL Server — Service Broker:**
```sql
ALTER DATABASE school SET ENABLE_BROKER;
```

**PostgreSQL — LISTEN/NOTIFY:**
```sql
-- Trigger function
CREATE OR REPLACE FUNCTION notify_sms_change() RETURNS trigger AS $$
BEGIN
  PERFORM pg_notify('sms_notifications', NEW.id::text);
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Trigger
CREATE TRIGGER sms_notifications_notify
  AFTER INSERT OR UPDATE ON sms_notifications
  FOR EACH ROW EXECUTE FUNCTION notify_sms_change();
```

**MySQL / SQLite — Polling:**
```sql
-- No special setup needed. App polls periodically.
-- Add an index on (status, retry_after) for efficient polling.
CREATE INDEX idx_sms_pending ON sms_notifications(status, retry_after);
```

**Files affected:**
- `src/Workers/TableChangeListener.cs` — inject `ITableChangeListener`
- `src/ServiceCollectionExtensions.cs` — register appropriate listener

**Effort:** ~1-2 days

---

## Step 3: Abstract Error Handling

**Current:** `catch (SqlException ex)`

**Target:**

```csharp
// Catch generic DbException (base class for all ADO.NET exceptions)
catch (DbException ex)
{
    logger.LogError(ex, "Database error");
}
```

**Files affected:**
- `src/Checks/DatabaseConnectionCheck.cs` — `SqlException` → `DbException`

**Effort:** ~30 minutes

---

## Step 4: Update Connection Strings

| Database | Connection String Format |
|----------|------------------------|
| SQL Server | `Server=127.0.0.1;Database=school;User Id=sa;Password=...;TrustServerCertificate=True;` |
| PostgreSQL | `Host=127.0.0.1;Database=school;Username=postgres;Password=...;SSL Mode=Prefer;` |
| MySQL | `Server=127.0.0.1;Database=school;Uid=root;Password=...;SslMode=Preferred;` |
| SQLite | `Data Source=C:\path\to\sms.db;` |

**Config changes:**
- `SmsServiceOptions.ConnectionString` stays the same — just change the value
- Add `SmsServiceOptions.DatabaseProvider` enum: `SqlServer`, `Postgres`, `MySql`, `Sqlite`

**Effort:** ~1 hour

---

## Step 5: Update Schema

### SQL Server (current)
```sql
CREATE TABLE sms_notifications (
    id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    phone_number    NVARCHAR(50)    NOT NULL,
    mpesa_code      NVARCHAR(100)   NOT NULL,
    adm_no          NVARCHAR(50)    NOT NULL,
    stud_names      NVARCHAR(200)   NULL,
    amount          DECIMAL(18,2)   NULL,
    receipt_no      NVARCHAR(100)   NULL,
    dated           DATETIME        NULL,
    status          NVARCHAR(20)    NOT NULL DEFAULT 'PENDING',
    max_retries     INT             NOT NULL DEFAULT 5,
    retry_count     INT             NOT NULL DEFAULT 0,
    retry_after     DATETIME        NULL,
    created_at      DATETIMEOFFSET  NULL,
    updated_at      DATETIMEOFFSET  NULL
);
```

### PostgreSQL
```sql
CREATE TABLE sms_notifications (
    id              BIGSERIAL       PRIMARY KEY,
    phone_number    VARCHAR(50)     NOT NULL,
    mpesa_code      VARCHAR(100)    NOT NULL,
    adm_no          VARCHAR(50)     NOT NULL,
    stud_names      VARCHAR(200)    NULL,
    amount          NUMERIC(18,2)   NULL,
    receipt_no      VARCHAR(100)    NULL,
    dated           TIMESTAMP       NULL,
    status          VARCHAR(20)     NOT NULL DEFAULT 'PENDING',
    max_retries     INTEGER         NOT NULL DEFAULT 5,
    retry_count     INTEGER         NOT NULL DEFAULT 0,
    retry_after     TIMESTAMP       NULL,
    created_at      TIMESTAMPTZ     NULL,
    updated_at      TIMESTAMPTZ     NULL
);
```

### MySQL
```sql
CREATE TABLE sms_notifications (
    id              BIGINT          NOT NULL AUTO_INCREMENT PRIMARY KEY,
    phone_number    VARCHAR(50)     NOT NULL,
    mpesa_code      VARCHAR(100)    NOT NULL,
    adm_no          VARCHAR(50)     NOT NULL,
    stud_names      VARCHAR(200)    NULL,
    amount          DECIMAL(18,2)   NULL,
    receipt_no      VARCHAR(100)    NULL,
    dated           DATETIME        NULL,
    status          VARCHAR(20)     NOT NULL DEFAULT 'PENDING',
    max_retries     INT             NOT NULL DEFAULT 5,
    retry_count     INT             NOT NULL DEFAULT 0,
    retry_after     DATETIME        NULL,
    created_at      TIMESTAMP       NULL,
    updated_at      TIMESTAMP       NULL
);
```

### SQLite
```sql
CREATE TABLE sms_notifications (
    id              INTEGER         PRIMARY KEY AUTOINCREMENT,
    phone_number    TEXT            NOT NULL,
    mpesa_code      TEXT            NOT NULL,
    adm_no          TEXT            NOT NULL,
    stud_names      TEXT            NULL,
    amount          REAL            NULL,
    receipt_no      TEXT            NULL,
    dated           TEXT            NULL,
    status          TEXT            NOT NULL DEFAULT 'PENDING',
    max_retries     INTEGER         NOT NULL DEFAULT 5,
    retry_count     INTEGER         NOT NULL DEFAULT 0,
    retry_after     TEXT            NULL,
    created_at      TEXT            NULL,
    updated_at      TEXT            NULL
);
```

**Effort:** ~2 hours per database

---

## Step 6: NuGet Package Changes

| Database | Add Package | Remove Package |
|----------|-------------|----------------|
| SQL Server | (none — current) | — |
| PostgreSQL | `Npgsql` | `Microsoft.Data.SqlClient` |
| MySQL | `MySqlConnector` | `Microsoft.Data.SqlClient` |
| SQLite | `Microsoft.Data.Sqlite` | `Microsoft.Data.SqlClient` |

**Effort:** ~15 minutes per database

---

## Step 7: Update DI Registration

```csharp
// ServiceCollectionExtensions.cs
public static IServiceCollection AddSmsNotificationServices(
    this IServiceCollection services, IConfiguration configuration)
{
    var provider = configuration["SmsService:DatabaseProvider"] ?? "SqlServer";

    switch (provider)
    {
        case "Postgres":
            services.AddSingleton<IDbConnectionFactory, PostgresConnectionFactory>();
            services.AddSingleton<ITableChangeListener, PostgresNotifyListener>();
            break;
        case "MySql":
            services.AddSingleton<IDbConnectionFactory, MySqlConnectionFactory>();
            services.AddSingleton<ITableChangeListener, PollingListener>();
            break;
        case "Sqlite":
            services.AddSingleton<IDbConnectionFactory, SqliteConnectionFactory>();
            services.AddSingleton<ITableChangeListener, PollingListener>();
            break;
        default:
            services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();
            services.AddSingleton<ITableChangeListener, SqlDependencyListener>();
            break;
    }
    // ...
}
```

**Effort:** ~1 hour

---

## Effort Summary

| Database | Effort | Priority | Notes |
|----------|--------|----------|-------|
| PostgreSQL | 2-3 days | High | Best alternative. Native LISTEN/NOTIFY. |
| MySQL | 2-3 days | Medium | No native change notification. Polling required. |
| SQLite | 3-4 days | Low | Not recommended for production services. |
| **Multi-DB abstract** | **1 week** | — | Supports all via provider selection. |

---

## File Change Summary

| File | Change Required | Databases Affected |
|------|----------------|-------------------|
| `src/Data/IDbConnectionFactory.cs` | **New** — interface | All |
| `src/Data/SqlConnectionFactory.cs` | **New** — SQL Server | SQL Server |
| `src/Data/PostgresConnectionFactory.cs` | **New** — PostgreSQL | PostgreSQL |
| `src/Data/MySqlConnectionFactory.cs` | **New** — MySQL | MySQL |
| `src/Data/SqliteConnectionFactory.cs` | **New** — SQLite | SQLite |
| `src/Data/ITableChangeListener.cs` | **New** — interface | All |
| `src/Data/PostgresNotifyListener.cs` | **New** — LISTEN/NOTIFY | PostgreSQL |
| `src/Data/PollingListener.cs` | **New** — timer-based | MySQL, SQLite |
| `src/Data/NotificationRepository.cs` | Update — use `IDbConnectionFactory` | All |
| `src/Data/SqlDependencyListener.cs` | Rename/keep — SQL Server only | SQL Server |
| `src/Checks/DatabaseConnectionCheck.cs` | Update — `DbException` | All |
| `src/ServiceCollectionExtensions.cs` | Update — provider switch | All |
| `src/Configuration/SmsServiceOptions.cs` | Add `DatabaseProvider` | All |
| `SmsNotificationService.csproj` | Swap provider package | Per database |

---

## Testing Strategy

1. **Unit tests** — Already mock `INotificationRepository`, no changes needed
2. **Integration tests** — Add per-database test fixtures:
   - `SqlServerIntegrationTests` — existing (if added)
   - `PostgresIntegrationTests` — Docker container + Npgsql
   - `MySqlIntegrationTests` — Docker container + MySqlConnector
3. **CI matrix** — Run integration tests against all supported databases

---

## Rollout Plan

1. **Phase 1:** Abstract connection + error handling (1 day)
2. **Phase 2:** PostgreSQL support (2 days)
3. **Phase 3:** MySQL support (2 days)
4. **Phase 4:** SQLite support if needed (2 days)
5. **Phase 5:** CI matrix + integration tests (1 day)

**Total: ~8 days for full multi-database support**
