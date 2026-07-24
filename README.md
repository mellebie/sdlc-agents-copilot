# SDLC Agent Pipeline — GitHub Copilot Edition

An end-to-end software delivery pipeline that transforms a Product Requirements Document (PRD) into delivered, tested, documented code — using GitHub Copilot as the execution runtime.

The pipeline is structured as a sequence of specialised agents. Each agent has a single responsibility, reads declared inputs, and writes a versioned output artifact that becomes the next agent's input. Human checkpoint gates separate analysis, design, and delivery phases.

---

## How It Works

The pipeline runs in two modes depending on the stage:

| Mode | Stages | How to run |
|------|--------|------------|
| **Copilot Chat — Agent Mode** | Steps 0–7 (analysis & planning) | Open a prompt file from `.github/prompts/` in VS Code → Copilot Chat → Agent mode |
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
Place your PRD at `inputs/prd.md`. If you have a BRD instead, place it at `inputs/brd.md` and run Step 0 first.

### Step 2 — Run Step 1 in Copilot Chat
1. In VS Code, open Copilot Chat (`Ctrl+Alt+I`)
2. Switch to **Agent** mode
3. Click the paperclip / attach prompt → select `.github/prompts/01-prd-analyst.prompt.md`
4. Send — Copilot reads `inputs/prd.md` and writes `outputs/requirements.md`

### Step 3 — Review and checkpoint
Review the output artifact. Answer any [AMBIGUOUS] or blocking questions before continuing to the next step. Human checkpoints are hard stops — do not skip them.

### Step 4 — Continue through Steps 2–7
Repeat the pattern: open the next prompt file, run in agent mode, review the output.

### Step 5 — Steps 8–13 via Copilot Coding Agent
1. On GitHub.com, go to **Issues → New Issue**
2. Select the SDLC template for the step you're running (e.g. `SDLC Step 08 — Code Generator`)
3. Tick all pre-condition checkboxes to confirm inputs exist
4. Create the issue and assign it to **Copilot**
5. Copilot Coding Agent will execute the stage and open a PR

---

## Pipeline Stages

| Step | Agent | Persona | Mode | Output |
|------|-------|---------|------|--------|
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

---

## Repository Structure

```
.github/
  copilot-instructions.md     # Global rules — loaded by Copilot on every request
  prompts/                    # 16 prompt files for Copilot Chat agent mode (Steps 0-7)
  ISSUE_TEMPLATE/             # 8 issue templates for Copilot Coding Agent (Steps 8-13)
.vscode/
  mcp.json                    # MCP server config — GitHub + Azure DevOps
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
| After Step 0 (BRD inputs only) | `Checkpoint 0 approved` |
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
