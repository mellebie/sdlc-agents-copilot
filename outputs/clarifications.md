<!-- SDLC Pipeline Artifact
     Stage: 02-clarification
     Source PRD: inputs/prd.md
     PRD Sections: All sections reviewed
     Generated: 2026-07-23
     Status: APPROVED — All blocking questions answered in interactive session with Mark Ellebie
-->

# Clarifications — TCPA Regulatory Compliance API

## Summary
- Blocking questions: 4 / 4 resolved
- Important questions: 5 / 5 resolved
- Nice to have questions: 2 / 2 resolved
- Total: 11
- Conflicts identified: 0

---

## Blocking Questions

### CQ-001 [Blocking] — Source: AMB-001
- **Source:** REQ-002, REQ-013
- **Question:** When an inbound customer reply arrives via Cool Text / Twilio, what identifies which Gas application it belongs to? Is there a one-to-one mapping between Cool Text accounts and applications?
- **Why it blocks:** The routing logic for inbound message forwarding (REQ-002) cannot be specced without knowing the account-to-application mapping model.
- **Answer:** One-to-one mapping. Each in-scope Gas application has exactly one Cool Text account. The TCPA API routes inbound messages to the correct application by looking up the Cool Text account number on the inbound message.

### CQ-002 [Blocking] — Source: AMB-003
- **Source:** REQ-009, REQ-010, REQ-011
- **Question:** Are REQ-009 (opted-in message volume report) and REQ-010 (opted-out message volume report) separate deliverables from the weekly compliance report (REQ-011), or sections within it?
- **Why it blocks:** Without this, Taylor cannot spec the reporting domain correctly — one report vs. three distinct outputs drives different data models and scheduling requirements.
- **Answer:** Separate weekly reports. REQ-009 and REQ-010 are distinct report outputs, each generated weekly. REQ-011 is the overarching weekly compliance report. All three are generated on the same weekly schedule (Monday for prior Mon–Sun week, US Eastern).

### CQ-003 [Blocking] — Source: GAP-001
- **Source:** REQ-014
- **Question:** How do inbound SMS messages from customers reach the TCPA API from Cool Text / Twilio — webhook callback push, or TCPA API polling?
- **Why it blocks:** This determines whether the TCPA API is event-driven or polling-based, which is a foundational architectural decision.
- **Answer:** Webhook callback. Cool Text / Twilio pushes inbound messages to a TCPA API callback endpoint. No specific URL pattern prescribed — standard webhook registration with the provider.

### CQ-004 [Blocking] — Jordan-identified
- **Source:** NFR-004 (0% delivery to opted-out numbers), REQ-006
- **Question:** If the TCPA API is unavailable when an in-scope application attempts to send a message, should the application fail-safe (block the send) or fail-open (send anyway)?
- **Why it blocks:** Fail-open violates TCPA and contradicts NFR-004. This decision drives availability requirements and the integration contract for all in-scope applications.
- **Answer:** Fail-safe. If the TCPA API is unreachable, in-scope applications must block the send. The TCPA API availability becomes a hard dependency for all outbound message flows. Architecture must address high-availability accordingly.

---

## Important Questions

### CQ-005 [Important] — Source: AMB-002, GAP-003
- **Source:** REQ-007, NFR-006
- **Question:** What authentication mechanism should the TCPA API use?
- **Suggested Default:** API key for admin/internal endpoints; bearer token for application-facing endpoints.
- **Answer:** API key for all endpoints. Simple, appropriate for trusted internal callers (Gas applications and Help Desk tooling).

### CQ-006 [Important] — Jordan-identified
- **Source:** REQ-004, REQ-006, NFR-004
- **Question:** Should opt-out status be checked at message queue time, at send time, or both?
- **Suggested Default:** Both — queue time for fast rejection; send time as safety net.
- **Answer:** Both. Queue-time check gives the calling application an immediate rejection if the number is opted out. Send-time check is the safety net before dispatch to Cool Text / Twilio. Both checks are required.

### CQ-007 [Important] — Jordan-identified
- **Source:** REQ-004, REQ-006, NFR-004
- **Question:** Is a message queued before a STOP but dispatched after the opt-out is written considered a TCPA violation?
- **Suggested Default:** Accepted edge case with audit log entry.
- **Answer:** Accepted edge case. A message in flight at the moment a STOP is received is not treated as a violation, provided suppression is enforced from opt-out confirmation forward. The system must write an audit log entry for every such occurrence.

### CQ-008 [Important] — Source: GAP-004
- **Source:** REQ-013
- **Question:** Should Cool Text account configuration be stored in a database table or a config file / environment variable?
- **Suggested Default:** Database table — new application onboarding should not require a deployment.
- **Answer:** Database table. New application onboarding (adding a Cool Text account) is a data operation, not a deployment. The table must be manageable via the admin API or a migration script.

### CQ-009 [Important] — Source: GAP-002
- **Source:** REQ-011
- **Question:** Is the email recipient list for the weekly compliance report a static distribution group, a team inbox, or configurable in the system?
- **Suggested Default:** Configurable list in system config.
- **Answer:** Configurable distribution list stored in system configuration. Recipients can be updated without a code change.

---

## Nice to Have Questions

### CQ-010 [Nice to Have] — Jordan-identified
- **Source:** REQ-011, NFR-005
- **Question:** What timezone should the weekly compliance report schedule use?
- **Answer:** US Eastern (EST/EDT). Report covers Monday–Sunday; generated Monday morning for the prior week.

### CQ-011 [Nice to Have] — Jordan-identified
- **Source:** REQ-007
- **Question:** Should the admin re-opt-in endpoint have rate limiting?
- **Answer:** 10 requests per minute per API key. Appropriate for Help Desk tool usage patterns; prevents accidental bulk calls.

---

## Conflicts Identified

| ID | Requirement A | Requirement B | Nature of Conflict |
|----|---------------|---------------|-------------------|
| — | — | — | None identified |

---

## Sign-off
All Blocking and Important questions answered in interactive session on 2026-07-23.
Status: APPROVED — pipeline clear to proceed to Stage 3 (Taylor).
