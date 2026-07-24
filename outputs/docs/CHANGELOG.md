# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [1.0.0] — 2026-07-24

### Added

**TCPA.Api**
- `POST /webhook/inbound` — receives inbound SMS events from Cool Text/Twilio; validates Cool Text account; enforces idempotency via `ProcessedMessages` composite PK; publishes to `inbound-messages` Kafka topic; returns HTTP 200 within the 5-second SLA
- `POST /api/v1/messages/outbound` — accepts outbound SMS submissions from SCG applications; queue-time opt-out check (fail-closed on DB error); idempotency via caller `correlationId`; publishes to `outbound-messages` Kafka topic
- `POST /api/v1/admin/reopt-in` — help desk re-opt-in endpoint; atomic audit + status write; rate-limited to 10 requests per minute per API key; anomaly flagging when number has no prior opt-out record
- `GET /api/v1/health` — dependency health check for database and Kafka; used by load balancer probes
- `X-Api-Key` authentication on all protected endpoints; separate `AdminKeys` list for admin endpoints
- Rate limiter with fixed-window policy (10/min per key) on `POST /api/v1/admin/reopt-in`; `Retry-After: 60` header on 429 responses
- Serilog structured logging with daily rolling file (90-day retention) and console sink
- Swagger UI in Development environment at `/swagger`

**TCPA.Core**
- `TcpaDbContext` with keyed registrations (`"primary"` and `"replica"`) for write/read separation
- Five database tables via EF Core 8 migrations: `OptOutStatuses`, `AuditLogs`, `CoolTextAccounts`, `SystemConfigs`, `ProcessedMessages`
- `ProcessedMessages` composite PK `(MessageId, Endpoint)` for per-endpoint idempotency
- `KeywordDetectionService` — exact-match detection of TCPA opt-out keywords: STOP, QUIT, END, REVOKE, OPT-OUT, CANCEL, UNSUBSCRIBE (case-insensitive, full-message match only)
- `PhoneNumberHasher` — HMAC-SHA256 hashing of phone numbers for PII-safe logging; key sourced from `Logging:PhoneHashKey`
- `ReOptInService` — atomic re-opt-in within a SQL transaction; anomaly flag on audit record when no prior opt-out exists; transaction guard for InMemory test compatibility
- `AddTcpaCore()` extension method for consistent DI registration across all host projects

**TCPA.MessageProcessor**
- Kafka consumer (`GroupId: tcpa-inbound-processor`) for `inbound-messages` topic; manual offset commit (`EnableAutoCommit = false`)
- `OptOutProcessingService` — atomic opt-out status + audit write within a SQL transaction; duplicate opt-outs recorded as `OptOutDuplicate` audit events without updating status
- `ConfirmationDispatchService` — sends opt-out confirmation SMS via Cool Text API; 3 retries with 2s/4s/8s exponential backoff; SLA threshold of 60 seconds from message receipt; `SlaBreach` audit event written when threshold exceeded
- `ReplyForwardingService` — forwards non-opt-out inbound replies to the Cool Text account's registered `CallbackUrl`
- Scope-per-message pattern using `IServiceScopeFactory` to prevent DbContext reuse across messages
- Retry policy: 2 attempts per Kafka message; poison-pill drain on exhaustion (offset committed, Critical log written)

**TCPA.OutboundDispatcher**
- Kafka consumer (`GroupId: tcpa-outbound-dispatcher`) for `outbound-messages` topic; manual offset commit
- `OutboundGateService` — evaluates two compliance gates before send: (1) opt-out status check, (2) TCPA quiet hours check (UTC 08:00–20:59 inclusive); `OutboundSuppressed` audit event on suppression; injectable clock for testability
- `OutboundSendService` — sends outbound SMS via Cool Text API; 3 retries with 2s/4s/8s exponential backoff; `OutboundDelivered` or `OutboundFailed` audit event written; never throws so caller can always commit offset
- Idempotency check at dispatch time guards against Kafka at-least-once redeliveries
- Scope-per-message pattern and poison-pill drain (same pattern as MessageProcessor)

### Security

- All phone numbers in logs (at every severity level) and in `AuditLog.Details` JSON are replaced with HMAC-SHA256 hex digests; raw E.164 values are stored only in `AuditLog.PhoneNumber` (database column, access-controlled)
- Cool Text API error response bodies are truncated to 200 characters before logging to prevent PII leakage from gateway responses
- Admin API key authentication layered on top of standard API key authentication for `POST /api/v1/admin/reopt-in`
- Fail-closed opt-out check: if the compliance database is unavailable at outbound submission time, the API returns 503 rather than allowing an unchecked message through
