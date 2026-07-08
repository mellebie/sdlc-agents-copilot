# AI-First SDLC Pipeline — Claude Code

A markdown-driven, multi-agent SDLC pipeline that transforms a BRD or PRD
into delivered, tested, documented code using Claude Code.

---

## Pipeline Overview

```
inputs/brd.md (if starting from a BRD)
     │
     ▼
[00] BRD to PRD Bridge    → inputs/prd.md
     │
     ▼ ⛔ HUMAN CHECKPOINT 0 (PRD review — BRD path only)
     │
inputs/prd.md (start here if you already have a PRD)
     │
     ▼
[01] PRD Analyst          → outputs/requirements.md
[02] Clarification        → outputs/clarifications.md
     │
     ▼ ⛔ HUMAN CHECKPOINT 1 (requirements review)
     │
[03] Spec Decomposer      → outputs/specs.md
[04] Architecture         → outputs/architecture.md
[05] Risk Assessment      → outputs/risks.md
     │
     ▼ ⛔ HUMAN CHECKPOINT 2 (architecture review)
     │
[06] Story Writer         → outputs/stories.md
[07] Task Breakdown       → outputs/tasks.md
     │
     ▼ ⛔ HUMAN CHECKPOINT 3 (backlog review)
     │
[08] Code Generator       → src/
[09] Unit & Integration Tests → tests/
[09b] Functional & E2E Tests  → tests/functional/
[09c] Test Plan Generator     → tests/[Product]-Test-Plan.xlsx
[10] Code Reviewer        → outputs/review-findings.md
[11] Security Agent       → outputs/security-findings.md
     │
     ▼ ⛔ HUMAN CHECKPOINT 4 (findings review)
     │
[12] Documentation        → outputs/docs/
[13] PR Assembler         → outputs/pr-description.md
     │
     ▼ ⛔ HUMAN PR APPROVAL (no auto-merge)
```

---

## Quick Start

**Starting from a BRD:**
1. Drop your BRD into `inputs/brd.md`
2. Open Claude Code in the `sdlc-agents/` directory
3. Say: `"Run Agent 00 against inputs/brd.md"`
4. Review `inputs/prd.md`, resolve all `[PRODUCT-DECISION-NEEDED]` items
5. Say: `"Checkpoint 0 approved — PRD ready"`
6. Say: `"Run the SDLC pipeline starting from Agent 01"`

**Starting from a PRD:**
1. Drop your PRD into `inputs/prd.md`
2. Open Claude Code in the `sdlc-agents/` directory
3. Say: `"Run the SDLC pipeline starting from Agent 01"`

**Resuming after a checkpoint:**
- Say: `"Checkpoint [n] approved — continue the pipeline"`

---

## Directory Structure

```
/sdlc-agents/
├── CLAUDE.md                      # Orchestrator — pipeline definition
├── README.md                      # This file
├── agents/
│   ├── 00-brd-to-prd.md          # BRD → PRD translation (optional)
│   ├── 01-prd-analyst.md         # Requirements extraction
│   ├── 02-clarification.md       # Ambiguity surfacing
│   ├── 03-spec-decomposer.md     # Functional specifications
│   ├── 04-architecture.md        # System design + ADRs
│   ├── 05-risk-assessment.md     # Delivery, security, ops risks
│   ├── 06-story-writer.md        # Epics → Stories → ACs
│   ├── 07-task-breakdown.md      # Developer tasks + estimates
│   ├── 08-code-generator.md      # Implementation
│   ├── 09-test-generator.md      # Unit & integration tests
│   ├── 09b-functional-test-agent.md # Journey, contract & smoke tests
│   ├── 09c-test-plan-agent.md    # Artifact-traced test plan (CSV + Excel)
│   ├── 10-code-reviewer.md       # Code review findings
│   ├── 11-security-agent.md      # Security vulnerability review
│   ├── 12-documentation-agent.md # API docs, README, ops guide
│   └── 13-pr-assembler.md        # PR description + traceability
├── inputs/
│   ├── brd.md                     # Drop BRD here (if applicable)
│   └── prd.md                     # Drop PRD here (or Agent 00 writes it)
├── outputs/                       # All pipeline artifacts
│   ├── requirements.md
│   ├── clarifications.md
│   ├── specs.md
│   ├── architecture.md
│   ├── risks.md
│   ├── stories.md
│   ├── tasks.md
│   ├── task-log.md                # Running log across all dev agents
│   ├── review-findings.md
│   ├── security-findings.md
│   ├── pr-description.md
│   └── docs/
│       ├── README.md
│       ├── api.md
│       ├── architecture.md
│       ├── operations.md
│       └── CHANGELOG.md
├── scripts/
│   └── Generate-TestPlan.ps1          # Excel generator (written by Agent 09c)
├── src/                           # Generated implementation code
└── tests/
    ├── [component]/               # Unit & integration tests (Agent 09)
    ├── [Product]-Test-Cases.csv   # Traceable test case source (Agent 09c)
    ├── [Product]-Test-Plan.xlsx   # Excel test plan — 3 sheets (Agent 09c)
    └── functional/
        ├── journeys/              # User journey tests (Agent 09b)
        ├── integration/           # Cross-component tests (Agent 09b)
        ├── contracts/             # API contract tests (Agent 09b)
        └── smoke/                 # Post-deployment smoke tests (Agent 09b)
```

---

## Agent Summary

| Agent | Name                     | Input                          | Output                        |
|-------|--------------------------|--------------------------------|-------------------------------|
| 00    | BRD to PRD Bridge        | inputs/brd.md                  | inputs/prd.md                 |
| 01    | PRD Analyst              | inputs/prd.md                  | outputs/requirements.md       |
| 02    | Clarification            | outputs/requirements.md        | outputs/clarifications.md     |
| 03    | Spec Decomposer          | outputs/requirements.md        | outputs/specs.md              |
| 04    | Architecture             | outputs/specs.md               | outputs/architecture.md       |
| 05    | Risk Assessment          | outputs/specs.md + architecture| outputs/risks.md              |
| 06    | Story Writer             | outputs/specs.md + architecture| outputs/stories.md            |
| 07    | Task Breakdown           | outputs/stories.md             | outputs/tasks.md              |
| 08    | Code Generator           | outputs/tasks.md               | src/                          |
| 09    | Unit & Integration Tests | outputs/tasks.md + src/        | tests/                        |
| 09b   | Functional & E2E Tests   | outputs/stories.md + tests/    | tests/functional/             |
| 09c   | Test Plan Generator      | outputs/specs.md + stories.md + risks.md | tests/[Product]-Test-Plan.xlsx |
| 10    | Code Reviewer            | src/ + tests/ + tests/functional/ | outputs/review-findings.md |
| 11    | Security Agent           | src/ + outputs/specs.md        | outputs/security-findings.md  |
| 12    | Documentation            | src/ + outputs/architecture.md | outputs/docs/                 |
| 13    | PR Assembler             | all outputs/                   | outputs/pr-description.md     |

---

## Human Checkpoints

| Checkpoint | After Step | What to Review                              | Confirm With                          |
|------------|------------|---------------------------------------------|---------------------------------------|
| 0          | 00         | Generated PRD — resolve [PRODUCT-DECISION-NEEDED] | "Checkpoint 0 approved — PRD ready" |
| 1          | 02         | Requirements + clarification answers        | "Checkpoint 1 approved"               |
| 2          | 05         | Specs, architecture, risks                  | "Checkpoint 2 approved"               |
| 3          | 07         | Stories, tasks, estimates                   | "Checkpoint 3 approved"               |
| 4          | 11         | Code review + security findings             | "Checkpoint 4 approved"               |
| Final      | 13         | PR description — human approves merge       | Approve PR in your Git platform       |

---

## Key Design Principles

- **No auto-merge.** Every PR requires explicit human approval.
- **Traceability.** Every artifact references its source PRD section.
- **Idempotency.** Re-running any stage produces consistent output without side effects.
- **Sensitive file exclusions.** Agents never modify `.tf`, `.bicep`, `.yml`, `.yaml`, `.cfn`, `.env` files.
- **Single responsibility.** Each agent does one thing well.
- **Ambiguity halts the pipeline.** `[AMBIGUOUS]` flags stop execution and surface to human.
- **Explicit handoffs.** Every agent declares its inputs and outputs. No agent reads another agent's source files directly.
- **Failure isolation.** Any agent failure halts only that step — the pipeline resumes from that step once resolved.
