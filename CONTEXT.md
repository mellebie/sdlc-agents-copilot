# Session Context — SDLC Pipeline Dashboard

> Load this file at the start of a new session to resume work without losing context.
> Tell Claude: "Read CONTEXT.md and continue from where we left off."

---

## What This Project Is

A dual-view pipeline tracking dashboard for the SDLC agent orchestrator defined in `CLAUDE.md`.

> **Design influence:** Agent identity blocks (persona, icon, principles) are styled after the [BMAD Method](https://github.com/bmad-code-org/BMAD-METHOD) persona pattern. The orchestration model is not BMAD — it is a sequential, gate-driven pipeline.

**Two files do all the work:**

| File | Purpose |
|------|---------|
| `pipeline-dashboard.html` | Single-file HTML/JS/CSS dashboard — open directly in browser, no server needed |
| `scripts/Sync-Dashboard.ps1` | PowerShell script that reads pipeline artifact files and patches the dashboard's embedded state |

**How it works:**
1. Run `Sync-Dashboard.ps1` from the project root after running any pipeline stage
2. The script reads `inputs/`, `outputs/`, `src/`, `tests/` file mtimes and content
3. It regex-patches a sentinel block inside `pipeline-dashboard.html` between `// <<PIPELINE_STATE_BEGIN>>` and `// <<PIPELINE_STATE_END>>`
4. Open/refresh `pipeline-dashboard.html` in a browser — no build step

---

## Dashboard Features (Current State — v3.0)

### Tab bar
- Two tabs: **Pipeline view** and **Artifact view**
- `v3.0` badge on the right of the tab bar
- Tab state tracked via `activeTab` JS variable; `switchTab(tab)` toggles visibility of `#pipeline-view` wrapper vs `#artifact-view` div
- `render()` calls `renderArtifactView()` automatically when artifact tab is active, keeping it fresh on state updates

### Pipeline view
- 5 phases: Requirements, Epics & user stories, Development, Testing, Security & quality
- Each phase shows: status badge, confidence badge, execution duration badge, agent count
- Phase duration uses `Get-DurationSeconds` — returns `-1` (rendered as `multi-session`) if span > 4h, `null` if < 30s
- Clicking a phase opens the detail panel (agent list + confidence chart sidebar)

### Agent rows (detail panel)
- Status: `pend` / `done` / `warn` / `err`
- Each completed agent shows an **execution duration badge** (not a date stamp)
- Duration calculated by `Get-AgentDuration($inputs, $outputs)`:
  - Finds latest input mtime ≤ latest output mtime as `$start`
  - Uses latest output mtime as `$end`
  - Falls back to output mtime spread if no inputs qualify
  - 5s minimum threshold; `-1` sentinel for > 4h (multi-session)
- `fmtDuration(-1)` → `'multi-session'`; `fmtDuration(null)` → nothing shown

### Confidence modal
- Clicking any agent's confidence badge opens a modal
- Modal shows:
  - Headline (from PS1 `reason` field)
  - Bulleted list of specific flagged items (e.g. SPEC names, story titles, risk titles)
  - "What to do next" action guidance (from `confActions` JS map in the HTML)
  - Link to the relevant output file
- PS1 enriches `reason` using `Format-BulletReason($headline, $titles, $totalCount, $moreFile)`
- JS uses `promptEl.innerHTML` (not `textContent`) — modal-prompt is styled as a plain div, not a code block

### Artifact view (v3.0 — implemented)
- Flat table of every pipeline output artifact, grouped by phase
- Summary bar: present / missing / pending counts + total
- Columns: File (monospaced), Agent, Status pill, Size, Last modified
- Status inferred from agent status (`done` → Present, `warn` → Conditions, `err` → Missing, `pend` → Pending)
- If `state.artifacts[path]` exists (populated by PS1), real `exists`/`size`/`mtime` values override the inferred status
- Directories (e.g. `src/TCPA.Api/`) rolled up to total size + latest mtime
- Missing artifacts still render as rows with `—` size/mtime — gaps are immediately visible
- JS functions: `avStatus()`, `renderArtifactView()` — called from `switchTab()` and `render()`

### Sync-Dashboard.ps1 — artifacts map (v3.0)
- New `Format-FileSize($bytes)` helper — formats bytes to B / KB / MB
- New `Get-ArtifactMeta($rel)` helper — returns `{ exists, size, mtime }` for file or directory
- New `$artifactsMap` built by iterating all agent output arrays after agent arrays are assembled
- `artifacts = $artifactsMap` added to `$state` before serialization
- JSON key order: `project → stats → inputs → artifacts → phases`

---

## BMAD Hybrid Pipeline — Option C (added this session)

A second execution mode that sits alongside the static pipeline. Purely additive — `agents/`, `CLAUDE.md`, dashboard, and scripts are untouched.

### Entry point
`@orchestrator.md` — tells the orchestrator to assess pipeline state, show the status dashboard, and load the appropriate SKILL.

### How it differs from the static pipeline

| | Static (Option A) | BMAD Hybrid (Option C) |
|---|---|---|
| Entry | `CLAUDE.md` + `agents/` | `orchestrator.md` + `skills/` |
| Agents 00–07 | Generate artifact → human edits | Converse with human → write near-complete artifact |
| Agents 08–13 | Autonomous | Autonomous (same behaviour) |
| Artifacts | Identical | Identical |
| Checkpoint gates | Text in CLAUDE.md | Phrase-locked in orchestrator — exact phrase required, no paraphrases |

### Agent tiers

| Tier | Agents | Mode |
|------|--------|------|
| Tier 1 | Alex (00), Jordan (02), Winston (04), Morgan (05), Riley (06), Casey (07) | Fully interactive — conversation first, artifact second |
| Tier 2 | Sam (01), Taylor (03) | Mostly autonomous — asks only if a genuine blocker is found |
| Tier 3 | Amelia–Sage (08–13) | Fully autonomous — same as static pipeline |

### Checkpoint gate phrases (exact, no paraphrases)
`Checkpoint 0 approved` → `Checkpoint 1 approved` → `Checkpoint 2 approved` → `Checkpoint 3 approved` → `Checkpoint 4 approved`

### New files
- `orchestrator.md` — the BMAD runtime; stateful conductor; enforces gates
- `skills/00-alex.md` through `skills/13-sage.md` — 16 SKILL files

---

## CLAUDE.md Skill Integrations (added this session)

Two `/superpowers` skill invocation points wired into the pipeline checkpoints:

| Checkpoint | Skill | Purpose |
|---|---|---|
| **Checkpoint 3** (after stories & tasks approved) | `/superpowers:writing-plans` | Translates `outputs/tasks.md` into a TDD-enforced agent-executable plan before Step 8 |
| **Checkpoint 4** (before human review) | `/pr-review-toolkit:review-pr all parallel` | Multi-agent review pass — 6 specialized agents in parallel before human sees findings |

---

## Key Implementation Details

### Sentinel pattern (PS1 → HTML)
```powershell
$pattern     = '(?s)(// <<PIPELINE_STATE_BEGIN>>.*?const PIPELINE_STATE = ).*?(;\s*// <<PIPELINE_STATE_END>>)'
$replacement = "// <<PIPELINE_STATE_BEGIN>>`r`n// Edit this object...\r\nconst PIPELINE_STATE = $compactJson;`r`n// <<PIPELINE_STATE_END>>"
$html = [regex]::Replace($html, $pattern, $replacement)
[System.IO.File]::WriteAllText($htmlPath, $html, [System.Text.Encoding]::UTF8)
```

### `Get-AgentDuration` logic
```powershell
function Get-AgentDuration($inputs, $outputs) {
    $inTimes  = @(Get-FileMtimes $inputs)
    $outTimes = @(Get-FileMtimes $outputs)
    if ($outTimes.Count -eq 0) { return $null }
    $end   = ($outTimes | Sort-Object -Descending | Select-Object -First 1)
    $start = $null
    if ($inTimes.Count -gt 0) {
        $start = ($inTimes | Where-Object { $_ -le $end } | Sort-Object -Descending | Select-Object -First 1)
    }
    if (-not $start) {
        if ($outTimes.Count -lt 2) { return $null }
        $start = ($outTimes | Sort-Object | Select-Object -First 1)
    }
    $delta = [int]($end - $start).TotalSeconds
    if ($delta -lt 5)     { return $null }
    if ($delta -gt 14400) { return -1 }
    return $delta
}
```

### `Format-BulletReason` (PS1)
```powershell
function Format-BulletReason($headline, $titles, $totalCount, $moreFile) {
    $t = @($titles)   # MUST wrap in @() — prevents scalar unwrapping on single match
    if ($t.Count -eq 0) { return $headline }
    $bullets = ($t | ForEach-Object { "- $_" }) -join "`n"
    $more    = if ($totalCount -gt $t.Count) { "`n(+ $($totalCount - $t.Count) more - see $moreFile)" } else { '' }
    return "$headline`n`n$bullets$more"
}
```

### `confActions` map (JS — inside `pipeline-dashboard.html`)
Maps agent ID (e.g. `'agent-03'`) → plain-English "What to do next" string.
Currently covers all 14 agents (00 through 13).
Located in the `<script>` block, near the `openConfFromEl` function.

### Modal rendering fix
`modal-prompt` element is styled as a code block by default. Must reset inline:
```javascript
showModal(`Confidence: ${levelLabel} — what to review`, '');
document.getElementById('modal-sub').textContent = '';
const promptEl = document.getElementById('modal-prompt');
promptEl.style.fontFamily = 'inherit';
promptEl.style.fontSize   = 'inherit';
promptEl.style.background = 'transparent';
promptEl.style.border     = 'none';
promptEl.style.padding    = '0';
promptEl.innerHTML = `...structured HTML...`;
```

---

## Known Issues / Gotchas

| Issue | Root Cause | Fix |
|-------|-----------|-----|
| PowerShell StrictMode: `$times.Count` fails on scalar | Function returns single value, not array | Always wrap with `@()`: `$times = @(Get-FileMtimes $rels)` |
| Em-dash `—` in PS1 string literals | UTF-8 em-dash causes parser errors | Replace with plain ASCII hyphen `-` |
| `Format-BulletReason` scalar unwrap | Single regex match returns string not array | `$t = @($titles)` inside function |
| Modal shows monospace font | `modal-prompt` CSS styles it as code block | Reset all styles inline in JS after `showModal` call |
| `showModal` strips HTML | Uses `textContent` internally | Call `showModal('title', '')` then set `innerHTML` separately |
| `serve` on port 3030 conflicts across sessions | Hardcoded `-l 3030` in launch.json args | Removed flag; `autoPort: true` in `.claude/launch.json` |
| Testing phase summary shows "All agents scored High confidence" even when Agent 09 is medium | Phase-level confidence aggregation in `Get-AgentConfidence` / phase summary string is not recomputed from individual agent scores — it uses a hardcoded template | **OPEN — follow-up fix needed.** Rewrite phase confidence reason to pull worst-scoring agent name when any agent is below high. Tracked in memory. |

---

## findskills MCP — Blocked

**Status: Blocked by enterprise policy.**

- `findskills-mcp` npm package (v0.1.25) was installed globally
- `~/.claude.json` manually updated with the server config (alongside working `playwright` entry)
- MCP server starts and responds correctly to the MCP `initialize` handshake
- `claude mcp add` returns: `Cannot add MCP server "findskills": not allowed by enterprise policy`
- Enterprise policy (Accenture-managed Claude Code) blocks unapproved MCP servers at the app level

**Workarounds:**
- Use findskills.org web interface directly
- Query the FindSkills REST API with a key from `npx findskills auth`
- Raise an IT request to whitelist `findskills-mcp`

---

## File Map

```
sdlc-agents/
├── CLAUDE.md                    # Static pipeline definition (Option A entry point)
├── CONTEXT.md                   # This file — loaded via @CONTEXT.md at top of CLAUDE.md
├── orchestrator.md              # BMAD hybrid runtime (Option C entry point)
├── README.md                    # User-facing docs — both options, quick start, agent summary
├── pipeline-dashboard.html      # The dashboard v3.0 (open in browser)
├── pipeline-dashboard-v2.html   # Previous version (kept for reference)
├── pipeline-state.json          # Last synced state (also embedded in HTML)
├── agents/                      # Static pipeline agent instruction files (00–13) — UNTOUCHED by Option C
├── skills/                      # BMAD hybrid SKILL files (00-alex through 13-sage)
│   ├── 00-alex.md  (Tier 1)    # Fully interactive
│   ├── 01-sam.md   (Tier 2)    # Mostly autonomous
│   ├── 02-jordan.md (Tier 1)   # Fully interactive
│   ├── 03-taylor.md (Tier 2)   # Mostly autonomous
│   ├── 04-winston.md (Tier 1)  # Fully interactive
│   ├── 05-morgan.md (Tier 1)   # Fully interactive
│   ├── 06-riley.md (Tier 1)    # Fully interactive
│   ├── 07-casey.md (Tier 1)    # Fully interactive
│   └── 08–13 (Tier 3)          # Fully autonomous — follow source agents/ exactly
├── scripts/
│   ├── Sync-Dashboard.ps1       # Patches dashboard state from artifact files
│   └── Convert-OutputsToHtml.ps1 # Generates HTML companion for each outputs/ markdown
├── inputs/                      # PRD / BRD inputs
├── outputs/                     # Pipeline artifacts (same for both options)
├── src/                         # Generated implementation (TCPA Regulatory Compliance API)
└── tests/                       # Generated tests
```

---

## TCPA Implementation — COMPLETE ✅

### Pipeline run: FULL DELIVERY — CLOSED

**GitHub PR:** https://github.com/mellebie/sdlc-agents/pull/4
**Branch:** `pipeline/tcpa-compliance-api-20260724`
**Final commit (Sage/PR):** `08fb630`
**Status:** PR open, awaiting human merge approval

### Plans executed
| Plan | File | Status |
|------|------|--------|
| TCPA Foundation | `docs/superpowers/plans/2026-07-23-tcpa-foundation.md` | COMPLETE (HEAD: 8f881be) |
| TCPA API | `docs/superpowers/plans/2026-07-23-tcpa-api.md` | COMPLETE (HEAD: ea5f6c1) |
| TCPA Inbound (MessageProcessor) | `docs/superpowers/plans/2026-07-23-tcpa-inbound.md` | COMPLETE (HEAD: baa3700) |
| TCPA Outbound (OutboundDispatcher) | `docs/superpowers/plans/2026-07-23-tcpa-outbound.md` | COMPLETE (HEAD: 97cd891) |

### Pipeline agents run
| Agent | Persona | Output | Commit |
|-------|---------|--------|--------|
| Agent 10 — Code Review | Blake | `outputs/review-findings.md` — CHANGES REQUIRED → all resolved | `26bc176` |
| Agent 11 — Security | Robin | `outputs/security-findings.md` — FAIL → PASS WITH CONDITIONS | `26bc176` |
| Agent 12 — Documentation | Jamie | `outputs/docs/` (README, api.md, architecture.md, operations.md, CHANGELOG.md) | `5ebc0a3` |
| Agent 13 — PR Assembler | Sage | `outputs/pr-description.md` + GitHub PR #4 | `08fb630` |

### Test counts (all green)
- TCPA.Api: 41/41
- TCPA.MessageProcessor: 22/22
- TCPA.OutboundDispatcher: 24/24

### Key implementation facts (carry forward)
- **EF Core 8:** `AddKeyedScoped<TcpaDbContext>("primary", factory)` — NOT `AddKeyedDbContext` (EF Core 9 only)
- **Transaction guard:** `if (_ctx.Database.IsRelational())` before `BeginTransactionAsync` — enables InMemory unit tests
- **Phone hashing:** Serilog params + AuditLog.Details JSON → `IPhoneNumberHasher.Hash()`. `AuditLog.PhoneNumber` column → raw E.164
- **ProcessedMessage PK:** composite `(MessageId, Endpoint)` — migration `20260724040710_ProcessedMessage_CompositeKey`
- **Branch:** main (all implementation commits merged; pipeline branch `pipeline/tcpa-compliance-api-20260724` awaits PR merge)
- **Docker not available locally** — Testcontainers integration tests skip but are wired

### Open items (for reviewer / next sprint)
- **DBA:** Provision `tcpa_app_user` SQL Server login before production deploy (SEC-006)
- **Legal:** Sign off opt-out confirmation SMS wording in SystemConfig (RISK-001)
- **IT/Ops:** Set all `REPLACE_IN_ENV` env vars before non-dev deployment
- **Sprint 2:** TCPA.ReportService (opt-out report generation — plan not yet written)
- **Sprint 2:** SEC-004 SSRF allowlist for `callbackUrl`; SEC-005 rate limiting on inbound/outbound endpoints

---

## Dashboard — This Session (2026-07-24)

### Commits pushed to `pipeline/tcpa-compliance-api-20260724`
| Commit | Change |
|--------|--------|
| `eff8032` | `Get-ArtifactStatus` downstream inference + `$cp4` accepts `warn` |
| `5915b06` | Persona subtitles in pipeline detail panel (10px italic muted) |
| `cd985d2` | Persona subtitles in artifact view agent column |

### Known open issue (tracked in memory)
- **Testing phase confidence aggregation** — phase shows "All agents scored High" despite Agent 09 being medium. Fix: rewrite phase confidence reason to pull worst-scoring agent name. See `memory/project_dashboard_confidence_bug.md`.

### Key PS1 encoding rule (re-confirmed this session)
- **Never use em dash `—` in PS1 string literals.** PS1 5.1 reads scripts as Windows-1252 by default; UTF-8 multibyte chars corrupt in the JSON output. Use ASCII hyphen ` - ` instead. Documented in Known Issues table above.

---

## Suggested Next Steps

1. **Merge PR #4** — after human review at https://github.com/mellebie/sdlc-agents/pull/4
2. **Fix Testing phase confidence aggregation** — `Sync-Dashboard.ps1` phase-level confidence reason not picking up Agent 09 medium score
3. **Write the Reporting plan** — `TCPA.ReportService`: opt-out audit report generation, CSV/Excel export
4. **Address SEC-004 / SEC-005** — SSRF allowlist and rate limiting (medium priority)

---

*Last updated: 2026-07-24*
