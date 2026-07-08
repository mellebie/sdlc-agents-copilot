<!-- SDLC Pipeline Artifact
     Stage: 02-clarification
     Source PRD: inputs/prd.md
     PRD Sections: §1 Overview, §2 Personas, §3 Functional Requirements, §4 Non-Functional Requirements, §5 Constraints, §6 Out of Scope, §7 Success Metrics, §8 Assumptions, §9 Dependencies, §10 Product Decisions Required
     Generated: 2026-06-26
     Status: APPROVED — human approved proceeding despite open questions (2026-06-26)
-->

# Clarifications Required — TCPA Regulatory Compliance for Text Messages

## Summary
- Blocking questions: 11
- Important questions: 8
- Nice to have questions: 4
- Total: 23

---

## Blocking Questions
These must be resolved before the pipeline continues.

### CQ-001
- **Source:** AMB-001 / REQ-003 / PD-005
- **Question:** When an inbound SMS body contains an opt-out keyword (STOP, QUIT, END, REVOKE, OPT-OUT, CANCEL, UNSUBSCRIBE), should the keyword be matched only when it appears as the entire message body (exact full-message match, e.g., the body is exactly "STOP"), or should the keyword trigger an opt-out when it appears anywhere within a longer message body (substring match, e.g., "Please STOP sending me texts")?
- **Why it blocks:** This is a binary architectural choice that determines the text-parsing logic at the core of the compliance engine. An exact-match implementation and a substring-match implementation are materially different in code and produce different compliance outcomes. Choosing substring match may produce false positives (e.g., a customer saying "I won't stop my service" inadvertently opts out). Choosing exact match may miss genuine opt-outs. The wrong choice creates TCPA liability.
- **Answer:** _[human to fill in]_

---

### CQ-002
- **Source:** AMB-002 / REQ-007 / PD-002
- **Question:** What is the exact, legally approved text of the global opt-out confirmation SMS that the TCPA API must send to a customer after their opt-out is processed? Please provide the full message string, including the re-opt-in phone number to be embedded in the message.
- **Why it blocks:** REQ-007 requires sending one specific standardized global opt-out confirmation SMS. This message cannot be implemented, tested, or reviewed for TCPA compliance without the approved text. DEP-009 (Legal/Compliance team review) must be complete before this requirement can be built.
- **Answer:** _[human to fill in]_

---

### CQ-003
- **Source:** AMB-003 / REQ-012 / PD-003
- **Question:** What is the mechanism by which a Help Desk agent performs a manual re-opt-in? Specifically: (a) Is there a UI (web-based admin portal)? (b) Is it an authenticated API endpoint that a Help Desk tool calls? (c) Is it a direct database update performed by IT? Please specify which approach is required, and if (a) or (b), describe the authentication model for access control.
- **Why it blocks:** The re-opt-in mechanism is a Must Have requirement (REQ-012). Without knowing the delivery mechanism, the architecture cannot be designed — a Help Desk UI and a secured API endpoint are different components requiring different implementation effort, security controls, and integration work.
- **Answer:** _[human to fill in]_

---

### CQ-004
- **Source:** AMB-004 / REQ-015 / PD-004
- **Question:** For the weekly compliance report (REQ-015): (a) Who receives it — list specific roles or distribution lists? (b) In what format is it delivered — email attachment, email body, dashboard, file drop to a shared location? (c) What day and time should the weekly report be generated and sent?
- **Why it blocks:** REQ-015 is a Must Have requirement. Without knowing the delivery channel, format, and schedule, the report generation and distribution components cannot be designed or built. "Automated weekly report" has entirely different architectural implications depending on whether it is an email, a dashboard widget, or a file drop.
- **Answer:** _[human to fill in]_

---

### CQ-005
- **Source:** AMB-005 / NFR-005 / PD-006
- **Question:** If the TCPA API is unavailable or unreachable when an upstream application (BizTalk, GCMA, KMI, ARM, CCB) attempts to send an outbound SMS, should the system fail-closed (block the message — no SMS is sent until the TCPA API is available) or fail-open (allow the message to pass through to Cool Text/Twilio without a compliance check)?
- **Why it blocks:** This is a fundamental safety and regulatory decision. Fail-open creates TCPA liability if an opted-out customer receives an SMS during an outage. Fail-closed may impact time-sensitive operational messages (e.g., gas leak alerts). The architecture for the upstream application integration, error handling, retry logic, and queuing strategy is entirely different depending on this decision. Legal/Compliance must weigh in.
- **Answer:** _[human to fill in]_

---

### CQ-006
- **Source:** AMB-006 / NFR-009 / PD-007
- **Question:** What is the estimated peak and average message volume (outbound SMS per hour, per day) across all five in-scope applications combined (BizTalk, GCMA, KMI, ARM, CCB/My Account)? Please provide per-application estimates if available.
- **Why it blocks:** The TCPA API must be sized for the actual load it will receive. Without volume figures, the architecture cannot specify infrastructure capacity, database connection pools, API gateway throughput, or queue depths. An undersized system that drops messages under load creates TCPA compliance failures at the worst possible time.
- **Answer:** _[human to fill in]_

---

### CQ-007
- **Source:** AMB-007 / NFR-011 / PD-008
- **Question:** What is the required uptime SLA for the TCPA API? For example: 99.9% (approximately 8.7 hours downtime per year), 99.5%, or 99.99%? Does the SLA apply 24x7 or only during business hours?
- **Why it blocks:** The availability target determines the deployment architecture — specifically whether the system requires active-active redundancy, geographic failover, hot standby, or a simpler single-region deployment. This directly drives infrastructure cost and complexity.
- **Answer:** _[human to fill in]_

---

### CQ-008
- **Source:** AMB-008 / REQ-009 / PD-009
- **Question:** Beyond the fields already specified in REQ-009 (date/time stamp, originating application, cell phone number, opt-out keyword received, system response), are there additional fields required in the audit log entry for a TCPA opt-out event to satisfy TCPA Section 64.1200 or CTIA audit requirements? Please provide the complete required field list, or confirm that the REQ-009 list is exhaustive.
- **Why it blocks:** The audit log schema must be finalized before development begins — changing the schema after data is being written is a migration risk and could create gaps in the compliance record. If the schema is too narrow, the audit trail may not satisfy regulatory discovery requests.
- **Answer:** _[human to fill in]_

---

### CQ-009
- **Source:** DEP-007 / ASM-004
- **Question:** Is the CCB/My Account integration included in the January 31, 2027 delivery scope, or is it deferred to a subsequent release? The PRD notes a "potential Q2 2026 go-live" for CCB/My Account and flags inclusion as conditional. Please make an explicit go/no-go decision for the initial release.
- **Why it blocks:** Including CCB/My Account significantly changes the integration scope, effort estimation, and potentially the delivery timeline. Story points, task estimates, and the dependency on DEP-007 cannot be correctly scoped until this is resolved. If CCB is in scope, DEP-007 becomes a hard dependency with a coordination risk.
- **Answer:** _[human to fill in]_

---

### CQ-010
- **Source:** REQ-002 / REQ-001 / Architecture gap
- **Question:** When the TCPA API receives an inbound SMS reply from a customer that is NOT an opt-out keyword (e.g., a general customer reply), should it: (a) forward the message to the originating application that sent the most recent outbound SMS to that cell number, (b) forward to all applications registered for that cell number, or (c) apply some other routing logic? What happens if the originating application cannot be determined?
- **Why it blocks:** REQ-002 requires forwarding inbound non-opt-out replies "unchanged to the originating application," but the routing logic for identifying the originating application is not specified. Without this, the inbound message routing architecture cannot be designed, and there is a risk of messages being lost or delivered to the wrong application.
- **Answer:** _[human to fill in]_

---

### CQ-011
- **Source:** REQ-016 / CON-003 / Architecture gap
- **Question:** How does the TCPA API receive and authenticate outbound SMS requests from upstream applications — specifically, what protocol does each application use to call the TCPA API (REST HTTP, SOAP, message queue, other)? Does each application use the same protocol, or does the integration pattern differ per application (e.g., BizTalk uses a different integration pattern than GCMA)?
- **Why it blocks:** The integration protocol determines the TCPA API's inbound interface design. If BizTalk requires SOAP and GCMA requires REST, the API must support multiple input protocols, which is a significant architectural decision. This cannot be assumed — it must be confirmed with the application teams before the interface contract is designed.
- **Answer:** _[human to fill in]_

---

## Important Questions
These affect architecture or story scope but may have reasonable defaults.

### CQ-012
- **Source:** AMB-009 / NFR-006
- **Question:** Is the assumed ≤ 5-second latency target for inbound opt-out keyword detection and opt-out status write acceptable? This target was inferred from the 60-second confirmation SLA — it is not stated in the PRD. Should a tighter or looser target be set?
- **Suggested Default:** ≤ 5 seconds from message receipt to status write, which leaves 55 seconds of headroom for the confirmation SMS to be sent within the 60-second TCPA window.
- **Answer:** _[human to fill in]_

---

### CQ-013
- **Source:** AMB-010 / NFR-007
- **Question:** The PRD does not explicitly state PII protection requirements for cell phone numbers. Should the following assumed controls be confirmed as requirements: (a) HTTPS/TLS 1.2 or higher for all API transit, (b) cell phone numbers encrypted at rest in the database? Are there any additional SCG data classification or InfoSec standards that apply?
- **Suggested Default:** Yes to both (a) and (b), based on regulatory context and standard data protection practices for PII.
- **Answer:** _[human to fill in]_

---

### CQ-014
- **Source:** GAP-001 / REQ-012
- **Question:** When a Help Desk agent processes a re-opt-in request (REQ-012), do they need to first look up the current opt-out status of a cell number before updating it? If so, should the TCPA API expose a status lookup endpoint (read-only query: "is this cell number currently opted in or out?") as part of the re-opt-in workflow?
- **Suggested Default:** Yes — a status lookup is a reasonable prerequisite for a re-opt-in action, and an authenticated read-only endpoint is a lower-risk addition than the update endpoint already required.
- **Answer:** _[human to fill in]_

---

### CQ-015
- **Source:** GAP-002
- **Question:** What are the disaster recovery requirements for the TCPA API opt-out database? Specifically: (a) What is the recovery time objective (RTO — maximum acceptable downtime after a failure)? (b) What is the recovery point objective (RPO — maximum acceptable data loss)? (c) Is database backup to a secondary region required?
- **Suggested Default:** RPO ≤ 1 hour, RTO ≤ 4 hours, with daily backups, based on the regulatory sensitivity of the data. However, this must be confirmed with IT and Legal.
- **Answer:** _[human to fill in]_

---

### CQ-016
- **Source:** GAP-003 / REQ-016
- **Question:** When a new application needs to be onboarded to the TCPA API (i.e., a new Cool Text account number must be registered), what is the process? Is this: (a) a configuration file change deployed by IT, (b) an admin API call, or (c) a database record insert by IT? Who is authorized to perform this action, and is there an approval process?
- **Suggested Default:** Configuration file managed by IT/Platform Engineering, with no self-service UI required in Phase 1.
- **Answer:** _[human to fill in]_

---

### CQ-017
- **Source:** GAP-004
- **Question:** Should the TCPA API implement rate limiting on its inbound API to protect against abuse or runaway upstream applications flooding the opt-out confirmation SMS channel? If yes, what are the acceptable thresholds (e.g., maximum requests per second per application)?
- **Suggested Default:** Basic rate limiting per registered Cool Text account (application) should be implemented as a defense-in-depth measure, with thresholds set based on confirmed message volume (CQ-006).
- **Answer:** _[human to fill in]_

---

### CQ-018
- **Source:** REQ-013 / REQ-014 / REQ-015
- **Question:** Are the reports described in REQ-013 (all SMS to opted-in numbers), REQ-014 (all SMS to opted-out numbers), and REQ-015 (weekly compliance report) three separate reports, or is REQ-015 the weekly scheduled delivery of the REQ-013 and REQ-014 data combined? Is there a self-service reporting interface, or are all reports system-generated outputs?
- **Suggested Default:** REQ-015 is the automated weekly delivery combining REQ-013 and REQ-014 data. REQ-013 and REQ-014 are the underlying data sets that can also be queried on demand. Confirm.
- **Answer:** _[human to fill in]_

---

### CQ-019
- **Source:** CON-007 / OOS-007
- **Question:** IVR Dialer SMS is explicitly out of scope and "may send SMS regardless of TCPA API opt-out status." Is there a legal or compliance risk acknowledged and accepted for IVR Dialer SMS bypassing TCPA opt-out enforcement? Has Legal signed off on this exclusion specifically?
- **Suggested Default:** This appears to be a known accepted risk. Confirm that Legal has explicitly approved the IVR Dialer exclusion and that it is documented in the compliance record.
- **Answer:** _[human to fill in]_

---

## Nice to Have Questions

### CQ-020
- **Source:** REQ-005 / REQ-007
- **Question:** If the opt-out confirmation SMS (REQ-005 / REQ-007) cannot be delivered to the customer's cell number (e.g., the number is invalid, the carrier rejects it, or Cool Text returns an error), what should the TCPA API do? Should it retry, log the failure, alert an operator, or simply log and continue?
- **Answer:** _[human to fill in]_

---

### CQ-021
- **Source:** REQ-004 / ASM-002
- **Question:** If the TCPA API receives a second STOP keyword from a cell number that is already opted out, should it: (a) silently ignore it (idempotent — no action, no re-confirmation SMS), (b) send the confirmation SMS again, or (c) log it but take no further action?
- **Answer:** _[human to fill in]_

---

### CQ-022
- **Source:** REQ-006 / ASM-008
- **Question:** REQ-006 states opted-out numbers receive no further SMS "within 10 calendar days." ASM-008 states opt-out must be enforced immediately upon status write. These are consistent but REQ-006's phrasing could be misread as allowing a 10-day delay. Should REQ-006 be clarified to state "immediately upon opt-out status write, and in all cases within 10 calendar days as required by TCPA"?
- **Answer:** _[human to fill in]_

---

### CQ-023
- **Source:** PER-004 / REQ-012
- **Question:** When a Help Desk agent processes a manual re-opt-in, should the TCPA API send an automated confirmation SMS to the customer's cell number informing them that they have been re-opted-in to SCG text communications, or is that notification handled entirely by the Help Desk agent via phone/other channel?
- **Answer:** _[human to fill in]_

---

## Conflicts Identified

| ID      | Requirement A                                      | Requirement B                                       | Nature of Conflict                                                                                                                                                                                                  |
|---------|-----------------------------------------------------|-----------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| CONF-001 | REQ-006: Opted-out cell numbers must receive no further SMS within 10 calendar days | ASM-008: Opt-out must be enforced immediately upon status write | Not a true conflict, but REQ-006's phrasing ("within 10 calendar days") implies a window that contradicts the immediate enforcement intent of ASM-008. The wording of REQ-006 should be tightened to avoid misinterpretation during implementation. See CQ-022. |
| CONF-002 | CON-005: Initial opt-in remains at application level; TCPA API does not manage opt-in | REQ-012: System must support manual re-opt-in (updating opt-out status back to OPT-IN) | The TCPA API explicitly does not manage initial opt-in (CON-005 / OOS-001), but REQ-012 requires it to write an OPT-IN status for re-opt-ins. This is technically consistent (re-opt-in after a STOP is different from initial opt-in), but the constraint language is ambiguous. The distinction between "initial opt-in" (out of scope) and "re-opt-in after an opt-out" (in scope, REQ-012) should be explicitly stated to prevent implementation errors. |
| CONF-003 | OOS-009: Application-level opt-out does not propagate to TCPA API | REQ-004: System sets opt-out status upon detecting STOP keyword via SMS | No direct conflict, but the asymmetry (opt-out only via STOP SMS keyword, never from application-level signal) must be architecturally enforced. If an upstream application sends an opt-out signal in the API request, the TCPA API must ignore it. The expected behavior for this scenario is not documented. |

---

## Sign-off
Once all Blocking and Important questions are answered, update
Status to APPROVED and proceed to Agent 03.
