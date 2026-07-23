# AI-First SDLC Pipeline — Claude Code

A markdown-driven, multi-agent SDLC pipeline that transforms a BRD or PRD
into delivered, tested, documented code using Claude Code.

Two execution modes. Same agents. Same artifacts. Same checkpoint gates.

---

## Two Ways to Run This Pipeline

| | **Static Pipeline** | **BMAD Hybrid (Option C)** |
|---|---|---|
| **Entry point** | `CLAUDE.md` + `agents/` | `orchestrator.md` + `skills/` |
| **Interaction model** | Agents generate → you edit artifacts to resolve gaps | Tier 1 agents converse with you → artifacts arrive near-complete |
| **Best for** | Known requirements, fast execution, repeat runs | Ambiguous requirements, first run of a new product, decisions need to be made live |
| **Checkpoint enforcement** | Text stops in `CLAUDE.md` | Phrase-gated locks in `orchestrator.md` |
| **Artifacts produced** | Identical | Identical |
| **Existing pipeline affected?** | — | No — Option C is purely additive |

---

## Pipeline Flow (both modes)

```
inputs/brd.md (optional — if starting from a BRD)
     │
     ▼
[00] Alex — BRD to PRD Bridge    → inputs/prd.md
     │
     ▼ ⛔ HUMAN CHECKPOINT 0 (PRD review — BRD path only)
     │
inputs/prd.md (start here if you already have a PRD)
     │
     ▼
[01] Sam   — PRD Analyst         → outputs/requirements.md
[02] Jordan — Clarification      → outputs/clarifications.md
     │
     ▼ ⛔ HUMAN CHECKPOINT 1 (requirements review)
     │
[03] Taylor — Spec Decomposer    → outputs/specs.md
[04] Winston — Architecture      → outputs/architecture.md
[05] Morgan — Risk Assessment    → outputs/risks.md
     │
     ▼ ⛔ HUMAN CHECKPOINT 2 (architecture review)
     │
[06] Riley — Story Writer        → outputs/stories.md
[07] Casey — Task Breakdown      → outputs/tasks.md
     │
     ▼ ⛔ HUMAN CHECKPOINT 3 (backlog review)
     │
[08] Amelia — Code Generator     → src/
[09] Quinn  — Unit & Integration Tests → tests/
[09b] Drew  — Functional & E2E Tests   → tests/functional/
[09c] Avery — Test Plan Generator      → tests/[Product]-Test-Plan.xlsx
[10] Blake  — Code Reviewer      → outputs/review-findings.md
[11] Robin  — Security Agent     → outputs/security-findings.md
     │
     ▼ ⛔ HUMAN CHECKPOINT 4 (findings review)
     │
[12] Jamie — Documentation       → outputs/docs/
[13] Sage  — PR Assembler        → outputs/pr-description.md
     │
     ▼ ⛔ HUMAN PR APPROVAL (no auto-merge)
```

---

## Option A — Static Pipeline: Quick Start

**Starting from a BRD:**
1. Place your BRD at `inputs/brd.md`
2. Open Claude Code in the `sdlc-agents/` directory
3. Say: `"Run Agent 00 against inputs/brd.md"`
4. Review `inputs/prd.md`, resolve all `[PRODUCT-DECISION-NEEDED]` items
5. Type: `"Checkpoint 0 approved — PRD ready"`
6. Say: `"Run the SDLC pipeline starting from Agent 01"`

**Starting from a PRD:**
1. Place your PRD at `inputs/prd.md`
2. Open Claude Code in the `sdlc-agents/` directory
3. Say: `"Run the SDLC pipeline starting from Agent 01"`

**Resuming after a checkpoint:**
- Type: `"Checkpoint [N] approved — continue the pipeline"`

**Running a specific agent:**
- Say: `"Run Agent 04 (Winston) against outputs/specs.md"`
- Or reference the file: `@agents/04-architecture.md`

---

## Option C — BMAD Hybrid: Quick Start

**First run (from BRD or PRD):**
1. Place your BRD at `inputs/brd.md` (or PRD at `inputs/prd.md`)
2. Open Claude Code in the `sdlc-agents/` directory
3. Say: `"@orchestrator.md"` or `"Load the SDLC pipeline orchestrator"`
4. The orchestrator reports pipeline state and presents a menu
5. Select **[1] Continue** to load the current stage's SKILL

**How Tier 1 agents work (Alex, Jordan, Winston, Morgan, Riley, Casey):**
- The agent reads the relevant inputs, then opens a conversation
- It asks targeted questions — grouped by theme, not one at a time
- You answer in the conversation; the agent captures your decisions live
- Once all blocking questions are answered, the agent writes the artifact with your answers already incorporated
- You review a near-complete document rather than a template full of gaps

**How Tier 3 agents work (Amelia through Sage):**
- They run autonomously, following the same source `agents/` file exactly
- They report completion and the orchestrator advances automatically

**At every checkpoint:**
- The active agent presents a summary of what was produced
- Review the artifact file(s) listed
- Type the exact approval phrase to advance (no paraphrases accepted):

| Checkpoint | Exact Phrase Required |
|-----------|----------------------|
| 0 | `Checkpoint 0 approved` |
| 1 | `Checkpoint 1 approved` |
| 2 | `Checkpoint 2 approved` |
| 3 | `Checkpoint 3 approved` |
| 4 | `Checkpoint 4 approved` |

**Resuming in a new session:**
- Say: `"@orchestrator.md"` — it re-assesses artifact state and picks up from the correct stage automatically

---

## Which Option Should I Use?

**Use the Static Pipeline when:**
- Requirements are well-understood and relatively stable
- You want to run a specific agent or stage without the full orchestrator
- You're on a repeat run and know what to expect from each stage
- You prefer to review and edit artifacts at your own pace

**Use the BMAD Hybrid when:**
- Requirements are ambiguous or this is the first run for a new product
- You want to resolve product decisions interactively rather than through file editing
- You want the orchestrator to manage state and checkpoint enforcement for you
- The team is less familiar with the pipeline and benefits from guided progression

---

## Directory Structure

```
sdlc-agents/
│
├── CLAUDE.md                       # Static pipeline definition (Option A entry point)
├── CONTEXT.md                      # Session state — load at start of new session
├── orchestrator.md                 # BMAD hybrid runtime (Option C entry point)
├── README.md                       # This file
│
├── agents/                         # Static pipeline agent instruction files
│   ├── 00-brd-to-prd.md           # Alex — BRD → PRD translation
│   ├── 01-prd-analyst.md          # Sam  — Requirements extraction
│   ├── 02-clarification.md        # Jordan — Ambiguity surfacing
│   ├── 03-spec-decomposer.md      # Taylor — Functional specifications
│   ├── 04-architecture.md         # Winston — System design + ADRs
│   ├── 05-risk-assessment.md      # Morgan — Delivery, security, ops risks
│   ├── 06-story-writer.md         # Riley — Epics → Stories → ACs
│   ├── 07-task-breakdown.md       # Casey — Developer tasks + estimates
│   ├── 08-code-generator.md       # Amelia — Implementation
│   ├── 09-test-generator.md       # Quinn — Unit & integration tests
│   ├── 09b-functional-test-agent.md # Drew — Journey, contract & smoke tests
│   ├── 09c-test-plan-agent.md     # Avery — Artifact-traced test plan (CSV + Excel)
│   ├── 10-code-reviewer.md        # Blake — Code review findings
│   ├── 11-security-agent.md       # Robin — Security vulnerability review
│   ├── 12-documentation-agent.md  # Jamie — API docs, README, ops guide
│   └── 13-pr-assembler.md         # Sage  — PR description + GitHub publish
│
├── skills/                         # BMAD hybrid SKILL files (Option C — mirrors agents/)
│   ├── 00-alex.md                 # Tier 1: fully interactive
│   ├── 01-sam.md                  # Tier 2: autonomous + blocker check
│   ├── 02-jordan.md               # Tier 1: fully interactive
│   ├── 03-taylor.md               # Tier 2: autonomous + blocker check
│   ├── 04-winston.md              # Tier 1: fully interactive
│   ├── 05-morgan.md               # Tier 1: fully interactive
│   ├── 06-riley.md                # Tier 1: fully interactive
│   ├── 07-casey.md                # Tier 1: fully interactive
│   ├── 08-amelia.md               # Tier 3: fully autonomous
│   ├── 09-quinn.md                # Tier 3: fully autonomous
│   ├── 09b-drew.md                # Tier 3: fully autonomous
│   ├── 09c-avery.md               # Tier 3: fully autonomous
│   ├── 10-blake.md                # Tier 3: fully autonomous
│   ├── 11-robin.md                # Tier 3: fully autonomous (checkpoint gate)
│   ├── 12-jamie.md                # Tier 3: fully autonomous
│   └── 13-sage.md                 # Tier 3: fully autonomous
│
├── inputs/
│   ├── brd.md                      # Drop BRD here (Agent 00 / Alex reads this)
│   └── prd.md                      # Drop PRD here (or Agent 00 / Alex writes it)
│
├── outputs/                        # All pipeline artifacts (same for both options)
│   ├── requirements.md
│   ├── clarifications.md
│   ├── specs.md
│   ├── architecture.md
│   ├── risks.md
│   ├── stories.md
│   ├── tasks.md
│   ├── task-log.md
│   ├── review-findings.md
│   ├── security-findings.md
│   ├── pr-description.md
│   └── docs/
│       ├── README.md
│       ├── api.md
│       ├── architecture.md
│       ├── operations.md
│       └── CHANGELOG.md
│
├── scripts/
│   ├── Sync-Dashboard.ps1          # Patches pipeline-dashboard.html with artifact state
│   ├── Convert-OutputsToHtml.ps1   # Generates HTML companion for each outputs/ markdown
│   └── Generate-TestPlan.ps1       # Excel test plan generator (written by Avery/Agent 09c)
│
├── pipeline-dashboard.html         # Live pipeline tracking dashboard (open in browser)
├── src/                            # Generated implementation code
└── tests/
    ├── [component]/                # Unit & integration tests (Quinn / Agent 09)
    ├── [Product]-Test-Cases.csv    # Traceable test case source (Avery / Agent 09c)
    ├── [Product]-Test-Plan.xlsx    # Excel test plan — 3 sheets (Avery / Agent 09c)
    └── functional/
        ├── journeys/               # User journey tests (Drew / Agent 09b)
        ├── integration/            # Cross-component tests (Drew / Agent 09b)
        ├── contracts/              # API contract tests (Drew / Agent 09b)
        └── smoke/                  # Post-deployment smoke tests (Drew / Agent 09b)
```

---

## Agent Summary

| # | Persona | Name | Input | Output |
|---|---------|------|-------|--------|
| 00 | 🗂️ Alex | BRD to PRD Bridge | inputs/brd.md | inputs/prd.md |
| 01 | 🔍 Sam | PRD Analyst | inputs/prd.md | outputs/requirements.md |
| 02 | ❓ Jordan | Clarification | outputs/requirements.md | outputs/clarifications.md |
| 03 | 📐 Taylor | Spec Decomposer | outputs/requirements.md | outputs/specs.md |
| 04 | 🏗️ Winston | Architecture | outputs/specs.md | outputs/architecture.md |
| 05 | ⚠️ Morgan | Risk Assessment | outputs/specs.md + architecture | outputs/risks.md |
| 06 | 📋 Riley | Story Writer | outputs/specs.md + architecture | outputs/stories.md |
| 07 | 🔧 Casey | Task Breakdown | outputs/stories.md | outputs/tasks.md |
| 08 | 💻 Amelia | Code Generator | outputs/tasks.md | src/ |
| 09 | 🧪 Quinn | Unit & Integration Tests | outputs/tasks.md + src/ | tests/ |
| 09b | 🔬 Drew | Functional & E2E Tests | outputs/stories.md + tests/ | tests/functional/ |
| 09c | 📊 Avery | Test Plan Generator | outputs/specs + stories + risks | tests/[Product]-Test-Plan.xlsx |
| 10 | 👁️ Blake | Code Reviewer | src/ + tests/ | outputs/review-findings.md |
| 11 | 🔒 Robin | Security Agent | src/ + outputs/specs.md | outputs/security-findings.md |
| 12 | 📝 Jamie | Documentation | src/ + outputs/architecture.md | outputs/docs/ |
| 13 | 🚀 Sage | PR Assembler | all outputs/ | outputs/pr-description.md + GitHub PR |

---

## Human Checkpoints

Same for both pipeline options.

| Checkpoint | After | Review | Approval Phrase |
|-----------|-------|--------|-----------------|
| 0 | Alex / Agent 00 | `inputs/prd.md` — resolve `[PRODUCT-DECISION-NEEDED]` | `Checkpoint 0 approved` |
| 1 | Jordan / Agent 02 | `outputs/requirements.md` + `outputs/clarifications.md` | `Checkpoint 1 approved` |
| 2 | Morgan / Agent 05 | `outputs/specs.md` + `outputs/architecture.md` + `outputs/risks.md` | `Checkpoint 2 approved` |
| 3 | Casey / Agent 07 | `outputs/stories.md` + `outputs/tasks.md` | `Checkpoint 3 approved` |
| 4 | Robin / Agent 11 | `outputs/review-findings.md` + `outputs/security-findings.md` | `Checkpoint 4 approved` |
| Final | Sage / Agent 13 | PR description — approve merge in GitHub | Approve PR |

---

## Key Design Principles

- **No auto-merge.** Every PR requires explicit human approval.
- **Traceability.** Every artifact references its source PRD section.
- **Idempotency.** Re-running any stage produces consistent output without side effects.
- **Sensitive file exclusions.** Agents never modify `.tf`, `.bicep`, `.yml`, `.yaml`, `.cfn`, `.env` files.
- **Single responsibility.** Each agent does one thing well.
- **Ambiguity halts the pipeline.** `[AMBIGUOUS]` flags stop execution and surface to human.
- **Explicit handoffs.** Every agent declares its inputs and outputs.
- **Failure isolation.** Any agent failure halts only that step.
- **Design influence.** Agent identity blocks are styled after the [BMAD Method](https://github.com/bmad-code-org/BMAD-METHOD) persona pattern. The orchestration model is our own.
