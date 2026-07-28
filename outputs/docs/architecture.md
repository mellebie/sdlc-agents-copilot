<!-- SDLC Pipeline Artifact
     Stage: 12-documentation
     Source PRD: inputs/prd.md
     PRD Sections: [all]
     Generated: 2026-07-28
     Status: DRAFT
-->

# Runtime Architecture (Implemented)

## Implemented Components
- Intake API (`src/IntakeApi`)
- Intent Classifier (`src/IntentClassifier`)
- Consent Service models/services embedded into API flow (`src/ConsentService`)

## Implemented Request Flow
1. Inbound request enters `POST /api/v1/inbound/messages`.
2. `InboundMessageRequestValidator` validates payload.
3. `RoutingEligibilityService` and `ScopeMappingResolver` evaluate in-scope mapping.
4. STOP/HELP/OTHER intent behavior is handled by classifier and forwarding services.
5. STOP transitions can be processed by `ConsentTransitionService` with escalation and failure-alert hooks.
6. Outbound send gating is handled by `POST /api/v1/enforcement/decisions`.
7. Re-opt-in transitions are handled by `POST /api/v1/consent/reoptin` with authorization and replay checks.

## Security Controls Currently Implemented
- API request gate requiring `X-Service-Auth` header.
- Re-opt-in authorization proof presence check.
- Replay detection using request ID with in-memory TTL window.

## Known Gaps (Code-Truth)
- Service authentication is header presence based, not cryptographic token verification.
- Consent/state and replay stores are in-memory, not durable shared stores.
- Confirmation orchestrator, immutable audit service, and reporting service are not delivered in this run.

## Codebase Navigation
- Controllers: `src/IntakeApi/Controllers`
- Request/response contracts: `src/IntakeApi/Contracts`
- Intake/enforcement services: `src/IntakeApi/Services`
- Consent transition + re-opt-in logic: `src/ConsentService/Services`
- Security utilities: `src/ConsentService/Security`
- Test coverage: `tests/IntakeApi.Tests`

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
