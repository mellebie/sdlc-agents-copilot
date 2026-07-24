# TCPA Compliance API — Developer Architecture Overview

This document is for developers joining the project. It explains what the system does, how it is structured, where to find things in the codebase, and why key decisions were made. For the full architecture artifact with ADRs and deployment topology, see `outputs/architecture.md`.

---

## What the system does

The TCPA system sits between SCG upstream applications (BizTalk, GCMA, KMI Active, ARM, CCB) and the Cool Text SMS platform. It has two jobs:

1. **Inbound:** receive SMS replies from recipients, detect opt-out keywords, record opt-out status, and send a confirmation SMS.
2. **Outbound:** accept send requests from SCG applications, check whether the recipient has opted out (and whether the current time is within TCPA allowed hours), and dispatch approved messages to Cool Text.

---

## Components

```
SCG Applications
    │
    │  POST /api/v1/messages/outbound
    ▼
TCPA.Api ──────────────────────────────────────────────┐
    │  POST /webhook/inbound                            │
    ▲                                                   │
Cool Text / Twilio                                      │
                                                        │ Kafka
                                         outbound-messages topic
                                                        │
                                                        ▼
                                          TCPA.OutboundDispatcher
                                                        │
                                                        │ Cool Text API
                                                        ▼
                                                    Cool Text

TCPA.Api ──► inbound-messages (Kafka topic) ──► TCPA.MessageProcessor
                                                        │
                                                        │ Cool Text API (confirmation SMS)
                                                        ▼
                                                    Cool Text

All components ──► SQL Server (TCPA.Core — shared data access)
```

### TCPA.Api

ASP.NET Core 8 Web API. Four endpoints:

- `POST /webhook/inbound` — receives inbound SMS from Cool Text; validates account; records idempotency; publishes to `inbound-messages` topic
- `POST /api/v1/messages/outbound` — accepts outbound requests; queue-time opt-out check; publishes to `outbound-messages` topic
- `POST /api/v1/admin/reopt-in` — help desk re-opt-in; rate-limited 10/min per key
- `GET /api/v1/health` — dependency health check (database + Kafka)

Auth: custom `X-Api-Key` filter. Admin endpoints require a second key in `ApiKeys:AdminKeys`.

### TCPA.Core

Class library shared by all components. Contains:

- `TcpaDbContext` — EF Core 8 DbContext, keyed as `"primary"` (write) and `"replica"` (read). Registered via `AddTcpaCore()` extension method.
- Five entity models: `OptOutStatus`, `AuditLog`, `CoolTextAccount`, `SystemConfig`, `ProcessedMessage`
- Five repository interfaces and SQL implementations
- Domain services: `KeywordDetectionService`, `PhoneNumberHasher`, `ReOptInService`
- EF Core migrations (5 migrations, all applied before first run)

### TCPA.MessageProcessor

.NET 8 Worker Service. Kafka consumer group `tcpa-inbound-processor`.

- Subscribes to `inbound-messages` topic
- Uses `KeywordDetectionService` to classify each message
- Opt-out path: `OptOutProcessingService` writes opt-out status + audit atomically; `ConfirmationDispatchService` sends confirmation SMS via Cool Text API (3 retries, 2s/4s/8s backoff; SLA threshold 60s)
- Non-opt-out path: `ReplyForwardingService` looks up the Cool Text account's `CallbackUrl` and forwards the message body
- Retry policy: 2 attempts per message; on all-attempts-failed, logs Critical and commits offset (poison-pill drain)
- Scope-per-message: fresh `IServiceScope` per message ensures DbContext is not reused across messages

### TCPA.OutboundDispatcher

.NET 8 Worker Service. Kafka consumer group `tcpa-outbound-dispatcher`.

- Subscribes to `outbound-messages` topic
- Per-message: idempotency check → gate evaluation → send → record processed
- Gate evaluation (`OutboundGateService`): opt-out check, then TCPA quiet hours check (UTC 08:00–20:59 inclusive); suppressed messages write `OutboundSuppressed` audit
- Send (`OutboundSendService`): Cool Text API call with 3 retries (2s/4s/8s backoff); writes `OutboundDelivered` or `OutboundFailed` audit
- Retry policy: 2 attempts per message; poison-pill drain on exhaustion
- Scope-per-message: same pattern as MessageProcessor

---

## Database schema

Five tables, all in `TcpaDbContext`:

| Table | Purpose |
|-------|---------|
| `OptOutStatuses` | One row per phone number; `Status` = `opted-in` or `opted-out` |
| `AuditLogs` | Immutable compliance event log; one row per event |
| `CoolTextAccounts` | Registered SMS accounts; `AccountNumber` maps to applications |
| `SystemConfigs` | Key-value config store; `OptOutMessageBody` key drives confirmation SMS text |
| `ProcessedMessages` | Idempotency store; composite PK `(MessageId, Endpoint)` |

Run migrations:

```bash
dotnet ef database update --project src/TCPA.Core --startup-project src/TCPA.Api
```

---

## Key design decisions

**Kafka at-least-once delivery with manual offset commit.** Both workers set `EnableAutoCommit = false` and commit the offset explicitly after processing completes (or after the poison-pill drain on exhaustion). This means a message may be delivered more than once on broker restart; idempotency via `ProcessedMessages` guards against duplicate processing.

**Fail-closed opt-out check.** If the opt-out status store is unavailable at queue time (outbound submission), the API returns 503 and does not queue the message. It is better to delay a non-urgent message than to risk sending to an opted-out recipient.

**Phone number hashing in all logs.** No raw phone numbers appear in any log at any severity level. `IPhoneNumberHasher` uses HMAC-SHA256 with a key from `Logging:PhoneHashKey`. The hash is deterministic, so a single number always produces the same hash token for correlation across log entries.

**TCPA quiet hours applied conservatively.** When recipient timezone is unknown, the dispatcher applies UTC. The allowed window is 08:00–20:59 UTC (hour `>= 8 && < 21`). Messages arriving outside this window are suppressed and audited.

**Transaction guard for InMemory compatibility.** `if (_ctx.Database.IsRelational())` gates all `BeginTransactionAsync` calls. This allows unit tests to run with an InMemory provider without throwing `InvalidOperationException`.

**Keyed DbContext registrations.** `AddTcpaCore()` registers three DbContext instances: keyed `"primary"` (write), keyed `"replica"` (read-replica, falls back to primary if `ConnectionStrings:ReadReplica` is absent), and a non-keyed alias for services that inject `TcpaDbContext` directly.

---

## Codebase navigation

```
src/
├── TCPA.Api/
│   ├── Controllers/          — AdminController, HealthController, InboundWebhookController, OutboundMessagesController
│   ├── Filters/              — ApiKeyAuthFilter, AdminApiKeyAuthFilter
│   ├── Messaging/            — IMessagePublisher, KafkaMessagePublisher, InboundMessageEvent, OutboundMessageEvent
│   ├── Models/               — Request and response records
│   └── Program.cs            — DI registration, rate limiter, Serilog setup
├── TCPA.Core/
│   ├── Data/                 — TcpaDbContext, TcpaDesignTimeFactory
│   ├── Extensions/           — ServiceCollectionExtensions (AddTcpaCore)
│   ├── Interfaces/           — Repository interfaces
│   ├── Migrations/           — EF Core migration files
│   ├── Models/               — Entity models and EF Core configurations
│   ├── Repositories/         — SQL implementations
│   └── Services/             — KeywordDetectionService, PhoneNumberHasher, ReOptInService, LogEventTypes
├── TCPA.MessageProcessor/
│   ├── Infrastructure/       — CoolTextApiClient
│   ├── Messaging/            — InboundMessageEvent (local copy)
│   ├── Services/             — OptOutProcessingService, ConfirmationDispatchService, ReplyForwardingService
│   ├── Workers/              — InboundMessageWorker
│   └── Program.cs
└── TCPA.OutboundDispatcher/
    ├── Infrastructure/       — CoolTextApiClient
    ├── Messaging/            — OutboundMessageEvent (local copy)
    ├── Services/             — OutboundGateService, OutboundSendService
    ├── Workers/              — OutboundMessageWorker
    └── Program.cs
```
