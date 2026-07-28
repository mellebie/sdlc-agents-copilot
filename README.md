# SDLC Agent Pipeline — GitHub Copilot Edition

An end-to-end software delivery pipeline that transforms a Product Requirements Document (PRD) into delivered, tested, documented code — using GitHub Copilot as the execution runtime.

The pipeline is structured as a sequence of specialised agents. Each agent has a single responsibility, reads declared inputs, and writes a versioned output artifact that becomes the next agent's input. Human checkpoint gates separate analysis, design, and delivery phases.

The repository also includes a reusable development context pack under `context/` that captures coding standards, testing standards, security standards, and approved examples for delivery agents.

---

## How It Works

The pipeline runs in two modes depending on the stage:

| Mode | Stages | How to run |
|------|--------|------------|
| **Copilot Chat — Agent Mode** | Step 00A (pre-pipeline bootstrap) and Steps 0–7 (analysis & planning) | Open a prompt file from `.github/prompts/` in VS Code → Copilot Chat → Agent mode |
| **Copilot Coding Agent** | Steps 8–13 (build & deliver) | Create a GitHub Issue using a template from `.github/ISSUE_TEMPLATE/` → assign to Copilot |

Copilot loads `.github/copilot-instructions.md` automatically for every request in this repo — it carries all pipeline rules, checkpoint phrases, and the stage map.

---

## Prerequisites

- VS Code with the GitHub Copilot extension (agent mode enabled)
- GitHub Copilot Enterprise or Pro+ (required for Copilot Coding Agent in Steps 8–13)
- For MCP tool access (GitHub PR creation, Azure DevOps story push): set environment variables:
  - `GITHUB_PAT` — GitHub personal access token with repo scope
  - `ADO_ORG` — Azure DevOps organisation URL (e.g. `https://dev.azure.com/myorg`)
  - `ADO_PAT` — Azure DevOps personal access token

---

## Quick Start

### Step 1 — Add your input document
Place your PRD at `inputs/prd.md`. If you have a BRD instead, place the Word document in `inputs/` and run Step 00A first to generate `inputs/brd.md`, then run Step 0.

### Step 1a — Validate inputs
Run `\.\scripts\Validate-PipelineInputs.ps1` before Step 00A, Step 0, or Step 1. It checks pipeline inputs for common PII patterns and blocks forbidden file types from entering the pipeline.

### Step 1b — Bootstrap BRD markdown when needed
If `inputs/brd.md` is missing and a Word BRD exists in `inputs/`, open `.github/prompts/00a-brd-bootstrap.prompt.md` in Copilot Chat (Agent mode) and run the bootstrap step first. It creates `inputs/brd.md` for the main pipeline.

### Step 2 — Run Step 0 in Copilot Chat
1. In VS Code, open Copilot Chat (`Ctrl+Alt+I`)
2. Switch to **Agent** mode
3. Click the paperclip / attach prompt → select `.github/prompts/00-brd-to-prd.prompt.md`
4. Send — Copilot reads `inputs/brd.md` and writes `inputs/prd.md`

### Step 3 — Run Step 1 in Copilot Chat
1. In VS Code, open Copilot Chat (`Ctrl+Alt+I`)
2. Switch to **Agent** mode
3. Click the paperclip / attach prompt → select `.github/prompts/01-prd-analyst.prompt.md`
4. Send — Copilot reads `inputs/prd.md` and writes `outputs/requirements.md`

### Step 4 — Review and checkpoint
Review the output artifact. Answer any [AMBIGUOUS] or blocking questions before continuing to the next step. Human checkpoints are hard stops — do not skip them.

### Step 5 — Continue through Steps 2–7
Repeat the pattern: open the next prompt file, run in agent mode, review the output.

### Step 6 — Steps 8–13 via Copilot Coding Agent
1. On GitHub.com, go to **Issues → New Issue**
2. Select the SDLC template for the step you're running (e.g. `SDLC Step 08 — Code Generator`)
3. Tick all pre-condition checkboxes to confirm inputs exist
4. Create the issue and assign it to **Copilot**
5. Copilot Coding Agent will execute the stage and open a PR

---

## Pipeline Stages

| Step | Agent | Persona | Mode | Output |
|------|-------|---------|------|--------|
| 00A | BRD Bootstrap | Intake Preparation | Chat | `inputs/brd.md` |
| 0 | BRD to PRD Bridge | Alex — The Translator | Chat | `inputs/prd.md` |
| 1 | PRD Analyst | Sam — The Forensic Analyst | Chat | `outputs/requirements.md` |
| 2 | Clarification | Jordan — The Interrogator | Chat | `outputs/clarifications.md` |
| ⛔ | **Checkpoint 1** | Human | — | Answer all blocking questions |
| 3 | Spec Decomposer | Taylor — The Precision Engineer | Chat | `outputs/specs.md` |
| 4 | Architecture | Winston — The Architect | Chat | `outputs/architecture.md` |
| 5 | Risk Assessment | Morgan — The Risk Officer | Chat | `outputs/risks.md` |
| ⛔ | **Checkpoint 2** | Human | — | Approve architecture and risks |
| 6 | Story Writer | Riley — The Product Owner | Chat | `outputs/stories.md` |
| 7 | Task Breakdown | Casey — The Tech Lead | Chat | `outputs/tasks.md` |
| ⛔ | **Checkpoint 3** | Human | — | Approve stories and tasks |
| 8 | Code Generator | Amelia — The Engineer | Coding Agent | `src/` |
| 9 | Unit & Integration Tests | Quinn — The QA Engineer | Coding Agent | `tests/` |
| 9b | Functional & E2E Tests | Drew — The Journey Tester | Coding Agent | `tests/functional/` |
| 9c | Test Plan | Avery — The QA Lead | Coding Agent | `tests/*.csv`, `tests/*.xlsx` |
| 10 | Code Reviewer | Blake — The Principal Engineer | Coding Agent | `outputs/review-findings.md` |
| 11 | Security Agent | Robin — The Security Engineer | Coding Agent | `outputs/security-findings.md` |
| ⛔ | **Checkpoint 4** | Human | — | Resolve all blocking findings |
| 12 | Documentation | Jamie — The Tech Writer | Coding Agent | `outputs/docs/` |
| 13 | PR Assembler | Sage — The Delivery Lead | Coding Agent | `outputs/pr-description.md` + GitHub PR |

## Pipeline Mermaid Diagrams

- Detailed engineering flow (all agents, checkpoints, controls, and guardrails): `outputs/docs/pipeline-flow.mmd`
- Executive summary flow (leadership/stakeholder view): `outputs/docs/pipeline-flow-executive.mmd`

These diagram files can be previewed directly in VS Code with the Mermaid extension.

---

## Development Context Pack (Steps 08-13)

Use the context pack to improve consistency and reuse across delivery agents.

- Location: `context/`
- Standards: `context/standards/`
- Reusable patterns: `context/patterns/`
- Examples: `context/examples/`

Important:
- Because scope isolation is enforced, agents only read files explicitly declared in each stage prompt.
- If you add a new context file, update the relevant `.github/prompts/*.prompt.md` Inputs section to include a `#file:` reference.

### Context-to-Stage Mapping

| Step | Prompt | Required context inputs |
|------|--------|-------------------------|
| 08 | `.github/prompts/08-code-generator.prompt.md` | `context/standards/coding-standards.md`, `context/standards/security-standards.md`, `context/patterns/error-handling-patterns.md` |
| 09 | `.github/prompts/09-test-generator.prompt.md` | `context/standards/testing-standards.md`, `context/standards/security-standards.md`, `context/examples/test-case-style-example.md` |
| 09b | `.github/prompts/09b-functional-tests.prompt.md` | `context/standards/testing-standards.md`, `context/standards/security-standards.md`, `context/examples/test-case-style-example.md` |
| 09c | `.github/prompts/09c-test-plan.prompt.md` | `context/standards/testing-standards.md`, `context/standards/security-standards.md`, `context/examples/test-case-style-example.md` |
| 10 | `.github/prompts/10-code-reviewer.prompt.md` | `context/standards/code-review-standards.md`, `context/standards/coding-standards.md`, `context/standards/testing-standards.md`, `context/standards/security-standards.md` |
| 11 | `.github/prompts/11-security-agent.prompt.md` | `context/standards/security-standards.md`, `context/standards/coding-standards.md`, `context/standards/testing-standards.md` |
| 12 | `.github/prompts/12-documentation.prompt.md` | `context/standards/documentation-standards.md`, `context/standards/security-standards.md` |
| 13 | `.github/prompts/13-pr-assembler.prompt.md` | `context/standards/pr-standards.md`, `context/standards/documentation-standards.md` |

Each delivery output must include standards traceability:
- `Context Standards Applied`
- `Context Divergences`

## Repository Structure

```
.github/
  copilot-instructions.md     # Global rules — loaded by Copilot on every request
  prompts/                    # 17 prompt files for Copilot Chat agent mode (Step 00A + Steps 0-7)
  ISSUE_TEMPLATE/             # 8 issue templates for Copilot Coding Agent (Steps 8-13)
.vscode/
  mcp.json                    # MCP server config — GitHub + Azure DevOps
context/
  README.md                   # Context governance and usage rules
  standards/                  # Shared coding, testing, security, docs, review, and PR standards
  patterns/                   # Reusable implementation patterns
  examples/                   # Approved example formats for delivery agents
agents/                       # Agent instruction source files (00-13)
scripts/
  Sync-Dashboard.ps1          # Syncs pipeline artifact state into the dashboard HTML
  Convert-OutputsToHtml.ps1   # Generates HTML companion for each outputs/ markdown
  Push-ToADO.ps1              # Pushes stories to Azure DevOps as epics + user stories
  Publish-PR.ps1              # Fallback: creates GitHub PR via gh CLI
pipeline-dashboard.html       # Single-file pipeline tracking dashboard (open in browser)
inputs/                       # Place your PRD or BRD here before starting
outputs/                      # Pipeline artifacts written here by each agent
src/                          # Generated implementation code
tests/                        # Generated tests
```

---

## Human Checkpoints

Checkpoints are hard stops. Confirm with the exact phrase to proceed:

| Checkpoint | Confirm with |
|------------|-------------|
| After Step 00A (BRD bootstrap only) | No checkpoint — continue to Step 0 |
| After Step 0 (PRD ready) | `Checkpoint 0 approved` |
| After Step 2 — requirements and clarifications reviewed | `Checkpoint 1 approved` |
| After Step 5 — architecture and risks approved | `Checkpoint 2 approved` |
| After Step 7 — stories and tasks approved | `Checkpoint 3 approved` |
| After Steps 10/11 — all blocking findings resolved | `Checkpoint 4 approved` |

---

## Pipeline Tracking Dashboard

Open `pipeline-dashboard.html` directly in a browser — no server required.

To sync dashboard state from the current artifact files, run:

```powershell
.\scripts\Sync-Dashboard.ps1
```

Then refresh the browser.

---

## Global Rules

These apply to every agent in the pipeline:

- **No auto-merge.** All PRs require explicit human approval.
- **Traceability is mandatory.** Every artifact must reference its source PRD section.
- **Ambiguity halts the pipeline.** Any [AMBIGUOUS] flag stops execution and surfaces to human.
- **Sensitive files are off limits.** No modifications to .tf, .bicep, .yml, .yaml, .cfn, .env files.
- **On any failure:** halt, report the step and reason, and surface to human before resuming.

## Operational Guardrails

- Run `.\scripts\Validate-PipelineInputs.ps1` before feeding new BRD/PRD content into Copilot.
- Run `.\scripts\Invoke-PipelineEval.ps1` after the analysis artifacts are updated to write `outputs/eval-summary.md` and the timestamped eval report.
- After Checkpoint 3 approval, run `.\scripts\Invoke-PipelineEval.ps1 -EnforcePostCheckpoint3` so missing delivery artifacts (Steps 08-13) are treated as `FAIL` rather than `MISSING`.
- Prefer `.\scripts\Invoke-PipelineEval-AutoGate.ps1` for normal operation. It auto-detects Checkpoint 3 approval from `outputs/pipeline-manifest.json` (with phrase fallback) and enables strict mode automatically.
- Run `.\scripts\Write-PipelineManifest.ps1` to refresh `outputs/pipeline-manifest.json` from the template and current repo state.
- Maintain `context/` as a controlled standards source; keep files concise, current, and explicitly referenced by delivery prompts.
- When context files are updated, re-run delivery eval (`.\scripts\Invoke-PipelineEval-AutoGate.ps1`) to ensure standards evidence remains present in artifacts.
- Keep prompt-file changes under the checkpoint process described in [decisions/pipeline-raci.md](decisions/pipeline-raci.md).
- Do not store real customer phone numbers, account identifiers, or secrets in examples, fixtures, or generated artifacts unless they are clearly synthetic and masked.
- Treat the high-blast-radius prompts in `.github/prompts/08-code-generator.prompt.md`, `.github/prompts/10-code-reviewer.prompt.md`, and `.github/prompts/11-security-agent.prompt.md` as controlled policy documents.
