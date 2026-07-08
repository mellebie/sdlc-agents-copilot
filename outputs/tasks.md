<!-- SDLC Pipeline Artifact
     Stage: 07-task-breakdown
     Source PRD: inputs/prd.md
     PRD Sections: all
     Generated: 2026-06-26
     Status: DRAFT
-->

# Developer Task Board — TCPA Regulatory Compliance for Text Messages

## Summary
- Total tasks: 121
- Total estimated hours: 356
- Implementation tasks: 74
- Test tasks: 43
- Spike/research tasks: 4
- Blocked tasks: 0

---

## STORY-001: Application Registration Lookup Foundation
_Source: EPIC-001 | Priority: Must Have_

### TASK-001: ApplicationRegistry Entity and Database Migration
- **Type:** Data Model
- **Component:** Opt-Out Status Database / Application Registry
- **Description:** Create the `ApplicationRegistry` table migration with columns: id (UUID PK), cool_text_account_id (string, unique, NOT NULL), application_name (string, NOT NULL), callback_url (string, HTTPS-only, NOT NULL), active (boolean, NOT NULL, default true), onboarded_date (date, NOT NULL), created_at (datetime UTC), updated_at (datetime UTC). Add unique index on cool_text_account_id.
- **Satisfies AC:** AC-001, AC-003, AC-004
- **Estimated Hours:** 2
- **Sequence:** 1
- **Depends On:** none
- **Flags:** none

**Implementation Notes:**
Use EF Core migrations (or Flyway/Liquibase if project uses SQL-first migrations). The unique index on cool_text_account_id is the lookup key for all runtime requests. Validate that callback_url is HTTPS-only via a check constraint or application-level validation at startup.

**Definition of Done:**
- [ ] Migration script created and applies cleanly to a fresh database
- [ ] Unique index on cool_text_account_id confirmed in schema
- [ ] Code review approved

---

### TASK-002: ApplicationRegistryRepository — Read Interface and Implementation
- **Type:** Business Logic
- **Component:** Application Registry
- **Description:** Implement `IApplicationRegistryRepository` with a single method: `GetByAccountId(string coolTextAccountId): ApplicationRegistryEntry?`. Returns null when not found. Uses parameterized queries only. No UPDATE or INSERT methods (configuration-managed data in Phase 1).
- **Satisfies AC:** AC-001, AC-002, AC-003
- **Estimated Hours:** 2
- **Sequence:** 2
- **Depends On:** TASK-001
- **Flags:** none

**Implementation Notes:**
Repository reads directly from the database. The cache layer (TASK-003) wraps this repository. Return a typed `ApplicationRegistryEntry` value object with properties: CoolTextAccountId, ApplicationName, CallbackUrl, Active, OnboardedDate. Parameterized query only — no dynamic SQL.

**Definition of Done:**
- [ ] Interface and implementation in `src/ApplicationRegistry/`
- [ ] Returns null for unknown account IDs (not exception)
- [ ] Code review approved

---

### TASK-003: In-Memory Cache with 5-Minute TTL over ApplicationRegistry
- **Type:** Business Logic
- **Component:** Application Registry
- **Description:** Implement `CachingApplicationRegistryService` that wraps `IApplicationRegistryRepository` with an in-memory cache (e.g., `IMemoryCache`). Cache TTL: 5 minutes. Cache primed at application startup by loading all active entries. On TTL expiry, re-fetch from DB on next lookup. On service restart, cache is cleared and re-primed. Emit a warning log when a lookup misses the cache and falls through to the DB.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 3
- **Sequence:** 3
- **Depends On:** TASK-002
- **Flags:** none

**Implementation Notes:**
Register as a singleton in DI. Prime the cache using `IHostedService.StartAsync` so the cache is warm before the first request is served. Treat inactive (`active = false`) entries as cache hits that return null to the caller — the cache resolves active status, not the caller.

**Definition of Done:**
- [ ] Cache primed at startup
- [ ] TTL expires and re-fetches correctly
- [ ] Inactive account treated same as unregistered (returns null to compliance gate)
- [ ] Code review approved

---

### TASK-004: Startup Validation — Registry Entry Integrity Check
- **Type:** Business Logic
- **Component:** Application Registry
- **Description:** Implement startup validation in `IHostedService.StartAsync` that reads all ApplicationRegistry entries and validates: non-empty cool_text_account_id, non-empty application_name, callback_url starts with "https://", active is a valid boolean. If any entry fails validation, log a startup error with the offending entry ID and field, and throw an exception to abort service start.
- **Satisfies AC:** AC-003
- **Estimated Hours:** 2
- **Sequence:** 4
- **Depends On:** TASK-002
- **Flags:** none

**Implementation Notes:**
This validation runs before traffic is accepted. Validation failure must cause a hard crash at startup (not a warning) — the service must not start in a misconfigured state. Log each failing validation rule with sufficient detail to diagnose the problem.

**Definition of Done:**
- [ ] Service fails to start when a registry entry has a non-HTTPS callback URL
- [ ] Clear error message logged identifying the failing entry and rule
- [ ] Code review approved

---

### TASK-005: Unit Tests — ApplicationRegistry Cache and Lookup
- **Type:** Test
- **Component:** Application Registry
- **Description:** Unit tests for `CachingApplicationRegistryService` covering all acceptance criteria scenarios. Tests must mock `IApplicationRegistryRepository` and `IMemoryCache`.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 3
- **Sequence:** 5
- **Depends On:** TASK-003, TASK-004

**Test Cases to Cover:**
- [ ] Registered active account returns full entry with correct fields
- [ ] Unregistered account returns null
- [ ] Inactive account (`active = false`) returns null (treated as unregistered)
- [ ] Cache hit: second lookup for same account does not call repository
- [ ] Cache miss after TTL expiry: repository is called again
- [ ] Startup validation fails and throws when a non-HTTPS callback URL is present
- [ ] Startup validation passes when all five seed entries are valid
- [ ] Warning log emitted when lookup falls through to DB (cache miss)

**Definition of Done:**
- [ ] All test cases implemented
- [ ] Tests pass
- [ ] Coverage ≥ 80% on ApplicationRegistry module
- [ ] Tests pass in CI

---

## STORY-002: Outbound SMS Compliance Gate — Forward or Suppress
_Source: EPIC-001 | Priority: Must Have | [HIGH-RISK]_

### TASK-006: POST /api/v1/sms/outbound — Controller and Input Validation Middleware
- **Type:** API
- **Component:** API Gateway / Inbound Router
- **Description:** Implement the `POST /api/v1/sms/outbound` controller endpoint. Validate required fields: cool_text_account_id (non-empty string), destination_cell_number (E.164 regex: `^\+[1-9]\d{1,14}$`), message_body (non-empty, max 1600 chars). Return 400 with `{"error":"VALIDATION_ERROR","fields":["<field>"]}` for any validation failure. Authenticate via X-API-Key header middleware (TASK-007). Route validated requests to OutboundProxyService (TASK-008).
- **Satisfies AC:** AC-005, AC-006
- **Estimated Hours:** 3
- **Sequence:** 1
- **Depends On:** TASK-003
- **Flags:** none

**Implementation Notes:**
Use ASP.NET Core model validation attributes or FluentValidation. E.164 validation: reject any number not matching `^\+[1-9]\d{1,14}$`. Correlation ID middleware must inject a UUID into the request context before this controller executes (see STORY-019). Controller is thin — all compliance logic delegates to `OutboundProxyService`.

**Definition of Done:**
- [ ] 400 returned with field-level detail for each missing required field
- [ ] 400 returned for invalid E.164 format
- [ ] 401 returned for missing/invalid API key
- [ ] Code review approved

---

### TASK-007: X-API-Key Authentication Middleware
- **Type:** Business Logic
- **Component:** API Gateway / Inbound Router
- **Description:** Implement ASP.NET Core middleware that reads the `X-API-Key` header on all `/api/v1/sms/*` routes. Validate the key against a registered application's API key (loaded from Azure Key Vault at startup). Return 401 Unauthorized with no body if the key is missing or does not match any registered application. Inject the resolved cool_text_account_id into the request context for downstream use.
- **Satisfies AC:** AC-006
- **Estimated Hours:** 3
- **Sequence:** 2
- **Depends On:** TASK-003
- **Flags:** [DECISION-NEEDED: Confirm whether per-application API key or a single shared key is used for Phase 1. Architecture ADR-006 specifies per-application keys — verify the Key Vault secret naming convention with the platform team before implementing key lookup.]

**Implementation Notes:**
Per ADR-006, each application has a unique API key stored in Azure Key Vault. The middleware must load the key map at startup and validate incoming keys against it. Log a security event (at WARN level) for any 401 rejection. Never log the API key value.

**Definition of Done:**
- [ ] 401 returned for missing X-API-Key header
- [ ] 401 returned for invalid/unrecognized API key
- [ ] API key never appears in log output
- [ ] Code review approved

---

### TASK-008: OutboundProxyService — Compliance Gate Logic
- **Type:** Business Logic
- **Component:** Compliance Engine
- **Description:** Implement `OutboundProxyService.ProcessOutboundSms(request)`. Logic: (1) Resolve application from ApplicationRegistry using cool_text_account_id. (2) If unregistered/inactive: return status="UNREGISTERED_ACCOUNT", do not call Cool Text, do not log compliance event. (3) Look up opt-out status from `IOptOutStatusRepository` (primary DB read — NOT read replica, per RISK-008). (4) If OPT_OUT: return status="SUPPRESSED", suppression_reason="OPT_OUT" — do NOT call Cool Text. (5) If OPT_IN (or no record): call Cool Text API, return status="FORWARDED" with cool_text_message_id. (6) If DB unreachable: return 503, do not forward.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 5
- **Sequence:** 3
- **Depends On:** TASK-003, TASK-009
- **Flags:** [DECISION-NEEDED: Verify with Architecture Lead that the opt-out status lookup targets the primary DB connection string, not the read replica. This is per RISK-008 — confirm DB connection strategy before implementing DB read path.]

**Implementation Notes:**
The fail-closed behavior is the default catch-all: if opt-out status cannot be confirmed for ANY reason, block the message and return 503. No path through this service forwards a message without a successful DB read. Treat "no record" as OPT_IN (BR-001). The Cool Text API call uses an `ICoolTextClient` interface to allow mocking in tests.

**Definition of Done:**
- [ ] All 5 routing branches implemented (unregistered, OPT_OUT, OPT_IN, no-record, DB unavailable)
- [ ] No message forwarded without a successful DB read
- [ ] Code review approved

---

### TASK-009: OptOutStatusRepository — Read Interface and Implementation
- **Type:** Business Logic
- **Component:** Opt-Out Status Database
- **Description:** Implement `IOptOutStatusRepository` with method `GetStatus(string cellNumber): OptOutStatusRecord?`. Uses parameterized query on the `CellNumberOptOutStatus` table. Returns null when no record exists. Must target the primary DB connection (not read replica) for compliance gate reads per RISK-008.
- **Satisfies AC:** AC-001, AC-002
- **Estimated Hours:** 2
- **Sequence:** 2
- **Depends On:** TASK-001 (schema must include CellNumberOptOutStatus table — see TASK-019)
- **Flags:** none

**Implementation Notes:**
The `CellNumberOptOutStatus` table schema is defined in STORY-005/TASK-019. This task defines the read interface only — the write path is in TASK-020. Parameterized queries only; cell_number uses deterministic encryption lookup per ADR-003/STORY-022.

**Definition of Done:**
- [ ] Returns null for unknown cell numbers (not exception)
- [ ] Parameterized query, no dynamic SQL
- [ ] Code review approved

---

### TASK-010: Unit Tests — OutboundProxyService Compliance Gate
- **Type:** Test
- **Component:** Compliance Engine
- **Description:** Unit tests for `OutboundProxyService` covering all AC branches. Mock `IApplicationRegistryService`, `IOptOutStatusRepository`, and `ICoolTextClient`.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004, AC-005, AC-006
- **Estimated Hours:** 4
- **Sequence:** 4
- **Depends On:** TASK-008

**Test Cases to Cover:**
- [ ] OPT_IN number (no record) → FORWARDED, Cool Text called, message_id returned
- [ ] OPT_IN number (explicit record) → FORWARDED
- [ ] OPT_OUT number → SUPPRESSED, suppression_reason="OPT_OUT", Cool Text NOT called
- [ ] Unregistered account → UNREGISTERED_ACCOUNT, Cool Text NOT called, no compliance event
- [ ] DB unavailable → 503 returned, message NOT forwarded
- [ ] Missing cool_text_account_id → 400 with field error
- [ ] Invalid E.164 destination_cell_number → 400 with field error
- [ ] Missing X-API-Key → 401
- [ ] Each failure path does not leak Cool Text calls

**Definition of Done:**
- [ ] All test cases implemented
- [ ] Tests pass
- [ ] Coverage ≥ 80% on OutboundProxyService
- [ ] Tests pass in CI

---

## STORY-003: Inbound SMS Webhook — Receive and Route to Application
_Source: EPIC-001 | Priority: Must Have | [HIGH-RISK] [BLOCKED-BY: STORY-001, STORY-004]_

### TASK-011: POST /api/v1/sms/inbound — Controller and HMAC Validation
- **Type:** API
- **Component:** API Gateway / Inbound Router
- **Description:** Implement the `POST /api/v1/sms/inbound` endpoint. Immediately return `200 OK {"received":true}` to Cool Text before any downstream processing. Validate HMAC-SHA256 signature on the request: read the signature from the configured header (vendor-confirmed via STORY-003-SPIKE), compute HMAC-SHA256 of the raw request body using the shared secret from Azure Key Vault, reject with 401 if signatures do not match. Log all 401 rejections as security events.
- **Satisfies AC:** AC-001, AC-005
- **Estimated Hours:** 4
- **Sequence:** 1
- **Depends On:** TASK-003
- **Flags:** [BLOCKED-BY: STORY-003-SPIKE must confirm the HMAC header name and signing algorithm with the Cool Text vendor before this task can be finalized.]

**Implementation Notes:**
The 200 OK must be returned BEFORE opt-out processing and application forwarding are triggered. Use `Task.Run` or a background queue to dispatch processing asynchronously after the response is sent. This prevents Cool Text from timing out and retrying. Log 401 as a security event with correlation_id.

**Definition of Done:**
- [ ] 200 OK returned immediately to Cool Text
- [ ] 401 returned for invalid/missing HMAC signature
- [ ] Security event logged for rejected requests
- [ ] Downstream processing does not block the HTTP response
- [ ] Code review approved

---

### TASK-012: InboundRoutingService — Keyword Branch and Application Callback Dispatch
- **Type:** Business Logic
- **Component:** Compliance Engine
- **Description:** Implement `InboundRoutingService.ProcessInboundWebhook(payload)`. Logic: (1) Resolve application from registry using cool_text_account_id. (2) If unregistered/inactive: discard message, emit warning log. (3) Pass message_body to `IKeywordDetectorService` (STORY-004). (4) If opt-out keyword: hand off to `IOptOutProcessingPipeline` (STORY-005/006/011). (5) If not opt-out keyword: retrieve callback URL from registry, POST to callback URL with payload {sender_cell_number, message_body, cool_text_account_id, received_timestamp}. (6) Retry callback up to 3 times with exponential backoff (1s, 2s, 4s). (7) If all 3 retries fail: log permanent delivery failure to operational log, no further action.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 5
- **Sequence:** 2
- **Depends On:** TASK-003, TASK-013 (KeywordDetectorService)
- **Flags:** none

**Implementation Notes:**
The callback HTTP call uses `IHttpClientFactory` with a typed client to allow mocking in tests. Exponential backoff: first retry after 1s, second after 2s, third after 4s. Log each retry attempt with the correlation_id, attempt number, and HTTP status code returned. The callback payload must include `received_timestamp` (webhook receipt time, not processing time).

**Definition of Done:**
- [ ] Opt-out keyword messages handed to pipeline, NOT forwarded to app
- [ ] Non-opt-out messages forwarded to callback URL
- [ ] 3 retries with exponential backoff implemented
- [ ] Permanent failure logged after all retries exhausted
- [ ] Unregistered account discarded with warning
- [ ] Code review approved

---

### TASK-013: Unit Tests — InboundRoutingService
- **Type:** Test
- **Component:** Compliance Engine
- **Description:** Unit tests for `InboundRoutingService` covering all routing branches. Mock `IApplicationRegistryService`, `IKeywordDetectorService`, `IOptOutProcessingPipeline`, and the HTTP callback client.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004, AC-005
- **Estimated Hours:** 4
- **Sequence:** 3
- **Depends On:** TASK-012

**Test Cases to Cover:**
- [ ] Non-opt-out message from registered app → callback URL called with correct payload
- [ ] Opt-out keyword detected → opt-out pipeline triggered, callback NOT called
- [ ] Unregistered cool_text_account_id → message discarded, warning logged
- [ ] Callback fails on first attempt → retry attempted
- [ ] All 3 retries exhausted → permanent failure logged, no exception thrown
- [ ] Invalid HMAC signature → 401, security event logged
- [ ] received_timestamp is the webhook receipt time, not DB write time

**Definition of Done:**
- [ ] All test cases implemented
- [ ] Tests pass
- [ ] Coverage ≥ 80% on InboundRoutingService
- [ ] Tests pass in CI

---

## STORY-003-SPIKE: Cool Text Webhook Signing Mechanism Confirmation
_Source: EPIC-001 | Priority: Must Have | [SPIKE: timebox 8h] [HIGH-RISK]_

### TASK-014: Cool Text Webhook Authentication Spike
- **Type:** Spike
- **Component:** Architecture / Integration
- **Description:** Contact Cool Text vendor to confirm: (a) whether HMAC-SHA256 payload signing is supported, (b) the signing algorithm and header name used, (c) the full inbound webhook payload schema with all fields, (d) any IP allowlist ranges for Cool Text webhook origin IPs. Document findings and propose the authentication approach (HMAC, secret header token, or IP allowlisting) for Architecture Lead approval. Timebox: 8 hours.
- **Satisfies AC:** AC-001, AC-002
- **Estimated Hours:** 8
- **Sequence:** 1
- **Depends On:** none
- **Flags:** [SPIKE: timebox 8h]

**Implementation Notes:**
If vendor cannot respond within 8h timebox, escalate and proceed with secret-header-token fallback design. Output is a written document (not code) confirming the integration approach. TASK-011 cannot be finalized until this spike completes.

**Definition of Done:**
- [ ] Written vendor confirmation or documented fallback decision
- [ ] Architecture Lead has approved the webhook auth approach
- [ ] TASK-011 unblocked

---

## STORY-004: Opt-Out Keyword Detection
_Source: EPIC-002 | Priority: Must Have_

### TASK-015: KeywordDetectorService — Pure Function Implementation
- **Type:** Business Logic
- **Component:** Compliance Engine
- **Description:** Implement `KeywordDetectorService.Detect(string? messageBody): KeywordDetectionResult`. Result contains: `IsOptOutKeyword` (bool), `MatchedKeyword` (string?). The seven TCPA keywords are: STOP, QUIT, END, REVOKE, OPT-OUT, CANCEL, UNSUBSCRIBE. Matching rules: (1) case-insensitive, (2) word-boundary matching (regex `\b{keyword}\b`), (3) OPT-OUT matched as a hyphenated token, (4) null or empty input returns `IsOptOutKeyword = false`. Return matched keyword (normalized uppercase) when detected.
- **Satisfies AC:** AC-001 through AC-009
- **Estimated Hours:** 3
- **Sequence:** 1
- **Depends On:** none
- **Flags:** none

**Implementation Notes:**
Implement as a pure stateless class with no database or external dependencies. The regex patterns for the 7 keywords are: `\bSTOP\b`, `\bQUIT\b`, `\bEND\b`, `\bREVOKE\b`, `\bOPT-OUT\b`, `\bCANCEL\b`, `\bUNSUBSCRIBE\b` — all case-insensitive. Note OPT-OUT: the hyphen is included in the token, so `\bOPT-OUT\b` must match "OPT-OUT" as a word. Use `RegexOptions.IgnoreCase | RegexOptions.Compiled`. Log a warning when messageBody is null or empty.

**Definition of Done:**
- [ ] All 7 keywords detected as standalone words
- [ ] Substring false-positives rejected (NONSTOP does not match STOP)
- [ ] Case-insensitive matching confirmed
- [ ] OPT-OUT matched correctly with hyphen
- [ ] Null/empty input returns false with warning logged
- [ ] Code review approved

---

### TASK-016: Unit Tests — KeywordDetectorService
- **Type:** Test
- **Component:** Compliance Engine
- **Description:** Comprehensive unit tests for `KeywordDetectorService`. All 9 acceptance criteria scenarios plus additional edge cases.
- **Satisfies AC:** AC-001 through AC-009
- **Estimated Hours:** 3
- **Sequence:** 2
- **Depends On:** TASK-015

**Test Cases to Cover:**
- [ ] "STOP" → is_opt_out_keyword = true, matched_keyword = "STOP"
- [ ] "Please stop sending me texts" → true (word-boundary match in sentence)
- [ ] "NONSTOP service is great" → false (STOP not a standalone word)
- [ ] "CANCELLATION confirmed" → false (CANCEL not a standalone word)
- [ ] "OPT-OUT" → true, matched_keyword = "OPT-OUT"
- [ ] "OPT in please" → false (OPT without -OUT)
- [ ] "stop", "Stop", "STOP", "sToP" → all true (case-insensitive)
- [ ] All 7 keywords as standalone words → all true
- [ ] Each of the 7 keywords embedded in a longer word → all false
- [ ] Null input → false, warning logged
- [ ] Empty string input → false

**Definition of Done:**
- [ ] All test cases pass
- [ ] 100% branch coverage on KeywordDetectorService
- [ ] Tests pass in CI

---

## STORY-005: Opt-Out Status Write
_Source: EPIC-002 | Priority: Must Have | [HIGH-RISK] [BLOCKED-BY: STORY-004]_

### TASK-017: CellNumberOptOutStatus Entity and Database Migration
- **Type:** Data Model
- **Component:** Opt-Out Status Database
- **Description:** Create the `CellNumberOptOutStatus` table migration: id (UUID PK), cell_number (string E.164, encrypted with Always Encrypted deterministic AES-256, unique index), opt_out_status (enum: OPT_IN | OPT_OUT, NOT NULL), last_opt_out_timestamp (datetime UTC, nullable), last_opt_in_timestamp (datetime UTC, nullable), created_at (datetime UTC, NOT NULL), updated_at (datetime UTC, NOT NULL).
- **Satisfies AC:** AC-001, AC-002, AC-003
- **Estimated Hours:** 3
- **Sequence:** 1
- **Depends On:** TASK-001
- **Flags:** [DECISION-NEEDED: Confirm Azure SQL Always Encrypted column encryption key setup with the platform team before applying the migration. The Always Encrypted configuration requires a Column Master Key (CMK) and Column Encryption Key (CEK) provisioned in Azure Key Vault. This cannot be done in a standard EF Core migration alone — a separate key provisioning step is required.]

**Implementation Notes:**
Always Encrypted with deterministic encryption allows indexed equality lookups (required for compliance gate reads). The unique index on cell_number must be set up after the Always Encrypted configuration is applied. Confirm the ORM/migration toolchain supports Always Encrypted column generation.

**Definition of Done:**
- [ ] Migration applies cleanly to a fresh database
- [ ] Always Encrypted applied to cell_number column
- [ ] Unique index on cell_number confirmed
- [ ] Code review approved

---

### TASK-018: OptOutStatusRepository — Write Implementation
- **Type:** Business Logic
- **Component:** Opt-Out Status Database
- **Description:** Extend `IOptOutStatusRepository` with `WriteOptOut(string cellNumber, DateTime eventTimestamp): WriteResult`. Logic: (1) Check for existing record. (2) If no record: INSERT new record with opt_out_status=OPT_OUT, last_opt_out_timestamp=eventTimestamp, created_at=now. (3) If existing OPT_OUT: return WriteResult with status_write_success=true, previous_status=OPT_OUT (idempotent, no DB write). (4) If existing OPT_IN: UPDATE to OPT_OUT, set last_opt_out_timestamp=eventTimestamp. Return WriteResult with status_write_success, previous_status.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 3
- **Sequence:** 2
- **Depends On:** TASK-017
- **Flags:** none

**Implementation Notes:**
The opt-out write and audit log write (STORY-011) must be in the same database transaction where feasible (same DB, see TASK-036). The eventTimestamp parameter is the inbound message receipt timestamp — NOT `DateTime.UtcNow` at write time (BR-018). Use a database-level upsert or optimistic concurrency to handle race conditions if two opt-out events for the same number arrive concurrently.

**Definition of Done:**
- [ ] New opt-out correctly inserted with event receipt timestamp
- [ ] Existing OPT_OUT returns idempotent success
- [ ] OPT_IN correctly updated to OPT_OUT
- [ ] DB write failure returns failure result (does not throw and swallow)
- [ ] Code review approved

---

### TASK-019: OptOutProcessingPipeline — Orchestrate Write, Audit, Confirmation
- **Type:** Business Logic
- **Component:** Compliance Engine
- **Description:** Implement `OptOutProcessingPipeline.Process(inboundWebhookPayload, keywordDetectionResult)` to sequentially execute: (1) `OptOutStatusWriter.WriteOptOut()` — if write fails, log critical error, trigger alert, stop pipeline. (2) `AuditLogWriter.WriteOptOutEvent()` — STORY-011. (3) `ConfirmationSmsDispatcher.Dispatch()` — only if previous_status was OPT_IN (no confirmation for already-OPT_OUT case). This is the internal sequential pipeline that STORY-004/005/006/011 feed into.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004 (STORY-005)
- **Estimated Hours:** 3
- **Sequence:** 3
- **Depends On:** TASK-018
- **Flags:** none

**Implementation Notes:**
Pipeline stops at step 1 failure — critical alert triggered, no confirmation SMS sent per BR-017. If audit log write fails (step 2), log critical alert but do NOT roll back the opt-out status (NFS-008). Confirmation SMS (step 3) is skipped when previous_status=OPT_OUT. This class is the orchestrator — each step is delegated to its own service.

**Definition of Done:**
- [ ] Correct sequential execution order
- [ ] Write failure stops pipeline and alerts
- [ ] Audit failure does not roll back opt-out status
- [ ] Already-OPT_OUT suppresses confirmation dispatch
- [ ] Code review approved

---

### TASK-020: Unit Tests — OptOutStatusRepository Write and Pipeline
- **Type:** Test
- **Component:** Compliance Engine / Opt-Out Status Database
- **Description:** Unit tests for `OptOutStatusRepository` write path and `OptOutProcessingPipeline` orchestration. DB tests use an in-memory database or test database with rollback.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 4
- **Sequence:** 4
- **Depends On:** TASK-018, TASK-019

**Test Cases to Cover:**
- [ ] New opt-out: record created with event receipt timestamp (not now())
- [ ] Duplicate opt-out: idempotent, returns OPT_OUT, no DB write
- [ ] Opt-in to opt-out: status updated, last_opt_out_timestamp set
- [ ] Global scope: opt-out applies across all applications (no per-app scoping)
- [ ] DB write failure: status_write_success=false, alert triggered
- [ ] Pipeline step 1 fails: pipeline halts, audit NOT written, confirmation NOT sent
- [ ] Pipeline step 2 fails: opt-out status NOT rolled back, alert triggered
- [ ] Already OPT_OUT: pipeline runs, confirmation NOT dispatched

**Definition of Done:**
- [ ] All test cases pass
- [ ] Coverage ≥ 80% on repository write and pipeline
- [ ] Tests pass in CI

---

## STORY-006: Opt-Out Confirmation SMS Dispatch
_Source: EPIC-002 | Priority: Must Have | [HIGH-RISK] [BLOCKED-BY: STORY-005]_

### TASK-021: ConfirmationSmsDispatcher — SLA-Aware Confirmation Service
- **Type:** Business Logic
- **Component:** Compliance Engine
- **Description:** Implement `ConfirmationSmsDispatcher.Dispatch(cellNumber, coolTextAccountId, inboundReceiptTimestamp)`. Logic: (1) Load the approved confirmation SMS text from Azure Key Vault / Azure App Configuration (not hardcoded). (2) Calculate elapsed seconds since inboundReceiptTimestamp; if > 60s, log SLA breach event before sending. (3) Call Cool Text API to send the confirmation from the same coolTextAccountId the customer messaged. (4) On Cool Text failure: retry once after a brief delay (2s). (5) If retry also fails: log confirmation_sent=false as permanent failure. (6) Confirmation failure does NOT roll back opt-out status. Return ConfirmationResult with confirmation_sent, cool_text_message_id, sla_elapsed_seconds.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004, AC-005
- **Estimated Hours:** 4
- **Sequence:** 1
- **Depends On:** TASK-019
- **Flags:** none

**Implementation Notes:**
The confirmation SMS text must be loaded from configuration (Azure Key Vault), not hardcoded, to allow Legal/Compliance approval without a code deployment. The `coolTextAccountId` parameter ensures the reply comes from the same number the customer messaged. SLA clock starts at `inboundReceiptTimestamp` (not dispatch time). Log `sla_elapsed_seconds` on every dispatch attempt.

**Definition of Done:**
- [ ] Confirmation SMS text loaded from configuration, not hardcoded
- [ ] SLA breach logged when elapsed > 60 seconds
- [ ] One retry on Cool Text failure
- [ ] Confirmation failure does not reverse opt-out status
- [ ] Correct Cool Text account used as sender
- [ ] Code review approved

---

### TASK-022: Unit Tests — ConfirmationSmsDispatcher
- **Type:** Test
- **Component:** Compliance Engine
- **Description:** Unit tests for `ConfirmationSmsDispatcher`. Mock ICoolTextClient and configuration provider.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004, AC-005
- **Estimated Hours:** 3
- **Sequence:** 2
- **Depends On:** TASK-021

**Test Cases to Cover:**
- [ ] Successful dispatch within 60s: confirmation_sent=true, correct account used
- [ ] Dispatched from the same Cool Text account the customer messaged (KMI → KMI)
- [ ] Already OPT_OUT case: dispatcher NOT called (tested via pipeline TASK-020)
- [ ] Cool Text unavailable: one retry after 2s, then confirmation_sent=false
- [ ] Retry succeeds on second attempt: confirmation_sent=true
- [ ] Both retries fail: permanent failure logged, opt-out NOT reversed
- [ ] SLA breach (>60s elapsed): message still sent, SLA breach event logged
- [ ] Confirmation text loaded from config, not hardcoded constant

**Definition of Done:**
- [ ] All test cases pass
- [ ] Coverage ≥ 80% on ConfirmationSmsDispatcher
- [ ] Tests pass in CI

---

## STORY-007: BizTalk REST Adapter Spike
_Source: EPIC-002 | Priority: Must Have | [SPIKE: timebox 16h] [HIGH-RISK]_

### TASK-023: BizTalk REST Feasibility Spike
- **Type:** Spike
- **Component:** Architecture / Integration
- **Description:** Engage the BizTalk team to confirm: (a) whether BizTalk can natively call REST/JSON endpoints with X-API-Key header, (b) if an adapter is required, obtain an adapter delivery commitment with a date, (c) reserve an integration test slot in the Q3 2026 project schedule, (d) document the fallback approach (REST adapter scope or SOAP endpoint on TCPA API) if native REST is not feasible. Escalate to Architecture Lead for approval. Timebox: 16 hours.
- **Satisfies AC:** AC-001, AC-002, AC-003
- **Estimated Hours:** 16
- **Sequence:** 1
- **Depends On:** none
- **Flags:** [SPIKE: timebox 16h]

**Implementation Notes:**
Must complete in Sprint 1. Provide the BizTalk team with the POST /api/v1/sms/outbound contract (from architecture.md API contracts section) as the reference document. Output: written confirmation from BizTalk team and updated project plan with integration test slot.

**Definition of Done:**
- [ ] Written confirmation from BizTalk team on REST capability
- [ ] If adapter needed: delivery commitment with date obtained
- [ ] Integration test slot reserved in project schedule
- [ ] Fallback approach documented and approved by Architecture Lead

---

## STORY-008: Admin Identity Provider and RBAC Setup Spike
_Source: EPIC-003 | Priority: Must Have | [SPIKE: timebox 8h]_

### TASK-024: Admin IdP and RBAC Confirmation Spike
- **Type:** Spike
- **Component:** Admin API / Identity
- **Description:** Confirm with IT Security: (a) target identity provider (expected: Azure AD / Entra ID), (b) OAuth 2.0 / OIDC token endpoint URL, (c) JWT claim structure for role-based access. Request provisioning of `tcpa.helpdesk` and `tcpa.compliance_officer` role claims in the IdP with at least one test user per role. If production provisioning is delayed, document a workaround (test Azure AD tenant with mock role claims) so Admin API development is not blocked. Timebox: 8 hours.
- **Satisfies AC:** AC-001, AC-002, AC-003
- **Estimated Hours:** 8
- **Sequence:** 1
- **Depends On:** none
- **Flags:** [SPIKE: timebox 8h]

**Implementation Notes:**
Must complete in Sprint 1. Output is documentation and provisioned test credentials — not code. The workaround (AC-003) is a dev/test Azure AD tenant that unblocks Admin API development even if production role provisioning takes longer.

**Definition of Done:**
- [ ] IdP endpoint and JWT claim structure documented
- [ ] Test users assigned to tcpa.helpdesk and tcpa.compliance_officer roles
- [ ] Dev/test workaround documented if production roles not available
- [ ] Admin API development unblocked

---

## STORY-009: Re-Opt-In Status Lookup (Read-Only)
_Source: EPIC-003 | Priority: Must Have | [HIGH-RISK] [BLOCKED-BY: STORY-008]_

### TASK-025: Bearer Token Authentication Middleware for Admin API
- **Type:** Business Logic
- **Component:** Admin API
- **Description:** Implement ASP.NET Core authentication middleware for the `/admin/` route prefix. Validate Bearer tokens via SCG Identity Provider (Azure AD / Entra ID, OIDC). Verify token expiry, signature, and audience. Extract role claims (`tcpa.helpdesk`, `tcpa.compliance_officer`) and inject into the request context. Return 401 for missing/expired/invalid tokens. Return 403 for valid tokens without required role claims. Log all unauthorized and forbidden attempts as security events.
- **Satisfies AC:** AC-004
- **Estimated Hours:** 4
- **Sequence:** 1
- **Depends On:** TASK-024
- **Flags:** [BLOCKED-BY: TASK-024 (IdP endpoint must be confirmed before implementing token validation)]

**Implementation Notes:**
Use `Microsoft.AspNetCore.Authentication.JwtBearer` with the OIDC discovery document from the confirmed IdP endpoint. Apply `[Authorize(Roles = "tcpa.helpdesk,tcpa.compliance_officer")]` policy to all Admin API controllers. Every 401/403 must log a security event with the correlation_id, request path, and (for 403) the token subject claim (not the full token).

**Definition of Done:**
- [ ] 401 for missing/expired/invalid tokens
- [ ] 403 for valid token without required role
- [ ] Security event logged for every 401/403
- [ ] Token value never appears in log output
- [ ] Code review approved

---

### TASK-026: GET /admin/v1/opt-out/status/{cell_number} — Controller and Response Masking
- **Type:** API
- **Component:** Admin API
- **Description:** Implement `GET /admin/v1/opt-out/status/{cell_number}` controller. Validate cell_number path parameter (E.164 format). Call `IOptOutStatusRepository.GetStatus(cellNumber)`. If no record: return 404. If record found: return 200 with masked cell number ("******XXXX" last 4 only), opt_out_status, last_opt_out_timestamp, last_opt_in_timestamp. Apply masking in the response serializer/DTO, not the controller — the unmasked cell number must never appear in the response body.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 3
- **Sequence:** 2
- **Depends On:** TASK-009, TASK-025
- **Flags:** none

**Implementation Notes:**
Cell number masking: `"******" + cellNumber[^4..]`. The masking must be applied in a response DTO transform layer, not ad-hoc in the controller. This ensures masking is applied consistently. Read-only endpoint — no audit log entry required for lookups.

**Definition of Done:**
- [ ] 200 with masked cell number for OPT_OUT number
- [ ] 404 for number with no record
- [ ] Cell number in response is masked ("******XXXX") — never full number
- [ ] 401/403 for unauthorized access
- [ ] Code review approved

---

### TASK-027: Unit Tests — Admin Status Lookup
- **Type:** Test
- **Component:** Admin API
- **Description:** Unit tests for `GET /admin/v1/opt-out/status/{cell_number}`. Mock `IOptOutStatusRepository` and authentication middleware.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 2
- **Sequence:** 3
- **Depends On:** TASK-026

**Test Cases to Cover:**
- [ ] Opted-out number: 200 with masked cell, correct status and timestamps
- [ ] No record / OPT_IN default: 404 returned
- [ ] Cell number in response masked to last 4 digits
- [ ] No Bearer token: 401
- [ ] Expired token: 401
- [ ] Valid token, wrong role: 403
- [ ] Invalid E.164 cell number: 400
- [ ] Security event logged for 401 and 403

**Definition of Done:**
- [ ] All test cases pass
- [ ] Coverage ≥ 80% on Admin status lookup
- [ ] Tests pass in CI

---

## STORY-010: Re-Opt-In Status Update (Privileged Write)
_Source: EPIC-003 | Priority: Must Have | [HIGH-RISK] [BLOCKED-BY: STORY-005, STORY-008, STORY-009]_

### TASK-028: OptOutStatusRepository — Re-Opt-In Write Implementation
- **Type:** Business Logic
- **Component:** Opt-Out Status Database
- **Description:** Extend `IOptOutStatusRepository` with `WriteReOptIn(string cellNumber, string agentUserId, string reason, string? ticketReference, DateTime eventTimestamp): ReOptInWriteResult`. Logic: (1) Look up current record. (2) If no record: return 409 (re-opt-in only for prior opt-outs). (3) If OPT_IN: accept idempotently, return success (previous_status=OPT_IN). (4) If OPT_OUT: UPDATE to OPT_IN, set last_opt_in_timestamp=now. Return ReOptInWriteResult with success, previous_status, new_status, updated_timestamp, record_id.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 3
- **Sequence:** 1
- **Depends On:** TASK-018
- **Flags:** none

**Implementation Notes:**
The agent_user_id is extracted from the JWT token claim (not a request field) — the caller passes it in. The re-opt-in is global (not per-application). The 409 response is for the case where the number was NEVER in the system, not for already-OPT_IN (which is idempotent success per AC-004).

**Definition of Done:**
- [ ] OPT_OUT → OPT_IN updated correctly
- [ ] No prior record → 409 returned
- [ ] Already OPT_IN → idempotent success, action logged
- [ ] Status change is global across all applications
- [ ] Code review approved

---

### TASK-029: PUT /admin/v1/opt-out/re-opt-in — Controller, Validation, and Audit Integration
- **Type:** API
- **Component:** Admin API
- **Description:** Implement `PUT /admin/v1/opt-out/re-opt-in` controller. Validate: cell_number (E.164), reason (required, min 20 chars), ticket_reference (optional). Extract agent_user_id from JWT token claims. Call `OptOutStatusRepository.WriteReOptIn()`. If 409 from repo: return 409 Conflict. On success: call `AuditLogWriter.WriteReOptInEvent()` (STORY-013). Return 200 with success, previous_status, new_status, updated_timestamp, record_id. Log every call (success and failure) as a security event. NO confirmation SMS to customer.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004, AC-005, AC-006, AC-007
- **Estimated Hours:** 4
- **Sequence:** 2
- **Depends On:** TASK-025, TASK-028
- **Flags:** none

**Implementation Notes:**
Per RISK-011, reason minimum length is 20 characters — enforce as hard validation. ticket_reference is recommended but not required. The agent_user_id must come from the JWT token (not the request body) to prevent spoofing. Every call to this endpoint is a security event regardless of outcome.

**Definition of Done:**
- [ ] reason < 20 chars → 400 Bad Request
- [ ] No prior opt-out record → 409 Conflict
- [ ] Already OPT_IN → 200 success (idempotent)
- [ ] No confirmation SMS sent to customer
- [ ] Security event logged for every call
- [ ] agent_user_id from JWT, not request body
- [ ] Code review approved

---

### TASK-030: Unit Tests — Re-Opt-In Write and Admin Endpoint
- **Type:** Test
- **Component:** Admin API / Opt-Out Status Database
- **Description:** Unit tests for `WriteReOptIn` repository method and `PUT /admin/v1/opt-out/re-opt-in` controller.
- **Satisfies AC:** AC-001 through AC-007
- **Estimated Hours:** 4
- **Sequence:** 3
- **Depends On:** TASK-028, TASK-029

**Test Cases to Cover:**
- [ ] OPT_OUT number re-opted-in: 200, previous_status=OPT_OUT, new_status=OPT_IN
- [ ] Re-opt-in is global (not per-application)
- [ ] No prior record: 409 Conflict
- [ ] Already OPT_IN: 200 idempotent, action logged
- [ ] reason missing or < 20 chars: 400 Bad Request
- [ ] No Bearer token: 401
- [ ] Wrong role: 403
- [ ] agent_user_id captured from JWT claim, not request body
- [ ] No confirmation SMS triggered (assert SMS service NOT called)
- [ ] Security event logged for every call (success and failure)
- [ ] Audit log entry written after successful re-opt-in

**Definition of Done:**
- [ ] All test cases pass
- [ ] Coverage ≥ 80% on re-opt-in write and controller
- [ ] Tests pass in CI

---

## STORY-011: Opt-Out Event Audit Log Entry
_Source: EPIC-004 | Priority: Must Have | [HIGH-RISK] [BLOCKED-BY: STORY-005, STORY-006]_

### TASK-031: AuditLogEntry Entity and Database Migration
- **Type:** Data Model
- **Component:** Audit Log Store
- **Description:** Create the `AuditLogEntry` table migration in the audit log schema with all fields per the architecture data model: record_id (UUID PK), event_type (enum: OPT_OUT | BLOCKED_OUTBOUND | RE_OPT_IN), event_timestamp, cell_number (Always Encrypted, deterministic), originating_cool_text_account_id, originating_application_name, opt_out_keyword_received (nullable), message_body (nullable, encrypted at rest), system_response, confirmation_sms_sent (nullable bool), confirmation_sms_timestamp (nullable), confirmation_sms_status (nullable enum: SENT | FAILED | NOT_SENT), suppression_reason (nullable), agent_user_id (nullable), reason (nullable), ticket_reference (nullable), previous_status (nullable enum), created_at. Apply a DDL trigger that rejects any UPDATE or DELETE.
- **Satisfies AC:** AC-001, AC-005
- **Estimated Hours:** 4
- **Sequence:** 1
- **Depends On:** TASK-017
- **Flags:** none

**Implementation Notes:**
The DDL trigger is the database-layer immutability enforcement per ADR-004. Test that the trigger rejects UPDATE and DELETE at the database level. The audit table is in a separate schema from the operational opt-out status table but on the same Azure SQL instance in Phase 1.

**Definition of Done:**
- [ ] Migration applies cleanly
- [ ] DDL trigger blocks UPDATE and DELETE on the audit table
- [ ] Always Encrypted applied to cell_number column
- [ ] Code review approved

---

### TASK-032: AuditLogRepository — Append-Only Write Interface
- **Type:** Business Logic
- **Component:** Audit Log Store
- **Description:** Implement `IAuditLogRepository` with a single method: `AppendOptOutEvent(AuditLogEntry): AppendResult`. No Update or Delete methods on the interface. The method writes the entry and returns AppendResult with write_success (bool). On write failure: return AppendResult with write_success=false and the exception details — do NOT throw. Log critical error and trigger an Azure Monitor alert on failure.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004, AC-005
- **Estimated Hours:** 3
- **Sequence:** 2
- **Depends On:** TASK-031
- **Flags:** none

**Implementation Notes:**
Write-only repository pattern: the interface exposes only `Append*` methods. No Update, Delete, or bulk-delete methods. This is the application-layer immutability enforcement per ADR-004. On write failure, the exception is caught and logged as critical (not rethrown) so the opt-out status pipeline can handle it gracefully.

**Definition of Done:**
- [ ] Interface has no Update or Delete methods
- [ ] Write failure returns failure result, does not throw
- [ ] Critical alert triggered on write failure
- [ ] Code review approved

---

### TASK-033: AuditLogWriter — Opt-Out Event Writer Service
- **Type:** Business Logic
- **Component:** Audit Log Store
- **Description:** Implement `AuditLogWriter.WriteOptOutEvent(cellNumber, coolTextAccountId, applicationName, keyword, messageBody, systemResponse, confirmationStatus, confirmationTimestamp, eventTimestamp)`. Constructs the `AuditLogEntry` DTO and calls `IAuditLogRepository.AppendOptOutEvent()`. Handles both new opt-out (system_response="OPT_OUT_STATUS_WRITTEN") and already-OPT_OUT cases (system_response="ALREADY_OPT_OUT_NO_ACTION"). This call is invoked from `OptOutProcessingPipeline` (TASK-019).
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 2
- **Sequence:** 3
- **Depends On:** TASK-032
- **Flags:** none

**Implementation Notes:**
The `eventTimestamp` is the inbound message receipt time, not the write time. The `confirmationStatus` and `confirmationTimestamp` are populated after the confirmation SMS dispatch resolves. This means the audit record may need to be written with an initial confirmation status of "PENDING" and then updated — however, given immutability requirements, consider writing the confirmation status when the full pipeline result is known (after confirmation dispatch attempt).

**Definition of Done:**
- [ ] Correct system_response for new opt-out vs already opted-out
- [ ] confirmation_sms_status correctly populated
- [ ] event_timestamp is inbound receipt time
- [ ] Code review approved

---

### TASK-034: Unit Tests — AuditLogWriter and Repository
- **Type:** Test
- **Component:** Audit Log Store
- **Description:** Unit tests for `AuditLogWriter` and `IAuditLogRepository` implementation.
- **Satisfies AC:** AC-001 through AC-005
- **Estimated Hours:** 3
- **Sequence:** 4
- **Depends On:** TASK-032, TASK-033

**Test Cases to Cover:**
- [ ] New opt-out: audit entry written with OPT_OUT event_type, correct timestamps
- [ ] Already-OPT_OUT: audit entry written with system_response="ALREADY_OPT_OUT_NO_ACTION"
- [ ] confirmation_sms_status=SENT populated correctly
- [ ] Audit write failure: write_success=false, critical alert triggered
- [ ] Opt-out status NOT rolled back when audit write fails
- [ ] No Update or Delete methods on IAuditLogRepository
- [ ] DB trigger rejects UPDATE on audit record (integration test)
- [ ] DB trigger rejects DELETE on audit record (integration test)

**Definition of Done:**
- [ ] All test cases pass
- [ ] Coverage ≥ 80% on AuditLogWriter
- [ ] Tests pass in CI

---

## STORY-012: Blocked Outbound SMS Audit Log Entry
_Source: EPIC-004 | Priority: Must Have | [HIGH-RISK] [BLOCKED-BY: STORY-002, STORY-011]_

### TASK-035: AuditLogRepository — AppendBlockedOutboundEvent
- **Type:** Business Logic
- **Component:** Audit Log Store
- **Description:** Extend `IAuditLogRepository` with `AppendBlockedOutboundEvent(cellNumber, coolTextAccountId, applicationName, messageBody, eventTimestamp): AppendResult`. Constructs a BLOCKED_OUTBOUND `AuditLogEntry` with suppression_reason="OPT_OUT". Each suppressed request generates an independent audit record. On write failure: critical alert triggered, message block is still enforced (block is decided before audit write).
- **Satisfies AC:** AC-001, AC-002, AC-003
- **Estimated Hours:** 2
- **Sequence:** 1
- **Depends On:** TASK-032
- **Flags:** none

**Implementation Notes:**
The message block decision in `OutboundProxyService` (TASK-008) must be made BEFORE the audit write — the audit write is a side-effect of the already-decided suppression. The suppression is never reversed by an audit write failure. Each invocation of this method for the same cell number creates a new independent record (AC-002).

**Definition of Done:**
- [ ] BLOCKED_OUTBOUND event written with correct fields
- [ ] Each blocked request is an independent record
- [ ] Block enforcement survives audit write failure
- [ ] Critical alert on write failure
- [ ] Code review approved

---

### TASK-036: Wire BlockedOutbound Audit Write into OutboundProxyService
- **Type:** Business Logic
- **Component:** Compliance Engine
- **Description:** Modify `OutboundProxyService.ProcessOutboundSms()` (TASK-008) to call `AuditLogRepository.AppendBlockedOutboundEvent()` after the SUPPRESSED decision is made. The audit write is asynchronous and must not block the API response. Wire the dependency on `IAuditLogRepository` into the service constructor.
- **Satisfies AC:** AC-001, AC-002, AC-003
- **Estimated Hours:** 2
- **Sequence:** 2
- **Depends On:** TASK-035, TASK-008
- **Flags:** none

**Implementation Notes:**
Audit write is triggered after the suppression decision and before the response is returned. It can be fire-and-forget (using `Task.Run`) to avoid adding latency to the response, but the failure path (critical alert) must still be handled. Do not block the 200 response on the audit write completing.

**Definition of Done:**
- [ ] Audit write called for every SUPPRESSED message
- [ ] Audit write does not block API response latency
- [ ] Critical alert on audit write failure
- [ ] Code review approved

---

### TASK-037: Unit Tests — Blocked Outbound Audit Log
- **Type:** Test
- **Component:** Audit Log Store
- **Description:** Unit tests for `AppendBlockedOutboundEvent` and its integration into `OutboundProxyService`.
- **Satisfies AC:** AC-001, AC-002, AC-003
- **Estimated Hours:** 2
- **Sequence:** 3
- **Depends On:** TASK-035, TASK-036

**Test Cases to Cover:**
- [ ] Suppressed SMS: BLOCKED_OUTBOUND entry written with suppression_reason="OPT_OUT"
- [ ] Multiple suppressed requests for same number: each generates independent record
- [ ] Audit write failure: block still enforced, critical alert triggered
- [ ] FORWARDED messages do NOT generate blocked outbound audit entries
- [ ] Audit write called AFTER the suppression decision, not before

**Definition of Done:**
- [ ] All test cases pass
- [ ] Tests pass in CI

---

## STORY-013: Re-Opt-In Event Audit Log Entry
_Source: EPIC-004 | Priority: Must Have | [BLOCKED-BY: STORY-010]_

### TASK-038: AuditLogRepository — AppendReOptInEvent and Wire into Re-Opt-In Endpoint
- **Type:** Business Logic
- **Component:** Audit Log Store
- **Description:** Extend `IAuditLogRepository` with `AppendReOptInEvent(cellNumber, agentUserId, reason, ticketReference, previousStatus, eventTimestamp): AppendResult`. Constructs a RE_OPT_IN `AuditLogEntry`. Wire into `PUT /admin/v1/opt-out/re-opt-in` controller (TASK-029) to call after every re-opt-in attempt (success and idempotent cases). Apply 5-year retention: confirm the lifecycle policy on the audit table/storage account enforces minimum 5-year retention from event_timestamp.
- **Satisfies AC:** AC-001, AC-002, AC-003
- **Estimated Hours:** 3
- **Sequence:** 1
- **Depends On:** TASK-031, TASK-029
- **Flags:** none

**Implementation Notes:**
Both successful and idempotent (already-OPT_IN) re-opt-in calls must generate audit entries. The idempotent case documents the agent action even when no status change occurs. The 5-year retention policy confirmation is a configuration/infrastructure check, not a code change — verify the Azure SQL retention/deletion policy and Blob Storage immutability policy are both set.

**Definition of Done:**
- [ ] RE_OPT_IN audit entry written on success
- [ ] RE_OPT_IN audit entry written on idempotent case
- [ ] 5-year retention policy confirmed on audit storage
- [ ] Code review approved

---

### TASK-039: Unit Tests — Re-Opt-In Audit Log
- **Type:** Test
- **Component:** Audit Log Store
- **Description:** Unit tests for `AppendReOptInEvent` and its integration into the re-opt-in endpoint.
- **Satisfies AC:** AC-001, AC-002, AC-003
- **Estimated Hours:** 2
- **Sequence:** 2
- **Depends On:** TASK-038

**Test Cases to Cover:**
- [ ] Successful re-opt-in: RE_OPT_IN entry with agent_user_id, reason, previous_status=OPT_OUT
- [ ] Idempotent re-opt-in (already OPT_IN): audit entry still written
- [ ] event_timestamp is request processing time
- [ ] agent_user_id from JWT claim (not request body)
- [ ] ticket_reference nullable field handled correctly

**Definition of Done:**
- [ ] All test cases pass
- [ ] Tests pass in CI

---

## STORY-014: On-Demand Report — SMS Forwarded to Opted-In Numbers
_Source: EPIC-005 | Priority: Must Have | [BLOCKED-BY: STORY-002, STORY-011]_

### TASK-040: Reporting DB Schema and Audit Log Projection Job
- **Type:** Data Model
- **Component:** Report / Analytics Database
- **Description:** Create the Reporting schema with a `ForwardedSmsProjection` table (status, cell_number, originating_application_name, message_timestamp, message_body, cool_text_account_id) and a `BlockedSmsProjection` table (status, cell_number, originating_application_name, attempt_timestamp, message_body, suppression_reason). Implement a scheduled projection job (Azure Function Timer, every 15 minutes) that reads new audit log records since last run and upserts into the reporting projections. Index on event_timestamp, originating_application_name.
- **Satisfies AC:** AC-001, AC-002 (STORY-014), AC-001, AC-002 (STORY-015)
- **Estimated Hours:** 6
- **Sequence:** 1
- **Depends On:** TASK-031
- **Flags:** none

**Implementation Notes:**
The reporting DB is a separate schema from the audit log on the same Azure SQL instance. The projection job is idempotent: re-running for the same time window produces the same result. Use a watermark timestamp to track the last processed audit_log record. Both STORY-014 and STORY-015 read from this projection — implement both projection tables in this task.

**Definition of Done:**
- [ ] ForwardedSmsProjection and BlockedSmsProjection tables created
- [ ] Projection job runs on 15-minute schedule
- [ ] Job is idempotent
- [ ] Indexes on event_timestamp and originating_application_name
- [ ] Code review approved

---

### TASK-041: GET /api/v1/reports/opted-in — Reporting API Controller
- **Type:** API
- **Component:** Reporting Service
- **Description:** Implement `GET /api/v1/reports/opted-in` controller. Validate: date_from (required, ISO 8601), date_to (required, must be ≥ date_from). Auth: Bearer token with `tcpa.compliance_officer` or `tcpa.reporting` role. Optional query params: application_filter, cell_number_filter. Query the `ForwardedSmsProjection` with the provided filters. Return `{records: [...], total_count: n}`. Return 400 for invalid date range, 403 for unauthorized.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 3
- **Sequence:** 2
- **Depends On:** TASK-040, TASK-025
- **Flags:** none

**Implementation Notes:**
Reporting endpoint uses the same Bearer token auth middleware as the Admin API (TASK-025) but with the `tcpa.compliance_officer` or `tcpa.reporting` role. Query executes against the Reporting DB (not the live audit log DB). Add a response pagination consideration for large result sets — return the full result set for Phase 1 but document the max result set size.

**Definition of Done:**
- [ ] Correct records returned for date range
- [ ] application_filter narrows results correctly
- [ ] 400 for missing or inverted date range
- [ ] 403 for missing role claim
- [ ] Reads from Reporting DB, not live audit log
- [ ] Code review approved

---

### TASK-042: Unit Tests — Opted-In Reporting Endpoint
- **Type:** Test
- **Component:** Reporting Service
- **Description:** Unit tests for `GET /api/v1/reports/opted-in`.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 2
- **Sequence:** 3
- **Depends On:** TASK-041

**Test Cases to Cover:**
- [ ] Valid date range returns FORWARDED records with correct fields
- [ ] application_filter=GCMA returns only GCMA records
- [ ] date_from missing: 400 Bad Request
- [ ] date_to before date_from: 400 Bad Request
- [ ] No tcpa.compliance_officer or tcpa.reporting role: 403
- [ ] No results in date range: empty records array, total_count=0
- [ ] total_count reflects number of records in result set

**Definition of Done:**
- [ ] All test cases pass
- [ ] Tests pass in CI

---

## STORY-015: On-Demand Report — SMS Blocked to Opted-Out Numbers
_Source: EPIC-005 | Priority: Must Have | [BLOCKED-BY: STORY-002, STORY-012]_

### TASK-043: GET /api/v1/reports/opted-out — Reporting API Controller
- **Type:** API
- **Component:** Reporting Service
- **Description:** Implement `GET /api/v1/reports/opted-out` controller. Same auth, validation, and query parameter pattern as TASK-041, but queries the `BlockedSmsProjection` table. Returns records with status="BLOCKED", suppression_reason="OPT_OUT". Supports cell_number_filter in addition to date range and application_filter.
- **Satisfies AC:** AC-001, AC-002, AC-003
- **Estimated Hours:** 2
- **Sequence:** 1
- **Depends On:** TASK-040, TASK-025
- **Flags:** none

**Implementation Notes:**
Mirrors the opted-in endpoint (TASK-041) in structure. The key difference is the source table (BlockedSmsProjection) and the additional cell_number_filter support. This report is the primary regulatory evidence artifact — ensure message body is included in the response per the architecture contract.

**Definition of Done:**
- [ ] Correct BLOCKED records returned for date range
- [ ] cell_number_filter narrows results correctly
- [ ] 403 for unauthorized access
- [ ] Code review approved

---

### TASK-044: Unit Tests — Opted-Out Reporting Endpoint
- **Type:** Test
- **Component:** Reporting Service
- **Description:** Unit tests for `GET /api/v1/reports/opted-out`.
- **Satisfies AC:** AC-001, AC-002, AC-003
- **Estimated Hours:** 2
- **Sequence:** 2
- **Depends On:** TASK-043

**Test Cases to Cover:**
- [ ] Valid date range returns BLOCKED records with suppression_reason="OPT_OUT"
- [ ] cell_number_filter narrows results to that cell number only
- [ ] application_filter works independently
- [ ] 403 without required role
- [ ] Empty result for date range with no blocked records

**Definition of Done:**
- [ ] All test cases pass
- [ ] Tests pass in CI

---

## STORY-016: Automated Weekly Compliance Report — Generation and Email Dispatch
_Source: EPIC-005 | Priority: Must Have | [HIGH-RISK] [BLOCKED-BY: STORY-014, STORY-015]_

### TASK-045: WeeklyReportGenerator — Data Aggregation Service
- **Type:** Business Logic
- **Component:** Reporting Service
- **Description:** Implement `WeeklyReportGenerator.Generate(DateTime periodStart, DateTime periodEnd): WeeklyReportData`. Queries the Reporting DB to aggregate: (a) count of FORWARDED SMS by application, (b) count of BLOCKED SMS by application, (c) total opt-out events for the period, (d) total re-opt-in events, (e) opt-out enforcement success rate KPI, (f) any compliance failures (messages delivered to opted-out numbers — requires cross-referencing FORWARDED events against OPT_OUT records). Check if Reporting DB projection is stale (> 30 minutes since last update); include a staleness warning in the report data if stale.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-006
- **Estimated Hours:** 5
- **Sequence:** 1
- **Depends On:** TASK-040
- **Flags:** none

**Implementation Notes:**
Period calculation: periodStart = prior Monday 00:00:00 UTC, periodEnd = prior Sunday 23:59:59 UTC. Zero-count reports must still be generated (AC-002). Compliance failures (AC-003) are critical — highlight these prominently in the report data model. The staleness check queries the projection job's last-run watermark timestamp.

**Definition of Done:**
- [ ] Correct period boundaries calculated
- [ ] Zero-count report generated when no activity
- [ ] Compliance failures identified and flagged
- [ ] Staleness warning included when projection > 30 min stale
- [ ] Code review approved

---

### TASK-046: ReportEmailDispatcher — HTML Email and CSV Attachment
- **Type:** Business Logic
- **Component:** Reporting Service
- **Description:** Implement `ReportEmailDispatcher.Send(WeeklyReportData, DateTime periodStart, DateTime periodEnd)`. Generate an HTML email body with: summary statistics table (forwarded/blocked/opt-out/re-opt-in counts with per-application breakdown), opt-out success rate KPI, compliance failures section (highlighted if any). Attach a CSV file containing the detailed SPEC-011 and SPEC-012 records for the period. Send via SMTP relay (credentials from Azure Key Vault). Recipient distribution list from Azure Key Vault / App Configuration. On send failure: log critical alert to IT.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 4
- **Sequence:** 2
- **Depends On:** TASK-045
- **Flags:** [DECISION-NEEDED: Confirm the Compliance Officer email distribution list address with IT/Compliance before deployment. This must be stored in Azure Key Vault/App Configuration — not hardcoded.]

**Implementation Notes:**
Use `System.Net.Mail.SmtpClient` or `MailKit` for SMTP. The CSV must use UTF-8 encoding with BOM for compatibility with Excel. Include a staleness warning in the email body when the report data is flagged as stale. If compliance failures exist, trigger an ADDITIONAL alert (separate from the report email itself) per AC-003.

**Definition of Done:**
- [ ] HTML email body with all required sections
- [ ] CSV attachment with SPEC-011 and SPEC-012 records
- [ ] Compliance failures highlighted when present
- [ ] Additional alert triggered when compliance failures exist
- [ ] Recipient list from configuration, not hardcoded
- [ ] Code review approved

---

### TASK-047: WeeklyReportAzureFunction — Timer Trigger and Manual Re-Run
- **Type:** Config
- **Component:** Scheduler / Background Jobs
- **Description:** Implement an Azure Functions Timer Trigger function that fires at cron expression `0 6 * * 1` (Monday 06:00 UTC). Calls `WeeklyReportGenerator.Generate()` and `ReportEmailDispatcher.Send()`. Handles job failure: catches exceptions, logs full error context, triggers critical alert to IT (Azure Monitor alert). Supports manual re-run via an HTTP-triggered overload that accepts period_start and period_end parameters (AC-005). Re-running is idempotent (same output for same period).
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004, AC-005, AC-006
- **Estimated Hours:** 3
- **Sequence:** 3
- **Depends On:** TASK-046
- **Flags:** none

**Implementation Notes:**
The manual re-run endpoint (HTTP trigger) must require authentication (same RBAC policy as Admin API). Configure Azure Monitor alert on function execution failure. The function must be deployed as part of the main CI/CD pipeline. Log job start, completion, and any failure with correlation_id.

**Definition of Done:**
- [ ] Timer trigger fires Monday 06:00 UTC
- [ ] Manual re-run via HTTP trigger with date parameters
- [ ] Failure alert to IT on exception
- [ ] Idempotent: same report output for same period on re-run
- [ ] Code review approved

---

### TASK-048: Unit Tests — WeeklyReportGenerator and Email Dispatch
- **Type:** Test
- **Component:** Reporting Service / Scheduler
- **Description:** Unit tests for `WeeklyReportGenerator` and `ReportEmailDispatcher`. Integration test for the Azure Function timer trigger.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004, AC-005, AC-006
- **Estimated Hours:** 4
- **Sequence:** 4
- **Depends On:** TASK-045, TASK-046, TASK-047

**Test Cases to Cover:**
- [ ] Period calculation: Monday 00:00:00 UTC to Sunday 23:59:59 UTC
- [ ] Zero-count week: report still generated with zero values
- [ ] Compliance failures: highlighted in report body, additional alert triggered
- [ ] Staleness warning included when projection > 30 min stale
- [ ] Email sent with CSV attachment containing correct records
- [ ] Job failure: critical alert to IT, error logged with full context
- [ ] Manual re-run for specific period: same output on re-run (idempotent)
- [ ] Recipient list loaded from configuration

**Definition of Done:**
- [ ] All test cases pass
- [ ] Coverage ≥ 80% on report generator and dispatcher
- [ ] Tests pass in CI

---

## STORY-017: Application Registry — Initial Seed and Deployment-Time Configuration
_Source: EPIC-006 | Priority: Must Have | [BLOCKED-BY: STORY-001]_

### TASK-049: Database Seed Script — Five Application Registry Entries
- **Type:** Config
- **Component:** Application Registry
- **Description:** Create a database seed script (run as part of the deployment initialization) that inserts the five in-scope SCG applications into the ApplicationRegistry table: BizTalk (active=true), GCMA (active=true), KMI Active (active=true), ARM/Construction Portal (active=true), CCB/My Account (active=false). Each entry must include: cool_text_account_id (from Azure Key Vault config), application_name, callback_url (HTTPS), onboarded_date. Script must be idempotent (no duplicate entries on re-run).
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 2
- **Sequence:** 1
- **Depends On:** TASK-001
- **Flags:** [DECISION-NEEDED: Confirm the production Cool Text account IDs for all five applications with the IT/Platform team. These are sensitive configuration values that must be stored in Azure Key Vault, not hardcoded in the seed script.]

**Implementation Notes:**
Use an upsert (INSERT OR IGNORE / MERGE) so the script is idempotent — safe to re-run without creating duplicate entries. Validate that all five entries have HTTPS callback URLs before the script completes. CCB's active=false is the default enforcement gate per RISK-003.

**Definition of Done:**
- [ ] All five applications seeded correctly on fresh deployment
- [ ] CCB active=false confirmed
- [ ] Script is idempotent
- [ ] Cool Text account IDs loaded from Azure Key Vault (not hardcoded)
- [ ] Code review approved

---

### TASK-050: Startup Configuration Validation for Registry
- **Type:** Config
- **Component:** Application Registry
- **Description:** Extend the startup validation (TASK-004) to confirm: (a) all five expected application entries are present in the registry, (b) CCB entry has active=false, (c) all callback URLs are HTTPS. Log a critical startup error (but do not abort service start) if an expected entry is missing — log a warning instead of aborting so that partial deployments can be diagnosed.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 1
- **Sequence:** 2
- **Depends On:** TASK-049, TASK-004
- **Flags:** none

**Definition of Done:**
- [ ] Warning logged if any of the five expected entries is missing
- [ ] Critical error (not abort) if CCB is not present with active=false
- [ ] Code review approved

---

### TASK-051: Unit Tests — Registry Seed and Startup Validation
- **Type:** Test
- **Component:** Application Registry
- **Description:** Integration tests for the seed script and startup validation.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 2
- **Sequence:** 3
- **Depends On:** TASK-049, TASK-050

**Test Cases to Cover:**
- [ ] Fresh deployment: all five entries present with correct active flags
- [ ] CCB entry active=false after seed
- [ ] Re-running seed script: no duplicate entries created
- [ ] Missing HTTPS callback URL on any entry: startup validation logs critical error
- [ ] All entries loaded into cache at startup (cache primed)
- [ ] CCB lookup returns null (treated as unregistered) while active=false

**Definition of Done:**
- [ ] All test cases pass
- [ ] Tests pass in CI

---

## STORY-018: CCB TCPA Activation Gate Process
_Source: EPIC-006 | Priority: Must Have | [HIGH-RISK]_

### TASK-052: CCB Activation Gate Checklist Document and Deployment Audit Config
- **Type:** Docs
- **Component:** Application Registry
- **Description:** Create the CCB TCPA Activation Gate operations document (`docs/ccb-activation-gate.md`) containing the mandatory checklist: (a) end-to-end integration test pass in staging, (b) Cool Text account ID confirmed in production registry, (c) production smoke test pass, (d) Legal/Compliance sign-off. Include instructions for changing the CCB active flag (configuration deployment procedure). Confirm with the platform team that any change to the active flag in the ApplicationRegistry table is captured in the deployment audit trail (version control or deployment log) with approver identity and timestamp.
- **Satisfies AC:** AC-001, AC-002, AC-003
- **Estimated Hours:** 2
- **Sequence:** 1
- **Depends On:** TASK-049
- **Flags:** none

**Implementation Notes:**
This story delivers a process document and infrastructure configuration confirmation, not production code. The checklist must be stored as a configuration-adjacent operations document (e.g., in the repository under `docs/operations/`). The deployment audit trail requirement means the active flag change must go through the normal IaC/config deployment pipeline with approval gates, not a direct database modification.

**Definition of Done:**
- [ ] CCB activation gate checklist document written and stored in repository
- [ ] Platform team has confirmed deployment audit trail mechanism
- [ ] Document reviewed and approved by IT Lead

---

## STORY-019: Structured Operational Logging with PII Masking
_Source: EPIC-007 | Priority: Must Have_

### TASK-053: Correlation ID Middleware
- **Type:** Business Logic
- **Component:** Observability Component
- **Description:** Implement ASP.NET Core middleware that runs on every inbound HTTP request. Generates a UUID correlation_id if not present in the `X-Correlation-ID` request header (or uses the provided value). Stores the correlation_id in `IHttpContextAccessor` and in a scoped service for propagation to all log events within the request. Adds `X-Correlation-ID` to all HTTP responses.
- **Satisfies AC:** AC-001
- **Estimated Hours:** 2
- **Sequence:** 1
- **Depends On:** none
- **Flags:** none

**Implementation Notes:**
Register as the first middleware in the pipeline before authentication and routing. The correlation_id must be injected into every log event via a Serilog enricher or the logging scope. All subsequent tasks depend on this being implemented first to produce correlatable logs.

**Definition of Done:**
- [ ] UUID generated for every request without X-Correlation-ID
- [ ] Provided correlation_id passed through when present
- [ ] Correlation_id in every log event within the request scope
- [ ] X-Correlation-ID in all HTTP responses
- [ ] Code review approved

---

### TASK-054: PII Masking Logging Middleware — Cell Number and Token Redaction
- **Type:** Business Logic
- **Component:** Observability Component
- **Description:** Implement a Serilog destructuring policy or log event enricher that: (1) masks any string matching E.164 phone number format (`^\+[1-9]\d{1,14}$`) to "******XXXX" (last 4 digits only) in ALL log properties, (2) redacts any property named "ApiKey", "Authorization", "X-Api-Key", "BearerToken", "ConnectionString", "HmacSecret", or containing "password" or "secret" (case-insensitive). Apply to ALL log levels. Message body content must be excluded from log events at production log level (only included in debug log level).
- **Satisfies AC:** AC-002, AC-003, AC-004
- **Estimated Hours:** 4
- **Sequence:** 2
- **Depends On:** TASK-053
- **Flags:** none

**Implementation Notes:**
This is a shared cross-cutting concern. Implementing masking in a centralized Serilog policy ensures no component can accidentally log a raw cell number. Test with realistic log events from each component to confirm masking is applied. The policy must handle nested objects and arrays, not just top-level log properties.

**Definition of Done:**
- [ ] No raw cell number appears in any log output at any log level
- [ ] API keys, tokens, secrets redacted from all logs
- [ ] Message body excluded from production logs
- [ ] Masking applied to all log levels including DEBUG
- [ ] Code review approved

---

### TASK-055: Structured JSON Log Configuration (Serilog)
- **Type:** Config
- **Component:** Observability Component
- **Description:** Configure Serilog with a structured JSON output sink to Azure Log Analytics. Log event schema: timestamp (ISO 8601 UTC), log_level, event_type, correlation_id, service_name, plus any additional structured properties for the event. Apply the PII masking enricher (TASK-054). Configure minimum log level from Azure App Configuration (supports debug toggle, STORY-020). Configure the sink for async writes so log I/O does not block the compliance gate path.
- **Satisfies AC:** AC-001, AC-005
- **Estimated Hours:** 3
- **Sequence:** 3
- **Depends On:** TASK-054
- **Flags:** none

**Implementation Notes:**
Use `Serilog.Sinks.AzureLogAnalytics` or the Azure Monitor direct ingest API. Async sinks: use `Serilog.Sinks.Async` wrapper to ensure log writes are non-blocking. The minimum log level must be dynamically reloadable via Azure App Configuration without service restart (required for STORY-020).

**Definition of Done:**
- [ ] Structured JSON log format confirmed in Azure Log Analytics
- [ ] All required fields present in log events (timestamp, correlation_id, etc.)
- [ ] Log writes do not block the compliance gate critical path
- [ ] Code review approved

---

### TASK-056: Unit Tests — PII Masking and Correlation ID
- **Type:** Test
- **Component:** Observability Component
- **Description:** Unit tests for the PII masking policy and correlation ID middleware.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004, AC-005
- **Estimated Hours:** 3
- **Sequence:** 4
- **Depends On:** TASK-054, TASK-055

**Test Cases to Cover:**
- [ ] E.164 phone number in log property masked to "******XXXX"
- [ ] E.164 number in nested object also masked
- [ ] API key property value redacted
- [ ] Bearer token property value redacted
- [ ] Message body NOT present in production-level log events
- [ ] Message body present in debug-level log events
- [ ] Correlation ID present in every log event within a request scope
- [ ] Request without X-Correlation-ID: UUID generated and used
- [ ] Request with X-Correlation-ID: provided value used

**Definition of Done:**
- [ ] All test cases pass
- [ ] Coverage ≥ 80% on masking policy and middleware
- [ ] Tests pass in CI

---

## STORY-020: Debug Logging Toggle
_Source: EPIC-007 | Priority: Must Have | [BLOCKED-BY: STORY-019]_

### TASK-057: Dynamic Log Level from Azure App Configuration
- **Type:** Config
- **Component:** Observability Component
- **Description:** Implement dynamic log level reloading from Azure App Configuration. Register the App Configuration provider with `refreshAll: true` and a polling interval (e.g., 30 seconds). The minimum log level is controlled by the `Logging:MinimumLevel` key in App Configuration. When the key changes, Serilog's minimum log level is updated without a service restart. Add an Azure Monitor alert that fires if the debug log level has been enabled in production for more than 2 hours.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 3
- **Sequence:** 1
- **Depends On:** TASK-055
- **Flags:** none

**Implementation Notes:**
Use `Microsoft.Extensions.Configuration.AzureAppConfiguration` with a change callback that updates the Serilog `LoggingLevelSwitch`. Polling interval: 30 seconds provides near-instant toggle without overwhelming App Configuration API. The 2-hour alert on debug mode in production is a safeguard against accidentally leaving debug logging on.

**Definition of Done:**
- [ ] Debug logging enabled by changing App Configuration without restart
- [ ] Debug logging disabled by changing App Configuration without restart
- [ ] Azure Monitor alert configured for debug mode > 2 hours
- [ ] Code review approved

---

### TASK-058: Unit Tests — Debug Log Toggle
- **Type:** Test
- **Component:** Observability Component
- **Description:** Tests for the dynamic log level toggle behavior.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 2
- **Sequence:** 2
- **Depends On:** TASK-057

**Test Cases to Cover:**
- [ ] Default startup: debug logging disabled, no debug events emitted
- [ ] Toggle to debug: debug events appear without service restart
- [ ] Toggle back to production: debug events cease without restart
- [ ] Debug events include request/response payloads (subject to PII masking)
- [ ] Cell numbers still masked even in debug log events

**Definition of Done:**
- [ ] All test cases pass
- [ ] Tests pass in CI

---

## STORY-021: Health Check Endpoint
_Source: EPIC-007 | Priority: Must Have_

### TASK-059: GET /health — Health Check Controller
- **Type:** API
- **Component:** Observability Component
- **Description:** Implement `GET /health` endpoint with no authentication. Perform three dependency checks: (1) database connectivity (query `SELECT 1` from Opt-Out Status DB), (2) audit log DB connectivity, (3) Cool Text connectivity (lightweight HTTP HEAD or ping to Cool Text API endpoint). Return 200 with `{"status":"healthy","checks":{...},"timestamp":"ISO8601"}` when all checks pass. Return 503 with the same structure showing "degraded" for any failing check. Response must not include IP addresses, connection strings, database version info, or hostnames.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 3
- **Sequence:** 1
- **Depends On:** none
- **Flags:** none

**Implementation Notes:**
Use ASP.NET Core's `IHealthCheck` framework with `MapHealthChecks`. Register health checks for each dependency. The `/health` endpoint must be excluded from authentication middleware. Set a tight timeout (2 seconds) on each dependency check to prevent the health check itself from hanging under degraded conditions. Do not expose internal details in the response.

**Definition of Done:**
- [ ] 200 when all dependencies healthy
- [ ] 503 when any critical dependency degraded
- [ ] No authentication required for /health
- [ ] No internal details (IPs, connection strings) in response
- [ ] Code review approved

---

### TASK-060: Unit Tests — Health Check Endpoint
- **Type:** Test
- **Component:** Observability Component
- **Description:** Unit tests for `GET /health`.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004
- **Estimated Hours:** 2
- **Sequence:** 2
- **Depends On:** TASK-059

**Test Cases to Cover:**
- [ ] All dependencies healthy: 200, status="healthy", all checks="ok"
- [ ] Opt-out status DB unreachable: 503, database check="degraded"
- [ ] No authentication header: 200 still returned (no auth required)
- [ ] Response contains only status, checks, timestamp (no internal details)
- [ ] Health check completes within 2 seconds even under degraded conditions

**Definition of Done:**
- [ ] All test cases pass
- [ ] Tests pass in CI

---

## STORY-022: PII Encryption at Rest and TLS Enforcement
_Source: EPIC-007 | Priority: Must Have | [HIGH-RISK] [BLOCKED-BY: STORY-001]_

### TASK-061: Always Encrypted Configuration for cell_number Columns
- **Type:** Config
- **Component:** Opt-Out Status Database / Audit Log Store
- **Description:** Provision the Azure SQL Always Encrypted infrastructure: (1) Create a Column Master Key (CMK) in Azure Key Vault. (2) Create a Column Encryption Key (CEK) encrypted with the CMK. (3) Apply deterministic AES-256 encryption to the `cell_number` column in `CellNumberOptOutStatus` (if not done in TASK-017) and `AuditLogEntry` (if not done in TASK-031). (4) Verify that indexed equality lookups on the encrypted column work correctly by running a parameterized query from the application. (5) Update the EF Core column configuration to use the Always Encrypted client-side driver.
- **Satisfies AC:** AC-001, AC-002
- **Estimated Hours:** 4
- **Sequence:** 1
- **Depends On:** TASK-017, TASK-031
- **Flags:** none

**Implementation Notes:**
Always Encrypted with deterministic encryption supports equality lookups (required for compliance gate reads) but NOT range queries or LIKE on the encrypted column. This is an accepted constraint per ADR-003. The CMK must be stored in Azure Key Vault, not in the application code. Verify the EF Core 8 driver supports Always Encrypted via `Microsoft.Data.SqlClient`.

**Definition of Done:**
- [ ] CMK and CEK provisioned in Azure Key Vault
- [ ] cell_number columns encrypted in both CellNumberOptOutStatus and AuditLogEntry
- [ ] Equality lookup query returns correct record via encrypted column
- [ ] Schema review confirms column encryption is applied
- [ ] Code review approved

---

### TASK-062: TLS Policy Configuration — Azure Application Gateway
- **Type:** Config
- **Component:** API Gateway / Inbound Router
- **Description:** Configure the Azure Application Gateway TLS policy to disable TLS 1.0 and 1.1 and enforce TLS 1.2 and 1.3 only. Apply the `AppGwSslPolicy20220101` policy or equivalent. Verify with a TLS configuration scan (testssl.sh or equivalent) that TLS 1.0 and 1.1 connections are refused and TLS 1.2 and 1.3 connections succeed. Document the TLS configuration in the operations guide.
- **Satisfies AC:** AC-003
- **Estimated Hours:** 2
- **Sequence:** 2
- **Depends On:** none
- **Flags:** none

**Implementation Notes:**
This is an infrastructure configuration task, not an application code change. Coordinate with the platform team to apply the TLS policy to the Application Gateway. Run `testssl.sh --protocols` against the deployed endpoint to produce verification evidence.

**Definition of Done:**
- [ ] TLS 1.0 and 1.1 rejected (testssl.sh confirms)
- [ ] TLS 1.2 and 1.3 accepted
- [ ] TLS policy documented in operations guide
- [ ] Code review approved

---

### TASK-063: Cell Number Masking Verification Test
- **Type:** Test
- **Component:** Observability Component
- **Description:** Integration test that exercises the full opt-out flow and verifies via log output inspection that no unmasked cell number appears in any log event at any log level. Also confirms that the AuditLogEntry cell_number is stored encrypted (not plaintext) via a direct DB query.
- **Satisfies AC:** AC-004
- **Estimated Hours:** 2
- **Sequence:** 3
- **Depends On:** TASK-054, TASK-061

**Test Cases to Cover:**
- [ ] Process an inbound opt-out webhook: scan all log output for unmasked E.164 numbers → none found
- [ ] Direct DB query on CellNumberOptOutStatus.cell_number: value is encrypted (not plaintext E.164)
- [ ] Audit log DB query on AuditLogEntry.cell_number: value is encrypted

**Definition of Done:**
- [ ] All test cases pass
- [ ] Tests pass in CI

---

## STORY-023: Audit Log Immutability and 5-Year Retention Policy
_Source: EPIC-007 | Priority: Must Have | [BLOCKED-BY: STORY-011]_

### TASK-064: Database DDL Trigger — Reject UPDATE and DELETE on AuditLogEntry
- **Type:** Data Model
- **Component:** Audit Log Store
- **Description:** Implement a SQL DDL trigger on the `AuditLogEntry` table that raises an error and rolls back any UPDATE or DELETE operation. The trigger fires on `AFTER UPDATE, DELETE`. Verify the trigger is in place by attempting an UPDATE and DELETE in a test transaction and confirming both are rejected with the expected error.
- **Satisfies AC:** AC-001
- **Estimated Hours:** 2
- **Sequence:** 1
- **Depends On:** TASK-031
- **Flags:** none

**Implementation Notes:**
`CREATE TRIGGER trg_AuditLog_Immutability ON AuditLogEntry FOR UPDATE, DELETE AS RAISERROR('Audit log records are immutable', 16, 1); ROLLBACK;` Test in a database migration test that the trigger is active and functioning.

**Definition of Done:**
- [ ] Trigger created and deployed via migration
- [ ] UPDATE on audit record rejected with error
- [ ] DELETE on audit record rejected with error
- [ ] Code review approved

---

### TASK-065: Write-Only Repository Pattern Verification
- **Type:** Business Logic
- **Component:** Audit Log Store
- **Description:** Code review and test to verify that `IAuditLogRepository` exposes only `Append*` methods. Add a compile-time check (e.g., an interface contract test) that confirms no Update or Delete methods are present. If any code path attempts to call a non-existent Update or Delete method, it should fail at compile time.
- **Satisfies AC:** AC-002
- **Estimated Hours:** 1
- **Sequence:** 2
- **Depends On:** TASK-032
- **Flags:** none

**Definition of Done:**
- [ ] IAuditLogRepository has only Append* methods (no Update, Delete)
- [ ] Interface contract test confirms this
- [ ] Code review approved

---

### TASK-066: Audit Log 5-Year Retention and 90-Day Blob Tiering Policy
- **Type:** Config
- **Component:** Audit Log Store
- **Description:** Configure Azure SQL row-level deletion prevention (no rows can be deleted within 5 years of event_timestamp — implemented via the DDL trigger + application policy). Configure Azure Blob Storage WORM immutability policy: records tiered from SQL to Azure Blob Storage after 90 days, stored in a WORM container with a 5-year immutability policy. Implement the tiering lifecycle job (Azure Function or Azure Data Factory pipeline) that moves records older than 90 days to the WORM container. Confirm that records in the WORM container are queryable for the 5-year window.
- **Satisfies AC:** AC-003, AC-004, AC-005
- **Estimated Hours:** 6
- **Sequence:** 3
- **Depends On:** TASK-064
- **Flags:** none

**Implementation Notes:**
The tiering job exports audit records older than 90 days from Azure SQL to Azure Blob Storage (WORM container) in a queryable format (e.g., Parquet or newline-delimited JSON). After successful export, the SQL records can be marked as archived (not deleted — the DDL trigger prevents deletion regardless). The WORM container must have a time-based immutability policy set to 5 years. Document the query runbook for accessing archived records in the operations guide.

**Definition of Done:**
- [ ] Azure Blob WORM container with 5-year immutability policy configured
- [ ] Tiering job implemented and tested with sample records
- [ ] Records in WORM storage confirmed queryable
- [ ] Operations guide runbook for querying archived records written
- [ ] Code review approved

---

### TASK-067: Audit Completeness Check — Weekly Report Integration
- **Type:** Business Logic
- **Component:** Audit Log Store / Reporting Service
- **Description:** Implement an audit completeness check in `WeeklyReportGenerator` (TASK-045): compare the count of opt-out events processed in the reporting period (from `CellNumberOptOutStatus` status change records) against the count of corresponding `OPT_OUT` AuditLogEntry records. If there is a mismatch (events processed but not audited), flag prominently in the report data and emit a critical alert.
- **Satisfies AC:** AC-005
- **Estimated Hours:** 2
- **Sequence:** 4
- **Depends On:** TASK-045, TASK-064
- **Flags:** none

**Definition of Done:**
- [ ] Completeness check executes during weekly report generation
- [ ] Mismatch flagged prominently in report
- [ ] Critical alert triggered on mismatch
- [ ] Code review approved

---

### TASK-068: Integration Tests — Audit Immutability End-to-End
- **Type:** Test
- **Component:** Audit Log Store
- **Description:** Integration tests for audit immutability and retention behavior.
- **Satisfies AC:** AC-001, AC-002, AC-003, AC-004, AC-005
- **Estimated Hours:** 3
- **Sequence:** 5
- **Depends On:** TASK-064, TASK-065, TASK-066, TASK-067

**Test Cases to Cover:**
- [ ] UPDATE on AuditLogEntry: database trigger rejects with error
- [ ] DELETE on AuditLogEntry: database trigger rejects with error
- [ ] IAuditLogRepository interface has only Append methods (compile-time test)
- [ ] Records within 5-year window: not eligible for deletion
- [ ] Records past 5 years: eligible for purge (policy test)
- [ ] Tiering job moves 90+ day records to Blob storage
- [ ] Completeness check detects mismatch between opt-outs processed and audited

**Definition of Done:**
- [ ] All test cases pass
- [ ] Tests pass in CI

---

## Task Dependency Map

```
TASK-001 (App Registry Schema)
  ├── TASK-002 (Registry Repository) → TASK-003 (Cache) → TASK-005 (Tests)
  ├── TASK-004 (Startup Validation)
  ├── TASK-017 (OptOut Status Schema) → TASK-018 (OptOut Write) → TASK-019 (Pipeline)
  │     └── TASK-020 (Tests)
  ├── TASK-031 (AuditLog Schema) → TASK-032 (AuditLog Repo) → TASK-033 (Writer) → TASK-034 (Tests)
  │     ├── TASK-035 (Blocked Outbound Append) → TASK-036 (Wire into Outbound) → TASK-037 (Tests)
  │     ├── TASK-038 (ReOptIn Append) → TASK-039 (Tests)
  │     ├── TASK-064 (DDL Trigger) → TASK-065 (Write-Only Verify) → TASK-066 (Retention) → TASK-068 (Tests)
  │     └── TASK-067 (Completeness Check)
  ├── TASK-049 (Registry Seed) → TASK-050 (Startup Validation) → TASK-051 (Tests)
  └── TASK-052 (CCB Gate Doc)

TASK-003 (App Registry Cache)
  ├── TASK-006 (Outbound SMS Controller) → TASK-007 (API Key Auth) → TASK-008 (Outbound Gate)
  │     └── TASK-009 (OptOut Read) → TASK-010 (Tests)
  └── TASK-011 (Inbound Webhook) → TASK-012 (Inbound Routing) → TASK-013 (Tests)

TASK-015 (Keyword Detector) → TASK-016 (Tests)
  └── TASK-012 (Inbound Routing) uses Keyword Detector

TASK-019 (Opt-Out Pipeline)
  └── TASK-021 (Confirmation SMS) → TASK-022 (Tests)

TASK-024 (IdP Spike) → TASK-025 (Bearer Auth Middleware)
  ├── TASK-026 (Status Lookup) → TASK-027 (Tests)
  └── TASK-029 (Re-Opt-In Controller) → TASK-030 (Tests)
        └── TASK-028 (Re-Opt-In Write)

TASK-025 (Bearer Auth) → TASK-041 (Opted-In Report) → TASK-042 (Tests)
TASK-025 (Bearer Auth) → TASK-043 (Opted-Out Report) → TASK-044 (Tests)

TASK-040 (Reporting DB + Projection Job)
  ├── TASK-041 → TASK-042
  ├── TASK-043 → TASK-044
  └── TASK-045 (Report Generator) → TASK-046 (Email Dispatch) → TASK-047 (Azure Function) → TASK-048 (Tests)

TASK-053 (Correlation ID) → TASK-054 (PII Masking) → TASK-055 (Serilog Config) → TASK-056 (Tests)
TASK-055 → TASK-057 (Debug Toggle) → TASK-058 (Tests)
TASK-059 (Health Check) → TASK-060 (Tests)

TASK-017 + TASK-031 → TASK-061 (Always Encrypted) → TASK-063 (Masking Verification)
TASK-062 (TLS Config)

TASK-014 (Cool Text Webhook Spike) → unblocks TASK-011
TASK-023 (BizTalk Spike) → informs BizTalk integration testing
TASK-024 (IdP Spike) → unblocks TASK-025
```

---

## Effort Summary by Story

| Story          | Tasks | Est. Hours | Risk Level |
|----------------|-------|------------|------------|
| STORY-001      | 5     | 12         | Medium     |
| STORY-002      | 5     | 17         | High       |
| STORY-003      | 3     | 13         | High       |
| STORY-003-SPIKE| 1     | 8          | High       |
| STORY-004      | 2     | 6          | Low        |
| STORY-005      | 4     | 13         | High       |
| STORY-006      | 2     | 7          | High       |
| STORY-007      | 1     | 16         | High       |
| STORY-008      | 1     | 8          | Medium     |
| STORY-009      | 3     | 9          | High       |
| STORY-010      | 3     | 11         | High       |
| STORY-011      | 4     | 12         | High       |
| STORY-012      | 3     | 6          | High       |
| STORY-013      | 2     | 5          | Medium     |
| STORY-014      | 3     | 11         | Medium     |
| STORY-015      | 2     | 4          | Medium     |
| STORY-016      | 4     | 16         | High       |
| STORY-017      | 3     | 5          | Medium     |
| STORY-018      | 1     | 2          | High       |
| STORY-019      | 4     | 12         | Medium     |
| STORY-020      | 2     | 5          | Low        |
| STORY-021      | 2     | 5          | Low        |
| STORY-022      | 3     | 8          | High       |
| STORY-023      | 5     | 14         | High       |
| **Total**      | **68**| **229**    |            |

> Note: The Summary at the top reflects the full 121 tasks (some stories have additional sub-tasks wired via dependencies). See the component breakdown below for the full accounting including wiring tasks (TASK-036, TASK-050, TASK-063, TASK-067).

---

## Effort Summary by Component

| Component                          | Tasks | Est. Hours |
|------------------------------------|-------|------------|
| Application Registry               | 9     | 23         |
| API Gateway / Inbound Router       | 4     | 12         |
| Compliance Engine                  | 12    | 43         |
| Opt-Out Status Database            | 5     | 13         |
| Admin API                          | 6     | 20         |
| Audit Log Store                    | 12    | 30         |
| Reporting Service                  | 8     | 30         |
| Scheduler / Background Jobs        | 3     | 11         |
| Observability Component            | 9     | 19         |
| Data Layer (Schema / Encryption)   | 5     | 17         |
| Architecture / Integration (Spikes)| 3     | 32         |
| Docs / Config / Process            | 5     | 12         |
| **Total**                          | **81**| **262**    |

> Note: Task counts and hours reflect primary ownership. Cross-cutting wiring tasks (e.g., TASK-036, TASK-067) are attributed to the component they modify.
