@CONTEXT.md

# SDLC Agent Orchestrator

## Purpose
This pipeline transforms a Product Requirements Document (PRD) into
delivered, tested, documented code via a sequence of specialized agents.
Each agent has a single responsibility and produces a versioned output
artifact that becomes the next agent's input. Claude Code executes each
step by reading the agent's markdown file as its instruction set, reading
the declared inputs, and writing the declared outputs before moving to
the next step.

---

## Global Rules
- **No auto-merge.** All PRs require explicit human approval.
- **Traceability is mandatory.** Every artifact must reference its source PRD section.
- **Ambiguity halts the pipeline.** Any [AMBIGUOUS] flag stops execution and surfaces to human.
- **Idempotency.** Re-running any stage must produce consistent output without side effects.
- **Scope isolation.** Each agent reads only its declared inputs. No agent accesses another agent's source files directly.
- **Sensitive file exclusions.** Never generate code modifications to: .tf, .bicep, .yml, .yaml, .cfn, .env files.
- **On any failure.** Halt, report the step that failed, the reason, and what the human needs to resolve before resuming.

---

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

---

## Pipeline Stages

---

### Step 0 — BRD to PRD Bridge
> Skip this step if your input is already a PRD. Go directly to Step 1.

**Agent:** @agents/00-brd-to-prd.md
**Reads:**
  - inputs/brd.md
**Writes:**
  - inputs/prd.md (overwrites placeholder)
**Pre-condition:** inputs/brd.md exists and is non-empty
**On failure:** Halt. Report what content was missing or unparseable in the BRD.

---
## ⛔ HUMAN CHECKPOINT 0 (BRD inputs only)
Review inputs/prd.md.
Resolve all [PRODUCT-DECISION-NEEDED] items by editing the file directly.
Confirm with: "Checkpoint 0 approved — PRD ready"
Do not proceed to Step 1 until confirmed.
---

### Step 1 — PRD Analyst
**Agent:** @agents/01-prd-analyst.md
**Reads:**
  - inputs/prd.md
**Writes:**
  - outputs/requirements.md
**Pre-condition:** inputs/prd.md exists and Status is APPROVED (or Checkpoint 0 confirmed)
**On failure:** Halt. Report which PRD sections were unparseable or missing required content.

---

### Step 2 — Clarification
**Agent:** @agents/02-clarification.md
**Reads:**
  - outputs/requirements.md
**Writes:**
  - outputs/clarifications.md
**Pre-condition:** outputs/requirements.md exists
**On failure:** Halt. Report what prevented clarification questions from being generated.

---
## ⛔ HUMAN CHECKPOINT 1
Review outputs/requirements.md and outputs/clarifications.md.
Answer all Blocking questions in clarifications.md by editing the Answer fields directly.
Resolve all [AMBIGUOUS] and [GAP] flags in requirements.md.
Confirm with: "Checkpoint 1 approved"
Do not proceed to Step 3 until confirmed.
---

### Step 3 — Spec Decomposer
**Agent:** @agents/03-spec-decomposer.md
**Reads:**
  - outputs/requirements.md
  - outputs/clarifications.md
**Writes:**
  - outputs/specs.md
**Pre-condition:**
  - outputs/requirements.md Status is APPROVED
  - All Blocking questions in outputs/clarifications.md have answers
**On failure:** Halt. Report which requirements could not be decomposed and why.

---

### Step 4 — Architecture
**Agent:** @agents/04-architecture.md
**Reads:**
  - outputs/specs.md
  - outputs/requirements.md (for NFRs and constraints)
**Writes:**
  - outputs/architecture.md
**Pre-condition:** outputs/specs.md exists and is complete
**On failure:** Halt. Report which specs could not be mapped to architecture components.

---

### Step 5 — Risk Assessment
**Agent:** @agents/05-risk-assessment.md
**Reads:**
  - outputs/specs.md
  - outputs/architecture.md
**Writes:**
  - outputs/risks.md
**Pre-condition:** outputs/specs.md and outputs/architecture.md both exist
**On failure:** Halt. Report what prevented risk assessment from completing.

---
## ⛔ HUMAN CHECKPOINT 2
Review outputs/specs.md, outputs/architecture.md, and outputs/risks.md.
Approve the architecture design.
Accept or dismiss risks in outputs/risks.md by updating their Status fields.
Resolve any [ARCH-RISK] items that require design changes before dev begins.
Confirm with: "Checkpoint 2 approved"
Do not proceed to Step 6 until confirmed.
---

### Step 6 — Story Writer
**Agent:** @agents/06-story-writer.md
**Reads:**
  - outputs/specs.md
  - outputs/architecture.md
  - outputs/risks.md (to apply [HIGH-RISK] flags to stories)
**Writes:**
  - outputs/stories.md
**Pre-condition:**
  - outputs/specs.md and outputs/architecture.md exist
  - Checkpoint 2 confirmed
**On failure:** Halt. Report which specs could not be translated into stories.

---

### Step 7 — Task Breakdown
**Agent:** @agents/07-task-breakdown.md
**Reads:**
  - outputs/stories.md
  - outputs/architecture.md (for component ownership)
**Writes:**
  - outputs/tasks.md
**Pre-condition:** outputs/stories.md exists and is complete
**On failure:** Halt. Report which stories could not be decomposed into tasks.

---
## ⛔ HUMAN CHECKPOINT 3
Review outputs/stories.md and outputs/tasks.md.
Adjust story scope, priority, or estimates as needed by editing the files directly.
Resolve any [DECISION-NEEDED] flags in tasks.md.
Confirm with: "Checkpoint 3 approved"
Do not proceed to Step 8 until confirmed.

### Recommended: Write an Implementation Plan Before Step 8
After Checkpoint 3 is approved, run the planning skill to translate outputs/tasks.md
into an agent-executable implementation plan before invoking the Code Generator:

```
/superpowers:writing-plans
```

This produces `docs/superpowers/plans/YYYY-MM-DD-<feature>.md` containing:
- **File map** — exact files to create/modify, one responsibility per file
- **Task steps** — TDD cycle per task: failing test → implement → passing test → commit
- **Interface contracts** — what each task consumes from and produces for neighboring tasks
- **No placeholders** — every step has actual code, commands, and expected output

After the plan is saved, choose an execution mode for Step 8:
- **Subagent-Driven (recommended):** `/superpowers:subagent-driven-development`
  Fresh subagent per task with review between each — matches the pipeline's scope
  isolation rule and keeps main context clean.
- **Inline Execution:** `/superpowers:executing-plans`
  Executes in the current session with checkpoints.

The plan's interface contracts directly enforce Agent 08's [BLOCKED-BY] dependency
checks — tasks cannot silently consume outputs that haven't been produced yet.
---

### Step 8 — Code Generator
**Agent:** @agents/08-code-generator.md
**Reads:**
  - outputs/tasks.md (process one task at a time, in sequence order)
  - outputs/architecture.md (for component patterns and API contracts)
  - outputs/specs.md (for business rules and edge cases)
  - src/ (existing files, for pattern consistency)
**Writes:**
  - src/[component]/[filename] (one or more implementation files per task)
  - outputs/task-log.md (append a completion record per task)
**Pre-condition:**
  - outputs/tasks.md exists and Checkpoint 3 is confirmed
  - No task is flagged [BLOCKED-BY] an incomplete task
  - No task is flagged [DECISION-NEEDED] without a resolution
**Execution:** Process tasks in sequence order within each story.
  Complete all tasks for one story before moving to the next.
**On failure:** Halt on the specific task. Report task ID, reason, and
  what needs to be resolved. Do not skip to the next task.

---

### Step 9 — Unit & Integration Test Generator
**Agent:** @agents/09-test-generator.md
**Reads:**
  - outputs/tasks.md (for ACs and task scope)
  - outputs/specs.md (for business rules and edge cases)
  - src/[component]/[filename] (the implementation being tested)
  - outputs/task-log.md (code generator notes for the test agent)
**Writes:**
  - tests/[component]/[filename].test.[ext] (one test file per implementation file)
  - outputs/task-log.md (append test coverage summary per task)
**Pre-condition:** Step 8 complete. src/ files exist for all tasks.
**Execution:** Process one task's tests immediately after that task's
  implementation is confirmed complete in task-log.md.
**On failure:** Halt. Report which task's tests could not be generated and why.

---

### Step 9b — Functional & E2E Test Generator
**Agent:** @agents/09b-functional-test-agent.md
**Reads:**
  - outputs/stories.md (user journeys and ACs)
  - outputs/specs.md (business rules and end-to-end flows)
  - outputs/architecture.md (component topology, integration points, event flows)
  - outputs/risks.md (high-risk areas requiring deeper coverage)
  - tests/ (all Agent 09 unit/integration tests — to avoid duplication)
  - outputs/task-log.md (implementation and unit test notes)
**Writes:**
  - tests/functional/journeys/ (user journey tests)
  - tests/functional/integration/ (cross-component integration tests)
  - tests/functional/contracts/ (API contract tests)
  - tests/functional/smoke/ (post-deployment smoke tests)
  - outputs/task-log.md (append functional test summary)
**Pre-condition:**
  - Step 9 complete. tests/ directory populated with unit/integration tests.
  - outputs/stories.md and outputs/architecture.md exist.
**Execution:** Generate all four test categories. For each Must Have story,
  produce at minimum one journey test. For each HIGH-RISK story, produce
  full happy and unhappy path coverage.
**On failure:** Halt. Report which stories or integration flows could not
  have functional tests generated, and why.

---

### Step 9c — Test Plan Generator
**Agent:** @agents/09c-test-plan-agent.md
**Reads:**
  - outputs/requirements.md (REQ-xxx and NFRs)
  - outputs/specs.md (BR-xxx, edge cases, error conditions)
  - outputs/stories.md (AC-xxx per story)
  - outputs/risks.md (Critical and High risks)
  - outputs/architecture.md (API contracts, integration points)
  - tests/ (all Agent 09 unit/integration tests — to record automated coverage)
  - tests/functional/ (all Agent 09b tests — to record automated coverage)
  - outputs/task-log.md (implementation and test agent notes)
**Writes:**
  - tests/[ProductName]-Test-Cases.csv (one row per test case with traceability)
  - tests/[ProductName]-Test-Plan.xlsx (Excel; multi-row per step + coverage sheets)
  - scripts/Generate-TestPlan.ps1 (PowerShell generator — overwrites if exists)
  - outputs/task-log.md (append test plan summary)
**Pre-condition:**
  - Step 9 complete. tests/ directory populated with unit/integration tests.
  - outputs/stories.md, outputs/specs.md, and outputs/risks.md exist.
  - Step 9b output used if available; not required.
**Execution:** Extract every testable item from all five artifacts. Generate
  one or more test cases per item. Write CSV then execute PowerShell to
  produce the Excel. Every test case must cite its source artifact ID.
**On failure:** Halt. Report which artifact could not be processed and why.
  CSV is always produced; Excel failure is non-blocking if Excel COM is
  unavailable (non-Windows) — document the gap in task-log.md.

---

### Step 10 — Code Reviewer
**Agent:** @agents/10-code-reviewer.md
**Reads:**
  - src/ (all implementation files)
  - tests/ (all unit and integration test files)
  - tests/functional/ (all functional and E2E test files)
  - outputs/architecture.md (to verify code fits intended design)
  - outputs/specs.md (to verify correct behavior implemented)
  - outputs/task-log.md (code generator and test generator notes)
**Writes:**
  - outputs/review-findings.md
**Pre-condition:** Steps 8, 9, 9b, and 9c all complete.
**On failure:** Halt. Report what prevented the review from completing.

---

### Step 11 — Security Agent
**Agent:** @agents/11-security-agent.md
**Reads:**
  - src/ (all implementation files)
  - outputs/specs.md (security-related specs and NFRs)
  - outputs/risks.md (security risks already identified)
  - outputs/architecture.md (auth, authorization, integration security patterns)
**Writes:**
  - outputs/security-findings.md
**Pre-condition:** Step 8 complete. src/ fully populated.
**On failure:** Halt. Report what prevented the security review from completing.

---
## ⛔ HUMAN CHECKPOINT 4

### Recommended: Run Multi-Agent Review Pass Before Human Review
Before presenting findings to the human, run the comprehensive skill-based review to augment
Agent 10 and Agent 11 output with additional specialized coverage:

```
/pr-review-toolkit:review-pr all parallel
```

This launches 6 specialized agents in parallel:
- **code-reviewer** — correctness, compliance, general quality
- **pr-test-analyzer** — behavioral coverage gaps
- **comment-analyzer** — documentation accuracy
- **silent-failure-hunter** — swallowed errors, missing error logging
- **type-design-analyzer** — type encapsulation (if new types added)
- **code-simplifier** — polish pass (runs after others pass)

Append any additional [BLOCKING] or [SECURITY-BLOCKING] findings into
outputs/review-findings.md and outputs/security-findings.md before presenting
to the human. Run `/security-review` alongside for deeper security coverage.

---

Review outputs/review-findings.md and outputs/security-findings.md.
All [BLOCKING] findings in review-findings.md must be resolved before continuing.
All [SECURITY-BLOCKING] findings in security-findings.md must be resolved before continuing.
For each resolved finding, update its Status to "Resolved" and add a note on what changed.
Confirm with: "Checkpoint 4 approved"
Do not proceed to Step 12 until confirmed.
---

### Step 12 — Documentation Agent
**Agent:** @agents/12-documentation-agent.md
**Reads:**
  - src/ (implementation files — source of truth for behavior)
  - tests/ (illustrate usage patterns)
  - outputs/architecture.md (system design context)
  - outputs/specs.md (business context)
  - outputs/stories.md (user-facing feature descriptions)
  - Existing docs/ and README.md (update, do not replace)
**Writes:**
  - outputs/docs/README.md
  - outputs/docs/api.md
  - outputs/docs/architecture.md
  - outputs/docs/operations.md
  - outputs/docs/CHANGELOG.md
  - outputs/task-log.md (append documentation summary)
**Pre-condition:**
  - Checkpoint 4 confirmed
  - src/ is complete and all BLOCKING findings resolved
**On failure:** Halt. Report which documentation sections could not be
  generated and what information was missing.

---

### Step 13 — PR Assembler
**Agent:** @agents/13-pr-assembler.md
**Reads:**
  - outputs/requirements.md
  - outputs/stories.md
  - outputs/tasks.md
  - outputs/task-log.md
  - outputs/review-findings.md
  - outputs/security-findings.md
  - outputs/docs/CHANGELOG.md
  - src/ (to enumerate changed files)
  - tests/ (to enumerate unit/integration test coverage)
  - tests/functional/ (to enumerate functional test coverage)
**Writes:**
  - outputs/pr-description.md
**Pre-condition:**
  - review-findings.md Overall Verdict is APPROVED or APPROVED WITH CONDITIONS
  - security-findings.md Overall Security Verdict is PASS or PASS WITH CONDITIONS
  - No BLOCKING findings remain open in review-findings.md
  - No SECURITY-BLOCKING findings remain open in security-findings.md
  - Step 12 complete. outputs/docs/ fully populated.
**On failure:** Halt. Report exactly which pre-condition failed and what
  must be resolved before PR assembly can proceed.

---

## Pipeline Execution Summary

| Step | Agent                    | Key Output                             | Gate              |
|------|--------------------------|----------------------------------------|-------------------|
| 0    | BRD to PRD Bridge        | inputs/prd.md                          | Checkpoint 0      |
| 1    | PRD Analyst              | outputs/requirements.md                |                   |
| 2    | Clarification            | outputs/clarifications.md              | Checkpoint 1      |
| 3    | Spec Decomposer          | outputs/specs.md                       |                   |
| 4    | Architecture             | outputs/architecture.md                |                   |
| 5    | Risk Assessment          | outputs/risks.md                       | Checkpoint 2      |
| 6    | Story Writer             | outputs/stories.md                     |                   |
| 7    | Task Breakdown           | outputs/tasks.md                       | Checkpoint 3      |
| 8    | Code Generator           | src/                                   |                   |
| 9    | Unit & Integration Tests | tests/                                 |                   |
| 9b   | Functional & E2E Tests   | tests/functional/                      |                   |
| 9c   | Test Plan Generator      | tests/[Product]-Test-Plan.xlsx         |                   |
| 10   | Code Reviewer            | outputs/review-findings.md             |                   |
| 11   | Security Agent           | outputs/security-findings.md           | Checkpoint 4      |
| 12   | Documentation            | outputs/docs/                          |                   |
| 13   | PR Assembler             | outputs/pr-description.md              | Human PR approval |
