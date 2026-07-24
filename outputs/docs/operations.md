# TCPA Compliance API — Operations Guide

This document is for the team responsible for deploying, configuring, and operating the TCPA Compliance system.

---

## Components to deploy

| Component | Type | Runs as |
|-----------|------|---------|
| `TCPA.Api` | ASP.NET Core 8 Web API | IIS / Docker / Windows Service |
| `TCPA.MessageProcessor` | .NET 8 Worker Service | Windows Service / Docker |
| `TCPA.OutboundDispatcher` | .NET 8 Worker Service / Docker |

All three components share a single SQL Server database (`TcpaCompliance`) via `TCPA.Core`.

---

## Configuration reference

All secrets must be supplied as environment variables or via a secrets manager. Values listed as `REPLACE_IN_ENV` in `appsettings.json` will cause startup failures if left as-is.

### TCPA.Api

| Config key | Required | Description | Example |
|------------|----------|-------------|---------|
| `ConnectionStrings:Primary` | Yes | SQL Server write endpoint | `Server=sql01;Database=TcpaCompliance;...` |
| `ConnectionStrings:ReadReplica` | No | Read-replica endpoint; falls back to Primary if absent | Same format as Primary |
| `ApiKeys:ValidKeys` | Yes | Comma-separated list of valid API keys for all upstream callers | `key-biztalk,key-gcma,key-kmi` |
| `ApiKeys:AdminKeys` | Yes | Comma-separated list of admin API keys (help desk only) | `key-helpdesk-prod` |
| `Kafka:BootstrapServers` | Yes | Kafka broker list | `kafka01:9092,kafka02:9092` |
| `Kafka:Topics:Inbound` | Yes | Inbound Kafka topic name | `inbound-messages` |
| `Kafka:Topics:Outbound` | Yes | Outbound Kafka topic name | `outbound-messages` |
| `Logging:PhoneHashKey` | Yes | HMAC-SHA256 key for phone number hashing in logs; minimum 32 characters | (secret — never log or share) |

### TCPA.MessageProcessor

| Config key | Required | Description |
|------------|----------|-------------|
| `ConnectionStrings:Primary` | Yes | SQL Server write endpoint |
| `ConnectionStrings:ReadReplica` | No | Falls back to Primary |
| `Kafka:BootstrapServers` | Yes | Kafka broker list |
| `CoolText:ApiUrl` | Yes | Cool Text API base URL, e.g. `https://api.cooltext.example.com` |
| `CoolText:ApiKey` | Yes | Cool Text API key for sending confirmation SMS |
| `Logging:PhoneHashKey` | Yes | Same key used in TCPA.Api — must match for cross-component log correlation |

**Database config (SystemConfigs table):** The confirmation SMS message body is stored in the database, not in `appsettings.json`. The key is `OptOutMessageBody`. Ensure this row exists before the processor handles its first message, otherwise all confirmation dispatches will log Critical and fail.

### TCPA.OutboundDispatcher

| Config key | Required | Description |
|------------|----------|-------------|
| `ConnectionStrings:Primary` | Yes | SQL Server write endpoint |
| `ConnectionStrings:ReadReplica` | No | Falls back to Primary |
| `Kafka:BootstrapServers` | Yes | Kafka broker list |
| `CoolText:ApiUrl` | Yes | Cool Text API base URL |
| `CoolText:ApiKey` | Yes | Cool Text API key for sending outbound SMS |
| `Logging:PhoneHashKey` | Yes | Same key used across all components |

---

## Database migrations

Run once per environment before starting any component. Must be run from a context with schema-change privileges.

```bash
dotnet ef database update \
  --project src/TCPA.Core \
  --startup-project src/TCPA.Api \
  --connection "Server=<host>;Database=TcpaCompliance;..."
```

Five migrations are included:

| Migration | Creates |
|-----------|---------|
| `20260723195540_CreateOptOutStatus` | `OptOutStatuses` table |
| `20260723200741_CreateAuditLog` | `AuditLogs` table |
| `20260723201745_CreateCoolTextAccount` | `CoolTextAccounts` table |
| `20260723203107_CreateSystemConfig` | `SystemConfigs` table |
| `20260723204812_CreateProcessedMessages` | `ProcessedMessages` table |
| `20260724003024_AddProcessedMessageCompositeUniqueIndex` | Unique index on `ProcessedMessages` |
| `20260724040710_ProcessedMessage_CompositeKey` | Migrates to composite PK `(MessageId, Endpoint)` |

After migrations run, seed the `CoolTextAccounts` and `SystemConfigs` tables with production data. At minimum:

- One `CoolTextAccounts` row per SCG application (`AccountNumber`, `ApplicationId`, `CallbackUrl`, `IsActive = 1`)
- One `SystemConfigs` row: `Key = OptOutMessageBody`, `Value = <confirmation text>`

---

## Health check

**Endpoint:** `GET /api/v1/health`
**Auth:** None required.

| Status | HTTP code | Meaning |
|--------|-----------|---------|
| `healthy` | 200 | Database and Kafka both reachable |
| `degraded` | 503 | One or more dependencies unreachable |

Check both `checks.database` and `checks.kafka` fields in the response body.

Use this endpoint for load balancer probes. A 503 response means the API cannot safely handle requests.

---

## Log events

All components use Serilog with structured logging. Log files rotate daily and are retained for 90 days.

| Log file | Component | Path |
|----------|-----------|------|
| `tcpa-api-YYYY-MM-DD.log` | TCPA.Api | `logs/tcpa-api-.log` |
| `tcpa-processor-YYYY-MM-DD.log` | MessageProcessor | `logs/tcpa-processor-.log` |
| `tcpa-outbound-YYYY-MM-DD.log` | OutboundDispatcher | `logs/tcpa-outbound-.log` |

### Key log event types

The `{EventType}` field in structured logs uses these constants from `LogEventTypes`:

| EventType | Severity | Meaning |
|-----------|----------|---------|
| `OPT_OUT_RECEIVED` | Information | Opt-out keyword detected and status written |
| `MESSAGE_QUEUED` | Information | Inbound or outbound message accepted and queued to Kafka |
| `MESSAGE_SUPPRESSED` | Information | Outbound message suppressed at queue time (opted-out) |
| `CONFIRMATION_SENT` | Information | Opt-out confirmation SMS dispatched successfully |
| `CONFIRMATION_FAILED` | Critical | Confirmation SMS failed after all retries |
| `SLA_BREACH` | Critical | Confirmation sent but exceeded 60-second SLA |
| `AUTH_FAILURE` | Warning | Invalid or missing API key |
| `ADMIN_RE_OPT_IN` | Information | Help desk re-opt-in executed |
| `POTENTIAL_VIOLATION` | Warning | Anomaly detected (e.g. re-opt-in on number with no prior opt-out) |

**Phone numbers are never logged in raw form.** All `{PhoneHash}` values in logs are HMAC-SHA256 hex digests. To correlate a specific number across log entries, compute `HMACSHA256(phoneNumber, Logging:PhoneHashKey)` and search for the hex output.

---

## Common failure modes and diagnosis

### API returns 503 on outbound submission

The opt-out status database is unreachable. The API is operating fail-closed — it is not sending to any recipients. Check `checks.database` in the health endpoint. Verify SQL Server connectivity and connection string.

### Confirmation SMS not being sent

1. Check MessageProcessor logs for `CONFIRMATION_FAILED` or `OptOutMessageBody config missing`.
2. If config is missing: insert a row into `SystemConfigs` with `Key = OptOutMessageBody` and the desired SMS text.
3. If Cool Text API errors: check `CoolText:ApiUrl` and `CoolText:ApiKey`. The processor retries 3 times (2s/4s/8s) before logging Critical.

### SLA_BREACH in MessageProcessor logs

The confirmation SMS was dispatched successfully but took longer than 60 seconds from message receipt. This is an informational alert — no message was lost. Investigate Kafka consumer lag, Cool Text API latency, and SQL Server response times. The `AuditLogs` table will have an `SlaBreach` event with latency details.

### Outbound messages not being delivered

1. Check OutboundDispatcher logs for `Outbound message suppressed`.
   - `reason: opt_out` — recipient is on the opt-out list. This is correct behaviour.
   - `reason: quiet_hours` — current UTC time is outside 08:00–20:59. Messages will dispatch when the window opens.
2. Check for `Outbound send failed after all retries` (Critical). Cool Text API is rejecting or not responding. Check API key and gateway status.
3. Check for poison-pill drain messages (Critical). A message could not be deserialized. Investigate the raw Kafka offset.

### Kafka partition blocked

Both workers implement a poison-pill drain: if a message fails all processing attempts, the offset is committed and a Critical log entry is written so the partition continues. Search logs for `Poison pill: all` to identify affected partitions and offsets. The raw message payload can be retrieved from Kafka for investigation.

### Duplicate messages in audit log

Expected behaviour under Kafka at-least-once delivery. The `ProcessedMessages` table with composite PK `(MessageId, Endpoint)` prevents double-processing. If duplicate audit entries appear, check whether the idempotency record was written before the duplicate Kafka delivery arrived.

---

## Rollback procedure

No application rollback mechanism is built in. Database migration rollback requires running `dotnet ef migrations remove` (development only) or applying a down migration manually. Coordinate with the DBA team before rolling back schema changes in production.

The `ProcessedMessages` table stores idempotency records indefinitely. If the table grows large, archive and truncate records older than the message retention window (coordinate with compliance team before deleting audit data).
