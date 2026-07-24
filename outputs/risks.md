<!-- SDLC Pipeline Artifact
     Stage: 05-risk-assessment
     Source PRD: inputs/prd.md
     PRD Sections: All
     Generated: 2026-07-23
     Status: APPROVED
-->

# Risk Assessment — TCPA Regulatory Compliance API

## Summary

| Category    | Critical | High | Medium | Low | Total |
|-------------|----------|------|--------|-----|-------|
| Delivery    | 1        | 2    | 1      | 0   | 4     |
| Security    | 0        | 1    | 3      | 0   | 4     |
| Operational | 1        | 1    | 0      | 1   | 3     |
| Compliance  | 1        | 0    | 1      | 0   | 2     |
| Tech Debt   | 0        | 0    | 1      | 1   | 2     |
| **Total**   | **3**    | **4**| **6**  | **2**| **15**|

**Recommendation:** GO WITH CONDITIONS — three design changes required before dev starts (RISK-001, RISK-002, RISK-005). All Critical/High risks have accepted mitigations. Six-month timeline is tight but feasible provided gas application integration kickoff happens in week 1.

---

## Critical & High Risks

### RISK-001: Legal wording for opt-out confirmation not approved
- **Category:** Compliance
- **Source:** PD-004 (pending), REQ-008, SPEC-004, ARCH-RISK-005
- **Description:** The global opt-out confirmation message body is pending Legal/Compliance sign-off. The architecture is configuration-driven (no code change needed to update wording), but if Legal approval does not arrive before go-live, the system cannot send a compliant TCPA confirmation — a direct regulatory violation.
- **Likelihood:** High
- **Impact:** Critical
- **Mitigation:**
  - Type: Design Change (process) + Go-Live Gate
  - Action: Assign a Legal review owner and set a formal deadline of 8 weeks before go-live (allowing UAT with real wording). Track as a non-negotiable go-live blocker in the project plan. Placeholder wording is in SPEC-016 for development purposes only.
  - Owner: Project Manager / Legal liaison
- **Status:** Accepted — mitigation accepted in Morgan session with Mark Ellebie (2026-07-23)

### RISK-002: Full TCPA.Api outage blocks all in-scope Gas application SMS
- **Category:** Operational
- **Source:** SPEC-006, CQ-004, ARCH-RISK-001, ADR-005
- **Description:** The fail-safe architecture (Option A) makes TCPA.Api a synchronous hard dependency for all four Gas applications. If both IIS nodes fail simultaneously, all in-scope applications cannot send any SMS until the API recovers. Duration of outage directly maps to customer SMS service disruption and potential compliance exposure if opt-out confirmations are delayed.
- **Likelihood:** Low
- **Impact:** Critical
- **Mitigation:**
  - Type: Design Change (already in architecture) + Implementation Guardrail
  - Action: IIS NLB with health-check-based failover (ADR-001, ADR-005 — already designed). Add monitoring alert on /api/v1/health endpoint with on-call escalation. Maintain a recovery runbook targeting RTO < 15 minutes. Load balancer must remove unhealthy node from rotation within 30 seconds of health check failure.
  - Owner: IT/DevOps
- **Status:** Accepted — mitigation accepted in Morgan session with Mark Ellebie (2026-07-23)

### RISK-003: Gas application integration dependency outside TCPA team's control
- **Category:** Delivery
- **Source:** DEP-003 through DEP-006, CON-001
- **Description:** Four separate Gas applications (BizTalk, GCMA, KMI, ARM) must each change their SMS routing from Cool Text direct to TCPA API. Each is owned by a different team with its own change management process. Any one application missing the integration deadline leaves a TCPA compliance gap for that application's SMS traffic — regulatory exposure persists until integration is complete.
- **Likelihood:** Medium
- **Impact:** High
- **Mitigation:**
  - Type: Implementation Guardrail (project management)
  - Action: Integration kickoff with all four application teams within the first two weeks of the project. Deliver OpenAPI spec and a test environment immediately. Track each application's integration status (design complete, dev complete, UAT complete, prod complete) as explicit project milestones. Escalate any application team slippage to leadership within one week.
  - Owner: Project Manager + TCPA API Tech Lead
- **Status:** Accepted — mitigation accepted in Morgan session with Mark Ellebie (2026-07-23)

### RISK-004: Kafka consumer lag causes confirmation SLA breach under burst load
- **Category:** Operational
- **Source:** NFR-001, NFS-001, ARCH-RISK-002, SPEC-004
- **Description:** At peak load (5,000 messages/hour), if TCPA.MessageProcessor falls behind processing the `inbound-messages` Kafka topic, STOP confirmation messages will be delayed beyond the 60-second P99 SLA. TCPA requires confirmation within a reasonable timeframe; sustained SLA breaches represent regulatory exposure.
- **Likelihood:** Medium
- **Impact:** High
- **Mitigation:**
  - Type: Implementation Guardrail + Go-Live Gate
  - Action: Instrument consumer lag on `inbound-messages` topic. Alert when lag exceeds 10 messages. TCPA.MessageProcessor must be horizontally scalable (multiple Windows Service instances in same Kafka consumer group). Load test at 120% of peak (6,000/hour) for 30 minutes as a go-live gate — P99 confirmation latency must remain ≤ 60 seconds throughout.
  - Owner: Dev team + IT/DevOps
- **Status:** Accepted — mitigation accepted in Morgan session with Mark Ellebie (2026-07-23)

### RISK-005: No API key rotation or revocation mechanism defined
- **Category:** Security
- **Source:** NFR-006, SPEC-001, SPEC-006, SPEC-011, ADR-007
- **Description:** The architecture authenticates all callers via API keys managed by the existing auth service. No key rotation schedule or revocation mechanism is defined. A compromised key allows an attacker to: submit fake STOP events (silencing legitimate customers), flood the outbound endpoint, or call the admin re-opt-in API. Six-month timeline makes this a before-dev design decision.
- **Likelihood:** Low
- **Impact:** High
- **Mitigation:**
  - Type: Design Change (before dev)
  - Action: Define a key revocation capability in the auth service: ability to invalidate a specific key and issue a replacement without downtime. Define a key rotation policy (recommend: annual rotation minimum, immediate rotation on suspected compromise). Document the rotation runbook.
  - Owner: Auth service team + Security
- **Status:** Accepted — mitigation accepted in Morgan session with Mark Ellebie (2026-07-23)

### RISK-006: Cool Text/Twilio webhook registration is an external dependency
- **Category:** Delivery
- **Source:** SPEC-001, CQ-003, DEP-001, DEP-002
- **Description:** Inbound customer SMS reaches the TCPA API via a webhook registered with Cool Text / Twilio. This registration must be performed by the provider before go-live and requires TCPA API to be deployed at a stable HTTPS URL first. Provider response times and change processes are outside the team's control.
- **Likelihood:** Medium
- **Impact:** High
- **Mitigation:**
  - Type: Implementation Guardrail (project management)
  - Action: Initiate Cool Text / Twilio webhook registration request no later than 6 weeks before go-live. Confirm registration in a staging environment before production switch. Identify a named contact at Cool Text for escalation.
  - Owner: IT + Project Manager
- **Status:** Open

---

## Medium Risks

### RISK-007: Secrets management for Cool Text/Twilio credentials not specified
- **Category:** Security
- **Source:** DEP-001, DEP-002, SPEC-004, SPEC-008
- **Description:** TCPA.OutboundDispatcher calls Cool Text / Twilio using provider credentials. Architecture does not specify where these credentials are stored. Plain app settings or config files would expose them to anyone with server access.
- **Likelihood:** Medium | **Impact:** Medium
- **Mitigation:** Store Cool Text / Twilio credentials and SMTP relay credentials in an encrypted store (Windows DPAPI-protected config, or a secrets management solution). Never store in source control or plain text config. Define a rotation policy.
- **Status:** Open

### RISK-008: API key scope not differentiated between admin and standard callers
- **Category:** Security
- **Source:** SPEC-011, NFR-006
- **Description:** The admin re-opt-in endpoint (/api/v1/admin/reopt-in) and the standard outbound message endpoint use the same API key mechanism. If scope differentiation is not enforced at the auth service, a standard Gas application key could call the admin endpoint.
- **Likelihood:** Low | **Impact:** Medium
- **Mitigation:** Define two API key scopes at the auth service: standard (outbound submit, webhook) and admin (re-opt-in only). Enforce scope check in TCPA.Api before processing admin requests.
- **Status:** Open

### RISK-009: Debug log access controls not defined
- **Category:** Security
- **Source:** SPEC-017, BR-057
- **Description:** Debug logs contain unhashed phone numbers (PII). BR-057 specifies debug level is off by default in production, but access controls on debug log files are not defined. If debug mode is temporarily enabled for troubleshooting, phone number data is exposed to anyone with log file access.
- **Likelihood:** Low | **Impact:** Medium
- **Mitigation:** Define access controls on debug log directories (IT admin access only). Document a procedure for enabling debug logging (ticket required, time-limited, auto-disable after 24 hours).
- **Status:** Open

### RISK-010: Stale Cool Text account registry causes silent routing failures
- **Category:** Delivery
- **Source:** SPEC-015, ARCH-RISK-004
- **Description:** A Gas application onboarded after go-live without a corresponding entry in the CoolTextAccount table will receive 400 errors on all messages — silently from the application's perspective if error handling is not robust.
- **Likelihood:** Medium | **Impact:** Medium
- **Mitigation:** Onboarding checklist requires TCPA API account registration (database entry + test) before any new application goes live. TCPA.Api returns 400 with a descriptive error body for unregistered accounts (already in SPEC-001 BR-001).
- **Status:** Open

### RISK-011: Help Desk re-opt-in process bottleneck at scale
- **Category:** Compliance
- **Source:** REQ-007, SPEC-011, CON-005
- **Description:** Re-opt-in is a manual Help Desk process (by design, CON-005). If the volume of re-opt-in requests grows significantly, the Help Desk becomes a bottleneck and customers may wait an unacceptable time to resume receiving texts.
- **Likelihood:** Low | **Impact:** Medium
- **Mitigation:** Monitor re-opt-in request volume from day one. Define an SLA for Help Desk re-opt-in processing (recommend: 2 business days). If volume exceeds Help Desk capacity, escalate to product team to consider a customer self-service re-opt-in in Phase 2 (already OOS-002).
- **Status:** Open

### RISK-012: SQL Server read contention at peak load
- **Category:** Operational
- **Source:** NFS-005, ARCH-RISK-003, SPEC-007, SPEC-008
- **Description:** At peak burst (5,000 messages/hour), the dual opt-out check generates ~10,000 SQL reads/hour on the opt-out status table, concurrent with audit log writes. On a single SQL Server primary this could cause read/write contention.
- **Likelihood:** Low | **Impact:** Medium
- **Mitigation:** Index phone_number on OptOutStatus table. Route opt-out status reads (SPEC-007, SPEC-008) to a SQL Server read replica. Audit writes go to primary. Monitor query latency; P99 status lookup must remain < 100ms (NFS-005 budget).
- **Status:** Open

---

## Low Risks

| ID       | Title | Category | Mitigation Summary |
|----------|-------|----------|--------------------|
| RISK-013 | Race-condition edge case produces false audit event | Tech Debt | Accepted by design (CQ-007). Audit log distinguishes accepted edge cases. Monitor count weekly. |
| RISK-014 | Report email delivery failure not surfaced to recipients | Tech Debt | Log and alert on email bounce. Add delivery confirmation to compliance report checklist. |

---

## Accepted Risks

| ID       | Risk | Rationale for Acceptance | Accepted By |
|----------|------|--------------------------|-------------|
| RISK-001 | Legal wording not approved before go-live | Mitigation (go-live blocker + Legal owner) accepted. Configuration-driven design eliminates code risk. | Mark Ellebie, 2026-07-23 |
| RISK-002 | Full TCPA.Api outage blocks Gas app SMS | Mitigation (IIS NLB + health monitoring + runbook) accepted. Low likelihood; RTO < 15 min target. | Mark Ellebie, 2026-07-23 |
| RISK-003 | Gas app integration dependency | Mitigation (kickoff week 1, milestone tracking) accepted. Team aware of external dependency risk. | Mark Ellebie, 2026-07-23 |
| RISK-004 | Kafka consumer lag → SLA breach | Mitigation (consumer lag alerting + load test gate) accepted. Addressable in dev phase. | Mark Ellebie, 2026-07-23 |
| RISK-005 | No API key rotation/revocation | Mitigation (auth service key revocation design change before dev) accepted. | Mark Ellebie, 2026-07-23 |

---

## Security Checklist
- [x] Authentication mechanism defined in architecture (API key via existing auth service)
- [ ] Authorization model — API key scoping (admin vs. standard) not yet enforced — see RISK-008
- [x] All PII identified — phone numbers in opt-out store and audit log; handling rules in BR-057
- [x] API inputs validated at boundary — SPEC-001 and SPEC-006 define validation rules
- [ ] Secrets management — Cool Text/Twilio and SMTP credentials storage not specified — see RISK-007
- [x] Dependency vulnerability scanning — covered by Southern standard security process (A3)
- [x] HTTPS enforced on all endpoints and integrations
- [x] Security logging and audit trail defined — SPEC-010, SPEC-017

---

## Pre-Development Required Actions
1. **RISK-005** — Define API key revocation capability with auth service team before development begins
2. **RISK-001** — Assign Legal review owner and set wording approval deadline (8 weeks before go-live)
3. **RISK-003** — Schedule Gas application integration kickoff within 2 weeks of project start
4. **RISK-006** — Initiate Cool Text / Twilio webhook registration no later than 6 weeks before go-live
