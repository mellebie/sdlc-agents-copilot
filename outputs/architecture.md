<!-- SDLC Pipeline Artifact
     Stage: 04-architecture
     Source PRD: inputs/prd.md
     PRD Sections: §1 Overview, §2 Personas, §3 Functional Requirements, §4 Non-Functional Requirements, §5 Constraints, §6 Out of Scope, §7 Success Metrics, §8 Assumptions, §9 Dependencies
     Generated: 2026-06-26
     Status: APPROVED — human approved proceeding despite open clarifications (2026-07-07)
-->

# Architecture — TCPA Regulatory Compliance for Text Messages

## System Overview

The TCPA API is a new standalone middleware service that sits between Southern Company Gas (SCG) upstream applications and the Cool Text/Twilio SMS platform. Its core function is a compliance gate: every outbound SMS from an in-scope application must pass through the TCPA API, which checks the destination cell number's opt-out status before deciding whether to forward or suppress the message. Inbound SMS replies from customers are received by the TCPA API from Cool Text and routed back to the appropriate originating application.

The system is designed around a fail-closed, immutable-audit-log architectural philosophy. Because TCPA non-compliance carries federal liability, the system errs on the side of blocking messages when opt-out status cannot be confirmed. Every compliance-relevant event — opt-out receipt, message block, re-opt-in action — generates an immutable audit record. The audit store is logically separate from the operational database to ensure retention integrity. The system must achieve 99.9% availability (≤ 43.8 minutes unplanned downtime per month) on a 24x7x365 basis.

The architecture follows a layered REST API monolith pattern for Phase 1. The scope (five integrated applications, moderate SMS volume at a utility scale, single-region SCG cloud footprint) does not justify microservices decomposition in the first release. The design uses internal service boundaries (bounded contexts) that can be extracted to separate services in Phase 2 if operational requirements demand it. All integration with upstream SCG applications is REST/JSON. BizTalk requires an adapter to translate from its native ESB protocol to REST — this is flagged as an integration risk and is the responsibility of the BizTalk team with TCPA API support.

---

## Component Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          SCG UPSTREAM APPLICATIONS                          │
│   BizTalk (ESB)   GCMA   KMI Active   ARM/Construction   CCB/My Account     │
│   [via REST Adapter]                                                        │
└───────────┬─────────┬─────────────────────────────────────────────────────┘
            │ Outbound SMS (REST/JSON)     ▲ Inbound Reply (webhook callback)
            ▼                             │
┌─────────────────────────────────────────────────────────────────────────────┐
│                            TCPA API SERVICE                                 │
│                                                                             │
│  ┌────────────────────┐   ┌──────────────────────────────────────────────┐  │
│  │  API Gateway /     │   │  Admin API (privileged)                      │  │
│  │  Inbound Router    │   │  • Re-Opt-In Update (PUT)                    │  │
│  │                    │   │  • Status Lookup (GET)                       │  │
│  │  • Auth middleware │   │  • Auth: SCG Identity Provider (RBAC)        │  │
│  │  • Input validation│   └──────────────────────────────────────────────┘  │
│  └──────┬─────────────┘                                                     │
│         │                                                                   │
│  ┌──────▼──────────────────────────────────────────────────────────────┐    │
│  │                     COMPLIANCE ENGINE                               │    │
│  │                                                                     │    │
│  │  ┌─────────────────┐  ┌──────────────────┐  ┌──────────────────┐   │    │
│  │  │ Outbound Proxy  │  │ Inbound Router   │  │ Re-Opt-In Svc    │   │    │
│  │  │ (SPEC-001, 006) │  │ (SPEC-002)       │  │ (SPEC-007)       │   │    │
│  │  └────────┬────────┘  └────────┬─────────┘  └──────────────────┘   │    │
│  │           │                   │                                     │    │
│  │  ┌────────▼────────┐  ┌────────▼─────────┐                         │    │
│  │  │ Opt-Out Status  │  │ Keyword Detector │                         │    │
│  │  │ Lookup          │  │ (SPEC-003)       │                         │    │
│  │  └────────┬────────┘  └────────┬─────────┘                         │    │
│  │           │                   │                                     │    │
│  │           │            ┌──────▼──────────┐                         │    │
│  │           │            │ Opt-Out Status  │                         │    │
│  │           │            │ Writer (SPEC-004│                         │    │
│  │           │            └──────┬──────────┘                         │    │
│  │           │                   │                                     │    │
│  │           │            ┌──────▼──────────┐                         │    │
│  │           │            │ Confirmation SMS│                         │    │
│  │           │            │ Dispatcher      │                         │    │
│  │           │            │ (SPEC-005)      │                         │    │
│  │           │            └─────────────────┘                         │    │
│  └──────┬────┴──────────────────────┬───────────────────────────────┘     │
│         │                           │                                      │
│  ┌──────▼───────────────────────────▼────────────────────────────────┐    │
│  │                     DATA LAYER                                    │    │
│  │                                                                   │    │
│  │  ┌─────────────────────┐  ┌────────────────────────────────────┐  │    │
│  │  │  Opt-Out Status DB  │  │  Audit Log Store                   │  │    │
│  │  │  (Operational)      │  │  (Immutable, 5-yr retention)       │  │    │
│  │  └─────────────────────┘  └────────────────────────────────────┘  │    │
│  │  ┌─────────────────────┐  ┌────────────────────────────────────┐  │    │
│  │  │  App Registry       │  │  Report / Analytics DB             │  │    │
│  │  │  (Configuration)    │  │  (Queryable, 5-yr retention)       │  │    │
│  │  └─────────────────────┘  └────────────────────────────────────┘  │    │
│  └───────────────────────────────────────────────────────────────────┘    │
│                                                                             │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │  SCHEDULER / BACKGROUND JOBS                                          │  │
│  │  • Weekly Compliance Report Generator (SPEC-013) — Monday 06:00 UTC  │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │  OBSERVABILITY                                                        │  │
│  │  • Structured JSON logging (SPEC-015) — production + debug streams   │  │
│  │  • Health check endpoint                                              │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
└─────────────────────┬──────────────────────────────────────────────────────┘
                      │ Outbound SMS forward / Inbound SMS webhook
                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                     COOL TEXT / TWILIO PLATFORM                             │
│  • Outbound SMS delivery to customer cell numbers                           │
│  • Inbound SMS webhook push to TCPA API                                     │
└─────────────────────────────────────────────────────────────────────────────┘
                      │ Email delivery
                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│              SCG SMTP / EMAIL RELAY                                         │
│  • Weekly compliance report distribution to Compliance Officers             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Components

### API Gateway / Inbound Router
- **Responsibility:** Receives all inbound HTTP requests (from upstream applications, from Cool Text webhooks, and from admin users), validates authentication tokens, validates request structure, routes to the appropriate internal service, and enforces TLS termination.
- **Owns Specs:** SPEC-001 (receive), SPEC-002 (receive), SPEC-007 (receive admin), SPEC-014 (application registry lookup), SPEC-015 (request logging)
- **Interfaces:** Exposes HTTPS REST/JSON endpoints. Consumes the App Registry to resolve application context from Cool Text account ID. Passes validated requests to Compliance Engine services.
- **Technology:** ASP.NET Core (C#) Web API — justified by SCG's existing .NET platform investment (BizTalk, GCMA run on .NET). Keeps operational skills on a consistent platform. Alternatively, a lightweight API gateway product (Kong, Azure API Management) could handle routing and auth as a separate layer; this is flagged as ADR-002.
- **Scaling approach:** Stateless — horizontally scalable behind a load balancer. No session affinity required. Each request carries full context in the payload.

---

### Compliance Engine
- **Responsibility:** Contains all business logic for opt-out enforcement, keyword detection, status reads and writes, and re-opt-in processing. This is the core of the TCPA API.
- **Owns Specs:** SPEC-001 (gate logic), SPEC-002 (inbound routing), SPEC-003 (keyword detection), SPEC-004 (opt-out status write), SPEC-005 (confirmation SMS dispatch), SPEC-006 (block enforcement), SPEC-007 (re-opt-in logic)
- **Interfaces:** Reads/writes Opt-Out Status DB. Writes to Audit Log Store. Calls Cool Text API for outbound forwarding and confirmation SMS. Calls registered application callback URLs for inbound reply forwarding.
- **Technology:** Internal service layer within the ASP.NET Core application — not a separate process in Phase 1. Clear internal interfaces (C# interfaces / service contracts) enable future extraction to a separate service.
- **Scaling approach:** Stateless business logic. Database is the scaling bottleneck; addressed via connection pooling and read replicas for the Opt-Out Status DB.

#### Compliance Engine Sub-services

**Outbound Proxy Service**
- Accepts outbound SMS requests from upstream apps.
- Resolves application from Cool Text account ID via App Registry.
- Performs opt-out status lookup (fail-closed: block on DB unavailability).
- Routes to Block Enforcement or Cool Text forwarding.
- Triggers Audit Log write (SPEC-009 for blocks, SPEC-011 data for forwards).

**Inbound Routing Service**
- Receives inbound webhook from Cool Text.
- Passes message body to Keyword Detector first.
- If opt-out keyword: hands off to Opt-Out Processing Pipeline.
- If non-opt-out: looks up application callback URL from App Registry; POSTs to callback with retry (3 attempts, exponential backoff).

**Opt-Out Processing Pipeline** (internal sequential flow)
1. Keyword Detector (SPEC-003): word-boundary regex matching.
2. Opt-Out Status Writer (SPEC-004): atomic write to Opt-Out Status DB.
3. Audit Log Writer (SPEC-008): immutable audit entry — must succeed; triggers alert on failure.
4. Confirmation SMS Dispatcher (SPEC-005): calls Cool Text API within 60-second SLA.

**Re-Opt-In Service**
- Authenticated endpoint for Help Desk / Compliance Officer roles.
- Reads current status (SPEC-007 GET).
- Writes OPT-IN status (SPEC-007 PUT), writes Audit Log (SPEC-010).

---

### Admin API
- **Responsibility:** Exposes the privileged re-opt-in and status-lookup endpoints to authenticated, authorized Help Desk and Compliance Officer users.
- **Owns Specs:** SPEC-007, SPEC-010
- **Interfaces:** Authenticated via SCG Identity Provider (OAuth 2.0 / OIDC with RBAC role claims). Returns JSON responses. All requests logged as security events.
- **Technology:** Part of the ASP.NET Core application, in a separate controller with its own auth policy. Admin endpoints are on a separate path prefix (`/admin/`) and may be network-restricted (e.g., accessible only from SCG internal network / VPN).
- **Scaling approach:** Very low traffic (manual Help Desk operations). No special scaling required.

---

### Application Registry
- **Responsibility:** Stores and provides lookups for the mapping between Cool Text account IDs and SCG applications (name, callback URL, active flag, onboard date). Determines which applications are subject to TCPA enforcement.
- **Owns Specs:** SPEC-014
- **Interfaces:** Read by Outbound Proxy Service and Inbound Routing Service at request time. Populated via configuration deployment (IT-managed, no runtime API in Phase 1).
- **Technology:** Database table within the Opt-Out Status DB, seeded at deployment. Values loaded into an in-memory cache at startup with TTL (e.g., 5 minutes) to avoid per-request DB lookups for a near-static dataset. Cache invalidation on service restart.
- **Scaling approach:** Near-static data; caching eliminates DB load. Cache refresh at startup or on a short TTL.

---

### Opt-Out Status Database
- **Responsibility:** Authoritative store of the current OPT-IN/OPT-OUT status for every cell number the system has processed. Also hosts the Application Registry table.
- **Owns Specs:** SPEC-004 (write), SPEC-006 (read), SPEC-007 (read/write), SPEC-014 (read)
- **Interfaces:** Accessed by Compliance Engine via parameterized queries only (no dynamic SQL). Read path is the critical path for outbound proxy compliance gate.
- **Technology:** Azure SQL Database (PaaS) — justified by SCG's Azure cloud footprint and .NET ecosystem alignment. Managed PaaS eliminates patching overhead. Azure SQL supports Always Encrypted for column-level encryption of cell phone numbers (satisfying NFS-007b). See ADR-003.
- **Scaling approach:** Active-passive read replica for read scale. Connection pooling in the application. The compliance gate read (SPEC-006) must be synchronous and low-latency (< 50ms p99 at expected load). Index on cell_number (hashed if encrypted at application layer, or using Azure SQL Always Encrypted with deterministic encryption to allow index lookup).

---

### Audit Log Store
- **Responsibility:** Immutable, append-only record of every compliance-relevant event: opt-out events (SPEC-008), blocked outbound attempts (SPEC-009), and re-opt-in events (SPEC-010). 5-year retention mandatory.
- **Owns Specs:** SPEC-008, SPEC-009, SPEC-010
- **Interfaces:** Append-only writes from Compliance Engine. Read by Reporting Service for on-demand queries (SPEC-011, SPEC-012) and weekly report (SPEC-013). No UPDATE or DELETE operations permitted.
- **Technology:** Azure SQL Database (separate logical database or schema from Operational DB) with row-level immutability enforced via database trigger (prevent UPDATE/DELETE on audit tables) and application-layer write-only repository pattern. Long-term retention via Azure Blob Storage tiering after 90 days hot → cool → archive at 2 years; audit records remain queryable from the SQL layer via an external table or ETL that populates a query-ready cold store. See ADR-004 for tiering decision.
- **Scaling approach:** Append-only write pattern is highly scalable. Read queries from reporting should be on the reporting DB (CQRS-lite: writes go to audit log, a projection is materialized into the reporting DB).

---

### Reporting Service
- **Responsibility:** Provides on-demand queryable data sets for Compliance Officers (SPEC-011, SPEC-012) and generates/sends the automated weekly compliance report (SPEC-013).
- **Owns Specs:** SPEC-011, SPEC-012, SPEC-013
- **Interfaces:** Reads from Report/Analytics DB (materialized from Audit Log). Sends email via SCG SMTP relay. Exposes authenticated REST query endpoints for on-demand report data.
- **Technology:** Part of the ASP.NET Core application. Scheduled report uses a hosted background service (IHostedService) or a separate scheduled job (Azure Functions Timer Trigger or Azure Logic Apps). See ADR-005 for scheduler choice.
- **Scaling approach:** Report generation is a batch read operation. Run against the reporting DB (not the live audit log) to avoid contention. Report generation for a 7-day window over utility-scale SMS volumes is not computationally intensive.

---

### Report / Analytics Database
- **Responsibility:** Queryable materialized view of the audit log, optimized for time-range queries by application, cell number, and event type. Populated by a near-real-time projection from the Audit Log Store.
- **Owns Specs:** SPEC-011, SPEC-012, SPEC-013 (data source)
- **Interfaces:** Read by Reporting Service. Written by Audit Log projection job (asynchronous, near-real-time).
- **Technology:** Azure SQL Database (same server, separate schema from Audit Log). For Phase 1, a simple scheduled projection (every 15 minutes) from Audit Log to Reporting schema is sufficient. If near-real-time reporting becomes a requirement in Phase 2, this can be converted to a change-feed pattern.
- **Scaling approach:** Read-only query workload. Indexed on event_timestamp, originating_application_name, and cell_number. Separate from operational DB to avoid query load impacting the compliance gate.

---

### Scheduler / Background Jobs
- **Responsibility:** Triggers the weekly compliance report generation (SPEC-013) every Monday at 06:00 UTC. Handles projection from Audit Log to Reporting DB.
- **Owns Specs:** SPEC-013
- **Interfaces:** Calls Reporting Service internals. Sends alert to IT on job failure.
- **Technology:** Azure Functions Timer Trigger — justification in ADR-005.
- **Scaling approach:** Single-instance scheduled jobs. Idempotent — re-running a failed report job produces the same output.

---

### Observability Component
- **Responsibility:** Structured JSON logging (production and debug), health check endpoint, correlation ID propagation across the request pipeline.
- **Owns Specs:** SPEC-015
- **Interfaces:** Log output to SCG log aggregation platform (e.g., Azure Monitor / Log Analytics). Health check at `GET /health`.
- **Technology:** Microsoft.Extensions.Logging with Serilog structured output sink. Correlation IDs via middleware (each inbound request gets a UUID correlation ID propagated through all log events).
- **Scaling approach:** Async log sinks; log writes must not block the compliance gate path.

---

## Data Model

### Entity: CellNumberOptOutStatus
| Field                  | Type         | Constraints                                 | Notes |
|------------------------|--------------|---------------------------------------------|-------|
| id                     | UUID         | Primary Key, system-generated               | |
| cell_number            | String(E.164)| Encrypted at rest (AES-256 / Always Encrypted); deterministic encryption to allow indexed lookup | PII — masked in logs |
| opt_out_status         | Enum         | OPT_IN \| OPT_OUT                           | Current authoritative status |
| last_opt_out_timestamp | DateTime UTC | Nullable; timestamp of most recent opt-out  | |
| last_opt_in_timestamp  | DateTime UTC | Nullable; timestamp of most recent re-opt-in| |
| created_at             | DateTime UTC | Set on first record creation                | |
| updated_at             | DateTime UTC | Updated on every status change              | |

**Relationships:**
- One CellNumberOptOutStatus per unique cell_number (unique index on cell_number).
- Referenced by AuditLogEntry via cell_number (not FK — audit log is independent of operational DB for resilience).

---

### Entity: AuditLogEntry
| Field                            | Type         | Constraints                                    | Notes |
|----------------------------------|--------------|------------------------------------------------|-------|
| record_id                        | UUID         | Primary Key, system-generated                  | |
| event_type                       | Enum         | OPT_OUT \| BLOCKED_OUTBOUND \| RE_OPT_IN       | |
| event_timestamp                  | DateTime UTC | NOT NULL; timestamp of triggering event        | Retention clock starts here |
| cell_number                      | String(E.164)| Encrypted at rest; deterministic for lookup    | PII |
| originating_cool_text_account_id | String       | NOT NULL                                       | |
| originating_application_name     | String       | NOT NULL                                       | Resolved from App Registry |
| opt_out_keyword_received         | String       | Nullable; populated for OPT_OUT events         | |
| message_body                     | String(TEXT) | Nullable; stored for OPT_OUT and BLOCKED_OUTBOUND | PII-adjacent; encrypted at rest |
| system_response                  | String       | NOT NULL                                       | e.g., "OPT_OUT_STATUS_WRITTEN" |
| confirmation_sms_sent            | Boolean      | Nullable; only for OPT_OUT events              | |
| confirmation_sms_timestamp       | DateTime UTC | Nullable                                       | |
| confirmation_sms_status          | Enum         | SENT \| FAILED \| NOT_SENT \| null             | |
| suppression_reason               | String       | Nullable; "OPT_OUT" for BLOCKED_OUTBOUND       | |
| agent_user_id                    | String       | Nullable; populated for RE_OPT_IN events       | Help Desk agent ID |
| reason                           | String       | Nullable; free text from agent for RE_OPT_IN   | |
| ticket_reference                 | String       | Nullable                                       | |
| previous_status                  | Enum         | OPT_IN \| OPT_OUT \| null                      | For RE_OPT_IN events |
| created_at                       | DateTime UTC | System-set at write time                       | Differs from event_timestamp |

**Immutability:** No UPDATE or DELETE permitted. Enforced by database trigger and application-layer write-only repository.

**Retention:** Records are retained for minimum 5 years from event_timestamp. Azure Blob Storage tiering applied after 90 days for cost management; records remain queryable.

**Relationships:**
- No FK to CellNumberOptOutStatus — audit log is independently resilient.

---

### Entity: ApplicationRegistry
| Field                | Type        | Constraints                              | Notes |
|----------------------|-------------|------------------------------------------|-------|
| id                   | UUID        | Primary Key                              | |
| cool_text_account_id | String      | Unique, NOT NULL                         | Lookup key |
| application_name     | String      | NOT NULL; e.g., "GCMA", "KMI", "ARM"    | Human-readable |
| callback_url         | String(URL) | NOT NULL; HTTPS only                     | Inbound reply forwarding |
| active               | Boolean     | NOT NULL; default true                   | Soft-disable without deletion |
| onboarded_date       | Date        | NOT NULL                                 | |
| created_at           | DateTime UTC| NOT NULL                                 | |
| updated_at           | DateTime UTC| NOT NULL                                 | |

**Relationships:**
- Looked up by cool_text_account_id on every inbound and outbound request.
- Cached in-memory at application startup with short TTL.

---

## API Contracts

### POST /api/v1/sms/outbound
**Receive and gate an outbound SMS request from an upstream application.**
- **Method:** POST
- **Path:** /api/v1/sms/outbound
- **Auth:** API Key (per-application, passed in `X-API-Key` header). See ADR-006 for authentication choice.
- **Request:**
  ```json
  {
    "cool_text_account_id": "string (required)",
    "destination_cell_number": "+12025551234 (E.164, required)",
    "message_body": "string (required, non-empty, max 1600 chars)",
    "originating_application_reference": "string (optional, caller reference for logging)"
  }
  ```
- **Response (200):**
  ```json
  {
    "status": "FORWARDED | SUPPRESSED | UNREGISTERED_ACCOUNT",
    "message_id": "cool-text-message-id or null",
    "suppression_reason": "OPT_OUT or null"
  }
  ```
- **Error Responses:**
  - `400 Bad Request` — missing required field or invalid E.164 number:
    ```json
    { "error": "VALIDATION_ERROR", "fields": ["destination_cell_number"] }
    ```
  - `401 Unauthorized` — missing or invalid API key
  - `503 Service Unavailable` — TCPA database unavailable (fail-closed):
    ```json
    { "error": "SERVICE_UNAVAILABLE", "message": "Compliance check unavailable; message not forwarded." }
    ```
  - `502 Bad Gateway` — Cool Text/Twilio unreachable after opt-in check passed
- **Owned by Component:** API Gateway / Inbound Router → Compliance Engine (Outbound Proxy Service)
- **Satisfies Specs:** SPEC-001, SPEC-006, SPEC-009

---

### POST /api/v1/sms/inbound (Cool Text Webhook)
**Receive an inbound SMS from a customer via Cool Text webhook push.**
- **Method:** POST
- **Path:** /api/v1/sms/inbound
- **Auth:** Webhook HMAC signature validation (Cool Text signs payloads; TCPA API verifies the signature). See ADR-007.
- **Request (from Cool Text):**
  ```json
  {
    "cool_text_account_id": "string (required)",
    "sender_cell_number": "+12025551234 (E.164, required)",
    "message_body": "string (required)",
    "cool_text_message_id": "string (required)"
  }
  ```
- **Response (200):**
  ```json
  { "received": true }
  ```
  *(Cool Text expects a 200 acknowledgement; all processing is asynchronous after acknowledgement.)*
- **Error Responses:**
  - `400 Bad Request` — malformed payload
  - `401 Unauthorized` — HMAC signature invalid
- **Owned by Component:** API Gateway / Inbound Router → Compliance Engine (Inbound Routing Service + Opt-Out Processing Pipeline)
- **Satisfies Specs:** SPEC-002, SPEC-003, SPEC-004, SPEC-005, SPEC-008

---

### PUT /admin/v1/opt-out/re-opt-in
**Manually re-opt-in a cell number (Help Desk / Compliance Officer only).**
- **Method:** PUT
- **Path:** /admin/v1/opt-out/re-opt-in
- **Auth:** Bearer token (SCG Identity Provider, OAuth 2.0 / OIDC). Required role claim: `tcpa.helpdesk` or `tcpa.compliance_officer`.
- **Request:**
  ```json
  {
    "cell_number": "+12025551234 (E.164, required)",
    "reason": "string (required, free text)",
    "ticket_reference": "string (optional)"
  }
  ```
- **Response (200):**
  ```json
  {
    "success": true,
    "previous_status": "OPT_OUT | OPT_IN",
    "new_status": "OPT_IN",
    "updated_timestamp": "2026-06-26T14:32:00Z",
    "record_id": "uuid"
  }
  ```
- **Error Responses:**
  - `400 Bad Request` — missing required field (reason missing, invalid cell number format)
  - `401 Unauthorized` — invalid or missing token
  - `403 Forbidden` — authenticated but role not authorized
  - `404 Not Found` — cell number has no record in the system
  - `409 Conflict` — cell number has no prior opt-out record (re-opt-in endpoint is for reversals only)
  - `503 Service Unavailable` — database unavailable
- **Owned by Component:** Admin API → Re-Opt-In Service
- **Satisfies Specs:** SPEC-007, SPEC-010

---

### GET /admin/v1/opt-out/status/{cell_number}
**Look up a cell number's current opt-out status (read-only).**
- **Method:** GET
- **Path:** /admin/v1/opt-out/status/{cell_number}
- **Auth:** Bearer token (same as above; required role claim: `tcpa.helpdesk` or `tcpa.compliance_officer`)
- **Path Parameter:** `cell_number` — E.164 format (URL-encoded)
- **Response (200):**
  ```json
  {
    "cell_number": "******1234 (masked — last 4 digits only)",
    "opt_out_status": "OPT_IN | OPT_OUT",
    "last_opt_out_timestamp": "2026-06-20T10:00:00Z or null",
    "last_opt_in_timestamp": "2026-06-25T14:00:00Z or null"
  }
  ```
- **Error Responses:**
  - `401 Unauthorized`, `403 Forbidden`
  - `404 Not Found` — no record for this cell number (implies OPT_IN by default; 404 communicates "no history")
- **Owned by Component:** Admin API → Re-Opt-In Service
- **Satisfies Specs:** SPEC-007

---

### GET /api/v1/reports/opted-in
**On-demand query: SMS forwarded to opted-in numbers (Compliance Officer access).**
- **Method:** GET
- **Path:** /api/v1/reports/opted-in
- **Auth:** Bearer token; required role claim: `tcpa.compliance_officer` or `tcpa.reporting`
- **Query Parameters:**
  - `date_from` (required): ISO 8601 date
  - `date_to` (required): ISO 8601 date
  - `application_filter` (optional): application name string
  - `cell_number_filter` (optional): E.164 cell number
- **Response (200):**
  ```json
  {
    "records": [
      {
        "status": "FORWARDED",
        "cell_number": "E.164",
        "originating_application_name": "GCMA",
        "message_timestamp": "ISO 8601 UTC",
        "message_body": "string",
        "cool_text_account_id": "string"
      }
    ],
    "total_count": 1234
  }
  ```
- **Error Responses:** `400` (invalid date range), `401`, `403`
- **Owned by Component:** Reporting Service
- **Satisfies Specs:** SPEC-011

---

### GET /api/v1/reports/opted-out
**On-demand query: SMS blocked to opted-out numbers (Compliance Officer access).**
- **Method:** GET
- **Path:** /api/v1/reports/opted-out
- **Auth:** Bearer token; required role claim: `tcpa.compliance_officer` or `tcpa.reporting`
- **Query Parameters:** Same as `/opted-in`
- **Response (200):**
  ```json
  {
    "records": [
      {
        "status": "BLOCKED",
        "cell_number": "E.164",
        "originating_application_name": "KMI",
        "attempt_timestamp": "ISO 8601 UTC",
        "message_body": "string",
        "suppression_reason": "OPT_OUT"
      }
    ],
    "total_count": 45
  }
  ```
- **Owned by Component:** Reporting Service
- **Satisfies Specs:** SPEC-012

---

### GET /health
**Health check endpoint for load balancer and external monitoring.**
- **Method:** GET
- **Path:** /health
- **Auth:** None
- **Response (200):**
  ```json
  {
    "status": "healthy",
    "checks": {
      "database": "ok | degraded",
      "cool_text_connectivity": "ok | degraded",
      "audit_log": "ok | degraded"
    },
    "timestamp": "ISO 8601 UTC"
  }
  ```
- **Response (503):** When any critical dependency is unavailable.
- **Owned by Component:** Observability Component
- **Satisfies Specs:** NFS-009 (monitoring prerequisite)

---

## Integration Points

| System                    | Direction         | Protocol             | Auth Method                           | Notes |
|---------------------------|-------------------|----------------------|---------------------------------------|-------|
| BizTalk (ESB)             | Inbound to TCPA   | REST/JSON via adapter| API Key (`X-API-Key`)                 | BizTalk requires a REST adapter; native BizTalk protocol is SOAP/ESB. Adapter is BizTalk team responsibility. See ARCH-RISK-001. |
| GCMA                      | Inbound to TCPA   | REST/JSON            | API Key (`X-API-Key`)                 | GCMA calls POST /api/v1/sms/outbound directly |
| KMI Active                | Inbound to TCPA   | REST/JSON            | API Key (`X-API-Key`)                 | |
| ARM / Construction Portal | Inbound to TCPA   | REST/JSON            | API Key (`X-API-Key`)                 | Already live; priority integration |
| CCB / My Account          | Inbound to TCPA   | REST/JSON            | API Key (`X-API-Key`)                 | Conditional on CCB go-live (ASM-004); active flag in registry controls enforcement |
| Cool Text Platform        | Bidirectional     | REST/JSON (outbound) + Webhook push (inbound) | Outbound: Cool Text API key. Inbound webhook: HMAC signature. | TCPA API registers a webhook endpoint with Cool Text for inbound message delivery |
| Twilio                    | Via Cool Text     | N/A — accessed via Cool Text abstraction | N/A | Twilio is the underlying carrier; Cool Text is the integration surface |
| SCG Identity Provider     | Inbound to TCPA   | OAuth 2.0 / OIDC     | JWT Bearer token with role claims     | Admin endpoint authentication only; specific IdP (Azure AD / SCG SSO) to be confirmed by IT Security |
| SCG SMTP / Email Relay    | Outbound from TCPA| SMTP (TLS)           | SMTP credentials (stored in Key Vault)| Weekly compliance report distribution |
| SCG Log Aggregation       | Outbound from TCPA| Platform SDK / sidecar| Managed identity / platform credentials| Azure Monitor / Log Analytics; structured JSON |

---

## Deployment Topology

```
┌────────────────────────────────────────────────────────────────────────┐
│                        SCG AZURE REGION (PRIMARY)                      │
│                                                                        │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │                   VIRTUAL NETWORK / VNET                          │  │
│  │                                                                   │  │
│  │  ┌─────────────────────────────────────┐                         │  │
│  │  │       APPLICATION TIER              │                         │  │
│  │  │  Azure App Service (or AKS pod)     │                         │  │
│  │  │  • TCPA API Service (2+ instances)  │                         │  │
│  │  │  • TLS termination at load balancer │                         │  │
│  │  │  • Scale: min 2, max auto           │                         │  │
│  │  └──────────────┬──────────────────────┘                         │  │
│  │                 │ Private endpoints                               │  │
│  │  ┌──────────────▼──────────────────────┐                         │  │
│  │  │        DATA TIER                    │                         │  │
│  │  │  Azure SQL Database (Business tier) │                         │  │
│  │  │  • Opt-Out Status DB               │                         │  │
│  │  │  • App Registry (same instance)    │                         │  │
│  │  │  • Audit Log DB (separate schema)  │                         │  │
│  │  │  • Reporting DB (separate schema)  │                         │  │
│  │  │  • Active-Passive replica for reads│                         │  │
│  │  │  Always Encrypted on cell_number   │                         │  │
│  │  └──────────────────────────────────┬─┘                         │  │
│  │                                     │ Tiering (>90 days)        │  │
│  │  ┌──────────────────────────────────▼─┐                         │  │
│  │  │  Azure Blob Storage (Archive tier) │                         │  │
│  │  │  • Audit log records > 90 days     │                         │  │
│  │  │  • 5-year lifecycle policy         │                         │  │
│  │  └────────────────────────────────────┘                         │  │
│  │                                                                   │  │
│  │  ┌─────────────────────────────────────┐                         │  │
│  │  │  SCHEDULER TIER                     │                         │  │
│  │  │  Azure Functions (Timer Trigger)    │                         │  │
│  │  │  • Weekly report job (Mon 06:00 UTC)│                         │  │
│  │  │  • Audit→Reporting DB projection    │                         │  │
│  │  └─────────────────────────────────────┘                         │  │
│  │                                                                   │  │
│  │  ┌─────────────────────────────────────┐                         │  │
│  │  │  SECRETS & CONFIGURATION            │                         │  │
│  │  │  Azure Key Vault                    │                         │  │
│  │  │  • DB connection strings            │                         │  │
│  │  │  • Cool Text API key                │                         │  │
│  │  │  • SMTP credentials                 │                         │  │
│  │  │  • Application API keys             │                         │  │
│  │  │  • Opt-out SMS message text         │                         │  │
│  │  └─────────────────────────────────────┘                         │  │
│  │                                                                   │  │
│  │  ┌─────────────────────────────────────┐                         │  │
│  │  │  MONITORING                         │                         │  │
│  │  │  Azure Monitor / Log Analytics      │                         │  │
│  │  │  Application Insights               │                         │  │
│  │  └─────────────────────────────────────┘                         │  │
│  └───────────────────────────────────────────────────────────────────┘  │
│                                                                        │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │  NETWORK BOUNDARY                                                 │  │
│  │  Azure Application Gateway / WAF (inbound from SCG apps)         │  │
│  │  Outbound via NAT Gateway → Cool Text / Twilio                   │  │
│  │  Admin endpoint restricted to SCG internal network (NSG rule)    │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────────────────────┘

Note: A secondary passive region (Azure paired region) should be evaluated
for the database to meet RTO/RPO requirements — see ARCH-RISK-002 and ADR-008.
The application tier itself is stateless and can be redeployed to a secondary
region rapidly; the database is the critical recovery asset.
```

---

## Architecture Decision Records

### ADR-001: Layered Monolith vs. Microservices
- **Status:** Accepted
- **Context:** The TCPA API covers 7 bounded contexts (SPEC-001 through SPEC-015) and integrates with 5 upstream applications and 1 SMS platform. Microservices would allow independent scaling and deployment of each bounded context.
- **Decision:** Deploy as a single layered monolith (modular monolith) with clear internal service boundaries in Phase 1.
- **Rationale:** (1) The system has a single team owner and a defined set of bounded contexts that are not independently scalable at utility-scale SMS volumes. (2) The compliance gate (opt-out status lookup) is a synchronous critical path — inter-service network hops introduce latency and failure surface that directly affects the 60-second SLA and fail-closed behavior. (3) The deployment timeline (January 31, 2027) favors delivery speed over operational flexibility. Internal service boundaries (C# interfaces, separate modules/namespaces) allow extraction to microservices in Phase 2 without a rewrite.
- **Alternatives Considered:**
  - Microservices: rejected — adds operational complexity (service mesh, distributed tracing, inter-service auth) that is disproportionate to the scale and delivery timeline.
  - Serverless (all Azure Functions): rejected — the compliance gate requires consistent low-latency synchronous reads; cold starts on serverless functions are incompatible with fail-closed 503 behavior under load.
- **Consequences:** All bounded contexts deploy together. A defect in any context requires a full application deployment to fix. Internal service boundaries must be maintained strictly to preserve the Phase 2 extraction path.

---

### ADR-002: API Gateway Product vs. In-Application Routing
- **Status:** Accepted
- **Context:** An API gateway product (Azure API Management, Kong) could handle authentication, rate limiting, and routing at the network edge, reducing the compliance application's surface area.
- **Decision:** Use in-application routing (ASP.NET Core middleware pipeline) for Phase 1, without a dedicated API gateway product.
- **Rationale:** (1) Adding Azure API Management (APIM) introduces cost, configuration complexity, and an additional operational dependency for a relatively small API surface. (2) Authentication (API key + OIDC) and input validation are straightforward to implement in ASP.NET Core middleware and keep all compliance logic in one deployable unit. (3) APIM can be added in Phase 2 if multi-tenant API management or advanced traffic shaping becomes a requirement.
- **Alternatives Considered:**
  - Azure API Management: considered — adds policy-based auth, rate limiting, and developer portal. Deferred to Phase 2.
  - Kong: rejected — additional infrastructure with no SCG precedent.
- **Consequences:** Rate limiting (GAP-004) is deferred or implemented in-application middleware. The TCPA API owns its own auth middleware, which must be kept current with SCG Identity Provider changes.

---

### ADR-003: Azure SQL Database for Opt-Out Status and Audit Log
- **Status:** Accepted
- **Context:** The opt-out status store requires fast, indexed reads (< 50ms p99) on the compliance gate path and encrypted storage of cell phone numbers. The audit log requires append-only writes with immutability and 5-year retention.
- **Decision:** Use Azure SQL Database (PaaS, Business Critical or General Purpose tier) for both the operational opt-out status store and the audit log. Cell phone numbers stored with Azure SQL Always Encrypted (deterministic encryption) to allow indexed lookups while keeping column values encrypted.
- **Rationale:** (1) Azure SQL is already within the SCG Azure footprint — aligns with existing platform skills and operational tooling. (2) Always Encrypted provides column-level encryption without application-layer key management complexity. (3) A relational model supports the compliance gate read (indexed point lookup by cell_number), the audit log append, and reporting queries in a single platform with proven ACID guarantees. (4) Azure SQL managed PaaS eliminates patching and infrastructure management.
- **Alternatives Considered:**
  - Azure Cosmos DB: considered for audit log (append-only, global distribution) — rejected because relational query patterns for compliance reports (date ranges, joins, aggregations) are significantly more complex in a document store.
  - Azure Table Storage: rejected — insufficient query flexibility for compliance reporting.
  - PostgreSQL (Azure Database for PostgreSQL): viable alternative with similar capability. Rejected because ASP.NET Core + Azure SQL is the existing SCG platform standard.
- **Consequences:** Schema migrations must be managed carefully — changing encrypted column structure requires re-encryption of existing data. Always Encrypted has limitations (e.g., no server-side range queries on deterministically encrypted columns; queries must pass the plaintext value from the client). This is acceptable for the point-lookup access pattern on cell_number.

---

### ADR-004: Audit Log Immutability Approach
- **Status:** Accepted
- **Context:** SPEC-008, SPEC-009, SPEC-010 require immutable audit records. The database is mutable by default. Regulatory compliance requires that audit records cannot be altered or deleted.
- **Decision:** Enforce immutability at two layers: (1) database-level: DDL trigger on audit log tables that raises an error on any UPDATE or DELETE attempt; (2) application-level: write-only repository pattern (no Update or Delete methods exposed for audit log entities). After 90 days, records are archived to Azure Blob Storage immutable storage (WORM — Write Once Read Many via Azure Blob Storage immutability policy). SQL records remain in the queryable reporting DB projection indefinitely (within 5-year retention window).
- **Rationale:** Defense-in-depth immutability at multiple layers. Azure Blob WORM storage provides a legally defensible immutable archive that cannot be altered even by administrators.
- **Alternatives Considered:**
  - Database-only immutability: insufficient — database administrators could disable triggers. Azure Blob WORM is the authoritative compliance archive.
  - Blockchain/distributed ledger: over-engineered for this use case. Azure Blob WORM with audit trail satisfies the regulatory requirement.
- **Consequences:** Querying archived records (> 90 days, in Blob storage) requires a separate query path (Azure Data Factory export or external table bridge). This should be documented in the operations guide.

---

### ADR-005: Weekly Report Scheduler — Azure Functions Timer Trigger
- **Status:** Accepted
- **Context:** The weekly compliance report must run automatically every Monday at 06:00 UTC without manual intervention. The scheduler must be reliable — a missed report is a compliance visibility gap.
- **Decision:** Use Azure Functions Timer Trigger for the weekly report job. The function is triggered by a cron expression, queries the Reporting DB, generates the HTML + CSV report, and sends via SMTP.
- **Rationale:** (1) Azure Functions Timer Trigger is a managed, serverless scheduler with built-in retry and monitoring integration. (2) The job is not latency-sensitive (batch report generation). Cold start is acceptable for a scheduled batch job. (3) Azure Functions integrates natively with Azure Monitor for failure alerting.
- **Alternatives Considered:**
  - IHostedService (in-application background service): viable — no cold start, but couples the scheduler lifecycle to the application process. A deployment during the report window could interrupt the job. Functions isolates the scheduler.
  - Azure Logic Apps: considered — low-code scheduler/workflow tool. Rejected because the report logic (SQL queries, CSV generation, email formatting) requires code that is cleaner in C# than in Logic Apps expressions.
  - Azure Kubernetes CronJob: over-engineered for a single scheduled job in Phase 1.
- **Consequences:** The Azure Function has a dependency on the Reporting DB and SMTP relay. Its failure mode must generate an alert to IT (per BR-060). The Function app must be deployed alongside the main application in the CI/CD pipeline.

---

### ADR-006: Upstream Application Authentication — API Key
- **Status:** Accepted
- **Context:** Upstream applications (BizTalk, GCMA, KMI, ARM, CCB) must authenticate to the TCPA API when submitting outbound SMS requests. The authentication model must be practical for server-to-server integration from SCG enterprise applications, some of which are older systems.
- **Decision:** Use per-application API keys passed in the `X-API-Key` HTTP header. Each registered application has a unique API key stored in Azure Key Vault and configured in the calling application's integration. API keys are rotatable without code changes.
- **Rationale:** (1) API keys are the simplest integration model for server-to-server calls and are universally supported, including by BizTalk. (2) OAuth 2.0 client credentials flow would require each upstream application to implement token acquisition and refresh — adding integration complexity to legacy systems (BizTalk). (3) Per-application keys allow per-application revocation without affecting other integrations. (4) API keys are sufficient for the threat model: the TCPA API endpoint is internal-network-only (not public internet); keys are rotatable on demand.
- **Alternatives Considered:**
  - OAuth 2.0 client credentials: more secure (short-lived tokens), but requires token endpoint integration from all 5 upstream applications. Impractical for BizTalk and other legacy systems in Phase 1. Recommended for Phase 2.
  - Mutual TLS (mTLS): strong authentication but requires certificate management on all upstream applications. Higher operational overhead than justified for internal-network communication.
- **Consequences:** API keys are long-lived credentials — rotation discipline is required. Keys must never appear in logs (BR-070). Key rotation procedure must be documented in the operations guide. Upgrade to OAuth 2.0 in Phase 2 should be planned.

---

### ADR-007: Cool Text Inbound Webhook Authentication — HMAC Signature
- **Status:** Accepted
- **Context:** Cool Text pushes inbound SMS messages to the TCPA API via webhook. The TCPA API must verify that inbound webhook calls are genuinely from Cool Text and not from an attacker spoofing the endpoint.
- **Decision:** Require HMAC-SHA256 signature validation on all inbound webhook calls. Cool Text signs the payload with a shared secret; the TCPA API rejects any request where the signature does not match. The shared secret is stored in Azure Key Vault.
- **Rationale:** (1) HMAC signature validation is the industry-standard approach for webhook security (used by Twilio, Stripe, GitHub, etc.). (2) It prevents payload spoofing without requiring a TLS client certificate (which Cool Text may not support). (3) It protects the inbound opt-out detection pipeline from injection of malicious opt-out keywords by an attacker.
- **Alternatives Considered:**
  - IP allowlisting only: insufficient — IP ranges can be spoofed or may change with Cool Text infrastructure changes.
  - No authentication on webhook endpoint: unacceptable — allows an attacker to inject fake opt-out keywords and suppress legitimate SMS delivery.
- **Consequences:** The Cool Text shared secret must be confirmed with the Cool Text vendor during integration testing. If Cool Text does not support HMAC (to be verified), fall back to webhook endpoint authentication via a secret token in the URL path or header (less preferred).

---

### ADR-008: Single-Region vs. Multi-Region Deployment
- **Status:** Proposed (requires human decision — see Open Questions)
- **Context:** The 99.9% availability SLA requires ≤ 43.8 minutes unplanned downtime per month. A single-region deployment with Azure Zone Redundancy (availability zones) can achieve this SLA for most failure scenarios. Geographic failover to a secondary region provides protection against full-region outages.
- **Decision:** Phase 1: Deploy to a single Azure region with availability zone redundancy for the application tier and Azure SQL Database (zone-redundant deployment). Phase 2: Evaluate active-passive secondary region for the database based on observed failure patterns.
- **Rationale:** (1) Azure SQL Database with zone redundancy (Business Critical tier) achieves 99.99% availability within a region. (2) A full Azure region outage is rare and would affect all SCG Azure workloads simultaneously. (3) Cross-region database replication introduces complexity (replication lag, failover orchestration) that is disproportionate to Phase 1 scope. (4) If the regulatory risk assessment (ARCH-RISK-002) determines that cross-region failover is required, this decision is revisited in Phase 2.
- **Alternatives Considered:**
  - Active-active multi-region: excessive complexity and cost for utility-scale SMS volume and a single-region SCG Azure footprint.
  - No availability zone redundancy (single AZ): insufficient — AZ redundancy should be the baseline for a regulatory compliance system.
- **Consequences:** The system can withstand availability zone failures within the primary region. A full region outage would exceed the 99.9% SLA. This is an accepted risk for Phase 1 (documented in ARCH-RISK-002). Disaster recovery procedures must be documented.

---

## NFR Fulfillment

| NFS-ID  | Requirement                                    | Architectural Response |
|---------|------------------------------------------------|------------------------|
| NFS-001 | Opt-out confirmation SMS ≤ 60 seconds          | The Opt-Out Processing Pipeline is synchronous and in-process within the application tier. The webhook acknowledgement (200 OK to Cool Text) is returned immediately; opt-out processing occurs in a fast async pipeline. Measured p99 from webhook receipt to Cool Text API call must be ≤ 60 seconds. Instrumented latency metric with alert if p99 > 45 seconds (early warning at 75% of SLA). Database write + Cool Text API call are the two latency contributors; both are monitored separately. |
| NFS-002 | Opt-out enforcement immediate after status write | The outbound proxy (SPEC-001) reads opt-out status directly from the Opt-Out Status DB at request time. No caching of opt-out status in the compliance gate path — this is a deliberate architectural decision to ensure immediate enforcement. The read replica is acceptable for the opt-out check because replication lag is negligible (< 1 second) compared to the enforcement window. If the replica is lagged, reads fall back to the primary automatically. |
| NFS-003 | Live in production by January 31, 2027         | Not an architectural decision — a delivery timeline constraint. Architecture is scoped to be implementable within this window. BizTalk adapter (ARCH-RISK-001) is the highest delivery risk. Integration testing with all 5 applications must begin no later than Q3 2026. |
| NFS-004 | Audit log 5-year retention, queryable           | Audit log records retained in Azure SQL for 90 days (hot, fully queryable), then tiered to Azure Blob Storage WORM (cool/archive tier, queryable via external table or data export). Lifecycle policy enforced at the storage account level. 5-year retention policy set on both SQL (no delete permission) and Blob (immutability policy). |
| NFS-005 | Fail-closed when TCPA database unavailable      | The Outbound Proxy Service catches database exceptions and, in all cases where opt-out status cannot be confirmed, returns 503 to the calling application and does not call Cool Text. This is implemented as the default catch-all error handler on the compliance gate path — no path through the code forwards a message without a successful DB read. |
| NFS-006 | Opt-out processing latency ≤ 5 seconds p99     | The opt-out processing pipeline (webhook receipt → keyword detection → DB write) is synchronous and in-process. Expected latency: keyword detection ~1ms, DB write ~20-50ms on Azure SQL. The 5-second target provides 100x headroom over expected latency. Alert threshold set at 2 seconds p99 to detect database performance degradation early. |
| NFS-007 | PII protection in transit and at rest           | (a) TLS 1.2+ enforced at the Azure Application Gateway (TLS policy `AppGwSslPolicy20220101` or equivalent, disabling TLS 1.0/1.1). (b) Cell phone numbers stored with Azure SQL Always Encrypted (AES-256, deterministic encryption for indexed lookup). Message body content encrypted at rest via Azure SQL Transparent Data Encryption (TDE) — column-level encryption evaluated but TDE is the baseline. (c) Cell phone numbers masked in all log output (last 4 digits only, applied in the logging middleware before any log event is emitted). |
| NFS-008 | 100% audit log completeness, 0 silent failures  | Audit log writes are part of the same transaction as the opt-out status write where technically feasible (same DB, same transaction scope). If the audit log write fails, the exception is caught, a critical alert is emitted to Azure Monitor (which pages on-call IT), and the failure is logged to the operational fallback log channel. The opt-out status is not rolled back — the status is preserved even if the audit write fails. The alert ensures IT can manually reconstruct the audit record if needed. |
| NFS-009 | 99.9% availability (≤ 43.8 min/month)          | Deployment on Azure App Service with availability zone redundancy (minimum 2 instances, auto-scaling). Azure SQL Database Business Critical tier with zone redundancy (99.99% SLA). Azure Application Gateway with zone redundancy. External health-check probe at ≤ 1-minute intervals via Azure Monitor. Planned maintenance windows communicated in advance and excluded from SLA calculation. |
| NFS-010 | Structured logs within 5 min, debug togglable  | Serilog structured JSON output sink to Azure Log Analytics. Log Analytics ingestion latency is typically < 2 minutes. Debug log level controlled by Azure App Configuration feature flag (no service restart required — dynamically reloaded at runtime). Production log stream and debug log stream separated by log level and log category filters. |

---

## Architectural Risks

| ID            | Risk                                                                                 | Likelihood | Impact   | Mitigation |
|---------------|--------------------------------------------------------------------------------------|------------|----------|------------|
| ARCH-RISK-001 | BizTalk cannot call REST natively; requires a custom adapter. Adapter development is the BizTalk team's responsibility and is on the critical path for compliance deadline. | High       | Critical | Confirm BizTalk REST capability in Week 1 of development. If a custom adapter is needed, initiate that work immediately as a parallel workstream. TCPA API provides a documented REST contract by end of Sprint 1. Adapter integration test slot reserved in the test schedule. |
| ARCH-RISK-002 | Single-region deployment cannot protect against full Azure region outage, which would exceed the 99.9% SLA and prevent opt-out processing during an outage. | Low        | High     | Availability zone redundancy mitigates intra-region failures. If cross-region failover is required by Legal/Compliance, add Azure SQL geo-replication and App Service deployment to secondary region. Evaluate in Phase 1 architecture review. Documented as an accepted risk if single-region is approved. |
| ARCH-RISK-003 | Azure SQL Always Encrypted (deterministic) has constraints: no server-side range queries on encrypted columns. Reporting queries that filter on cell_number directly must pass the plaintext value from the application. Compliance Officer ad-hoc queries may not be possible without decryption capability in the query tool. | Medium     | Medium   | Reporting queries that need to filter by cell number are issued from the application layer (which has the encryption key), not from direct DB access. Compliance Officers use the API reporting endpoints, not direct SQL access. Document constraint in operations guide. |
| ARCH-RISK-004 | Cool Text webhook authentication mechanism (HMAC) must be confirmed with the Cool Text vendor. If Cool Text does not support HMAC or uses a non-standard signing mechanism, the inbound webhook security model must be revised. | Medium     | High     | Confirm Cool Text webhook security capabilities in Week 1 of development during vendor integration kickoff. Define fallback authentication (secret URL token) if HMAC is not available. |
| ARCH-RISK-005 | The 60-second opt-out confirmation SLA (NFS-001) is a hard compliance requirement. Any latency degradation in the database (SPEC-004 write) or Cool Text API (SPEC-005 call) can cause SLA breaches during peak load. | Medium     | High     | Monitor p99 latency for the opt-out pipeline end-to-end. Alert threshold at 45 seconds (75% of SLA) to provide response time. Database write latency should be < 50ms; Cool Text API call should be < 2 seconds under normal conditions. Load test the pipeline before go-live. |
| ARCH-RISK-006 | CCB/My Account integration status is conditional (ASM-004). If CCB goes live after the TCPA API is deployed but before the TCPA API integration is complete, CCB would be sending unprotected SMS. | Medium     | Critical | The Application Registry active flag allows CCB to be registered but inactive. CCB SMS is not protected until the active flag is set. Coordinate CCB go-live and TCPA active-flag enablement with the CCB team. Document the CCB go-live dependency as a hard gate — do not enable CCB in production until TCPA integration is tested end-to-end. |
| ARCH-RISK-007 | The opt-out confirmation SMS message text (SPEC-005) requires Legal/Compliance approval (CQ-002, still open). If Legal approval is delayed, the confirmation SMS cannot be deployed compliant. | High       | High     | The message text is stored in Azure Key Vault / configuration, not hardcoded. A placeholder can be deployed and updated without a code release once Legal approves. Legal approval process should begin immediately. |
| ARCH-RISK-008 | No disaster recovery (DR) requirements are defined for the opt-out status database (GAP-002). Loss of the opt-out status database could result in opted-out customers receiving SMS or in re-opted-in customers being incorrectly blocked. | Medium     | Critical | Define RPO and RTO targets with IT and Legal (CQ-015). Implement Azure SQL automated backups (Point-in-Time Restore, minimum 7-day retention). For the compliance deadline, confirm whether daily backup + 4-hour RTO is sufficient or whether geo-redundant backup is required. |

---

## Open Questions for Human Review

1. **BizTalk REST Adapter (ARCH-RISK-001):** Has the BizTalk team confirmed that a REST adapter can be developed and tested within the delivery timeline? If not, what is the fallback (SOAP endpoint on the TCPA API)?

2. **Cross-Region DR (ADR-008 / ARCH-RISK-002):** Does Legal/Compliance require cross-region database failover to meet TCPA compliance obligations, or is single-region with availability zone redundancy acceptable for Phase 1? This decision gates infrastructure cost and complexity.

3. **SCG Identity Provider for Admin Endpoint (SPEC-007):** Which SCG Identity Provider should the Admin API authenticate against? (Azure Active Directory / Entra ID assumed — confirm with IT Security.) What RBAC roles exist today, and do `tcpa.helpdesk` and `tcpa.compliance_officer` need to be provisioned?

4. **Cool Text Webhook Mechanism (ADR-007):** What is the Cool Text webhook signing mechanism? Does Cool Text support HMAC-SHA256 payload signing? What is the inbound webhook payload schema? This must be confirmed with the Cool Text vendor before the inbound routing architecture can be finalized.

5. **Database Tier for Production (ADR-003):** Should the Azure SQL Database be deployed at General Purpose tier (cost-optimized) or Business Critical tier (higher availability, local SSD, 99.99% SLA)? Business Critical is recommended given the 99.9% uptime SLA; confirm with IT on budget.

6. **Opt-Out Confirmation SMS Text (ARCH-RISK-007):** Legal/Compliance approval for the confirmation SMS message text (CQ-002) is blocking the SPEC-005 implementation. When will approved text be available?

7. **Disaster Recovery RTO/RPO (ARCH-RISK-008 / CQ-015):** What are the required Recovery Time Objective (RTO) and Recovery Point Objective (RPO) for the opt-out status database? This determines whether Azure SQL's built-in PITR is sufficient or whether geo-redundant backups are required.

8. **Report Distribution List (CQ-004):** The weekly compliance report requires a specific email distribution list for Compliance Officers. What is the distribution list address? This must be in configuration before the report scheduler goes live.
