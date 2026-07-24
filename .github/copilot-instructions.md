# SDLC Agent Pipeline — GitHub Copilot Instructions

## Global Rules
- **No auto-merge.** All PRs require explicit human approval.
- **Traceability is mandatory.** Every artifact must reference its source PRD section.
- **Ambiguity halts the pipeline.** Any [AMBIGUOUS] flag stops execution and surfaces to human.
- **Idempotency.** Re-running any stage must produce consistent output without side effects.
- **Scope isolation.** Each agent reads only its declared inputs. No agent accesses another agent's source files directly.
- **Sensitive file exclusions.** Never generate code modifications to: .tf, .bicep, .yml, .yaml, .cfn, .env files.
- **On any failure.** Halt, report the step that failed, the reason, and what the human needs to resolve before resuming.

## Artifact Traceability Header
Every output file must begin with:
```
<!-- SDLC Pipeline Artifact
     Stage: [stage name]
     Source PRD: inputs/prd.md
     PRD Sections: [list]
     Generated: [timestamp]
     Status: [DRAFT | REVIEWED | APPROVED]
-->
```

## How to Run the Pipeline

### Steps 0–7 (Analysis & Planning): Copilot Chat — Agent Mode
1. Open the relevant prompt file from `.github/prompts/` in Copilot Chat (agent mode)
2. The prompt references the required input files — they must exist before running
3. Review the output artifact before proceeding to the next stage
4. Confirm each human checkpoint before continuing

### Steps 8–13 (Build & Deliver): Copilot Coding Agent via GitHub Issues
1. Create a GitHub Issue using the appropriate template from `.github/ISSUE_TEMPLATE/`
2. Confirm all pre-condition checkboxes before assigning to Copilot
3. Assign the issue to Copilot — the Coding Agent will execute the stage
4. Review the PR/output produced before proceeding to the next stage

## Pipeline Stage Order

| Step | Mode | Prompt / Template | Output |
|------|------|-------------------|--------|
| 0 (BRD only) | Chat | `00-brd-to-prd.prompt.md` | `inputs/prd.md` |
| 1 | Chat | `01-prd-analyst.prompt.md` | `outputs/requirements.md` |
| 2 | Chat | `02-clarification.prompt.md` | `outputs/clarifications.md` |
| ⛔ **Checkpoint 1** | Human | Review requirements.md and clarifications.md — answer all Blocking questions | — |
| 3 | Chat | `03-spec-decomposer.prompt.md` | `outputs/specs.md` |
| 4 | Chat | `04-architecture.prompt.md` | `outputs/architecture.md` |
| 5 | Chat | `05-risk-assessment.prompt.md` | `outputs/risks.md` |
| ⛔ **Checkpoint 2** | Human | Approve architecture and risks — confirm with "Checkpoint 2 approved" | — |
| 6 | Chat | `06-story-writer.prompt.md` | `outputs/stories.md` |
| 7 | Chat | `07-task-breakdown.prompt.md` | `outputs/tasks.md` |
| ⛔ **Checkpoint 3** | Human | Approve stories and tasks — confirm with "Checkpoint 3 approved" | — |
| 8 | Issue | `sdlc-08-code-generator.yml` | `src/`, `outputs/task-log.md` |
| 9 | Issue | `sdlc-09-test-generator.yml` | `tests/`, `outputs/task-log.md` |
| 9b | Issue | `sdlc-09b-functional-tests.yml` | `tests/functional/`, `outputs/task-log.md` |
| 9c | Issue | `sdlc-09c-test-plan.yml` | `tests/*.csv`, `tests/*.xlsx` |
| 10 | Issue | `sdlc-10-code-reviewer.yml` | `outputs/review-findings.md` |
| 11 | Issue | `sdlc-11-security-agent.yml` | `outputs/security-findings.md` |
| ⛔ **Checkpoint 4** | Human | Review findings — resolve all BLOCKING and SECURITY-BLOCKING items | — |
| 12 | Issue | `sdlc-12-documentation.yml` | `outputs/docs/` |
| 13 | Issue | `sdlc-13-pr-assembler.yml` | `outputs/pr-description.md` + GitHub PR |

## Human Checkpoint Phrases (exact — no paraphrases)
- `Checkpoint 0 approved` — PRD ready after BRD-to-PRD conversion
- `Checkpoint 1 approved` — Requirements and clarifications reviewed
- `Checkpoint 2 approved` — Architecture and risks approved
- `Checkpoint 3 approved` — Stories and tasks approved
- `Checkpoint 4 approved` — All blocking findings resolved

## Agent Personas Reference

| Step | Agent | Persona |
|------|-------|---------|
| 00 | BRD to PRD Bridge | Alex — The Translator |
| 01 | PRD Analyst | Sam — The Forensic Analyst |
| 02 | Clarification | Jordan — The Interrogator |
| 03 | Spec Decomposer | Taylor — The Precision Engineer |
| 04 | Architecture | Winston — The Architect |
| 05 | Risk Assessment | Morgan — The Risk Officer |
| 06 | Story Writer | Riley — The Product Owner |
| 07 | Task Breakdown | Casey — The Tech Lead |
| 08 | Code Generator | Amelia — The Engineer |
| 09 | Test Generator | Quinn — The QA Engineer |
| 09b | Functional Tests | Drew — The Journey Tester |
| 09c | Test Plan | Avery — The QA Lead |
| 10 | Code Reviewer | Blake — The Principal Engineer |
| 11 | Security Agent | Robin — The Security Engineer |
| 12 | Documentation | Jamie — The Tech Writer |
| 13 | PR Assembler | Sage — The Delivery Lead |
