<!-- SDLC Pipeline Artifact
     Stage: 03-spec-decomposer
     Source PRD: inputs/prd.md
     PRD Sections: All
     Generated: 2026-07-23
     Status: APPROVED
-->

# Functional Specifications — TCPA Regulatory Compliance API

## Bounded Contexts
- [BC-1]: Inbound Message Processing — receiving, parsing, and responding to customer SMS
- [BC-2]: Outbound Message Gateway — intercepting, checking, and dispatching application-originated SMS
- [BC-3]: Opt-Out & Compliance Management — opt-out status store, re-opt-in, and audit trail
- [BC-4]: Reporting — three weekly report outputs
- [BC-5]: Configuration & Administration — Cool Text account registry, message wording, log infrastructure

---

## BC-1: Inbound Message Processing

### SPEC-001: Webhook Ingestion
- **Source Requirements:** REQ-014
- **PRD Reference:** PRD §3, CQ-003
- **Priority:** Must Have
- **Dependencies:** SPEC-016 (Cool Text account registry must exist to identify the originating application)
- **Flags:** none

**Behavior:**
Cool Text / Twilio pushes inbound customer SMS messages to a TCPA API webhook endpoint. The system receives the HTTP POST, validates the request origin (API key), extracts the message payload, and routes it to internal processing. The endpoint returns HTTP 200 immediately upon successful receipt — it does not wait for downstream processing to complete.

**Inputs:**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| from | string | E.164 phone number format | Yes |
| to | string | E.164 phone number — must match a registered Cool Text account number | Yes |
| body | string | SMS message text, max 160 chars | Yes |
| provider | string | "cooltext" or "twilio" | Yes |
| messageId | string | Provider-assigned unique message ID | Yes |
| timestamp | datetime | ISO 8601 UTC | Yes |
| apiKey | string | Header: X-Api-Key | Yes |

**Outputs:**
| Field | Type | Format/Constraints |
|-------|------|--------------------|
| status | string | "received" |
| internalId | string | UUID assigned by TCPA API |

**Business Rules:**
- BR-001: The `to` number must match a registered Cool Text account in SPEC-016. Unrecognised accounts return 400.
- BR-002: The API key must be valid. Invalid keys return 401.
- BR-003: The system must return HTTP 200 within 5 seconds. Processing continues asynchronously.

**Edge Cases:**
| Scenario | Expected System Behavior |
|----------|--------------------------|
| `to` number not in Cool Text account registry | Return 400; log unrecognised account; do not process |
| Duplicate messageId received | Return 200 (idempotent); do not reprocess; log duplicate |
| Malformed phone number in `from` | Return 400; log validation failure |
| Body is empty | Return 400; log validation failure |

**Error Conditions:**
| Error | Trigger | System Response |
|-------|---------|-----------------|
| 401 Unauthorized | Invalid or missing API key | HTTP 401; log authentication failure |
| 400 Bad Request | Missing required field or invalid format | HTTP 400 with field-level error detail |
| 500 Internal Error | Unexpected processing failure | HTTP 500; log full stack; alert on-call |

---

### SPEC-002: Keyword Detection
- **Source Requirements:** REQ-003
- **PRD Reference:** PRD §3, OOBR03, PD-002
- **Priority:** Must Have
- **Dependencies:** SPEC-001 (message must be ingested first)
- **Flags:** none

**Behavior:**
After ingestion, the system evaluates the message body to determine whether it is a TCPA opt-out request. The body is trimmed of leading and trailing whitespace and compared case-insensitively to the seven opt-out keywords. Match must be exact — the trimmed body must equal the keyword in its entirety.

The seven opt-out keywords are: STOP, QUIT, END, REVOKE, OPT-OUT, CANCEL, UNSUBSCRIBE.

If the body matches exactly, the message is classified as an opt-out request and routed to SPEC-003. If it does not match, the message is classified as a general reply and routed to SPEC-005 for forwarding to the originating application.

**Inputs:**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| messageBody | string | Raw SMS body from SPEC-001 | Yes |

**Outputs:**
| Field | Type | Format/Constraints |
|-------|------|--------------------|
| isOptOut | boolean | true if exact keyword match |
| matchedKeyword | string | The matched keyword, or null |
| normalizedBody | string | Trimmed, uppercased body used for comparison |

**Business Rules:**
- BR-004: Comparison is case-insensitive. "stop", "Stop", "STOP" all match.
- BR-005: Comparison is exact-word. "Please STOP" does not match. "STOP" alone does.
- BR-006: Leading/trailing whitespace is stripped before comparison.
- BR-007: "OPT-OUT" with a hyphen is the only multi-word keyword; it matches only if the entire body is "OPT-OUT" (case-insensitive, trimmed).

**Edge Cases:**
| Scenario | Expected System Behavior |
|----------|--------------------------|
| Body is "stop " (trailing space) | Trim → "stop" → match STOP → opt-out |
| Body is "please stop" | No match → general reply |
| Body is "STOPNOW" | No match → general reply |
| Body is "OPT-OUT" | Match → opt-out |
| Body is "opt out" (space not hyphen) | No match → general reply |
| Body is "STOP\n" (newline) | Trim → "STOP" → match |

**Error Conditions:**
| Error | Trigger | System Response |
|-------|---------|-----------------|
| Null body | Body field is null after ingestion | Log error; treat as non-opt-out; route to SPEC-005 |

---

### SPEC-003: Opt-Out Processing
- **Source Requirements:** REQ-004
- **PRD Reference:** PRD §3, OOBR04
- **Priority:** Must Have
- **Dependencies:** SPEC-002 (keyword detection must classify as opt-out), SPEC-010 (audit log)
- **Flags:** none

**Behavior:**
When SPEC-002 classifies a message as an opt-out request, the system updates the customer's opt-out status in the compliance store. The customer is identified by their `from` phone number (E.164). The update is atomic — the status change and the audit log entry (SPEC-010) must succeed together or both roll back. After successful update, SPEC-004 is triggered to send the confirmation.

**Inputs:**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| phoneNumber | string | E.164 format from SPEC-001 | Yes |
| matchedKeyword | string | From SPEC-002 | Yes |
| internalMessageId | string | UUID from SPEC-001 | Yes |
| receivedAt | datetime | ISO 8601 UTC | Yes |

**Outputs:**
| Field | Type | Format/Constraints |
|-------|------|--------------------|
| optOutId | string | UUID of the opt-out record created |
| phoneNumber | string | E.164 |
| status | string | "opted-out" |
| effectiveAt | datetime | ISO 8601 UTC — time the record was written |

**Business Rules:**
- BR-008: If the customer is already opted out, the system records the duplicate STOP request in the audit log but does not create a new opt-out record. Confirmation is still sent.
- BR-009: The opt-out status must be written before the confirmation (SPEC-004) is triggered.
- BR-010: The audit log entry (SPEC-010) is written atomically with the opt-out record.

**Edge Cases:**
| Scenario | Expected System Behavior |
|----------|--------------------------|
| Customer already opted out | Log duplicate; send confirmation; do not duplicate opt-out record |
| Database write fails | Roll back; do not trigger confirmation; log error; alert |
| Same number sends STOP twice within 1 second | Idempotency — second write is a no-op; one confirmation sent |

**Error Conditions:**
| Error | Trigger | System Response |
|-------|---------|-----------------|
| Database unavailable | Persistence layer unreachable | Log error; alert on-call; do not send confirmation |

---

### SPEC-004: Opt-Out Confirmation
- **Source Requirements:** REQ-005, REQ-008
- **PRD Reference:** PRD §3, OOBR04, OOBR09, PD-004
- **Priority:** Must Have
- **Dependencies:** SPEC-003 (opt-out record must be written), SPEC-016 (Cool Text account for originating app), SPEC-017 (message wording config)
- **Flags:** none

**Behavior:**
After a successful opt-out record is written (SPEC-003), the system dispatches a confirmation SMS to the customer's phone number. The message body is read from the configurable opt-out message store (SPEC-017) — it is never hardcoded. The message is dispatched via the same Cool Text account that received the original STOP. The entire flow from STOP receipt to confirmation dispatch must complete within 60 seconds (P99).

**Inputs:**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| phoneNumber | string | E.164 — destination (customer) | Yes |
| coolTextAccountNumber | string | From SPEC-016 — originating account | Yes |
| optOutId | string | UUID from SPEC-003 | Yes |
| receivedAt | datetime | ISO 8601 UTC — original STOP receipt time | Yes |

**Outputs:**
| Field | Type | Format/Constraints |
|-------|------|--------------------|
| confirmationMessageId | string | Provider-assigned ID |
| dispatchedAt | datetime | ISO 8601 UTC |
| latencySeconds | integer | dispatchedAt minus receivedAt — for SLA monitoring |
| messageBody | string | Actual text sent (snapshot for audit) |

**Business Rules:**
- BR-011: Message body must be read from SPEC-017 at dispatch time, not cached at startup.
- BR-012: Dispatch must complete within 60 seconds of the original STOP receipt (NFR-001).
- BR-013: The confirmation is sent via the same Cool Text account number the STOP was received on.
- BR-014: A snapshot of the exact message body sent is written to the audit log.

**Edge Cases:**
| Scenario | Expected System Behavior |
|----------|--------------------------|
| Cool Text / Twilio dispatch fails | Retry up to 3 times with exponential backoff; log failure; alert if all retries exhausted |
| Confirmation sent after 60-second window | Log SLA breach; alert; do not suppress the message |
| Opt-out message config missing | Log error; alert; do not send blank message; escalate |

**Error Conditions:**
| Error | Trigger | System Response |
|-------|---------|-----------------|
| Provider dispatch failure after retries | 3 failed dispatch attempts | Log permanent failure; alert on-call; audit record marked "confirmation-failed" |
| SLA breach | dispatchedAt > receivedAt + 60s | Log SLA violation metric; alert; audit record flagged |

---

### SPEC-005: Inbound Message Routing
- **Source Requirements:** REQ-002
- **PRD Reference:** PRD §3, OOBR02, CQ-001
- **Priority:** Must Have
- **Dependencies:** SPEC-001 (ingestion), SPEC-002 (keyword detection — only non-opt-out messages route here), SPEC-016 (account-to-application mapping)
- **Flags:** none

**Behavior:**
For inbound customer messages that are not opt-out requests (SPEC-002 returns isOptOut = false), the system forwards the exact original message body to the Gas application that owns the Cool Text account on which the message was received. The `to` number on the inbound message maps to exactly one registered Cool Text account (CQ-001: 1:1 mapping), which maps to exactly one Gas application with a registered callback endpoint.

**Inputs:**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| internalMessageId | string | UUID from SPEC-001 | Yes |
| from | string | E.164 — customer number | Yes |
| body | string | Original unmodified message body | Yes |
| coolTextAccountNumber | string | The `to` number from the original inbound message | Yes |

**Outputs:**
| Field | Type | Format/Constraints |
|-------|------|--------------------|
| forwardStatus | string | "forwarded" or "failed" |
| applicationEndpoint | string | URL the message was forwarded to |
| httpStatusCode | integer | Response code from the application callback |

**Business Rules:**
- BR-015: The message body forwarded must be byte-for-byte identical to the body received — no modification.
- BR-016: The target application is resolved by looking up the Cool Text account number in SPEC-016.
- BR-017: If the application callback returns non-2xx, the forward is logged as failed. The TCPA API does not retry — application delivery is best-effort for general replies.

**Edge Cases:**
| Scenario | Expected System Behavior |
|----------|--------------------------|
| Application callback endpoint unreachable | Log forwarding failure; do not retry; alert IT |
| Application returns 500 | Log failure with response; do not retry |
| Message is an opt-out keyword — routed here in error | Should not occur; if it does, log anomaly and halt forwarding |

**Error Conditions:**
| Error | Trigger | System Response |
|-------|---------|-----------------|
| No application registered for Cool Text account | Account in SPEC-016 has no callback URL | Log error; alert IT; do not forward |

---

## BC-2: Outbound Message Gateway

### SPEC-006: Outbound Message Submission
- **Source Requirements:** REQ-014 (outbound side)
- **PRD Reference:** PRD §3, CQ-004
- **Priority:** Must Have
- **Dependencies:** SPEC-007 (queue-time opt-out check), SPEC-016 (Cool Text account registry)
- **Flags:** [COMPLEX: fail-safe behaviour when TCPA API is unavailable — drives HA design for all in-scope application integrations]

**Behavior:**
In-scope Gas applications submit outbound SMS messages to the TCPA API rather than calling Cool Text / Twilio directly. The TCPA API receives the outbound message request, performs a queue-time opt-out check (SPEC-007), and — if the number is not opted out — queues the message for dispatch. If the TCPA API is unavailable, the calling application receives an explicit error response and must not send the message through any alternative path (fail-safe per CQ-004).

**Inputs:**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| toNumber | string | E.164 destination (customer) | Yes |
| body | string | SMS message body, max 160 chars | Yes |
| coolTextAccountNumber | string | The application's registered account | Yes |
| applicationId | string | Registered application identifier | Yes |
| apiKey | string | Header: X-Api-Key | Yes |
| correlationId | string | Caller-provided idempotency key | No |

**Outputs:**
| Field | Type | Format/Constraints |
|-------|------|--------------------|
| status | string | "queued", "suppressed", or "rejected" |
| suppressionReason | string | "opted-out" if suppressed, null otherwise |
| messageId | string | UUID if queued; null if suppressed |
| queuedAt | datetime | ISO 8601 UTC |

**Business Rules:**
- BR-018: A request with a `correlationId` that matches a previously processed request returns the original response (idempotency).
- BR-019: If the `coolTextAccountNumber` is not registered in SPEC-016, the request is rejected with 400.
- BR-020: Fail-safe — if the TCPA API is itself unavailable, the HTTP client connection times out and the calling application must treat this as a send-blocking error.

**Edge Cases:**
| Scenario | Expected System Behavior |
|----------|--------------------------|
| TCPA API is overloaded | Return 503 with Retry-After header; calling app must not bypass |
| Duplicate correlationId | Return original response; do not re-queue |
| Body exceeds 160 chars | Return 400 with validation error |

**Error Conditions:**
| Error | Trigger | System Response |
|-------|---------|-----------------|
| 400 Bad Request | Missing field, invalid format, unregistered account | HTTP 400 with field detail |
| 401 Unauthorized | Invalid API key | HTTP 401 |
| 503 Service Unavailable | TCPA API overloaded or dependencies degraded | HTTP 503 + Retry-After |

---

### SPEC-007: Queue-Time Opt-Out Check
- **Source Requirements:** REQ-006
- **PRD Reference:** PRD §3, OOBR06, CQ-006
- **Priority:** Must Have
- **Dependencies:** SPEC-006 (outbound submission), BC-3 opt-out status store
- **Flags:** none

**Behavior:**
At the point an outbound message is submitted (SPEC-006), the system queries the opt-out status store for the destination phone number. If the number is opted out, the message is suppressed immediately and the caller receives status "suppressed". The suppression is logged. If the number is opted in (or has no record, per ASM-002), the message proceeds to the dispatch queue.

**Inputs:**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| toNumber | string | E.164 | Yes |
| messageId | string | UUID from SPEC-006 | Yes |

**Outputs:**
| Field | Type | Format/Constraints |
|-------|------|--------------------|
| optOutStatus | string | "opted-out" or "opted-in" |
| checkedAt | datetime | ISO 8601 UTC |
| suppressionLogId | string | UUID of suppression log entry if suppressed |

**Business Rules:**
- BR-021: No record in the opt-out store means opted-in (ASM-002).
- BR-022: A suppressed message must be logged with: toNumber, messageId, applicationId, checkedAt, optOutStatus.
- BR-023: The opt-out status lookup must complete within 100ms P99 to stay within the overall outbound submission latency budget.

**Edge Cases:**
| Scenario | Expected System Behavior |
|----------|--------------------------|
| Opt-out status is being written concurrently (race) | Queue-time check returns opted-in; send-time check (SPEC-008) catches it. Race condition is an accepted edge case per CQ-007 — audit log entry written. |
| Status store query times out | Return 503 to caller (fail-safe — cannot confirm status) |

**Error Conditions:**
| Error | Trigger | System Response |
|-------|---------|-----------------|
| Status store unavailable | Persistence layer unreachable | Return 503 to SPEC-006; suppress send |

---

### SPEC-008: Send-Time Opt-Out Check
- **Source Requirements:** REQ-006
- **PRD Reference:** PRD §3, OOBR06, CQ-006, CQ-007
- **Priority:** Must Have
- **Dependencies:** SPEC-007 (message must have passed queue-time check), BC-3 opt-out status store
- **Flags:** none

**Behavior:**
Immediately before dispatching a queued message to Cool Text / Twilio, the system performs a second opt-out status check. This is the safety net that catches messages queued before a STOP was processed (the race condition acknowledged in CQ-007). If the number is now opted out, the message is suppressed at send time. An audit log entry is written indicating the message was suppressed at send time after passing the queue-time check — this is logged as an accepted edge case, not a violation.

**Inputs:**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| toNumber | string | E.164 | Yes |
| messageId | string | UUID from SPEC-006 | Yes |
| queuedAt | datetime | ISO 8601 UTC — when the message was originally queued | Yes |

**Outputs:**
| Field | Type | Format/Constraints |
|-------|------|--------------------|
| optOutStatus | string | "opted-out" or "opted-in" |
| checkedAt | datetime | ISO 8601 UTC |
| action | string | "dispatched" or "suppressed-at-send-time" |

**Business Rules:**
- BR-024: If opted out at send time (but not at queue time), suppress and log as "suppressed-at-send-time" with queuedAt and optOutEffectiveAt timestamps.
- BR-025: A "suppressed-at-send-time" event is classified as an accepted edge case, not a TCPA violation, provided optOutEffectiveAt > queuedAt (i.e. the opt-out was received after the message was queued).
- BR-026: If optOutEffectiveAt <= queuedAt (opt-out was already in effect when message was queued), this is flagged as a suppression failure and treated as a potential violation — alert immediately.

**Edge Cases:**
| Scenario | Expected System Behavior |
|----------|--------------------------|
| Opt-out received after queue time, before send time | Suppress at send time; log as accepted edge case |
| Opt-out was already in effect at queue time | Log as potential violation; alert; suppress |
| Status store unavailable at send time | Suppress send (fail-safe); log; alert |

**Error Conditions:**
| Error | Trigger | System Response |
|-------|---------|-----------------|
| Status store unavailable | Persistence unreachable at send time | Suppress message (fail-safe); log; alert on-call |

---

## BC-3: Opt-Out & Compliance Management

### SPEC-009: Opt-Out Status Store
- **Source Requirements:** REQ-004, REQ-006
- **PRD Reference:** PRD §3, OOBR04, OOBR06
- **Priority:** Must Have
- **Dependencies:** none (foundational)
- **Flags:** none

**Behavior:**
The opt-out status store is the authoritative record of every customer phone number's opt-out status. A record exists for every number that has ever sent an opt-out keyword. Numbers with no record are treated as opted-in (ASM-002). The store supports three operations: write opt-out (SPEC-003), write re-opt-in (SPEC-011), and read status (SPEC-007, SPEC-008).

**Inputs (Write Opt-Out):**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| phoneNumber | string | E.164, primary key | Yes |
| status | string | "opted-out" | Yes |
| effectiveAt | datetime | ISO 8601 UTC | Yes |
| optOutId | string | UUID — links to audit record | Yes |

**Inputs (Write Re-Opt-In):**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| phoneNumber | string | E.164, primary key | Yes |
| status | string | "opted-in" | Yes |
| effectiveAt | datetime | ISO 8601 UTC | Yes |
| reOptInId | string | UUID — links to audit record | Yes |

**Business Rules:**
- BR-027: The store is keyed by E.164 phone number. One record per number (upsert on write).
- BR-028: Status transitions are: (none) → opted-out → opted-in → opted-out (cycles allowed).
- BR-029: Status reads must return results within 100ms P99.
- BR-030: All historical status changes are preserved in the audit log (SPEC-010); the status store holds only the current state.

**Edge Cases:**
| Scenario | Expected System Behavior |
|----------|--------------------------|
| Re-opt-in for number that was never opted out | Write opted-in record; log as anomaly; do not reject |
| Concurrent write (opt-out and re-opt-in for same number) | Last-write-wins with timestamp; both writes are audit-logged |

---

### SPEC-010: Audit Logging
- **Source Requirements:** REQ-001, REQ-007 (re-opt-in audit)
- **PRD Reference:** PRD §3, OOBR01, NFR-003
- **Priority:** Must Have
- **Dependencies:** none (foundational — all other specs write to this)
- **Flags:** none

**Behavior:**
The system writes an immutable audit log entry for every compliance-significant event. Events include: STOP received, opt-out status written, confirmation dispatched, confirmation failed, message suppressed (queue-time), message suppressed (send-time), re-opt-in performed, race-condition edge case logged. Audit records are retained for 5 years and must be queryable by phone number and date range.

**Audit Event Schema:**
| Field | Type | Constraints |
|-------|------|-------------|
| auditId | string | UUID, primary key |
| eventType | string | Enum: STOP_RECEIVED, OPT_OUT_WRITTEN, CONFIRMATION_DISPATCHED, CONFIRMATION_FAILED, SUPPRESSED_QUEUE_TIME, SUPPRESSED_SEND_TIME, RE_OPT_IN, RACE_CONDITION_EDGE_CASE, SLA_BREACH |
| phoneNumber | string | E.164 |
| occurredAt | datetime | ISO 8601 UTC |
| applicationId | string | Originating Gas application (where applicable) |
| messageId | string | UUID of the associated message |
| details | JSON | Event-specific payload (keyword, message body snapshot, latency, agent ID for re-opt-ins, etc.) |

**Business Rules:**
- BR-031: Audit records are immutable — no update or delete operations permitted.
- BR-032: Audit records must be retained for a minimum of 5 years from the event date (NFR-003).
- BR-033: Audit records must be queryable by: phoneNumber, eventType, date range (from/to), applicationId.
- BR-034: Every opt-out and re-opt-in write in SPEC-009 must have a corresponding audit record written atomically.

**Error Conditions:**
| Error | Trigger | System Response |
|-------|---------|-----------------|
| Audit write failure | Persistence unavailable | Block the triggering operation (opt-out/re-opt-in cannot succeed if audit cannot be written) |

---

### SPEC-011: Admin Re-Opt-In API
- **Source Requirements:** REQ-007
- **PRD Reference:** PRD §3, OOBR07, CQ-005, CQ-011
- **Priority:** Must Have
- **Dependencies:** SPEC-009 (opt-out status store), SPEC-010 (audit log)
- **Flags:** none

**Behavior:**
An authenticated REST endpoint allows a Help Desk agent to update a customer's opt-out status to opted-in. The caller must provide a valid API key and a reason for the re-opt-in. The system validates that the number exists in the status store (a re-opt-in for an unknown number is allowed but logged as anomalous). The status is updated and an audit record is written atomically. Rate limiting of 10 requests per minute per API key is enforced.

**Inputs:**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| phoneNumber | string | E.164 | Yes |
| reason | string | Free text, max 500 chars | Yes |
| agentId | string | Help Desk agent identifier | Yes |
| apiKey | string | Header: X-Api-Key | Yes |

**Outputs:**
| Field | Type | Format/Constraints |
|-------|------|--------------------|
| reOptInId | string | UUID of the re-opt-in record |
| phoneNumber | string | E.164 |
| status | string | "opted-in" |
| effectiveAt | datetime | ISO 8601 UTC |

**Business Rules:**
- BR-035: Rate limit: 10 requests per minute per API key. Excess requests return 429 with Retry-After header.
- BR-036: The reason field is mandatory and must be stored in the audit record.
- BR-037: The agentId is stored in the audit record to provide a full chain of custody.
- BR-038: Re-opt-in for a number with no opt-out record is permitted; logged as anomalous.
- BR-039: The status update and audit record are written atomically — partial writes must roll back.

**Edge Cases:**
| Scenario | Expected System Behavior |
|----------|--------------------------|
| Rate limit exceeded | HTTP 429 with Retry-After: 60 |
| Number not currently opted out | Update status to opted-in; log as anomalous; return success |
| agentId not provided | HTTP 400 |

**Error Conditions:**
| Error | Trigger | System Response |
|-------|---------|-----------------|
| 401 Unauthorized | Invalid API key | HTTP 401 |
| 429 Too Many Requests | Rate limit exceeded | HTTP 429 + Retry-After |
| 500 Internal Error | Atomic write failure | HTTP 500; roll back; log; alert |

---

## BC-4: Reporting

### SPEC-012: Opted-In Message Volume Report
- **Source Requirements:** REQ-009
- **PRD Reference:** PRD §3, RPBR01, CQ-002, CQ-010
- **Priority:** Must Have
- **Dependencies:** SPEC-006 (outbound submission — source of message volume data), SPEC-015 (shares scheduling infrastructure)
- **Flags:** none

**Behavior:**
Every Monday (US Eastern), the system generates a report covering all outbound SMS messages successfully dispatched to opted-in numbers during the prior Monday–Sunday week. The report is delivered by email to the configurable distribution list (SPEC-017). Data is sourced from the outbound message dispatch log.

**Report Contents:**
| Field | Description |
|-------|-------------|
| Reporting period | Start date and end date (Mon–Sun) |
| Total messages dispatched to opted-in numbers | Count |
| Breakdown by application | Count per Gas application |
| Breakdown by day | Daily count within the period |

**Business Rules:**
- BR-040: Report covers messages dispatched (reached Cool Text / Twilio) — not messages submitted (which may have been suppressed).
- BR-041: Report is generated at 06:00 US Eastern every Monday.
- BR-042: Delivery failures (email bounce) are logged and alerted.

---

### SPEC-013: Opted-Out Message Volume Report
- **Source Requirements:** REQ-010
- **PRD Reference:** PRD §3, RPBR02, CQ-002, CQ-010
- **Priority:** Must Have
- **Dependencies:** SPEC-007, SPEC-008 (suppression events — source data), SPEC-015 (scheduling)
- **Flags:** none

**Behavior:**
Every Monday (US Eastern), the system generates a report covering all outbound SMS messages suppressed during the prior Monday–Sunday week because the destination number was opted out. Data covers both queue-time and send-time suppressions. Delivered by email to the configurable distribution list.

**Report Contents:**
| Field | Description |
|-------|-------------|
| Reporting period | Start date and end date |
| Total messages suppressed | Count |
| Breakdown by application | Count per Gas application |
| Breakdown by suppression type | Queue-time vs. send-time |
| Breakdown by day | Daily count |
| Race-condition edge cases | Count of accepted send-time suppressions |

**Business Rules:**
- BR-043: Each suppressed message appears once — not counted at both queue-time and send-time.
- BR-044: Report generated at 06:00 US Eastern every Monday; same delivery mechanism as SPEC-012.

---

### SPEC-014: Weekly Compliance Report
- **Source Requirements:** REQ-011
- **PRD Reference:** PRD §3, RPBR03, CQ-002, CQ-009, CQ-010
- **Priority:** Must Have
- **Dependencies:** SPEC-012, SPEC-013 (data feeds), SPEC-010 (audit log — source of opt-out event data)
- **Flags:** none

**Behavior:**
Every Monday (US Eastern), the system generates a consolidated compliance report covering the prior week. This is the primary TCPA audit artefact. It is delivered by email to the configurable distribution list (which may differ from SPEC-012 and SPEC-013 recipients). The report summarises the opt-out enforcement effectiveness.

**Report Contents:**
| Field | Description |
|-------|-------------|
| Reporting period | Start date and end date |
| Total STOP requests received | Count |
| Total confirmations sent within 60 seconds | Count and % |
| Total confirmations breaching 60-second SLA | Count |
| Total opted-out numbers (cumulative) | Count |
| Total messages suppressed this week | Count |
| Total messages dispatched to opted-in numbers | Count |
| Opt-out suppression rate | % (should be 100%) |
| Re-opt-ins performed | Count |
| Alerts triggered | List with descriptions |

**Business Rules:**
- BR-045: Suppression rate below 100% triggers an alert in the report body.
- BR-046: Any SLA breach (confirmation > 60 seconds) is listed individually.
- BR-047: Report generated at 06:00 US Eastern every Monday.
- BR-048: Email recipient list is read from SPEC-017 (configurable, separate from SPEC-012/013 lists if needed).

---

## BC-5: Configuration & Administration

### SPEC-015: Cool Text Account Registry
- **Source Requirements:** REQ-013
- **PRD Reference:** PRD §3, CQ-001, CQ-008
- **Priority:** Must Have
- **Dependencies:** none (foundational)
- **Flags:** none

**Behavior:**
The system maintains a database table of Cool Text account numbers and their associated Gas application metadata. This table is the authoritative source for: inbound message routing (SPEC-005), outbound message validation (SPEC-006), and confirmation dispatch (SPEC-004). Entries are managed via database migration scripts — no UI required for Phase 1. Each entry maps a Cool Text account number 1:1 to exactly one Gas application.

**Schema:**
| Field | Type | Constraints |
|-------|------|-------------|
| coolTextAccountNumber | string | Primary key, E.164 or account ID format |
| applicationId | string | Unique identifier of the Gas application |
| applicationName | string | Human-readable (e.g. "BizTalk", "GCMA") |
| callbackUrl | string | HTTPS URL — where to forward inbound replies |
| isActive | boolean | Inactive accounts are rejected on inbound/outbound |
| createdAt | datetime | ISO 8601 UTC |
| updatedAt | datetime | ISO 8601 UTC |

**Business Rules:**
- BR-049: One Cool Text account maps to exactly one applicationId (1:1).
- BR-050: Inactive accounts (isActive = false) are rejected with 400 on inbound webhook and outbound submission.
- BR-051: Changes to this table take effect immediately — no restart required.
- BR-052: In-scope Phase 1 applications: BizTalk, GCMA, KMI, ARM / Construction Portal.

---

### SPEC-016: System Configuration Store
- **Source Requirements:** REQ-008, REQ-011 (distribution lists)
- **PRD Reference:** PRD §3, PD-004, CQ-009
- **Priority:** Must Have
- **Dependencies:** none (foundational)
- **Flags:** none

**Behavior:**
The system maintains a configuration store for runtime-adjustable values that must not require a code deployment to change. Key configuration items: opt-out confirmation message body, email distribution lists for each report type, weekly report schedule, rate limiting thresholds. Values are read at runtime — not cached at startup.

**Configuration Items:**
| Key | Description | Default |
|-----|-------------|---------|
| optOutMessageBody | Global opt-out confirmation text (PENDING LEGAL) | Placeholder — see PRD §3 REQ-008 |
| reportRecipients.optedIn | Email list for SPEC-012 | configurable |
| reportRecipients.optedOut | Email list for SPEC-013 | configurable |
| reportRecipients.compliance | Email list for SPEC-014 | configurable |
| reportSchedule.dayOfWeek | Day to generate reports | Monday |
| reportSchedule.timeEastern | Time to generate reports (US Eastern) | 06:00 |
| adminApi.rateLimitPerMinute | Re-opt-in API rate limit | 10 |

**Business Rules:**
- BR-053: Configuration values are read at call time — a change is effective on the next invocation without restart.
- BR-054: The optOutMessageBody key must exist and be non-empty. If missing or empty, SPEC-004 must fail with an alert rather than send a blank message.

---

### SPEC-017: Production & Debug Logging
- **Source Requirements:** REQ-012
- **PRD Reference:** PRD §3, §Appendix
- **Priority:** Must Have
- **Dependencies:** none
- **Flags:** none

**Behavior:**
The system produces structured logs at two levels for IT consumption. Production logs capture every significant operational event (message received, message dispatched, suppression, opt-out written, re-opt-in, report generated, alert triggered). Debug logs capture full request/response payloads and internal state transitions. Log level is configurable at runtime. Logs must not contain customer PII beyond the phone number, and must not contain message body content in production-level logs.

**Business Rules:**
- BR-055: Production logs: message events (no body), opt-out events, suppression events, report generation, API auth failures, SLA events.
- BR-056: Debug logs: full request/response payloads, internal processing steps. Debug level is off by default in production.
- BR-057: Phone numbers in logs are hashed in production log level; unhashed in debug (IT use only, access-controlled).
- BR-058: Log retention: at minimum 90 days for production logs; 30 days for debug logs.

---

## Non-Functional Specifications

### NFS-001: Opt-Out Confirmation Latency
- **Source:** NFR-001
- **Category:** Performance
- **Measurable Target:** P99 latency from STOP receipt to confirmation SMS dispatched to Cool Text / Twilio ≤ 60 seconds, measured at the TCPA API layer. Network delivery time by Cool Text / Twilio is excluded.
- **Verification Method:** Application performance monitoring. Log `receivedAt` and `dispatchedAt` for every STOP event. P99 calculated over rolling 7-day window. Alert if P99 exceeds 50 seconds (warning) or 60 seconds (breach).

### NFS-002: Opted-Out Number Suppression Rate
- **Source:** NFR-004
- **Category:** Reliability
- **Measurable Target:** 0 messages dispatched to opted-out numbers per reporting week. Accepted edge-case suppressions (CQ-007: message queued before STOP) do not count as failures provided optOutEffectiveAt > queuedAt and the send-time check (SPEC-008) suppressed the message.
- **Verification Method:** Weekly compliance report (SPEC-014). Any non-zero count of messages reaching opted-out numbers (excluding accepted edge cases) triggers immediate alert and investigation.

### NFS-003: Audit Log Retention
- **Source:** NFR-003
- **Category:** Compliance
- **Measurable Target:** All audit records queryable for minimum 5 years (1,825 days) from event date. Query response for a date-range lookup against 5-year-old data must return within 30 seconds.
- **Verification Method:** Annual data retention audit. Query test against oldest records.

### NFS-004: API Authentication
- **Source:** NFR-006
- **Category:** Security
- **Measurable Target:** 100% of API endpoints reject requests without a valid API key. Zero unauthenticated requests reach any business logic. Measured by: all 4xx responses to unauthenticated requests have HTTP 401 status; no 2xx responses to unauthenticated requests.
- **Verification Method:** Security test suite — all endpoints probed without API key; all must return 401.

### NFS-005: Message Throughput
- **Source:** NFR-008
- **Category:** Scalability
- **Measurable Target:** System handles steady-state volume of ~1,000 messages/day without latency degradation. System handles peak burst of 5,000 messages/hour (outage notification event) while maintaining NFS-001 P99 confirmation latency ≤ 60 seconds.
- **Verification Method:** Load test at 5,000 messages/hour sustained for 15 minutes. NFS-001 P99 must remain ≤ 60 seconds throughout. Zero message loss or duplicate dispatches.

### NFS-006: Compliance Deadline
- **Source:** CON-001
- **Category:** Compliance
- **Measurable Target:** System live in production and enforcing opt-outs before January 31, 2027. Production deployment date is the measurement.
- **Verification Method:** Production go-live date recorded and signed off by compliance team.

---

## Spec Dependency Map

```
SPEC-015 (Cool Text Registry)
  └─► SPEC-001 (Webhook Ingestion)
        └─► SPEC-002 (Keyword Detection)
              ├─► SPEC-003 (Opt-Out Processing)
              │     ├─► SPEC-010 (Audit Log)
              │     └─► SPEC-004 (Confirmation Dispatch)
              │           └─► SPEC-016 (Config Store)
              └─► SPEC-005 (Inbound Routing)

SPEC-009 (Status Store)
  ├─► SPEC-007 (Queue-Time Check)
  │     └─► SPEC-006 (Outbound Submission)
  └─► SPEC-008 (Send-Time Check)

SPEC-010 (Audit Log)
  └─► SPEC-011 (Admin Re-Opt-In)

SPEC-012, SPEC-013 (Volume Reports) ─► SPEC-014 (Compliance Report)
```

## Specs Summary
- Total specs: 17 (functional) + 6 (non-functional) = 23
- Bounded contexts: 5
- Complex specs requiring architecture attention: 1 (SPEC-006 — fail-safe HA design)
- Must Have: 17 / Should Have: 0 / Could Have: 0
