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
├── CLAUDE.md                    # Pipeline orchestration rules (14 stages) + skill integrations
├── CONTEXT.md                   # This file — loaded via @CONTEXT.md at top of CLAUDE.md
├── pipeline-dashboard.html      # The dashboard v3.0 (open in browser)
├── pipeline-dashboard-v2.html   # Previous version (kept for reference)
├── pipeline-state.json          # Last synced state (also embedded in HTML)
├── scripts/
│   └── Sync-Dashboard.ps1       # Patches dashboard state from artifact files
├── agents/                      # Agent instruction files (00–13)
├── inputs/                      # PRD / BRD inputs
├── outputs/                     # Pipeline artifacts
├── src/                         # Generated implementation (TCPA Regulatory Compliance API)
└── tests/                       # Generated tests
```

---

## Suggested Next Steps

1. **Test confidence modal on a real pipeline run** — verify bullet items render correctly for agents with flagged content
2. **Deduplication in artifact view** — `outputs/task-log.md` appears twice (owned by Steps 08 and 09); consider deduplicating by path in `renderArtifactView()`
3. **PS1 auto-hook** — wire `Sync-Dashboard.ps1` into a Claude Code PostToolUse hook so it runs automatically after each agent write
4. **findskills** — raise IT whitelist request, or use REST API directly if a use case comes up

---

*Last updated: 2026-07-07*
