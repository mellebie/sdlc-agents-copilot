<!-- SDLC Pipeline Artifact
     Stage: 06-story-writer
     Source PRD: inputs/prd.md
     PRD Sections: All
     Generated: 2026-07-23
     Status: APPROVED
-->

# Product Backlog — TCPA Regulatory Compliance API

## Backlog Summary
- Total epics: 5
- Total stories: 18
- Must Have stories: 18
- Should Have stories: 0
- Could Have stories: 0
- High-risk stories: 5
- Spike stories: 0
- Total story points: 58 (~2 sprints at 30pts/sprint for implementation; testing and integration sprints additional)

---

## EPIC-001: Opt-Out & Compliance Foundation
- **Description:** Establishes the persistent data layer and infrastructure that every other epic depends on — opt-out status store, audit log, Cool Text account registry, system configuration, and structured logging.
- **Source Specs:** SPEC-009, SPEC-010, SPEC-015, SPEC-016, SPEC-017
- **Priority:** Must Have
- **Personas:** PER-005 (IT / Developer — builds and operates this layer)
- **Delivery order:** Sprint 1 — must be complete before EPIC-002 and EPIC-003 begin

---

### STORY-001: Opt-Out Status Store
**User Story:**
As the TCPA Regulatory Compliance API,
I want to persistently store and retrieve each customer's current opt-out status by phone number,
So that every inbound opt-out request and every outbound message check has an authoritative, consistent source of truth.

**Source:** SPEC-009 | PRD §3 REQ-004, REQ-006
**Priority:** Must Have
**Story Points:** 3
**Flags:** none

**Acceptance Criteria:**

_AC-001 (Happy Path — write opt-out):_
- Given a valid E.164 phone number and an opt-out event
- When the system writes an opt-out record
- Then the record is persisted with status "opted-out", effectiveAt timestamp, and a link to the audit record
- And a subsequent status read for that number returns "opted-out"

_AC-002 (Happy Path — read opted-in by default):_
- Given a phone number with no record in the status store
- When the system reads the opt-out status for that number
- Then the status returned is "opted-in" (ASM-002)

_AC-003 (Unhappy Path — concurrent write):_
- Given two concurrent opt-out writes for the same phone number
- When both writes complete
- Then exactly one record exists (upsert semantics, not duplicate insert)
- And both events are individually recorded in the audit log

_AC-004 (Performance):_
- Given a status read request under any load condition
- When the query executes
- Then it returns within 100ms P99

**Out of Scope for this story:** Historical status query (audit log responsibility — STORY-002). Re-opt-in write (STORY-015).

**Notes:** Index on phone_number is mandatory. Schema migration script required — DBA team to review before merge.

---

### STORY-002: Audit Logging Infrastructure
**User Story:**
As the Compliance / Audit Team,
I want every compliance-significant event to be immutably recorded with full context,
So that I can demonstrate TCPA adherence during an audit and investigate any incident with a complete chain of events.

**Source:** SPEC-010 | PRD §3 REQ-001, NFR-003
**Priority:** Must Have
**Story Points:** 3
**Flags:** none

**Acceptance Criteria:**

_AC-001 (Happy Path — write audit record):_
- Given any compliance event (STOP received, opt-out written, confirmation dispatched, message suppressed, re-opt-in performed)
- When the event occurs
- Then an audit record is written with: auditId, eventType, phoneNumber, occurredAt (UTC), applicationId, messageId, and a JSON details payload
- And the write is atomic with the triggering operation (both succeed or both roll back)

_AC-002 (Happy Path — query by phone number):_
- Given a phone number and a date range
- When a query is issued against the audit log
- Then all audit records for that number within the date range are returned in chronological order

_AC-003 (Unhappy Path — audit write failure blocks triggering operation):_
- Given a database failure during an audit write
- When an opt-out or re-opt-in operation attempts to commit
- Then the entire transaction rolls back
- And neither the status record nor the audit record is persisted

_AC-004 (Retention):_
- Given an audit record created 5 years ago
- When queried by phone number and date range
- Then the record is returned (5-year retention, NFR-003)

**Out of Scope for this story:** Audit query API endpoint (compliance team queries directly via database tooling in Phase 1).

**Notes:** NO DELETE policy must be enforced at the database level (deny DELETE permission on audit_log table). Immutability is a compliance requirement.

---

### STORY-003: Cool Text Account Registry
**User Story:**
As a Southern Company Gas Application,
I want the TCPA API to know which Cool Text account number belongs to me,
So that inbound customer replies are routed to my callback URL and my outbound messages are validated against my registered account.

**Source:** SPEC-015 | PRD §3 REQ-013, CQ-001, CQ-008
**Priority:** Must Have
**Story Points:** 2
**Flags:** [HIGH-RISK — RISK-010: stale registry causes silent routing failures; onboarding checklist required]

**Acceptance Criteria:**

_AC-001 (Happy Path — account lookup):_
- Given a Cool Text account number registered in the registry
- When the system looks up the account
- Then it returns: applicationId, applicationName, callbackUrl, isActive status

_AC-002 (Happy Path — 1:1 mapping enforced):_
- Given an attempt to register a second application against an already-registered Cool Text account number
- When the migration script runs
- Then a unique constraint violation prevents the duplicate registration

_AC-003 (Unhappy Path — unregistered account):_
- Given an inbound webhook or outbound submission with a Cool Text account number not in the registry
- When the system attempts the lookup
- Then it returns a 400 error with a descriptive message identifying the unregistered account number

_AC-004 (Runtime update):_
- Given a new Cool Text account added via database migration
- When the next request arrives referencing that account
- Then it is resolved without a service restart

**Out of Scope for this story:** A UI for managing accounts — Phase 1 uses database migrations only.

**Notes:** Phase 1 in-scope applications: BizTalk, GCMA, KMI, ARM / Construction Portal. Seed migration must include all four. callbackUrl column stores HTTPS URL for general reply forwarding (STORY-010).

---

### STORY-004: System Configuration Store
**User Story:**
As the TCPA Regulatory Compliance API,
I want runtime-adjustable configuration values to be stored in a database and read at call time,
So that the opt-out message wording, email recipient lists, and rate limits can be updated without a code deployment.

**Source:** SPEC-016 | PRD §3 REQ-008, REQ-011, PD-004, CQ-009
**Priority:** Must Have
**Story Points:** 2
**Flags:** none

**Acceptance Criteria:**

_AC-001 (Happy Path — read config value):_
- Given a config key that exists in the store
- When the system reads that key at call time
- Then it returns the current value
- And a value updated since the last request is reflected on the next read (no restart required)

_AC-002 (Unhappy Path — missing required config):_
- Given the optOutMessageBody config key is missing or empty
- When SPEC-004 (confirmation dispatch) attempts to read it
- Then an alert is raised and the confirmation is not sent with a blank body

_AC-003 (Seed data):_
- Given the initial deployment migration
- When it runs
- Then all required config keys are present with default values: optOutMessageBody (placeholder), report recipient lists (empty — to be configured pre-go-live), report schedule (Monday 06:00 Eastern), admin rate limit (10/min)

**Out of Scope for this story:** A UI for editing config — Phase 1 uses direct database updates.

---

### STORY-005: Structured Production and Debug Logging
**User Story:**
As an IT / Developer,
I want structured logs at production and debug levels capturing all significant system events,
So that I can monitor message flows, diagnose failures, and investigate incidents without direct database access.

**Source:** SPEC-017 | PRD §3 REQ-012
**Priority:** Must Have
**Story Points:** 2
**Flags:** none

**Acceptance Criteria:**

_AC-001 (Happy Path — production log event):_
- Given any message event (received, dispatched, suppressed, opt-out written, re-opt-in, report generated, auth failure, SLA event)
- When the event occurs
- Then a structured log entry is written at production level containing: timestamp, eventType, applicationId, messageId
- And the phone number is hashed (not plain text) in production-level logs

_AC-002 (Happy Path — debug log event):_
- Given debug logging is enabled (non-default, access-controlled)
- When a request is processed
- Then full request/response payloads and internal processing steps are logged with unhashed phone numbers

_AC-003 (Unhappy Path — debug mode is off by default):_
- Given a fresh production deployment
- When the logging configuration is read
- Then debug level is disabled
- And enabling it requires a configuration change (not a code change)

**Out of Scope for this story:** Log shipping to a centralised platform — that is an IT/infrastructure concern beyond this story's scope.

---

## EPIC-002: Inbound Message Processing
- **Description:** Receives customer SMS via Cool Text / Twilio webhook, classifies as opt-out or general reply, processes opt-outs, sends confirmations, and routes general replies back to Gas applications.
- **Source Specs:** SPEC-001, SPEC-002, SPEC-003, SPEC-004, SPEC-005
- **Priority:** Must Have
- **Personas:** PER-001 (Gas Customer), PER-002 (Gas Application — receives forwarded replies)
- **Delivery order:** Sprint 1 / Sprint 2 — depends on EPIC-001

---

### STORY-006: Inbound Webhook Endpoint
**User Story:**
As a Cool Text / Twilio messaging provider,
I want to push inbound customer SMS messages to a TCPA API webhook endpoint,
So that customer opt-out requests and general replies are received and processed by the compliance system.

**Source:** SPEC-001 | PRD §3 REQ-014, CQ-003
**Priority:** Must Have
**Story Points:** 3
**Flags:** [BLOCKED-BY: STORY-001, STORY-003]

**Acceptance Criteria:**

_AC-001 (Happy Path — valid inbound message):_
- Given a valid authenticated POST to /webhook/inbound with a well-formed payload
- When the request is received
- Then the system returns HTTP 200 within 5 seconds with status "received" and an internalId
- And the message is published to the inbound-messages Kafka topic for async processing

_AC-002 (Happy Path — idempotent duplicate):_
- Given a duplicate messageId (same provider message ID received twice)
- When the second request arrives
- Then the system returns HTTP 200 (idempotent)
- And the message is not re-published to Kafka

_AC-003 (Unhappy Path — invalid API key):_
- Given a POST with a missing or invalid X-Api-Key header
- When the request arrives
- Then the system returns HTTP 401
- And an authentication failure is written to the production log

_AC-004 (Unhappy Path — unregistered Cool Text account):_
- Given an inbound message with a `to` number not registered in the Cool Text account registry
- When the request arrives
- Then the system returns HTTP 400 with a descriptive error
- And the event is logged

_AC-005 (Unhappy Path — malformed payload):_
- Given a POST with a missing required field (from, to, body, messageId)
- When the request arrives
- Then the system returns HTTP 400 with field-level error detail

**Out of Scope for this story:** Keyword detection and opt-out processing — those are STORY-007 and STORY-008 (async, post-Kafka consume).

---

### STORY-007: Opt-Out Keyword Detection
**User Story:**
As the TCPA Regulatory Compliance API,
I want to detect exact opt-out keywords in inbound customer SMS messages,
So that a customer who sends STOP (or any of the seven TCPA keywords) is correctly identified for opt-out processing.

**Source:** SPEC-002 | PRD §3 REQ-003, PD-002
**Priority:** Must Have
**Story Points:** 2
**Flags:** [BLOCKED-BY: STORY-006]

**Acceptance Criteria:**

_AC-001 (Happy Path — exact keyword match):_
- Given an inbound message body of "STOP" (any case, with or without surrounding whitespace)
- When keyword detection runs
- Then isOptOut is true and matchedKeyword is "STOP"

_AC-002 (Happy Path — all seven keywords):_
- Given message bodies of "QUIT", "END", "REVOKE", "OPT-OUT", "CANCEL", "UNSUBSCRIBE" (each individually, any case)
- When keyword detection runs
- Then each returns isOptOut = true

_AC-003 (Unhappy Path — substring does not match):_
- Given message bodies such as "Please STOP texting", "STOPNOW", "I want to CANCEL my service"
- When keyword detection runs
- Then isOptOut is false for all cases

_AC-004 (Unhappy Path — near-match does not match):_
- Given a message body of "opt out" (space instead of hyphen)
- When keyword detection runs
- Then isOptOut is false (only "OPT-OUT" with a hyphen matches)

_AC-005 (Edge — empty body):_
- Given a message body that is null or empty after trimming
- When keyword detection runs
- Then isOptOut is false and the message is routed as a general reply

**Out of Scope for this story:** Acting on the opt-out classification — that is STORY-008.

---

### STORY-008: Opt-Out Status Write
**User Story:**
As a Gas Customer,
I want my opt-out request to be recorded immediately and permanently across all Southern Company Gas applications,
So that I no longer receive text messages regardless of which app originally sent them.

**Source:** SPEC-003, SPEC-009 (write path) | PRD §3 REQ-004, OOBR04
**Priority:** Must Have
**Story Points:** 3
**Flags:** [BLOCKED-BY: STORY-007, STORY-002]

**Acceptance Criteria:**

_AC-001 (Happy Path — first opt-out):_
- Given an inbound message classified as an opt-out keyword
- When opt-out processing runs
- Then the phone number's status is written as "opted-out" with effectiveAt timestamp
- And an audit record of type OPT_OUT_WRITTEN is written atomically
- And STORY-009 (confirmation dispatch) is triggered

_AC-002 (Happy Path — duplicate opt-out):_
- Given a phone number that is already opted-out
- When another opt-out keyword is received from that number
- Then no new opt-out record is created (idempotent)
- And an audit record of type OPT_OUT_WRITTEN is written noting it was a duplicate
- And a confirmation is still sent (AC-001 of STORY-009)

_AC-003 (Unhappy Path — database failure):_
- Given a database failure during the atomic write
- When the transaction attempts to commit
- Then both the status record and the audit record are rolled back
- And the confirmation is not triggered
- And an alert is raised

**Out of Scope for this story:** The confirmation message itself — that is STORY-009.

---

### STORY-009: Opt-Out Confirmation Dispatch
**User Story:**
As a Gas Customer,
I want to receive a confirmation SMS within 60 seconds of sending a STOP keyword,
So that I know my opt-out request was received and I understand how to re-opt-in if I change my mind.

**Source:** SPEC-004 | PRD §3 REQ-005, REQ-008, OOBR04, OOBR09
**Priority:** Must Have
**Story Points:** 5
**Flags:** [HIGH-RISK — RISK-004: Kafka consumer lag may cause SLA breach at peak load] [BLOCKED-BY: STORY-008, STORY-004]

**Acceptance Criteria:**

_AC-001 (Happy Path — confirmation sent within SLA):_
- Given a successful opt-out write
- When the confirmation dispatch runs
- Then a confirmation SMS is sent to the customer's phone number via the same Cool Text account that received the STOP
- And the message body is read from the system configuration store (not hardcoded)
- And dispatchedAt minus receivedAt is ≤ 60 seconds (P99)

_AC-002 (Happy Path — confirmation body is configurable):_
- Given the optOutMessageBody config value is updated
- When the next confirmation is dispatched
- Then the new wording is used without a service restart

_AC-003 (Unhappy Path — Cool Text dispatch fails):_
- Given a transient Cool Text / Twilio API failure
- When the first dispatch attempt fails
- Then the system retries up to 3 times with exponential backoff
- And if all retries fail, an audit record of type CONFIRMATION_FAILED is written and an alert is raised

_AC-004 (Unhappy Path — SLA breach):_
- Given processing delay causes dispatchedAt > receivedAt + 60 seconds
- When the confirmation is dispatched (even late)
- Then the confirmation is still sent
- And an audit record of type SLA_BREACH is written
- And a production log alert is raised

_AC-005 (Unhappy Path — missing config):_
- Given the optOutMessageBody config key is missing or empty
- When confirmation dispatch attempts to read it
- Then the dispatch is halted, an alert is raised, and the audit record is marked "confirmation-failed"

**Out of Scope for this story:** Legal review of the message wording — tracked as RISK-001 go-live blocker separately.

**Notes:** P99 latency measurement must be instrumented from receivedAt (webhook receipt) to dispatchedAt (Cool Text API call). Load test at 120% peak is a go-live gate (RISK-004 mitigation).

---

### STORY-010: General Reply Forwarding
**User Story:**
As a Southern Company Gas Application,
I want non-opt-out customer replies to be forwarded to my registered callback URL,
So that my application can handle customer responses without bypassing the TCPA API.

**Source:** SPEC-005 | PRD §3 REQ-002, OOBR02, CQ-001
**Priority:** Must Have
**Story Points:** 3
**Flags:** [BLOCKED-BY: STORY-006, STORY-003]

**Acceptance Criteria:**

_AC-001 (Happy Path — reply forwarded):_
- Given an inbound customer message that is not an opt-out keyword
- When the message processor handles it
- Then the exact original message body is POSTed to the Gas application's registered callbackUrl
- And the callbackUrl is resolved from the CoolTextAccount registry using the inbound `to` account number

_AC-002 (Unhappy Path — callback unreachable):_
- Given the Gas application callback URL returns a non-2xx response or times out
- When the forward attempt completes
- Then the failure is logged with the application ID, message ID, and HTTP status
- And no retry is attempted (best-effort delivery for general replies)
- And the Gas application IT team is alerted

_AC-003 (Edge — opt-out keyword routed here in error):_
- Given an opt-out keyword message that reaches the forwarding step (should not occur)
- When the system detects it is a keyword
- Then the forward is halted and an anomaly alert is raised

**Out of Scope for this story:** Retry logic for failed application callbacks — general replies are best-effort per SPEC-005 BR-017.

---

## EPIC-003: Outbound Message Gateway
- **Description:** Accepts outbound SMS requests from Gas applications, enforces opt-out suppression at queue time and send time, and dispatches clean messages to Cool Text / Twilio.
- **Source Specs:** SPEC-006 (split), SPEC-007, SPEC-008
- **Priority:** Must Have
- **Personas:** PER-002 (Gas Application — submits outbound messages)
- **Delivery order:** Sprint 2 — depends on EPIC-001

---

### STORY-011: Outbound Message Submission API
**User Story:**
As a Southern Company Gas Application,
I want to submit outbound SMS messages through the TCPA API instead of calling Cool Text directly,
So that every message is centrally validated and opted-out numbers are suppressed before dispatch.

**Source:** SPEC-006 (core) | PRD §3 REQ-014, CQ-003
**Priority:** Must Have
**Story Points:** 5
**Flags:** [HIGH-RISK — RISK-002: TCPA.Api outage blocks all Gas app SMS] [BLOCKED-BY: STORY-013]

**Acceptance Criteria:**

_AC-001 (Happy Path — message queued):_
- Given a valid authenticated POST to /api/v1/messages/outbound with an opted-in destination number
- When the request is processed
- Then the system returns HTTP 200 with status "queued" and a messageId
- And the message is published to the outbound-messages Kafka topic

_AC-002 (Happy Path — message suppressed at queue time):_
- Given a valid request where the destination number is opted-out
- When the queue-time opt-out check runs
- Then the system returns HTTP 200 with status "suppressed" and suppressionReason "opted-out"
- And no message is published to Kafka

_AC-003 (Happy Path — idempotent resubmission):_
- Given a request with a correlationId that matches a previously processed request
- When the duplicate arrives
- Then the original response is returned without re-queuing

_AC-004 (Unhappy Path — unregistered Cool Text account):_
- Given a request with a coolTextAccountNumber not in the registry
- When the request is processed
- Then the system returns HTTP 400 with a descriptive error

_AC-005 (Unhappy Path — TCPA API overloaded):_
- Given the system is under extreme load
- When it cannot safely process the request
- Then it returns HTTP 503 with a Retry-After header
- And the calling application must not bypass the API to send directly

**Out of Scope for this story:** The fail-safe HA behaviour and resilience patterns — those are STORY-012.

---

### STORY-012: Fail-Safe Resilience
**User Story:**
As a Southern Company Gas Application,
I want the TCPA API to signal clearly when it is unavailable so I can block the send,
So that no SMS is ever sent to an opted-out number due to an API availability failure.

**Source:** SPEC-006 (fail-safe) | PRD §3 CQ-004, ARCH-RISK-001, ADR-005
**Priority:** Must Have
**Story Points:** 3
**Flags:** [HIGH-RISK — RISK-002: this story implements the core fail-safe guarantee] [BLOCKED-BY: STORY-011]

**Acceptance Criteria:**

_AC-001 (Happy Path — health check passes):_
- Given all dependencies (database, Kafka, auth service) are healthy
- When GET /api/v1/health is called
- Then HTTP 200 is returned with status "healthy" and per-dependency check results

_AC-002 (Unhappy Path — dependency unhealthy):_
- Given one or more dependencies are unreachable
- When GET /api/v1/health is called
- Then HTTP 503 is returned
- And the NLB load balancer removes the node from rotation within 30 seconds

_AC-003 (Unhappy Path — calling app handles 503):_
- Given the TCPA API returns 503 or times out on an outbound submission request
- When the Gas application receives the response
- Then the calling application must treat this as a send-blocking error (documented in OpenAPI spec and integration guide)
- And no message is dispatched to Cool Text by the calling application directly

_AC-004 (Operational — recovery):_
- Given a previously unhealthy node whose dependencies have recovered
- When the health check returns 200
- Then the NLB returns the node to rotation automatically

**Notes:** The Gas application integration contract (fail-safe behaviour on 503/timeout) must be documented in the OpenAPI spec and communicated to all four integration teams as part of RISK-003 mitigation.

---

### STORY-013: Queue-Time Opt-Out Check
**User Story:**
As a Southern Company Gas Application,
I want to receive an immediate response telling me whether my message was suppressed before it was queued,
So that my application can log the suppression and avoid unnecessary processing downstream.

**Source:** SPEC-007 | PRD §3 REQ-006, OOBR06, CQ-006
**Priority:** Must Have
**Story Points:** 3
**Flags:** [BLOCKED-BY: STORY-001, STORY-011]

**Acceptance Criteria:**

_AC-001 (Happy Path — opted-in number passes check):_
- Given an outbound message request for an opted-in destination number
- When the queue-time check runs synchronously in the API
- Then the check completes within 100ms P99
- And the message proceeds to the Kafka outbound-messages topic

_AC-002 (Happy Path — opted-out number suppressed):_
- Given an outbound message request for an opted-out destination number
- When the queue-time check runs
- Then the message is suppressed immediately
- And a suppression audit record (type SUPPRESSED_QUEUE_TIME) is written
- And the caller receives status "suppressed"

_AC-003 (Unhappy Path — status store unavailable):_
- Given the opt-out status store is unreachable during the queue-time check
- When the check runs
- Then the API returns HTTP 503 (fail-safe — cannot confirm status)
- And no message is published to Kafka

**Out of Scope for this story:** The send-time check — that is STORY-014.

---

### STORY-014: Send-Time Opt-Out Check
**User Story:**
As the TCPA Regulatory Compliance API,
I want to perform a final opt-out check immediately before dispatching a message to Cool Text / Twilio,
So that messages queued before a STOP was received are suppressed before they reach the customer.

**Source:** SPEC-008 | PRD §3 REQ-006, OOBR06, CQ-006, CQ-007
**Priority:** Must Have
**Story Points:** 3
**Flags:** [BLOCKED-BY: STORY-013]

**Acceptance Criteria:**

_AC-001 (Happy Path — number still opted-in at send time):_
- Given a queued message where the destination number is still opted-in
- When the send-time check runs in OutboundDispatcher
- Then the message is dispatched to Cool Text / Twilio
- And a dispatch audit record is written

_AC-002 (Happy Path — race-condition edge case handled):_
- Given a message queued before a STOP was received (queuedAt < optOutEffectiveAt)
- When the send-time check runs and finds the number is now opted-out
- Then the message is suppressed
- And an audit record of type SUPPRESSED_SEND_TIME is written with both queuedAt and optOutEffectiveAt timestamps
- And the suppression is classified as an accepted edge case (not a violation)

_AC-003 (Unhappy Path — opt-out already in effect at queue time):_
- Given a message that passed queue-time check despite the number being opted-out at that time (queue-time check failure)
- When the send-time check detects optOutEffectiveAt <= queuedAt
- Then the message is suppressed
- And a POTENTIAL_VIOLATION alert is raised immediately
- And an audit record flags the event as requiring investigation

_AC-004 (Unhappy Path — status store unavailable at send time):_
- Given the status store is unreachable during send-time check
- When the check runs
- Then the message is suppressed (fail-safe)
- And an alert is raised

**Out of Scope for this story:** Cool Text / Twilio dispatch — that is part of STORY-011's downstream processing. This story covers only the check logic in OutboundDispatcher.

---

## EPIC-004: Admin & Re-Opt-In
- **Description:** Provides the Help Desk with an authenticated API endpoint to manually re-opt-in customers who previously sent a STOP keyword.
- **Source Specs:** SPEC-011
- **Priority:** Must Have
- **Personas:** PER-003 (Help Desk Agent)
- **Delivery order:** Sprint 2 — depends on EPIC-001

---

### STORY-015: Admin Re-Opt-In API
**User Story:**
As a Help Desk Agent,
I want to update a customer's opt-out status to opted-in via an authenticated API,
So that a customer who calls to reverse their STOP request can resume receiving text messages without any code deployment.

**Source:** SPEC-011 | PRD §3 REQ-007, OOBR07, CQ-005, CQ-011
**Priority:** Must Have
**Story Points:** 5
**Flags:** [BLOCKED-BY: STORY-001, STORY-002]

**Acceptance Criteria:**

_AC-001 (Happy Path — successful re-opt-in):_
- Given a valid admin API key and a POST to /api/v1/admin/reopt-in with phoneNumber, reason, and agentId
- When the request is processed
- Then the phone number's status is updated to "opted-in" with a new effectiveAt timestamp
- And an audit record of type RE_OPT_IN is written atomically with: phoneNumber, agentId, reason, effectiveAt
- And the response includes reOptInId and the new status

_AC-002 (Happy Path — re-opt-in for number with no prior opt-out):_
- Given a phone number that has no opt-out record
- When a re-opt-in is submitted
- Then the status is set to "opted-in"
- And an audit record is written with an anomaly note
- And the response is successful (not an error)

_AC-003 (Unhappy Path — rate limit exceeded):_
- Given more than 10 requests per minute from the same API key
- When the 11th request arrives within the minute window
- Then the system returns HTTP 429 with Retry-After: 60
- And no status change is made

_AC-004 (Unhappy Path — missing required field):_
- Given a request missing phoneNumber, reason, or agentId
- When the request arrives
- Then the system returns HTTP 400 with field-level validation errors

_AC-005 (Unhappy Path — invalid API key):_
- Given a request with an invalid or missing admin API key
- When the request arrives
- Then the system returns HTTP 401

_AC-006 (Unhappy Path — atomic write failure):_
- Given a database failure during the status + audit atomic write
- When the transaction attempts to commit
- Then both writes are rolled back
- And HTTP 500 is returned
- And an alert is raised

**Out of Scope for this story:** A UI for the Help Desk — Phase 1 is API only. Help Desk tooling team integrates against the OpenAPI spec.

**Notes:** Admin-scope API keys must be distinct from standard Gas application keys (RISK-008 mitigation). Auth service team must provision admin-scoped keys separately.

---

## EPIC-005: Reporting
- **Description:** Generates three weekly reports: opted-in message volume, opted-out message volume, and the primary weekly compliance report. All delivered by email Monday 06:00 Eastern.
- **Source Specs:** SPEC-012, SPEC-013, SPEC-014
- **Priority:** Must Have
- **Personas:** PER-004 (Compliance / Audit Team — primary recipient)
- **Delivery order:** Sprint 3 — depends on EPIC-001 through EPIC-003 producing data

---

### STORY-016: Opted-In Message Volume Report
**User Story:**
As a Compliance / Audit Team member,
I want to receive a weekly email report showing all SMS messages successfully dispatched to opted-in customers,
So that I can verify the system is operating correctly and producing an evidence trail for TCPA compliance.

**Source:** SPEC-012 | PRD §3 REQ-009, CQ-002, CQ-010
**Priority:** Must Have
**Story Points:** 3
**Flags:** [BLOCKED-BY: STORY-004, STORY-011]

**Acceptance Criteria:**

_AC-001 (Happy Path — report generated and emailed):_
- Given it is Monday at 06:00 US Eastern
- When the report job runs
- Then an email is sent to the configurable opted-in report recipient list
- And the email contains: reporting period (Mon–Sun prior week), total messages dispatched to opted-in numbers, breakdown by application, breakdown by day

_AC-002 (Happy Path — data accuracy):_
- Given 500 messages dispatched to opted-in numbers during the reporting week
- When the report is generated
- Then the total count is 500 (suppressed messages are excluded)

_AC-003 (Unhappy Path — email delivery failure):_
- Given the SMTP relay is unavailable when the report job runs
- When the email send fails
- Then the failure is logged, an alert is raised, and the report data is retained for manual re-send

_AC-004 (Edge — no messages in period):_
- Given zero messages were dispatched in the reporting week
- When the report is generated
- Then the report is still sent with a zero count (not suppressed)

**Out of Scope for this story:** PDF or formatted attachment — plain text or HTML email body is sufficient for Phase 1.

---

### STORY-017: Opted-Out Message Volume Report
**User Story:**
As a Compliance / Audit Team member,
I want to receive a weekly email report showing all outbound SMS messages suppressed because the destination number was opted-out,
So that I can verify suppression is working correctly and quantify the volume of blocked messages.

**Source:** SPEC-013 | PRD §3 REQ-010, CQ-002, CQ-010
**Priority:** Must Have
**Story Points:** 3
**Flags:** [BLOCKED-BY: STORY-013, STORY-014]

**Acceptance Criteria:**

_AC-001 (Happy Path — report generated and emailed):_
- Given it is Monday at 06:00 US Eastern
- When the report job runs
- Then an email is sent to the configurable opted-out report recipient list
- And the email contains: reporting period, total suppressions, breakdown by application, breakdown by suppression type (queue-time vs. send-time), breakdown by day, count of accepted race-condition edge cases

_AC-002 (Happy Path — each suppressed message counted once):_
- Given a message suppressed at both queue time and send time (should not occur by design, but defensively)
- When the report counts suppressions
- Then the message appears once in the total

_AC-003 (Unhappy Path — email delivery failure):_
- Given SMTP failure at report time
- When the email send fails
- Then the failure is logged and alerted; data retained for re-send

**Out of Scope for this story:** Individual phone number detail in the report — aggregated counts only in Phase 1.

---

### STORY-018: Weekly Compliance Report
**User Story:**
As a Compliance / Audit Team member,
I want to receive a weekly email compliance report summarising the full opt-out enforcement picture,
So that I have a TCPA audit artefact demonstrating the system is enforcing opt-outs correctly every week.

**Source:** SPEC-014 | PRD §3 REQ-011, RPBR03, CQ-009, CQ-010
**Priority:** Must Have
**Story Points:** 5
**Flags:** [HIGH-RISK — RISK-001: opt-out confirmation wording pending Legal approval; report content depends on STORY-009 wording being finalised] [BLOCKED-BY: STORY-016, STORY-017, STORY-009]

**Acceptance Criteria:**

_AC-001 (Happy Path — report generated and emailed):_
- Given it is Monday at 06:00 US Eastern
- When the compliance report job runs
- Then an email is sent to the configurable compliance report recipient list
- And the report contains: reporting period, total STOP requests received, confirmations sent within 60s (count + %), SLA breaches (count, individually listed), cumulative opted-out numbers, total suppressions, total dispatched messages, opt-out suppression rate (%), re-opt-ins performed, alerts triggered

_AC-002 (Happy Path — suppression rate below 100% triggers alert):_
- Given any non-race-condition message reached an opted-out number during the reporting week
- When the compliance report is generated
- Then the suppression rate is flagged below 100% in the report body
- And an alert is included identifying the incident

_AC-003 (Happy Path — SLA breach listed individually):_
- Given 3 confirmations breached the 60-second SLA during the reporting week
- When the report is generated
- Then each breach is listed individually with phone number hash, timestamp, and actual latency

_AC-004 (Unhappy Path — email delivery failure):_
- Given SMTP failure at report time
- When the email send fails
- Then the failure is logged and alerted immediately (compliance report failure is higher severity than volume reports)

_AC-005 (Edge — recipient list empty):_
- Given the compliance report recipient list is empty in the config store
- When the report job runs
- Then the job halts before sending, raises an alert, and logs the misconfiguration

**Out of Scope for this story:** Report archival to a shared drive or document management system — Phase 1 is email delivery only.

**Notes:** This is the primary TCPA audit artefact. Recipient list configuration must be validated before go-live. Legal wording approval (RISK-001) does not block this story's implementation — the report references the wording via STORY-009's config-driven approach.

---

## Dependency Map

```
STORY-001 (Status Store)
  └─► STORY-008 (Opt-Out Write)
  └─► STORY-013 (Queue-Time Check)
  └─► STORY-014 (Send-Time Check)
  └─► STORY-015 (Admin Re-Opt-In)

STORY-002 (Audit Log)
  └─► STORY-008 (Opt-Out Write)
  └─► STORY-015 (Admin Re-Opt-In)

STORY-003 (Cool Text Registry)
  └─► STORY-006 (Inbound Webhook)
  └─► STORY-010 (General Reply Forwarding)
  └─► STORY-011 (Outbound Submission)

STORY-004 (Config Store)
  └─► STORY-009 (Confirmation Dispatch)

STORY-006 (Inbound Webhook)
  └─► STORY-007 (Keyword Detection)
        └─► STORY-008 (Opt-Out Write)
              └─► STORY-009 (Confirmation)
        └─► STORY-010 (Reply Forward)

STORY-011 (Outbound Submission)
  └─► STORY-012 (Fail-Safe)

STORY-013 (Queue-Time Check)
  └─► STORY-011 (gates outbound submission)
  └─► STORY-014 (Send-Time Check)

STORY-014 └─► STORY-017 (Opted-Out Report data source)
STORY-011 └─► STORY-016 (Opted-In Report data source)
STORY-009 └─► STORY-018 (Compliance Report references confirmation SLA data)
STORY-016 + STORY-017 └─► STORY-018 (Compliance Report)
```

## Traceability Matrix

| Story     | Spec(s)           | PRD Ref           | Component              | Priority  |
|-----------|-------------------|-------------------|------------------------|-----------|
| STORY-001 | SPEC-009          | REQ-004, REQ-006  | SQL Server             | Must Have |
| STORY-002 | SPEC-010          | REQ-001, NFR-003  | SQL Server             | Must Have |
| STORY-003 | SPEC-015          | REQ-013           | SQL Server             | Must Have |
| STORY-004 | SPEC-016          | REQ-008, REQ-011  | SQL Server             | Must Have |
| STORY-005 | SPEC-017          | REQ-012           | All components         | Must Have |
| STORY-006 | SPEC-001          | REQ-014           | TCPA.Api               | Must Have |
| STORY-007 | SPEC-002          | REQ-003           | TCPA.MessageProcessor  | Must Have |
| STORY-008 | SPEC-003          | REQ-004           | TCPA.MessageProcessor  | Must Have |
| STORY-009 | SPEC-004          | REQ-005, REQ-008  | TCPA.MessageProcessor  | Must Have |
| STORY-010 | SPEC-005          | REQ-002           | TCPA.MessageProcessor  | Must Have |
| STORY-011 | SPEC-006 (core)   | REQ-014           | TCPA.Api               | Must Have |
| STORY-012 | SPEC-006 (HA)     | CQ-004            | TCPA.Api               | Must Have |
| STORY-013 | SPEC-007          | REQ-006           | TCPA.Api               | Must Have |
| STORY-014 | SPEC-008          | REQ-006           | TCPA.OutboundDispatcher| Must Have |
| STORY-015 | SPEC-011          | REQ-007           | TCPA.Api               | Must Have |
| STORY-016 | SPEC-012          | REQ-009           | TCPA.ReportService     | Must Have |
| STORY-017 | SPEC-013          | REQ-010           | TCPA.ReportService     | Must Have |
| STORY-018 | SPEC-014          | REQ-011           | TCPA.ReportService     | Must Have |
