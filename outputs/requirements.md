<!-- SDLC Pipeline Artifact
     Stage: 01-prd-analyst
     Source PRD: inputs/prd.md
     PRD Sections: All (Overview, Personas, Functional Requirements, NFRs, Constraints, Out of Scope, Success Metrics, Assumptions, Dependencies, Decisions)
     Generated: 2026-07-23
     Status: DRAFT
-->

# Requirements — TCPA Regulatory Compliance API

## Product Vision
The TCPA Regulatory Compliance API is a centralized SMS filtering and opt-out enforcement service for Southern Company Gas. It intercepts all outbound text messages across every in-scope Gas application (BizTalk, GCMA, KMI, ARM), enforces customer opt-out decisions consistently across all four LDCs, and ensures compliance with the federal TCPA mandate effective January 31, 2027. A customer who sends a single STOP keyword to any in-scope application is globally opted out across the entire platform.

## Goals

| ID     | Goal                                                              | Success Metric                                                  | PRD Ref   |
|--------|-------------------------------------------------------------------|-----------------------------------------------------------------|-----------|
| GOAL-1 | Centralize opt-out enforcement across all in-scope applications  | Zero text messages sent to opted-out numbers across all apps   | PRD §1    |
| GOAL-2 | Achieve TCPA compliance before the Jan 31, 2027 deadline         | System live and enforcing opt-outs before Jan 31, 2027         | PRD §1    |
| GOAL-3 | Provide a standardized, auditable opt-out confirmation process   | 100% of STOP requests acknowledged within 60 seconds           | PRD §1    |
| GOAL-4 | Provide compliance reporting for opt-in/opt-out activity         | Weekly automated compliance reports generated and delivered     | PRD §1    |

## Personas

| ID      | Persona Name                     | Description                                                          | Primary Needs                                                              | PRD Ref    |
|---------|----------------------------------|----------------------------------------------------------------------|----------------------------------------------------------------------------|------------|
| PER-001 | Gas Customer                     | Cell phone owner who receives texts from Southern Company Gas apps   | Opt out of all Gas texts with one STOP; receive confirmation; know how to re-opt-in | PRD §2 |
| PER-002 | Southern Company Gas Application | BizTalk, GCMA, KMI, ARM — SMS-sending systems routing through the API | Route outbound texts through TCPA API; receive inbound customer replies forwarded | PRD §2 |
| PER-003 | Help Desk Agent                  | Customer operations staff handling re-opt-in requests                | Manually re-opt-in a customer via authenticated admin API                  | PRD §2    |
| PER-004 | Compliance / Audit Team          | Internal auditing and legal staff                                    | Access audit logs; receive weekly compliance reports; demonstrate TCPA adherence | PRD §2 |
| PER-005 | IT / Developer                   | Gas Technology Solutions Delivery and DTS engineers                 | Monitor API health; debug message flows; access production and debug logs  | PRD §2    |

## Functional Requirements

| ID      | Requirement                                                                                               | Priority (MoSCoW) | PRD Ref         | Flags               |
|---------|-----------------------------------------------------------------------------------------------------------|-------------------|-----------------|---------------------|
| REQ-001 | The system shall maintain an audit log of all opt-out text message requests                               | Must              | PRD §3, OOBR01  |                     |
| REQ-002 | The system shall forward the exact inbound text content received from a customer to the applicable Gas application | Must        | PRD §3, OOBR02  | [RESOLVED: AMB-001] |
| REQ-003 | The system shall identify the seven TCPA opt-out keywords via exact-word, case-insensitive match          | Must              | PRD §3, OOBR03  |                     |
| REQ-004 | The system shall update a customer's status to opted-out upon receipt of an exact opt-out keyword         | Must              | PRD §3, OOBR04  |                     |
| REQ-005 | The system shall send an opt-out acknowledgement to the customer within 60 seconds of a STOP request     | Must              | PRD §3, OOBR04  |                     |
| REQ-006 | The system shall suppress all outbound SMS messages to opted-out cell phone numbers                       | Must              | PRD §3, OOBR06  |                     |
| REQ-007 | The system shall expose an authenticated admin REST API for Help Desk to manually re-opt-in a customer   | Must              | PRD §3, OOBR07  | [RESOLVED: AMB-002] |
| REQ-008 | The system shall send a configurable global opt-out confirmation message upon STOP request                | Must              | PRD §3, OOBR09  |                     |
| REQ-009 | The system shall report on SMS messages sent to opted-in cell phone numbers                              | Must              | PRD §3, RPBR01  | [RESOLVED: AMB-003] |
| REQ-010 | The system shall report on SMS messages sent to opted-out cell phone numbers                             | Must              | PRD §3, RPBR02  | [RESOLVED: AMB-003] |
| REQ-011 | The system shall automatically generate weekly compliance reports and deliver them via email             | Must              | PRD §3, RPBR03  | [GAP: GAP-002]      |
| REQ-012 | The system shall produce production analysis and debug logs for IT use                                   | Must              | PRD §3, §Appendix |                   |
| REQ-013 | The system shall maintain Cool Text account number configuration for all in-scope applications           | Must              | PRD §3          | [RESOLVED: GAP-004] |
| REQ-014 | The system shall act as a message broker between in-scope applications and Cool Text / Twilio            | Must              | PRD §3          | [RESOLVED: GAP-001] |

### Requirement Details

#### REQ-002: Inbound Message Forwarding
**Behavior:** When a customer replies to a Gas application text, the TCPA API receives the inbound message from Cool Text / Twilio and forwards it verbatim to the originating Gas application.
**Ambiguity — AMB-001:** The routing mechanism (how the TCPA API identifies which Gas application a specific inbound message belongs to) is not specified. Likely via Cool Text account number mapping (REQ-013), but the data model and routing logic need architectural definition.

#### REQ-003: Keyword Detection
**Behavior:** The seven opt-out keywords are: STOP, QUIT, END, REVOKE, OPT-OUT, CANCEL, UNSUBSCRIBE. The inbound SMS body is trimmed and compared case-insensitively. Match must be exact — "Please STOP texting" does NOT trigger opt-out. Only "STOP" (alone) does.
**Source:** PRD §3, OOBR03 — PD-002 confirmed exact-word matching.

#### REQ-005: 60-Second Acknowledgement
**Behavior:** From the moment the TCPA API receives a STOP request, it must send the global opt-out confirmation text to the customer within 60 seconds. SLA applies to API processing latency, not end-to-end network delivery time through Cool Text / Twilio.

#### REQ-007: Manual Re-Opt-In
**Behavior:** A token-gated admin REST API endpoint allows a Help Desk agent to update a cell phone number's status from opted-out to opted-in. Every re-opt-in action writes a full audit record (number, agent identity, timestamp, reason).
**Ambiguity — AMB-002:** Authentication mechanism (API key vs. bearer token), token issuance, and key management process not specified in PRD.

#### REQ-008: Global Opt-Out Confirmation Message
**Behavior:** Message wording is stored as a configuration value — not hardcoded — enabling Legal to update it without a code deployment.
**Current placeholder:** "You have been unsubscribed from Southern Company Gas text messages. Reply START or call 1-800-XXX-XXXX to re-subscribe."
**Note:** Final wording requires Legal / Compliance sign-off before go-live (PD-004 pending).

#### REQ-009 / REQ-010: Message Volume Reporting
**Ambiguity — AMB-003:** PRD specifies reports on opted-in (REQ-009) and opted-out (REQ-010) message volumes, but does not specify whether these are standalone deliverables or components of the weekly compliance report (REQ-011). Jordan to confirm before spec decomposition.

#### REQ-014: Message Broker
**Gap — GAP-001:** How inbound SMS messages reach the TCPA API is not specified. The industry standard is a webhook callback from Cool Text / Twilio, but this needs architectural confirmation. Blocking for spec decomposition.

---

## Non-Functional Requirements

| ID      | Category     | Requirement                                  | Measurable Target                                                              | PRD Ref         |
|---------|--------------|----------------------------------------------|--------------------------------------------------------------------------------|-----------------|
| NFR-001 | Performance  | Opt-out acknowledgement latency              | P99: STOP receipt → confirmation sent ≤ 60 seconds                            | PRD §4, OOBR04  |
| NFR-002 | Performance  | Opt-out status update speed                  | 100% of STOP requests in opted-out status within 10 days (regulatory bound); actual processing near-real-time | PRD §4 |
| NFR-003 | Compliance   | Audit log retention                          | All opt-out and re-opt-in records queryable for 5 years post-event            | PRD §4, OOBR01  |
| NFR-004 | Reliability  | Opted-out number suppression                 | 0% delivery rate to opted-out cell phone numbers                               | PRD §4, OOBR06  |
| NFR-005 | Reporting    | Weekly compliance report delivery            | Delivered by email within 24 hours of week-end cutoff                         | PRD §4, RPBR03  |
| NFR-006 | Security     | API authentication                           | All endpoints require authentication; zero unauthenticated access             | PRD §4          |
| NFR-007 | Auditability | Production and debug logs                    | Logs capture: message send success/failure, opt-out events, re-opt-in events, full message flow | PRD §4 |
| NFR-008 | Scalability  | Message throughput                           | Steady state: ~1,000 msgs/day. Peak burst: up to 5,000 msgs/hour. P99 latency must not degrade below NFR-001 at peak. | PRD §4 |

## Constraints

| ID      | Type        | Description                                                                        | PRD Ref |
|---------|-------------|------------------------------------------------------------------------------------|---------|
| CON-001 | Regulatory  | System must be live and enforcing opt-outs before January 31, 2027                | PRD §5  |
| CON-002 | Technical   | Messaging platforms are Twilio and Cool Text only                                  | PRD §5  |
| CON-003 | Technical   | Only applications with Cool Text accounts configured in TCPA API are impacted     | PRD §5  |
| CON-004 | Scope       | Initial opt-in remains at application level — TCPA API does not manage opt-in    | PRD §5  |
| CON-005 | Scope       | Re-opt-in from STOP is manual — no automated re-opt-in                            | PRD §5  |
| CON-006 | Scope       | Application-level opt-outs do NOT propagate to TCPA API                           | PRD §5  |
| CON-007 | Geographic  | Covers four LDCs: Virginia Natural Gas, Chattanooga Gas, Nicor Gas, Atlanta Gas Light | PRD §5 |
| CON-008 | Technical   | Dialer IVR is excluded; IVR-originated texts bypass TCPA API                     | PRD §5  |

## Out of Scope

| ID      | Item                                                                           | PRD Ref |
|---------|--------------------------------------------------------------------------------|---------|
| OOS-001 | Initial opt-in to text messaging (remains at application level)                | PRD §6  |
| OOS-002 | Automated re-opt-in process                                                    | PRD §6  |
| OOS-003 | Campaign/program-specific text message creation                                | PRD §6  |
| OOS-004 | Solutions outside Southern Company Gas                                         | PRD §6  |
| OOS-005 | Emergency notifications                                                        | PRD §6  |
| OOS-006 | Vendor SMS on behalf of Southern Company Gas (ACI SpeedPay, Google Notifications) | PRD §6 |
| OOS-007 | Dialer IVR (Part 280 Shut Off, PURL, AGLC Atlanta IVR)                        | PRD §6  |
| OOS-008 | Multi-factor authentication                                                    | PRD §6  |
| OOS-009 | Application opt-out propagation to TCPA API                                   | PRD §6  |
| OOS-010 | CCB / My Account (Phase 2 — confirmed PD-007)                                  | PRD §6  |

## Assumptions

| ID      | Assumption                                                                                                  | Owner    | PRD Ref |
|---------|-------------------------------------------------------------------------------------------------------------|----------|---------|
| ASM-001 | Twilio and Cool Text are the only messaging platforms in use                                               | BRD      | PRD §8  |
| ASM-002 | All customers are globally opted-in until a STOP request is received via the TCPA API                      | BRD      | PRD §8  |
| ASM-003 | The 60-second SLA applies to TCPA API processing latency, not network delivery time                        | Agent 00 | PRD §8  |
| ASM-004 | "10 days" processing SLA is a regulatory outer bound; actual processing is near-real-time                  | Agent 00 | PRD §8  |
| ASM-005 | The system requires API authentication — not specified in BRD but implied by regulatory sensitivity         | Agent 00 | PRD §8  |
| ASM-006 | Weekly reporting = Monday–Sunday week, report generated Monday for prior week                               | Agent 00 | PRD §8  |
| ASM-007 | CCB/My Account is definitively out of Phase 1 scope (confirmed PD-007)                                     | Confirmed | PRD §8 |

## External Dependencies

| ID      | System/Team             | Nature of Dependency                                                | PRD Ref |
|---------|-------------------------|---------------------------------------------------------------------|---------|
| DEP-001 | Cool Text               | TCPA API routes all outbound messages through Cool Text accounts   | PRD §9  |
| DEP-002 | Twilio                  | Secondary messaging platform linked to Cool Text accounts          | PRD §9  |
| DEP-003 | BizTalk                 | In-scope application; must integrate with TCPA API                 | PRD §9  |
| DEP-004 | GCMA                    | In-scope application; must integrate with TCPA API                 | PRD §9  |
| DEP-005 | KMI                     | In-scope application; must integrate with TCPA API                 | PRD §9  |
| DEP-006 | ARM / Construction Portal | In-scope application; must integrate with TCPA API               | PRD §9  |
| DEP-007 | CCB / My Account        | Phase 2 dependency only — excluded from Phase 1                    | PRD §9  |
| DEP-008 | Help Desk system        | Manual re-opt-in requires Help Desk ticket workflow                | PRD §9  |
| DEP-009 | Legal / Compliance      | Must approve global opt-out message wording (PD-004 pending)       | PRD §9  |

## Ambiguities & Gaps

| ID      | Type      | Description                                                                                                                      | Blocking? | Status |
|---------|-----------|----------------------------------------------------------------------------------------------------------------------------------|-----------|--------|
| AMB-001 | AMBIGUOUS | REQ-002: How does the TCPA API identify which Gas application an inbound reply belongs to? Cool Text account mapping implied but routing mechanism not specified. | Yes | **Resolved** — `ReplyForwardingService` uses `callbackUrl` from `SystemConfig` to route replies to the originating application. |
| AMB-002 | AMBIGUOUS | REQ-007: Auth mechanism for admin re-opt-in API (API key, bearer token, OAuth) and key management process not specified.        | No        | **Resolved** — API key auth via `X-Admin-Key` header; keys stored in `ApiKeys:AdminKeys` config. |
| AMB-003 | AMBIGUOUS | REQ-009/010: Are opted-in/opted-out message volume reports standalone deliverables or components of the weekly compliance report (REQ-011)? Frequency not specified. | Yes | **Resolved** — All message events recorded in `AuditLog` (delivered, suppressed, failed). Full report generation deferred to `TCPA.ReportService` (Phase 1 Sprint 2). |
| GAP-001 | GAP       | REQ-014: Inbound SMS delivery mechanism to TCPA API (webhook callback vs. polling) not specified anywhere in PRD.               | Yes       | **Resolved** — Webhook callback from Cool Text to `POST /webhook/inbound`; TCPA.Api publishes to `inbound-messages` Kafka topic; `TCPA.MessageProcessor` consumes. |
| GAP-002 | GAP       | REQ-011: Email recipient list / distribution group for weekly compliance report not defined.                                     | No        | **Open** — `TCPA.ReportService` not yet built. Deferred to Sprint 2. Recipient list requires stakeholder input before implementation. |
| GAP-003 | GAP       | NFR-006: Authentication scheme for API endpoints not specified (API key, OAuth 2.0, etc.).                                      | No        | **Resolved** — API key auth implemented (`X-Api-Key` for webhook/outbound, `X-Admin-Key` for admin endpoints). |
| GAP-004 | GAP       | REQ-013: Cool Text account configuration management mechanism (static config vs. database) and ownership not defined.           | No        | **Resolved** — `SystemConfig` database table stores per-application Cool Text credentials; managed via EF Core migrations. |

## Requirements Summary
- Total functional requirements: 14
- Must Have: 14 | Should Have: 0 | Could Have: 0 | Won't Have: 0
- Non-functional requirements: 8
- Ambiguities resolved: 3/3 (AMB-001, AMB-002, AMB-003)
- Gaps resolved: 3/4 (GAP-001, GAP-003, GAP-004)
- **Open gap: 1** — GAP-002 (REQ-011 weekly email report recipients — deferred to TCPA.ReportService, Sprint 2)
