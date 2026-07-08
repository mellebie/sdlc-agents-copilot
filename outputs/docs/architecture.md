# TCPA Compliance API — Developer Architecture Overview

This document is for developers joining the project. It explains what the system does, how it is structured, where to find things in the codebase, and why key decisions were made. For the full architecture document with ADRs, deployment topology, and NFR fulfillment details, see `outputs/architecture.md`.

---

## What This System Does

The TCPA Compliance API sits between SCG's upstream applications and the Cool Text/Twilio SMS platform. Its single job is to enforce TCPA opt-out compliance:

- **Outbound:** An SCG application submits an SMS to send. The API checks whether the recipient has opted out. If they have, the message is blocked and the attempt is logged. If they haven't, the message is forwarded to Cool Text.
- **Inbound:** A customer replies to an SMS (e.g., sends "STOP"). Cool Text pushes the reply to this API. The API detects the opt-out keyword, records the opt-out, sends a confirmation SMS to the customer, and writes an immutable audit record.
- **Admin:** Help Desk agents and Compliance Officers can look up a number's status and manually reverse an opt-out when the customer has re-consented.
- **Reporting:** Compliance Officers can query forwarded and blocked message history and receive an automated weekly compliance report by email.

**Fail-closed principle:** if the opt-out database is unavailable, outbound messages are blocked (503 returned). The system never forwards a message without confirming the recipient's opt-in status.

---

## How the Codebase Is Organized

```
src/
├── TCPA.Api/                       # Main ASP.NET Core Web API (.NET 8)
│   ├── Controllers/                # HTTP endpoints
│   ├── Models/                     # Request/response DTOs
│   ├── Domain/                     # EF Core entities and enums
│   ├── Services/
│   │   ├── OptOut/                 # Opt-out keyword detection, status writes
│   │   ├── ReOptIn/                # Admin re-opt-in workflow
│   │   ├── SmsProxy/               # Outbound gate and inbound routing
│   │   ├── Reporting/              # Compliance report queries and email dispatch
│   │   ├── AuditLog/               # Append-only audit log writes
│   │   └── Observability/          # Correlation ID middleware
│   └── Infrastructure/
│       ├── Auth/                   # API key auth filter
│       ├── Configuration/          # Application Registry (cache-backed)
│       ├── CoolText/               # Cool Text HTTP client and webhook validator
│       └── Data/                   # EF Core DbContext, entity configurations, migrations
├── TCPA.Scheduler/                 # Azure Functions (weekly report scheduler)
└── tests/
    └── TCPA.Api.Tests/             # Unit tests
```

---

## Components and Their Responsibilities

### Controllers (HTTP boundary)

Controllers are thin. They validate input, call a service, map the result to HTTP responses, and mask PII in log statements. No business logic lives in controllers.

| Controller | Route prefix | What it does |
|------------|-------------|--------------|
| `OutboundSmsController` | `POST /api/v1/sms/outbound` | Receives outbound SMS requests from SCG apps; calls `IOutboundSmsGate` |
| `InboundSmsController` | `POST /api/v1/sms/inbound` | Receives Cool Text webhooks; validates HMAC; returns 200 immediately; processes async |
| `AdminController` | `/admin/v1/opt-out/` | Re-opt-in (PUT) and status lookup (GET) for authenticated Help Desk/Compliance Officers |
| `ReportingController` | `/api/v1/reports/` | On-demand compliance report queries; requires `ComplianceReporting` auth policy |
| `HealthController` | `GET /health` | Unauthenticated health check; sanitizes descriptions before returning |

### Services (business logic)

**`SmsProxy/`** — the core compliance pipeline:

- `OutboundSmsGate` (`IOutboundSmsGate`): Looks up the application in the registry, calls `IsOptedOutAsync` on the opt-out service, suppresses or forwards the message. Any exception from the opt-out check becomes `OutboundGateUnavailableException` → 503.
- `InboundSmsHandler` (`IInboundSmsHandler`): Receives an inbound message, runs keyword detection, triggers the opt-out pipeline if needed, forwards to the application callback if not.

**`OptOut/`** — opt-out business logic:

- `OptOutDetector` (`IOptOutDetector`): Stateless. Matches the message body against 7 CTIA opt-out keywords (`STOP`, `CANCEL`, `UNSUBSCRIBE`, `END`, `QUIT`, `REMOVE`, `OPT-OUT`) using word-boundary regex patterns. Case-insensitive.
- `OptOutStatusService` (`IOptOutStatusService`): Reads and writes opt-out status in the database. Idempotent on writes. Fail-closed: re-throws DB exceptions on reads so the gate can return 503.
- `ConfirmationDispatcher` (`IConfirmationDispatcher`): Sends the opt-out confirmation SMS within the 60-second SLA. Single retry on Cool Text failure. Never reverses the opt-out if the confirmation fails.

**`ReOptIn/`** — admin re-opt-in:

- `ReOptInService` (`IReOptInService`): Writes OPT_IN status, writes an audit log entry, returns a result with the previous status. Returns a sentinel value (`"NO_RECORD"`) instead of throwing when no prior record exists — lets the controller map to 409 without exception-driven flow.

**`AuditLog/`** — immutable audit:

- `AuditLogService` (`IAuditLogService`): Append-only writes to `AuditLogEntries`. Calls `AddAsync` only — never `Update` or `Remove`. On write failure: logs Critical (triggers Azure Monitor alert) and throws `AuditLogWriteException`. Cell numbers are always masked to last-4 digits in logs.

**`Reporting/`** — compliance reports:

- `ReportingService` (`IReportingService`): Queries `SmsMessageLogs` (forwarded messages) and `AuditLogEntries` (blocked messages) via EF Core with `AsNoTracking`. Enforces a 90-day maximum query window. Detects compliance failures (forwarded messages to opted-out numbers) and logs Critical if found.
- `ReportEmailer` (`IReportEmailer`): SMTP email dispatch. Reads credentials from config at dispatch time (never cached). Sends HTML + CSV attachment. Subjects compliance failure reports with `[COMPLIANCE FAILURE]` prefix.

### Infrastructure

**`Auth/ApiKeyAuthFilter`** — ASP.NET Core action filter on `OutboundSmsController`. Reads the expected key from `IConfiguration["Auth:ApiKey"]` at request time (so rotation takes effect without restart). Uses constant-time comparison (`CryptographicOperations.FixedTimeEquals`) to prevent timing attacks.

**`CoolText/`** — SMS platform integration:
- `CoolTextClient`: Forwards outbound messages to Cool Text (`ICoolTextClient`) and relays inbound messages back to SCG app callback URLs (`ICoolTextForwardingClient`). 3-attempt exponential backoff (1s/2s/4s) on callback forwarding.
- `CoolTextWebhookValidator`: HMAC-SHA256 signature validation for inbound webhooks. Fails at startup if the secret is not configured. Strips `sha256=` prefix. Constant-time comparison.

**`Configuration/ApplicationRegistryService`** — cache-backed lookup of Cool Text account ID → SCG application name + callback URL. 5-minute TTL (configurable). Unregistered or inactive accounts return `null` (treated as unregistered by the gate). Primes the cache at startup via `ApplicationRegistryStartupService`.

**`Data/`** — EF Core database layer:
- `TcpaDbContext`: four `DbSet`s: `ApplicationRegistrations`, `CellNumberOptOutRecords`, `AuditLogEntries`, `SmsMessageLogs`.
- Migrations in `Data/Migrations/`. The initial migration (`20260626000001_InitialSchema`) creates all four tables. Always Encrypted is a post-migration infrastructure step — columns are plain `nvarchar` until the platform team applies Always Encrypted via Azure Key Vault.

---

## Data Flow: Outbound SMS

```
SCG Application
  │ POST /api/v1/sms/outbound (X-API-Key)
  ▼
OutboundSmsController
  │ Validates input, calls gate
  ▼
OutboundSmsGate
  │ ApplicationRegistryService.GetByAccountNumberAsync(accountId)
  │ OptOutStatusService.IsOptedOutAsync(cellNumber)  ← fail-closed on exception
  │
  ├─[opted out]──▶ AuditLogService.WriteBlockedOutboundEventAsync()
  │                return SUPPRESSED
  │
  └─[opted in]───▶ CoolTextClient.SendSmsAsync()
                   return FORWARDED (with message_id)
```

## Data Flow: Inbound SMS (Opt-Out)

```
Cool Text Platform
  │ POST /api/v1/sms/inbound (X-CoolText-Signature)
  ▼
InboundSmsController
  │ HMAC signature validated → 401 if invalid
  │ 200 OK returned to Cool Text immediately
  │ ProcessInboundAsync() fired as background task
  ▼
InboundSmsHandler (background)
  │ OptOutDetector.Detect(messageBody)
  │
  ├─[opt-out keyword]──▶ OptOutStatusService.WriteOptOutAsync()
  │                       ConfirmationDispatcher.DispatchAsync()  ← 60s SLA
  │                       AuditLogService.WriteOptOutEventAsync()
  │                       (message NOT forwarded to application)
  │
  └─[no keyword]────────▶ CoolTextClient.ForwardToApplicationAsync(callbackUrl)
```

---

## Key Architectural Decisions

These are the decisions most likely to affect day-to-day development. Full ADR detail is in `outputs/architecture.md`.

**Layered monolith (ADR-001):** The system is a single ASP.NET Core application, not microservices. Internal boundaries are enforced by C# interfaces and separate namespaces. Designed to allow extraction to microservices in Phase 2 without a rewrite. Do not blur these internal boundaries.

**API key auth for upstream apps (ADR-006):** SCG applications authenticate with a shared `X-API-Key` header. The key is read from `IConfiguration["Auth:ApiKey"]` at request time so it can be rotated without a service restart. Keys are stored in Azure Key Vault — never in source code.

**HMAC-SHA256 for webhooks (ADR-007):** Cool Text signs inbound webhooks with a shared secret. The signature header is `X-CoolText-Signature` by default (configurable via `CoolText:WebhookSignatureHeader`). The shared secret is `CoolText:WebhookSecret` in Key Vault. The signing mechanism must be confirmed with the Cool Text vendor.

**No opt-out status caching (NFS-002):** The outbound compliance gate reads opt-out status directly from the database on every request. There is no caching of opt-out status in the gate path. This ensures enforcement is immediate after a status write.

**Immutable audit log (ADR-004):** `AuditLogEntries` records are never updated or deleted. Enforced at two layers: the application calls `AddAsync` only (no `Update`/`Remove` methods on the service interface), and a DDL trigger (`trg_AuditLogEntries_Immutability`) in the database rejects any UPDATE or DELETE. After 90 days, records are archived to Azure Blob Storage WORM (immutable) storage.

**Azure Functions for the scheduler (ADR-005):** The weekly compliance report runs via an Azure Functions Timer Trigger (cron `0 6 * * 1` = Monday 06:00 UTC). This isolates the job from the application process lifecycle. Failure triggers a Critical log event, which alerts IT via Azure Monitor.

---

## PII Handling

Cell phone numbers are PII. The following rules apply throughout the codebase:

- Cell numbers are **never logged in full**. Always use the last-4-digit masking pattern: `MaskCellNumber(cellNumber)` or equivalent.
- Cell numbers are stored with **Azure SQL Always Encrypted** (AES-256, deterministic encryption). This is a post-migration infrastructure step; columns are `nvarchar(20)` until the platform team applies encryption.
- The connection string must include `Column Encryption Setting=Enabled` for the driver to transparently encrypt/decrypt.
- Admin API responses return `maskedCellNumber` (last 4 digits only), never the full number.

---

## Running Migrations

In non-Production environments, migrations run automatically at startup. In Production, apply migrations via the CI/CD pipeline:

```bash
dotnet ef database update \
  --project src/TCPA.Api \
  --connection "<production-connection-string>"
```

After the initial migration, the platform team must apply two post-migration steps:
1. Always Encrypted column encryption on `CellPhoneNumber` columns (TASK-061).
2. The audit log immutability DDL trigger from `src/TCPA.Api/Infrastructure/Data/Seeds/002_AuditLogImmutabilityTrigger.sql`.
3. The application registration seed from `src/TCPA.Api/Infrastructure/Data/Seeds/001_ApplicationRegistrationSeed.sql` (after replacing the placeholder values with production Cool Text account IDs).
