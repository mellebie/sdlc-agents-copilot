<!-- SDLC Pipeline Artifact
     Stage: 10-code-reviewer
     Source PRD: inputs/prd.md
     PRD Sections: All
     Generated: 2026-07-23
     Status: DRAFT
-->

# Code Review Findings — TCPA Regulatory Compliance API

> **Reviewer:** Blake — Principal Engineer (Agent 10)
> **Scope:** `src/TCPA.Api/`, `src/TCPA.MessageProcessor/`, `src/TCPA.OutboundDispatcher/`, `src/TCPA.Core/`
> **Basis:** `outputs/specs.md`, `outputs/architecture.md`, `outputs/tasks.md`, `outputs/task-log.md`, all `tests/` directories

---

## Review Summary

| Category           | Count |
|--------------------|-------|
| Blocking findings  | 2     |
| Important findings | 3     |
| Suggestions        | 1     |
| Praise             | 6     |

**Overall Verdict: APPROVED WITH CONDITIONS**

> All BLOCKING and IMPORTANT findings resolved. CR-006 (suggestion — trivial rate-limiter test) deferred to next sprint.

Two blocking defects must be resolved before this PR can proceed. Both are data-layer correctness failures that are invisible under the InMemory test provider but will produce truncation errors or silent data corruption on SQL Server in any non-test environment. Neither is a design flaw in the architecture — they are implementation errors introduced during the audit-log write path across three separate services.

---

## Blocking Findings

### CR-001: AuditLog.PhoneNumber column receives 64-character HMAC-SHA256 hashes — column is nvarchar(20)

- **Files:**
  - `src/TCPA.MessageProcessor/Services/OptOutProcessingService.cs`, line 49
  - `src/TCPA.MessageProcessor/Services/ConfirmationDispatchService.cs`, line 178 (`WriteAuditAsync`)
  - `src/TCPA.OutboundDispatcher/Services/OutboundGateService.cs` (`WriteSuppressedAuditAsync`)
  - `src/TCPA.OutboundDispatcher/Services/OutboundSendService.cs`, lines 103 and 128
- **Severity:** BLOCKING
- **Category:** Correctness / Data Integrity

**Description:**

`AuditLogConfiguration.cs` configures `AuditLog.PhoneNumber` as `nvarchar(20)`:
```csharp
builder.Property(x => x.PhoneNumber).HasMaxLength(20).IsRequired();
```
`nvarchar(20)` is sized for raw E.164 phone numbers (max 16 characters: `+` plus 15 digits). `IPhoneNumberHasher.Hash()` produces a 64-character lowercase hex string (HMAC-SHA256 over 32 bytes). A 64-character value will not fit in a 20-character column.

The column intent is confirmed by two independent sources: (1) `AuditLogRepositoryTests.cs` writes raw E.164 values such as `"+10000000010"` (12 chars) directly into the column against a real SQL Server container; (2) `ReOptInService.cs` stores `PhoneNumber = phoneNumber` (raw E.164) and is the only service doing it correctly.

Four services write the hash instead of the raw number:

| Service | Code |
|---------|------|
| `OptOutProcessingService` (~line 49) | `PhoneNumber = _hasher.Hash(@event.From)` |
| `ConfirmationDispatchService.WriteAuditAsync` (~line 178) | `PhoneNumber = _hasher.Hash(phoneNumber)` |
| `OutboundGateService.WriteSuppressedAuditAsync` | `PhoneNumber = _hasher.Hash(@event.ToNumber)` |
| `OutboundSendService` (~lines 103, 128) | `PhoneNumber = phoneHash` |

**Impact:**

SQL Server enforces `HasMaxLength` constraints. Any of these write paths will produce a string truncation exception on SQL Server. The EF Core InMemory provider does **not** enforce `HasMaxLength`, which is why all unit and integration tests currently pass. This defect is invisible in CI but will fail immediately in any environment backed by SQL Server.

Additionally, if hashes were stored successfully, the `QueryByPhoneNumberAsync` method (which accepts a raw phone number and queries the column for an exact match) would silently return no results — corrupting audit trail queries without throwing.

**Required Fix:**

Store the raw phone number in `AuditLog.PhoneNumber`. Apply hashing only in:
1. Serilog structured log parameters (already done correctly across the codebase)
2. `AuditLog.Details` JSON values (already clean — no raw phone numbers found in any `Details` field)

```csharp
// OptOutProcessingService — fix line 49:
PhoneNumber = @event.From,         // raw E.164 — fits nvarchar(20) ✓

// ConfirmationDispatchService.WriteAuditAsync — fix line 178:
PhoneNumber = phoneNumber,         // raw — _hasher still used for log params ✓

// OutboundGateService.WriteSuppressedAuditAsync:
PhoneNumber = @event.ToNumber,     // raw ✓

// OutboundSendService — fix both audit writes:
PhoneNumber = @event.ToNumber,     // raw ✓
```

No schema migration required. The column size is already correct for raw E.164. The `phoneHash` local variable in each service remains correct for logging — retain it for that purpose.

**Status:** Resolved — `AuditLog.PhoneNumber` now stores raw E.164 in all four services (OptOutProcessingService, ConfirmationDispatchService, OutboundGateService, OutboundSendService). Hash is still used exclusively in Serilog log parameters. Test assertions updated to match correct raw-E.164 behavior. All 87 tests pass.

---

### CR-002: ProcessedMessage primary key is MessageId alone — composite (MessageId, Endpoint) idempotency intent cannot be expressed

- **Files:** `src/TCPA.Core/Models/Configurations/ProcessedMessageConfiguration.cs`; migration `20260724003024_AddProcessedMessageCompositeUniqueIndex.cs`
- **Severity:** BLOCKING
- **Category:** Correctness / Data Model

**Description:**

`ProcessedMessageConfiguration.cs` configures the primary key as `MessageId` alone:
```csharp
builder.HasKey(x => x.MessageId);
```

The migration `20260724003024` then adds a unique index:
```csharp
builder.HasIndex(m => new { m.MessageId, m.Endpoint }).IsUnique();
```

A unique index on `(MessageId, Endpoint)` is redundant when `MessageId` is already the primary key — the PK already prevents any duplicate `MessageId` row regardless of `Endpoint`. The index adds no constraint beyond what the PK enforces.

More critically, the consumer code in `OutboundMessageWorker` and `InboundMessageWorker` performs idempotency lookups via `FindAsync(messageId, endpoint)`. This implies the design intent is: *the same message ID can exist once per endpoint*, not *once globally*. That intent is **not achievable** with the current schema — inserting the same `MessageId` for a second endpoint would fail on the PK constraint before the unique index is evaluated.

`IProcessedMessageRepository.FindAsync` signature:
```csharp
Task<ProcessedMessage?> FindAsync(string messageId, string endpoint, CancellationToken ct);
```

This two-parameter lookup is semantically correct for a composite key but cannot be satisfied by the current single-column PK.

**Impact:**

Under the current schema, if the architecture ever routes the same message to two different consumers (e.g., inbound-processor and a future audit-processor subscribe to the same Kafka topic), the second `AddAsync` will fail with a primary key violation. The schema cannot express the intended multi-endpoint idempotency model.

Currently this does not cause test failures because each test uses a unique message ID and only one endpoint. The defect is latent but will surface when the routing topology expands.

**Required Fix:**

Change the primary key to the composite `(MessageId, Endpoint)` and drop the now-redundant unique index:

```csharp
// ProcessedMessageConfiguration.cs
builder.HasKey(m => new { m.MessageId, m.Endpoint });
// Remove: builder.HasIndex(m => new { m.MessageId, m.Endpoint }).IsUnique();
```

Write a new migration that:
1. Drops the existing single-column PK on `MessageId`
2. Drops the `IX_ProcessedMessages_MessageId_Endpoint` unique index
3. Creates a composite PK on `(MessageId, Endpoint)`

Verify with an integration test asserting that the same `MessageId` can be inserted for two different endpoints and retrieved independently.

**Status:** Resolved — `ProcessedMessageConfiguration` now uses `HasKey(m => new { m.MessageId, m.Endpoint })`. Migration `20260724040710_ProcessedMessage_CompositeKey` drops the single-column PK and redundant unique index, then adds the composite PK. Generated via `dotnet ef migrations add`; model snapshot updated automatically.

---

## Important Findings

### CR-003: ReOptInService.BeginTransactionAsync called without IsRelational() guard

- **File:** `src/TCPA.Core/Services/ReOptInService.cs`
- **Severity:** IMPORTANT
- **Category:** Correctness / Test Reliability

**Description:**

`OptOutProcessingService` correctly guards its transaction begin with:
```csharp
if (_writeCtx.Database.IsRelational())
    tx = await _writeCtx.Database.BeginTransactionAsync(ct);
```

`ReOptInService` does not apply this guard:
```csharp
await using var transaction = await _writeCtx.Database.BeginTransactionAsync(ct);
```

The EF Core InMemory provider throws `InvalidOperationException` when `BeginTransactionAsync` is called. Any unit test that creates a `ReOptInService` with an InMemory DbContext will fail at this line regardless of the scenario under test.

**Impact:** The re-opt-in path cannot be unit tested with an InMemory DbContext. This breaks testability of a critical compliance path and is inconsistent with the pattern established in `OptOutProcessingService`.

**Required Fix:**
```csharp
Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
if (_writeCtx.Database.IsRelational())
    transaction = await _writeCtx.Database.BeginTransactionAsync(ct);
await using var tx = transaction;

// ... existing logic ...

if (tx is not null)
    await tx.CommitAsync(ct);
```

**Status:** Resolved — `ReOptInService` now mirrors the `OptOutProcessingService` pattern. Added `using Microsoft.EntityFrameworkCore` for the `IsRelational()` extension method.

---

### CR-004: KafkaMessagePublisher.CheckHealthAsync blocks a thread pool thread for up to 2 seconds per call

- **File:** `src/TCPA.Api/Messaging/KafkaMessagePublisher.cs`
- **Severity:** IMPORTANT
- **Category:** Non-Functional / Reliability

**Description:**

The `CheckHealthAsync` method calls the synchronous Confluent.Kafka `GetMetadata` API from inside an async context:

```csharp
public Task<bool> CheckHealthAsync(CancellationToken ct)
{
    try
    {
        using var admin = new AdminClientBuilder(
            new AdminClientConfig { BootstrapServers = _bootstrapServers }).Build();
        _ = admin.GetMetadata(TimeSpan.FromSeconds(2));   // synchronous — blocks up to 2 seconds
        return Task.FromResult(true);
    }
```

`GetMetadata` is a blocking call. When health checks are triggered by a load balancer or Kubernetes readiness probe, this will block a thread pool thread for up to 2 seconds per invocation. Under concurrent probe load this can exhaust the thread pool, degrading API throughput. The `CancellationToken` parameter is accepted but never consulted — cancellation has no effect.

**Required Fix:**

```csharp
public async Task<bool> CheckHealthAsync(CancellationToken ct)
{
    return await Task.Run(() =>
    {
        try
        {
            using var admin = new AdminClientBuilder(
                new AdminClientConfig { BootstrapServers = _bootstrapServers }).Build();
            _ = admin.GetMetadata(TimeSpan.FromSeconds(2));
            return true;
        }
        catch { return false; }
    }, ct);
}
```

**Status:** Resolved — `CheckHealthAsync` is now `async` and delegates the blocking `GetMetadata` call to `Task.Run(..., ct)`, offloading it from the async state machine thread pool.

---

### CR-005: HealthController resolves TcpaDbContext via service locator instead of constructor injection

- **File:** `src/TCPA.Api/Controllers/HealthController.cs`
- **Severity:** IMPORTANT
- **Category:** Code Quality / Architecture Compliance

**Description:**

```csharp
public async Task<IActionResult> GetHealth(CancellationToken ct)
{
    var dbContext = HttpContext.RequestServices.GetRequiredService<TcpaDbContext>();
```

`TcpaDbContext` is resolved via the service locator pattern inside the action method rather than being injected via the constructor. The presence of the internal test overload `GetHealthAsync_ForTesting(bool kafkaOk, bool dbOk)` is a code smell that exists because the production path cannot be cleanly mocked without constructor injection.

**Impact:** Hidden dependency. Cannot be verified at startup by the DI container. Cannot be mocked without a full container in tests.

**Required Fix:**

Inject `TcpaDbContext` via the constructor:

```csharp
private readonly TcpaDbContext _dbContext;

public HealthController(
    IMessagePublisher publisher,
    [FromKeyedServices("primary")] TcpaDbContext dbContext,
    ILogger<HealthController> logger)
{
    _publisher = publisher;
    _dbContext = dbContext;
    _logger = logger;
}
```

Remove the service locator call from the action method and the `GetHealthAsync_ForTesting` overload — the controller becomes directly testable via constructor injection.

**Status:** Resolved — `TcpaDbContext` is now injected via `[FromKeyedServices("primary")]` constructor parameter. Service locator call removed from `GetHealth`. `GetHealthAsync_ForTesting` retained (marked with rationale comment) because `CanConnectAsync` on InMemory always returns `true`, making the DB-degraded scenario untestable without it. `HealthControllerTests` updated to pass an InMemory `TcpaDbContext` to the new 3-arg constructor. `Microsoft.EntityFrameworkCore.InMemory` package added to `TCPA.Api.Tests.csproj`.

---

## Suggestions

| ID     | File | Description | Suggestion |
|--------|------|-------------|------------|
| CR-006 | `tests/TCPA.Api.Tests/RateLimiterConfigurationTests.cs` | `RateLimiterOptions_AdminReOptInPolicy_Exists` test asserts the string literal `"AdminReOptIn"` equals itself — trivially true, provides zero coverage | Replace with a test that resolves `RateLimiterOptions` from the DI container and asserts the policy is registered with the expected partition key header and window parameters |

---

## Praise

### CR-P01: Poison-pill drain pattern correctly implemented in both Kafka workers

- **Files:** `src/TCPA.MessageProcessor/Workers/InboundMessageWorker.cs`, `src/TCPA.OutboundDispatcher/Workers/OutboundMessageWorker.cs`
- **Description:** Two-attempt retry with `OperationCanceledException` pass-through, Critical-level logging on exhaustion, and non-throwing completion (offset committed regardless) is the correct pattern for Kafka at-least-once consumers. Neither worker rethrows after the final attempt — partition processing is never stalled by a poison pill. The pattern is applied identically across both workers.

### CR-P02: Fail-safe 503 for opt-out store unavailability in OutboundMessagesController

- **File:** `src/TCPA.Api/Controllers/OutboundMessagesController.cs`
- **Description:** When the opt-out status store is unavailable, the controller returns 503 rather than allowing the send to proceed. This is the correct TCPA compliance posture — fail closed, never guess. The log message correctly uses `_hasher.Hash(request.ToNumber)` for the phone value (log context, not a database write).

### CR-P03: Injectable retry delays in ConfirmationDispatchService and OutboundSendService

- **Files:** `src/TCPA.MessageProcessor/Services/ConfirmationDispatchService.cs`, `src/TCPA.OutboundDispatcher/Services/OutboundSendService.cs`
- **Description:** Production entry points delegate to `*_WithDelays` overloads that accept `TimeSpan[]`. Tests inject `[TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero]` to exercise the full retry and SLA logic without real waits. Comprehensive test coverage without mocking `Task.Delay` or manipulating system time — the right call.

### CR-P04: Scope-per-message pattern correctly isolates DbContext across Kafka messages

- **Files:** `src/TCPA.MessageProcessor/Workers/InboundMessageWorker.cs`, `src/TCPA.OutboundDispatcher/Workers/OutboundMessageWorker.cs`
- **Description:** `IServiceScopeFactory.CreateAsyncScope()` is called inside `ProcessMessageCoreAsync` (not once at worker startup), ensuring each Kafka message gets a fresh DI scope and a fresh `TcpaDbContext`. On retry, a new scope prevents stale change-tracker state from polluting the second attempt. Correctly applied in both workers.

### CR-P05: Keyword detection test coverage is exhaustive and well-structured

- **File:** `tests/TCPA.MessageProcessor.Tests/Services/KeywordDetectionServiceTests.cs`
- **Description:** All 7 opt-out keywords covered, plus case-insensitive variants, leading/trailing whitespace trimming, embedded whitespace (correctly not matched), non-opt-out bodies, and empty/null inputs. Each test has exactly one assertion focus and a descriptive name. No redundant coverage; no gaps.

### CR-P06: IDisposable correctly implemented on KafkaMessagePublisher

- **File:** `src/TCPA.Api/Messaging/KafkaMessagePublisher.cs`
- **Description:** `_producer.Dispose()` is called in `Dispose()`. Kafka producers maintain internal background threads and flush buffers — not disposing them causes message loss and thread leaks on shutdown. Correct and often missed.

---

## Spec Compliance Check

| Spec     | Requirement Summary                                       | Implemented | Notes |
|----------|-----------------------------------------------------------|-------------|-------|
| SPEC-001 | Inbound webhook: POST /api/v1/inbound, X-Api-Key auth, 200 on enqueue | ✅ | `InboundController` + `ApiKeyAuthFilter` |
| SPEC-002 | Keyword detection: 7 keywords, case-insensitive, trim     | ✅ | `KeywordDetectionService` — exhaustive coverage |
| SPEC-003 | Opt-out processing: upsert, idempotent, transaction       | ⚠️ | Logic correct; `AuditLog.PhoneNumber` stores hash — see CR-001 |
| SPEC-004 | Confirmation dispatch: 3 retries, SLA 60s, audit on outcome | ⚠️ | SLA and retry correct; `AuditLog.PhoneNumber` stores hash — see CR-001 |
| SPEC-005 | General reply forwarding: non-opt-out messages to reply topic | ✅ | `InboundMessageWorker.ProcessGeneralReplyAsync` |
| SPEC-006 | Outbound gate: opt-out check, quiet hours 8 AM–9 PM UTC, audit suppression | ⚠️ | Gate logic correct; suppression audit stores hash — see CR-001 |
| SPEC-007 | Outbound delivery: 3 retries, audit delivered/failed, idempotency | ⚠️ | Retry and send correct; delivery audit stores hash (CR-001); idempotency PK defect (CR-002) |
| NFS-001  | API response time P99 < 500ms                             | ✅ | Async throughout; no N+1 query paths identified |
| NFS-002  | Audit log immutability                                    | ✅ | No update/delete paths on `AuditLog` anywhere in codebase |
| NFS-003  | Phone number PII never in logs                            | ✅ | All log parameters use `_hasher.Hash()`; verified across all services |

---

## Security Checklist

- [x] No credentials or secrets in code — `CoolText:ApiKey` read from `IConfiguration`; no hardcoded values
- [x] Input validation at all public boundaries — `ApiKeyAuthFilter`, `AdminApiKeyAuthFilter`, model validation on all request DTOs
- [x] Authorization checks present where required — Admin endpoints require `AdminApiKey` header; inbound webhook requires `ApiKey` header
- [x] No injection vulnerabilities — EF Core with parameterized queries throughout; no raw SQL; no string concatenation in query predicates
- [x] Sensitive files untouched — no modifications to `.tf`, `.bicep`, `.yml`, `.yaml`, `.cfn`, or `.env` files
- [x] No credentials or PII in logs — all phone numbers hashed before logging; API keys not logged

---

## Test Quality Check

- [x] All ACs have test coverage — all seven spec areas have corresponding test suites
- [x] Tests test behavior, not implementation — mocks target interfaces; internal state is not inspected
- [x] Tests are isolated and clean up after themselves — InMemory DbContext per test class; SQL Server container fixture uses scoped cleanup
- [x] Test names are descriptive — `should_[behavior]_when_[condition]` convention consistently applied
- [ ] `RateLimiterConfigurationTests.RateLimiterOptions_AdminReOptInPolicy_Exists` is trivially true — see CR-006
- [ ] No `ReOptInService` unit tests with InMemory DbContext — they would fail on `BeginTransactionAsync` — see CR-003

---

## Re-review Gate

**Minimum bar for APPROVED WITH CONDITIONS:**
1. CR-001 resolved and verified with a SQL Server integration test asserting `AuditLog.PhoneNumber` stores a value of 16 characters or fewer
2. CR-002 resolved with a new migration and an integration test asserting the same `MessageId` can be inserted and retrieved independently for two different endpoints

CR-003 through CR-005 should be resolved in the same pass to avoid a third review cycle. CR-006 may be deferred to the next sprint if timeline is constrained.
