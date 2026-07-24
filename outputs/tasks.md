<!-- SDLC Pipeline Artifact
     Stage: 07-task-breakdown
     Source PRD: inputs/prd.md
     PRD Sections: All
     Generated: 2026-07-23
     Status: APPROVED
-->

# Developer Task Board — TCPA Regulatory Compliance API

## Solution Structure
```
TCPA-Compliance.sln
├── src/
│   ├── TCPA.Core/          # Shared models, interfaces, EF Core DbContext, migrations, domain services
│   ├── TCPA.Api/           # ASP.NET Core 8 Web API (IIS hosted)
│   ├── TCPA.MessageProcessor/   # .NET 8 Worker Service — inbound Kafka consumer
│   ├── TCPA.OutboundDispatcher/ # .NET 8 Worker Service — outbound Kafka consumer
│   └── TCPA.ReportService/      # .NET 8 Worker Service — scheduled reporting
└── tests/
    ├── TCPA.Core.Tests/
    ├── TCPA.Api.Tests/
    ├── TCPA.MessageProcessor.Tests/
    ├── TCPA.OutboundDispatcher.Tests/
    └── TCPA.ReportService.Tests/
```

## Summary
- Total tasks: 64
- Total estimated hours: 152
- Implementation tasks: 45
- Test tasks: 19
- Blocked tasks: 0
- [DECISION-NEEDED] flags: 0

---

## EPIC-001: Opt-Out & Compliance Foundation
_Stories 001–005 — must complete before EPIC-002 and EPIC-003 begin_

---

## STORY-001: Opt-Out Status Store
_Source: EPIC-001 | Priority: Must Have | SPEC-009_

### TASK-001: OptOutStatus schema migration
- **Type:** Data Model
- **Component:** TCPA.Core
- **Description:** Write an EF Core migration creating the `OptOutStatus` table with columns: `Id` (PK, bigint, identity), `PhoneNumber` (varchar(20), not null), `Status` (varchar(20), not null, "opted-in"/"opted-out"), `EffectiveAt` (datetime2, not null, UTC), `AuditRecordId` (bigint, FK to AuditLog). Add unique index on `PhoneNumber`. Add non-clustered index on `PhoneNumber` for fast lookups. Include `CreatedAt` (datetime2) and `UpdatedAt` (datetime2) audit columns.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 2
- **Sequence:** 1
- **Depends On:** none

**Implementation Notes:** Use `HasIndex(e => e.PhoneNumber).IsUnique()` in EF Core Fluent API. Grant only SELECT/INSERT/UPDATE to the application DB user — no DELETE. Coordinate migration review with DBA team before merging.

**Definition of Done:**
- [ ] Migration file created and runs cleanly against local dev SQL Server
- [ ] Unique constraint and performance index confirmed in SQL Server Management Studio
- [ ] DBA review approved

---

### TASK-002: OptOutStatus domain model and repository interface
- **Type:** Business Logic
- **Component:** TCPA.Core
- **Description:** Create `OptOutStatus` entity class in `TCPA.Core/Models/`. Create `IOptOutStatusRepository` interface in `TCPA.Core/Interfaces/` with methods: `GetStatusAsync(string phoneNumber)` (returns current status or opted-in default), `UpsertOptOutAsync(string phoneNumber, long auditRecordId, DateTime effectiveAt)` (upsert semantics), `SetOptedInAsync(string phoneNumber, long auditRecordId, DateTime effectiveAt)`. All methods take `CancellationToken`.
- **Satisfies AC:** AC-001, AC-002, AC-003
- **Estimated Hours:** 2
- **Sequence:** 2
- **Depends On:** TASK-001

**Implementation Notes:** `GetStatusAsync` must return `"opted-in"` (not null) when no record exists — this implements ASM-002 (opt-in by default). Interface lives in TCPA.Core so all 3 Worker Services can reference it without circular dependencies.

**Definition of Done:**
- [ ] Entity class with data annotations matching migration schema
- [ ] Interface with XML documentation on each method

---

### TASK-003: OptOutStatus SQL Server repository implementation
- **Type:** Business Logic
- **Component:** TCPA.Core
- **Description:** Implement `SqlOptOutStatusRepository : IOptOutStatusRepository` in `TCPA.Core/Repositories/`. Use EF Core `TcpaDbContext`. Implement upsert using `ExecuteSqlRawAsync` with `MERGE` statement to handle concurrent writes atomically. Route read calls to the read replica connection string (`TcpaDbContextReadOnly`).
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 3
- **Sequence:** 3
- **Depends On:** TASK-002

**Implementation Notes:** Read replica routing: inject two `TcpaDbContext` instances — `TcpaDbContext` (primary, for writes) and `TcpaDbContextReadOnly` (read replica, for status reads). Register both via DI with separate connection strings from `IConfiguration`. P99 < 100ms target means no lazy loading — all status reads are direct indexed lookups.

**Definition of Done:**
- [ ] MERGE upsert handles concurrent inserts without duplicate key exceptions
- [ ] Read calls go to read replica connection string
- [ ] DI registration documented in README

---

### TASK-004: Unit and integration tests — OptOutStatus repository
- **Type:** Test
- **Component:** TCPA.Core.Tests
- **Description:** Write integration tests using a real SQL Server (Testcontainers.MsSql) for the repository. Test: default opted-in for unknown number, upsert creates record, upsert on existing updates without duplicate, concurrent upsert resolves cleanly, read replica path returns correct status.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 3
- **Sequence:** 4
- **Depends On:** TASK-003

**Test Cases to Cover:**
- [ ] `GetStatusAsync` returns "opted-in" for unknown phone number
- [ ] `UpsertOptOutAsync` writes "opted-out" with correct effectiveAt
- [ ] Second upsert on same number does not throw; updates effectiveAt
- [ ] `SetOptedInAsync` updates status back to "opted-in"
- [ ] P99 read latency assertion (informational — log if > 100ms)

**Definition of Done:**
- [ ] All test cases pass against Testcontainers SQL Server
- [ ] Tests clean up the database after each class run

---

## STORY-002: Audit Logging Infrastructure
_Source: EPIC-001 | Priority: Must Have | SPEC-010_

### TASK-005: AuditLog schema migration
- **Type:** Data Model
- **Component:** TCPA.Core
- **Description:** Write an EF Core migration creating the `AuditLog` table: `Id` (PK, bigint, identity), `EventType` (varchar(50), not null), `PhoneNumber` (varchar(20), not null), `OccurredAt` (datetime2, not null, UTC), `ApplicationId` (varchar(50), nullable), `MessageId` (varchar(100), nullable), `AgentId` (varchar(100), nullable), `Details` (nvarchar(max), nullable — JSON payload), `AnomalyFlag` (bit, default 0). Index on `PhoneNumber, OccurredAt`. Deny DELETE on this table at the database level (script in migration `Up()`).
- **Satisfies AC:** AC-001, AC-002, AC-004
- **Estimated Hours:** 2
- **Sequence:** 1
- **Depends On:** none

**Implementation Notes:** The `DENY DELETE` SQL must be in the migration `Up()` method via `migrationBuilder.Sql("DENY DELETE ON dbo.AuditLog TO [tcpa_app_user]")`. Confirm the application DB username with DBA. 5-year retention enforced by partition archiving (DBA responsibility) not by this migration.

**Definition of Done:**
- [ ] Migration runs cleanly
- [ ] DELETE denied for application user — verified by attempting a DELETE in SSMS under app credentials
- [ ] DBA review approved

---

### TASK-006: AuditLog domain model, event types enum, and repository interface
- **Type:** Business Logic
- **Component:** TCPA.Core
- **Description:** Create `AuditLog` entity in `TCPA.Core/Models/`. Create `AuditEventType` enum in `TCPA.Core/Models/`: `OPT_OUT_WRITTEN`, `OPT_OUT_DUPLICATE`, `CONFIRMATION_DISPATCHED`, `CONFIRMATION_FAILED`, `SLA_BREACH`, `MESSAGE_SUPPRESSED_QUEUE_TIME`, `MESSAGE_SUPPRESSED_SEND_TIME`, `RACE_CONDITION_EDGE_CASE`, `POTENTIAL_VIOLATION`, `RE_OPT_IN`, `REPLY_FORWARDED`, `REPLY_FORWARD_FAILED`. Create `IAuditLogRepository` interface with: `WriteAsync(AuditLog entry, CancellationToken ct)`, `QueryByPhoneNumberAsync(string phoneNumber, DateTime from, DateTime to, CancellationToken ct)`.
- **Satisfies AC:** AC-001, AC-002
- **Estimated Hours:** 2
- **Sequence:** 2
- **Depends On:** TASK-005

**Definition of Done:**
- [ ] All event types in enum match SPEC-010 event taxonomy
- [ ] XML doc on each enum value explains when it is written

---

### TASK-007: AuditLog SQL Server repository implementation
- **Type:** Business Logic
- **Component:** TCPA.Core
- **Description:** Implement `SqlAuditLogRepository : IAuditLogRepository` in `TCPA.Core/Repositories/`. `WriteAsync` uses the primary `TcpaDbContext`. Must be called within the same `DbContext` transaction as the triggering status write so both commit or roll back atomically. `QueryByPhoneNumberAsync` uses the read replica context.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 3
- **Sequence:** 3
- **Depends On:** TASK-006

**Implementation Notes:** Do not manage the transaction inside the repository. The calling service (OptOutProcessingService, ReOptInService) opens the transaction and passes the `DbContext` with the open transaction. Repository just calls `SaveChangesAsync` — the transaction boundary is the service's responsibility. This keeps the repository testable independently.

**Definition of Done:**
- [ ] `WriteAsync` participates in ambient EF Core transaction
- [ ] `QueryByPhoneNumberAsync` returns results sorted by OccurredAt ascending
- [ ] No DELETE call path anywhere in implementation

---

### TASK-008: Unit and integration tests — AuditLog repository
- **Type:** Test
- **Component:** TCPA.Core.Tests
- **Description:** Test: write audit record with all event types, query returns records for phone number in date range sorted correctly, atomic rollback — both status write and audit write roll back together, DELETE attempt throws (verify constraint).
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 3
- **Sequence:** 4
- **Depends On:** TASK-007

**Test Cases to Cover:**
- [ ] `WriteAsync` persists record with all fields populated
- [ ] `QueryByPhoneNumberAsync` returns correct records for date range
- [ ] Records outside date range excluded from query results
- [ ] Transaction rollback removes both status record and audit record
- [ ] Attempting DELETE throws a SQL exception (enforcing no-delete constraint)

**Definition of Done:**
- [ ] All tests pass against Testcontainers SQL Server
- [ ] Rollback test proves atomic write

---

## STORY-003: Cool Text Account Registry
_Source: EPIC-001 | Priority: Must Have | SPEC-015 | [HIGH-RISK]_

### TASK-009: CoolTextAccount schema migration and seed data
- **Type:** Data Model
- **Component:** TCPA.Core
- **Description:** Write EF Core migration creating `CoolTextAccount` table: `Id` (int, PK, identity), `AccountNumber` (varchar(50), not null, unique), `ApplicationId` (varchar(50), not null), `ApplicationName` (varchar(100), not null), `CallbackUrl` (varchar(500), not null), `IsActive` (bit, not null, default 1), `CreatedAt` (datetime2), `UpdatedAt` (datetime2). Unique constraint on `AccountNumber`. Include seed `InsertData()` calls for all 4 initial Gas applications: BizTalk, GCMA, KMI, ARM/Construction Portal (use placeholder account numbers until IT provides real values — mark with a comment).
- **Satisfies AC:** AC-002, AC-004
- **Estimated Hours:** 2
- **Sequence:** 1
- **Depends On:** none

**Implementation Notes:** Real Cool Text account numbers and callback URLs to be provided by IT/integration teams (RISK-003 onboarding kickoff). Seed data uses placeholder values; update before go-live. Flag as [PENDING: Cool Text account numbers from IT] in the migration comment.

**Definition of Done:**
- [ ] Migration runs, table created, unique constraint enforced
- [ ] Seed data for all 4 applications inserted
- [ ] DBA review approved

---

### TASK-010: CoolTextAccount domain model and repository
- **Type:** Business Logic
- **Component:** TCPA.Core
- **Description:** Create `CoolTextAccount` entity in `TCPA.Core/Models/`. Create `ICoolTextAccountRepository` interface with: `GetByAccountNumberAsync(string accountNumber)` returns `CoolTextAccount` or null. Implement `SqlCoolTextAccountRepository` using read replica context (accounts are read-only at runtime).
- **Satisfies AC:** AC-001, AC-003, AC-004
- **Estimated Hours:** 2
- **Sequence:** 2
- **Depends On:** TASK-009

**Definition of Done:**
- [ ] Repository reads from read replica
- [ ] Returns null (not throws) for unregistered account number
- [ ] Callers handle null and return 400

---

### TASK-011: Unit tests — CoolTextAccount repository
- **Type:** Test
- **Component:** TCPA.Core.Tests
- **Description:** Test: registered account returns entity, unregistered account returns null, unique constraint prevents duplicate account number.
- **Satisfies AC:** AC-001, AC-002, AC-003
- **Estimated Hours:** 2
- **Sequence:** 3
- **Depends On:** TASK-010

**Test Cases to Cover:**
- [ ] Registered account number returns correct entity with all fields
- [ ] Unregistered account number returns null
- [ ] Inserting duplicate account number throws unique constraint violation

**Definition of Done:**
- [ ] All tests pass against Testcontainers SQL Server

---

## STORY-004: System Configuration Store
_Source: EPIC-001 | Priority: Must Have | SPEC-016_

### TASK-012: SystemConfig schema migration and seed data
- **Type:** Data Model
- **Component:** TCPA.Core
- **Description:** Write EF Core migration creating `SystemConfig` table: `Key` (varchar(100), PK), `Value` (nvarchar(max), not null), `Description` (nvarchar(500), nullable), `UpdatedAt` (datetime2). Seed initial rows: `OptOutMessageBody` (placeholder text), `OptedInReportRecipients` (empty JSON array), `OptedOutReportRecipients` (empty JSON array), `ComplianceReportRecipients` (empty JSON array), `ReportScheduleCron` ("0 6 * * 1" — Monday 06:00), `AdminRateLimitPerMinute` ("10").
- **Satisfies AC:** AC-001, AC-002, AC-003
- **Estimated Hours:** 2
- **Sequence:** 1
- **Depends On:** none

**Definition of Done:**
- [ ] Migration runs, all seed keys present
- [ ] DBA review approved

---

### TASK-013: SystemConfig domain model and repository
- **Type:** Business Logic
- **Component:** TCPA.Core
- **Description:** Create `SystemConfig` entity. Create `ISystemConfigRepository` with: `GetValueAsync(string key, CancellationToken ct)` (returns string or null), `GetRequiredValueAsync(string key, CancellationToken ct)` (throws `ConfigurationException` if missing). Implement `SqlSystemConfigRepository` using read replica. No caching — read at call time so config updates take effect immediately without restart.
- **Satisfies AC:** AC-001, AC-002, AC-003
- **Estimated Hours:** 2
- **Sequence:** 2
- **Depends On:** TASK-012

**Implementation Notes:** `GetRequiredValueAsync` throwing on missing key is intentional — callers like `ConfirmationDispatchService` must fail loudly rather than send blank confirmation messages. Log the missing key with a production-level alert before throwing.

**Definition of Done:**
- [ ] `GetRequiredValueAsync` throws `ConfigurationException` with the key name in the message
- [ ] No caching layer — database read every call

---

### TASK-014: Unit tests — SystemConfig repository
- **Type:** Test
- **Component:** TCPA.Core.Tests
- **Description:** Test: existing key returns value, missing key returns null from GetValue / throws from GetRequired, updated value reflects on next read.
- **Satisfies AC:** AC-001, AC-002, AC-003
- **Estimated Hours:** 2
- **Sequence:** 3
- **Depends On:** TASK-013

**Test Cases to Cover:**
- [ ] `GetValueAsync` returns value for existing key
- [ ] `GetValueAsync` returns null for missing key
- [ ] `GetRequiredValueAsync` throws `ConfigurationException` for missing key
- [ ] After updating value in DB, next `GetValueAsync` returns new value

**Definition of Done:**
- [ ] All tests pass against Testcontainers SQL Server

---

## STORY-005: Structured Production and Debug Logging
_Source: EPIC-001 | Priority: Must Have | SPEC-017_

### TASK-015: Serilog setup and phone number hashing service
- **Type:** Config
- **Component:** TCPA.Core
- **Description:** Add Serilog (with Serilog.AspNetCore and Serilog.Sinks.File) to all 5 projects. Create `PhoneNumberHasher` service in `TCPA.Core/Services/` using SHA-256 + a per-environment HMAC key from `IConfiguration`. Create `LogEventType` enum matching the production log event taxonomy. Create extension methods `LogOptOutEvent(...)`, `LogSuppressionEvent(...)`, `LogAuthFailure(...)`, etc. that emit structured log entries with hashed phone numbers at production level.
- **Satisfies AC:** AC-001, AC-003
- **Estimated Hours:** 3
- **Sequence:** 1
- **Depends On:** none

**Implementation Notes:** HMAC key for phone number hashing must come from configuration (environment variable or secrets management), never hardcoded. Debug level gating: check `ISystemConfigRepository.GetValueAsync("DebugLoggingEnabled")` at startup; if not "true", set minimum level to Information. Worker Service projects configure Serilog in `Program.cs` `CreateHostBuilder`.

**Definition of Done:**
- [ ] Production log writes structured JSON with hashed phone number
- [ ] Debug logging defaults to disabled in production configuration
- [ ] HMAC key read from configuration

---

### TASK-016: Unit tests — PhoneNumberHasher and log event structure
- **Type:** Test
- **Component:** TCPA.Core.Tests
- **Description:** Test: same phone number + same key = same hash (deterministic), different keys produce different hashes, hash does not contain original phone number digits, debug mode gating works.
- **Satisfies AC:** AC-001, AC-002, AC-003
- **Estimated Hours:** 2
- **Sequence:** 2
- **Depends On:** TASK-015

**Test Cases to Cover:**
- [ ] Hash is deterministic for same input + key
- [ ] Different HMAC keys produce different hashes for same phone number
- [ ] Production log output does not contain raw phone number string
- [ ] Debug level log not written when debug logging disabled

**Definition of Done:**
- [ ] All test cases pass
- [ ] No test writes actual credentials or raw PII to test output

---

---

## EPIC-002: Inbound Message Processing

---

## STORY-006: Inbound Webhook Endpoint
_Source: EPIC-002 | Priority: Must Have | SPEC-001 | [BLOCKED-BY: STORY-001, STORY-003]_

### TASK-017: API key authentication middleware
- **Type:** Business Logic
- **Component:** TCPA.Api
- **Description:** Create `ApiKeyAuthMiddleware` in `TCPA.Api/Middleware/`. Read `X-Api-Key` header from each request. Validate against the auth service (external HTTP call or shared config — confirm with auth service team which pattern is used). Return HTTP 401 immediately on invalid/missing key. Log authentication failure at production level (without the key value). Register middleware in `Program.cs` before routing.
- **Satisfies AC:** AC-003 (STORY-006), AC-005 (STORY-015)
- **Estimated Hours:** 2
- **Sequence:** 1
- **Depends On:** none

**Implementation Notes:** The middleware must apply to all routes except `/api/v1/health` (health check must be unauthenticated for NLB probe). Use `IEndpointRouteBuilder` attribute or route filtering to exclude health. Auth service call must be timeout-protected (500ms timeout) — auth failure = 401, auth service timeout = 503.

**Definition of Done:**
- [ ] Missing X-Api-Key returns 401
- [ ] Invalid key returns 401
- [ ] Health endpoint bypasses auth check
- [ ] Auth failure written to production log

---

### TASK-018: POST /webhook/inbound controller and request validation
- **Type:** API
- **Component:** TCPA.Api
- **Description:** Create `WebhookController` in `TCPA.Api/Controllers/`. Implement `POST /webhook/inbound` action. Validate required fields: `from` (E.164 format), `to` (registered Cool Text account number), `body` (string, max 1600 chars), `messageId` (string, required). Check `ICoolTextAccountRepository.GetByAccountNumberAsync(to)` — return 400 if unregistered. Check idempotency: query a Redis or DB-backed idempotency store keyed on `messageId` — return 200 with original response if already processed. Return 200 `{ status: "received", internalId: guid }` on success.
- **Satisfies AC:** AC-001, AC-002, AC-004, AC-005
- **Estimated Hours:** 3
- **Sequence:** 2
- **Depends On:** TASK-017, TASK-010

**Implementation Notes:** Idempotency store: use a `ProcessedMessages` table in SQL Server (messageId varchar PK, processedAt datetime2, internalId uniqueidentifier). TTL of 7 days is sufficient. Add migration for this table as part of this task. Do not use distributed cache for idempotency — SQL Server provides durability across restarts.

**Definition of Done:**
- [ ] All 5 ACs validated by integration tests
- [ ] OpenAPI annotations on controller action (Swashbuckle attributes)

---

### TASK-019: Kafka producer — publish to inbound-messages topic
- **Type:** Business Logic
- **Component:** TCPA.Api
- **Description:** Create `IKafkaInboundProducer` interface and `ConfluentKafkaInboundProducer` implementation in `TCPA.Api/Kafka/`. Produce `InboundMessageEvent` (JSON serialised) to `inbound-messages` topic. Partition key: `from` phone number (ensures ordering per phone number). Handle `ProduceException` — log at production level and return HTTP 500 (the webhook controller must return 500 if Kafka publish fails, so Cool Text retries).
- **Satisfies AC:** AC-001
- **Estimated Hours:** 2
- **Sequence:** 3
- **Depends On:** TASK-018

**Implementation Notes:** Use `Confluent.Kafka` NuGet. Producer is a singleton registered via DI. Use `acks=all` for durability. Topic name from `IConfiguration["Kafka:InboundTopic"]`.

**Definition of Done:**
- [ ] Kafka produce failure causes HTTP 500 response (Cool Text will retry)
- [ ] Partition key is the `from` phone number

---

### TASK-020: Integration tests — webhook endpoint
- **Type:** Test
- **Component:** TCPA.Api.Tests
- **Description:** Use `WebApplicationFactory<Program>` for in-process testing. Mock `IKafkaInboundProducer` and `ICoolTextAccountRepository`. Test all 5 ACs: valid request → 200 + internalId, duplicate messageId → 200 (idempotent), missing API key → 401, unregistered `to` account → 400, missing required field → 400.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004, AC-005
- **Estimated Hours:** 3
- **Sequence:** 4
- **Depends On:** TASK-019

**Test Cases to Cover:**
- [ ] Valid request returns 200 with internalId
- [ ] Duplicate messageId returns 200 with same internalId (no second Kafka produce)
- [ ] Missing X-Api-Key returns 401
- [ ] Unregistered `to` account returns 400 with descriptive message
- [ ] Missing `from` field returns 400 with field-level error

**Definition of Done:**
- [ ] All 5 tests pass
- [ ] Kafka mock verifies produce called exactly once per unique messageId

---

## STORY-007: Opt-Out Keyword Detection
_Source: EPIC-002 | Priority: Must Have | SPEC-002 | [BLOCKED-BY: STORY-006]_

### TASK-021: KeywordDetectionService
- **Type:** Business Logic
- **Component:** TCPA.Core
- **Description:** Create `IKeywordDetectionService` interface and `KeywordDetectionService` implementation in `TCPA.Core/Services/`. Method: `Detect(string messageBody) → KeywordDetectionResult { IsOptOut, MatchedKeyword }`. Logic: trim whitespace from body; compare trimmed result case-insensitively (OrdinalIgnoreCase) against exactly: STOP, QUIT, END, REVOKE, OPT-OUT, CANCEL, UNSUBSCRIBE. No substring matching — full equality only. Null/empty body → IsOptOut = false.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004, AC-005
- **Estimated Hours:** 2
- **Sequence:** 1
- **Depends On:** none (no storage dependency)

**Definition of Done:**
- [ ] All 7 keywords detected case-insensitively
- [ ] "Please STOP texting" → IsOptOut = false
- [ ] "STOPNOW" → IsOptOut = false
- [ ] "opt out" (space not hyphen) → IsOptOut = false

---

### TASK-022: Unit tests — KeywordDetectionService
- **Type:** Test
- **Component:** TCPA.Core.Tests
- **Description:** Exhaustive tests for all keyword variants and non-matching cases from SPEC-002 edge case table.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004, AC-005
- **Estimated Hours:** 3
- **Sequence:** 2
- **Depends On:** TASK-021

**Test Cases to Cover:**
- [ ] "STOP" → IsOptOut = true
- [ ] "stop" → IsOptOut = true (case insensitive)
- [ ] "  STOP  " → IsOptOut = true (trimmed)
- [ ] "QUIT", "END", "REVOKE", "OPT-OUT", "CANCEL", "UNSUBSCRIBE" → all IsOptOut = true
- [ ] "Please STOP" → IsOptOut = false (not exact match)
- [ ] "STOPNOW" → IsOptOut = false
- [ ] "opt out" → IsOptOut = false (hyphen required)
- [ ] "" (empty) → IsOptOut = false
- [ ] null → IsOptOut = false

**Definition of Done:**
- [ ] All test cases pass, including all 9 above plus any additional edge cases from SPEC-002

---

## STORY-008: Opt-Out Status Write
_Source: EPIC-002 | Priority: Must Have | SPEC-003 | [BLOCKED-BY: STORY-007, STORY-002]_

### TASK-023: OptOutProcessingService — atomic opt-out write
- **Type:** Business Logic
- **Component:** TCPA.MessageProcessor
- **Description:** Create `IOptOutProcessingService` interface and `OptOutProcessingService` in `TCPA.MessageProcessor/Services/`. Method: `ProcessOptOutAsync(InboundMessageEvent event, CancellationToken ct)`. Steps: (1) Begin EF Core transaction on primary DbContext. (2) Write AuditLog entry with event type OPT_OUT_WRITTEN (or OPT_OUT_DUPLICATE if already opted-out). (3) Upsert OptOutStatus via `IOptOutStatusRepository`. (4) Commit transaction. (5) Return `OptOutResult { IsNew, AuditRecordId }` for downstream confirmation dispatch. On DB failure: rollback, log production alert, do not trigger confirmation.
- **Satisfies AC:** AC-001, AC-002, AC-003
- **Estimated Hours:** 3
- **Sequence:** 1
- **Depends On:** TASK-003, TASK-007

**Implementation Notes:** The `AuditLog.AuditRecordId` FK on `OptOutStatus` must be written as part of the same transaction. Write the AuditLog entry first (to get the ID), then write/update OptOutStatus with that FK. This ensures every status record has a traceable audit entry.

**Definition of Done:**
- [ ] Transaction rolls back both writes on any failure
- [ ] OPT_OUT_DUPLICATE event type used on repeat opt-outs
- [ ] Confirmation is not triggered if DB write fails

---

### TASK-024: Unit tests — OptOutProcessingService
- **Type:** Test
- **Component:** TCPA.MessageProcessor.Tests
- **Description:** Test with in-memory EF Core DbContext for unit tests. Integration test with Testcontainers for atomicity.
- **Satisfies AC:** AC-001, AC-002, AC-003
- **Estimated Hours:** 3
- **Sequence:** 2
- **Depends On:** TASK-023

**Test Cases to Cover:**
- [ ] First opt-out writes OPT_OUT_WRITTEN audit record and opted-out status
- [ ] Second opt-out writes OPT_OUT_DUPLICATE audit record; status remains opted-out
- [ ] DB failure causes full rollback — neither audit nor status record persisted
- [ ] `IsNew = true` returned for first opt-out; `IsNew = false` for duplicate

**Definition of Done:**
- [ ] Unit tests use EF Core InMemory provider
- [ ] Integration test uses Testcontainers to verify rollback atomicity

---

## STORY-009: Opt-Out Confirmation Dispatch
_Source: EPIC-002 | Priority: Must Have | SPEC-004 | [HIGH-RISK] [BLOCKED-BY: STORY-008, STORY-004]_

### TASK-025: ConfirmationDispatchService — read config, call Cool Text, retry, SLA measurement
- **Type:** Business Logic
- **Component:** TCPA.MessageProcessor
- **Description:** Create `IConfirmationDispatchService` and `ConfirmationDispatchService` in `TCPA.MessageProcessor/Services/`. Method: `DispatchConfirmationAsync(string phoneNumber, string coolTextAccountNumber, DateTime receivedAt, long auditRecordId, CancellationToken ct)`. Steps: (1) Read `OptOutMessageBody` from `ISystemConfigRepository` — if missing/empty, write CONFIRMATION_FAILED audit record and alert; return. (2) Call Cool Text / Twilio send API with the configured message body. (3) Retry up to 3 times on failure with exponential backoff (2s, 4s, 8s). (4) Calculate latency: dispatchedAt - receivedAt. (5) Write CONFIRMATION_DISPATCHED audit record. (6) If latency > 60s, also write SLA_BREACH audit record and emit production alert. (7) On all retries failed: write CONFIRMATION_FAILED audit record and alert.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004, AC-005
- **Estimated Hours:** 4
- **Sequence:** 1
- **Depends On:** TASK-013, TASK-007

**Implementation Notes:** SLA measurement must use `receivedAt` from the original inbound webhook event (carried through the Kafka message payload) — not `DateTime.UtcNow` at service startup. Cool Text send API client is injected as `ICoolTextApiClient` (interface defined in TCPA.Core for mockability). Retry policy: use Polly `RetryPolicy` with exponential backoff. CancellationToken propagated through all async calls.

**Definition of Done:**
- [ ] Missing config body halts dispatch with CONFIRMATION_FAILED audit record
- [ ] 3 retries with exponential backoff before CONFIRMATION_FAILED
- [ ] SLA_BREACH audit record written if dispatchedAt > receivedAt + 60s
- [ ] SLA breach produces production-level log alert

---

### TASK-026: Unit tests — ConfirmationDispatchService
- **Type:** Test
- **Component:** TCPA.MessageProcessor.Tests
- **Description:** Mock `ISystemConfigRepository`, `ICoolTextApiClient`, `IAuditLogRepository`. Test all AC scenarios including retry behaviour and SLA breach.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004, AC-005
- **Estimated Hours:** 4
- **Sequence:** 2
- **Depends On:** TASK-025

**Test Cases to Cover:**
- [ ] Happy path: config present, Cool Text call succeeds, CONFIRMATION_DISPATCHED audit written
- [ ] Config body missing: CONFIRMATION_FAILED written, Cool Text not called
- [ ] Cool Text fails once: retry succeeds on second attempt
- [ ] Cool Text fails 3 times: CONFIRMATION_FAILED written after 3rd failure
- [ ] dispatchedAt - receivedAt > 60s: SLA_BREACH audit written AND dispatch still completes
- [ ] Updated config value used without restart

**Definition of Done:**
- [ ] All 6 scenarios tested and passing
- [ ] Retry test verifies exactly 3 Cool Text API calls before giving up

---

## STORY-010: General Reply Forwarding
_Source: EPIC-002 | Priority: Must Have | SPEC-005 | [BLOCKED-BY: STORY-006, STORY-003]_

### TASK-027: ReplyForwardingService
- **Type:** Business Logic
- **Component:** TCPA.MessageProcessor
- **Description:** Create `IReplyForwardingService` and `ReplyForwardingService` in `TCPA.MessageProcessor/Services/`. Method: `ForwardReplyAsync(InboundMessageEvent event, string callbackUrl, CancellationToken ct)`. POST the original message body and metadata to `callbackUrl` using `HttpClient` (timeout 10s). On non-2xx or timeout: log REPLY_FORWARD_FAILED at production level with applicationId, messageId, HTTP status. No retry (best-effort per SPEC-005 BR-017).
- **Satisfies AC:** AC-001, AC-002
- **Estimated Hours:** 2
- **Sequence:** 1
- **Depends On:** TASK-010

**Definition of Done:**
- [ ] Original `body` field forwarded unchanged
- [ ] Timeout fires after 10s (not longer)
- [ ] Forward failure logged but does not propagate exception to worker

---

### TASK-028: InboundMessageWorker — Kafka consumer routing keyword vs. reply
- **Type:** Business Logic
- **Component:** TCPA.MessageProcessor
- **Description:** Create `InboundMessageWorker : BackgroundService` in `TCPA.MessageProcessor/Workers/`. Consume `inbound-messages` Kafka topic (consumer group: `tcpa-inbound-processor`). For each message: (1) Deserialize `InboundMessageEvent`. (2) Run `IKeywordDetectionService.Detect(body)`. (3) If IsOptOut → call `IOptOutProcessingService.ProcessOptOutAsync()`, then `IConfirmationDispatchService.DispatchConfirmationAsync()`. (4) If not IsOptOut → lookup callback URL via `ICoolTextAccountRepository`, call `IReplyForwardingService.ForwardReplyAsync()`. (5) Commit Kafka offset after processing. Handle per-message exceptions by logging and committing offset (poison pill handling — do not block the partition).
- **Satisfies AC:** AC-001, AC-002, AC-003 (STORY-010), AC-001, AC-002 (STORY-007 routing)
- **Estimated Hours:** 3
- **Sequence:** 2
- **Depends On:** TASK-021, TASK-023, TASK-025, TASK-027

**Implementation Notes:** Poison pill: if a message throws an unhandled exception after 2 processing attempts, write a production alert and commit the offset to avoid blocking the partition. Log the raw message payload at debug level for diagnosis.

**Definition of Done:**
- [ ] Keyword messages → opt-out path
- [ ] Non-keyword messages → forward path
- [ ] Kafka offset committed after each message (success or poison pill)
- [ ] Worker starts and stops cleanly with `CancellationToken` from host

---

### TASK-029: Unit tests — InboundMessageWorker and ReplyForwardingService
- **Type:** Test
- **Component:** TCPA.MessageProcessor.Tests
- **Description:** Unit test worker routing logic with mocked services. Integration test forwarding with Testcontainers Kafka + a mock HTTP server.
- **Satisfies AC:** AC-001, AC-002, AC-003
- **Estimated Hours:** 3
- **Sequence:** 3
- **Depends On:** TASK-028

**Test Cases to Cover:**
- [ ] Keyword message → OptOutProcessingService called; ReplyForwardingService NOT called
- [ ] Non-keyword message → ReplyForwardingService called; OptOutProcessingService NOT called
- [ ] Callback returns 500 → REPLY_FORWARD_FAILED logged; offset committed; no exception thrown
- [ ] Callback times out → same handling as above
- [ ] Opt-out detection anomaly guard (AC-003): keyword reaching forward path raises alert

**Definition of Done:**
- [ ] All test cases pass
- [ ] Kafka consumer group tested with Testcontainers embedded Kafka

---

## EPIC-003: Outbound Message Gateway

---

## STORY-011: Outbound Message Submission API
_Source: EPIC-003 | Priority: Must Have | SPEC-006 (core) | [HIGH-RISK] [BLOCKED-BY: STORY-013]_

### TASK-030: POST /api/v1/messages/outbound controller
- **Type:** API
- **Component:** TCPA.Api
- **Description:** Create `MessagesController` in `TCPA.Api/Controllers/`. Implement `POST /api/v1/messages/outbound`. Validate request: `destinationNumber` (E.164), `coolTextAccountNumber` (registered), `messageBody` (1–1600 chars), `correlationId` (required, UUID). Run queue-time opt-out check via `IQueueTimeOptOutCheckService`. If suppressed, return `{ status: "suppressed", suppressionReason: "opted-out", messageId: guid }`. If opted-in, publish to `outbound-messages` Kafka topic and return `{ status: "queued", messageId: guid }`. Idempotency on `correlationId` same as webhook (ProcessedMessages table).
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004, AC-005
- **Estimated Hours:** 3
- **Sequence:** 1
- **Depends On:** TASK-017, TASK-044 (STORY-013 queue-time check)

**Implementation Notes:** OpenAPI annotations required. The `correlationId` from the caller becomes the `messageId` returned. Gas application teams use this `messageId` for their own message tracking.

**Definition of Done:**
- [ ] Opted-out destination returns suppressed response (not an error)
- [ ] Kafka produce failure returns 500 (Gas app retries)
- [ ] OpenAPI spec generated correctly by Swashbuckle

---

### TASK-031: Kafka producer — outbound-messages topic
- **Type:** Business Logic
- **Component:** TCPA.Api
- **Description:** Create `IKafkaOutboundProducer` interface and `ConfluentKafkaOutboundProducer` in `TCPA.Api/Kafka/`. Produce `OutboundMessageEvent` (JSON) to `outbound-messages` topic. Partition key: `destinationNumber`. Include in event: messageId, destinationNumber, coolTextAccountNumber, messageBody, applicationId (from API key lookup), queuedAt (UTC), correlationId.
- **Satisfies AC:** AC-001
- **Estimated Hours:** 2
- **Sequence:** 2
- **Depends On:** TASK-030

**Definition of Done:**
- [ ] `queuedAt` timestamp written to event (critical for race-condition classification in STORY-014)
- [ ] `applicationId` resolved from validated API key

---

### TASK-032: Integration tests — outbound submission endpoint
- **Type:** Test
- **Component:** TCPA.Api.Tests
- **Description:** Use `WebApplicationFactory`. Mock `IQueueTimeOptOutCheckService` and `IKafkaOutboundProducer`. Test all 5 ACs.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004, AC-005
- **Estimated Hours:** 3
- **Sequence:** 3
- **Depends On:** TASK-031

**Test Cases to Cover:**
- [ ] Opted-in number → 200 with status "queued", Kafka produce called
- [ ] Opted-out number → 200 with status "suppressed", Kafka produce NOT called
- [ ] Duplicate correlationId → 200 with same messageId, Kafka produce NOT called again
- [ ] Unregistered Cool Text account → 400
- [ ] Missing destinationNumber → 400 with field-level error
- [ ] Kafka produce throws → 500 returned to caller

**Definition of Done:**
- [ ] All 6 tests pass

---

## STORY-012: Fail-Safe Resilience
_Source: EPIC-003 | Priority: Must Have | SPEC-006 (HA) | [HIGH-RISK] [BLOCKED-BY: STORY-011]_

### TASK-033: GET /api/v1/health endpoint with dependency checks
- **Type:** API
- **Component:** TCPA.Api
- **Description:** Create `HealthController` in `TCPA.Api/Controllers/`. Implement `GET /api/v1/health`. Run three dependency checks in parallel: (1) SQL Server primary reachable (simple `SELECT 1` query with 2s timeout). (2) Kafka broker reachable (metadata request with 2s timeout). (3) Auth service reachable (HTTP GET health endpoint with 2s timeout). If all pass: return 200 `{ status: "healthy", checks: [...] }`. If any fail: return 503 `{ status: "unhealthy", checks: [...] }`. Health endpoint bypasses API key auth middleware (TASK-017).
- **Satisfies AC:** AC-001, AC-002, AC-004
- **Estimated Hours:** 3
- **Sequence:** 1
- **Depends On:** TASK-017

**Implementation Notes:** Use ASP.NET Core Health Checks framework (`AddHealthChecks()` in `Program.cs`). Register custom `SqlServerHealthCheck`, `KafkaHealthCheck`, and `AuthServiceHealthCheck`. This integrates with the NLB health probe — the NLB hits `/api/v1/health` every 5s and removes the node on 503.

**Definition of Done:**
- [ ] Returns 503 if any single dependency is unhealthy
- [ ] All 3 checks run in parallel (not sequential — total timeout ≤ 2s)
- [ ] Response body lists each dependency's status individually

---

### TASK-034: Integration tests — health endpoint
- **Type:** Test
- **Component:** TCPA.Api.Tests
- **Description:** Test all healthy, partial failure (one dependency down), and full failure scenarios.
- **Satisfies AC:** AC-001, AC-002, AC-004
- **Estimated Hours:** 2
- **Sequence:** 2
- **Depends On:** TASK-033

**Test Cases to Cover:**
- [ ] All dependencies healthy → 200
- [ ] SQL Server unreachable → 503 with SQL check marked failed
- [ ] Kafka unreachable → 503 with Kafka check marked failed
- [ ] Health endpoint returns 200 without valid API key (bypass confirmed)

**Definition of Done:**
- [ ] All 4 tests pass using mocked dependency health checks

---

## STORY-013: Queue-Time Opt-Out Check
_Source: EPIC-003 | Priority: Must Have | SPEC-007 | [BLOCKED-BY: STORY-001, STORY-011]_

### TASK-035: QueueTimeOptOutCheckService
- **Type:** Business Logic
- **Component:** TCPA.Api
- **Description:** Create `IQueueTimeOptOutCheckService` interface and `QueueTimeOptOutCheckService` in `TCPA.Api/Services/`. Method: `CheckAsync(string destinationNumber, CancellationToken ct) → QueueTimeCheckResult { IsOptedOut, ShouldBlock }`. Read from `IOptOutStatusRepository` (read replica path). If opted-out: write SUPPRESSED_QUEUE_TIME audit record; set `ShouldBlock = true`. If opted-in: `ShouldBlock = false`. If status store unavailable: `ShouldBlock = true` (fail-safe — AC-003). Log audit write at production level.
- **Satisfies AC:** AC-001, AC-002, AC-003
- **Estimated Hours:** 2
- **Sequence:** 1
- **Depends On:** TASK-003

**Implementation Notes:** Status store unavailability returns `ShouldBlock = true` not an exception — the controller handles this path as a 503 response to the caller (not a 500 — it is intentional fail-safe behaviour, not an error).

**Definition of Done:**
- [ ] Opted-out number → ShouldBlock = true + SUPPRESSED_QUEUE_TIME audit record
- [ ] Status store unavailable → ShouldBlock = true (no exception propagated)
- [ ] P99 < 100ms target — no joins, indexed read

---

### TASK-036: Unit tests — QueueTimeOptOutCheckService
- **Type:** Test
- **Component:** TCPA.Api.Tests
- **Description:** Mock `IOptOutStatusRepository` and `IAuditLogRepository`. Test all 3 ACs.
- **Satisfies AC:** AC-001, AC-002, AC-003
- **Estimated Hours:** 2
- **Sequence:** 2
- **Depends On:** TASK-035

**Test Cases to Cover:**
- [ ] Opted-in number → ShouldBlock = false, audit NOT written
- [ ] Opted-out number → ShouldBlock = true, SUPPRESSED_QUEUE_TIME audit written
- [ ] Repository throws → ShouldBlock = true (fail-safe), exception swallowed

**Definition of Done:**
- [ ] All 3 tests pass with mocked dependencies

---

## STORY-014: Send-Time Opt-Out Check
_Source: EPIC-003 | Priority: Must Have | SPEC-008 | [BLOCKED-BY: STORY-013]_

### TASK-037: OutboundMessageWorker — Kafka consumer + SendTimeOptOutCheckService
- **Type:** Business Logic
- **Component:** TCPA.OutboundDispatcher
- **Description:** Create `OutboundMessageWorker : BackgroundService` in `TCPA.OutboundDispatcher/Workers/`. Consume `outbound-messages` topic (group: `tcpa-outbound-dispatcher`). For each `OutboundMessageEvent`: (1) Read current opt-out status via `IOptOutStatusRepository`. (2) If opted-in: dispatch to Cool Text via `ICoolTextApiClient`; write DISPATCH audit record. (3) If opted-out and `optOutEffectiveAt > queuedAt` (race condition): write RACE_CONDITION_EDGE_CASE audit record; suppress. (4) If opted-out and `optOutEffectiveAt <= queuedAt` (opt-out was in effect when queued): write POTENTIAL_VIOLATION audit record; raise immediate alert; suppress. (5) If status store unavailable: suppress (fail-safe); write alert. Commit Kafka offset after each message.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 5
- **Sequence:** 1
- **Depends On:** TASK-003, TASK-007

**Implementation Notes:** The `queuedAt` timestamp in the Kafka event (written by TASK-031) is the reference point for race-condition classification. Use UTC comparison throughout. The POTENTIAL_VIOLATION path should generate a synchronous alert (not just a log) — write a separate `ViolationAlertService.RaiseAsync()` that POSTs to an alerting endpoint or writes to a monitoring topic.

**Definition of Done:**
- [ ] Race condition (queuedAt < optOutEffectiveAt) → RACE_CONDITION_EDGE_CASE; no dispatch
- [ ] Potential violation (queuedAt >= optOutEffectiveAt) → POTENTIAL_VIOLATION alert raised
- [ ] Status store unavailable → suppress + alert (not dispatch)
- [ ] Kafka offset committed after every message regardless of outcome

---

### TASK-038: Unit tests — OutboundMessageWorker and send-time check
- **Type:** Test
- **Component:** TCPA.OutboundDispatcher.Tests
- **Description:** Mock `IOptOutStatusRepository`, `ICoolTextApiClient`, `IAuditLogRepository`. Test all 4 AC scenarios with controlled timestamps.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 3
- **Sequence:** 2
- **Depends On:** TASK-037

**Test Cases to Cover:**
- [ ] Opted-in at send time → Cool Text dispatch called, DISPATCH audit written
- [ ] Opted-out, queuedAt < optOutEffectiveAt → race condition path, Cool Text NOT called
- [ ] Opted-out, queuedAt >= optOutEffectiveAt → POTENTIAL_VIOLATION alert, Cool Text NOT called
- [ ] Status store throws → suppress + alert, Cool Text NOT called
- [ ] Kafka offset committed in all 4 scenarios

**Definition of Done:**
- [ ] All 5 tests pass with mocked dependencies

---

## EPIC-004: Admin & Re-Opt-In

---

## STORY-015: Admin Re-Opt-In API
_Source: EPIC-004 | Priority: Must Have | SPEC-011 | [BLOCKED-BY: STORY-001, STORY-002]_

### TASK-039: POST /api/v1/admin/reopt-in controller
- **Type:** API
- **Component:** TCPA.Api
- **Description:** Create `AdminController` in `TCPA.Api/Controllers/`. Implement `POST /api/v1/admin/reopt-in`. Validate request: `phoneNumber` (E.164, required), `reason` (string, required, max 500 chars), `agentId` (string, required, max 100 chars). Validate that the API key has admin scope (check key's scope claim from auth service — must be "admin", not "standard"). On valid request: call `IReOptInService.ExecuteAsync(...)`. Return 200 `{ reOptInId: guid, status: "opted-in", effectiveAt: utc-timestamp }`.
- **Satisfies AC:** AC-001, AC-002, AC-004, AC-005
- **Estimated Hours:** 2
- **Sequence:** 1
- **Depends On:** TASK-017

**Implementation Notes:** Admin scope check is separate from base authentication. The `ApiKeyAuthMiddleware` validates the key exists; the controller then checks the key's scope. Inject `IApiKeyScopeValidator` (to be defined — calls auth service for scope check). This is the RISK-008 mitigation.

**Definition of Done:**
- [ ] Standard-scope API key returns 403 (not 401) on admin endpoint
- [ ] OpenAPI annotations mark this endpoint as admin-only

---

### TASK-040: Rate limiting for admin endpoint
- **Type:** Business Logic
- **Component:** TCPA.Api
- **Description:** Implement `AdminRateLimitMiddleware` or use ASP.NET Core Rate Limiting (`AddRateLimiter`) to enforce 10 requests/minute/API key on `/api/v1/admin/reopt-in`. Read rate limit value from `ISystemConfigRepository.GetValueAsync("AdminRateLimitPerMinute")` at startup. On limit exceeded: return HTTP 429 with `Retry-After: 60` header.
- **Satisfies AC:** AC-003
- **Estimated Hours:** 2
- **Sequence:** 2
- **Depends On:** TASK-039

**Definition of Done:**
- [ ] 11th request within 60s window returns 429 with Retry-After header
- [ ] Rate limit counter is per-API-key (not global)
- [ ] Rate limit value is read from config (not hardcoded)

---

### TASK-041: ReOptInService — atomic status write and audit
- **Type:** Business Logic
- **Component:** TCPA.Core
- **Description:** Create `IReOptInService` interface and `ReOptInService` in `TCPA.Core/Services/`. Method: `ExecuteAsync(string phoneNumber, string agentId, string reason, CancellationToken ct) → ReOptInResult { ReOptInId, EffectiveAt }`. Steps within a single transaction: (1) Write RE_OPT_IN AuditLog entry. (2) Call `IOptOutStatusRepository.SetOptedInAsync(phoneNumber, auditRecordId, DateTime.UtcNow)`. (3) If no prior opt-out record exists: complete successfully, set `AnomalyFlag = true` on audit record with note "re-opt-in for number with no prior opt-out record". (4) Commit. On failure: rollback, throw.
- **Satisfies AC:** AC-001, AC-002, AC-006
- **Estimated Hours:** 3
- **Sequence:** 3
- **Depends On:** TASK-003, TASK-007

**Definition of Done:**
- [ ] Both writes atomic — rollback on any failure
- [ ] No-prior-opt-out case completes successfully (not an error) with AnomalyFlag on audit
- [ ] Returns ReOptInId from the audit record ID

---

### TASK-042: Integration tests — admin re-opt-in endpoint and service
- **Type:** Test
- **Component:** TCPA.Api.Tests / TCPA.Core.Tests
- **Description:** API-level integration tests with WebApplicationFactory. Core-level integration tests for atomicity.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004, AC-005, AC-006
- **Estimated Hours:** 3
- **Sequence:** 4
- **Depends On:** TASK-041

**Test Cases to Cover:**
- [ ] Valid admin request → 200 with reOptInId and effectiveAt
- [ ] Number with no prior opt-out → 200 with AnomalyFlag on audit record
- [ ] 11th request in 60s → 429 with Retry-After header
- [ ] Missing `reason` field → 400
- [ ] Standard-scope API key → 403
- [ ] DB failure → 500 + rollback (no partial write)

**Definition of Done:**
- [ ] All 6 test cases pass
- [ ] Atomicity verified with Testcontainers SQL Server

---

## EPIC-005: Reporting

---

## STORY-016: Opted-In Message Volume Report
_Source: EPIC-005 | Priority: Must Have | SPEC-012 | [BLOCKED-BY: STORY-004, STORY-011]_

### TASK-043: OptedInVolumeReportQuery
- **Type:** Business Logic
- **Component:** TCPA.Core
- **Description:** Create `IOptedInVolumeReportQuery` interface and `SqlOptedInVolumeReportQuery` in `TCPA.Core/Queries/`. Method: `ExecuteAsync(DateTime weekStart, DateTime weekEnd, CancellationToken ct) → OptedInVolumeReportData`. Query AuditLog for DISPATCH events in period; aggregate by applicationId and by day. Return: total dispatched, breakdown by application, breakdown by day of week. Use read replica context.
- **Satisfies AC:** AC-001, AC-002, AC-004
- **Estimated Hours:** 3
- **Sequence:** 1
- **Depends On:** TASK-007

**Implementation Notes:** `weekStart` and `weekEnd` are inclusive UTC timestamps. Report covers Monday 00:00 EST through Sunday 23:59:59 EST — convert to UTC in the service layer before calling the query.

**Definition of Done:**
- [ ] Query uses read replica
- [ ] Zero-result week still returns a valid (zero-count) result object

---

### TASK-044: OptedInVolumeReportService — format and email
- **Type:** Business Logic
- **Component:** TCPA.ReportService
- **Description:** Create `IOptedInVolumeReportService` and `OptedInVolumeReportService` in `TCPA.ReportService/Services/`. Method: `GenerateAndSendAsync(DateTime weekStart, DateTime weekEnd, CancellationToken ct)`. Steps: (1) Call `IOptedInVolumeReportQuery`. (2) Format as HTML email with totals and breakdowns. (3) Read `OptedInReportRecipients` from `ISystemConfigRepository` (JSON array of email addresses). (4) Send via SMTP (`ISmtpEmailService`). On SMTP failure: log production alert; persist report data to a retry queue (or log to file for manual re-send).
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 2
- **Sequence:** 2
- **Depends On:** TASK-043

**Definition of Done:**
- [ ] SMTP failure logged and alerted; does not throw unhandled exception in worker
- [ ] Zero-count week still sends email (AC-004)

---

### TASK-045: Unit tests — OptedInVolumeReport
- **Type:** Test
- **Component:** TCPA.ReportService.Tests
- **Description:** Mock `IOptedInVolumeReportQuery`, `ISystemConfigRepository`, `ISmtpEmailService`. Test report generation, SMTP failure, empty week.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 2
- **Sequence:** 3
- **Depends On:** TASK-044

**Test Cases to Cover:**
- [ ] Data returned from query → email sent to recipient list
- [ ] Zero-count week → email still sent (not skipped)
- [ ] SMTP throws → alert logged; no unhandled exception
- [ ] Empty recipient list → report not sent (log warning)

**Definition of Done:**
- [ ] All 4 test cases pass

---

## STORY-017: Opted-Out Message Volume Report
_Source: EPIC-005 | Priority: Must Have | SPEC-013 | [BLOCKED-BY: STORY-013, STORY-014]_

### TASK-046: OptedOutVolumeReportQuery
- **Type:** Business Logic
- **Component:** TCPA.Core
- **Description:** Create `IOptedOutVolumeReportQuery` and `SqlOptedOutVolumeReportQuery` in `TCPA.Core/Queries/`. Query AuditLog for SUPPRESSED_QUEUE_TIME and SUPPRESSED_SEND_TIME events in period. Aggregate: total suppressions, by applicationId, by suppression type, by day, count of RACE_CONDITION_EDGE_CASE events. Ensure each messageId counted once (defensive dedup on correlationId).
- **Satisfies AC:** AC-001, AC-002
- **Estimated Hours:** 3
- **Sequence:** 1
- **Depends On:** TASK-007

**Definition of Done:**
- [ ] Both suppression types aggregated separately in result
- [ ] Dedup on correlationId prevents double-counting

---

### TASK-047: OptedOutVolumeReportService — format and email
- **Type:** Business Logic
- **Component:** TCPA.ReportService
- **Description:** Same pattern as TASK-044 but for opted-out volume report. Read `OptedOutReportRecipients` from config. Format HTML with total suppressions + breakdown by type + race-condition count. Send via `ISmtpEmailService`.
- **Satisfies AC:** AC-001, AC-003
- **Estimated Hours:** 2
- **Sequence:** 2
- **Depends On:** TASK-046

**Definition of Done:**
- [ ] Breakdown by queue-time vs. send-time suppressions clearly labelled
- [ ] SMTP failure handling same as TASK-044

---

### TASK-048: Unit tests — OptedOutVolumeReport
- **Type:** Test
- **Component:** TCPA.ReportService.Tests
- **Description:** Same test pattern as TASK-045 for opted-out scenario.
- **Satisfies AC:** AC-001, AC-002, AC-003
- **Estimated Hours:** 2
- **Sequence:** 3
- **Depends On:** TASK-047

**Test Cases to Cover:**
- [ ] Both suppression types counted and reported correctly
- [ ] Race-condition edge cases counted separately
- [ ] SMTP failure → alert logged; no unhandled exception

**Definition of Done:**
- [ ] All 3 test cases pass

---

## STORY-018: Weekly Compliance Report
_Source: EPIC-005 | Priority: Must Have | SPEC-014 | [HIGH-RISK] [BLOCKED-BY: STORY-016, STORY-017, STORY-009]_

### TASK-049: ComplianceReportQuery
- **Type:** Business Logic
- **Component:** TCPA.Core
- **Description:** Create `IComplianceReportQuery` and `SqlComplianceReportQuery` in `TCPA.Core/Queries/`. Query AuditLog to aggregate: total OPT_OUT_WRITTEN events, count of CONFIRMATION_DISPATCHED within 60s (SLA met), count of SLA_BREACH events (with individual details: phone hash, timestamp, latency), cumulative opted-out count from OptOutStatus table, total suppressions (both types), total dispatches, RE_OPT_IN count, alert count. Compute opt-out suppression rate = suppressions / (suppressions + dispatches). Return list of individual SLA breach records for detailed listing.
- **Satisfies AC:** AC-001, AC-002, AC-003
- **Estimated Hours:** 4
- **Sequence:** 1
- **Depends On:** TASK-007, TASK-003

**Definition of Done:**
- [ ] SLA breach list includes hashed phone number (not plain text), timestamp, and latency
- [ ] Suppression rate calculated as a percentage (not raw ratio)
- [ ] Uses read replica for all queries

---

### TASK-050: ComplianceReportService — format, validate recipient list, email
- **Type:** Business Logic
- **Component:** TCPA.ReportService
- **Description:** Create `IComplianceReportService` and `ComplianceReportService` in `TCPA.ReportService/Services/`. Steps: (1) Read `ComplianceReportRecipients` from config — if empty, raise alert and abort (AC-005). (2) Call `IComplianceReportQuery`. (3) Format HTML report with all metrics. (4) If suppression rate < 100%: flag explicitly in email subject and body (AC-002). (5) List individual SLA breaches (AC-003). (6) Send via `ISmtpEmailService`. (7) On SMTP failure: log at highest severity alert level (AC-004).
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004, AC-005
- **Estimated Hours:** 3
- **Sequence:** 2
- **Depends On:** TASK-049

**Definition of Done:**
- [ ] Empty recipient list → abort before send + alert (AC-005)
- [ ] Suppression rate < 100% flags in email subject line
- [ ] Each SLA breach listed with hashed phone, timestamp, latency

---

### TASK-051: ReportWorker — Monday 06:00 Eastern scheduled trigger for all 3 reports
- **Type:** Business Logic
- **Component:** TCPA.ReportService
- **Description:** Create `ReportWorker : BackgroundService` in `TCPA.ReportService/Workers/`. Implement a cron-style scheduler (use `Cronos` NuGet or `NCrontab`) reading the schedule from `ISystemConfigRepository.GetValueAsync("ReportScheduleCron")` (default: `"0 6 * * 1"` — Monday 06:00). On trigger: calculate prior week's Monday 00:00 EST → Sunday 23:59:59 EST as UTC range. Call all 3 report services in sequence: `IOptedInVolumeReportService`, `IOptedOutVolumeReportService`, `IComplianceReportService`. Log start, completion, and any failure at production level. Worker starts on service startup; waits until next Monday 06:00 EST before first run.
- **Satisfies AC:** AC-001 (all 3 reporting stories)
- **Estimated Hours:** 2
- **Sequence:** 3
- **Depends On:** TASK-044, TASK-047, TASK-050

**Implementation Notes:** EST/EDT handling: use `TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")` (Windows TZ name). Convert to UTC before passing to queries. If running on Linux (future), use `"America/New_York"`. Document this in operations guide.

**Definition of Done:**
- [ ] Cron schedule from config (not hardcoded)
- [ ] Monday 06:00 Eastern fires correctly in DST and non-DST
- [ ] All 3 reports called even if one fails (individual try/catch per report)

---

### TASK-052: Unit tests — ComplianceReportService and ReportWorker
- **Type:** Test
- **Component:** TCPA.ReportService.Tests
- **Description:** Test all 5 ACs for compliance report. Test schedule calculation for DST transitions. Test that all 3 reports run even if one throws.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004, AC-005
- **Estimated Hours:** 3
- **Sequence:** 4
- **Depends On:** TASK-051

**Test Cases to Cover:**
- [ ] All metrics present in formatted output
- [ ] Suppression rate < 100% flags in subject line
- [ ] 3 SLA breach records listed individually with correct data
- [ ] SMTP failure on compliance report → highest-severity alert
- [ ] Empty recipient list → abort before send; alert raised
- [ ] ReportWorker: one report failing does not prevent remaining 2 from running
- [ ] Schedule fires at Monday 06:00 Eastern (verified with test clock)

**Definition of Done:**
- [ ] All 7 test cases pass

---

## Task Dependency Map

```
TASK-001 → TASK-002 → TASK-003 → TASK-004
TASK-005 → TASK-006 → TASK-007 → TASK-008
TASK-009 → TASK-010 → TASK-011
TASK-012 → TASK-013 → TASK-014
TASK-015 → TASK-016

TASK-017 ─────────────────────────────────────────────────────────► (all API tasks)
TASK-018 → TASK-019 → TASK-020
TASK-021 → TASK-022

TASK-003 + TASK-007 → TASK-023 → TASK-024
TASK-013 + TASK-007 → TASK-025 → TASK-026
TASK-010 → TASK-027 → TASK-028 → TASK-029

TASK-003 → TASK-035 → TASK-036
TASK-035 → TASK-030 → TASK-031 → TASK-032
TASK-033 → TASK-034

TASK-003 + TASK-007 → TASK-037 → TASK-038
TASK-007 + TASK-003 → TASK-041 → TASK-039 → TASK-040 → TASK-042

TASK-007 → TASK-043 → TASK-044 → TASK-045
TASK-007 → TASK-046 → TASK-047 → TASK-048
TASK-007 + TASK-003 → TASK-049 → TASK-050 → TASK-051 → TASK-052
```

## Effort Summary by Story

| Story     | Tasks | Est. Hours | Risk Level |
|-----------|-------|------------|------------|
| STORY-001 | 4     | 10         | Standard   |
| STORY-002 | 4     | 10         | Standard   |
| STORY-003 | 3     | 6          | High       |
| STORY-004 | 3     | 6          | Standard   |
| STORY-005 | 2     | 5          | Standard   |
| STORY-006 | 4     | 10         | Standard   |
| STORY-007 | 2     | 5          | Standard   |
| STORY-008 | 2     | 6          | Standard   |
| STORY-009 | 2     | 8          | High       |
| STORY-010 | 3     | 8          | Standard   |
| STORY-011 | 3     | 8          | High       |
| STORY-012 | 2     | 5          | High       |
| STORY-013 | 2     | 4          | Standard   |
| STORY-014 | 2     | 8          | Standard   |
| STORY-015 | 4     | 10         | Standard   |
| STORY-016 | 3     | 7          | Standard   |
| STORY-017 | 3     | 7          | Standard   |
| STORY-018 | 4     | 12         | High       |
| **Total** | **52**| **135**    |            |

_Note: 12 tasks are test tasks (included in above). Infrastructure/DI setup tasks not listed separately — estimated ~17h across projects for solution scaffolding, NuGet management, DI wiring in Program.cs, and OpenAPI/Swashbuckle configuration._

## Effort Summary by Component

| Component                  | Tasks | Est. Hours |
|----------------------------|-------|------------|
| TCPA.Core                  | 22    | 59         |
| TCPA.Api                   | 16    | 43         |
| TCPA.MessageProcessor      | 7     | 20         |
| TCPA.OutboundDispatcher    | 2     | 8          |
| TCPA.ReportService         | 5     | 17         |
| Infrastructure/scaffolding | —     | ~17        |
| **Total**                  | **52**| **~164**   |
