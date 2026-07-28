<!-- SDLC Pipeline Artifact
     Stage: 12-documentation
     Source PRD: inputs/prd.md
     PRD Sections: [all]
     Generated: 2026-07-28
     Status: DRAFT
-->

# Operations Guide

## Runtime Requirements
- .NET 8 SDK
- PowerShell (for pipeline scripts)

## Configuration
No required environment variables are currently enforced by code paths in this run.

### Request-Time Headers Required
- `X-Service-Auth`: required for all API endpoints.
- `X-ReOptIn-Proof`: required for re-opt-in authorization checks.
- `X-Request-Nonce`: used in re-opt-in replay controls.

## Build and Test
- Build:
  - `dotnet build sldc-agents-copilot.sln`
- Test:
  - `dotnet test sldc-agents-copilot.sln`

## Pipeline Validation
- Strict eval:
  - `./scripts/Invoke-PipelineEval-AutoGate.ps1`

## Known Failure Modes
- Missing `X-Service-Auth` returns `401 UNAUTHORIZED`.
- Invalid or missing re-opt-in proof returns `401 REOPTIN_NOT_AUTHORIZED`.
- Duplicate re-opt-in request ID within replay window returns `401 REPLAY_DETECTED`.
- Consent lookup guarded failure returns `500 ENFORCEMENT_UNAVAILABLE`.

## Operational Gaps
- [TODO: Replace header-presence auth gate with cryptographically verified service identity.]
- [TODO: Replace in-memory consent/replay stores with durable shared data stores.]

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
