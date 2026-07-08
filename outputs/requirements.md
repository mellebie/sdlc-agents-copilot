<!-- SDLC Pipeline Artifact
     Stage: 01-prd-analyst
     Source PRD: inputs/prd.md
     PRD Sections: §1 Overview, §2 Personas, §3 Functional Requirements, §4 Non-Functional Requirements, §5 Constraints, §6 Out of Scope, §7 Success Metrics, §8 Assumptions, §9 Dependencies, §10 Product Decisions Required
     Generated: 2026-06-26
     Status: APPROVED — human approved proceeding despite open clarifications (2026-06-26)
-->

# Requirements — TCPA Regulatory Compliance for Text Messages

## Product Vision
The TCPA API is a centralized middleware filtering layer that sits between Southern Company Gas (SCG) applications and the Cool Text/Twilio SMS platform. It enforces opt-out compliance with TCPA Section 64.1200 by intercepting all outbound SMS and blocking delivery to any cell number that has submitted a STOP request, regardless of which SCG application originated the message. The system must be live and enforcing by January 31, 2027.

## Goals

| ID     | Goal                                                                                 | Success Metric                                                                       | PRD Ref    |
|--------|--------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------|------------|
| GOAL-1 | Achieve TCPA compliance for all outbound SMS by the Jan 31, 2027 regulatory deadline | Zero SMS messages delivered to opted-out cell numbers through the TCPA API           | PRD §1     |
| GOAL-2 | Centralize opt-out enforcement across all in-scope SCG applications                  | 100% of in-scope application text messages routed through TCPA API                   | PRD §1     |
| GOAL-3 | Provide a compliance audit trail and automated reporting                             | Audit log retained 5 years; weekly compliance reports generated with zero manual intervention | PRD §1 |
| GOAL-4 | Reduce consumer opt-out processing time to within TCPA-mandated limits               | Opt-out acknowledgement ≤ 60 seconds; opt-out effective within 10 days               | PRD §1     |

## Personas

| ID      | Persona Name          | Description                                                                 | Primary Needs                                                                  | PRD Ref  |
|---------|-----------------------|-----------------------------------------------------------------------------|--------------------------------------------------------------------------------|----------|
| PER-001 | Gas Customer          | SCG consumer who receives SMS alerts from one or more SCG applications      | Reliable opt-out that stops all SCG texts across all applications; clear confirmation of opt-out | PRD §2 |
| PER-002 | Application System    | Upstream systems (BizTalk, GCMA, KMI, ARM, CCB/My Account) that send SMS   | Authoritative opt-in/opt-out status lookup before sending; receipt of inbound customer replies | PRD §2 |
| PER-003 | Compliance Officer    | Customer Operations, Legal, and Internal Auditing staff                     | Audit evidence of opt-out compliance; visibility into enforcement failures; automated reports | PRD §2 |
| PER-004 | Help Desk Agent       | Resource Management staff processing re-opt-in requests                     | Ability to manually re-opt-in a cell number for a customer who requests it     | PRD §2   |
| PER-005 | IT / Platform Engineer| Gas Technology Solutions Delivery and DTS engineers                         | Production/debug logs for diagnosing API behavior and failures                 | PRD §2   |

## Functional Requirements

| ID      | Requirement                                                                                                                         | Priority (MoSCoW) | PRD Ref    | Flags                                                                 |
|---------|-------------------------------------------------------------------------------------------------------------------------------------|-------------------|------------|-----------------------------------------------------------------------|
| REQ-001 | The system must act as a middleware proxy between in-scope SCG applications and Cool Text/Twilio for all outbound and inbound SMS    | Must              | PRD §3     |                                                                       |
| REQ-002 | The system must receive inbound SMS replies from customers (via Cool Text) and forward them unchanged to the originating application | Must              | PRD §3     |                                                                       |
| REQ-003 | The system must detect any of the 7 TCPA opt-out keywords (STOP, QUIT, END, REVOKE, OPT-OUT, CANCEL, UNSUBSCRIBE) in inbound SMS, case-insensitive | Must | PRD §3 | [AMBIGUOUS: PRD §3 REQ-003 detail notes "parse for any occurrence" implying substring match, but PD-005 explicitly flags this as unresolved — full-body exact match vs. substring is a blocking open question] |
| REQ-004 | The system must set a cell phone number's opt-out status to OPT-OUT in the TCPA API database upon detecting an opt-out keyword      | Must              | PRD §3     |                                                                       |
| REQ-005 | The system must send an opt-out acknowledgement confirmation SMS to the customer's cell number within 60 seconds of opt-out keyword detection | Must       | PRD §3     |                                                                       |
| REQ-006 | Opted-out cell numbers must receive no further SMS within 10 calendar days of the opt-out request                                   | Must              | PRD §3     |                                                                       |
| REQ-007 | The system must send one standardized global opt-out confirmation SMS to the opted-out cell number, informing them they are opted out of ALL SCG applications and providing a phone number to re-opt in | Must | PRD §3 | [AMBIGUOUS: exact message text and content owner are unresolved — PD-002 is a blocking product decision; requirement cannot be fully implemented without approved message content] |
| REQ-008 | The system must block (suppress and not deliver) outbound SMS to any cell number with OPT-OUT status before forwarding to Cool Text/Twilio | Must          | PRD §3     |                                                                       |
| REQ-009 | The system must write an audit log entry for every opt-out request processed, including: date/time stamp, originating application, cell phone number, opt-out keyword received, system response, and any TCPA-required fields | Must | PRD §3 | [AMBIGUOUS: "other info as required by TCPA" is undefined — PD-009 is a blocking product decision; the full required field set for the audit log is not specified] |
| REQ-010 | The system must also log every blocked outbound SMS attempt (cell number, application, date/time, message content) in the audit log  | Must              | PRD §3     |                                                                       |
| REQ-011 | Audit log data must be retained for a minimum of 5 years from the event date                                                        | Must              | PRD §3     |                                                                       |
| REQ-012 | The system must support manually updating a cell phone number's opt-out status back to OPT-IN (re-opt-in)                          | Must              | PRD §3     | [AMBIGUOUS: exact mechanism for re-opt-in (Help Desk UI, admin API endpoint, database operation) is unresolved — PD-003 is a blocking product decision] |
| REQ-013 | The system must produce a report of all SMS messages sent to opted-in cell numbers (status, cell number, application, date/time, message content) | Must  | PRD §3     |                                                                       |
| REQ-014 | The system must produce a report of all SMS messages attempted to opted-out cell numbers (status, cell number, application, date/time, message content) | Must | PRD §3 |                                                                    |
| REQ-015 | The system must automatically generate weekly compliance reports including: alerts for messages sent to opted-out numbers, alerts for messages sent to opted-in numbers, and opt-out success rate (compliance KPI) | Must | PRD §3 | [AMBIGUOUS: report recipients, format (email/dashboard/file), and delivery schedule are unresolved — PD-004 is a blocking product decision] |
| REQ-016 | The system must store and use the Cool Text account number for each in-scope application data flow to route messages correctly       | Must              | PRD §3     |                                                                       |
| REQ-017 | The system must produce structured production and debug logs containing details of all API actions, successes, failures, and behaviors, accessible to IT | Must | PRD §3 |                                                               |
| REQ-018 | Applications not registered in the TCPA API (no Cool Text account configured) must not be affected by the TCPA API                 | Must              | PRD §3     |                                                                       |

## Non-Functional Requirements

| ID      | Category       | Requirement                                                           | Measurable Target                                                                                 | PRD Ref  |
|---------|----------------|-----------------------------------------------------------------------|---------------------------------------------------------------------------------------------------|----------|
| NFR-001 | Compliance     | Opt-out acknowledgement timing                                        | Confirmation SMS sent ≤ 60 seconds from opt-out keyword receipt                                   | PRD §4   |
| NFR-002 | Compliance     | Opt-out enforcement timing                                            | Opted-out cell number receives no further SMS within 10 calendar days of opt-out request          | PRD §4   |
| NFR-003 | Compliance     | TCPA regulatory deadline                                              | System live and enforcing opt-out rules in production by January 31, 2027                         | PRD §4   |
| NFR-004 | Data Retention | Audit log retention                                                   | Audit log data retained for minimum 5 years from event date                                       | PRD §4   |
| NFR-005 | Reliability    | Fail-safe behavior when TCPA API is unavailable                       | [AMBIGUOUS: fail-closed vs. fail-open behavior not specified — PD-006 is a blocking product decision; no measurable target can be set until resolved] | PRD §4 |
| NFR-006 | Performance    | Inbound keyword detection and opt-out status write latency            | Opt-out keyword detection and status write ≤ 5 seconds from message receipt [ASSUMED — no explicit target beyond the 60s confirmation SLA in the PRD] | PRD §4 |
| NFR-007 | Security       | Cell phone number (PII) protection in transit and at rest             | All cell phone numbers encrypted in the database and in API transit using HTTPS/TLS 1.2 or higher [ASSUMED — not explicitly stated in PRD but implied by regulatory and data policy context] | PRD §4 |
| NFR-008 | Auditability   | Audit log completeness                                                | 100% of opt-out requests produce a corresponding audit log entry; no silent failures              | PRD §4   |
| NFR-009 | Scalability    | Concurrent load from all in-scope applications                        | [AMBIGUOUS: message volume per application is not specified — PD-007 is a blocking product decision; no measurable target can be set until volume estimates are provided] | PRD §4 |
| NFR-010 | Observability  | Structured production and debug logging                               | All API actions, successes, failures, and behaviors logged in structured format accessible to IT  | PRD §4   |
| NFR-011 | Availability   | TCPA API uptime SLA                                                   | [AMBIGUOUS: target availability SLA is not specified in the PRD — PD-008 is a blocking product decision] | PRD §4 |

## Constraints

| ID      | Type        | Description                                                                                                | PRD Ref  |
|---------|-------------|------------------------------------------------------------------------------------------------------------|----------|
| CON-001 | Regulatory  | Must comply with TCPA Section 64.1200 effective January 31, 2027                                           | PRD §5   |
| CON-002 | Regulatory  | Must comply with CTIA guidelines for SMS opt-out handling                                                   | PRD §5   |
| CON-003 | Technical   | Messaging platform is exclusively Cool Text and Twilio; no other SMS platform is in scope                  | PRD §5   |
| CON-004 | Technical   | TCPA API must not modify opt-out status at the application level; it manages only its own centralized database | PRD §5 |
| CON-005 | Scope       | Initial customer opt-in remains at the application level; TCPA API does not manage opt-in                  | PRD §5   |
| CON-006 | Scope       | Vendor SMS (ACI SpeedPay, Google Notifications) is out of scope                                             | PRD §5   |
| CON-007 | Scope       | IVR Dialer SMS is out of scope; it may send SMS regardless of TCPA API opt-out status                      | PRD §5   |
| CON-008 | Scope       | Multi-factor authentication SMS is out of scope                                                             | PRD §5   |
| CON-009 | Geographic  | Must cover all four SCG LDCs: Virginia Natural Gas, Chattanooga Gas Company, Nicor Gas, Atlanta Gas Light  | PRD §5   |
| CON-010 | Business    | Re-opt-in is a manual process only; no automated re-opt-in triggered by application-level opt-in actions   | PRD §5   |

## Out of Scope

| ID      | Item                                                                                                  | PRD Ref  |
|---------|-------------------------------------------------------------------------------------------------------|----------|
| OOS-001 | Initial customer opt-in to text messaging (remains at application level)                              | PRD §6   |
| OOS-002 | Automated re-opt-in after opt-out                                                                     | PRD §6   |
| OOS-003 | Program/campaign-specific message creation by the TCPA API                                            | PRD §6   |
| OOS-004 | Solutions for entities outside Southern Company Gas                                                   | PRD §6   |
| OOS-005 | Emergency SMS notifications                                                                           | PRD §6   |
| OOS-006 | Vendor-managed SMS (ACI SpeedPay Twilio, Google Notifications)                                        | PRD §6   |
| OOS-007 | IVR Dialer SMS (IVR Part 280 Shut Off Notice, PURL Request, AGLC Atlanta IVR Marketing List URL)     | PRD §6   |
| OOS-008 | Multi-factor authentication SMS                                                                       | PRD §6   |
| OOS-009 | Application-level opt-out propagating to TCPA API (only SMS STOP keyword triggers TCPA API opt-out)  | PRD §6   |
| OOS-010 | Opt-out status push from TCPA API back to individual applications (removed from Phase 1)              | PRD §6   |

## Assumptions

| ID      | Assumption                                                                                                             | Owner              | PRD Ref  |
|---------|------------------------------------------------------------------------------------------------------------------------|--------------------|----------|
| ASM-001 | Twilio and Cool Text are the only messaging platforms currently in use by Southern Company Gas                         | Confirmed — Prashant Pathak (2/19/2026) | PRD §8 |
| ASM-002 | All customers are globally opted-in at the TCPA API level by default until a STOP keyword is received via SMS          | Confirmed — Prashant Pathak (2/19/2026) | PRD §8 |
| ASM-003 | Customer opt-in occurs at the application level; no SMS is sent until the customer opts in via the application         | Product / BRD      | PRD §8   |
| ASM-004 | CCB/My Account integration is included in this release given a potential Q2 2026 go-live                               | Product / BRD      | PRD §8   |
| ASM-005 | ARM/Construction Portal (GlanceAndSee campaign) is already live as of February 2026 and sending texts                 | Confirmed — BRD    | PRD §8   |
| ASM-006 | Cool Text account numbers for all in-scope applications are available and will be provided via configuration (not user-entered) | IT / Platform Engineer | PRD §8 |
| ASM-007 | The TCPA API is a new standalone service, not a modification to any existing application                               | Architect          | PRD §8   |
| ASM-008 | The 10-day opt-out window is a regulatory maximum; opt-out must be enforced immediately upon status write              | Legal / Compliance | PRD §8   |

## External Dependencies

| ID      | System/Team                  | Nature of Dependency                                                            | PRD Ref  |
|---------|------------------------------|---------------------------------------------------------------------------------|----------|
| DEP-001 | Cool Text platform           | TCPA API must integrate with Cool Text for inbound and outbound SMS routing     | PRD §9   |
| DEP-002 | Twilio platform              | Part of the messaging infrastructure; interaction model TBD                     | PRD §9   |
| DEP-003 | BizTalk                      | Must route all outbound SMS through TCPA API before go-live                     | PRD §9   |
| DEP-004 | GCMA                         | Must route all outbound SMS through TCPA API before go-live                     | PRD §9   |
| DEP-005 | KMI Active                   | Must route all outbound SMS through TCPA API before go-live                     | PRD §9   |
| DEP-006 | ARM / Construction Portal    | Must route all outbound SMS through TCPA API; already live and sending texts    | PRD §9   |
| DEP-007 | CCB / My Account             | Integration targeted for this release (Q2 2026 go-live); inclusion TBD         | PRD §9   |
| DEP-008 | Help Desk ticketing system   | Manual re-opt-in process requires a defined Help Desk workflow and tooling      | PRD §9   |
| DEP-009 | TCPA Compliance / Legal team | Must review and approve global opt-out confirmation message content             | PRD §9   |

## Ambiguities & Gaps

| ID      | Type      | Description                                                                                                                           | Blocking? |
|---------|-----------|---------------------------------------------------------------------------------------------------------------------------------------|-----------|
| AMB-001 | AMBIGUOUS | REQ-003: Opt-out keyword matching scope — full-message exact match vs. substring/partial match not specified (PD-005 in PRD)          | Yes       |
| AMB-002 | AMBIGUOUS | REQ-007: Exact text and content owner for the global opt-out confirmation SMS not defined (PD-002 in PRD)                             | Yes       |
| AMB-003 | AMBIGUOUS | REQ-012: Mechanism for manual re-opt-in (Help Desk UI, admin API, database operation) not specified (PD-003 in PRD)                   | Yes       |
| AMB-004 | AMBIGUOUS | REQ-015: Weekly compliance report recipients, format, and delivery schedule not specified (PD-004 in PRD)                             | Yes       |
| AMB-005 | AMBIGUOUS | NFR-005: Fail-closed vs. fail-open behavior when TCPA API is unavailable not addressed (PD-006 in PRD)                               | Yes       |
| AMB-006 | AMBIGUOUS | NFR-009: Message volume per application not provided; system cannot be sized without this (PD-007 in PRD)                             | Yes       |
| AMB-007 | AMBIGUOUS | NFR-011: Target availability SLA (e.g., 99.9% uptime) not specified in PRD (PD-008 in PRD)                                          | Yes       |
| AMB-008 | AMBIGUOUS | REQ-009: "Other info as required by TCPA" audit log fields are undefined; full required field set for audit log not specified (PD-009 in PRD) | Yes |
| AMB-009 | AMBIGUOUS | NFR-006: Inbound keyword detection latency target is assumed (≤ 5 seconds) — PRD does not state an explicit target beyond the 60s confirmation SLA | No |
| AMB-010 | AMBIGUOUS | NFR-007: PII encryption requirements are assumed (TLS 1.2+, encryption at rest) — not explicitly stated in PRD                       | No        |
| GAP-001 | GAP       | No requirement for a mechanism to query a cell number's current opt-out status — PD-001 (real-time lookup by call center/help desk agents) was removed from scope but the Help Desk re-opt-in workflow (REQ-012) implicitly requires some form of status lookup before or after updating | No |
| GAP-002 | GAP       | No disaster recovery or backup requirements defined for the TCPA API database containing opt-out records                              | No        |
| GAP-003 | GAP       | No requirement for how the TCPA API is notified of new applications being onboarded (Cool Text account registration process)          | No        |
| GAP-004 | GAP       | No rate limiting or anti-abuse requirements defined for the API (e.g., to prevent flood attacks that could exhaust opt-out confirmations) | No     |
| GAP-005 | GAP       | CCB/My Account integration status (included or deferred) is conditional on Q2 2026 go-live — no explicit decision is recorded in the PRD, leaving integration scope ambiguous | No |

## Requirements Summary
- Total functional requirements: 18
- Must Have: 18 | Should Have: 0 | Could Have: 0 | Won't Have: 0
- Ambiguities requiring resolution: 10
- Gaps identified: 5
