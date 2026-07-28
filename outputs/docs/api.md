<!-- SDLC Pipeline Artifact
     Stage: 12-documentation
     Source PRD: inputs/prd.md
     PRD Sections: [all]
     Generated: 2026-07-28
     Status: DRAFT
-->

# API Documentation

## Authentication
All API endpoints currently require a non-empty `X-Service-Auth` header.

## Endpoints Implemented

### POST /api/v1/inbound/messages
- Purpose: Validate and accept inbound SMS messages for classification.
- Request:
```json
{
  "eventId": "evt-001",
  "receivedAtUtc": "2026-07-28T18:00:00Z",
  "customerPhoneNumber": "+14045550100",
  "sourceLdc": "Vng",
  "sourceApplication": "BizTalk",
  "coolTextAccountId": "acct-001",
  "messageText": "STOP"
}
```
- Success 200:
```json
{
  "accepted": true,
  "classificationState": "PENDING",
  "correlationId": "4b9aef6f8f5f4c77a7f2dc0583f1bd52"
}
```
- Failure responses:
  - 400 `INVALID_INPUT`
  - 401 `UNAUTHORIZED`
  - 404 `SCOPE_MAPPING_NOT_FOUND`

### POST /api/v1/enforcement/decisions
- Purpose: Return outbound decision (`ALLOW` or `BLOCK`) and reason before send.
- Request:
```json
{
  "outboundRequestId": "out-1",
  "customerPhoneNumber": "+14045550100",
  "sourceApplication": "BizTalk",
  "sourceLdc": "Vng",
  "applicationReportedStatus": "OptIn"
}
```
- Success 200:
```json
{
  "enforcementDecision": "ALLOW",
  "decisionReason": "APP_STATUS_TAKES_PRECEDENCE",
  "decisionTimestampUtc": "2026-07-28T18:00:00Z",
  "correlationId": "b71d1a9d0f5f42ff8f20ec84f0e17f4f"
}
```
- Failure responses:
  - 400 `INVALID_OUTBOUND_REQUEST`
  - 401 `UNAUTHORIZED`
  - 404 `OUT_OF_SCOPE`
  - 500 `ENFORCEMENT_UNAVAILABLE`

### POST /api/v1/consent/reoptin
- Purpose: Process re-opt-in transition for FORM or SMS_RESPONSE channels.
- Required headers:
  - `X-Service-Auth`
  - `X-ReOptIn-Proof`
  - `X-Request-Nonce`
- Request:
```json
{
  "reOptInRequestId": "reopt-1",
  "customerPhoneNumber": "+14045550155",
  "initiationChannel": "Form",
  "initiatedAtUtc": "2026-07-28T18:00:00Z"
}
```
- Success 200:
```json
{
  "updatedConsentStatus": "OPT-IN",
  "updateResult": "UPDATED",
  "updateTimestampUtc": "2026-07-28T18:00:00Z",
  "correlationId": "ee694518941a4f6d8389b031ca3bd44f"
}
```
- Failure responses:
  - 400 `INVALID_REOPTIN_REQUEST` or `INVALID_REOPTIN_CHANNEL`
  - 401 `REOPTIN_NOT_AUTHORIZED` or `REPLAY_DETECTED`

## Implemented Behavior vs Spec Notes
- Implemented endpoint behavior is documented from current code in `src/IntakeApi/Controllers`.
- SPEC-006, SPEC-008, and SPEC-009 components are not yet implemented and therefore have no API surface in this run.

## Context Standards Applied
- `context/standards/documentation-standards.md`
- `context/standards/security-standards.md`

## Context Divergences
- None.

---

> **AI Pipeline Disclosure**  
> This document was produced by an AI pipeline (GitHub Copilot Chat, Agent Mode) with human checkpoint review.  
> Pipeline version: 1.0 | Prompt version: 1.0  
> Accountable reviewer: _[to be named at checkpoint approval]_ | Review date: _[to be filled at approval]_
