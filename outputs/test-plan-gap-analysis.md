# Gap Analysis: Legacy BizTalk TCPA System vs. Generated .NET 8 TCPA Compliance Engine API Test Plan

**Document Type:** QA Gap Analysis — Pre-Go-Live Gate Review
**Reference Plan:** Legacy BizTalk TCPA System Test Cases (`Copy of BizTalk-TCPA-Test Case-20260609.xls`) — 22 unique TCs (TC-135106 through TC-135947)
**Generated Plan:** .NET 8 TCPA Compliance Engine API Test Plan (`tests/TCPA-Test-Cases.csv` / `tests/TCPA-Test-Plan.xlsx`) — 150 test cases
**Analysis Date:** 2026-06-26
**Intended Audience:** QA Lead, Compliance Officer, Engineering Lead

---

## 1. Executive Summary

The generated 150-TC plan substantially expands coverage over the legacy 22-TC reference — adding security, NFR, audit immutability, compliance reporting, and API contract tests that never existed in the old system. However, four behavioral gaps were found where legacy functionality may not have been carried forward to the new architecture: two are **HIGH risk** (the exception override mechanism for PURL/SONP/Emergency messages, and the TCPAEnabled kill switch), and two are **MEDIUM risk** (HELP keyword passthrough + callback forwarding, and the in-app work-order opt-out vs. TCPA global opt-out boundary). Two additional LOW risk gaps exist (CoolTexting 408 timeout, re-opt-in data model semantics).

Of the 22 unique reference TCs, 14 are correctly superseded by the new architecture, 2 have partial coverage requiring supplemental test cases, and 6 require new dedicated test cases to close confirmed or probable gaps. Items marked CRITICAL require a product decision — either adding test cases or obtaining explicit documented sign-off from Legal/Compliance — before the test plan can be submitted for go-live gate approval.

---

## 2. Coverage Comparison

| Metric | Reference Plan (Legacy BizTalk) | Generated Plan (.NET 8 API) |
|---|---|---|
| Total test cases | 23 (22 unique; TC-135562 is duplicate of TC-135519) | 150 |
| Unique behaviors covered | ~18 distinct behavioral areas | ~42 distinct behavioral areas |
| Security tests | 0 | 24 (HMAC validation, API key, JWT/RBAC, constant-time) |
| NFR / SLA tests | 0 | 8 (60s SLA, TLS 1.2+, AES-256, 99.9% uptime) |
| API contract tests | 0 | 12 |
| Audit immutability tests | 0 | 6 (DDL trigger, WORM blob, completeness check) |
| Compliance report tests | 0 | 6 (opted-in, opted-out, weekly) |
| Health check tests | 0 | 3 |
| Exception override tests | 2 (TC-135564, TC-135642) | **0 — GAP** |
| Kill switch / feature flag tests | 1 (TC-135644) | **0 — GAP** |
| HELP keyword passthrough tests | 1 (TC-135518) | Partial — no callback forwarding test |
| App-level vs. TCPA opt-out boundary | 1 (TC-135519) | **0 — GAP** |
| Reference TCs fully superseded by new architecture | — | 14 of 22 |
| Reference TCs with confirmed gaps | — | 6 of 22 |

---

## 3. HIGH RISK Gaps

### 3.1 Exception Override Mechanism — Emergency / PURL / SONP Delivery (TC-135564, TC-135642)

**Risk Level:** HIGH — Compliance and Legal Exposure

**Legacy Behavior:**
The legacy BizTalk system implements a configurable exception override mechanism governed by the `dbo.OptOutExceptions` table and documented in section 7.2 of the legacy specification ("Parsing the Message for exceptions"). When a message body contains a word configured as an exception (e.g., "Emergency"), the system bypasses the TCPA opt-out check and delivers the message to the customer regardless of opt-out status. The `Reason` column in `TCPALogs` records the exception word that triggered the override. Named scenarios:

- **PURL (Payment URL):** Messages containing a payment link that customers are legally or contractually entitled to receive even if globally opted out of marketing SMS.
- **SONP (Southern Company Notice Protocol):** Emergency or safety-critical notifications (outage restoration, gas leak, emergency response) where non-delivery to an opted-out customer may create safety or regulatory liability.

**Gap in Generated Plan:**
The generated test plan contains no test case for the exception override mechanism. There is no test that:
- Verifies a message containing a configured exception keyword is delivered to an opted-out customer.
- Verifies the override reason is recorded in the audit log.
- Verifies that a message NOT containing an exception keyword is still blocked (confirming the exception is precise, not a blanket bypass).
- Verifies the `dbo.OptOutExceptions` table equivalent (or its replacement in the new architecture) is consulted during the filtering decision.

**Why This Is HIGH Risk:**
If the exception override mechanism was not carried forward into the new system, Southern Company cannot deliver legally required payment notifications or emergency safety messages to opted-out customers. Depending on jurisdiction and message type, failure to deliver emergency notifications may create regulatory liability. Failure to deliver payment-related messages may create breach-of-contract exposure.

**Required Action Before Go-Live:**
1. Confirm with the Product Owner and Legal/Compliance team whether the exception override mechanism was intentionally removed, intentionally retained, or inadvertently omitted from the new system's design.
2. If retained: add TC-NEW-001 through TC-NEW-003 (see Section 9).
3. If intentionally removed: document the decision with explicit sign-off from Legal/Compliance, confirm the new system has an equivalent mechanism (e.g., message-type allowlisting at the API caller level), and record the decision in the SDLC artifacts.
4. If inadvertently omitted: treat as a functional regression and raise a defect before test execution.

---

### 3.2 TCPAEnabled Feature Flag / Kill Switch (TC-135644)

**Risk Level:** HIGH (Operational) / MEDIUM-HIGH (Compliance)

**Legacy Behavior:**
The legacy system exposes a `dbo.Configuration` table with `KeyName="TCPAEnabled"` / `KeyValue="True"|"False"`. When the value is `False`, TCPA filtering is disabled entirely — messages are delivered to opted-out customers as if no opt-out list existed. This functions as an emergency kill switch: if the TCPA filtering logic malfunctions and begins incorrectly blocking legitimate messages, an operator can disable filtering entirely while a fix is developed.

**Gap in Generated Plan:**
The generated test plan contains no test case for a system-wide kill switch, feature flag, or emergency bypass mechanism. It does not indicate whether:
- An equivalent configuration key exists in the new system.
- The new system exposes a runtime toggle (API, environment variable, Azure App Configuration flag, feature management) for disabling TCPA filtering.
- The operational runbook includes an emergency procedure if TCPA filtering becomes faulty.

**Why This Is MEDIUM-HIGH Risk:**
The absence of a kill switch is an incident response capability gap. In a production incident where the TCPA filtering engine incorrectly blocks legitimate message delivery at scale, the legacy operator response is a single configuration table update. Without a tested equivalent, the incident response procedure is undefined. Additionally, if the kill switch was removed without a documented replacement procedure, that removal requires Compliance review — disabling TCPA filtering has regulatory implications.

**Required Action Before Go-Live:**
1. Confirm whether the new system retains an equivalent kill switch mechanism.
2. If retained: add TC-NEW-004 and TC-NEW-005 (see Section 9).
3. If intentionally removed: document the incident response procedure for TCPA filtering failures in the Operations Guide, confirm with Compliance, and add a monitoring/alerting test case.

---

## 4. MEDIUM RISK Gaps

### 4.1 HELP Keyword Passthrough and Callback Forwarding (TC-135518)

**Risk Level:** MEDIUM

**Legacy Behavior:**
TC-135518 verifies that customer replies containing "HELP" are not interpreted as opt-out triggers and ARE forwarded to the originating application's callback endpoint. The system must: (a) recognize "HELP" is not an opt-out keyword, (b) not modify the customer's opt-out status, (c) forward the reply to the application callback URL, and (d) record the interaction in logs.

**Partial Coverage in Generated Plan:**
The generated plan tests NONSTOP and CANCELLATION as non-matching strings (confirming they do not trigger opt-out). However, it does not contain a dedicated test case that verifies:
- A "HELP" reply is forwarded to the application callback endpoint.
- Forwarding occurs correctly even if the customer is currently opted out.
- The log entry for a HELP reply is categorized distinctly from opt-out events.

A test that confirms NONSTOP does not trigger opt-out does not confirm that HELP replies are routed to the callback — these are two different code paths.

**Required Action:** Add TC-NEW-006 (HELP from opted-in) and TC-NEW-007 (HELP from opted-out customer, still forwarded to callback).

---

### 4.2 Application-Level Work-Order Opt-Out vs. TCPA Global Opt-Out Boundary (TC-135519)

**Risk Level:** MEDIUM

**Legacy Behavior:**
TC-135519 documents two distinct and independent opt-out mechanisms:

- **In-app work-order opt-out:** Customer opts out through GCMA or ARM UI at the work-order level. Stops messages for that specific work order but does NOT write to the TCPA global opt-out list. Customer's TCPA opt-out status is unchanged.
- **TCPA keyword opt-out:** Customer replies to SMS with a keyword. Writes to the TCPA global opt-out list; affects all future TCPA-regulated messages.

**Gap in Generated Plan:**
The generated plan covers TCPA keyword opt-out thoroughly. It does not verify that performing an in-app work-order opt-out does NOT alter the TCPA global opt-out status for that customer.

This boundary is critical for compliance accuracy. If the systems are incorrectly coupled — if an in-app opt-out inadvertently sets TCPA global opt-out — customers who intend to stop one specific work-order communication will have their TCPA global consent record incorrectly altered.

**Required Action:** Add TC-NEW-008. This may require an integration-level test involving GCMA/ARM and the TCPA API. Confirm system boundary ownership with the integration team.

---

## 5. LOW RISK Gaps

### 5.1 CoolTexting API Timeout — 408 Response Code (TC-135523)

**Risk Level:** LOW

**Legacy Coverage:** TC-135523 verifies that a CoolTexting API timeout produces an HTTP 408 (Request Timeout) response.

**Generated Plan Coverage:** Tests 503 for database unavailability and tests CoolTexting retry logic. No explicit test for the timeout scenario as a distinct HTTP 408 response.

**Note:** The retry logic tests likely exercise the timeout code path implicitly. However, if the new system maps timeouts to 503 or 504 instead of 408, API callers that programmatically distinguish error types may behave incorrectly.

**Required Action:** Add TC-NEW-009.

---

### 5.2 Re-Opt-In Data Model Semantics — Physical Row Deletion vs. Status Flag (TC-135645)

**Risk Level:** LOW

**Legacy Behavior:** Re-opt-in in the legacy system: sets `OptOutChange="Deleted"` in `dbo.OptOutChangeLogs` AND physically deletes the customer's row from `dbo.OptOutList`. Opted-out state is represented by row presence, not a status flag.

**Generated Plan Coverage:** Tests re-opt-in status update and audit log creation but does not verify whether re-opt-in removes the customer from a separate "currently opted-out" store vs. updating a status field on an existing row.

**Why LOW Risk:** Observable behavior — customer receives messages after re-opt-in — is identical regardless of implementation. Risk is limited to edge cases where compliance reports count opted-out customers by row presence rather than status value.

**Required Action:** Verify the re-opt-in data model. If the new system uses a status flag, add TC-NEW-010 confirming re-opted-in customers appear correctly in both compliance reports.

---

## 6. Coverage Superseded by Architecture Change

The following reference TCs test behaviors specific to the legacy BizTalk architecture. The new .NET 8 system implements equivalent behaviors using different mechanisms. These are not gaps.

| Reference TC | Legacy Behavior Tested | New System Equivalent | Supersession Rationale |
|---|---|---|---|
| TC-135106, TC-135517 | `dbo.TCPALogs` table schema (Timestamp, PhoneNumber, CoolTextingAPIResponse, Message, OpCo, MessageSentStatus) | Structured JSON logging via Application Insights; `AuditLogEntries` DB table (STORY-019) | New system uses structured log events and a separate audit table. Column-level schema testing replaced by structured log field and audit entry tests. |
| TC-135560, TC-135561 | GTTF API middleware logging (inbound and outbound) | TCPA Compliance Engine API is the direct replacement for GTTF; STORY-019 logging tests cover equivalent behavior | GTTF as a middleware component no longer exists. The new API is its functional successor. |
| TC-135509 | Re-opt-in via direct SQL `UPDATE dbo.OptOutList SET OptOutStatus=0` | Authenticated REST API endpoint `PUT /admin/v1/opt-out/re-opt-in` with 12 dedicated test cases | Direct DB manipulation was a legacy workaround with no audit trail. The new system provides a proper API with authentication, authorization, and full audit logging. Note: the legacy direct-DB approach bypassed the audit log — itself a compliance gap in the legacy system. |
| TC-135643 | `dbo.OptOutKeywords` table — keyword storage and lookup | Keyword matching tested at behavior level (all 7 keywords, case-insensitive, word-boundary) | Whether keywords are stored in a DB table or compiled regex is an implementation detail. Behavioral coverage is complete. |
| TC-135146 | Retention period 5 years for TCPA records | TCPA-TC-098: Audit Immutability module, AC-003 STORY-023 NFS-004 | Covered. |
| TC-135520 | QUIT keyword — "confirmation within 10 days" | 60-second confirmation SLA tested (NFR-001) | The new system intentionally improves on the regulatory maximum. 60 seconds satisfies the 10-day upper bound. |
| TC-135521 | Error messages in `WebAPI.log` server-side file | Structured JSON error log events; Application Insights / Azure Monitor | The specific log file name is a legacy artifact. The new system uses structured cloud-native logging. |
| TC-135522 | Global opt-out confirmation SMS | Opt-Out Confirmation SMS module: 9 test cases (AC-001 through AC-005, STORY-006) | Fully covered. |
| TC-135946 | GCMA E2E workflow (UI step-by-step) | Application Integration module: 6 E2E test cases including GCMA workflow | Reference TC includes UI steps specific to current application versions. Generated plan tests the behavioral integration contract between GCMA and the TCPA API. UI-specific steps should remain as manual test scripts maintained by the GCMA team. |
| TC-135947 | ARM E2E workflow (UI step-by-step) | Application Integration module: 6 E2E test cases including ARM workflow | Same rationale as TC-135946. |
| TC-135563 | Case-insensitive keywords (sTop, STOP, stop, etc.) | AC-007 STORY-004: case-insensitive matching tested | Covered. |

---

## 7. New Coverage in Generated Plan Not in Reference

The following test areas exist in the generated plan but had no equivalent in the legacy BizTalk reference.

| Coverage Area | Test Count | Significance |
|---|---|---|
| HMAC-SHA256 webhook signature validation | 12 | Critical for preventing spoofed inbound webhook calls. Not applicable to legacy BizTalk architecture. |
| API key authentication | 4 | The legacy system used direct DB access; the new system exposes authenticated REST APIs. |
| JWT / Role-Based Access Control for admin endpoints | 6 | Admin functions now protected with 401/403 response verification. |
| Constant-time signature comparison (anti-timing attack) | 2 | Security hardening with no legacy equivalent. |
| PII masking in log output | 3 | Phone numbers and customer data redacted in application logs. |
| API response schema contract tests | 12 | Validates response shapes for all 6 endpoints. Legacy system had no versioned API contract. |
| TLS 1.2+ enforcement | 2 | NFR verification. |
| AES-256 encryption at rest | 2 | NFR verification. |
| 99.9% uptime / availability SLA | 2 | NFR verification. |
| 60-second opt-out confirmation SLA | 3 | NFR-001 verification. Replaces legacy "10 days" maximum with a tighter operational SLA. |
| Health check endpoint with DB degraded state | 3 | Operational readiness testing. |
| Audit log DDL trigger (rejects UPDATE/DELETE) | 3 | Immutability enforcement — audit records cannot be tampered with. |
| WORM blob storage | 2 | Audit archive immutability for compliance retention. |
| Compliance reports (opted-in, opted-out, weekly) | 6 | New reporting capability with no legacy equivalent. |
| Debug log level toggle (dynamic, no restart) | 2 | Operational capability test. |

---

## 8. Reference TC Disposition Table

| Reference TC | Title | Status | Gap Type | Risk | Notes |
|---|---|---|---|---|---|
| TC-135106 | TCPALogs table column structure | Superseded | Architecture change | None | New system uses structured JSON logs and AuditLogEntries. |
| TC-135145 | Unsubscribe keyword | Covered | N/A | None | UNSUBSCRIBE + global scope covered across multiple generated TCs. |
| TC-135146 | 5-year retention period | Covered | N/A | None | TCPA-TC-098, STORY-023 NFS-004. |
| TC-135508 | 6 opt-out keywords | Covered | N/A | None | Generated plan covers all 7 keywords (adds UNSUBSCRIBE). |
| TC-135509 | Re-opt-in via direct SQL UPDATE | Superseded | Architecture improvement | None | 12 generated TCs cover the new REST API endpoint with audit trail. |
| TC-135517 | Audit Log — TCPALogs columns | Superseded | Architecture change | None | Same rationale as TC-135106. |
| TC-135518 | HELP text message for ARM | **GAP** | Partial coverage | **MEDIUM** | Non-opt-out passthrough not tested; callback forwarding missing. |
| TC-135519 | App-level work-order opt-out vs. TCPA global opt-out | **GAP** | Behavioral gap | **MEDIUM** | Boundary between in-app and TCPA opt-out not tested. |
| TC-135520 | QUIT keyword — 10-day confirmation timing | Superseded | Architecture improvement | None | 60s SLA (NFR-001) supersedes 10-day regulatory maximum. |
| TC-135521 | Error messages in WebAPI.log | Superseded | Architecture change | None | Cloud-native structured logging replaces server log file. |
| TC-135522 | Global opt-out confirmation SMS | Covered | N/A | None | 9 TCs in Opt-Out Confirmation SMS module (STORY-006). |
| TC-135523 | No API response — 408 timeout | **GAP** | Error code coverage | **LOW** | 503 and retry tested; 408 timeout not explicitly tested. |
| TC-135560 | Logging flow outbound (App → CoolText) | Superseded | Architecture change | None | GTTF replaced by new TCPA API; STORY-019 logging covers equivalent. |
| TC-135561 | Logging flow inbound (CoolText → App) | Superseded | Architecture change | None | Same rationale as TC-135560. |
| TC-135562 | [Duplicate of TC-135519] | Deleted | Duplicate | None | Marked "Delete-(Duplicate)" in reference plan. |
| TC-135563 | Case-insensitive keywords | Covered | N/A | None | AC-007 STORY-004. |
| TC-135564 | Sending message despite opt-out (PURL) | **GAP** | Feature not carried forward | **HIGH** | Exception override mechanism absent from generated plan. |
| TC-135642 | OptOutExceptions table — Emergency / SONP | **GAP** | Feature not carried forward | **HIGH** | Same as TC-135564. |
| TC-135643 | OptOutKeywords table | Superseded | Architecture difference | None | Behavioral keyword coverage complete; storage mechanism is impl detail. |
| TC-135644 | TCPAEnabled feature flag / kill switch | **GAP** | Missing operational control | **HIGH** | No kill switch test in generated plan. |
| TC-135645 | OptOutChangeLogs row deletion on re-opt-in | **GAP** | Data model difference | **LOW** | Physical deletion vs. status flag semantics not verified. |
| TC-135946 | GCMA E2E workflow | Superseded / Partial | Architecture evolution | None | TCPA API integration covered; UI steps belong to GCMA team. |
| TC-135947 | ARM E2E workflow | Superseded / Partial | Architecture evolution | None | TCPA API integration covered; UI steps belong to ARM team. |

---

## 9. Recommended New Test Cases

All 10 new test cases are recommended to close the identified gaps. Items TC-NEW-001 through TC-NEW-003 are CRITICAL — they require a product decision before they can be written.

---

### TC-NEW-001: Message containing PURL exception keyword delivered to opted-out customer
**Closes:** TC-135564 | **Priority:** CRITICAL — requires product decision first

> **Given:** Customer phone number X is on the TCPA opt-out list
> **And:** The system has a configured exception keyword matching the PURL pattern
> **When:** A message containing the PURL exception keyword is submitted to the TCPA API
> **Then:** The message is delivered to phone number X despite opt-out status
> **And:** The delivery audit log entry records the exception override reason (e.g., "PURL exception")
> **And:** The customer's opt-out status in the opt-out store is unchanged

---

### TC-NEW-002: Message NOT containing exception keyword is still blocked for opted-out customer (control case)
**Closes:** TC-135564 | **Priority:** CRITICAL — requires product decision first

> **Given:** Customer phone number X is on the TCPA opt-out list
> **When:** A message NOT containing any configured exception keyword is submitted
> **Then:** The message is blocked
> **And:** The audit log records opt-out suppression with no exception override

---

### TC-NEW-003: Message containing emergency / SONP exception keyword delivered to opted-out customer
**Closes:** TC-135642 | **Priority:** CRITICAL — requires product decision first

> Same structure as TC-NEW-001 using the SONP / emergency exception keyword (e.g., "Emergency")
> **And:** Verify the audit log distinguishes SONP override from PURL override where applicable

---

### TC-NEW-004: TCPA filtering disabled via kill switch — opted-out customer receives message
**Closes:** TC-135644 | **Priority:** HIGH — requires product decision first

> **Given:** System kill switch is set to disabled (TCPAEnabled = False or equivalent)
> **And:** Customer phone number X is on the TCPA opt-out list
> **When:** A message is submitted to the TCPA API for phone number X
> **Then:** The message is delivered to phone number X
> **And:** The audit log records that filtering was bypassed due to kill switch
> **And:** Customer's opt-out status is unchanged

---

### TC-NEW-005: TCPA filtering re-enabled via kill switch — opted-out customer blocked again
**Closes:** TC-135644 | **Priority:** HIGH — requires product decision first

> **Given:** Kill switch is re-enabled (TCPAEnabled = True)
> **When:** A message is submitted for opted-out phone number X
> **Then:** The message is blocked and opt-out enforcement is correctly applied

---

### TC-NEW-006: HELP reply from opted-in customer forwarded to application callback
**Closes:** TC-135518 | **Priority:** MEDIUM

> **Given:** Customer phone number X has NOT opted out
> **When:** Customer sends "HELP" reply via inbound webhook
> **Then:** System does NOT modify the customer's opt-out status
> **And:** System forwards the HELP reply to the originating application's callback endpoint
> **And:** Audit/log entry records a HELP event (not an opt-out event)

---

### TC-NEW-007: HELP reply from opted-out customer forwarded to application callback
**Closes:** TC-135518 | **Priority:** MEDIUM

> **Given:** Customer phone number X IS on the opt-out list
> **When:** Customer sends "HELP" reply via inbound webhook
> **Then:** System does NOT modify the customer's opt-out status (no re-opt-in)
> **And:** System forwards the HELP reply to the originating application's callback
> **And:** Audit/log entry records a HELP event with customer's opted-out status noted

---

### TC-NEW-008: In-app work-order opt-out does NOT write to TCPA opt-out list
**Closes:** TC-135519 | **Priority:** MEDIUM — requires GCMA/ARM integration team coordination

> **Given:** Customer phone number X has not opted out of TCPA (TCPA status = OPT_IN)
> **When:** The GCMA or ARM application performs a work-order-level opt-out for phone number X via its own in-app mechanism
> **Then:** No record for phone number X is created or modified in the TCPA opt-out store
> **And:** Customer's TCPA opt-out status remains OPT_IN
> **And:** A subsequent TCPA API call for phone number X returns FORWARDED (not SUPPRESSED)

---

### TC-NEW-009: CoolTexting API call timeout returns 408 response
**Closes:** TC-135523 | **Priority:** LOW

> **Given:** The CoolTexting API is configured to time out after N milliseconds
> **When:** A message is submitted and the CoolTexting API does not respond within N milliseconds
> **Then:** The TCPA API returns HTTP 408 to the caller
> **And:** The error is logged with the timeout duration and target endpoint
> **And:** The retry mechanism is triggered per the configured retry policy

---

### TC-NEW-010: Re-opted-in customer absent from opted-out compliance report
**Closes:** TC-135645 | **Priority:** LOW — verify data model first

> **Given:** Customer phone number X is on the opt-out list
> **When:** Re-opt-in is performed via `PUT /admin/v1/opt-out/re-opt-in`
> **Then:** Customer phone number X does NOT appear in the opted-out compliance report
> **And:** Customer phone number X DOES appear in the opted-in compliance report
> **And:** All subsequent TCPA API calls for phone number X return FORWARDED

---

## 10. Recommended New Test Cases — Summary

| Test Case | Closes | Priority | Precondition |
|---|---|---|---|
| TC-NEW-001 | TC-135564 (PURL exception — positive) | **CRITICAL** | Confirm exception mechanism exists in new system |
| TC-NEW-002 | TC-135564 (PURL — control case) | **CRITICAL** | Confirm exception mechanism exists |
| TC-NEW-003 | TC-135642 (SONP/Emergency exception) | **CRITICAL** | Confirm exception mechanism exists |
| TC-NEW-004 | TC-135644 (kill switch disabled) | **HIGH** | Confirm kill switch mechanism exists |
| TC-NEW-005 | TC-135644 (kill switch re-enabled) | **HIGH** | Confirm kill switch mechanism exists |
| TC-NEW-006 | TC-135518 (HELP opted-in, forwarded) | MEDIUM | None |
| TC-NEW-007 | TC-135518 (HELP opted-out, still forwarded) | MEDIUM | None |
| TC-NEW-008 | TC-135519 (app opt-out boundary) | MEDIUM | GCMA/ARM integration team coordination |
| TC-NEW-009 | TC-135523 (408 timeout) | LOW | None |
| TC-NEW-010 | TC-135645 (re-opt-in data model) | LOW | Verify new system data model first |

---

*Items marked CRITICAL (TC-NEW-001 through TC-NEW-003) require a product decision before test cases can be written. If the exception override mechanism does not exist in the new system, that is a functional regression that must be escalated to Legal/Compliance before the go-live gate can be cleared.*

*This document should be reviewed and signed off by the QA Lead and Compliance Officer before the test plan is submitted for go-live gate approval.*
