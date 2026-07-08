<!-- SDLC Pipeline Artifact
     Stage: 03-spec-decomposer
     Source PRD: inputs/prd.md
     PRD Sections: §1 Overview, §2 Personas, §3 Functional Requirements, §4 Non-Functional Requirements, §5 Constraints, §6 Out of Scope, §7 Success Metrics, §8 Assumptions, §9 Dependencies, §10 Product Decisions Required
     Generated: 2026-06-26
     Status: APPROVED — human approved proceeding despite open clarifications (2026-07-07)
-->

# Functional Specifications — TCPA Regulatory Compliance for Text Messages

## Bounded Contexts

- **[BC-1] SMS Proxy & Routing** — Intercepts all outbound SMS from in-scope applications, routes inbound SMS replies back to the originating application, and enforces the compliance check gate before any message reaches Cool Text/Twilio.
- **[BC-2] Opt-Out Management** — Detects opt-out keywords in inbound messages, writes opt-out status, sends the standardized opt-out confirmation SMS, and enforces the block on future outbound messages to opted-out numbers.
- **[BC-3] Re-Opt-In Management** — Provides an authenticated privileged API endpoint that allows authorized Help Desk agents to manually reset a cell number's status from OPT-OUT back to OPT-IN.
- **[BC-4] Audit Logging** — Records every compliance-relevant event (opt-out events and blocked outbound attempts) into a tamper-evident, long-retention audit log.
- **[BC-5] Compliance Reporting** — Produces on-demand queryable data and automated weekly scheduled compliance reports for Compliance Officers.
- **[BC-6] Application Registration & Configuration** — Stores and manages the mapping between Cool Text account identifiers and the in-scope SCG applications; controls which applications are subject to TCPA enforcement.
- **[BC-7] Observability** — Produces structured operational and debug logs for IT/Platform Engineering use.

---

## BC-1: SMS Proxy & Routing

### SPEC-001: Outbound SMS Proxy — Receive and Gate
- **Source Requirements:** REQ-001, REQ-008, REQ-018
- **PRD Reference:** PRD §3
- **Priority:** Must Have
- **Dependencies:** SPEC-020 (Application Registration must exist so the system can resolve the Cool Text account)
- **Flags:** [COMPLEX: The proxy must be the authoritative interception point for all in-scope applications; any integration gap results in TCPA non-compliance. BizTalk integration protocol requires verification — see ASSUMED note below.]

**Behavior:**
The TCPA API exposes an inbound REST/JSON endpoint that accepts outbound SMS requests from in-scope applications. Upon receiving a request, the system:
1. Resolves the originating application from the Cool Text account identifier embedded in the request.
2. Checks whether the destination cell number has OPT-OUT status in the TCPA database.
3. If the number is OPT-IN (or has no status record, which defaults to OPT-IN per ASM-002): forwards the message to Cool Text/Twilio unchanged.
4. If the number is OPT-OUT: suppresses the message (does not forward to Cool Text/Twilio), logs the blocked attempt per SPEC-012, and returns an appropriate response to the calling application indicating suppression.
5. If the Cool Text account identifier in the request does not match any registered application: passes the request through to Cool Text/Twilio without compliance enforcement and without logging a blocked attempt (per REQ-018 — unregistered applications are unaffected).

[ASSUMED: All in-scope applications (BizTalk, GCMA, KMI, ARM, CCB/My Account) communicate with the TCPA API via REST/JSON. BizTalk's native integration capability requires verification by the integration team — if BizTalk cannot call REST natively, an adapter layer will be needed. This is flagged as an integration risk but is not modeled as a separate protocol variant in this spec.]

**Inputs:**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| cool_text_account_id | String | Must be a registered Cool Text account identifier; non-empty | Yes |
| destination_cell_number | String | Valid E.164 format (e.g., +12025551234); non-empty | Yes |
| message_body | String | Non-empty; max length per Twilio/Cool Text platform limits (typically 1600 chars for concatenated SMS) | Yes |
| originating_application_reference | String | Optional caller reference for logging; free text | No |

**Outputs:**
| Field | Type | Format/Constraints |
|-------|------|--------------------|
| status | Enum | FORWARDED \| SUPPRESSED \| UNREGISTERED_ACCOUNT (pass-through) |
| message_id | String | Cool Text message ID returned when FORWARDED; null otherwise |
| suppression_reason | String | "OPT_OUT" when SUPPRESSED; null otherwise |

**Business Rules:**
- BR-001: All cell numbers default to OPT-IN status if no record exists in the TCPA database (ASM-002).
- BR-002: The compliance gate decision (OPT-IN / OPT-OUT) is made at request time against the current database state; there is no caching of opt-out status that could cause stale enforcement.
- BR-003: The TCPA API does not modify the opt-out status at the application level; it manages only its own centralized database (CON-004).
- BR-004: If the Cool Text account identifier is not registered in the TCPA system, the message is forwarded without enforcement and the request is not logged as a compliance event (REQ-018).
- BR-005: Vendor SMS (ACI SpeedPay, Google Notifications), IVR Dialer SMS, MFA SMS, and emergency SMS are outside the scope of this proxy (CON-006, CON-007, CON-008, OOS-005).

**Edge Cases:**
| Scenario | Expected System Behavior |
|----------|--------------------------|
| Cell number not in TCPA database | Treat as OPT-IN (ASM-002); forward message |
| Cool Text account ID present but not registered in TCPA config | Forward without compliance check; do not log as compliance event |
| Message body is empty | Return 400 Bad Request; do not forward |
| destination_cell_number is not valid E.164 | Return 400 Bad Request; do not forward |
| TCPA API database is unavailable at time of request | Fail-closed: do not forward the message; return 503 Service Unavailable to calling application (see SPEC-021) |
| Application sends duplicate message to same opted-out number | Suppress each attempt independently; each generates a separate blocked-attempt audit log entry |

**Error Conditions:**
| Error | Trigger | System Response |
|-------|---------|-----------------|
| Missing required field | Request body missing cool_text_account_id, destination_cell_number, or message_body | 400 Bad Request with field-level error detail |
| Database unavailable | TCPA opt-out database unreachable | 503 Service Unavailable; message not forwarded (fail-closed) |
| Cool Text / Twilio unreachable | Downstream platform unavailable after opt-in check | 502 Bad Gateway; log failure in operational log |
| Invalid cell number format | destination_cell_number does not match E.164 | 400 Bad Request |

---

### SPEC-002: Inbound SMS Routing — Forward to Originating Application
- **Source Requirements:** REQ-001, REQ-002
- **PRD Reference:** PRD §3
- **Priority:** Must Have
- **Dependencies:** SPEC-020 (Application Registration must exist to resolve inbound account routing), SPEC-003 (Opt-Out Keyword Detection is processed before non-keyword messages are routed)

**Behavior:**
The TCPA API receives inbound SMS replies from customers via the Cool Text platform webhook. Upon receipt, the system:
1. Inspects the message body for opt-out keywords (handled by SPEC-003).
2. If no opt-out keyword is detected: identifies the destination application using the Cool Text account identifier included in the inbound webhook payload, then forwards the message body unchanged to the registered callback endpoint for that application.
3. If the Cool Text account ID in the inbound payload does not map to a registered application: logs a warning in the operational log and discards the message (no delivery target available).
4. Does not modify, filter, or enrich the message body before forwarding to the application.

[ASSUMED: The Cool Text platform delivers inbound messages to the TCPA API via a webhook push (HTTP POST). The inbound webhook payload contains the Cool Text account identifier, which maps to the originating application in the TCPA API configuration. The TCPA API stores a callback URL per registered application to forward inbound non-opt-out replies. This is consistent with the routing model described in the clarification defaults (CQ-010 context: "inbound messages carry a Cool Text account identifier which maps to the originating application in the TCPA API config").]

**Inputs (from Cool Text webhook):**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| cool_text_account_id | String | Account ID from Cool Text webhook payload | Yes |
| sender_cell_number | String | E.164 cell number of the customer who sent the reply | Yes |
| message_body | String | Raw message body; forwarded unchanged | Yes |
| cool_text_message_id | String | Platform-assigned message identifier for logging | Yes |

**Outputs (to originating application callback):**
| Field | Type | Format/Constraints |
|-------|------|--------------------|
| sender_cell_number | String | Forwarded unchanged from inbound |
| message_body | String | Forwarded unchanged from inbound |
| cool_text_account_id | String | Forwarded for application context |
| received_timestamp | String | ISO 8601 UTC timestamp of TCPA API receipt |

**Business Rules:**
- BR-006: Inbound messages that are opt-out keywords are processed by SPEC-003 and are NOT forwarded to the originating application as general replies.
- BR-007: Non-opt-out inbound replies are forwarded to the single application registered for the Cool Text account ID in the inbound payload.
- BR-008: The message body is forwarded without modification.
- BR-009: If no application callback is registered for the inbound Cool Text account ID, the message is dropped and a warning is logged.

**Edge Cases:**
| Scenario | Expected System Behavior |
|----------|--------------------------|
| Inbound message contains opt-out keyword | Trigger SPEC-003; do not forward to application |
| Cool Text account ID not in TCPA config | Log warning; discard message; do not forward |
| Originating application callback URL unreachable | Log delivery failure in operational log; do not retry indefinitely (retry up to 3 times with exponential backoff, then log permanent failure) |
| Message body is empty | Forward empty body unchanged; log warning |

**Error Conditions:**
| Error | Trigger | System Response |
|-------|---------|-----------------|
| Webhook payload missing required fields | cool_text_account_id or sender_cell_number absent | Return 400; log malformed webhook event |
| Application callback unreachable after retries | HTTP error or timeout on all retry attempts | Log permanent delivery failure; no further action |

---

## BC-2: Opt-Out Management

### SPEC-003: Opt-Out Keyword Detection
- **Source Requirements:** REQ-003
- **PRD Reference:** PRD §3
- **Priority:** Must Have
- **Dependencies:** SPEC-002 (Inbound SMS is received before keyword detection runs)

**Behavior:**
When an inbound SMS message is received from a customer via Cool Text, the system inspects the message body for the presence of any of the 7 TCPA-mandated opt-out keywords. Detection uses exact word-boundary matching: the keyword must appear as a complete word (case-insensitive) in the message body. Substring matches that are part of a larger word do not trigger opt-out (e.g., "CANCEL" matches, "CANCELLATION" does not; "STOP" matches, "NONSTOP" does not).

[ASSUMED: Exact word-boundary match (case-insensitive) per the clarification default provided for CQ-001. The 7 keywords are: STOP, QUIT, END, REVOKE, OPT-OUT, CANCEL, UNSUBSCRIBE. "OPT-OUT" (hyphenated) is treated as a single token. Matching is applied to the full message body; if any of the 7 keywords appears as a complete word anywhere in the body, the message is classified as an opt-out request.]

**Inputs:**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| message_body | String | Raw inbound SMS body; may be any length | Yes |
| sender_cell_number | String | E.164 format | Yes |
| cool_text_account_id | String | From inbound webhook | Yes |
| received_timestamp | String | ISO 8601 UTC | Yes |

**Outputs:**
| Field | Type | Format/Constraints |
|-------|------|--------------------|
| is_opt_out_keyword | Boolean | true if any of the 7 keywords matched as a complete word |
| matched_keyword | String | The specific keyword matched (e.g., "STOP"); null if no match |

**Business Rules:**
- BR-010: The 7 opt-out keywords are: STOP, QUIT, END, REVOKE, OPT-OUT, CANCEL, UNSUBSCRIBE.
- BR-011: Matching is case-insensitive ("stop", "STOP", "Stop" all match).
- BR-012: Matching is word-boundary exact: a keyword must not be embedded within a longer word (e.g., "STOPPED" does not match "STOP"; "UNSUBSCRIBED" does not match "UNSUBSCRIBE").
- BR-013: "OPT-OUT" is matched as a hyphenated token; "OPT" alone without "-OUT" does not trigger opt-out.
- BR-014: If a message matches any of the 7 keywords, it is classified as an opt-out request regardless of other content in the message body.
- BR-015: If a cell number is already OPT-OUT and sends another opt-out keyword, the TCPA API logs the event but does not re-send the confirmation SMS (idempotent opt-out per CQ-021 default: log only, no re-confirmation).

**Edge Cases:**
| Scenario | Expected System Behavior |
|----------|--------------------------|
| Message is exactly "STOP" | Match — opt-out |
| Message is "Please stop sending me texts" | Match — "stop" is a complete word with word boundaries |
| Message is "NONSTOP service" | No match — "NONSTOP" embeds STOP without word boundary |
| Message is "CANCELLATION" | No match — not a word-boundary match on "CANCEL" |
| Message is "CANCEL everything" | Match — "CANCEL" appears as a complete word |
| Message contains "OPT-OUT" | Match |
| Message contains "OPT" but not "OPT-OUT" | No match |
| Cell number already OPT-OUT sends "STOP" again | Log event; take no further action (no re-confirmation SMS) |
| Message is empty | No match; forward as non-opt-out inbound reply |

**Error Conditions:**
| Error | Trigger | System Response |
|-------|---------|-----------------|
| Null or missing message_body | Webhook payload missing body | Log warning; treat as non-opt-out |

---

### SPEC-004: Opt-Out Status Write
- **Source Requirements:** REQ-004
- **PRD Reference:** PRD §3
- **Priority:** Must Have
- **Dependencies:** SPEC-003 (Keyword detection must confirm opt-out before status write)

**Behavior:**
Upon confirmation from SPEC-003 that an inbound message contains an opt-out keyword, the system immediately writes an OPT-OUT status record for the sender's cell phone number in the TCPA database. The status write sets the cell number to OPT-OUT globally — the opt-out is not scoped to a single application; it applies across all in-scope SCG applications.

**Inputs:**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| cell_number | String | E.164 format | Yes |
| opt_out_timestamp | String | ISO 8601 UTC; the timestamp of inbound message receipt | Yes |
| opt_out_keyword | String | The specific keyword that triggered opt-out | Yes |
| cool_text_account_id | String | Account ID that received the opt-out | Yes |

**Outputs:**
| Field | Type | Format/Constraints |
|-------|------|--------------------|
| status_write_success | Boolean | true if the record was written successfully |
| previous_status | Enum | OPT_IN \| OPT_OUT (the status before this write) |
| record_id | String | Unique identifier of the opt-out record |

**Business Rules:**
- BR-016: An OPT-OUT status is global across all in-scope SCG applications; it is not scoped per application.
- BR-017: The opt-out status write is atomic; if the write fails, the opt-out confirmation SMS must not be sent (the confirmation would be misleading if status was not actually persisted).
- BR-018: The status write timestamp is the timestamp of the inbound message receipt, not the time of the database write.
- BR-019: If the cell number is already OPT-OUT, the write is a no-op (idempotent); previous_status returns OPT_OUT and status_write_success returns true.
- BR-020: The TCPA API does not propagate opt-out status changes back to individual applications (OOS-010).

**Edge Cases:**
| Scenario | Expected System Behavior |
|----------|--------------------------|
| Cell number already OPT-OUT | Idempotent — no duplicate record; return true with previous_status = OPT_OUT |
| Database write fails | status_write_success = false; do not send confirmation SMS; log error; trigger alert |
| Cell number has no prior record | Create new OPT-OUT record; previous_status = OPT_IN (default per ASM-002) |

**Error Conditions:**
| Error | Trigger | System Response |
|-------|---------|-----------------|
| Database unavailable | Cannot write opt-out record | Log critical error; do not send confirmation SMS; alert operations team |
| Constraint violation | Duplicate key or schema error | Log error; investigate; do not send confirmation SMS |

---

### SPEC-005: Opt-Out Confirmation SMS
- **Source Requirements:** REQ-005, REQ-007
- **PRD Reference:** PRD §3
- **Priority:** Must Have
- **Dependencies:** SPEC-004 (Opt-out status must be successfully written before confirmation is sent)

**Behavior:**
Within 60 seconds of a successful opt-out status write (SPEC-004), the system sends a single standardized global opt-out confirmation SMS to the customer's cell number via Cool Text/Twilio. The confirmation message informs the customer they are opted out of ALL SCG text communications and provides the re-opt-in phone number. The message text is fixed and configured as a system constant; it is not dynamically assembled.

[ASSUMED: The exact message text is a legal/compliance-approved constant stored in system configuration. A placeholder template is used here:
"You have been unsubscribed from all Southern Company Gas text messages. To re-opt in, call [RE-OPT-IN-PHONE-NUMBER]. Msg&Data rates may apply."
The actual approved text must be provided by the Legal/Compliance team (CQ-002) and substituted before deployment. The re-opt-in phone number is a configuration value.]

[ASSUMED: If the confirmation SMS cannot be delivered (carrier rejection, invalid number, Cool Text error), the system logs the failure and does not retry more than once. The opt-out status remains OPT-OUT regardless of confirmation delivery failure.]

**Inputs:**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| destination_cell_number | String | E.164 format; the opted-out customer's number | Yes |
| opt_out_record_id | String | Reference to the opt-out record from SPEC-004 | Yes |
| opt_out_timestamp | String | ISO 8601 UTC; used for SLA tracking | Yes |

**Outputs:**
| Field | Type | Format/Constraints |
|-------|------|--------------------|
| confirmation_sent | Boolean | true if Cool Text accepted the message |
| cool_text_message_id | String | Platform message ID; null on failure |
| send_timestamp | String | ISO 8601 UTC timestamp when the confirmation was dispatched |
| sla_elapsed_seconds | Integer | Seconds from opt_out_timestamp to send_timestamp |

**Business Rules:**
- BR-021: The confirmation SMS must be sent within 60 seconds of the opt-out status write timestamp (NFR-001).
- BR-022: The confirmation message text is a single standardized global message (REQ-007); it is not application-specific.
- BR-023: The confirmation is sent only once per opt-out event; re-triggering an opt-out on an already-opted-out number does not resend the confirmation (BR-015).
- BR-024: The confirmation SMS is sent from the Cool Text account that received the opt-out keyword, so the customer's device associates the reply with the same sender.
- BR-025: A confirmation SMS delivery failure does not reverse the opt-out status. The customer is opted out regardless.
- BR-026: The SLA clock starts at the timestamp of inbound message receipt (not at the time of database write).

**Edge Cases:**
| Scenario | Expected System Behavior |
|----------|--------------------------|
| Cool Text rejects the confirmation SMS | Log delivery failure; do not retry more than once; opt-out status remains OPT-OUT |
| Confirmation sent but SLA would be breached (>60s from receipt) | Send anyway; log SLA breach event for compliance review |
| Customer's number is invalid or unreachable (carrier-level failure) | Log delivery failure; opt-out status remains OPT-OUT |
| Status write completed but confirmation dispatch exceeds 60s due to system load | Log SLA breach; alert operations; compliance review |

**Error Conditions:**
| Error | Trigger | System Response |
|-------|---------|-----------------|
| Cool Text API unavailable | Cannot dispatch confirmation | Log failure; single retry after brief delay; log permanent failure if retry fails |
| opt_out_record_id not found | Orphaned call without a valid opt-out record | Log error; do not send confirmation |

---

### SPEC-006: Outbound SMS Block Enforcement
- **Source Requirements:** REQ-006, REQ-008
- **PRD Reference:** PRD §3
- **Priority:** Must Have
- **Dependencies:** SPEC-004 (Opt-out status must exist in the database), SPEC-001 (Block decision is made within the outbound proxy gate)

**Behavior:**
This spec defines the enforcement behavior within SPEC-001. When an outbound SMS request is received and the destination cell number has OPT-OUT status in the TCPA database, the message is blocked immediately — it is never forwarded to Cool Text/Twilio. The block takes effect immediately upon the opt-out status write (not after a delay). The TCPA "within 10 calendar days" requirement is a regulatory ceiling; the system must enforce the block immediately.

**Inputs:**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| destination_cell_number | String | E.164 format | Yes |
| current_opt_out_status | Enum | OPT_IN \| OPT_OUT (from TCPA database lookup) | Yes |

**Outputs:**
| Field | Type | Format/Constraints |
|-------|------|--------------------|
| block_applied | Boolean | true if message was suppressed due to OPT-OUT status |
| block_timestamp | String | ISO 8601 UTC |

**Business Rules:**
- BR-027: A cell number with OPT-OUT status receives no outbound SMS from any in-scope application via the TCPA API.
- BR-028: The opt-out block is enforced immediately upon status write; there is no grace period or delay.
- BR-029: The 10 calendar day window (REQ-006) is a TCPA regulatory maximum ceiling, not a permitted delay. Implementation must be immediate.
- BR-030: A blocked message is never delivered to Cool Text/Twilio. There is no delayed delivery or queuing for later delivery.

**Edge Cases:**
| Scenario | Expected System Behavior |
|----------|--------------------------|
| Cell number opts out and application sends another message milliseconds later | Block applied if OPT-OUT status is in database at time of lookup; no race condition window permitted |
| Application retries a blocked message multiple times | Each retry is independently blocked and independently logged |

**Error Conditions:**
| Error | Trigger | System Response |
|-------|---------|-----------------|
| Database unavailable during block check | Cannot confirm OPT-OUT status | Fail-closed: block the message (SPEC-021) |

---

## BC-3: Re-Opt-In Management

### SPEC-007: Re-Opt-In via Privileged Admin API Endpoint
- **Source Requirements:** REQ-012
- **PRD Reference:** PRD §3
- **Priority:** Must Have
- **Dependencies:** SPEC-004 (Opt-out status records must exist before re-opt-in can reverse them)
- **Flags:** [COMPLEX: Authentication and authorization model for the admin endpoint must be tightly controlled — unauthorized re-opt-ins create compliance risk in the opposite direction.]

**Behavior:**
The TCPA API exposes a privileged REST/JSON admin endpoint that allows authorized Help Desk agents to manually update a cell number's opt-out status from OPT-OUT back to OPT-IN. The endpoint requires authentication. Only users with the Help Desk / Compliance Officer role may call this endpoint. The re-opt-in is global — the cell number is re-opted-in across all SCG applications simultaneously.

The endpoint also supports a read-only status lookup (GET) to allow a Help Desk agent to verify a cell number's current opt-out status before making the update.

[ASSUMED: Implementation is a privileged admin REST API endpoint with no UI in Phase 1 (per clarification default for CQ-003). Authentication uses the same identity provider as other SCG internal systems (specific mechanism to be confirmed by IT Security). The status lookup GET endpoint is included as a prerequisite workflow step for Help Desk agents (per CQ-014 default).]

**Inputs (Re-Opt-In Update — POST/PUT):**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| cell_number | String | E.164 format | Yes |
| agent_user_id | String | Authenticated agent identifier from auth token | Yes (from auth context) |
| reason | String | Free-text reason for re-opt-in (e.g., "Customer called in to request re-opt-in, ticket #12345") | Yes |
| ticket_reference | String | Help Desk ticket number or reference | No |

**Inputs (Status Lookup — GET):**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| cell_number | String | E.164 format; URL path parameter | Yes |

**Outputs (Re-Opt-In Update):**
| Field | Type | Format/Constraints |
|-------|------|--------------------|
| success | Boolean | true if status was updated to OPT_IN |
| previous_status | Enum | OPT_OUT \| OPT_IN |
| new_status | Enum | OPT_IN |
| updated_timestamp | String | ISO 8601 UTC |
| record_id | String | Audit record ID of the re-opt-in event |

**Outputs (Status Lookup):**
| Field | Type | Format/Constraints |
|-------|------|--------------------|
| cell_number | String | Echoed back (masked — last 4 digits visible only for display) |
| opt_out_status | Enum | OPT_IN \| OPT_OUT |
| last_opt_out_timestamp | String | ISO 8601 UTC of most recent opt-out; null if never opted out |
| last_opt_in_timestamp | String | ISO 8601 UTC of most recent re-opt-in; null if never re-opted-in |

**Business Rules:**
- BR-031: Only authenticated users with the Help Desk or Compliance Officer role may call the re-opt-in update endpoint.
- BR-032: Unauthenticated or unauthorized requests return 401/403 and are logged as security events.
- BR-033: Re-opt-in is global — it applies across all in-scope SCG applications simultaneously. There is no per-application re-opt-in.
- BR-034: Re-opt-in is a manual-only process; no automated system action triggers a re-opt-in (CON-010).
- BR-035: If the cell number is already OPT-IN, the re-opt-in update is accepted (idempotent) but logs the action.
- BR-036: The re-opt-in does not send a confirmation SMS to the customer in Phase 1 (per CQ-023 default: notification is handled by the Help Desk agent via phone/other channel).
- BR-037: The status lookup endpoint (GET) is read-only and returns masked cell number data (last 4 digits only) to minimize PII exposure in logs.
- BR-038: Initial customer opt-in (a customer who has never sent a STOP keyword) cannot be written via this endpoint — this endpoint is only for re-opt-in after a prior opt-out (CON-005, OOS-001, CONF-002 resolution).

**Edge Cases:**
| Scenario | Expected System Behavior |
|----------|--------------------------|
| Cell number has no opt-out record (never opted out) | Reject with 409 Conflict — re-opt-in endpoint is only for reversing a prior opt-out |
| Cell number is already OPT-IN (was previously re-opted-in) | Accept idempotently; log the action; return success |
| Unauthenticated request | Return 401 Unauthorized; log security event |
| Agent lacks required role | Return 403 Forbidden; log security event |
| reason field is missing | Return 400 Bad Request; re-opt-in not performed |

**Error Conditions:**
| Error | Trigger | System Response |
|-------|---------|-----------------|
| Invalid authentication token | Expired or malformed token | 401 Unauthorized |
| Insufficient permissions | Caller role not Help Desk or Compliance Officer | 403 Forbidden |
| Cell number not found | No record exists for this cell number | 404 Not Found |
| Cell number never opted out | Attempting re-opt-in when no opt-out record exists | 409 Conflict |
| Database unavailable | Cannot write re-opt-in record | 503 Service Unavailable |

---

## BC-4: Audit Logging

### SPEC-008: Opt-Out Event Audit Log Entry
- **Source Requirements:** REQ-009, REQ-011
- **PRD Reference:** PRD §3
- **Priority:** Must Have
- **Dependencies:** SPEC-004 (Opt-out status write triggers the audit log entry)

**Behavior:**
Every time an opt-out event is successfully processed (opt-out keyword detected and OPT-OUT status written), the system writes an immutable audit log entry. The audit log entry must be written atomically with the status write — an opt-out that fails to produce an audit log entry is a compliance failure. The audit log must be retained for a minimum of 5 years from the event date.

[ASSUMED: The audit log required fields (CQ-008 default) are taken as those explicitly listed in REQ-009. The field "TCPA-required fields" is interpreted as including: the CTIA-standard fields of date/time, cell number, opt-out keyword, and system response. The full field set is defined below. Any additional fields identified during Legal review must be added before go-live.]

**Inputs (from opt-out processing pipeline):**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| event_type | Enum | OPT_OUT | Yes |
| event_timestamp | String | ISO 8601 UTC; timestamp of inbound message receipt | Yes |
| cell_number | String | E.164 format | Yes |
| originating_cool_text_account_id | String | Cool Text account that received the opt-out | Yes |
| originating_application_name | String | Resolved application name (e.g., "GCMA", "KMI") | Yes |
| opt_out_keyword_received | String | The exact keyword string from the message body | Yes |
| message_body | String | Full inbound message body | Yes |
| system_response | String | Description of system action (e.g., "OPT_OUT_STATUS_WRITTEN", "ALREADY_OPT_OUT_NO_ACTION") | Yes |
| confirmation_sms_sent | Boolean | Whether the opt-out confirmation SMS was dispatched | Yes |
| confirmation_sms_timestamp | String | ISO 8601 UTC; null if not sent | No |
| confirmation_sms_status | Enum | SENT \| FAILED \| NOT_SENT | Yes |
| record_id | String | UUID; unique identifier for this audit record | Yes (system-generated) |

**Outputs:**
| Field | Type | Format/Constraints |
|-------|------|--------------------|
| audit_record_id | String | UUID of the written audit record |
| write_success | Boolean | true if audit record persisted successfully |

**Business Rules:**
- BR-039: An audit log entry must be written for every opt-out event, including events where the cell number was already OPT-OUT (idempotent opt-out attempts are still logged).
- BR-040: The audit log is immutable — records cannot be updated or deleted after writing.
- BR-041: Audit log data must be retained for a minimum of 5 years from the event_timestamp (NFR-004).
- BR-042: A failure to write the audit log entry is a critical error that must trigger an alert to the operations team; it does not roll back the opt-out status write.
- BR-043: Cell phone numbers in the audit log are stored in a way that supports regulatory discovery requests while complying with applicable data handling requirements.

**Edge Cases:**
| Scenario | Expected System Behavior |
|----------|--------------------------|
| Opt-out status write succeeds but audit log write fails | Log critical error; alert operations; do not roll back opt-out |
| Cell number already OPT-OUT when keyword received | Write audit log entry with system_response = "ALREADY_OPT_OUT_NO_ACTION" |
| Confirmation SMS not sent (delivery failure) | Log confirmation_sms_status = FAILED; audit entry still written |

**Error Conditions:**
| Error | Trigger | System Response |
|-------|---------|-----------------|
| Audit log database unavailable | Cannot persist audit record | Log critical error to operational log; alert operations team |

---

### SPEC-009: Blocked Outbound SMS Audit Log Entry
- **Source Requirements:** REQ-010, REQ-011
- **PRD Reference:** PRD §3
- **Priority:** Must Have
- **Dependencies:** SPEC-006 (Block enforcement triggers this log entry), SPEC-001 (Outbound proxy identifies the blocking event)

**Behavior:**
Every time an outbound SMS is suppressed because the destination cell number has OPT-OUT status, the system writes an immutable audit log entry. The entry records all information needed to demonstrate that the block was correctly applied.

**Inputs:**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| event_type | Enum | BLOCKED_OUTBOUND | Yes |
| event_timestamp | String | ISO 8601 UTC; timestamp of the suppression decision | Yes |
| cell_number | String | E.164 format; the destination that was blocked | Yes |
| originating_cool_text_account_id | String | Cool Text account that submitted the outbound request | Yes |
| originating_application_name | String | Resolved application name | Yes |
| message_body | String | Full message body of the suppressed message | Yes |
| suppression_reason | String | "OPT_OUT" | Yes |
| record_id | String | UUID; system-generated | Yes |

**Outputs:**
| Field | Type | Format/Constraints |
|-------|------|--------------------|
| audit_record_id | String | UUID |
| write_success | Boolean | true if record persisted |

**Business Rules:**
- BR-044: Every blocked outbound SMS attempt generates an independent audit log entry.
- BR-045: The audit log is immutable; records cannot be updated or deleted.
- BR-046: Blocked outbound audit records are retained for a minimum of 5 years (NFR-004).
- BR-047: Message body is stored in the audit log to support regulatory discovery.
- BR-048: A failure to write the blocked-outbound audit log entry is a critical error; the block on the message is still applied regardless of audit log write failure.

**Edge Cases:**
| Scenario | Expected System Behavior |
|----------|--------------------------|
| Application submits 100 blocked messages in rapid succession | Each generates an independent audit log entry |
| Audit log database unavailable | Log critical error; block still applied; alert operations |

**Error Conditions:**
| Error | Trigger | System Response |
|-------|---------|-----------------|
| Audit log database unavailable | Cannot write blocked-outbound record | Critical error; alert operations; block still enforced |

---

### SPEC-010: Re-Opt-In Event Audit Log Entry
- **Source Requirements:** REQ-009 (audit trail for all compliance-relevant events), REQ-011
- **PRD Reference:** PRD §3
- **Priority:** Must Have
- **Dependencies:** SPEC-007 (Re-opt-in write triggers this log entry)

**Behavior:**
Every manual re-opt-in action performed via SPEC-007 generates an immutable audit log entry. The entry records who performed the re-opt-in, when, for which cell number, and the reason provided.

**Inputs:**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| event_type | Enum | RE_OPT_IN | Yes |
| event_timestamp | String | ISO 8601 UTC | Yes |
| cell_number | String | E.164 format | Yes |
| agent_user_id | String | Authenticated user ID of the Help Desk agent | Yes |
| reason | String | Free-text reason provided by the agent | Yes |
| ticket_reference | String | Help Desk ticket reference | No |
| previous_status | Enum | OPT_OUT \| OPT_IN | Yes |
| record_id | String | UUID; system-generated | Yes |

**Outputs:**
| Field | Type | Format/Constraints |
|-------|------|--------------------|
| audit_record_id | String | UUID |
| write_success | Boolean | |

**Business Rules:**
- BR-049: Every re-opt-in action, including idempotent ones (re-opting-in a number already OPT-IN), generates an audit log entry.
- BR-050: The audit log entry is immutable and retained for 5 years (NFR-004).

---

## BC-5: Compliance Reporting

### SPEC-011: On-Demand Report — SMS to Opted-In Numbers
- **Source Requirements:** REQ-013
- **PRD Reference:** PRD §3
- **Priority:** Must Have
- **Dependencies:** SPEC-001 (Forwarded outbound messages must be recorded), SPEC-009 (Audit log is the data source)

**Behavior:**
The system provides a queryable data set (and report output) containing all outbound SMS messages that were successfully forwarded to opted-in cell numbers. The report data includes the fields specified in REQ-013 for each forwarded message. This report data is the underlying data set used by the weekly compliance report (SPEC-013).

[ASSUMED: Per CQ-018 default — REQ-013 and REQ-014 are the underlying queryable data sets; REQ-015 is the automated weekly delivery combining both. The on-demand query is available to Compliance Officers via a mechanism to be determined by the architecture (API query or report extract). No self-service UI is in scope for Phase 1.]

**Inputs (Query Parameters):**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| date_from | String | ISO 8601 date; start of reporting period | Yes |
| date_to | String | ISO 8601 date; end of reporting period | Yes |
| application_filter | String | Filter by application name; null = all applications | No |
| cell_number_filter | String | Filter by specific cell number; null = all numbers | No |

**Outputs (per record):**
| Field | Type | Format/Constraints |
|-------|------|--------------------|
| status | String | "FORWARDED" |
| cell_number | String | E.164 |
| originating_application_name | String | |
| message_timestamp | String | ISO 8601 UTC |
| message_body | String | |
| cool_text_account_id | String | |

**Business Rules:**
- BR-051: Only users with the Compliance Officer or authorized reporting role may access this data.
- BR-052: The report reflects data from the audit log; it does not query the live SMS platform.
- BR-053: The report covers all four SCG LDCs (CON-009).

---

### SPEC-012: On-Demand Report — SMS Attempted to Opted-Out Numbers
- **Source Requirements:** REQ-014
- **PRD Reference:** PRD §3
- **Priority:** Must Have
- **Dependencies:** SPEC-009 (Blocked outbound audit log is the data source)

**Behavior:**
The system provides a queryable data set of all outbound SMS messages that were suppressed because the destination cell number had OPT-OUT status. This serves as evidence of correct TCPA enforcement and is also used by the weekly compliance report (SPEC-013).

**Inputs (Query Parameters):**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| date_from | String | ISO 8601 date | Yes |
| date_to | String | ISO 8601 date | Yes |
| application_filter | String | Filter by application name; null = all | No |
| cell_number_filter | String | Filter by specific cell number; null = all | No |

**Outputs (per record):**
| Field | Type | Format/Constraints |
|-------|------|--------------------|
| status | String | "BLOCKED" |
| cell_number | String | E.164 |
| originating_application_name | String | |
| attempt_timestamp | String | ISO 8601 UTC |
| message_body | String | |
| suppression_reason | String | "OPT_OUT" |

**Business Rules:**
- BR-054: Only users with the Compliance Officer or authorized reporting role may access this data.
- BR-055: The data set is drawn from the immutable audit log.

---

### SPEC-013: Automated Weekly Compliance Report
- **Source Requirements:** REQ-015
- **PRD Reference:** PRD §3
- **Priority:** Must Have
- **Dependencies:** SPEC-011, SPEC-012 (Data sets must be queryable), SPEC-008, SPEC-009 (Audit log data)
- **Flags:** [COMPLEX: Requires a scheduled job, email dispatch integration, and report formatting logic. Report generation must be reliable — a missed weekly report is a compliance visibility gap.]

**Behavior:**
Every Monday at 6:00 AM, the TCPA API automatically generates a weekly compliance report covering the prior 7 calendar days (Monday through Sunday). The report is emailed to the Compliance Officers distribution list. The report contains:
1. Summary of all SMS messages forwarded to opted-in numbers (count, per-application breakdown).
2. Summary of all SMS messages blocked/suppressed to opted-out numbers (count, per-application breakdown).
3. Any cases where a message was delivered to a cell number that was opted out at the time of delivery (compliance failures — should be zero; non-zero is an alert condition).
4. Opt-out success rate KPI: (total opt-out events processed / (opt-out events + confirmation failures)) × 100%.
5. Total opt-out and re-opt-in counts for the period.

[ASSUMED: Report is emailed to Compliance Officers every Monday at 6:00 AM (per clarification default for CQ-004). Format is an HTML email body with a CSV attachment containing the detailed records. Recipients are the Compliance Officer persona group; specific distribution list to be confirmed by IT/Compliance. If a Monday report generation fails, the failure is logged and an alert is sent to IT.]

**Inputs (scheduled trigger):**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| report_period_start | String | ISO 8601 date; prior Monday 00:00:00 UTC | System-calculated |
| report_period_end | String | ISO 8601 date; prior Sunday 23:59:59 UTC | System-calculated |

**Outputs (email):**
| Field | Type | Format/Constraints |
|-------|------|--------------------|
| email_subject | String | "TCPA Compliance Weekly Report — [period dates]" |
| email_body | String | HTML summary with KPI metrics |
| csv_attachment | File | Detailed records: SPEC-011 + SPEC-012 data for the period |
| recipients | List<Email> | Compliance Officers distribution list (configuration) |

**Business Rules:**
- BR-056: Report generation runs every Monday at 6:00 AM system time (assumed UTC unless configured otherwise).
- BR-057: The report covers the 7-day period from the prior Monday 00:00:00 UTC through Sunday 23:59:59 UTC.
- BR-058: Report generation requires no manual intervention.
- BR-059: Any compliance failures detected (messages delivered to opted-out numbers) are prominently highlighted in the report.
- BR-060: If report generation or email dispatch fails, a critical alert is sent to IT/Platform Engineering; the failure is logged with full error detail.
- BR-061: The report email is not sent to external parties — it is internal SCG distribution only.

**Edge Cases:**
| Scenario | Expected System Behavior |
|----------|--------------------------|
| No SMS activity in the reporting period | Report still generated with zero counts |
| Email dispatch fails | Log failure; retry once; log permanent failure and alert IT if retry fails |
| Report generation job fails (exception) | Log critical error; alert IT; do not silently skip |
| Compliance failure detected (non-zero messages to opted-out numbers) | Include alert section in report; trigger additional alert to Compliance Officers |

**Error Conditions:**
| Error | Trigger | System Response |
|-------|---------|-----------------|
| Scheduled job does not run | Infrastructure failure | Alert IT; investigate; manually trigger if needed |
| Email server unavailable | SMTP/email relay unreachable | Retry once; log permanent failure if retry fails |

---

## BC-6: Application Registration & Configuration

### SPEC-014: Cool Text Account Registration
- **Source Requirements:** REQ-016, REQ-018
- **PRD Reference:** PRD §3
- **Priority:** Must Have
- **Dependencies:** None

**Behavior:**
The TCPA API maintains a configuration registry mapping Cool Text account identifiers to in-scope SCG applications. This registry determines which applications are subject to TCPA enforcement. An application is "in scope" if and only if its Cool Text account ID appears in the registry. Applications not in the registry pass through the system without enforcement (REQ-018).

[ASSUMED: Application registration is managed via a configuration file or database record maintained by IT/Platform Engineering. No self-service UI is required in Phase 1 (per CQ-016 default). The initial registry entries for the five in-scope applications (BizTalk, GCMA, KMI, ARM, CCB/My Account) are populated at deployment time via configuration. Changes to the registry require an IT-managed configuration deployment.]

The registry stores, per application:
- Cool Text account ID (used to match inbound and outbound messages)
- Application name (human-readable label for logging and reports)
- Application callback URL (for routing inbound non-opt-out SMS replies)
- Active/inactive flag (to disable an application without deleting its record)
- Onboarded date

**Inputs (configuration — managed by IT, not a runtime API):**
| Field | Type | Constraints | Required |
|-------|------|-------------|----------|
| cool_text_account_id | String | Unique; non-empty | Yes |
| application_name | String | Human-readable; non-empty | Yes |
| callback_url | String | Valid HTTPS URL; used for inbound reply routing | Yes |
| active | Boolean | Whether this account is actively enforced | Yes |

**Outputs (runtime lookup result):**
| Field | Type | Format/Constraints |
|-------|------|--------------------|
| is_registered | Boolean | true if account ID found in registry |
| application_name | String | null if not registered |
| callback_url | String | null if not registered |
| is_active | Boolean | null if not registered |

**Business Rules:**
- BR-062: Exactly the five in-scope applications are registered at launch: BizTalk, GCMA, KMI Active, ARM/Construction Portal, CCB/My Account.
- BR-063: CCB/My Account registration is included but the active flag may be set to false pending confirmation of CCB go-live readiness (ASM-004). The active flag can be toggled without a code change.
- BR-064: An unregistered Cool Text account ID is treated as pass-through — no enforcement, no logging of compliance events (REQ-018).
- BR-065: Cool Text account IDs are provided via configuration, not user-entered at runtime (ASM-006).
- BR-066: The registry change process is an IT configuration deployment; no runtime admin API for registry changes in Phase 1.

**Edge Cases:**
| Scenario | Expected System Behavior |
|----------|--------------------------|
| Inbound message from unregistered Cool Text account | Pass through without enforcement; log warning |
| Outbound message for unregistered Cool Text account | Pass through without enforcement |
| Application marked inactive in registry | Treat as unregistered — no enforcement |

---

## BC-7: Observability

### SPEC-015: Structured Operational & Debug Logging
- **Source Requirements:** REQ-017
- **PRD Reference:** PRD §3
- **Priority:** Must Have
- **Dependencies:** None (cross-cutting concern)

**Behavior:**
The TCPA API emits structured logs for every significant operational event. Logs are written in a machine-parseable format (JSON) and are accessible to IT/Platform Engineering through the standard log infrastructure. Two log levels are supported:
- **Production logs:** Significant events only — opt-out received, message forwarded, message blocked, confirmation SMS sent/failed, re-opt-in processed, weekly report generated/failed, authentication events. Suitable for always-on production monitoring.
- **Debug logs:** Full request/response detail, database query timings, retry attempts, internal state transitions. Available for incident diagnosis; may be toggled on/off per environment.

**Business Rules:**
- BR-067: All log entries include: timestamp (ISO 8601 UTC), log level, event type, correlation ID (request-scoped UUID for tracing), and relevant entity IDs.
- BR-068: Cell phone numbers in logs are masked (last 4 digits visible only) to limit PII exposure in log aggregation systems.
- BR-069: Message body content is not logged in production logs; it is available in debug logs only.
- BR-070: No credentials, authentication tokens, or API keys appear in any log output.
- BR-071: Logs do not contain raw PII beyond the masked cell number format.
- BR-072: Production and debug log streams are separable so production log volumes remain manageable.

**Edge Cases:**
| Scenario | Expected System Behavior |
|----------|--------------------------|
| Log write fails (disk full, log sink unavailable) | System continues operating; log failure itself is caught and emitted at a best-effort fallback channel |
| Debug logging enabled in production | Possible via configuration flag; not enabled by default; must not impact request throughput |

---

## Non-Functional Specifications

### NFS-001: Opt-Out Confirmation SMS Timing
- **Source:** NFR-001
- **Category:** Compliance
- **Measurable Target:** The opt-out confirmation SMS (SPEC-005) must be dispatched to Cool Text within 60 seconds of the timestamp of the inbound message receipt that triggered the opt-out. Measured end-to-end from webhook receipt to Cool Text API call initiation.
- **Verification Method:** Load test with recorded timestamps at webhook receipt and at Cool Text API call; assert p99 dispatch latency ≤ 60 seconds. SLA breach events logged and available for compliance audit.

---

### NFS-002: Opt-Out Enforcement Timing
- **Source:** NFR-002
- **Category:** Compliance
- **Measurable Target:** Once a cell number's OPT-OUT status is written to the TCPA database (SPEC-004), any subsequent outbound SMS to that number (SPEC-001, SPEC-006) must be blocked. The block must apply to 100% of outbound requests received after the status write completes. There is no grace period.
- **Verification Method:** Automated test: write OPT-OUT status for a cell number; immediately submit outbound SMS request for that number; assert SUPPRESSED response. Test with concurrent requests to verify no race condition.

---

### NFS-003: TCPA Regulatory Deadline
- **Source:** NFR-003
- **Category:** Compliance
- **Measurable Target:** The TCPA API must be deployed to production and enforcing opt-out rules for all five in-scope applications by January 31, 2027.
- **Verification Method:** Production deployment sign-off checklist with date stamp; all five application integrations verified end-to-end in production before deadline.

---

### NFS-004: Audit Log Retention
- **Source:** NFR-004
- **Category:** Data Retention
- **Measurable Target:** Audit log records (SPEC-008, SPEC-009, SPEC-010) must be retained for a minimum of 5 years from the event_timestamp of each record. Records must be queryable (not just archived) for the full 5-year retention period.
- **Verification Method:** Data retention policy applied at the storage layer; automated purge test confirms records older than 5 years + 1 day are eligible for purge while records at exactly 5 years are retained. Spot-query test confirms 5-year-old records are accessible.

---

### NFS-005: Fail-Closed on TCPA API Unavailability
- **Source:** NFR-005
- **Category:** Reliability / Compliance
- **Measurable Target:** If the TCPA API database is unavailable and the opt-out status of a destination cell number cannot be determined, the outbound SMS must be blocked (not forwarded). The upstream application receives a 503 Service Unavailable response. Zero messages must be forwarded without a confirmed OPT-IN status check.
- **Verification Method:** Integration test: bring down TCPA database; submit outbound SMS request; assert 503 response and no message delivered to Cool Text. Verify message blocked count in operational logs.

---

### NFS-006: Opt-Out Processing Latency
- **Source:** NFR-006
- **Category:** Performance
- **Measurable Target:** From inbound webhook receipt (Step 1) to opt-out status written to database (Step 4 — SPEC-004 completion), the elapsed time must be ≤ 5 seconds at p99 under normal operating load. This leaves at least 55 seconds of headroom within the 60-second confirmation SMS SLA (NFS-001).
- **Verification Method:** Instrumented timing in integration tests and production monitoring; p99 latency metric on the opt-out processing pipeline; alert if p99 exceeds 5 seconds.

---

### NFS-007: PII Protection in Transit and at Rest
- **Source:** NFR-007
- **Category:** Security
- **Measurable Target:** (a) All API communication (inbound from applications, outbound to Cool Text, admin endpoint) uses HTTPS with TLS 1.2 or higher. TLS 1.0 and 1.1 are disabled. (b) Cell phone numbers stored in the TCPA database are encrypted at rest using AES-256 or equivalent. (c) Cell phone numbers in logs are masked (last 4 digits visible only — BR-068).
- **Verification Method:** (a) TLS configuration scan (e.g., testssl.sh or equivalent) confirms TLS 1.2+ only. (b) Database column encryption verified in schema review. (c) Log output review confirms masking is applied in all log events.

---

### NFS-008: Audit Log Completeness
- **Source:** NFR-008
- **Category:** Auditability
- **Measurable Target:** 100% of opt-out events (SPEC-003 positive detections) must produce a corresponding audit log entry (SPEC-008). 0% silent failures — every failure to write an audit log entry must generate a critical operational alert. Measured as: (audit_log_entries / opt_out_events_processed) = 1.00.
- **Verification Method:** Integration test: process N opt-out events; assert N audit log entries exist. Chaos test: simulate audit log write failure; assert critical alert is generated and no opt-out event is silently dropped from the audit trail.

---

### NFS-009: System Availability
- **Source:** NFR-011
- **Category:** Availability
- **Measurable Target:** The TCPA API must achieve 99.9% uptime measured on a rolling 30-day window (≤ 43.8 minutes of unplanned downtime per month). SLA applies 24x7x365.
- **Verification Method:** Uptime monitoring with external health-check probe at ≤ 1-minute intervals. Monthly SLA report generated from monitoring data. Planned maintenance windows excluded from SLA calculation if communicated in advance.

---

### NFS-010: Structured Log Availability
- **Source:** NFR-010
- **Category:** Observability
- **Measurable Target:** All production log events are written in structured JSON format and are accessible to IT/Platform Engineering via the standard log aggregation platform within 5 minutes of the event occurring. Debug logs are togglable without a service restart.
- **Verification Method:** Log format validation in CI (assert all log output is valid JSON). Log availability test: emit test event; verify it appears in log aggregation system within 5 minutes.

---

## Spec Dependency Map

```
SPEC-020 (App Registration — BC-6)
  └── SPEC-001 (Outbound SMS Proxy)
        └── SPEC-006 (Block Enforcement)
              └── SPEC-009 (Blocked Outbound Audit Log)
                    └── SPEC-012 (On-Demand Report: Opted-Out)
                          └── SPEC-013 (Weekly Compliance Report)

SPEC-002 (Inbound SMS Routing)
  └── SPEC-003 (Opt-Out Keyword Detection)
        └── SPEC-004 (Opt-Out Status Write)
              ├── SPEC-005 (Opt-Out Confirmation SMS)
              ├── SPEC-008 (Opt-Out Event Audit Log)
              │     └── SPEC-011 (On-Demand Report: Opted-In)
              │           └── SPEC-013 (Weekly Compliance Report)
              └── SPEC-007 (Re-Opt-In Admin Endpoint)
                    └── SPEC-010 (Re-Opt-In Audit Log)

SPEC-015 (Structured Logging) — cross-cutting, no upstream dependency
```

Note: SPEC-014 is the configuration spec for SPEC-020 (Application Registration). These are combined as BC-6 / SPEC-014 (the runtime lookup) backed by the configuration registry. SPEC-020 referenced in the dependency map = SPEC-014 runtime lookup component.

---

## Specs Summary
- Total specs: 15 functional specs + 10 non-functional specs = 25 total
- Bounded contexts: 7
- Complex specs requiring architecture attention: 3 (SPEC-001, SPEC-007, SPEC-013)
- Must Have: 15 | Should Have: 0 | Could Have: 0
- Non-Functional Specs: 10 (NFS-001 through NFS-010)

### Requirement Coverage
| REQ-ID | Covered by SPEC(s) |
|--------|--------------------|
| REQ-001 | SPEC-001, SPEC-002 |
| REQ-002 | SPEC-002 |
| REQ-003 | SPEC-003 |
| REQ-004 | SPEC-004 |
| REQ-005 | SPEC-005 |
| REQ-006 | SPEC-006 |
| REQ-007 | SPEC-005 |
| REQ-008 | SPEC-001, SPEC-006 |
| REQ-009 | SPEC-008 |
| REQ-010 | SPEC-009 |
| REQ-011 | SPEC-008, SPEC-009, SPEC-010 (NFS-004) |
| REQ-012 | SPEC-007 |
| REQ-013 | SPEC-011 |
| REQ-014 | SPEC-012 |
| REQ-015 | SPEC-013 |
| REQ-016 | SPEC-014 |
| REQ-017 | SPEC-015 |
| REQ-018 | SPEC-001, SPEC-014 |

### Assumptions Applied in This Stage
| ID | Assumption | Source |
|----|-----------|--------|
| SPEC-A-001 | All in-scope applications use REST/JSON to communicate with TCPA API; BizTalk protocol requires IT verification | CQ-011 clarification default |
| SPEC-A-002 | Opt-out keyword matching is exact word-boundary match, case-insensitive; not substring-only | CQ-001 clarification default |
| SPEC-A-003 | Fail behavior when TCPA API is unavailable is fail-closed (block SMS, return 503) | CQ-005 clarification default |
| SPEC-A-004 | CCB/My Account included in scope; active flag in configuration allows go-live to be gated | CQ-009 clarification default |
| SPEC-A-005 | Inbound routing uses Cool Text account ID to map to originating application | CQ-010 clarification default |
| SPEC-A-006 | Availability SLA is 99.9% uptime 24x7 | CQ-007 clarification default |
| SPEC-A-007 | Weekly reports emailed to Compliance Officers every Monday at 6:00 AM | CQ-004 clarification default |
| SPEC-A-008 | Re-opt-in mechanism is a privileged admin REST API endpoint; no UI in Phase 1 | CQ-003 clarification default |
| SPEC-A-009 | Duplicate opt-out keyword from already-opted-out number: log only, no re-confirmation SMS | CQ-021 default |
| SPEC-A-010 | Re-opt-in does not send confirmation SMS to customer in Phase 1; Help Desk notifies via phone | CQ-023 default |
| SPEC-A-011 | Opt-out confirmation SMS message text is a compliance-approved constant stored in configuration; placeholder used; actual text to be provided by Legal before go-live | CQ-002 open |
| SPEC-A-012 | Application registration is a configuration-file-managed IT process; no runtime admin API for registration in Phase 1 | CQ-016 default |
