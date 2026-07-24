<!-- SDLC Pipeline Artifact
     Stage: 00-brd-to-prd
     Source BRD: inputs/brd_extracted.txt (extracted from inputs/brd.doc v1.0)
     Generated: 2026-07-14
     Decisions resolved: 2026-07-23 (Option C — Alex, interactive session with Mark Ellebie)
     Status: APPROVED
-->

# Product Requirements Document — TCPA Regulatory Compliance API

> ✅ All product decisions resolved in interactive session (2026-07-23) with Mark Ellebie.
> Status: APPROVED. Sam (Agent 01) may proceed.

---

## 1. Overview

### Product Vision
The TCPA Regulatory Compliance API (TCPA API) is a centralized SMS filtering and opt-out enforcement service for Southern Company Gas. It intercepts all outbound text messages across every Gas application, enforces customer opt-out decisions consistently, and ensures compliance with the Telephone Consumer Protection Act (TCPA) — specifically the federal mandate effective January 31, 2027 requiring immediate and universal opt-out enforcement.

**BRD Source:** BRD §Executive Summary, §TCPA API Overview

### Problem Statement
Southern Company Gas currently handles SMS opt-out requests independently within each application (BizTalk, GCMA, KMI, ARM, CCB/My Account). There is no centralized enforcement mechanism, meaning a customer who opts out via one application may still receive texts from others. This exposes the company to legal liability, regulatory penalties, and customer harm under TCPA.

**BRD Source:** BRD §Executive Summary

### Goals

| ID     | Goal                                                              | Success Metric                                                  | BRD Source             |
|--------|-------------------------------------------------------------------|-----------------------------------------------------------------|------------------------|
| GOAL-1 | Centralize opt-out enforcement across all in-scope applications  | Zero text messages sent to opted-out numbers across all apps   | BRD §In Scope          |
| GOAL-2 | Achieve TCPA compliance before the Jan 31, 2027 deadline         | System live and enforcing opt-outs before Jan 31, 2027         | BRD §Executive Summary |
| GOAL-3 | Provide a standardized, auditable opt-out confirmation process   | 100% of STOP requests acknowledged within 60 seconds           | BRD §OOBR04, OOBR09   |
| GOAL-4 | Provide compliance reporting for opt-in/opt-out activity         | Weekly automated compliance reports generated and delivered    | BRD §RPBR03            |

---

## 2. Personas

| ID      | Persona                          | BRD Stakeholder                                           | Primary Needs                                                           | Key Workflows                                                                    | BRD Source           |
|---------|----------------------------------|-----------------------------------------------------------|-------------------------------------------------------------------------|----------------------------------------------------------------------------------|----------------------|
| PER-001 | Gas Customer                     | Customer (cell phone owner)                               | Opt out of all Gas texts with a single message; receive confirmation; know how to re-opt-in | Send STOP keyword; receive global opt-out confirmation; request re-opt-in via Help Desk | BRD §OOBR03–09 |
| PER-002 | Southern Company Gas Application | BizTalk, GCMA, KMI, ARM, CCB                             | Route outbound texts through TCPA API; receive opt-out notifications; comply with suppression | Send outbound text via TCPA API; receive forwarded customer replies              | BRD §OOBR02, §Appendix |
| PER-003 | Help Desk Agent                  | Customer Operations (Rivera, Marten, DeLoach, Coleman, Houston) | Manually process re-opt-in requests from customers                 | Receive Help Desk ticket; update TCPA API opt-in status                          | BRD §OOBR07          |
| PER-004 | Compliance / Audit Team          | Internal Auditing, Legal                                  | Access audit logs; run compliance reports; demonstrate TCPA adherence   | Review audit logs; run/receive weekly compliance reports                          | BRD §OOBR01, RPBR01–03 |
| PER-005 | IT / Developer                   | Gas Technology Solutions Delivery, DTS                    | Monitor API health; debug message flows; respond to failures            | Review production logs; diagnose failures; manage deployments                    | BRD §Appendix        |

[CONFIRMED IN SESSION: PD-001 — The opt-out and TCPA compliance model is uniform across all four LDCs (VNG, CGC, Nicor, AGL). One opt-out record, one confirmation message, one enforcement mechanism — no per-LDC configuration required.]

---

## 3. Functional Requirements

| ID      | Requirement                                                                                               | Priority (MoSCoW) | BRD Source     | Flags |
|---------|-----------------------------------------------------------------------------------------------------------|-------------------|----------------|-------|
| REQ-001 | The system shall maintain an audit log of all opt-out text message requests                               | Must              | OOBR01         |       |
| REQ-002 | The system shall forward the exact text content received from a customer to the applicable Gas application | Must              | OOBR02         |       |
| REQ-003 | The system shall identify and parse the seven TCPA opt-out keywords in inbound SMS messages               | Must              | OOBR03         |       |
| REQ-004 | The system shall update a customer's status to opted-out upon receipt of a STOP keyword                   | Must              | OOBR04         |       |
| REQ-005 | The system shall send an opt-out acknowledgement to the customer within 60 seconds of a STOP request      | Must              | OOBR04         |       |
| REQ-006 | The system shall suppress all outbound SMS messages to opted-out cell phone numbers                        | Must              | OOBR06         |       |
| REQ-007 | The system shall support manual re-opt-in of opted-out cell phone numbers                                 | Must              | OOBR07         |       |
| REQ-008 | The system shall send a standardised global opt-out confirmation message upon STOP request                 | Must              | OOBR09         |       |
| REQ-009 | The system shall report on SMS messages sent to opted-in cell phone numbers                               | Must              | RPBR01         |       |
| REQ-010 | The system shall report on SMS messages sent to opted-out cell phone numbers                              | Must              | RPBR02         |       |
| REQ-011 | The system shall automatically generate weekly compliance reports including alerts and opt-out success rate | Must             | RPBR03         |       |
| REQ-012 | The system shall produce production analysis and debug logs for IT use                                    | Must              | BRD §Appendix  | [ASSUMED: Must based on operational necessity] |
| REQ-013 | The system shall contain the Cool Text account numbers for all in-scope applications                      | Must              | BRD §TCPA API Overview |  |
| REQ-014 | The system shall act as a message broker between in-scope applications and Cool Text / Twilio             | Must              | BRD §TCPA API Overview |  |

### Requirement Details

#### REQ-003: Keyword Identification
**Behavior:** The system must detect any of the following seven keywords in an inbound SMS (case-insensitive): STOP, QUIT, END, REVOKE, OPT-OUT, CANCEL, UNSUBSCRIBE. Receipt of any keyword triggers opt-out processing identically.
**Business Rule Source:** BRD §OOBR03
**Matching Rule:** [CONFIRMED IN SESSION: PD-002] Exact-word match only. The inbound SMS body (trimmed, case-insensitive) must equal one of the seven keywords exactly. A message such as "Please STOP texting me" does NOT trigger opt-out. This simplifies implementation and eliminates false-positive risk.

#### REQ-005: 60-Second Acknowledgement
**Behavior:** From the moment the TCPA API receives a STOP request, it must send the global opt-out confirmation text to the customer within 60 seconds.
**Business Rule Source:** BRD §OOBR04
**Flags:** [ASSUMED: ASM-003 — The 60-second SLA applies to the API's own processing latency, not end-to-end delivery time through Cool Text / Twilio network.]

#### REQ-007: Manual Re-Opt-In
**Behavior:** The system must expose a mechanism that allows a Help Desk agent to update a cell phone number's status from opted-out to opted-in without requiring application-level code changes.
**Business Rule Source:** BRD §OOBR07 — "This to be a manual process (to be defined) and may require a Help Desk ticket."
**Interface:** [CONFIRMED IN SESSION: PD-003] Authenticated admin REST API endpoint. The endpoint is token-gated (API key or bearer token). A full audit record is written on every re-opt-in action. No web UI or script — audit trail requirement makes the API the only compliant option.

#### REQ-008: Global Opt-Out Confirmation Message
**Behavior:** The TCPA API sends a single standardised opt-out confirmation message. It must state the customer has opted out of ALL Southern Company Gas applications and include a phone number for re-opt-in enquiries. One message for all LDCs, regardless of which application the original STOP was sent to.
**Business Rule Source:** BRD §OOBR09
**Message Wording:** [PENDING LEGAL APPROVAL: PD-004] Legal wording not yet approved. Placeholder for development purposes: "You have been unsubscribed from Southern Company Gas text messages. Reply START or call 1-800-XXX-XXXX to re-subscribe." Final wording must be reviewed and approved by Legal / Compliance before go-live. Implementation must use a configuration value — not a hardcoded string — so the wording can be updated without a code deployment.

---

## 4. Non-Functional Requirements

| ID      | Category     | Requirement                                                          | Measurable Target                                                   | BRD Source      | Flags |
|---------|--------------|----------------------------------------------------------------------|---------------------------------------------------------------------|-----------------|-------|
| NFR-001 | Performance  | Opt-out acknowledgement sent within 60 seconds of STOP receipt       | P99 latency: STOP receipt → outbound confirmation ≤ 60s            | OOBR04          |       |
| NFR-002 | Performance  | Opt-out status updated within regulatory window                      | 100% of STOP requests reflected in opted-out status ≤ 10 days     | OOBR04          | [ASSUMED: 10 days is regulatory outer bound; system processes in near-real-time] |
| NFR-003 | Compliance   | Audit log retained for 5 years                                       | All opt-out records queryable for 5 years post-event               | OOBR01          |       |
| NFR-004 | Reliability  | No text messages delivered to opted-out numbers                      | 0% delivery rate to opted-out cell phone numbers                   | OOBR06          |       |
| NFR-005 | Reporting    | Weekly compliance report generated automatically and emailed         | Report delivered by email within 24 hours of week-end cutoff       | RPBR03          | [CONFIRMED IN SESSION: PD-006 — delivery via email distribution list] |
| NFR-006 | Security     | All API communication must be authenticated                          | All endpoints require authentication; zero unauthenticated access  | [ASSUMED]       | [ASSUMED: implied by regulatory sensitivity] |
| NFR-007 | Auditability | Production and debug logs available to IT                            | Logs capture success, failure, and full message flow               | BRD §Appendix   |       |
| NFR-008 | Scalability  | System handles all in-scope application message volumes              | Steady state: ~1,000 msgs/day. Peak burst: up to 5,000 msgs/hour (major outage notification event). P99 throughput must not degrade below NFR-001 SLA at peak. | [CONFIRMED IN SESSION: PD-005 — rough order-of-magnitude estimate] | |

---

## 5. Constraints

| ID      | Type        | Constraint                                                                        | BRD Source              |
|---------|-------------|-----------------------------------------------------------------------------------|-------------------------|
| CON-001 | Regulatory  | System must be live and enforcing opt-outs before January 31, 2027               | BRD §Executive Summary  |
| CON-002 | Technical   | Messaging platforms are Twilio and Cool Text — no others in scope                | BRD §A0001              |
| CON-003 | Technical   | Only applications with Cool Text accounts configured in TCPA API are impacted    | BRD §TCPA API Overview  |
| CON-004 | Scope       | Initial opt-in remains at application level — TCPA API does not manage opt-in   | BRD §Out of Scope       |
| CON-005 | Scope       | Re-opt-in from STOP is a manual process — no automated re-opt-in                 | BRD §Out of Scope, OOBR07 |
| CON-006 | Scope       | Application-level opt-outs do NOT propagate to the TCPA API                     | BRD §Out of Scope       |
| CON-007 | Geographic  | Covers four LDCs: Virginia Natural Gas, Chattanooga Gas, Nicor Gas, Atlanta Gas Light | BRD §In Scope       |
| CON-008 | Technical   | Dialer IVR is excluded; IVR-originated texts bypass TCPA API                    | BRD §Out of Scope       |

---

## 6. Out of Scope

| ID      | Item                                                                           | Source                     |
|---------|--------------------------------------------------------------------------------|----------------------------|
| OOS-001 | Initial opt-in to text messaging (remains at application level)                | BRD §Out of Scope          |
| OOS-002 | Automated re-opt-in process                                                    | BRD §Out of Scope          |
| OOS-003 | Campaign/program-specific text message creation                                | BRD §Out of Scope          |
| OOS-004 | Solutions outside Southern Company Gas                                         | BRD §Out of Scope          |
| OOS-005 | Emergency notifications                                                        | BRD §Out of Scope          |
| OOS-006 | Vendor SMS on behalf of Southern Company Gas (ACI SpeedPay, Google Notifications) | BRD §Out of Scope       |
| OOS-007 | Dialer IVR (Part 280 Shut Off, PURL, AGLC Atlanta IVR)                        | BRD §Out of Scope          |
| OOS-008 | Multi-factor authentication                                                    | BRD §Out of Scope          |
| OOS-009 | Application opt-out propagation to TCPA API                                   | BRD §Out of Scope          |
| OOS-010 | OOBR05 — app-level opt-out triggering TCPA API update                         | BRD §OOBR05 (removed)      |
| OOS-011 | OOBR08 — push TCPA opt-out status back to applications (Phase 2)              | BRD §OOBR08 (removed, Phase 1) |

---

## 7. Success Metrics

| ID      | Metric                                     | Target              | Measurement Method                                          | BRD Source       |
|---------|--------------------------------------------|---------------------|-------------------------------------------------------------|------------------|
| MET-001 | Opt-out acknowledgement latency            | ≤ 60 seconds (P99)  | API log: STOP receipt timestamp → confirmation sent timestamp | OOBR04         |
| MET-002 | Opted-out number suppression rate          | 100%                | RPBR02 report: messages sent to opted-out numbers = 0       | OOBR06           |
| MET-003 | Audit log completeness                     | 100% of STOP events logged | Audit log row count vs. STOP requests received        | OOBR01           |
| MET-004 | Weekly compliance report delivery          | Every week          | Scheduled job execution log                                 | RPBR03           |
| MET-005 | System live before regulatory deadline     | Before Jan 31, 2027 | Production deployment date                                  | BRD §Executive Summary |

---

## 8. Assumptions

| ID      | Assumption                                                                                                  | Type               | BRD Source       |
|---------|-------------------------------------------------------------------------------------------------------------|--------------------|------------------|
| ASM-001 | Twilio and Cool Text are the only messaging platforms in use                                               | From BRD           | BRD §A0001       |
| ASM-002 | All customers are globally opted-in until a STOP request is received via the TCPA API                      | From BRD           | BRD §A0002       |
| ASM-003 | The 60-second SLA applies to TCPA API processing latency, not network delivery time                        | [ASSUMED by Agent] |                  |
| ASM-004 | "10 days" processing SLA is a regulatory outer bound; actual processing is near-real-time                  | [ASSUMED by Agent] | BRD §OOBR04      |
| ASM-005 | The system requires API authentication — not specified in BRD but implied by regulatory sensitivity         | [ASSUMED by Agent] |                  |
| ASM-006 | Weekly reporting = Monday–Sunday week, report generated on Monday for prior week                            | [ASSUMED by Agent] | BRD §RPBR03      |
| ASM-007 | CCB/My Account is definitively out of Phase 1 scope                                                        | [CONFIRMED IN SESSION: PD-007] | BRD §Appendix    |

---

## 9. Dependencies

| ID      | System/Team             | Dependency Type                                                | BRD Source              |
|---------|-------------------------|----------------------------------------------------------------|-------------------------|
| DEP-001 | Cool Text               | TCPA API routes messages through Cool Text accounts            | BRD §TCPA API Overview  |
| DEP-002 | Twilio                  | Secondary messaging platform; Cool Text accounts linked        | BRD §A0001              |
| DEP-003 | BizTalk                 | In-scope application; must integrate with TCPA API             | BRD §In Scope           |
| DEP-004 | GCMA                    | In-scope application; must integrate with TCPA API             | BRD §In Scope           |
| DEP-005 | KMI                     | In-scope application; must integrate with TCPA API             | BRD §In Scope           |
| DEP-006 | ARM / Construction Portal | In-scope application; must integrate with TCPA API           | BRD §In Scope           |
| DEP-007 | CCB / My Account        | Future integration — out of Phase 1 scope (ASM-007)            | BRD §Appendix           |
| DEP-008 | Help Desk system        | Manual re-opt-in requires Help Desk ticket workflow            | BRD §OOBR07             |
| DEP-009 | Legal / Compliance      | Must approve global opt-out message wording (PD-004)           | BRD §OOBR09             |

---

## 10. Product Decisions — Resolved

All decisions resolved in interactive session on 2026-07-23 with Mark Ellebie (Senior Manager, Software & Platform Engineering, Accenture).

| ID     | Decision                                                                       | Resolution                                                            | Status    |
|--------|--------------------------------------------------------------------------------|-----------------------------------------------------------------------|-----------|
| PD-001 | LDC-specific vs. uniform opt-out model                                         | Uniform — one model across VNG, CGC, Nicor, AGL                      | ✅ Resolved |
| PD-002 | Keyword matching: exact-word or substring                                      | Exact-word match only (trimmed, case-insensitive equality)            | ✅ Resolved |
| PD-003 | Re-opt-in interface: admin API, web UI, or script                              | Authenticated admin REST API with full audit trail                   | ✅ Resolved |
| PD-004 | Approved legal wording of global opt-out confirmation message                  | Pending Legal approval — placeholder wording in REQ-008; must be configuration-driven | ✅ Resolved (pending legal sign-off) |
| PD-005 | Expected peak SMS throughput                                                   | Rough estimate: ~1,000/day steady state; up to 5,000/hour burst (outage events) | ✅ Resolved |
| PD-006 | Weekly compliance report delivery mechanism                                    | Email distribution to a defined recipient list                        | ✅ Resolved |
| PD-007 | CCB/My Account Phase 1 scope                                                   | Definitively out of Phase 1 scope                                     | ✅ Resolved |

---

## Translation Summary
- Functional requirements extracted: 14
- NFRs translated from BRD policy: 8
- Assumptions applied: 7
- Product decisions resolved: 7 / 7
- Open items: PD-004 wording pending Legal approval (non-blocking — placeholder in place)
- **Ready to proceed to Agent 01:** Yes
