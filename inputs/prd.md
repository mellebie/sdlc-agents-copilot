<!-- SDLC Pipeline Artifact
     Stage: 00-brd-to-prd
     Source BRD: inputs/brd.doc
     Generated: 2026-06-26
     Status: DRAFT — REQUIRES HUMAN REVIEW BEFORE PIPELINE CONTINUES
-->

# Product Requirements Document — TCPA Regulatory Compliance for Text Messages

> ⚠️ This PRD was generated from a BRD by Agent 00.
> Review all [ASSUMED] and [PRODUCT-DECISION-NEEDED] flags before
> running the SDLC pipeline. Do not proceed to Agent 01 until this
> document is approved.

---

## 1. Overview

### Product Vision
Build a centralized TCPA API — a middleware filtering layer — that sits between Southern Company Gas applications and the Cool Texting/Twilio messaging platform. The API enforces opt-out status in compliance with TCPA Section 64.1200, ensuring no outbound SMS is delivered to any opted-out cell phone number across all Southern Company Gas LDCs, regardless of which application initiates the message.

**BRD Source:** BRD §Executive Summary, §TCPA API Overview

### Problem Statement
Southern Company Gas currently handles SMS opt-out requests inconsistently at the individual application level. With TCPA Section 64.1200 (effective January 31, 2027) requiring that any opt-out revoke consent for all communications from the operating company, the absence of a centralized enforcement mechanism exposes the company to federal non-compliance, legal penalties, customer harm, and reputational risk.

**BRD Source:** BRD §Executive Summary

### Goals

| ID     | Goal                                                                                  | Success Metric                                                                  | BRD Source             |
|--------|---------------------------------------------------------------------------------------|---------------------------------------------------------------------------------|------------------------|
| GOAL-1 | Achieve TCPA compliance for all outbound SMS by Jan 31, 2027 deadline                | Zero SMS messages delivered to opted-out cell numbers through the TCPA API      | BRD §Executive Summary |
| GOAL-2 | Centralize opt-out enforcement across all in-scope Southern Company Gas applications  | 100% of in-scope application text messages routed through TCPA API              | BRD §In Scope          |
| GOAL-3 | Provide compliance audit trail and reporting                                          | Audit log retained 5 years; weekly compliance reports generated without manual intervention | BRD §OOBR01, §RPBR03  |
| GOAL-4 | Reduce consumer opt-out processing time to within TCPA-mandated limits                | Opt-out acknowledgement ≤ 60 seconds; status effective within 10 days           | BRD §OOBR04            |

---

## 2. Personas

> BRDs define stakeholders by role. These have been translated into
> product personas representing user types who interact with the system.

| ID      | Persona               | BRD Stakeholder                               | Primary Needs                                                                 | Key Workflows                                             | BRD Source              |
|---------|-----------------------|-----------------------------------------------|-------------------------------------------------------------------------------|-----------------------------------------------------------|-------------------------|
| PER-001 | Gas Customer          | Consumers sending/receiving SMS                | Reliable opt-out that stops all SCG texts; clear confirmation they are opted out | Replying STOP to any SCG text; receiving global opt-out confirmation | BRD §OOBR04, §OOBR09   |
| PER-002 | Application System    | BizTalk, GCMA, KMI, ARM, CCB/My Account       | Authoritative opt-in/opt-out status before sending SMS; receive customer replies | Sending outbound SMS through TCPA API; receiving inbound customer replies | BRD §OOBR02, §OOBR06   |
| PER-003 | Compliance Officer    | Customer Operations, Legal, Internal Auditing | Audit evidence of opt-out compliance; visibility into enforcement failures     | Reviewing audit logs; reviewing weekly compliance reports | BRD §OOBR01, §RPBR01-03 |
| PER-004 | Help Desk Agent       | Resource Management                           | Ability to manually re-opt-in a cell number for a customer requesting it      | Processing re-opt-in requests via manual/Help Desk ticket | BRD §OOBR07             |
| PER-005 | IT / Platform Engineer| Gas Technology Solutions Delivery, DTS        | Production logs for debugging; visibility into API behavior and failures       | Reviewing debug/production logs; monitoring API health    | BRD §Appendix           |

[PRODUCT-DECISION-NEEDED: PD-001 — Are there internal dashboard users (e.g., call center agents) who need real-time opt-out status lookups by cell number? BRD §OOBR08 (removed from scope) implied this need but the requirement was removed.]

---

## 3. Functional Requirements

> Business rules and process requirements from the BRD have been
> translated into system behaviors.

| ID      | Requirement                                                                                              | Priority (MoSCoW) | BRD Source  | Flags |
|---------|----------------------------------------------------------------------------------------------------------|-------------------|-------------|-------|
| REQ-001 | The system must act as a middleware proxy between in-scope SCG applications and Cool Text/Twilio          | Must              | BRD §TCPA API Overview | |
| REQ-002 | The system must receive inbound SMS replies from customers (via Cool Text) and forward them unchanged to the originating application | Must | BRD §OOBR02 | |
| REQ-003 | The system must detect any of the 7 TCPA opt-out keywords in inbound SMS: STOP, QUIT, END, REVOKE, OPT-OUT, CANCEL, UNSUBSCRIBE (case-insensitive) | Must | BRD §OOBR03 | |
| REQ-004 | The system must set a cell phone number's opt-out status to OPT-OUT in the TCPA API database upon detecting an opt-out keyword | Must | BRD §OOBR04 | |
| REQ-005 | Opt-out acknowledgement (global confirmation text) must be sent to the customer's cell number within 60 seconds of opt-out keyword detection | Must | BRD §OOBR04, §OOBR09 | |
| REQ-006 | Opt-out must be fully effective (no further SMS delivered) within 10 days of the opt-out request         | Must              | BRD §OOBR04 | [ASSUMED: "within 10 days" is a regulatory maximum; the system must enforce immediately upon status update, with 10 days being the outer SLA] |
| REQ-007 | The system must send one standardized global opt-out confirmation SMS to the opted-out cell number, informing them they are opted out of ALL SCG applications and providing a phone number to re-opt in | Must | BRD §OOBR09 | [PRODUCT-DECISION-NEEDED: PD-002 — What is the exact text of the global opt-out confirmation message? Who owns this content?] |
| REQ-008 | The system must block outbound SMS delivery to any cell number with OPT-OUT status in the TCPA API database | Must | BRD §OOBR06 | |
| REQ-009 | The system must write an audit log entry for every opt-out request processed, including: date/time stamp, application, cell phone number, opt-out keyword received, system response, and any other TCPA-required fields | Must | BRD §OOBR01 | |
| REQ-010 | Audit log data must be retained for a minimum of 5 years                                                 | Must              | BRD §OOBR01 | |
| REQ-011 | The system must support manually updating a cell number's opt-out status back to opt-in (re-opt-in)      | Must              | BRD §OOBR07 | [PRODUCT-DECISION-NEEDED: PD-003 — What is the exact mechanism for the manual re-opt-in process? BRD §OOBR07 states it will require a Help Desk ticket but provides no further detail. Who triggers the update — a Help Desk UI, an admin API endpoint, a database script?] |
| REQ-012 | The system must report on all SMS messages sent to opted-in cell numbers (status, cell number, application, date/time, message content) | Must | BRD §RPBR01 | |
| REQ-013 | The system must report on all SMS messages attempted to opted-out cell numbers (status, cell number, application, date/time, message content) | Must | BRD §RPBR02 | |
| REQ-014 | The system must automatically generate weekly compliance reports including: alerts for messages sent to opted-out numbers, alerts for messages sent to opted-in numbers, and opt-out success rate (compliance KPI) | Must | BRD §RPBR03 | [PRODUCT-DECISION-NEEDED: PD-004 — Who receives the weekly compliance reports? What format (email, dashboard, file export)? What is the delivery day/time for weekly reports?] |
| REQ-015 | The system must store and use the Cool Text account number for each in-scope application data flow to route messages correctly | Must | BRD §TCPA API Overview | [ASSUMED: Cool Text account numbers are provided via configuration, not entered by end-users] |
| REQ-016 | The system must produce production analysis and debug logs containing details of all API actions, successes, failures, and behaviors for IT review | Must | BRD §Appendix | |
| REQ-017 | Applications not registered in the TCPA API (Cool Text account not configured) must not be impacted by the TCPA API | Must | BRD §TCPA API Overview | |

### Requirement Details

#### REQ-003: Opt-Out Keyword Detection
**Behavior:** Upon receipt of any inbound SMS, the system must scan the message content for any of the 7 TCPA opt-out keywords. Detection must be case-insensitive. Receipt of any one of these keywords constitutes a STOP request and must be handled identically regardless of which keyword was used.
**Business Rule Source:** BRD §OOBR03 — "STOP text requests can be provided as STOP, QUIT, END, REVOKE, OPT-OUT, CANCEL, UNSUBSCRIBE. Receipt of any of these keywords to imply STOP and shall be handled in the same manner."
**Translated to System Behavior:** Parse inbound SMS body for any occurrence of the 7 keywords. On match, trigger the opt-out workflow regardless of surrounding message content.
**Flags:** [PRODUCT-DECISION-NEEDED: PD-005 — Must the keyword be the entire message body, or should partial-match/substring detection be used? E.g., does a message "Please STOP sending me alerts" trigger opt-out?]

#### REQ-004 / REQ-006: Opt-Out Timing SLA
**Behavior:** The system must set opt-out status immediately upon detection of a STOP keyword. The 60-second SLA applies to sending the confirmation message. The 10-day SLA is the regulatory outer boundary.
**Business Rule Source:** BRD §OOBR04 — "Acknowledgement of Opt-out request to be sent to the customer within 60 seconds. Process of the Opt-Out of text messages to be done within 10 days."
**Translated to System Behavior:** Opt-out status is written to the database synchronously on keyword detection. Confirmation SMS (REQ-007) is dispatched within 60 seconds. Enforcement (REQ-008) takes effect on status write.

#### REQ-008: SMS Blocking for Opted-Out Numbers
**Behavior:** Before forwarding any outbound SMS from an application to Cool Text/Twilio, the system must check the TCPA API database. If the destination cell number has OPT-OUT status, the message is dropped and not delivered. The system must log this blocked attempt per REQ-009.
**Business Rule Source:** BRD §TCPA API Overview — "To restrict sending text messages to cell phone numbers that have been identified as opted out in the TCPA API."
**Translated to System Behavior:** TCPA API intercepts all outbound messages. Database lookup on destination cell number. OPT-OUT status = message suppressed + logged.

---

## 4. Non-Functional Requirements

> BRD compliance, performance, and policy statements translated into
> measurable system targets.

| ID      | Category       | Requirement                                                    | Measurable Target                                                                  | BRD Source     | Flags |
|---------|----------------|----------------------------------------------------------------|------------------------------------------------------------------------------------|----------------|-------|
| NFR-001 | Compliance     | Opt-out acknowledgement timing                                 | Confirmation SMS sent ≤ 60 seconds from opt-out keyword receipt                    | BRD §OOBR04    | |
| NFR-002 | Compliance     | Opt-out enforcement timing                                     | Opted-out cell number receives no further SMS within 10 calendar days of opt-out request | BRD §OOBR04 | |
| NFR-003 | Compliance     | TCPA regulatory deadline                                       | System live and enforcing opt-out rules by January 31, 2027                        | BRD §Executive Summary | |
| NFR-004 | Data Retention | Audit log retention                                            | Audit log data retained for minimum 5 years from event date                        | BRD §OOBR01    | |
| NFR-005 | Reliability    | Outbound SMS blocking must not fail open                       | If TCPA API is unavailable, outbound SMS to unverified numbers must be blocked (fail-closed) | BRD §OOBR06 | [PRODUCT-DECISION-NEEDED: PD-006 — BRD does not specify fail-open vs. fail-closed behavior when the TCPA API is unavailable. Failing closed is safer for compliance but could block all SMS during outages. Confirm with Jim Stagg / Prashant Pathak.] |
| NFR-006 | Performance    | Inbound keyword detection latency                              | Opt-out keyword detection and status write ≤ 5 seconds of message receipt [ASSUMED] | BRD §OOBR03-04 | [ASSUMED: BRD implies near-real-time but provides no explicit target beyond the 60s confirmation SLA] |
| NFR-007 | Security       | Cell phone numbers (PII) protected in transit and at rest      | All cell phone numbers encrypted in database and in API transit (HTTPS/TLS 1.2+)  | [ASSUMED: TCPA compliance and SCG data policies imply PII protection] | [ASSUMED] |
| NFR-008 | Auditability   | Audit log completeness                                         | 100% of opt-out requests produce a corresponding audit log entry; no silent failures | BRD §OOBR01   | |
| NFR-009 | Scalability    | Support all in-scope applications simultaneously               | System handles concurrent message volume from BizTalk, GCMA, KMI, ARM, CCB/My Account without degradation | BRD §In Scope | [PRODUCT-DECISION-NEEDED: PD-007 — Message volume per application is not stated in BRD. Volume estimates needed for sizing.] |
| NFR-010 | Observability  | Production and debug logging                                   | All API actions, successes, failures, and behavior logged in structured format accessible to IT | BRD §Appendix | |

---

## 5. Constraints

| ID      | Type        | Constraint                                                                       | BRD Source                  |
|---------|-------------|---------------------------------------------------------------------------------|-----------------------------|
| CON-001 | Regulatory  | Must comply with TCPA Section 64.1200 (effective January 31, 2027)               | BRD §Executive Summary      |
| CON-002 | Regulatory  | Must comply with CTIA guidelines for SMS opt-out handling                        | BRD §Executive Summary      |
| CON-003 | Technical   | Messaging platform is Cool Text and Twilio — no other SMS platform in scope     | BRD §A0001                  |
| CON-004 | Technical   | TCPA API must not modify opt-out status at the application level; only in its own database | BRD §OOBR04, §TCPA API Overview |
| CON-005 | Scope       | Initial opt-in remains at the application level; TCPA API does not manage opt-in | BRD §Out of Scope, §A0002   |
| CON-006 | Scope       | Vendor SMS (ACI SpeedPay, Google Notifications) is out of scope                 | BRD §Out of Scope           |
| CON-007 | Scope       | IVR Dialer is out of scope; can still send SMS regardless of TCPA API status    | BRD §Out of Scope           |
| CON-008 | Scope       | Multi-factor authentication SMS is out of scope                                  | BRD §Out of Scope           |
| CON-009 | Geographic  | Must cover all four LDCs: Virginia Natural Gas, Chattanooga Gas Company, Nicor Gas, Atlanta Gas Light | BRD §In Scope |
| CON-010 | Business    | Re-opt-in is a manual process only; no automated re-opt-in from application opt-in actions | BRD §Out of Scope, §OOBR07 |

---

## 6. Out of Scope

| ID      | Item                                                                                              | Source                           |
|---------|---------------------------------------------------------------------------------------------------|----------------------------------|
| OOS-001 | Initial customer opt-in to text messaging (remains at application level)                          | BRD §Out of Scope                |
| OOS-002 | Automated re-opt-in after opt-out                                                                 | BRD §Out of Scope                |
| OOS-003 | Program/campaign-specific message creation by TCPA API                                           | BRD §Out of Scope                |
| OOS-004 | Solutions for entities outside Southern Company Gas                                               | BRD §Out of Scope                |
| OOS-005 | Emergency SMS notifications (no emergency texts exist today)                                      | BRD §Out of Scope                |
| OOS-006 | Vendor-managed SMS texting (ACI SpeedPay Twilio, Google Notifications)                           | BRD §Out of Scope                |
| OOS-007 | Dialer IVR SMS (IVR Part 280 Shut Off Notice, PURL Request, AGLC Atlanta IVR Marketing List URL) | BRD §Out of Scope                |
| OOS-008 | Multi-factor authentication SMS                                                                   | BRD §Out of Scope                |
| OOS-009 | Application-level opt-out propagating to TCPA API (only SMS STOP keyword triggers TCPA API opt-out) | BRD §Out of Scope             |
| OOS-010 | Opt-out status push from TCPA API to applications (removed from Phase 1)                         | BRD §OOBR05, §OOBR08 removed    |

---

## 7. Success Metrics

| ID      | Metric                                                   | Target                                             | Measurement Method                               | BRD Source            |
|---------|----------------------------------------------------------|----------------------------------------------------|--------------------------------------------------|-----------------------|
| MET-001 | Opt-out confirmation delivery time                       | ≤ 60 seconds from STOP keyword receipt             | Audit log timestamps: keyword received vs. confirmation sent | BRD §OOBR04  |
| MET-002 | Opt-out enforcement effectiveness                        | 0 SMS delivered to opted-out cell numbers          | Weekly compliance report: messages sent to opted-out numbers | BRD §OOBR06, §RPBR02 |
| MET-003 | Audit log completeness                                   | 100% of opt-out events logged                      | Count of opt-out status changes vs. audit log entries | BRD §OOBR01    |
| MET-004 | Weekly compliance report generation                      | 100% automated, zero manual interventions required | Report delivery logs                              | BRD §RPBR03           |
| MET-005 | Application integration coverage                         | 100% of in-scope application text flows routed via TCPA API by go-live | Integration testing across BizTalk, GCMA, KMI, ARM, (CCB future) | BRD §In Scope |
| MET-006 | TCPA regulatory deadline                                 | System live in production by January 31, 2027      | Production deployment date                        | BRD §Executive Summary |

---

## 8. Assumptions

| ID      | Assumption                                                                                                   | Type               | BRD Source           |
|---------|--------------------------------------------------------------------------------------------------------------|--------------------|----------------------|
| ASM-001 | Twilio and Cool Text are the only messaging platforms currently in use by Southern Company Gas                | From BRD           | BRD §A0001 (confirmed Prashant Pathak, 2/19/2026) |
| ASM-002 | All customers are globally opted-in at the TCPA API level until a STOP keyword is received via SMS           | From BRD           | BRD §A0002 (confirmed Prashant Pathak, 2/19/2026) |
| ASM-003 | Customer opt-in occurs at the application level; no SMS is sent until the customer opts in via the application | From BRD          | BRD §A0002           |
| ASM-004 | CCB/My Account integration will be included in this release given potential Q2 2026 go-live                  | From BRD           | BRD §Appendix §Applications in Scope |
| ASM-005 | ARM/Construction Portal (GlanceAndSee campaign) went live February 2026 and is already sending texts         | From BRD           | BRD §Appendix        |
| ASM-006 | Cool Text account numbers for in-scope applications are available and will be provided via configuration     | [ASSUMED by Agent] | BRD §TCPA API Overview |
| ASM-007 | The TCPA API is built as a new standalone service, not as a modification to an existing application           | [ASSUMED by Agent] | BRD §TCPA API Overview |
| ASM-008 | The 10-day opt-out window is a regulatory maximum; the system must enforce opt-out status as immediately as technically possible | [ASSUMED by Agent] | BRD §OOBR04 |

---

## 9. Dependencies

| ID      | System/Team                        | Dependency Type                                                       | BRD Source           |
|---------|------------------------------------|-----------------------------------------------------------------------|----------------------|
| DEP-001 | Cool Text platform                 | TCPA API must integrate with Cool Text for inbound/outbound SMS routing | BRD §TCPA API Overview |
| DEP-002 | Twilio platform                    | Twilio is part of the messaging infrastructure in use                 | BRD §A0001           |
| DEP-003 | BizTalk                            | Application integration — must route outbound SMS through TCPA API  | BRD §In Scope        |
| DEP-004 | GCMA                               | Application integration — must route outbound SMS through TCPA API  | BRD §In Scope        |
| DEP-005 | KMI Active                         | Application integration — must route outbound SMS through TCPA API  | BRD §In Scope        |
| DEP-006 | ARM / Construction Portal          | Application integration — must route outbound SMS through TCPA API  | BRD §In Scope        |
| DEP-007 | CCB / My Account (Future)          | Application integration — target Q2 2026 go-live, may be included   | BRD §Appendix        |
| DEP-008 | Help Desk ticketing system         | Manual re-opt-in process requires a defined Help Desk workflow        | BRD §OOBR07          |
| DEP-009 | TCPA compliance/legal team         | Legal must review and approve global opt-out message content          | BRD §OOBR09, §Sign-off |

---

## 10. Product Decisions Required

> These items could not be translated from the BRD because the BRD
> does not contain sufficient product detail. A human must resolve
> these before the pipeline continues.

| ID    | Question                                                                                                                               | BRD Context                                      | Blocking? |
|-------|----------------------------------------------------------------------------------------------------------------------------------------|--------------------------------------------------|-----------|
| PD-001 | Are there internal users (e.g., call center agents) who need a real-time opt-out status lookup UI by cell number?                    | BRD §OOBR08 was removed from scope for Phase 1  | No        |
| PD-002 | What is the exact text of the global opt-out confirmation SMS? Who owns and approves this content?                                    | BRD §OOBR09 — "ONE message for all, regardless of LDC" | Yes  |
| PD-003 | What is the exact mechanism for the manual re-opt-in process? (Help Desk UI, admin API endpoint, database script, other?)             | BRD §OOBR07 — "manual process (to be defined)"  | Yes       |
| PD-004 | Who receives the weekly compliance reports? In what format (email, dashboard, file)? On what day/time?                                | BRD §RPBR03 — delivery details not specified     | Yes       |
| PD-005 | Does opt-out keyword detection require a full-message exact match, or substring/partial match?                                         | BRD §OOBR03 — matching scope not specified       | Yes       |
| PD-006 | Should the TCPA API fail closed (block all SMS) or fail open (pass through) when the API is unavailable?                              | Not addressed in BRD                             | Yes       |
| PD-007 | What is the expected message volume (messages/day) per application? Required for sizing.                                              | Not provided in BRD                              | Yes       |
| PD-008 | What is the target availability SLA for the TCPA API? (e.g., 99.9% uptime)                                                           | Not addressed in BRD                             | Yes       |
| PD-009 | What constitutes the "other info as required by TCPA" field in the audit log?                                                         | BRD §OOBR01 — "Other info as required by TCPA" without specifying | Yes |

---

## Translation Summary
- Functional requirements extracted: 17
- NFRs translated from BRD policy: 10
- Assumptions applied: 8
- Product decisions required: 9
- **Ready to proceed to Agent 01:** No — pending resolution of PD-002 through PD-009
