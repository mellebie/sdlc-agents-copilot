<!-- SDLC Pipeline Artifact
     Stage: 09c-test-plan-agent (supplemental)
     Source PRD: inputs/prd.md
     Generated: 2026-06-26
     Status: DRAFT — REQUIRES PRODUCT OWNER AND COMPLIANCE REVIEW
-->

# Test Coverage Gap Summary — TCPA Compliance Engine

**Reference:** `Copy of BizTalk-TCPA-Test Case-20260609.xls` (22 unique TCs)
**Generated plan:** `tests/TCPA-Test-Cases.csv` / `tests/TCPA-Test-Plan.xlsx` (150 TCs)
**Verification method:** Direct CSV keyword search against every reference TC behavior
**Full analysis:** `outputs/test-plan-gap-analysis.md`

---

## Verdict

| Status | Count | Reference TCs |
|---|---|---|
| Covered (equivalent or superseded) | 15 | TC-135106, TC-135145, TC-135146, TC-135508, TC-135509, TC-135517, TC-135520, TC-135521, TC-135522, TC-135560, TC-135561, TC-135563, TC-135643, TC-135946, TC-135947 |
| Partial — sub-scenario missing | 2 | TC-135518, TC-135645 |
| Not covered — zero matching TCs | 4 | TC-135519, TC-135523, TC-135564/TC-135642, TC-135644 |
| Duplicate (excluded) | 1 | TC-135562 |

---

## NOT COVERED — Zero Matching Test Cases

### TC-135519 — App Work-Order Opt-Out vs. TCPA Global Opt-Out
**Risk:** MEDIUM

The reference tests that two independent opt-out mechanisms exist:
- **In-app work-order opt-out** (GCMA / ARM UI): stops messages for one specific work order only. Does **not** write to the TCPA global opt-out list. Customer's TCPA status is unchanged.
- **TCPA keyword opt-out** (customer replies STOP / QUIT / etc.): writes to TCPA global opt-out list; blocks all future TCPA-regulated messages from all apps.

**Reason for gap:** The BRD and PRD describe the TCPA Compliance Engine in isolation. They document how keyword opt-outs are captured and enforced but make no mention of the in-app work-order opt-out features built into GCMA and ARM. Because the BRD never described the integration boundary between the GCMA/ARM work-order UI and the TCPA store, no functional requirement (REQ-xxx) was ever written for this boundary, no spec (SPEC-xxx) defined its expected behavior, and no story AC stated what must NOT happen when a work-order opt-out is performed. Agent 09c derives test cases strictly from documented pipeline artifacts — a behavior with no REQ, SPEC, or AC cannot generate a test case. This is an upstream documentation gap in the BRD: it should have explicitly stated that GCMA/ARM in-app opt-out is scoped to the individual work order and must not write to the TCPA global opt-out list. Without that statement, the new system's TCPA compliance boundary with the calling applications was never formally specified.

**What is missing:** No generated test case verifies that performing a work-order opt-out inside GCMA or ARM does **not** create or modify a record in the TCPA opt-out store. If this boundary is broken, customer TCPA consent records can be silently altered by a UI action that was never intended to affect global SMS consent.

**Recommended test case (TC-NEW-008):**
> Given customer +1XXXXXXXXXX has TCPA status = OPT_IN
> When GCMA or ARM performs a work-order-level opt-out for that customer
> Then the customer's TCPA opt-out status remains OPT_IN
> And no record is created in the TCPA opt-out store
> And a subsequent POST /api/v1/sms/outbound for that number returns status=FORWARDED

**Owner:** Requires coordination with GCMA/ARM integration team to define the test boundary.

---

### TC-135523 — CoolTexting API Timeout — HTTP 408 Response
**Risk:** LOW

The reference tests that when the CoolTexting API does not respond within the configured timeout window, the TCPA system returns HTTP **408 Request Timeout** to the caller.

**Reason for gap:** The pipeline artifacts define the fail-closed behavior for database unavailability (NFS-005: HTTP 503 when DB is unreachable) and document CoolText retry logic, but no NFR, spec, or story AC explicitly required the HTTP 408 response code as the specific status returned when the CoolTexting delivery API times out. The architecture (architecture.md) documents the CoolText integration point but does not specify the HTTP status code mapping for timeout conditions. Because the required response code was never stated in any artifact, Agent 09c had no source item from which to derive a timeout test case. The 503/retry scenarios were generated because they appeared explicitly in the specs; the 408 scenario was not because it did not. This is a gap in the NFRs: the integration error response contract for timeout specifically should have been captured as a measurable requirement.

**What is missing:** The generated plan tests HTTP 503 (database unavailable) and retry exhaustion, but contains no test case for the timeout scenario as a distinct HTTP 408 response code. If the new system maps timeouts to 503 or 504 instead of 408, callers that programmatically branch on status code will behave incorrectly.

**Recommended test case (TC-NEW-009):**
> Given the CoolTexting API is configured to simulate a timeout
> When POST /api/v1/sms/outbound is called with a valid payload
> Then the TCPA API returns HTTP 408 to the caller
> And the error is logged with timeout duration and target endpoint
> And the configured retry policy is triggered

---

### TC-135564 + TC-135642 — Exception Override: PURL / SONP / Emergency Messages
**Risk:** HIGH — Compliance and Legal Exposure

The reference documents an exception override mechanism (`dbo.OptOutExceptions` table, specification section 7.2 "Parsing the Message for exceptions"). When a message body contains a configured exception word, the TCPA opt-out check is bypassed and the message is delivered to the opted-out customer. The `Reason` column in `TCPALogs` records the exception that triggered the override.

Two named exception scenarios covered by these TCs:
- **TC-135564 (PURL):** Payment URL messages customers are legally entitled to receive regardless of SMS opt-out status.
- **TC-135642 (SONP / Emergency):** Safety-critical notifications (gas leak, outage restoration, emergency response) where non-delivery creates safety or regulatory liability.

**Reason for gap:** The exception override mechanism (`dbo.OptOutExceptions`, PURL, SONP) exists in the legacy BizTalk system and was documented in the legacy specification (section 7.2). It was **not carried forward** into any of the new system's pipeline artifacts — it does not appear in the BRD, PRD, requirements.md, specs.md, stories.md, risks.md, or architecture.md. Because Agent 09c reads only the pipeline artifacts to derive test cases, a mechanism that was never documented in those artifacts produces no test cases. This is the most consequential gap in the pipeline: a business-critical feature (exception delivery for PURL payments and SONP emergency notifications) that existed in the legacy system was either intentionally removed from the new design without recorded justification, or was inadvertently omitted from the BRD during requirements elicitation. Either way, the gap is invisible to the SDLC pipeline because no artifact mentions it — and therefore no test was generated to detect its absence or verify its behavior.

**What is missing:** The 150 generated test cases contain **no test** that:
- Verifies an exception-keyword message is delivered to an opted-out customer
- Verifies the override reason is recorded in the audit log
- Verifies a non-exception message is still blocked for opted-out customers (control case)
- Verifies the exception configuration store is consulted during the outbound filtering decision

**This gap requires a product decision before test cases can be written.** The exception override mechanism may have been: (a) intentionally removed from the new system's design, (b) retained but not documented in the pipeline artifacts, or (c) inadvertently omitted.

**Required action before go-live:**
1. Confirm with Product Owner and Legal/Compliance whether the exception override mechanism exists in the new system.
2. **If retained:** add TC-NEW-001 through TC-NEW-003 (see below).
3. **If intentionally removed:** obtain documented sign-off from Legal/Compliance confirming an alternative mechanism (e.g., message-type allowlisting at the caller level) covers the PURL and SONP scenarios. Record the decision in the SDLC artifacts.
4. **If inadvertently omitted:** raise a functional regression defect before test execution begins.

**Recommended test cases (require product decision first):**

> **TC-NEW-001:** Given customer X is opted out AND message body contains configured PURL exception keyword, When POST /api/v1/sms/outbound is called, Then message is delivered AND audit log records exception override reason AND customer opt-out status is unchanged.

> **TC-NEW-002:** Given customer X is opted out AND message body does NOT contain any exception keyword, When POST /api/v1/sms/outbound is called, Then message is suppressed AND audit log records OPT_OUT suppression with no exception override. *(Control case.)*

> **TC-NEW-003:** Same as TC-NEW-001 using the SONP / Emergency exception keyword.

---

### TC-135644 — TCPAEnabled Feature Flag / Kill Switch
**Risk:** HIGH (Operational) / MEDIUM-HIGH (Compliance)

The reference tests a `dbo.Configuration` table with `KeyName="TCPAEnabled"` / `KeyValue="True|False"`. When set to `False`, TCPA filtering is disabled system-wide — all messages are delivered regardless of opt-out status. This is an emergency kill switch: if TCPA filtering malfunctions and incorrectly blocks legitimate messages at scale, an operator can disable filtering while a fix is deployed.

**Reason for gap:** The `TCPAEnabled` kill switch is a legacy operational control stored in `dbo.Configuration`. Like the exception override mechanism, it was **never documented in any pipeline artifact** for the new system — it does not appear in the BRD, PRD, NFRs, requirements.md, specs.md, architecture.md, or risks.md. The new system's architecture uses Azure App Configuration for feature flags (tested by TCPA-TC-148), but that test covers generic configuration cache invalidation, not a system-wide kill switch that bypasses opt-out enforcement. Because no pipeline artifact specifies that a kill switch must exist, Agent 09c generated no test cases for it. This gap likely reflects a decision made during the migration from BizTalk to the new .NET 8 platform: the operational kill switch was either intentionally redesigned (e.g., to require a redeployment rather than a database toggle), or it was overlooked entirely. Without a documented kill switch in the new architecture, there is no incident response procedure if TCPA filtering itself becomes the failure mode.

**What is missing:** The generated plan's TCPA-TC-148 tests Azure App Configuration cache invalidation (a general feature flag polling mechanism), but tests no scenario where a system-wide TCPA kill switch changes the filtering behavior from block → pass. These are different behaviors.

**Required action before go-live:**
1. Confirm whether the new system exposes an equivalent kill switch (environment variable, Azure App Configuration flag, Azure Feature Management toggle, or equivalent).
2. **If retained:** add TC-NEW-004 and TC-NEW-005.
3. **If intentionally removed:** document the incident response procedure for TCPA filtering failures in `outputs/docs/operations.md`. Obtain Compliance sign-off that emergency disabling of TCPA filtering is handled through an alternative procedure.

**Recommended test cases (require product decision first):**

> **TC-NEW-004:** Given TCPAEnabled = False AND customer X is opted out, When POST /api/v1/sms/outbound is called, Then message is delivered AND audit log records filtering bypassed due to kill switch AND customer opt-out status is unchanged.

> **TC-NEW-005:** Given kill switch re-enabled (TCPAEnabled = True), When POST /api/v1/sms/outbound is called for opted-out customer X, Then message is suppressed and normal enforcement applies.

---

## PARTIAL — Sub-Scenario Missing

### TC-135518 — HELP Keyword Passthrough to Application Callback
**Risk:** MEDIUM

**What is covered:** The generated plan confirms "HELP" does not trigger opt-out (word-boundary logic tests confirm non-opt-out keywords are not matched).

**Reason for gap:** The pipeline artifacts document inbound SMS routing at the keyword-detection level: spec SPEC-002 and STORY-003 define that opt-out keywords (STOP/QUIT/END/REVOKE/OPT-OUT/CANCEL/UNSUBSCRIBE) trigger opt-out logic, and that non-matching inbound messages are forwarded to the originating application's callback URL. However, no story or acceptance criterion explicitly names HELP as a keyword category requiring its own dedicated test coverage. The specs focus entirely on the 7 opt-out keywords — HELP appears only in the boundary-value edge case list ("HELP does not match opt-out keywords") rather than as a first-class routing case with a defined forwarding behavior. Agent 09c generated a test proving HELP does not cause an opt-out (because that AC existed), but generated no test proving HELP is affirmatively routed to the callback (because no AC required that). This is a spec granularity gap: the inbound routing spec should have enumerated HELP, STOP, and unknown messages as three named cases, each with a defined expected outcome.

**What is missing:** The reference tests that a "HELP" reply is actively **forwarded to the originating application's callback URL** — a distinct affirmative routing action, not merely a non-match. There is no generated test case verifying this forwarding occurs, nor that it occurs correctly when the customer is opted out.

**Recommended test cases:**

> **TC-NEW-006:** Given customer X is OPT_IN AND sends "HELP" reply via inbound webhook, When POST /api/v1/sms/inbound is processed, Then opt-out status is unchanged AND callback URL is called with the HELP reply AND log records a HELP event (not an opt-out event).

> **TC-NEW-007:** Given customer X is OPT_OUT AND sends "HELP" reply, When POST /api/v1/sms/inbound is processed, Then opt-out status is unchanged (no re-opt-in) AND callback URL is still called AND log records a HELP event with opted-out status noted.

---

### TC-135645 — OptOutChangeLogs Row Deletion Semantics on Re-Opt-In
**Risk:** LOW

**What is covered:** Re-opt-in updates the customer's status and creates an audit log entry.

**Reason for gap:** The legacy BizTalk system physically deletes the customer's row from `dbo.OptOutList` when they re-opt-in (`OptOutChange="Deleted"`), and the reference compliance reports filter opted-out customers by row presence — if no row exists, the customer is considered opted in. The new system almost certainly uses a status-flag approach (a column on the record rather than row deletion), which is architecturally superior but means the compliance report query must filter on the status column rather than row existence. This design detail — how the data model represents the absence of opt-out status — is not documented in any pipeline artifact. The architecture.md data model section defines entity shapes but does not specify whether re-opt-in is implemented as a soft delete (status flag) or hard delete (row removal). Because no spec or AC described this data model choice, Agent 09c had no source item from which to generate a test verifying that re-opted-in customers are correctly excluded from compliance reports. The tested behavior (re-opt-in changes status and creates audit log) was covered; the downstream effect on report queries was not, because that effect depends on an undocumented implementation decision.

**What is missing:** The legacy system physically deletes the customer's row from `dbo.OptOutList` on re-opt-in (`OptOutChange="Deleted"`). The new system likely uses a status flag instead. No generated test case verifies that a re-opted-in customer is correctly excluded from the opted-out compliance report — which depends on whether the report query filters on row presence or on a status column.

**Recommended test case:**

> **TC-NEW-010:** Given customer X is on the opt-out list, When PUT /admin/v1/opt-out/re-opt-in is called, Then customer X does NOT appear in GET /api/v1/reports/opted-out AND customer X DOES appear in GET /api/v1/reports/opted-in AND all subsequent outbound calls for customer X return status=FORWARDED.

---

## Summary of Recommended New Test Cases

| TC | Closes | Risk | Requires Product Decision? |
|---|---|---|---|
| TC-NEW-001 | TC-135564 (PURL exception — positive) | HIGH | Yes — confirm mechanism exists |
| TC-NEW-002 | TC-135564 (control case — still blocks) | HIGH | Yes — confirm mechanism exists |
| TC-NEW-003 | TC-135642 (SONP/Emergency exception) | HIGH | Yes — confirm mechanism exists |
| TC-NEW-004 | TC-135644 (kill switch disabled) | HIGH | Yes — confirm mechanism exists |
| TC-NEW-005 | TC-135644 (kill switch re-enabled) | HIGH | Yes — confirm mechanism exists |
| TC-NEW-006 | TC-135518 (HELP opted-in, forwarded) | MEDIUM | No |
| TC-NEW-007 | TC-135518 (HELP opted-out, still forwarded) | MEDIUM | No |
| TC-NEW-008 | TC-135519 (work-order opt-out boundary) | MEDIUM | No — needs GCMA/ARM team |
| TC-NEW-009 | TC-135523 (408 timeout) | LOW | No |
| TC-NEW-010 | TC-135645 (re-opt-in report query) | LOW | No |

**TC-NEW-006 through TC-NEW-010 can be added to `tests/TCPA-Test-Cases.csv` immediately and the Excel regenerated.** TC-NEW-001 through TC-NEW-005 must wait for the product owner and Legal/Compliance team to confirm whether the underlying features exist in the new system.
