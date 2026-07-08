<!-- SDLC Pipeline Artifact
     Stage: 05-risk-assessment
     Source PRD: inputs/prd.md
     PRD Sections: §1 Overview, §2 Personas, §3 Functional Requirements, §4 Non-Functional Requirements, §5 Constraints, §6 Out of Scope, §7 Success Metrics, §8 Assumptions, §9 Dependencies
     Generated: 2026-06-26
     Status: APPROVED — human approved proceeding despite open clarifications (2026-07-07)
-->

# Risk Assessment — TCPA Regulatory Compliance for Text Messages

## Summary

| Category    | Critical | High | Medium | Low | Total |
|-------------|----------|------|--------|-----|-------|
| Delivery    | 1        | 3    | 2      | 1   | 7     |
| Security    | 0        | 2    | 2      | 1   | 5     |
| Operational | 0        | 2    | 2      | 1   | 5     |
| Compliance  | 1        | 2    | 1      | 0   | 4     |
| Tech Debt   | 0        | 0    | 2      | 2   | 4     |
| **Total**   | **2**    | **9**| **9**  | **5**| **25**|

**Recommendation:** GO WITH CONDITIONS — The architecture is sound and the compliance requirements are well-understood. However, two Critical and four High risks must be actively managed before development begins. The BizTalk adapter dependency (RISK-001) and the CCB go-live timing (RISK-006) are on the critical path and require immediate coordination. Legal approval of the confirmation SMS text (RISK-007) must be unblocked before Sprint 1 ends. These are not showstoppers — they are known risks with clear mitigations — but they require parallel workstreams from Day 1.

---

## Critical & High Risks

### RISK-001: BizTalk REST Adapter — External Dependency on the Compliance Deadline Critical Path
- **Category:** Delivery
- **Source:** ARCH-RISK-001, SPEC-001 [COMPLEX flag], SPEC-A-001
- **Description:** BizTalk is a SOAP/ESB-native integration platform. The TCPA API exposes REST/JSON exclusively. A custom adapter must be built by the BizTalk team to translate BizTalk outbound SMS calls into REST before the TCPA API can enforce compliance on that message stream. This adapter is not owned by the TCPA API team. If the BizTalk team cannot deliver a working adapter by the integration testing window (Q3 2026), BizTalk-originated SMS messages will reach Cool Text/Twilio unprotected — which is a TCPA compliance violation.
- **Likelihood:** High
- **Impact:** Critical
- **Mitigation:**
  - Type: Design Change (if HMAC/REST not feasible) + Implementation Guardrail
  - Action 1: Confirm BizTalk REST capability with the BizTalk team in Week 1 of development. Obtain a written commitment with a delivery date for the adapter.
  - Action 2: If BizTalk cannot call REST, evaluate adding a SOAP endpoint to the TCPA API as a fallback input channel (this is an architectural scope addition — requires human approval). Alternatively, evaluate whether Cool Text routing for BizTalk can bypass BizTalk entirely (routing at the Cool Text account level).
  - Action 3: Reserve an integration testing slot with the BizTalk team no later than Q3 2026, identified in the project plan as a hard dependency.
  - Owner: Architecture Lead (escalation owner); BizTalk Team (delivery owner)
- **Status:** Open

---

### RISK-002: Opt-Out Confirmation SMS Text Not Yet Legally Approved
- **Category:** Compliance
- **Source:** ARCH-RISK-007, SPEC-005, CQ-002 (open), SPEC-A-011
- **Description:** SPEC-005 requires a standardized opt-out confirmation SMS that informs the customer they are opted out of ALL SCG text communications and provides the re-opt-in phone number. The exact message text must be approved by Legal/Compliance before deployment. As of 2026-06-26, this approval is still open (CQ-002). Deploying with a placeholder text is a compliance risk: if the confirmation SMS does not meet TCPA regulatory language requirements, SCG could be liable for non-compliant opt-out acknowledgements even if the suppression itself worked correctly.
- **Likelihood:** High (the legal approval process typically introduces delays)
- **Impact:** High
- **Mitigation:**
  - Type: Implementation Guardrail + Design Change
  - Action 1: Escalate Legal approval as a blocking pre-go-live gate. Initiate the approval process immediately, providing Legal with a draft message for review. Target approval no later than 60 days before the go-live date.
  - Action 2: The message text is stored in Azure Key Vault configuration (not hardcoded), so it can be updated post-build without a code deployment. This is the correct implementation pattern — confirm it is followed.
  - Action 3: If Legal does not approve text before go-live, the system should be configured to emit a compliant interim message (coordinated with Legal) rather than a placeholder.
  - Owner: Legal/Compliance Team (approval owner); Product Owner (escalation owner)
- **Status:** Open

---

### RISK-003: CCB/My Account Go-Live Timing — Potential Unprotected SMS Window
- **Category:** Delivery / Compliance
- **Source:** ARCH-RISK-006, SPEC-014 (BR-063), ASM-004, CQ-009
- **Description:** CCB/My Account is estimated to go live Q2 2026 (TBD). If CCB goes live and begins sending SMS before its TCPA API integration is complete and tested, those SMS messages will reach Cool Text/Twilio without opt-out enforcement — a TCPA compliance violation. The Application Registry active flag (BR-063) is the mitigation, but it relies on coordination discipline: someone must enable the CCB active flag only after end-to-end integration testing is confirmed. If this coordination fails, opted-out customers could receive CCB SMS.
- **Likelihood:** Medium (coordination gaps are common at go-live boundaries)
- **Impact:** Critical
- **Mitigation:**
  - Type: Implementation Guardrail + Process Gate
  - Action 1: Define a formal "CCB TCPA Activation Gate" — a documented checklist that must be signed off before the CCB active flag is set to true in production. Include: end-to-end integration test pass, Cool Text account ID confirmed in registry, production smoke test, Legal/Compliance sign-off.
  - Action 2: Default CCB to active=false in the production Application Registry at deployment time. Make active=true a deliberate, documented action.
  - Action 3: Establish communication between the CCB go-live team and the TCPA API team so that CCB's production SMS traffic does not start until TCPA protection is confirmed.
  - Owner: Product Owner (gate coordination); CCB Team Lead; TCPA API Team
- **Status:** Open

---

### RISK-004: No Disaster Recovery RTO/RPO Defined for Opt-Out Status Database
- **Category:** Operational
- **Source:** ARCH-RISK-008, GAP-002, CQ-015 (unanswered)
- **Description:** The opt-out status database is the authoritative source of truth for whether a customer has opted out. If this database is lost or corrupted without a recent backup, the system cannot determine opt-out status — which forces a fail-closed state (503 to all SMS requests) or worse, if fail-closed is not implemented uniformly, may result in opted-out customers receiving messages. As of the architecture review, RTO/RPO targets have not been confirmed with IT and Legal. Azure SQL Point-in-Time Restore (PITR) defaults to 7-day retention, but whether 7 days and a 4-hour RTO are sufficient for a TCPA compliance system has not been validated.
- **Likelihood:** Low (database loss events are rare with managed PaaS)
- **Impact:** Critical
- **Mitigation:**
  - Type: Design Change (before dev starts)
  - Action 1: Define RPO and RTO with IT and Legal before database schema is finalized. Recommended minimum: RPO ≤ 1 hour (Azure SQL automatic backups every 5-12 minutes for Business Critical tier achieves this); RTO ≤ 4 hours.
  - Action 2: Enable geo-redundant Azure SQL backups (not just local backups) to protect against a full region failure.
  - Action 3: Document the DR runbook for opt-out database recovery, including the process for reconstructing missing records from the audit log (which is independently retained in WORM storage).
  - Owner: IT/Platform Engineering (infrastructure); Architecture Lead (requirements definition)
- **Status:** Open

---

### RISK-005: Cool Text Webhook HMAC Support Unconfirmed
- **Category:** Security
- **Source:** ARCH-RISK-004, ADR-007
- **Description:** The inbound webhook security model (ADR-007) requires HMAC-SHA256 signature validation on all Cool Text webhook payloads. This is the mechanism that prevents an attacker from injecting fake opt-out keywords into the TCPA API (e.g., to suppress legitimate SMS delivery to a targeted cell number). However, whether Cool Text actually supports HMAC payload signing has not been confirmed with the vendor. If Cool Text does not support HMAC, the fallback is a secret URL token — which provides significantly weaker protection (token is visible in server logs, HTTP access logs, and firewall logs).
- **Likelihood:** Medium
- **Impact:** High
- **Mitigation:**
  - Type: Design Change (before inbound webhook implementation begins)
  - Action 1: Contact Cool Text vendor in Week 1 of development to confirm webhook signing mechanism. Request the exact signing algorithm, header name, and payload signing method.
  - Action 2: If HMAC is not available, fall back to: (a) a secret token in a custom header (not in URL path), (b) IP allowlisting for Cool Text webhook origin IPs (defense-in-depth), and (c) rate limiting on the inbound webhook endpoint to limit injection blast radius.
  - Action 3: If neither HMAC nor a secret header token is available, do not deploy the inbound webhook endpoint without WAF rules (Application Gateway WAF) that restrict the endpoint to Cool Text IP ranges.
  - Owner: Architecture Lead (to confirm vendor capability); Integration Engineer
- **Status:** Open

---

### RISK-006: API Key Authentication — Long-Lived Credential Risk for Upstream Applications
- **Category:** Security
- **Source:** ADR-006
- **Description:** Per ADR-006, upstream applications authenticate to the TCPA API using per-application API keys in the `X-API-Key` header. API keys are long-lived credentials. If an API key is compromised (e.g., leaked in a log, committed to source control, or intercepted on the network), an attacker could submit arbitrary outbound SMS requests — including fabricating requests with spoofed cell numbers to check opt-out status (information disclosure), or submitting high volumes of requests to trigger the fail-closed 503 behavior (denial of service against the compliance gate). The architecture defers OAuth 2.0 to Phase 2.
- **Likelihood:** Medium
- **Impact:** High
- **Mitigation:**
  - Type: Implementation Guardrail
  - Action 1: Ensure all API keys are stored exclusively in Azure Key Vault. Never store keys in application configuration files, source control, or environment variables outside Key Vault.
  - Action 2: Implement rate limiting per API key (in-application middleware, as noted in ADR-002 consequence). Define a per-application per-minute threshold appropriate for expected SMS volume.
  - Action 3: Implement API key rotation procedures and document the rotation runbook before go-live. Target rotation frequency: every 90 days or immediately on suspected compromise.
  - Action 4: Log all API key authentication events (success and failure) to Azure Monitor. Alert on anomalous request volumes from a single API key.
  - Owner: IT Security (key management procedures); Development Team (rate limiting implementation)
- **Status:** Open

---

### RISK-007: BizTalk Adapter Scope Uncertainty — Integration Testing Window Insufficient
- **Category:** Delivery
- **Source:** ARCH-RISK-001 (delivery sub-risk), NFS-003
- **Description:** Even if the BizTalk adapter is technically feasible, end-to-end integration testing with all five in-scope applications (BizTalk, GCMA, KMI, ARM, CCB) requires coordination across multiple teams and a dedicated test environment. The architecture notes that integration testing must begin no later than Q3 2026. With a January 31, 2027 hard deadline, slippage in integration testing has no buffer — any application that misses its integration test window is unprotected at go-live. Each untested application represents a TCPA compliance gap.
- **Likelihood:** Medium
- **Impact:** High
- **Mitigation:**
  - Type: Implementation Guardrail
  - Action 1: Create an integration testing schedule with a dedicated slot per application team no later than Q3 2026. Treat each application's integration test as a separate delivery milestone tracked in the project plan.
  - Action 2: Provide each application team with a TCPA API integration guide (REST contract, API key issuance, test environment credentials) by the end of Sprint 1.
  - Action 3: Establish a test environment with a Cool Text sandbox account to allow application teams to test without risk to production opt-out data.
  - Owner: Project Manager (schedule); TCPA API Team Lead (integration guide and test environment)
- **Status:** Open

---

### RISK-008: Opt-Out Status Race Condition at Status Write / Outbound Check Boundary
- **Category:** Compliance
- **Source:** SPEC-006 (BR-027, BR-028, BR-029), NFS-002, architecture (read replica)
- **Description:** The architecture reads opt-out status from an Azure SQL read replica for outbound proxy lookups (NFS-002 fulfillment). Azure SQL active-passive replication introduces a non-zero lag (typically < 1 second). In the scenario where a customer sends a STOP message and an upstream application sends an outbound SMS to that same number in the milliseconds between the opt-out write (to the primary) and the replica sync, the outbound proxy could read a stale OPT-IN status from the replica and forward the message — a TCPA compliance violation.
- **Likelihood:** Low (replication lag is typically < 1 second; the window is narrow)
- **Impact:** High
- **Mitigation:**
  - Type: Design Change
  - Action 1: Direct the opt-out compliance gate read (SPEC-006 / SPEC-001 lookup) to the primary database, not the read replica. The performance cost is marginal (compliance gate is a single indexed point lookup; primary handle the load at expected utility-scale SMS volumes). Reserve the read replica for reporting queries only.
  - Action 2: Alternatively, if read replica is required for gate scalability, implement a cache-poisoning approach: on any opt-out status write, immediately invalidate any cached opt-in status for that cell number.
  - Owner: Architecture Lead (decision on primary vs. replica for compliance gate)
- **Status:** Open

---

### RISK-009: Weekly Compliance Report Scheduler — Single Point of Failure
- **Category:** Operational
- **Source:** SPEC-013 [COMPLEX flag], ADR-005, BR-060
- **Description:** The weekly compliance report (SPEC-013) uses an Azure Functions Timer Trigger. If the Azure Function fails to trigger (infrastructure issue, deployment failure, misconfiguration), a Monday report is silently missed unless the alerting is correctly configured. A missed compliance report is a visibility gap: the Compliance Officers will not have confirmation of opt-out enforcement for that week. Additionally, if the report detects zero compliance failures but the underlying data is incomplete (e.g., audit log projection lag), the report may give false assurance.
- **Likelihood:** Low
- **Impact:** Medium
- **Mitigation:**
  - Type: Implementation Guardrail
  - Action 1: Implement Azure Monitor alerts on the Azure Functions execution success/failure. Alert the IT on-call if the Monday 06:00 UTC report job does not complete successfully within 30 minutes of its scheduled trigger time.
  - Action 2: Make the report generator idempotent with a manual trigger capability (admin endpoint or Azure Portal re-run). Compliance Officers or IT should be able to manually re-run a missed report.
  - Action 3: Validate the audit log projection freshness before generating the report. If the reporting DB projection is more than 30 minutes stale, include a data-staleness warning in the report.
  - Owner: Development Team (alerting and manual trigger); IT/Platform Engineering (monitoring)
- **Status:** Open

---

### RISK-010: Audit Log Write Failure Does Not Roll Back Opt-Out Status
- **Category:** Compliance
- **Source:** SPEC-008 (BR-042), NFS-008, architecture (NFS-008 fulfillment)
- **Description:** By design (BR-042, NFS-008 fulfillment), if the audit log write fails, the opt-out status write is preserved and an alert is sent to IT — but the audit log record is not created. This is the correct compliance-first behavior (the opt-out takes effect regardless). However, the architecture notes that IT would need to manually reconstruct the missing audit record. If the failure is not detected promptly or the manual reconstruction is not completed, the audit trail will have a gap. In a regulatory discovery or litigation scenario, a gap in the audit log — even for a single opt-out event — could be used as evidence of incomplete compliance controls.
- **Likelihood:** Low (Azure SQL managed PaaS has high write reliability)
- **Impact:** Medium
- **Mitigation:**
  - Type: Implementation Guardrail
  - Action 1: Implement a dual-write pattern: on audit log write failure, write a fallback record to a secondary durable store (Azure Service Bus dead-letter or Azure Blob Storage) that can be used to reconstruct the audit log entry. This provides the raw data for manual reconstruction even if the primary audit log write failed.
  - Action 2: Page on-call IT immediately on any audit log write failure (not just log — active alert). Define a 4-hour SLA for manual audit log reconstruction after a failure event.
  - Action 3: Include an audit log completeness check in the weekly compliance report (count of opt-out events vs. count of audit log entries for the reporting period; flag any mismatch).
  - Owner: Development Team (dual-write fallback); IT/Platform Engineering (alert and reconstruction procedure)
- **Status:** Open

---

### RISK-011: Re-Opt-In Admin Endpoint — No UI Increases Mis-Use Risk
- **Category:** Security
- **Source:** SPEC-007 [COMPLEX flag], SPEC-A-008, CQ-003
- **Description:** The re-opt-in mechanism is a REST API endpoint with no UI in Phase 1. Help Desk agents will interact with it via direct API calls (likely using a tool like Postman or a curl script). The absence of a UI increases the risk of mis-use: an agent could re-opt-in a cell number that the customer did not actually request to re-opt-in, either due to data entry error or social engineering. There is no step-by-step workflow that enforces the correct procedure (look up status, confirm it is OPT-OUT, verify customer identity, then perform re-opt-in). Every re-opt-in is logged (SPEC-010), which provides auditability, but does not prevent incorrect re-opt-ins from occurring.
- **Likelihood:** Medium (Help Desk agents are generally process-following, but API-level tools have low friction for mis-use)
- **Impact:** Medium
- **Mitigation:**
  - Type: Implementation Guardrail + Post-Delivery
  - Action 1: Require the `reason` field to be non-empty and enforce a minimum length (e.g., 20 characters) to prevent agents from entering placeholder values like "fix" or "n/a".
  - Action 2: Include `ticket_reference` as required (not optional) for Phase 1 — a Help Desk ticket number creates an external audit trail that ties each re-opt-in to a customer interaction.
  - Action 3: Schedule a Phase 2 task to build a minimal Help Desk UI that enforces the correct re-opt-in workflow and reduces the risk of API mis-use.
  - Owner: Development Team (field validation); Help Desk Management (procedure); Product Owner (Phase 2 UI scope)
- **Status:** Open

---

## Medium Risks

### RISK-012: Always Encrypted Deterministic Constraints May Impact Compliance Reporting
- **Category:** Tech Debt
- **Source:** ARCH-RISK-003, ADR-003
- **Description:** Azure SQL Always Encrypted with deterministic encryption on `cell_number` does not allow server-side range queries or LIKE queries on the encrypted column. All queries that filter by cell number must pass the plaintext value from the application layer (which holds the encryption key). Compliance Officers who might want ad-hoc SQL access to the database (e.g., via SQL Server Management Studio or Azure Data Studio) cannot directly query by cell number without decryption capability in their client. This limits investigative querying during a compliance audit.
- **Likelihood:** Medium
- **Impact:** Medium
- **Mitigation:**
  - Type: Implementation Guardrail
  - Action 1: Ensure all cell-number-filtered queries for compliance reporting are exposed through the authenticated reporting API endpoints (SPEC-011, SPEC-012) — Compliance Officers should never need direct SQL access.
  - Action 2: Document the encryption constraint in the operations guide so that any IT engineer who opens a direct SQL session understands why cell_number columns appear as opaque encrypted values.
  - Action 3: If ad-hoc encrypted column lookup is required for incident investigation, provide an IT-only query helper script that decrypts via the application key.
- **Status:** Open

---

### RISK-013: Audit Log Tiering to Blob Storage — Query Access Path for Old Records
- **Category:** Operational
- **Source:** ADR-004, NFS-004
- **Description:** Audit log records older than 90 days are tiered to Azure Blob Storage WORM. The architecture notes that querying these records requires a "separate query path (Azure Data Factory export or external table bridge)." This path is not defined in detail. In a regulatory discovery scenario requiring audit records older than 90 days, IT would need to execute an ad-hoc data extraction process — which may be slow, undocumented, or require skills not present in the on-call team.
- **Likelihood:** Medium (regulatory discovery requests for records >90 days old are plausible)
- **Impact:** Medium
- **Mitigation:**
  - Type: Post-Delivery
  - Action 1: Define and document the cold storage query runbook before go-live. Specify the exact procedure for retrieving audit records from Azure Blob WORM storage, including the query tool, expected turnaround time, and authorization required.
  - Action 2: Consider maintaining a queryable "extended" reporting DB projection for the full 5-year retention window (not just 90 days), accepting the additional Azure SQL storage cost. At utility-scale SMS volumes, 5 years of audit records is unlikely to be cost-prohibitive.
- **Status:** Open

---

### RISK-014: SCG Identity Provider for Admin Endpoint Not Confirmed
- **Category:** Delivery
- **Source:** Architecture Open Question 3, SPEC-007
- **Description:** The Admin API (re-opt-in endpoint) authenticates via OAuth 2.0 / OIDC using the SCG Identity Provider. The specific IdP (assumed to be Azure Active Directory / Entra ID) and the required RBAC roles (`tcpa.helpdesk`, `tcpa.compliance_officer`) have not been confirmed with IT Security. If these roles do not exist and require provisioning, there is a lead time for role creation, user assignment, and security review. Starting this process late could delay Admin API testing.
- **Likelihood:** Medium
- **Impact:** Medium
- **Mitigation:**
  - Type: Implementation Guardrail
  - Action 1: Submit a request to IT Security in Week 1 of development to confirm: (a) the target IdP, (b) whether `tcpa.helpdesk` and `tcpa.compliance_officer` role claims need to be created, and (c) the process for assigning users to these roles.
  - Action 2: For development and testing, use a test Azure AD tenant or service principal with mock role claims so Admin API development is not blocked on production role provisioning.
- **Status:** Open

---

### RISK-015: Report Distribution List Not Confirmed
- **Category:** Delivery
- **Source:** Architecture Open Question 8, SPEC-013, CQ-004
- **Description:** The weekly compliance report (SPEC-013) requires a specific email distribution list for Compliance Officers. This distribution list has not been confirmed. If it is not in configuration at go-live, the automated report will either fail to send or send to a placeholder address — creating a compliance visibility gap from Day 1.
- **Likelihood:** Medium
- **Impact:** Low (report generation still works; only delivery is affected)
- **Mitigation:**
  - Type: Implementation Guardrail
  - Action 1: Obtain the Compliance Officer email distribution list address from the IT/Compliance team before deployment. Store it in Azure Key Vault configuration.
  - Action 2: Implement a startup validation check that confirms the distribution list address is configured and non-empty. Fail deployment if not configured.
- **Status:** Open

---

### RISK-016: In-Application Rate Limiting Not Planned for Phase 1
- **Category:** Tech Debt
- **Source:** ADR-002 (consequence), ADR-006
- **Description:** ADR-002 deferred rate limiting to "in-application middleware or Phase 2." Without rate limiting, an application with a compromised API key (or a misbehaving integration) can flood the TCPA API with outbound SMS requests, potentially consuming database connection pool capacity or causing the compliance gate to degrade under load — triggering the fail-closed behavior and blocking all SMS from all applications. This is a denial-of-service risk against the compliance gate itself.
- **Likelihood:** Low
- **Impact:** Medium
- **Mitigation:**
  - Type: Implementation Guardrail
  - Action 1: Implement per-API-key rate limiting in the ASP.NET Core middleware pipeline (e.g., ASP.NET Core Rate Limiting middleware, available in .NET 7+). Set per-key rate limits based on expected per-application SMS volume with a 5x headroom multiplier.
  - Action 2: Ensure the Azure Application Gateway WAF has DDoS protection enabled on the inbound endpoint.
- **Status:** Open

---

## Low Risks

| ID       | Title | Category | Mitigation Summary |
|----------|-------|----------|--------------------|
| RISK-017 | Debug logging enabled in production could expose message body PII | Security | Enforce that debug logging requires explicit configuration change with dual-approval; default is production mode. Monitor for unexpected debug log volume in Azure Monitor. |
| RISK-018 | Application Registry in-memory cache TTL could delay deactivation of an application | Operational | TTL set to 5 minutes (per architecture). Document that deactivating an application takes effect within 5 minutes. For emergency deactivation, a service restart flushes the cache immediately. |
| RISK-019 | Confirmation SMS sent from wrong Cool Text account in multi-account scenarios | Compliance | SPEC-005 (BR-024) specifies the confirmation is sent from the Cool Text account that received the opt-out keyword. Verify this is implemented correctly in end-to-end testing; incorrect account would show a different SMS sender to the customer. |
| RISK-020 | OAuth 2.0 upgrade for upstream applications deferred to Phase 2 | Tech Debt | API key rotation discipline must be enforced as a compensating control until Phase 2. Document Phase 2 OAuth upgrade as a committed backlog item. |
| RISK-021 | SMTP relay credentials stored in Key Vault — rotation procedure not defined | Operational | Include SMTP credentials in the Key Vault credential rotation runbook. Alert if SMTP connection fails at report send time. |

---

## Accepted Risks

| ID       | Risk | Rationale for Acceptance | Approved By |
|----------|------|--------------------------|-------------|
| RISK-022 | Single-region Azure deployment cannot protect against full region outage (ARCH-RISK-002) | Azure zone redundancy mitigates intra-region failures (99.99% SLA at database tier). A full region outage is a rare event that would affect all SCG Azure workloads simultaneously. The architecture specifies a Phase 2 evaluation of geo-replication. This is an accepted risk for Phase 1. | [Human approval required] |
| RISK-023 | Opt-out confirmation SMS delivery failure does not reverse opt-out status (SPEC-005, BR-025) | Correct by design. The TCPA compliance obligation is to process the opt-out — the confirmation SMS is a best-effort notification. An undeliverable confirmation (carrier rejection, invalid number) does not change the fact that the customer opted out. | [Human approval required] |

---

## Security Checklist

- [x] Authentication mechanism defined in architecture — API Key for upstream applications (ADR-006); OAuth 2.0 / OIDC Bearer token for Admin API (SPEC-007); HMAC signature for Cool Text webhook (ADR-007)
- [x] Authorization model (RBAC) specified — `tcpa.helpdesk` and `tcpa.compliance_officer` role claims on Admin API; Compliance Officer / reporting roles on report endpoints; no authorization required on outbound SMS endpoint beyond valid API key
- [x] All PII identified and data handling documented — cell phone numbers identified as PII; encrypted at rest (Always Encrypted AES-256); masked in logs (last 4 digits, BR-068); message body encrypted via TDE; no PII in error responses
- [ ] API inputs validated at boundary — specified in architecture (API Gateway validates input before routing); must be implemented and verified. See RISK-005 (webhook auth) and RISK-006 (API key rate limiting)
- [x] Secrets management approach defined — Azure Key Vault for all secrets (DB connection strings, Cool Text API key, SMTP credentials, application API keys, confirmation SMS text)
- [ ] Dependency vulnerability scanning in pipeline — not mentioned in architecture or specs. Must be added to CI/CD pipeline definition. NuGet package scanning (e.g., `dotnet list package --vulnerable`) should run on every build.
- [x] HTTPS enforced on all endpoints — TLS 1.2+ enforced at Azure Application Gateway (NFS-007a); TLS 1.0/1.1 disabled
- [x] Security logging and audit trail defined — authentication failures logged as security events (BR-032); all Admin API actions logged; opt-out and re-opt-in events in immutable audit log (SPEC-008, SPEC-009, SPEC-010); cell numbers masked in production logs (BR-068)

---

## Pre-Development Required Actions

These risks require design decisions or process gates to be resolved **before development begins**:

1. **RISK-001 (Critical — Delivery):** Confirm BizTalk REST adapter feasibility with the BizTalk team. If a REST adapter is not feasible within the timeline, initiate a scope change to add SOAP input support to the TCPA API, or escalate to the program sponsor. This is the single highest-risk item in the delivery.

2. **RISK-004 (Critical — Operational):** Obtain RPO/RTO requirements from IT and Legal for the opt-out status database. This determines the Azure SQL tier (General Purpose vs. Business Critical) and whether geo-redundant backup is required. This decision affects infrastructure provisioning and must precede database schema design.

3. **RISK-008 (High — Compliance):** Confirm with the Architecture Lead that the opt-out compliance gate read (SPEC-006) will target the primary Azure SQL Database, not the read replica. If the read replica must be used for compliance gate reads (for scalability reasons), define and implement cache-poisoning logic to eliminate the replication-lag window. This is a design decision that must be made before the Outbound Proxy Service is implemented.

4. **RISK-005 (High — Security):** Contact Cool Text vendor in Week 1 to confirm webhook signing mechanism. The Inbound Routing Service (SPEC-002) cannot be finalized without knowing whether HMAC or an alternative authentication method is available for the inbound webhook.

5. **RISK-002 (High — Compliance):** Initiate Legal/Compliance review of the opt-out confirmation SMS message text immediately. Provide Legal with the draft message and target a review turnaround of 30 days. This must not be left to the final sprint.

6. **RISK-014 (Medium — Delivery):** Submit IT Security request for IdP confirmation and RBAC role provisioning in Week 1. Admin API development should not be blocked on identity infrastructure.
