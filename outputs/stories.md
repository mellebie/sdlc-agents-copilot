<!-- SDLC Pipeline Artifact
     Stage: 06-story-writer
     Source PRD: inputs/prd.md
     PRD Sections: §1 Overview, §2 Personas, §3 Functional Requirements, §4 Non-Functional Requirements, §5 Constraints, §6 Out of Scope, §7 Success Metrics, §8 Assumptions, §9 Dependencies
     Generated: 2026-06-26
     Status: DRAFT
-->

# Product Backlog — TCPA Regulatory Compliance for Text Messages

## Backlog Summary
- Total epics: 7
- Total stories: 24
- Must Have stories: 24
- Should Have stories: 0
- Could Have stories: 0
- High-risk stories: 10
- Spike stories: 2

---

## EPIC-001: SMS Proxy & Routing
- **Description:** Intercepts all outbound SMS from in-scope applications, enforces the opt-out compliance gate, and routes inbound SMS webhook replies back to the correct originating application.
- **Source Specs:** SPEC-001, SPEC-002, SPEC-006, SPEC-014
- **Priority:** Must Have
- **Personas:** PER-002 (Application System), PER-005 (IT / Platform Engineer)

---

### STORY-001: Application Registration Lookup Foundation
**User Story:**
As an Application System,
I want the TCPA API to identify which SCG application I belong to based on my Cool Text account ID,
So that compliance enforcement and routing decisions can be scoped correctly to my application.

**Source:** SPEC-014 | PRD §3
**Priority:** Must Have
**Story Points:** 3
**Flags:** [BLOCKED-BY: none — this is the foundational story all others depend on]

**Acceptance Criteria:**

_AC-001 (Happy Path — Registered Active Application):_
- Given a Cool Text account ID that is registered and active in the Application Registry
- When the TCPA API receives any inbound or outbound request bearing that account ID
- Then the system resolves the application name, callback URL, and active status from the registry
- And the resolved application context is available for routing, logging, and compliance decisions

_AC-002 (Unhappy Path — Unregistered Account):_
- Given a Cool Text account ID that has no entry in the Application Registry
- When the TCPA API receives a request bearing that account ID
- Then the system treats it as an unregistered pass-through with no enforcement and no compliance event logged
- And a warning is emitted to the operational log

_AC-003 (Edge Case — Registered but Inactive Application):_
- Given a Cool Text account ID that is registered but has active = false
- When the TCPA API receives a request bearing that account ID
- Then the system treats it as unregistered (no enforcement, no compliance event)

_AC-004 (Edge Case — Registry Cache):_
- Given the Application Registry is loaded into in-memory cache at startup with a 5-minute TTL
- When a request arrives within the TTL window
- Then the lookup completes without a database round-trip
- And cache entries are refreshed on TTL expiry or service restart

**Out of Scope for this story:** Runtime admin API to add or modify registry entries (Phase 1 is IT-managed configuration deployment only). Changes to which applications are registered.

**Notes:** This story must be completed first — all other Epic-001 stories depend on it. CCB/My Account is pre-loaded with active = false at deployment time per RISK-003. Callback URL must be HTTPS; reject any registry entry with a non-HTTPS callback on startup validation.

---

### STORY-002: Outbound SMS Compliance Gate — Forward or Suppress
**User Story:**
As an Application System,
I want to send an outbound SMS through the TCPA API and receive an immediate decision on whether it was forwarded or suppressed,
So that my application does not send text messages to customers who have opted out.

**Source:** SPEC-001, SPEC-006 | PRD §3
**Priority:** Must Have
**Story Points:** 5
**Flags:** [HIGH-RISK] [BLOCKED-BY: STORY-001]

**Acceptance Criteria:**

_AC-001 (Happy Path — OPT-IN Number Forwarded):_
- Given a valid POST /api/v1/sms/outbound request with a registered Cool Text account ID and a destination cell number that is OPT-IN (or has no status record)
- When the TCPA API receives the request with a valid API key
- Then the message is forwarded to Cool Text/Twilio unchanged
- And the response returns status = "FORWARDED" with the Cool Text message ID

_AC-002 (Unhappy Path — OPT-OUT Number Suppressed):_
- Given a POST /api/v1/sms/outbound request for a destination cell number that has OPT-OUT status
- When the TCPA API checks the opt-out status database
- Then the message is NOT forwarded to Cool Text/Twilio
- And the response returns status = "SUPPRESSED" with suppression_reason = "OPT_OUT"

_AC-003 (Edge Case — Unregistered Cool Text Account):_
- Given a POST /api/v1/sms/outbound request with a Cool Text account ID not in the Application Registry
- When the TCPA API receives the request
- Then the message is forwarded to Cool Text/Twilio without compliance enforcement
- And the response returns status = "UNREGISTERED_ACCOUNT"
- And no compliance event is logged

_AC-004 (Fail-Closed — Database Unavailable):_
- Given the TCPA opt-out status database is unreachable
- When an outbound SMS request is received
- Then the message is NOT forwarded (fail-closed)
- And the API returns 503 Service Unavailable with message "Compliance check unavailable; message not forwarded."

_AC-005 (Input Validation):_
- Given a POST /api/v1/sms/outbound request with a missing required field (cool_text_account_id, destination_cell_number, or message_body) or a destination_cell_number not in E.164 format
- When the TCPA API receives the request
- Then the API returns 400 Bad Request with field-level error detail
- And the message is not forwarded

_AC-006 (Authentication):_
- Given a POST /api/v1/sms/outbound request with a missing or invalid X-API-Key header
- When the TCPA API processes the request
- Then the API returns 401 Unauthorized
- And the request is not processed further

**Out of Scope for this story:** Opt-out audit logging for suppressed messages (STORY-009). Writing the blocked-outbound audit log record is a separate concern covered in STORY-009.

**Notes:** [HIGH-RISK] due to RISK-001 (BizTalk REST adapter must be confirmed before integration testing), RISK-008 (compliance gate read must target primary DB, not read replica — verify with Architecture Lead before implementing the DB read path). Cell numbers default to OPT-IN if no record exists (BR-001). The compliance gate DB read must be a primary database read — not the read replica — per RISK-008 resolution.

---

### STORY-003: Inbound SMS Webhook — Receive and Route to Application
**User Story:**
As an Application System,
I want inbound SMS replies from customers to be delivered to my registered callback URL,
So that my application can process customer responses without managing a direct integration with Cool Text.

**Source:** SPEC-002 | PRD §3
**Priority:** Must Have
**Story Points:** 3
**Flags:** [HIGH-RISK] [BLOCKED-BY: STORY-001, STORY-004]

**Acceptance Criteria:**

_AC-001 (Happy Path — Non-Opt-Out Reply Routed to Application):_
- Given Cool Text sends a valid inbound webhook POST to /api/v1/sms/inbound with a registered Cool Text account ID and a message body that does not contain an opt-out keyword
- When the TCPA API receives the webhook
- Then the system immediately returns 200 OK to Cool Text
- And the message is forwarded to the registered application callback URL with sender_cell_number, message_body, cool_text_account_id, and received_timestamp
- And the callback is attempted up to 3 times with exponential backoff if the initial delivery fails

_AC-002 (Unhappy Path — Opt-Out Keyword Detected, Not Forwarded):_
- Given Cool Text sends an inbound webhook with a message body containing an opt-out keyword (e.g., "STOP")
- When the TCPA API processes the webhook
- Then the message is NOT forwarded to the application callback URL
- And opt-out processing is triggered (see STORY-004)

_AC-003 (Edge Case — Unregistered Cool Text Account):_
- Given Cool Text sends an inbound webhook with a Cool Text account ID not in the Application Registry
- When the TCPA API receives the webhook
- Then the system returns 200 OK to Cool Text
- And the message is discarded with a warning emitted to the operational log

_AC-004 (Edge Case — Application Callback Permanently Unreachable):_
- Given all 3 delivery attempts to the application callback URL fail
- When the final retry is exhausted
- Then a permanent delivery failure is logged to the operational log
- And no further action is taken (no indefinite retry)

_AC-005 (Webhook Authentication):_
- Given an inbound webhook request arrives at /api/v1/sms/inbound with an invalid or missing HMAC signature
- When the TCPA API validates the signature
- Then the request is rejected with 401 Unauthorized
- And the event is logged as a security event

**Out of Scope for this story:** Opt-out keyword detection logic (STORY-004). Application registration (STORY-001).

**Notes:** [HIGH-RISK] due to RISK-005 (Cool Text HMAC webhook signing must be confirmed with vendor before implementing AC-005 — if HMAC unavailable, fall back to secret header token per risk mitigation). Return 200 OK to Cool Text immediately; all downstream processing (keyword detection, application forwarding, audit logging) occurs after the 200 acknowledgement to avoid Cool Text timeout retries.

---

### STORY-003-SPIKE: Cool Text Webhook Signing Mechanism Confirmation
**User Story:**
As an IT / Platform Engineer,
I want to confirm what authentication mechanism Cool Text uses to sign inbound webhook payloads,
So that the TCPA API can correctly validate webhook authenticity and prevent injection of fake opt-out keywords.

**Source:** RISK-005, ADR-007 | PRD §3
**Priority:** Must Have
**Story Points:** 2
**Flags:** [SPIKE: timebox 8h] [HIGH-RISK]

**Acceptance Criteria:**

_AC-001 (Spike Output):_
- Given the spike is complete
- When the findings are documented
- Then a written confirmation from the Cool Text vendor specifies: (a) whether HMAC-SHA256 payload signing is supported, (b) the signing algorithm and header name, (c) the inbound webhook payload schema, and (d) any IP ranges for Cool Text webhook origins
- And an architecture decision is documented on whether to implement HMAC, secret header token, or IP allowlisting

_AC-002 (Spike Blocker Condition):_
- Given HMAC is confirmed as unavailable
- When the spike concludes
- Then the fallback authentication approach is agreed and documented before STORY-003 implementation begins

**Out of Scope for this story:** Implementation of the webhook authentication — only the vendor confirmation and decision are in scope.

**Notes:** This spike is a hard prerequisite for finalizing the STORY-003 authentication implementation. Timebox: 8 hours. If the vendor cannot respond within the spike timebox, escalate and proceed with the secret-header-token fallback design.

---

## EPIC-002: Opt-Out Management
- **Description:** Detects opt-out keywords in inbound SMS, writes the opt-out status, sends the standardized opt-out confirmation SMS, and enforces the immediate block on future outbound messages to opted-out numbers.
- **Source Specs:** SPEC-003, SPEC-004, SPEC-005, SPEC-006
- **Priority:** Must Have
- **Personas:** PER-001 (Gas Customer), PER-002 (Application System)

---

### STORY-004: Opt-Out Keyword Detection
**User Story:**
As a Gas Customer,
I want the TCPA API to recognize when I send an opt-out keyword such as STOP in a reply,
So that my opt-out request is correctly identified and processed without requiring an exact word match.

**Source:** SPEC-003 | PRD §3
**Priority:** Must Have
**Story Points:** 2
**Flags:** [BLOCKED-BY: none — pure logic, no external dependencies]

**Acceptance Criteria:**

_AC-001 (Happy Path — Keyword STOP):_
- Given an inbound SMS message body of "STOP"
- When the keyword detector processes the message
- Then is_opt_out_keyword = true and matched_keyword = "STOP"

_AC-002 (Happy Path — Keyword in Sentence):_
- Given an inbound SMS message body of "Please stop sending me texts"
- When the keyword detector processes the message
- Then is_opt_out_keyword = true (word-boundary match on "stop")

_AC-003 (Unhappy Path — Substring Does Not Match):_
- Given an inbound SMS message body of "NONSTOP service is great"
- When the keyword detector processes the message
- Then is_opt_out_keyword = false (STOP is not a standalone word)

_AC-004 (Unhappy Path — Substring Does Not Match 2):_
- Given an inbound SMS message body of "CANCELLATION confirmed"
- When the keyword detector processes the message
- Then is_opt_out_keyword = false (CANCEL must appear as a complete word)

_AC-005 (Hyphenated Keyword — OPT-OUT):_
- Given an inbound SMS message body of "OPT-OUT"
- When the keyword detector processes the message
- Then is_opt_out_keyword = true and matched_keyword = "OPT-OUT"

_AC-006 (Partial Keyword — OPT Without -OUT):_
- Given an inbound SMS message body of "OPT in please"
- When the keyword detector processes the message
- Then is_opt_out_keyword = false

_AC-007 (Case Insensitivity):_
- Given inbound message bodies of "stop", "Stop", "STOP", "sToP"
- When the keyword detector processes each message
- Then is_opt_out_keyword = true for all four variants

_AC-008 (All Seven Keywords):_
- Given inbound messages each containing exactly one of: STOP, QUIT, END, REVOKE, OPT-OUT, CANCEL, UNSUBSCRIBE (as standalone words)
- When the keyword detector processes each message
- Then is_opt_out_keyword = true for every one of the seven messages

_AC-009 (Empty Message):_
- Given an inbound SMS message body is empty or null
- When the keyword detector processes the message
- Then is_opt_out_keyword = false (treat as no match; log warning)

**Out of Scope for this story:** Writing the opt-out status to the database (STORY-005). Sending the confirmation SMS (STORY-006). Logging the opt-out event (STORY-008).

**Notes:** The 7 TCPA-mandated keywords are: STOP, QUIT, END, REVOKE, OPT-OUT, CANCEL, UNSUBSCRIBE. Matching is word-boundary exact and case-insensitive. OPT-OUT is matched as a hyphenated token. Implement as a pure, stateless function for easy unit testing.

---

### STORY-005: Opt-Out Status Write
**User Story:**
As a Gas Customer,
I want my opt-out to be recorded in the TCPA system immediately when I send an opt-out keyword,
So that no further SMS messages are sent to my phone number from any SCG application.

**Source:** SPEC-004 | PRD §3
**Priority:** Must Have
**Story Points:** 3
**Flags:** [HIGH-RISK] [BLOCKED-BY: STORY-004]

**Acceptance Criteria:**

_AC-001 (Happy Path — New Opt-Out Written):_
- Given the keyword detector has confirmed an opt-out keyword in an inbound message from a cell number with no prior opt-out record
- When the Opt-Out Status Writer processes the event
- Then an OPT-OUT status record is created for the cell number with the inbound message receipt timestamp as opt_out_timestamp
- And status_write_success = true and previous_status = OPT_IN

_AC-002 (Idempotent — Already OPT-OUT):_
- Given the cell number is already OPT-OUT and sends another opt-out keyword
- When the Opt-Out Status Writer processes the event
- Then no new record is created (idempotent operation)
- And status_write_success = true and previous_status = OPT_OUT
- And no confirmation SMS is re-sent

_AC-003 (Happy Path — Global Scope):_
- Given a cell number opts out via a message to the GCMA Cool Text account
- When the opt-out status is written
- Then the OPT-OUT status applies globally across all five in-scope SCG applications (not scoped to GCMA only)

_AC-004 (Unhappy Path — Database Write Failure):_
- Given the TCPA database is unavailable when the opt-out status write is attempted
- When the Opt-Out Status Writer encounters the error
- Then status_write_success = false
- And the confirmation SMS is NOT sent (BR-017)
- And a critical error is logged and an alert is triggered to the operations team
- And the opt-out status write is not silently dropped

**Out of Scope for this story:** Sending the confirmation SMS (STORY-006). Writing the audit log (STORY-008).

**Notes:** [HIGH-RISK] due to RISK-008 — the opt-out status write is the trigger for immediate enforcement on the outbound gate. The write timestamp must be the inbound message receipt timestamp, not the DB write timestamp (BR-018). The status write and audit log write (STORY-008) must both succeed — STORY-005 and STORY-008 are designed to be atomic where technically feasible (same DB, same transaction scope).

---

### STORY-006: Opt-Out Confirmation SMS Dispatch
**User Story:**
As a Gas Customer,
I want to receive a confirmation text message within 60 seconds of sending an opt-out keyword,
So that I know my request was received and that I will no longer receive text messages from Southern Company Gas.

**Source:** SPEC-005 | PRD §3
**Priority:** Must Have
**Story Points:** 3
**Flags:** [HIGH-RISK] [BLOCKED-BY: STORY-005]

**Acceptance Criteria:**

_AC-001 (Happy Path — Confirmation Sent Within SLA):_
- Given the opt-out status write completed successfully for a cell number
- When the confirmation SMS dispatcher is triggered
- Then a standardized opt-out confirmation SMS is sent via Cool Text to the customer's cell number
- And the message is dispatched within 60 seconds of the inbound message receipt timestamp
- And confirmation_sent = true with the Cool Text message ID
- And sla_elapsed_seconds is recorded

_AC-002 (Confirmation Uses Correct Cool Text Account):_
- Given the customer opted out via a message received on the KMI Cool Text account
- When the confirmation SMS is dispatched
- Then the confirmation is sent from the KMI Cool Text account (same sender the customer messaged)

_AC-003 (Already OPT-OUT — No Re-Confirmation):_
- Given the cell number was already OPT-OUT when the opt-out keyword was received
- When the confirmation dispatcher is evaluated
- Then NO confirmation SMS is sent (BR-015, BR-023)

_AC-004 (Unhappy Path — Cool Text Unavailable):_
- Given Cool Text is unavailable when the confirmation SMS is dispatched
- When the dispatcher attempts delivery
- Then a single retry is attempted after a brief delay
- And if the retry also fails, confirmation_sent = false is logged as a permanent failure
- And the opt-out status remains OPT-OUT regardless of the delivery failure (BR-025)

_AC-005 (SLA Breach Logged):_
- Given the confirmation SMS would be dispatched more than 60 seconds after the inbound message receipt
- When the dispatcher detects the SLA breach (sla_elapsed_seconds > 60)
- Then the confirmation is sent anyway
- And an SLA breach event is logged for compliance review

**Out of Scope for this story:** The specific confirmation SMS message text (to be provided by Legal/Compliance — stored in Azure Key Vault config, not hardcoded). Writing the confirmation outcome to the audit log (STORY-008).

**Notes:** [HIGH-RISK] due to RISK-002 — the confirmation SMS message text must be Legal/Compliance-approved before go-live. The text is a configuration value in Azure Key Vault (not hardcoded). A placeholder can be deployed and swapped without a code release. The SLA clock starts at inbound message receipt, not at database write time (BR-026). Confirmation failure does not reverse opt-out status.

---

### STORY-007: BizTalk REST Adapter Spike
**User Story:**
As an IT / Platform Engineer,
I want to confirm whether BizTalk can call the TCPA API REST endpoint natively or requires a custom adapter,
So that the integration approach is defined before development begins and the delivery timeline risk is understood.

**Source:** RISK-001, ARCH-RISK-001 | PRD §3
**Priority:** Must Have
**Story Points:** 3
**Flags:** [SPIKE: timebox 16h] [HIGH-RISK]

**Acceptance Criteria:**

_AC-001 (Spike Output — REST Feasibility):_
- Given the spike is complete
- When the findings are documented
- Then a written confirmation from the BizTalk team states whether BizTalk can natively call REST/JSON endpoints using the X-API-Key header pattern
- And if an adapter is required, an adapter delivery commitment with a date is obtained from the BizTalk team

_AC-002 (Fallback Path Documented):_
- Given BizTalk cannot call REST natively
- When the spike concludes
- Then a fallback approach is documented and approved: either (a) a BizTalk REST adapter scope and timeline, or (b) an architectural scope change to add a SOAP input channel to the TCPA API
- And the chosen fallback is escalated to the Architecture Lead for approval

_AC-003 (Integration Test Slot Reserved):_
- Given the spike is complete regardless of outcome
- When the project plan is updated
- Then an integration test slot with the BizTalk team is reserved no later than Q3 2026 in the project schedule

**Out of Scope for this story:** Building the adapter or SOAP endpoint — only the feasibility confirmation and planning are in scope.

**Notes:** [HIGH-RISK] due to RISK-001 — this is the single highest-risk delivery item. Timebox: 16 hours. Escalate immediately if BizTalk team cannot confirm within the timebox. This spike must complete in Sprint 1.

---

## EPIC-003: Re-Opt-In Management
- **Description:** Provides a privileged, authenticated admin API endpoint for Help Desk agents to manually reset a customer's opt-out status to OPT-IN, with full auditability.
- **Source Specs:** SPEC-007, SPEC-010
- **Priority:** Must Have
- **Personas:** PER-004 (Help Desk Agent), PER-003 (Compliance Officer), PER-005 (IT / Platform Engineer)

---

### STORY-008: Admin Identity Provider and RBAC Setup Spike
**User Story:**
As an IT / Platform Engineer,
I want to confirm the SCG Identity Provider details and provision the required RBAC roles for the Admin API,
So that the re-opt-in endpoint can be built and tested against the real authentication infrastructure.

**Source:** RISK-014, Architecture Open Question 3, SPEC-007 | PRD §3
**Priority:** Must Have
**Story Points:** 2
**Flags:** [SPIKE: timebox 8h] [BLOCKED-BY: none]

**Acceptance Criteria:**

_AC-001 (IdP Confirmed):_
- Given the spike is complete
- When findings are documented
- Then IT Security has confirmed: (a) the target identity provider (expected: Azure AD / Entra ID), (b) the OAuth 2.0 / OIDC token endpoint, and (c) the JWT claim structure for role-based access

_AC-002 (RBAC Roles Provisioned):_
- Given IT Security has confirmed the IdP
- When role provisioning is requested
- Then the `tcpa.helpdesk` and `tcpa.compliance_officer` role claims are created in the identity provider and at least one test user is assigned to each role for development/testing purposes

_AC-003 (Dev/Test Workaround Available):_
- Given production role provisioning may take longer than the timebox
- When the spike concludes without production roles available
- Then a documented workaround is in place (e.g., test Azure AD tenant with mock role claims) so that Admin API development is not blocked

**Out of Scope for this story:** Implementing the re-opt-in endpoint (STORY-009). User onboarding for Help Desk agents.

**Notes:** This spike must complete in Sprint 1. Admin API development should not wait on identity infrastructure — the dev/test workaround path (AC-003) ensures implementation can proceed in parallel.

---

### STORY-009: Re-Opt-In Status Lookup (Read-Only)
**User Story:**
As a Help Desk Agent,
I want to look up a customer's current opt-out status before performing any changes,
So that I can verify the customer is truly opted out before initiating a re-opt-in and avoid unnecessary changes.

**Source:** SPEC-007 (GET) | PRD §3
**Priority:** Must Have
**Story Points:** 2
**Flags:** [HIGH-RISK] [BLOCKED-BY: STORY-008]

**Acceptance Criteria:**

_AC-001 (Happy Path — Opted-Out Number Found):_
- Given an authenticated Help Desk agent calls GET /admin/v1/opt-out/status/{cell_number} for a number that has an OPT-OUT record
- When the request is processed
- Then the response returns opt_out_status = "OPT_OUT" with last_opt_out_timestamp and a masked cell number (last 4 digits only)

_AC-002 (Happy Path — Opted-In or No History):_
- Given an authenticated Help Desk agent calls GET /admin/v1/opt-out/status/{cell_number} for a number with no opt-out record
- When the request is processed
- Then the API returns 404 Not Found (no record implies OPT_IN by default; 404 communicates no history)

_AC-003 (Cell Number Masking):_
- Given any authenticated response
- When the cell_number is returned in the response body
- Then the cell number is displayed as "******XXXX" with only the last 4 digits visible

_AC-004 (Unauthorized Access):_
- Given a request with no Bearer token, an expired token, or a token lacking `tcpa.helpdesk` or `tcpa.compliance_officer` role claims
- When the Admin API processes the request
- Then 401 or 403 is returned
- And the unauthorized access attempt is logged as a security event

**Out of Scope for this story:** The re-opt-in update (STORY-010). Audit logging for the status lookup (read-only lookups do not produce audit log entries).

**Notes:** [HIGH-RISK] due to RISK-005 (Admin endpoint security) and RISK-011 (no UI — agents will use this API directly via tooling). The GET endpoint is read-only and returns masked PII only.

---

### STORY-010: Re-Opt-In Status Update (Privileged Write)
**User Story:**
As a Help Desk Agent,
I want to manually re-opt-in a customer who has previously opted out after they call in to request it,
So that the customer can once again receive text messages from SCG applications without requiring them to take further action.

**Source:** SPEC-007 (PUT), SPEC-010 | PRD §3
**Priority:** Must Have
**Story Points:** 5
**Flags:** [HIGH-RISK] [BLOCKED-BY: STORY-005, STORY-008, STORY-009]

**Acceptance Criteria:**

_AC-001 (Happy Path — Re-Opt-In Succeeds):_
- Given an authenticated Help Desk agent calls PUT /admin/v1/opt-out/re-opt-in with a valid cell_number, reason, and ticket_reference for a number that has a prior OPT-OUT record
- When the request is processed
- Then the cell number status is updated to OPT-IN in the TCPA database
- And the response returns success = true, previous_status = "OPT_OUT", new_status = "OPT_IN", updated_timestamp, and record_id

_AC-002 (Re-Opt-In is Global):_
- Given a cell number that opted out via the ARM application is re-opted-in
- When the re-opt-in takes effect
- Then the cell number is OPT-IN across ALL in-scope SCG applications (not just ARM)

_AC-003 (Unhappy Path — Cell Number Never Opted Out):_
- Given the cell number has no prior opt-out record in the system
- When a re-opt-in is attempted
- Then the API returns 409 Conflict (re-opt-in endpoint is only for reversing a prior opt-out)

_AC-004 (Idempotent — Already OPT-IN):_
- Given the cell number is already OPT-IN (was previously re-opted-in)
- When a re-opt-in is attempted
- Then the request is accepted (idempotent)
- And the action is logged
- And success = true is returned

_AC-005 (Missing Required Field — reason):_
- Given a PUT request is submitted without the reason field or with reason fewer than 20 characters
- When the Admin API validates the request
- Then the API returns 400 Bad Request
- And the re-opt-in is not performed

_AC-006 (Unauthorized Access):_
- Given a request with no Bearer token, an expired token, or a role not matching `tcpa.helpdesk` or `tcpa.compliance_officer`
- When the Admin API receives the request
- Then 401 or 403 is returned
- And the security event is logged

_AC-007 (No Confirmation SMS to Customer):_
- Given a successful re-opt-in is completed
- When the operation completes
- Then NO confirmation SMS is sent to the customer's cell number (Phase 1: Help Desk notifies the customer via phone)

**Out of Scope for this story:** A UI for the re-opt-in workflow (Phase 2 item per RISK-011). Confirmation SMS to customer on re-opt-in.

**Notes:** [HIGH-RISK] due to RISK-005 (Admin endpoint must be network-restricted to SCG internal network/VPN per architecture), RISK-011 (no UI — enforce minimum reason length of 20 chars and treat ticket_reference as required per risk mitigation). Agent user ID is captured from the authenticated JWT token, not a request field. Every call to this endpoint — success or failure — is logged as a security event.

---

## EPIC-004: Audit Logging
- **Description:** Records every compliance-relevant event (opt-out receipt, blocked outbound attempt, re-opt-in action) into an immutable, tamper-evident audit log with 5-year retention.
- **Source Specs:** SPEC-008, SPEC-009, SPEC-010
- **Priority:** Must Have
- **Personas:** PER-003 (Compliance Officer), PER-005 (IT / Platform Engineer)

---

### STORY-011: Opt-Out Event Audit Log Entry
**User Story:**
As a Compliance Officer,
I want every customer opt-out to be recorded in an immutable audit log entry at the time it occurs,
So that I can demonstrate to regulators that opt-outs were received, processed, and confirmed within required timeframes.

**Source:** SPEC-008 | PRD §3
**Priority:** Must Have
**Story Points:** 3
**Flags:** [HIGH-RISK] [BLOCKED-BY: STORY-005, STORY-006]

**Acceptance Criteria:**

_AC-001 (Happy Path — Audit Entry Written):_
- Given an opt-out keyword is detected and the OPT-OUT status is written successfully
- When the audit log writer processes the event
- Then an immutable audit log entry is created with event_type = OPT_OUT, event_timestamp, cell_number, originating_cool_text_account_id, originating_application_name, opt_out_keyword_received, message_body, system_response, and confirmation_sms_status
- And write_success = true is returned

_AC-002 (Already-OPT-OUT Event Also Logged):_
- Given a cell number that is already OPT-OUT sends another opt-out keyword
- When the audit log writer processes the event
- Then an audit log entry is written with system_response = "ALREADY_OPT_OUT_NO_ACTION"
- And write_success = true

_AC-003 (Confirmation Outcome Recorded):_
- Given the confirmation SMS dispatch outcome is known (SENT, FAILED, or NOT_SENT)
- When the audit log entry is written or updated
- Then confirmation_sms_sent and confirmation_sms_status are correctly populated in the audit record

_AC-004 (Audit Log Write Failure — Critical Alert):_
- Given the audit log database is unavailable when the audit write is attempted
- When the write fails
- Then a critical error is logged to the operational log
- And an alert is triggered to the operations team
- And the opt-out status is NOT rolled back (the opt-out remains valid)

_AC-005 (Immutability):_
- Given an audit log entry has been written
- When any UPDATE or DELETE is attempted on the audit record
- Then the database trigger rejects the operation with an error
- And the record remains unchanged

**Out of Scope for this story:** Blocked outbound SMS audit entries (STORY-012). Re-opt-in audit entries (STORY-013).

**Notes:** [HIGH-RISK] due to RISK-010 (audit log write failure must not silently drop events — dual-write fallback to Azure Service Bus dead-letter per risk mitigation). 5-year retention enforced at storage layer. Opt-out status write and audit log write should be in the same database transaction where technically feasible (NFS-008 fulfillment).

---

### STORY-012: Blocked Outbound SMS Audit Log Entry
**User Story:**
As a Compliance Officer,
I want every suppressed outbound SMS attempt to be recorded in the audit log,
So that I can demonstrate to regulators that opted-out customers were never sent messages after their opt-out.

**Source:** SPEC-009 | PRD §3
**Priority:** Must Have
**Story Points:** 2
**Flags:** [HIGH-RISK] [BLOCKED-BY: STORY-002, STORY-011]

**Acceptance Criteria:**

_AC-001 (Happy Path — Blocked Attempt Logged):_
- Given an outbound SMS request is suppressed because the destination cell number has OPT-OUT status
- When the audit log writer processes the suppression event
- Then an immutable audit log entry is created with event_type = BLOCKED_OUTBOUND, event_timestamp, cell_number, originating_cool_text_account_id, originating_application_name, message_body, and suppression_reason = "OPT_OUT"
- And write_success = true

_AC-002 (Each Attempt is Independent):_
- Given an application submits multiple outbound SMS requests to the same opted-out cell number
- When each request is suppressed
- Then each suppressed request generates an independent audit log entry (not a single grouped entry)

_AC-003 (Block Still Enforced if Audit Write Fails):_
- Given the audit log write fails for a suppressed outbound SMS
- When the error is handled
- Then the message block is still enforced (message never forwarded to Cool Text)
- And a critical alert is triggered to the operations team

**Out of Scope for this story:** Logging forwarded (non-suppressed) outbound messages — the reporting data for forwarded messages is derived from successful FORWARDED responses tracked separately.

**Notes:** [HIGH-RISK] because a gap in the blocked-outbound audit log is direct evidence of incomplete TCPA enforcement record-keeping.

---

### STORY-013: Re-Opt-In Event Audit Log Entry
**User Story:**
As a Compliance Officer,
I want every manual re-opt-in action to be recorded in the audit log with the agent's identity and reason,
So that I can audit who re-opted-in customers and why, and ensure no unauthorized re-opt-ins occurred.

**Source:** SPEC-010 | PRD §3
**Priority:** Must Have
**Story Points:** 2
**Flags:** [BLOCKED-BY: STORY-010]

**Acceptance Criteria:**

_AC-001 (Happy Path — Re-Opt-In Audit Entry Written):_
- Given a Help Desk agent successfully performs a re-opt-in via PUT /admin/v1/opt-out/re-opt-in
- When the audit log writer processes the re-opt-in event
- Then an immutable audit log entry is created with event_type = RE_OPT_IN, event_timestamp, cell_number, agent_user_id, reason, ticket_reference, and previous_status
- And write_success = true

_AC-002 (Idempotent Re-Opt-In Also Logged):_
- Given the cell number was already OPT-IN when the re-opt-in was requested (idempotent case)
- When the audit log writer processes the event
- Then an audit log entry is still written, documenting the agent action

_AC-003 (5-Year Retention):_
- Given a re-opt-in audit record is written
- When the retention policy is applied
- Then the record is retained for a minimum of 5 years from event_timestamp and cannot be deleted before that period

**Out of Scope for this story:** Audit entries for opt-out events (STORY-011) or blocked outbound (STORY-012).

**Notes:** The re-opt-in audit entry serves both compliance (trail of re-opt-in actions) and security (evidence of authorized vs. unauthorized access to the Admin API).

---

## EPIC-005: Compliance Reporting
- **Description:** Provides on-demand queryable data sets and an automated weekly scheduled compliance report for Compliance Officers to monitor opt-out enforcement.
- **Source Specs:** SPEC-011, SPEC-012, SPEC-013
- **Priority:** Must Have
- **Personas:** PER-003 (Compliance Officer), PER-005 (IT / Platform Engineer)

---

### STORY-014: On-Demand Report — SMS Forwarded to Opted-In Numbers
**User Story:**
As a Compliance Officer,
I want to query all outbound SMS messages that were successfully forwarded to opted-in customers for any date range,
So that I can verify the volume and accuracy of SMS delivery and identify any anomalies.

**Source:** SPEC-011 | PRD §3
**Priority:** Must Have
**Story Points:** 3
**Flags:** [BLOCKED-BY: STORY-002, STORY-011]

**Acceptance Criteria:**

_AC-001 (Happy Path — Query Returns Results):_
- Given an authenticated Compliance Officer calls GET /api/v1/reports/opted-in with a valid date_from and date_to
- When the request is processed
- Then the response contains a list of records each with status = "FORWARDED", cell_number, originating_application_name, message_timestamp, message_body, and cool_text_account_id
- And total_count reflects the total records in the result set

_AC-002 (Application Filter):_
- Given a GET /api/v1/reports/opted-in request includes application_filter = "GCMA"
- When the query executes
- Then only records with originating_application_name = "GCMA" are returned

_AC-003 (Unauthorized Access):_
- Given a request without `tcpa.compliance_officer` or `tcpa.reporting` role claim
- When the Reporting API receives the request
- Then 403 Forbidden is returned

_AC-004 (Invalid Date Range):_
- Given date_from is missing or date_to is earlier than date_from
- When the Reporting API validates the request
- Then 400 Bad Request is returned

**Out of Scope for this story:** Automated report scheduling (STORY-016). Opted-out query endpoint (STORY-015).

**Notes:** Query reads from the Reporting/Analytics DB (materialized projection from Audit Log — separate from the live operational DB). No direct SQL access for Compliance Officers; all access is through this API endpoint.

---

### STORY-015: On-Demand Report — SMS Blocked to Opted-Out Numbers
**User Story:**
As a Compliance Officer,
I want to query all outbound SMS messages that were suppressed because the destination number was opted out,
So that I can verify that opted-out customers were never sent messages after their opt-out and produce evidence for regulatory inquiries.

**Source:** SPEC-012 | PRD §3
**Priority:** Must Have
**Story Points:** 3
**Flags:** [BLOCKED-BY: STORY-002, STORY-012]

**Acceptance Criteria:**

_AC-001 (Happy Path — Query Returns Suppressed Records):_
- Given an authenticated Compliance Officer calls GET /api/v1/reports/opted-out with valid date range
- When the request is processed
- Then the response contains records each with status = "BLOCKED", cell_number, originating_application_name, attempt_timestamp, message_body, and suppression_reason = "OPT_OUT"

_AC-002 (Cell-Number Filter):_
- Given a GET /api/v1/reports/opted-out request includes cell_number_filter = "+12025551234"
- When the query executes
- Then only records for that cell number are returned

_AC-003 (Unauthorized Access):_
- Given a request without `tcpa.compliance_officer` or `tcpa.reporting` role claim
- When the Reporting API receives the request
- Then 403 Forbidden is returned

**Out of Scope for this story:** Forwarded message query (STORY-014). Automated report (STORY-016).

**Notes:** Data is drawn exclusively from the immutable audit log projection — not the live operational DB. This report is the primary evidence artifact in a regulatory inquiry.

---

### STORY-016: Automated Weekly Compliance Report — Generation and Email Dispatch
**User Story:**
As a Compliance Officer,
I want to receive an automated weekly email report every Monday morning summarizing opt-out enforcement activity for the prior week,
So that I can confirm on an ongoing basis that the TCPA compliance system is functioning correctly without manually running queries.

**Source:** SPEC-013 | PRD §3
**Priority:** Must Have
**Story Points:** 5
**Flags:** [HIGH-RISK] [BLOCKED-BY: STORY-014, STORY-015]

**Acceptance Criteria:**

_AC-001 (Happy Path — Report Generated and Sent):_
- Given Monday 06:00 UTC arrives and the scheduled job triggers
- When the report generator runs
- Then an HTML email is sent to the Compliance Officers distribution list with:
  (a) count of SMS forwarded to opted-in numbers (per-application breakdown),
  (b) count of SMS blocked to opted-out numbers (per-application breakdown),
  (c) total opt-out and re-opt-in counts for the week,
  (d) opt-out success rate KPI,
  (e) any compliance failures (messages delivered to opted-out numbers)
- And a CSV attachment containing the detailed SPEC-011 and SPEC-012 records for the reporting period is included

_AC-002 (No Activity — Zero-Count Report Still Sent):_
- Given no SMS activity occurred during the reporting week
- When the report generator runs
- Then a report with zero counts is still generated and emailed (absence of report ≠ absence of activity)

_AC-003 (Compliance Failure Highlighted):_
- Given any records exist where a message was delivered to a cell number that was opted-out at delivery time
- When the report is generated
- Then those records are prominently highlighted in the report body
- And an additional alert is triggered to Compliance Officers (not just included in the report)

_AC-004 (Job Failure — Alert Triggered):_
- Given the scheduled report job fails to complete (exception, timeout, email dispatch failure)
- When the failure is detected
- Then a critical alert is triggered to IT/Platform Engineering
- And the failure details are logged with full error context

_AC-005 (Manual Re-Run Capability):_
- Given a Monday report was missed due to a job failure
- When IT/Platform Engineering needs to re-run the report
- Then the report can be manually triggered for any historical week period via an admin invocation
- And re-running is idempotent (produces the same output for the same period)

_AC-006 (Report Period Accuracy):_
- Given Monday 06:00 UTC trigger fires
- When the report period is calculated
- Then the report covers the exact 7-day window from the prior Monday 00:00:00 UTC through Sunday 23:59:59 UTC

**Out of Scope for this story:** Self-service UI for Compliance Officers to generate reports on demand (Phase 2). Report archival beyond email delivery.

**Notes:** [HIGH-RISK] due to RISK-009 (scheduler single point of failure — implement Azure Monitor alert per risk mitigation). The email distribution list must be confirmed with IT/Compliance and stored in Azure Key Vault config before deployment. Implement data-staleness check: if the Reporting DB projection is more than 30 minutes stale when the report runs, include a staleness warning in the email body.

---

## EPIC-006: Application Registration & Configuration
- **Description:** Manages the registry of in-scope SCG applications and their Cool Text account mappings, determining which applications are subject to TCPA enforcement.
- **Source Specs:** SPEC-014
- **Priority:** Must Have
- **Personas:** PER-005 (IT / Platform Engineer)

---

### STORY-017: Application Registry — Initial Seed and Deployment-Time Configuration
**User Story:**
As an IT / Platform Engineer,
I want the five in-scope SCG applications to be pre-registered in the TCPA system at deployment time with the correct Cool Text account IDs and callback URLs,
So that compliance enforcement begins immediately for all in-scope applications when the system goes live.

**Source:** SPEC-014 | PRD §3
**Priority:** Must Have
**Story Points:** 2
**Flags:** [BLOCKED-BY: STORY-001]

**Acceptance Criteria:**

_AC-001 (Initial Registry Populated at Deployment):_
- Given the TCPA API is deployed to a new environment
- When the database initialization scripts run
- Then the Application Registry contains entries for: BizTalk (active = true), GCMA (active = true), KMI Active (active = true), ARM/Construction Portal (active = true), and CCB/My Account (active = false)
- And each entry includes the Cool Text account ID, application name, callback URL, and onboarded_date

_AC-002 (CCB Defaulted to Inactive):_
- Given CCB/My Account is in the registry with active = false
- When the TCPA API processes CCB Cool Text account SMS requests
- Then CCB messages are treated as unregistered (no enforcement) until the active flag is manually set to true
- And the active flag can be toggled without a code deployment (configuration change only)

_AC-003 (Startup Validation):_
- Given the TCPA API service starts up
- When the Application Registry is loaded
- Then the service validates that all registered entries have: non-empty Cool Text account ID, non-empty application name, HTTPS-only callback URL, and valid active flag
- And if any entry fails validation, a startup error is logged and the service fails to start

_AC-004 (Cache Primed at Startup):_
- Given the Application Registry is loaded successfully
- When the in-memory cache is initialized
- Then all registry entries are loaded into the cache with a 5-minute TTL
- And subsequent lookups within the TTL do not hit the database

**Out of Scope for this story:** Runtime admin API for registry changes (Phase 1 is configuration-deploy only). Adding or removing applications at runtime.

**Notes:** CCB active = false is a hard default — enabling it requires a deliberate IT configuration change as part of the formal CCB TCPA Activation Gate process (RISK-003). The activation gate checklist (end-to-end integration test pass, Legal sign-off) must be completed before setting CCB to active = true in production.

---

### STORY-018: CCB TCPA Activation Gate Process
**User Story:**
As an IT / Platform Engineer,
I want a documented and enforced activation gate for enabling CCB/My Account in the TCPA Application Registry,
So that CCB SMS messages are not processed without TCPA protection and the enablement is only performed after end-to-end integration testing is confirmed.

**Source:** RISK-003, SPEC-014 (BR-063) | PRD §3
**Priority:** Must Have
**Story Points:** 1
**Flags:** [HIGH-RISK]

**Acceptance Criteria:**

_AC-001 (Gate Checklist Exists):_
- Given the CCB TCPA Activation Gate is defined
- When the checklist is reviewed
- Then it includes as mandatory items: (a) end-to-end integration test pass in staging, (b) Cool Text account ID confirmed in production registry, (c) production smoke test, (d) Legal/Compliance sign-off
- And the checklist is stored as a configuration-adjacent operations document

_AC-002 (Active Flag Change is Auditable):_
- Given the CCB active flag is changed from false to true in any environment
- When the configuration deployment runs
- Then the change is recorded in the deployment audit trail (version control or deployment log) with the approver identity and timestamp

_AC-003 (Active Flag is NOT Changed Without Gate Sign-Off):_
- Given the CCB activation gate checklist has not been completed
- When CCB active flag remains false in production
- Then CCB SMS traffic continues to pass through without enforcement (no regression for pre-gate state)

**Out of Scope for this story:** Actual CCB integration testing (that is a per-application integration test effort, not a story in this system). Setting the active flag itself.

**Notes:** [HIGH-RISK] due to RISK-003 — an opted-out customer receiving CCB SMS because the activation gate was skipped is a direct TCPA compliance violation. This story delivers the process and documentation artifact, not code.

---

## EPIC-007: Observability & Non-Functional
- **Description:** Delivers structured operational logging, health check endpoint, PII-safe log masking, debug logging capability, and system availability controls.
- **Source Specs:** SPEC-015, NFS-001 through NFS-010
- **Priority:** Must Have
- **Personas:** PER-005 (IT / Platform Engineer)

---

### STORY-019: Structured Operational Logging with PII Masking
**User Story:**
As an IT / Platform Engineer,
I want all TCPA API events to be emitted as structured JSON logs with cell numbers masked,
So that I can query, alert on, and diagnose the system in production without exposing customer PII in the log aggregation platform.

**Source:** SPEC-015, NFS-010 | PRD §3
**Priority:** Must Have
**Story Points:** 3
**Flags:** [BLOCKED-BY: none — cross-cutting concern, implement from the start]

**Acceptance Criteria:**

_AC-001 (Structured JSON Format):_
- Given any significant operational event occurs (opt-out received, message forwarded, message blocked, confirmation SMS sent/failed, re-opt-in processed, report generated/failed, authentication event)
- When the event is logged
- Then the log entry is emitted in structured JSON format with fields: timestamp (ISO 8601 UTC), log_level, event_type, correlation_id (UUID), and relevant entity IDs

_AC-002 (Cell Number Masking):_
- Given any log event contains a cell phone number reference
- When the event is emitted at any log level
- Then the cell number is masked as "******XXXX" (only last 4 digits visible)
- And no unmasked cell number appears in any log output

_AC-003 (No Message Body in Production Logs):_
- Given production log level is active
- When an outbound or inbound SMS event is logged
- Then the message body content is NOT included in the log entry
- And message body is only present in debug log entries

_AC-004 (No Credentials or Tokens in Logs):_
- Given any event is logged
- When the log entry is emitted
- Then no API keys, Bearer tokens, HMAC secrets, or database connection strings appear in any log output

_AC-005 (Production Logs Accessible in 5 Minutes):_
- Given a production log event is emitted
- When the event is ingested to the SCG log aggregation platform (Azure Log Analytics)
- Then the event is queryable within 5 minutes of emission

**Out of Scope for this story:** Debug logging toggle (STORY-020). Health check endpoint (STORY-021).

**Notes:** Correlation ID middleware must be implemented early — every inbound request gets a UUID correlation ID that is propagated through all log events for that request. Cell number masking must be enforced in a shared logging utility/middleware layer so no component can accidentally log an unmasked number.

---

### STORY-020: Debug Logging Toggle
**User Story:**
As an IT / Platform Engineer,
I want to enable and disable detailed debug logging without restarting the TCPA API service,
So that I can diagnose incidents in production without causing a service interruption or requiring a deployment.

**Source:** SPEC-015, NFS-010 | PRD §3
**Priority:** Must Have
**Story Points:** 2
**Flags:** [BLOCKED-BY: STORY-019]

**Acceptance Criteria:**

_AC-001 (Debug Logging Off by Default):_
- Given the TCPA API is deployed with default configuration
- When the service starts
- Then debug logging is disabled and no debug log entries are emitted in production

_AC-002 (Debug Logging Toggled Without Restart):_
- Given an IT engineer changes the debug logging configuration flag in Azure App Configuration
- When the configuration change propagates (within the polling interval)
- Then debug log entries begin appearing in the log output without a service restart

_AC-003 (Debug Logs Include Additional Detail):_
- Given debug logging is enabled
- When an inbound webhook is processed
- Then debug log entries include: full request/response payloads, database query timings, retry attempt details, and internal state transitions (subject to PII masking rules — cell numbers still masked, message body may be logged in debug mode with explicit documentation that debug mode should not remain enabled in production)

_AC-004 (Debug Logging Disabled Again Without Restart):_
- Given debug logging is currently enabled in production
- When an IT engineer sets the debug flag back to off in App Configuration
- Then debug log entries cease without a service restart

**Out of Scope for this story:** Debug log retention policy differences from production logs.

**Notes:** Document in the operations guide that debug logging must not remain enabled in production for extended periods — debug logs may contain message body content (PII-adjacent). Include an Azure Monitor alert if the debug logging flag remains enabled for more than 2 hours in production.

---

### STORY-021: Health Check Endpoint
**User Story:**
As an IT / Platform Engineer,
I want a health check endpoint that reports the status of all critical TCPA API dependencies,
So that the load balancer, monitoring system, and on-call team can immediately detect when the system or its dependencies are degraded.

**Source:** SPEC-015, NFS-009 | PRD §3
**Priority:** Must Have
**Story Points:** 2
**Flags:** [BLOCKED-BY: none]

**Acceptance Criteria:**

_AC-001 (Happy Path — All Healthy):_
- Given all dependencies are available (opt-out status database, audit log database, Cool Text connectivity)
- When GET /health is called
- Then the response is 200 OK with status = "healthy" and each check reporting "ok" with a timestamp

_AC-002 (Degraded — Critical Dependency Down):_
- Given the opt-out status database is unreachable
- When GET /health is called
- Then the response is 503 Service Unavailable with the database check showing "degraded"

_AC-003 (No Authentication Required):_
- Given a GET /health request arrives with no authentication headers
- When the health check processes the request
- Then the response is returned without authentication (health check must be accessible to the load balancer/monitoring probe)

_AC-004 (External Monitor Interval):_
- Given an Azure Monitor external health probe is configured
- When the probe polls GET /health at ≤ 1-minute intervals
- Then the probe can detect a 503 response and trigger an availability alert within 2 minutes of the outage beginning

**Out of Scope for this story:** Detailed diagnostic information beyond dependency status (to avoid information disclosure).

**Notes:** Health check response must not expose internal IP addresses, connection strings, or version details. The /health endpoint feeds into the 99.9% uptime SLA tracking.

---

### STORY-022: PII Encryption at Rest and TLS Enforcement
**User Story:**
As an IT / Platform Engineer,
I want cell phone numbers to be encrypted at rest in the database and all API communication to use TLS 1.2 or higher,
So that customer PII is protected from unauthorized access in storage and in transit.

**Source:** NFS-007 | PRD §3
**Priority:** Must Have
**Story Points:** 3
**Flags:** [HIGH-RISK] [BLOCKED-BY: STORY-001]

**Acceptance Criteria:**

_AC-001 (Cell Number Encryption at Rest):_
- Given the CellNumberOptOutStatus table and AuditLogEntry table are created
- When cell_number column values are stored
- Then the values are encrypted using Azure SQL Always Encrypted with AES-256 deterministic encryption
- And a schema review confirms the column encryption is applied

_AC-002 (Indexed Lookup on Encrypted Column):_
- Given the cell_number column uses deterministic encryption
- When a compliance gate lookup queries the CellNumberOptOutStatus table by cell_number
- Then the lookup returns the correct record (deterministic encryption supports indexed equality lookup)

_AC-003 (TLS 1.0 and 1.1 Disabled):_
- Given the Azure Application Gateway TLS policy is configured
- When a TLS configuration scan is executed (e.g., testssl.sh)
- Then TLS 1.0 and 1.1 connections are refused
- And TLS 1.2 and 1.3 connections succeed

_AC-004 (Cell Number Masking in Logs Verified):_
- Given the logging middleware is active
- When a log event containing a cell number reference is emitted
- Then the emitted log entry contains only "******XXXX" (last 4 digits) — no unmasked number appears

**Out of Scope for this story:** Azure Blob Storage WORM immutability policy (STORY-023). Database backup configuration.

**Notes:** [HIGH-RISK] because failure to encrypt cell numbers at rest is both a regulatory violation and a PII data breach risk. Always Encrypted with deterministic encryption allows indexed equality lookups but does not support range queries or LIKE on the encrypted column — this is an accepted constraint per ADR-003 and RISK-012. Compliance Officers query by cell number through the API (not direct SQL), so this constraint does not impact their workflow.

---

### STORY-023: Audit Log Immutability and 5-Year Retention Policy
**User Story:**
As a Compliance Officer,
I want audit log records to be tamper-evident and retained for at least 5 years from each event,
So that I can produce an unaltered audit trail for regulatory discovery requests covering the full required retention period.

**Source:** NFS-004, NFS-008, ADR-004 | PRD §3
**Priority:** Must Have
**Story Points:** 3
**Flags:** [BLOCKED-BY: STORY-011]

**Acceptance Criteria:**

_AC-001 (Database-Level Immutability):_
- Given the audit log table is created with an immutability trigger
- When any UPDATE or DELETE is attempted on an audit log record
- Then the database trigger rejects the operation with an error
- And the original record remains unchanged

_AC-002 (Application-Level Write-Only Repository):_
- Given the audit log repository is implemented
- When the code is reviewed
- Then no Update or Delete methods are exposed for audit log entities in the repository interface

_AC-003 (5-Year Retention — Active Records):_
- Given audit log records are written
- When retention policy validation is run
- Then records within the 5-year window cannot be deleted
- And records that have reached 5 years + 1 day are eligible for purge

_AC-004 (Tiering to Azure Blob WORM After 90 Days):_
- Given audit log records are older than 90 days
- When the tiering lifecycle policy runs
- Then records are moved to Azure Blob Storage WORM immutable storage
- And the records remain queryable for the full 5-year period (via the reporting projection or blob query path)

_AC-005 (Audit Completeness Check):_
- Given the weekly compliance report runs
- When the completeness check executes
- Then the number of opt-out events processed in the reporting period is compared against the number of corresponding audit log entries
- And any mismatch is flagged prominently in the report

**Out of Scope for this story:** The cold-storage query runbook for audit records > 90 days (operations documentation).

**Notes:** The operations guide must document the procedure for querying archived (> 90 day) audit records from Azure Blob Storage — this is an operations artifact, not a story deliverable, but it must be complete before go-live per RISK-013 mitigation.

---

## Dependency Map

```
STORY-001 (App Registry)
  ├── STORY-002 (Outbound SMS Gate) ──► STORY-012 (Blocked Outbound Audit Log)
  │                                              └── STORY-015 (Opted-Out Report)
  │                                                        └── STORY-016 (Weekly Report)
  ├── STORY-003 (Inbound Webhook Routing)
  │     └── STORY-004 (Keyword Detection)
  │           └── STORY-005 (Opt-Out Status Write)
  │                 ├── STORY-006 (Confirmation SMS)
  │                 ├── STORY-011 (Opt-Out Audit Log) ──► STORY-014 (Opted-In Report)
  │                 │                                             └── STORY-016 (Weekly Report)
  │                 └── STORY-010 (Re-Opt-In Write)
  │                       └── STORY-013 (Re-Opt-In Audit Log)
  └── STORY-017 (Registry Seed)
        └── STORY-018 (CCB Activation Gate — process only)

STORY-008 (IdP/RBAC Spike)
  └── STORY-009 (Status Lookup)
        └── STORY-010 (Re-Opt-In Write)

STORY-003-SPIKE (Cool Text Webhook Auth) → informs STORY-003 implementation
STORY-007 (BizTalk Spike) → informs BizTalk integration testing

STORY-019 (Structured Logging)
  └── STORY-020 (Debug Toggle)

STORY-021 (Health Check) — independent
STORY-022 (PII Encryption + TLS) ──► depends on STORY-001 (schema design)
STORY-023 (Audit Immutability + Retention) ──► depends on STORY-011 (audit table exists)
```

---

## Traceability Matrix

| Story       | Spec(s)                  | PRD Ref | Component (Arch)                     | Priority   |
|-------------|--------------------------|---------|--------------------------------------|------------|
| STORY-001   | SPEC-014                 | PRD §3  | Application Registry / API Gateway   | Must Have  |
| STORY-002   | SPEC-001, SPEC-006       | PRD §3  | Compliance Engine / Outbound Proxy   | Must Have  |
| STORY-003   | SPEC-002                 | PRD §3  | Compliance Engine / Inbound Routing  | Must Have  |
| STORY-003-SPIKE | ARCH-RISK-004, ADR-007| PRD §3 | Architecture / Integration          | Must Have  |
| STORY-004   | SPEC-003                 | PRD §3  | Compliance Engine / Keyword Detector | Must Have  |
| STORY-005   | SPEC-004                 | PRD §3  | Compliance Engine / Opt-Out Pipeline | Must Have  |
| STORY-006   | SPEC-005                 | PRD §3  | Compliance Engine / Confirmation SMS | Must Have  |
| STORY-007   | RISK-001, ARCH-RISK-001  | PRD §3  | Architecture / BizTalk Integration   | Must Have  |
| STORY-008   | RISK-014, SPEC-007       | PRD §3  | Admin API / Identity                 | Must Have  |
| STORY-009   | SPEC-007 (GET)           | PRD §3  | Admin API / Re-Opt-In Service        | Must Have  |
| STORY-010   | SPEC-007 (PUT), SPEC-010 | PRD §3  | Admin API / Re-Opt-In Service        | Must Have  |
| STORY-011   | SPEC-008                 | PRD §3  | Compliance Engine / Audit Log Store  | Must Have  |
| STORY-012   | SPEC-009                 | PRD §3  | Compliance Engine / Audit Log Store  | Must Have  |
| STORY-013   | SPEC-010                 | PRD §3  | Admin API / Audit Log Store          | Must Have  |
| STORY-014   | SPEC-011                 | PRD §3  | Reporting Service                    | Must Have  |
| STORY-015   | SPEC-012                 | PRD §3  | Reporting Service                    | Must Have  |
| STORY-016   | SPEC-013                 | PRD §3  | Reporting Service / Scheduler        | Must Have  |
| STORY-017   | SPEC-014                 | PRD §3  | Application Registry                 | Must Have  |
| STORY-018   | SPEC-014 (BR-063), RISK-003 | PRD §3 | Application Registry / Process      | Must Have  |
| STORY-019   | SPEC-015, NFS-010        | PRD §3  | Observability Component              | Must Have  |
| STORY-020   | SPEC-015, NFS-010        | PRD §3  | Observability Component              | Must Have  |
| STORY-021   | SPEC-015, NFS-009        | PRD §3  | Observability Component              | Must Have  |
| STORY-022   | NFS-007                  | PRD §3  | Data Layer / API Gateway             | Must Have  |
| STORY-023   | NFS-004, NFS-008, ADR-004| PRD §3  | Audit Log Store                      | Must Have  |

### Spec Coverage Check
| Spec      | Story Coverage                                        |
|-----------|-------------------------------------------------------|
| SPEC-001  | STORY-002                                             |
| SPEC-002  | STORY-003                                             |
| SPEC-003  | STORY-004                                             |
| SPEC-004  | STORY-005                                             |
| SPEC-005  | STORY-006                                             |
| SPEC-006  | STORY-002 (enforcement behavior within outbound gate) |
| SPEC-007  | STORY-009 (GET), STORY-010 (PUT)                      |
| SPEC-008  | STORY-011                                             |
| SPEC-009  | STORY-012                                             |
| SPEC-010  | STORY-013 (via STORY-010)                             |
| SPEC-011  | STORY-014                                             |
| SPEC-012  | STORY-015                                             |
| SPEC-013  | STORY-016                                             |
| SPEC-014  | STORY-001, STORY-017, STORY-018                       |
| SPEC-015  | STORY-019, STORY-020, STORY-021                       |
| NFS-001   | STORY-006 (60-second SLA enforcement)                 |
| NFS-002   | STORY-002 (immediate enforcement), STORY-005          |
| NFS-004   | STORY-023                                             |
| NFS-005   | STORY-002 (fail-closed AC-004)                        |
| NFS-007   | STORY-022                                             |
| NFS-008   | STORY-011, STORY-023                                  |
| NFS-009   | STORY-021                                             |
| NFS-010   | STORY-019, STORY-020                                  |
