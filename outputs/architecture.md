<!-- SDLC Pipeline Artifact
     Stage: 04-architecture
     Source PRD: inputs/prd.md
     PRD Sections: All
     Generated: 2026-07-23
     Status: APPROVED
-->

# Architecture — TCPA Regulatory Compliance API

## System Overview

The TCPA Regulatory Compliance API is a centralized SMS compliance enforcement service built as a set of ASP.NET Core services running on IIS, backed by SQL Server and Apache Kafka. It sits between Southern Company Gas applications and the Cool Text / Twilio messaging providers, intercepting all outbound SMS traffic and handling all inbound customer replies.

The system has two primary message flows. The **outbound flow** receives SMS send requests from Gas applications via the Gravitee API gateway, performs a synchronous queue-time opt-out check, and — if the destination is opted-in — publishes the message to a Kafka topic for async dispatch to Cool Text / Twilio with a final send-time safety check. The **inbound flow** receives customer replies via a Cool Text / Twilio webhook, classifies them as opt-out requests or general replies, and either triggers the opt-out processing chain or forwards the message to the originating application.

The architecture favours simplicity and operational familiarity (IIS, SQL Server, Kafka — all existing at Southern) over distributed complexity. The single [COMPLEX] spec (SPEC-006, fail-safe availability) is addressed by IIS-level load balancing and Kafka's own durability guarantees rather than introducing an additional queuing layer between Gas apps and the TCPA API.

## Component Diagram

```
Gas Applications (BizTalk, GCMA, KMI, ARM)
         │
         │ HTTPS (outbound message submission)
         ▼
  [Gravitee API Gateway]  ◄── API key validation via existing auth service
         │
         │ HTTPS
         ▼
  [TCPA.Api]  ──────────────────────────────────────────────────┐
   ASP.NET Core on IIS                                           │
   - Queue-time opt-out check                                    │
   - Admin re-opt-in endpoint                                    │
   - Inbound webhook endpoint (from Cool Text / Twilio)          │
         │                                                       │
         │ Kafka produce                                         │ SQL Server read
         ▼                                                       ▼
  ┌─────────────────────────────────────┐           [SQL Server]
  │         Apache Kafka                │            - tcpa_opt_out_status
  │  Topics:                            │            - tcpa_audit_log
  │   inbound-messages                  │            - tcpa_cooltext_accounts
  │   outbound-messages                 │            - tcpa_config
  └───────────────┬─────────────────────┘
                  │
        ┌─────────┴──────────┐
        │                    │
        ▼                    ▼
[TCPA.MessageProcessor]  [TCPA.OutboundDispatcher]
 .NET Worker Service      .NET Worker Service
 - Keyword detection      - Send-time opt-out check
 - Opt-out processing     - Dispatch to Cool Text/Twilio
 - Confirmation send      - Suppression logging
 - General reply forward
        │                    │
        │                    │ HTTPS
        ▼                    ▼
  [SQL Server]        [Cool Text / Twilio]
  (audit writes)
        │
        ▼
[TCPA.ReportService]
 .NET Worker Service (Windows Task)
 - Weekly report generation
 - Email delivery via SMTP relay
```

## Components

### TCPA.Api
- **Responsibility:** HTTP boundary for all external callers — Gas applications (outbound), Cool Text/Twilio (inbound webhook), Help Desk (admin re-opt-in). Performs synchronous validation and queue-time opt-out check. Publishes to Kafka. Returns immediate responses.
- **Owns Specs:** SPEC-001, SPEC-006, SPEC-007, SPEC-011
- **Interfaces:** Consumes existing auth service (API key validation). Reads SQL Server (opt-out status for queue-time check, Cool Text account registry). Publishes to Kafka topics `inbound-messages` and `outbound-messages`.
- **Technology:** ASP.NET Core 8 Web API, deployed to IIS. OpenAPI (Swagger) spec generated from code. Serilog structured logging.
- **Scaling approach:** Multiple IIS nodes behind Windows Network Load Balancing (NLB) or Application Request Routing (ARR). Stateless — all state in SQL Server and Kafka.

### TCPA.MessageProcessor
- **Responsibility:** Processes inbound customer SMS messages consumed from Kafka. Performs keyword detection, writes opt-out status, sends confirmation, forwards general replies to Gas application callbacks.
- **Owns Specs:** SPEC-002, SPEC-003, SPEC-004, SPEC-005, SPEC-010 (opt-out and confirmation audit writes)
- **Interfaces:** Consumes `inbound-messages` Kafka topic. Reads SQL Server (Cool Text account registry, config store). Writes SQL Server (opt-out status, audit log). Calls Cool Text / Twilio API (confirmation dispatch). Calls Gas application callback URLs (general reply forwarding).
- **Technology:** .NET 8 Worker Service, deployed as Windows Service.
- **Scaling approach:** Multiple consumer instances in the same Kafka consumer group — Kafka partitioning distributes load. Stateless processing — idempotency via Kafka message ID.

### TCPA.OutboundDispatcher
- **Responsibility:** Consumes queued outbound messages from Kafka, performs send-time opt-out check, dispatches to Cool Text / Twilio, logs suppression or dispatch outcome.
- **Owns Specs:** SPEC-008, SPEC-010 (dispatch and suppression audit writes)
- **Interfaces:** Consumes `outbound-messages` Kafka topic. Reads SQL Server (opt-out status). Writes SQL Server (audit log). Calls Cool Text / Twilio API (message dispatch).
- **Technology:** .NET 8 Worker Service, deployed as Windows Service.
- **Scaling approach:** Multiple consumer instances. Kafka consumer group balances partitions across instances.

### TCPA.ReportService
- **Responsibility:** Generates three weekly reports (opted-in volume, opted-out volume, compliance) and delivers by email to configured distribution lists.
- **Owns Specs:** SPEC-012, SPEC-013, SPEC-014
- **Interfaces:** Reads SQL Server (audit log, dispatch log). Reads SQL Server (config store for recipient lists). Sends email via SMTP relay.
- **Technology:** .NET 8 Worker Service or Windows Task Scheduler invoking a .NET console application. Runs Monday 06:00 US Eastern.
- **Scaling approach:** Single instance — weekly batch job, not latency-sensitive.

### SQL Server Database
- **Responsibility:** Persistent state for all compliance data — opt-out status, audit log, Cool Text account registry, system configuration.
- **Owns Specs:** SPEC-009, SPEC-010, SPEC-015, SPEC-016
- **Technology:** SQL Server (existing Southern standard). AlwaysOn Availability Groups for HA.
- **Scaling approach:** Read replicas for report queries to avoid contention with real-time opt-out checks.

---

## Data Model

### Entity: OptOutStatus
| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| phone_number | nvarchar(20) | PK, E.164 | Indexed |
| status | nvarchar(20) | 'opted-out' or 'opted-in' | |
| effective_at | datetimeoffset | NOT NULL | UTC |
| last_event_id | uniqueidentifier | FK → AuditLog | |
| updated_at | datetimeoffset | NOT NULL | UTC |

### Entity: AuditLog
| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| audit_id | uniqueidentifier | PK | |
| event_type | nvarchar(50) | NOT NULL | Enum — see SPEC-010 |
| phone_number | nvarchar(20) | NOT NULL, indexed | |
| occurred_at | datetimeoffset | NOT NULL | UTC |
| application_id | nvarchar(100) | nullable | |
| message_id | uniqueidentifier | nullable | |
| details | nvarchar(max) | JSON | Event-specific payload |

### Entity: CoolTextAccount
| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| cooltext_account_number | nvarchar(50) | PK | |
| application_id | nvarchar(100) | NOT NULL, UNIQUE | 1:1 mapping |
| application_name | nvarchar(200) | NOT NULL | |
| callback_url | nvarchar(500) | NOT NULL | HTTPS |
| is_active | bit | NOT NULL, DEFAULT 1 | |
| created_at | datetimeoffset | NOT NULL | UTC |
| updated_at | datetimeoffset | NOT NULL | UTC |

### Entity: SystemConfig
| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| config_key | nvarchar(200) | PK | |
| config_value | nvarchar(max) | NOT NULL | |
| updated_at | datetimeoffset | NOT NULL | UTC |
| updated_by | nvarchar(200) | NOT NULL | |

**Relationships:**
- OptOutStatus 1──1 (latest) AuditLog via last_event_id
- CoolTextAccount 1──* AuditLog via application_id

---

## API Contracts

### POST /webhook/inbound
- **Method:** POST
- **Path:** /webhook/inbound
- **Auth:** X-Api-Key header (validated against auth service)
- **Description:** Webhook endpoint registered with Cool Text / Twilio. Receives inbound customer SMS.
- **Request:**
  ```json
  {
    "from": "+14045551234",
    "to": "+18005559876",
    "body": "STOP",
    "provider": "cooltext",
    "messageId": "ct-abc-123",
    "timestamp": "2026-07-23T11:00:00Z"
  }
  ```
- **Response (200):**
  ```json
  {
    "status": "received",
    "internalId": "550e8400-e29b-41d4-a716-446655440000"
  }
  ```
- **Error Responses:** 400 (invalid payload), 401 (invalid API key), 500 (internal error)
- **Owned by Component:** TCPA.Api
- **Satisfies Specs:** SPEC-001

### POST /api/v1/messages/outbound
- **Method:** POST
- **Path:** /api/v1/messages/outbound
- **Auth:** X-Api-Key header (validated against auth service)
- **Description:** Gas applications submit outbound SMS through this endpoint. Queue-time opt-out check performed synchronously before queuing.
- **Request:**
  ```json
  {
    "toNumber": "+14045551234",
    "body": "Your account balance is due.",
    "coolTextAccountNumber": "CT-BIZTALK-001",
    "applicationId": "biztalk",
    "correlationId": "app-generated-idempotency-key"
  }
  ```
- **Response (200 — queued):**
  ```json
  {
    "status": "queued",
    "messageId": "550e8400-e29b-41d4-a716-446655440001",
    "queuedAt": "2026-07-23T11:00:00Z"
  }
  ```
- **Response (200 — suppressed):**
  ```json
  {
    "status": "suppressed",
    "suppressionReason": "opted-out",
    "messageId": null
  }
  ```
- **Error Responses:** 400 (validation failure or unregistered account), 401 (invalid API key), 503 (TCPA API unavailable — caller must block send)
- **Owned by Component:** TCPA.Api
- **Satisfies Specs:** SPEC-006, SPEC-007

### POST /api/v1/admin/reopt-in
- **Method:** POST
- **Path:** /api/v1/admin/reopt-in
- **Auth:** X-Api-Key header (admin-scoped key, rate-limited to 10 req/min per key)
- **Description:** Help Desk agent manually re-opts-in a customer who previously sent a STOP keyword.
- **Request:**
  ```json
  {
    "phoneNumber": "+14045551234",
    "reason": "Customer called Help Desk and confirmed re-opt-in verbally. Ticket #45678.",
    "agentId": "hdagent-jsmith"
  }
  ```
- **Response (200):**
  ```json
  {
    "reOptInId": "550e8400-e29b-41d4-a716-446655440002",
    "phoneNumber": "+14045551234",
    "status": "opted-in",
    "effectiveAt": "2026-07-23T11:00:00Z"
  }
  ```
- **Error Responses:** 400 (missing required field), 401 (invalid key), 429 (rate limit — Retry-After: 60), 500 (atomic write failure)
- **Owned by Component:** TCPA.Api
- **Satisfies Specs:** SPEC-011

### GET /api/v1/health
- **Method:** GET
- **Path:** /api/v1/health
- **Auth:** None (internal monitoring only — restrict at network level)
- **Description:** Health check for load balancer and monitoring.
- **Response (200):**
  ```json
  {
    "status": "healthy",
    "checks": {
      "database": "ok",
      "kafka": "ok",
      "authService": "ok"
    },
    "timestamp": "2026-07-23T11:00:00Z"
  }
  ```
- **Response (503):** Any dependency unhealthy — load balancer removes node from rotation.
- **Owned by Component:** TCPA.Api
- **Satisfies Specs:** NFS-005 (availability monitoring)

---

## Integration Points

| System | Direction | Protocol | Auth Method | Notes |
|--------|-----------|----------|-------------|-------|
| Gas Applications (BizTalk, GCMA, KMI, ARM) | Inbound to TCPA.Api | HTTPS REST | API key via Gravitee | Apps call TCPA API instead of Cool Text directly |
| Cool Text | Inbound webhook (customer replies) | HTTPS POST | API key | Cool Text pushes inbound SMS to /webhook/inbound |
| Cool Text | Outbound (send SMS) | HTTPS REST | Cool Text account credentials (stored in config) | TCPA.OutboundDispatcher calls Cool Text API |
| Twilio | Inbound/Outbound | HTTPS REST | API key / Twilio credentials | Same pattern as Cool Text |
| Gravitee API Gateway | Passthrough | HTTPS | API key validation delegated to auth service | Gravitee routes /api/v1/* to TCPA.Api |
| Existing Auth Service | Outbound (API key validation) | HTTPS | Internal service token | Called on every inbound request |
| SMTP Relay | Outbound (email reports) | SMTP | Internal relay credentials | TCPA.ReportService sends weekly reports |
| Gas App Callback URLs | Outbound (general reply forward) | HTTPS POST | Registered per app in CoolTextAccount table | TCPA.MessageProcessor forwards non-opt-out replies |

---

## Kafka Topics

| Topic | Producer | Consumer | Partition Strategy | Retention |
|-------|----------|----------|--------------------|-----------|
| inbound-messages | TCPA.Api | TCPA.MessageProcessor | by phone number (ensures ordering per customer) | 7 days |
| outbound-messages | TCPA.Api | TCPA.OutboundDispatcher | by phone number | 7 days |

---

## Deployment Topology

```
                    [Windows NLB / ARR]
                    /                 \
             [IIS Node 1]         [IIS Node 2]
             TCPA.Api             TCPA.Api
                    \                 /
                     [Gravitee Gateway]
                           |
                    [Apache Kafka Cluster]
                    /                 \
      [Worker Server 1]           [Worker Server 2]
      - TCPA.MessageProcessor     - TCPA.OutboundDispatcher
      - TCPA.ReportService

                    [SQL Server Primary]
                           |
                    [SQL Server Secondary]
                    (AlwaysOn AG — synchronous replica)
```

---

## Architecture Decision Records

### ADR-001: ASP.NET Core on IIS
- **Status:** Accepted
- **Context:** Southern Company Gas mandates .NET as the application platform and IIS as the hosting environment (A1 confirmed in Winston session).
- **Decision:** TCPA.Api implemented as ASP.NET Core 8 Web API hosted on IIS. Worker services implemented as .NET 8 Worker Services running as Windows Services.
- **Rationale:** Platform mandate. No viable alternative within Southern's constraints.
- **Alternatives Considered:** None — platform is mandated.
- **Consequences:** IIS-specific HA patterns required (NLB/ARR rather than container orchestration). Windows Service deployment for background workers rather than container-based deployments.

### ADR-002: Gravitee as API Gateway
- **Status:** Accepted
- **Context:** Southern has an existing Gravitee API gateway through which Gas applications route API calls (C1 confirmed in Winston session).
- **Decision:** All external-facing TCPA API endpoints are registered in Gravitee. Gravitee handles routing, API key forwarding, and rate limiting passthrough.
- **Rationale:** Existing platform asset. Avoids duplicating gateway infrastructure.
- **Alternatives Considered:** Direct IIS exposure without gateway — rejected as it bypasses Southern's existing API management controls.
- **Consequences:** TCPA API deployment requires Gravitee API plan configuration (not in scope for this pipeline — operational task for IT).

### ADR-003: SQL Server for Persistence
- **Status:** Accepted
- **Context:** Southern's standard relational database is SQL Server. The opt-out status store and audit log require ACID transactions (atomic opt-out write + audit write per SPEC-010).
- **Decision:** SQL Server for all persistent state. AlwaysOn Availability Groups for HA.
- **Rationale:** ACID requirement for opt-out + audit atomicity eliminates eventually-consistent stores. SQL Server is the Southern-standard choice.
- **Alternatives Considered:** Azure SQL — considered but not mandated; local SQL Server preferred for on-premise IIS deployment.
- **Consequences:** SQL Server licensing and AlwaysOn AG configuration required. DBA team involvement for schema deployment.

### ADR-004: Apache Kafka for Internal Async Processing
- **Status:** Accepted
- **Context:** Southern has an approved Kafka deployment (A3 confirmed). Even under Option A (synchronous external API), internal async processing via Kafka provides durability and decoupling between the API and message processing workers.
- **Decision:** Kafka used as the internal event backbone. Two topics: inbound-messages (webhook receipt → processing) and outbound-messages (submission → dispatch). Partitioned by phone number to preserve per-customer ordering.
- **Rationale:** Kafka provides message durability — if TCPA.MessageProcessor or TCPA.OutboundDispatcher restarts, in-flight messages are not lost. Phone-number partitioning ensures a customer's STOP and a queued outbound message are processed in order within a partition.
- **Alternatives Considered:** In-process queuing (ConcurrentQueue) — rejected: not durable across service restarts. Direct synchronous processing — rejected: ties up API thread pool during peak burst (5,000/hour).
- **Consequences:** Kafka consumer group configuration required. Partition count must be set at topic creation (recommend 12 for `outbound-messages` to support burst load).

### ADR-005: Synchronous External API, Async Internal Processing (Option A)
- **Status:** Accepted
- **Context:** SPEC-006 is [COMPLEX] — the fail-safe requirement means Gas apps block if TCPA API is unavailable. Two options presented in Winston session: Option A (sync REST + IIS HA) vs Option B (async queue between apps and TCPA API). Mark selected Option A.
- **Decision:** Gas applications call TCPA.Api synchronously via REST. TCPA.Api performs the queue-time opt-out check inline and returns an immediate accepted/suppressed response. Internal processing (keyword detection, opt-out write, confirmation, dispatch) is async via Kafka.
- **Rationale:** Option A is simpler to operate and fits Southern's IIS/REST conventions. The fail-safe is enforced by the calling application treating a 503 or timeout as a block — no new middleware required. IIS NLB/ARR provides availability redundancy.
- **Alternatives Considered:** Option B (async queue between apps and TCPA API) — rejected: adds message broker dependency for the app integration path, increases operational complexity, and was assessed as unnecessary given platform-level HA.
- **Consequences:** TCPA.Api availability is a hard dependency for all in-scope Gas application outbound SMS. IIS NLB configuration and SQL Server HA are critical — a full TCPA.Api outage blocks all Gas application SMS. [ARCH-RISK-001]

### ADR-006: Dual Opt-Out Check (Queue-Time + Send-Time)
- **Status:** Accepted
- **Context:** CQ-006 confirmed opt-out status must be checked at both queue time (SPEC-007, synchronous in API) and send time (SPEC-008, in OutboundDispatcher before Cool Text dispatch). CQ-007 accepted the race-condition edge case.
- **Decision:** Two opt-out checks per outbound message. Queue-time check returns immediate suppressed response to caller. Send-time check is the safety net before Cool Text dispatch. Race-condition edge case (opt-out received between queue and send) is logged as accepted, not a violation, provided opt-out was received after message was queued.
- **Rationale:** Dual-check is the only way to achieve 0% suppression failure (NFR-004) while keeping external API synchronous and fast. Single check at queue time has a race window; single check at send time delays the caller's feedback.
- **Consequences:** Two opt-out status queries per outbound message. At peak (5,000/hour), this is ~10,000 SQL reads/hour — well within SQL Server capacity.

### ADR-007: OpenAPI Spec as Contract
- **Status:** Accepted
- **Context:** C2 confirmed Southern requires OpenAPI spec for REST APIs.
- **Decision:** TCPA.Api generates an OpenAPI 3.0 spec from code annotations (Swashbuckle). Spec is published at /swagger and available as a downloadable JSON for Gas application teams to generate client SDKs.
- **Rationale:** Southern standard. Enables Gas application teams to integrate without manual contract negotiation.
- **Consequences:** OpenAPI annotations required on all controller endpoints. Breaking API changes must version the spec.

---

## NFR Fulfillment

| NFS-ID  | Requirement | Architectural Response |
|---------|-------------|------------------------|
| NFS-001 | P99 confirmation latency ≤ 60s | TCPA.Api returns 200 within 5s of webhook receipt. TCPA.MessageProcessor consumes from Kafka and dispatches confirmation. Kafka latency target: < 1s. Cool Text dispatch target: < 5s. Total budget: ~11s nominal, 60s P99. |
| NFS-002 | 0% delivery to opted-out numbers | Dual opt-out check (ADR-006). Fail-safe on API unavailability (ADR-005). |
| NFS-003 | 5-year audit log retention | SQL Server AuditLog table with NO DELETE policy. Annual archival to cold storage if volume warrants. |
| NFS-004 | All endpoints authenticated | Gravitee enforces API key on all /api/v1/* routes. /webhook/inbound validated in-process against auth service. |
| NFS-005 | 5,000 msg/hour burst | IIS NLB across 2 nodes; 12 Kafka partitions on outbound-messages topic; 2+ OutboundDispatcher instances consuming in parallel. SQL Server read replica for opt-out status lookups. |
| NFS-006 | Live before Jan 31, 2027 | Architectural choices favour Southern-standard stack — no new platform skills required. |

---

## Architectural Risks

| ID | Risk | Likelihood | Impact | Mitigation |
|----|------|-----------|--------|------------|
| ARCH-RISK-001 | Full TCPA.Api outage blocks all in-scope Gas application SMS | Low | Critical | IIS NLB with health check-based failover. SQL Server AlwaysOn AG. Monitoring and alerting on API health endpoint. Runbook for fast recovery. |
| ARCH-RISK-002 | Kafka consumer lag under sustained burst causes confirmation SLA breach | Medium | High | Monitor consumer lag on inbound-messages topic. Alert if lag > 10 messages. Scale MessageProcessor instances if lag persists. |
| ARCH-RISK-003 | SQL Server opt-out status read contention at peak load (10,000 reads/hour + audit writes) | Low | Medium | Read replica for status lookups. Audit writes to primary. Index on phone_number column. |
| ARCH-RISK-004 | Cool Text account config table stale — new application onboarded without TCPA API entry | Medium | High | Onboarding checklist requires TCPA API account registration before application go-live. Admin API validation rejects unregistered accounts with 400. |
| ARCH-RISK-005 | opt-out message wording not approved by Legal before go-live | High | Critical | PD-004 is pending legal sign-off. Configuration-driven design means wording can be updated without deployment. Legal review must be tracked as a go-live blocker. |

---

## Open Questions for Human Review
- None — all Phase 1 questions resolved in Winston session. ARCH-RISK-005 (Legal wording approval) is a go-live blocker but does not affect architecture; it is a process dependency.
