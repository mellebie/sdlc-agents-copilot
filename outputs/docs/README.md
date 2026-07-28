<!-- SDLC Pipeline Artifact
     Stage: 12-documentation
     Source PRD: inputs/prd.md
     PRD Sections: [all]
     Generated: 2026-07-28
     Status: DRAFT
-->

# Delivery Documentation

This documentation set reflects implemented behavior in the current codebase and test suite.

## Quickstart (5 steps)
1. Build the solution:
     - `dotnet build sldc-agents-copilot.sln`
2. Run tests:
     - `dotnet test sldc-agents-copilot.sln`
3. Run strict pipeline eval:
     - `./scripts/Invoke-PipelineEval-AutoGate.ps1`
     - This run includes automatic stage-to-rubric selection and rubric scoring where rubric files exist.
     - Add `-EnforceRubricGate` only when rubric failures should fail the overall pipeline gate.
4. Run the API locally (from `src/IntakeApi`):
     - `dotnet run`
5. Call endpoints with required headers:
     - `X-Service-Auth` for all endpoints
     - `X-ReOptIn-Proof` and `X-Request-Nonce` for re-opt-in

## Documentation Index
- `outputs/docs/api.md` — implemented endpoint contracts and examples
- `outputs/docs/architecture.md` — implemented runtime component behavior
- `outputs/docs/operations.md` — operational commands, failure modes, and runtime controls
- `outputs/docs/CHANGELOG.md` — this run's delivery changes (Added/Changed/Fixed/Security)

## Pipeline Flow Diagrams
- Detailed engineering view: `outputs/docs/pipeline-flow.mmd`
- Executive storyboard view: `outputs/docs/pipeline-flow-executive.mmd`

## Eval Orchestration Notes
- `scripts/Invoke-PipelineEval.ps1` performs deterministic checks plus automatic rubric orchestration.
- Rubric results are written into `outputs/eval-summary.md` and timestamped `outputs/eval-report-*.md` artifacts.
- Rubric gating is advisory by default and can be enforced explicitly with `-EnforceRubricGate`.

## Implemented Endpoint Count
- 3 endpoints documented
  - `POST /api/v1/inbound/messages`
  - `POST /api/v1/enforcement/decisions`
  - `POST /api/v1/consent/reoptin`

## Spec-to-Code Divergence Notes
- Spec components pending implementation in this run:
  - SPEC-006 (confirmation orchestrator)
  - SPEC-008 (immutable audit service)
  - SPEC-009 (reporting service)

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
