# Database Migration Guide

FeeSyncer currently supports SQL Server only. This document outlines the work
required to add another database provider.

## SQL Server Lock-In

- `Microsoft.Data.SqlClient` connection and exception types
- `SqlDependency` and Service Broker notifications
- `TOP 100`, `dbo` qualification, and `SYSUTCDATETIME()`
- SQL Server system catalogs used by diagnostics
- `DATETIME` and `DATETIMEOFFSET` assumptions
- Tray notification insertion and connection-string editing

Portable areas include `INotificationRepository`, `NotificationProcessor`,
`RetryPoller`, HTTP sending, and most domain models. The current query text is
not fully ANSI SQL.

## Proposed Abstractions

### Connection factory

```csharp
public interface IDbConnectionFactory
{
    DbConnection Create();
}
```

Use it in:

- `src/Sms/Data/NotificationRepository.cs`
- `src/Sms/Data/SqlDependencyListener.cs`
- `src/Sms/Checks/DatabaseConnectionCheck.cs`
- Shared/tray connection validation and insertion code

### Change listener

```csharp
public interface ITableChangeListener
{
    Task StartAsync(Func<Task> onChange, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
```

Implementations could use:

| Provider | Mechanism |
|---|---|
| SQL Server | Service Broker / `SqlDependency` |
| PostgreSQL | `LISTEN`/`NOTIFY` plus a trigger |
| MySQL | Bounded polling |
| SQLite | Bounded polling, development only |

### SQL dialect

Move provider-specific statements behind a repository/dialect implementation:

- Batch limiting: `TOP` versus `LIMIT`
- Current UTC timestamp expression
- Identifier qualification
- Identity/auto-increment syntax
- Catalog and table-existence queries
- Retry timestamp parameter handling

## Canonical Columns

Every provider needs equivalents for:

```text
id
phone_number
mpesa_code
adm_no
stud_names
amount
receipt_no
dated
description_json
status
max_retries
retry_count
retry_after
created_at
updated_at
```

Add an index suitable for the eligibility predicate:

```sql
CREATE INDEX idx_sms_notifications_pending
ON sms_notifications(status, retry_after);
```

Exact filtered/partial-index syntax should be provider-specific.

## Configuration

Add a validated provider setting, for example:

```json
{
  "SmsService": {
    "DatabaseProvider": "SqlServer",
    "ConnectionString": "..."
  }
}
```

Register the matching connection factory, dialect/repository, listener, startup
check, and tray editor behavior in `src/Sms/ServiceCollectionExtensions.cs` and
the shared UI support.

## Testing

1. Keep processor unit tests provider-neutral.
2. Add repository integration tests for every provider.
3. Add listener tests for SQL Server and PostgreSQL.
4. Test retry eligibility, cancellation, schema mapping, and concurrent claims.
5. Run providers in CI containers where supported.
6. Add tray tests for provider-specific connection strings and insertion.

There are currently no database integration tests, so provider abstraction
should begin by adding SQL Server coverage before changing behavior.

## Suggested Rollout

1. Add SQL Server connection/listener abstractions without changing behavior.
2. Add deterministic ordering and a database claim strategy.
3. Add SQL Server integration tests.
4. Add PostgreSQL repository and `LISTEN`/`NOTIFY` support.
5. Add polling providers only when there is a concrete deployment requirement.
