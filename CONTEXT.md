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

## Suggested Next Steps

1. **Run Option C end-to-end** — load `@orchestrator.md` and run a TCPA pipeline pass using the BMAD hybrid to validate the Tier 1 conversation model in practice
2. **Test confidence modal on a real pipeline run** — verify bullet items render correctly for agents with flagged content
3. **Deduplication in artifact view** — `outputs/task-log.md` appears twice (owned by Steps 08 and 09); consider deduplicating by path in `renderArtifactView()`
4. **Resolve Checkpoint 0** — `inputs/prd.md` was generated by Agent 00 this session; PD-001 through PD-005 and PD-007 are blocking; resolve before running Agent 01 / Sam
5. **findskills** — raise IT whitelist request, or use REST API directly if a use case comes up

---

*Last updated: 2026-07-23*
