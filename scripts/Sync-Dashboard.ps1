#Requires -Version 5.1
<#
.SYNOPSIS
    Reads pipeline output artifacts and syncs pipeline-state.json and
    pipeline-dashboard.html with the current completion status of each agent.
.DESCRIPTION
    Run automatically via Claude Code PostToolUse hook, or manually:
        .\scripts\Sync-Dashboard.ps1
#>

param([string]$Root = (Split-Path $PSScriptRoot -Parent))

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Helpers ───────────────────────────────────────────────────────────────────

function Exists($rel)              { Test-Path (Join-Path $Root $rel) }
function ReadContent($rel)         { $p = Join-Path $Root $rel; if (Test-Path $p) { Get-Content $p -Raw } else { '' } }
function HasFiles($rel, $pat='*')  { $d = Join-Path $Root $rel; if (-not (Test-Path $d)) { return $false }; $null -ne (Get-ChildItem $d -Recurse -File -Filter $pat -ErrorAction SilentlyContinue | Select-Object -First 1) }

function Get-FileMtimes($rels) {
    # Returns all LastWriteTime values across a list of file/folder paths
    $times = [System.Collections.Generic.List[datetime]]::new()
    foreach ($rel in $rels) {
        $p = Join-Path $Root $rel
        if     (Test-Path $p -PathType Leaf)      { $times.Add((Get-Item $p).LastWriteTime) }
        elseif (Test-Path $p -PathType Container) {
            Get-ChildItem $p -Recurse -File -ErrorAction SilentlyContinue |
                ForEach-Object { $times.Add($_.LastWriteTime) }
        }
    }
    return $times
}

function Format-FileSize($bytes) {
    if ($null -eq $bytes -or $bytes -eq 0) { return $null }
    if ($bytes -lt 1024)    { return "$bytes B" }
    if ($bytes -lt 1048576) { return "$([math]::Round($bytes / 1024, 1)) KB" }
    return "$([math]::Round($bytes / 1048576, 1)) MB"
}

function Get-ArtifactMeta($rel) {
    $p = Join-Path $Root $rel
    if (Test-Path $p -PathType Leaf) {
        $f = Get-Item $p
        return [ordered]@{ exists = $true; size = (Format-FileSize $f.Length); mtime = $f.LastWriteTime.ToString('yyyy-MM-dd HH:mm') }
    } elseif (Test-Path $p -PathType Container) {
        $files = @(Get-ChildItem $p -Recurse -File -ErrorAction SilentlyContinue)
        if ($files.Count -gt 0) {
            $totalBytes  = ($files | Measure-Object -Property Length -Sum).Sum
            $latestMtime = ($files | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime
            return [ordered]@{ exists = $true; size = (Format-FileSize $totalBytes); mtime = $latestMtime.ToString('yyyy-MM-dd HH:mm') }
        }
        return [ordered]@{ exists = $false; size = $null; mtime = $null }
    }
    return [ordered]@{ exists = $false; size = $null; mtime = $null }
}

function Get-DurationSeconds($rels) {
    # Duration = span between earliest and latest file mtime in the set.
    # Returns $null if < 30s (noise), -1 if > 14400s (multi-session), else seconds.
    $times = @(Get-FileMtimes $rels)
    if ($times.Count -lt 2) { return $null }
    $min = ($times | Sort-Object | Select-Object -First 1)
    $max = ($times | Sort-Object -Descending | Select-Object -First 1)
    $delta = [int]($max - $min).TotalSeconds
    if ($delta -lt 30)    { return $null }
    if ($delta -gt 14400) { return -1 }
    return $delta
}

function Get-AgentDuration($inputs, $outputs) {
    # end   = max(output mtimes)
    # start = latest input mtime that is <= end  (ignores stale re-generated prerequisites)
    # Falls back to earliest output when no qualifying input found.
    # Returns $null if < 5s, -1 if > 14400s (multi-session), else seconds.
    $inTimes  = @(Get-FileMtimes $inputs)
    $outTimes = @(Get-FileMtimes $outputs)
    if ($outTimes.Count -eq 0) { return $null }
    $end = ($outTimes | Sort-Object -Descending | Select-Object -First 1)
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

function Get-PhaseDuration($agents) {
    $allRels = @()
    foreach ($a in $agents) {
        if (-not $a.Contains('checkpoint') -and $a.outputs) { $allRels += $a.outputs }
    }
    return Get-DurationSeconds $allRels
}

function Get-CompletedAt($rel) {
    $p = Join-Path $Root $rel
    if (-not (Test-Path $p)) { return $null }
    $c = Get-Content $p -Raw
    if ($c -match 'Generated:\s*(\d{4}-\d{2}-\d{2})') { return $Matches[1] }
    return (Get-Item $p).LastWriteTime.ToString('yyyy-MM-dd')
}

function Get-ArtifactStatus($rel) {
    if (-not (Exists $rel)) { return 'pend' }
    $c = ReadContent $rel
    if ($c -match 'Overall Verdict:\s*CHANGES REQUIRED')           { return 'err'  }
    if ($c -match 'Overall Security Verdict:\s*FAIL')              { return 'err'  }
    if ($c -match 'Overall Verdict:\s*APPROVED WITH CONDITIONS')   { return 'warn' }
    if ($c -match 'Overall Security Verdict:\s*PASS WITH CONDITIONS') { return 'warn' }
    if ($c -match 'Status:\s*(APPROVED WITH CONDITIONS|PASS WITH CONDITIONS)') { return 'warn' }
    if ($c -match 'Status:\s*DRAFT')                               { return 'warn' }
    return 'done'
}

# ── Confidence telemetry ──────────────────────────────────────────────────────

function Count-Flags($content, $pattern) {
    if (-not $content) { return 0 }
    @([regex]::Matches($content, $pattern)).Count
}

function Get-SectionHeadings($content, $sectionTitle, $headingPattern, $max) {
    # Extract headings from a specific ## section; falls back to whole-doc search
    if (-not $content) { return @() }
    $sectionMatch = [regex]::Match($content, "(?m)^##\s+$([regex]::Escape($sectionTitle))\s*$")
    $scope = if ($sectionMatch.Success) {
        $rest = $content.Substring($sectionMatch.Index + $sectionMatch.Length)
        $nxt  = [regex]::Match($rest, '(?m)^## ')
        if ($nxt.Success) { $rest.Substring(0, $nxt.Index) } else { $rest }
    } else { $content }
    @([regex]::Matches($scope, $headingPattern) | ForEach-Object { $_.Groups[1].Value.Trim() } | Select-Object -First $max)
}

function Format-BulletReason($headline, $titles, $totalCount, $moreFile) {
    $t = @($titles)
    if ($t.Count -eq 0) { return $headline }
    $bullets = ($t | ForEach-Object { "- $_" }) -join "`n"
    $more    = if ($totalCount -gt $t.Count) { "`n(+ $($totalCount - $t.Count) more - see $moreFile)" } else { '' }
    return "$headline`n`n$bullets$more"
}

function Check-SelfReported($content) {
    if ($content -and $content -match '(?m)^Confidence:\s*(HIGH|MEDIUM|LOW)\s*$') {
        $lvl = $Matches[1].ToLower()
        return [ordered]@{ level = $lvl; reason = 'Agent self-reported' }
    }
    return $null
}

function Get-AgentConfidence($id) {
    switch ($id) {
        '00' {
            $c = ReadContent 'inputs/prd.md'
            $sr = Check-SelfReported $c; if ($sr) { return $sr }
            if (-not $c) { return [ordered]@{ level='low'; reason='prd.md not found' } }
            $n = Count-Flags $c '\[PRODUCT-DECISION-NEEDED'
            if ($n -eq 0) { return [ordered]@{ level='high'; reason='No product decisions pending' } }
            if ($n -le 3) { return [ordered]@{ level='medium'; reason="$n product decision(s) pending" } }
            return [ordered]@{ level='low'; reason="$n product decisions unresolved" }
        }
        '01' {
            $c = ReadContent 'outputs/requirements.md'
            $sr = Check-SelfReported $c; if ($sr) { return $sr }
            if (-not $c) { return [ordered]@{ level='low'; reason='requirements.md not found' } }
            # [RESOLVED:...] flags do not match this pattern - only unresolved AMBIGUOUS/GAP count
            $n = Count-Flags $c '\[(AMBIGUOUS|GAP)'
            # Cross-check clarifications: if all blocking questions answered, remaining flags are less severe
            $cl = ReadContent 'outputs/clarifications.md'
            $clarifiedAll = $cl -and ((Count-Flags $cl '_\[human to fill in\]_') -eq 0)
            if ($n -eq 0) { return [ordered]@{ level='high'; reason='No ambiguities or gaps flagged' } }
            if ($clarifiedAll) {
                if ($n -le 2) { return [ordered]@{ level='medium'; reason="$n open flag(s) - all clarifications answered; resolve remaining flags in requirements.md" } }
                return [ordered]@{ level='low'; reason="$n open flags remain in requirements.md - update resolved flags to [RESOLVED:...]" }
            }
            if ($n -le 3) { return [ordered]@{ level='medium'; reason="$n ambiguit(ies)/gap(s) flagged" } }
            return [ordered]@{ level='low'; reason="$n open flags require resolution" }
        }
        '02' {
            $c = ReadContent 'outputs/clarifications.md'
            $sr = Check-SelfReported $c; if ($sr) { return $sr }
            if (-not $c) { return [ordered]@{ level='low'; reason='clarifications.md not found' } }
            $unanswered = Count-Flags $c '_\[human to fill in\]_'
            if ($unanswered -eq 0) { return [ordered]@{ level='high'; reason='All blocking questions answered' } }
            if ($unanswered -le 2) { return [ordered]@{ level='medium'; reason="$unanswered question(s) awaiting answers" } }
            return [ordered]@{ level='low'; reason="$unanswered blocking question(s) unanswered" }
        }
        '03' {
            $c = ReadContent 'outputs/specs.md'
            $sr = Check-SelfReported $c; if ($sr) { return $sr }
            if (-not $c) { return [ordered]@{ level='low'; reason='specs.md not found' } }
            $n = Count-Flags $c '\[COMPLEX'
            if ($n -eq 0) { return [ordered]@{ level='high'; reason='No complex specs flagged' } }
            # Find SPEC headings whose block contains [COMPLEX within ~10 lines
            $titles  = @([regex]::Matches($c, '(?ms)^### (SPEC-\d+:[^\r\n]+)(?:(?!^###).){0,500}\[COMPLEX') |
                         ForEach-Object { $_.Groups[1].Value.Trim() } | Select-Object -First 3)
            $headline = "$n complex spec$(if($n -ne 1){'s'}) flagged - review with architect before dev"
            $reason  = Format-BulletReason $headline $titles $n 'outputs/specs.md'
            if ($n -le 3) { return [ordered]@{ level='medium'; reason=$reason } }
            return [ordered]@{ level='low'; reason=$reason }
        }
        '04' {
            $c = ReadContent 'outputs/architecture.md'
            $sr = Check-SelfReported $c; if ($sr) { return $sr }
            if (-not $c) { return [ordered]@{ level='low'; reason='architecture.md not found' } }
            # Count unique ARCH-RISK IDs (raw Count-Flags double-counts inline refs + table rows)
            $allIds = @([regex]::Matches($c, 'ARCH-RISK-\d+') | ForEach-Object { $_.Value } | Sort-Object -Unique)
            $n = $allIds.Count
            if ($n -eq 0) { return [ordered]@{ level='high'; reason='No architectural risks flagged' } }
            # Cross-check risks.md: count only ARCH-RISK items that still have Status: Open
            $r = ReadContent 'outputs/risks.md'
            $openCount = $n  # assume all open if risks.md missing
            if ($r) {
                $blocks = @([regex]::Matches($r, '(?ms)^### RISK-\d+:.*?(?=^### RISK-\d+:|\z)') | ForEach-Object { $_.Value })
                $openCount = @($blocks | Where-Object { $_ -match 'ARCH-RISK-\d+' -and $_ -match 'Status:\s*Open' }).Count
            }
            if ($openCount -eq 0) { return [ordered]@{ level='high'; reason="All $n architectural risk(s) mitigated or accepted in risks.md" } }
            $titles  = Get-SectionHeadings $c 'Architectural Risks' '(?m)\|\s*ARCH-RISK-\d+\s*\|\s*([^|]+?)\s*\|' 3
            $headline = "$openCount of $n architectural risk(s) still Open - review risks.md before proceeding"
            $reason  = Format-BulletReason $headline $titles $openCount 'outputs/architecture.md'
            if ($openCount -le 2) { return [ordered]@{ level='medium'; reason=$reason } }
            return [ordered]@{ level='low'; reason=$reason }
        }
        '05' {
            $c = ReadContent 'outputs/risks.md'
            $sr = Check-SelfReported $c; if ($sr) { return $sr }
            if (-not $c) { return [ordered]@{ level='low'; reason='risks.md not found' } }
            $critHigh = Get-SectionHeadings $c 'Critical & High Risks' '(?m)^### RISK-\d+:\s*(.+)' 3
            $nCrit    = Count-Flags $c '(?i)\|\s*Critical\s*\|'
            $nHigh    = Count-Flags $c '(?i)\|\s*High\s*\|'
            if ($c -match 'NO.GO') {
                $headline = "NO-GO - $nCrit critical, $nHigh high risk$(if(($nCrit+$nHigh)-ne 1){'s'}) open"
                return [ordered]@{ level='low';    reason=(Format-BulletReason $headline $critHigh ($nCrit+$nHigh) 'outputs/risks.md') }
            }
            if ($c -match 'GO WITH CONDITIONS') {
                $headline = "GO WITH CONDITIONS - $nHigh high risk$(if($nHigh -ne 1){'s'}) remain open"
                return [ordered]@{ level='medium'; reason=(Format-BulletReason $headline $critHigh $nHigh 'outputs/risks.md') }
            }
            if ($c -match '\bGO\b') { return [ordered]@{ level='high'; reason='GO recommendation - all critical risks mitigated' } }
            return [ordered]@{ level='medium'; reason='Risk assessment complete - check recommendation' }
        }
        '06' {
            $c = ReadContent 'outputs/stories.md'
            $sr = Check-SelfReported $c; if ($sr) { return $sr }
            if (-not $c) { return [ordered]@{ level='low'; reason='stories.md not found' } }
            $complex = Count-Flags $c '\[COMPLEX\]'
            $spike   = Count-Flags $c '\[SPIKE\]'
            $total   = $complex + $spike
            if ($total -eq 0) { return [ordered]@{ level='high'; reason='No complex or spike stories' } }
            $flagged = Get-SectionHeadings $c $null '(?m)^### (STORY-\d+:[^\r\n]+\[(?:COMPLEX|SPIKE)\])' 3
            $headline = "$complex complex, $spike spike $(if($total -eq 1){'story'}else{'stories'}) flagged - consider splitting before sprint"
            $reason  = Format-BulletReason $headline $flagged $total 'outputs/stories.md'
            if ($total -le 3) { return [ordered]@{ level='medium'; reason=$reason } }
            return [ordered]@{ level='low'; reason=$reason }
        }
        '07' {
            $c = ReadContent 'outputs/tasks.md'
            $sr = Check-SelfReported $c; if ($sr) { return $sr }
            if (-not $c) { return [ordered]@{ level='low'; reason='tasks.md not found' } }
            $decisions = Count-Flags $c '\[DECISION-NEEDED:'
            # BLOCKED-BY at story level are sequencing dependencies, not unresolved blockers.
            # Once implementation has started (task-log.md exists), they are resolved by definition.
            $codeStarted = Exists 'outputs/task-log.md'
            $blocked   = if ($codeStarted) { 0 } else { Count-Flags $c '\[BLOCKED-BY' }
            $total     = $decisions + $blocked
            if ($total -eq 0) {
                if ($codeStarted) { return [ordered]@{ level='high'; reason='No decisions pending - all dependencies resolved by implementation' } }
                return [ordered]@{ level='high'; reason='No blocked or undecided tasks' }
            }
            $flagged  = Get-SectionHeadings $c $null '(?m)^### (TASK-\d+:[^\r\n]+\[(?:DECISION-NEEDED|BLOCKED-BY)[^\]]*\])' 3
            $headline = "$decisions decision$(if($decisions -ne 1){'s'}) pending, $blocked blocked task$(if($blocked -ne 1){'s'}) - resolve before dev starts"
            $reason   = Format-BulletReason $headline $flagged $total 'outputs/tasks.md'
            if ($total -le 3) { return [ordered]@{ level='medium'; reason=$reason } }
            return [ordered]@{ level='low'; reason=$reason }
        }
        '08' {
            $c = ReadContent 'outputs/task-log.md'
            $sr = Check-SelfReported $c; if ($sr) { return $sr }
            if (-not $c) { return [ordered]@{ level='low'; reason='task-log.md not found' } }
            $completed = Count-Flags $c '\*\*Status:\*\* Complete'
            $notes     = Count-Flags $c '(?i)Known Limitations|Deviations from Spec'
            if ($completed -gt 0 -and $notes -eq 0) { return [ordered]@{ level='high';   reason="$completed task(s) complete, no issues noted" } }
            if ($completed -gt 0)                   { return [ordered]@{ level='medium'; reason="$completed task(s) complete, implementation notes logged" } }
            return [ordered]@{ level='low'; reason='No completed tasks found in log' }
        }
        '09' {
            $c = ReadContent 'outputs/task-log.md'
            $sr = Check-SelfReported $c; if ($sr) { return $sr }
            if (-not $c) { return [ordered]@{ level='low'; reason='task-log.md not found' } }
            $covered = Count-Flags $c 'ACs Covered:'
            $gaps    = Count-Flags $c 'Known Coverage Gaps:'
            if ($covered -gt 0 -and $gaps -eq 0) { return [ordered]@{ level='high';   reason='All ACs covered, no gaps noted' } }
            if ($covered -gt 0)                  { return [ordered]@{ level='medium'; reason="Coverage gaps noted in $gaps test suite(s)" } }
            return [ordered]@{ level='low'; reason='No test coverage recorded in task log' }
        }
        '9b' {
            $hasJ = HasFiles 'tests/functional/journeys'
            $hasC = HasFiles 'tests/functional/contracts'
            $hasS = HasFiles 'tests/functional/smoke'
            $cnt  = @($hasJ,$hasC,$hasS | Where-Object { $_ }).Count
            if ($cnt -eq 3) { return [ordered]@{ level='high';   reason='Journey, contract and smoke tests all present' } }
            if ($cnt -ge 1) { return [ordered]@{ level='medium'; reason="$cnt of 3 functional test categories present" } }
            return [ordered]@{ level='low'; reason='No functional test categories found' }
        }
        '9c' {
            $hasCsv  = HasFiles 'tests' '*.csv'
            $hasXlsx = HasFiles 'tests' '*.xlsx'
            if ($hasCsv -and $hasXlsx) { return [ordered]@{ level='high';   reason='Test plan CSV and Excel both generated' } }
            if ($hasCsv)               { return [ordered]@{ level='medium'; reason='CSV generated - Excel not found' } }
            return [ordered]@{ level='low'; reason='Test plan not generated' }
        }
        '10' {
            $c = ReadContent 'outputs/review-findings.md'
            $sr = Check-SelfReported $c; if ($sr) { return $sr }
            if (-not $c) { return [ordered]@{ level='low'; reason='review-findings.md not found' } }
            $nBlock  = Count-Flags $c '\[BLOCKING\]'
            $nImport = Count-Flags $c '\[IMPORTANT\]'
            if ($c -match 'Overall Verdict:\s*CHANGES REQUIRED') {
                $titles   = Get-SectionHeadings $c 'Blocking Findings' '(?m)^### CR-\d+:\s*(.+)' 3
                $headline = "CHANGES REQUIRED - $nBlock blocking, $nImport important finding$(if(($nBlock+$nImport)-ne 1){'s'})"
                return [ordered]@{ level='low'; reason=(Format-BulletReason $headline $titles $nBlock 'outputs/review-findings.md') }
            }
            if ($c -match 'Overall Verdict:\s*APPROVED WITH CONDITIONS') {
                $titles   = Get-SectionHeadings $c 'Important Findings' '(?m)^### CR-\d+:\s*(.+)' 3
                $headline = "APPROVED WITH CONDITIONS - $nImport open condition$(if($nImport -ne 1){'s'})"
                return [ordered]@{ level='medium'; reason=(Format-BulletReason $headline $titles $nImport 'outputs/review-findings.md') }
            }
            if ($c -match 'Overall Verdict:\s*APPROVED') { return [ordered]@{ level='high'; reason='APPROVED - no blocking findings' } }
            return [ordered]@{ level='medium'; reason='Review complete - check verdict' }
        }
        '11' {
            $c = ReadContent 'outputs/security-findings.md'
            $sr = Check-SelfReported $c; if ($sr) { return $sr }
            if (-not $c) { return [ordered]@{ level='low'; reason='security-findings.md not found' } }
            $nBlocking = Count-Flags $c '\[SECURITY-BLOCKING\]'
            $nHigh     = Count-Flags $c '\[SECURITY-HIGH\]'
            if ($c -match 'Overall Security Verdict:\s*FAIL') {
                $titles   = Get-SectionHeadings $c 'Security-Blocking Findings' '(?m)^### SEC-\d+:\s*(.+)' 3
                $headline = "FAIL - $nBlocking security-blocking, $nHigh high finding$(if(($nBlocking+$nHigh)-ne 1){'s'})"
                return [ordered]@{ level='low'; reason=(Format-BulletReason $headline $titles $nBlocking 'outputs/security-findings.md') }
            }
            if ($c -match 'Overall Security Verdict:\s*PASS WITH CONDITIONS') {
                $titles   = Get-SectionHeadings $c 'High Findings' '(?m)^### SEC-\d+:\s*(.+)' 3
                $headline = "PASS WITH CONDITIONS - $nHigh open condition$(if($nHigh -ne 1){'s'})"
                return [ordered]@{ level='medium'; reason=(Format-BulletReason $headline $titles $nHigh 'outputs/security-findings.md') }
            }
            if ($c -match 'Overall Security Verdict:\s*PASS') { return [ordered]@{ level='high'; reason='PASS - no security-blocking findings' } }
            return [ordered]@{ level='medium'; reason='Security review complete - check verdict' }
        }
        '12' {
            $docFiles = @('outputs/docs/README.md','outputs/docs/api.md','outputs/docs/operations.md','outputs/docs/CHANGELOG.md')
            $found = @($docFiles | Where-Object { Exists $_ }).Count
            if ($found -eq 4) { return [ordered]@{ level='high';   reason='All 4 documentation artifacts present' } }
            if ($found -ge 2) { return [ordered]@{ level='medium'; reason="$found of 4 documentation files present" } }
            return [ordered]@{ level='low'; reason="Only $found documentation file(s) found" }
        }
        '13' {
            $c = ReadContent 'outputs/pr-description.md'
            $sr = Check-SelfReported $c; if ($sr) { return $sr }
            if (-not $c) { return [ordered]@{ level='low'; reason='pr-description.md not found' } }
            # Match only verdict-announcing lines — avoids false positives from section headers
            # that repeat the verdict text (e.g. "Open conditions (PASS WITH CONDITIONS):")
            $conditions = Count-Flags $c '\*\*Verdict:.*WITH CONDITIONS'
            if ($conditions -eq 0) { return [ordered]@{ level='high';   reason='PR ready - no conditions outstanding' } }
            if ($conditions -le 2) { return [ordered]@{ level='medium'; reason="$conditions review(s) approved WITH CONDITIONS - acknowledge before merge" } }
            return [ordered]@{ level='low'; reason="$conditions open conditions before merge" }
        }
        default { return [ordered]@{ level='medium'; reason='Confidence not assessed' } }
    }
}

function Get-PhaseConfidence($agents) {
    # Weighted average: High=2, Medium=1, Low=0 — excludes pending agents
    # >= 1.5 -> High, >= 0.5 -> Medium, < 0.5 -> Low
    $scored = @($agents | Where-Object { -not $_.Contains('checkpoint') -and $_.Contains('confidence') -and $null -ne $_.confidence -and $_.confidence.level -ne 'pend' })
    if ($scored.Count -eq 0) { return [ordered]@{ level='high'; reason='No agents scored yet' } }

    $scoreMap = @{ high=2; medium=1; low=0 }
    $total    = ($scored | ForEach-Object { $scoreMap[$_.confidence.level] } | Measure-Object -Sum).Sum
    $avg      = $total / $scored.Count

    $lowNames  = @($scored | Where-Object { $_.confidence.level -eq 'low'    } | ForEach-Object { $_.name })
    $medNames  = @($scored | Where-Object { $_.confidence.level -eq 'medium' } | ForEach-Object { $_.name })

    if ($avg -ge 1.5) {
        $reason = 'All agents scored High confidence'
        return [ordered]@{ level='high'; reason=$reason }
    } elseif ($avg -ge 0.5) {
        $dragging = if ($lowNames.Count -gt 0) { $lowNames -join ', ' } else { $medNames -join ', ' }
        return [ordered]@{ level='medium'; reason="Weighted average Medium - dragged by: $dragging" }
    } else {
        $dragging = $lowNames -join ', '
        return [ordered]@{ level='low'; reason="Weighted average Low - dragged by: $dragging" }
    }
}

function New-Agent($id, $name, $file, $status, $sourceRel, $inputs, $outputs, $noteType, $noteText) {
    $ca   = if ($status -ne 'pend') { Get-CompletedAt $sourceRel } else { $null }
    $note = if ($noteType)          { [ordered]@{ type = $noteType; text = $noteText } } else { $null }
    $conf = if ($status -ne 'pend') { Get-AgentConfidence $id } else { [ordered]@{ level='pend'; reason='Agent not yet run' } }
    # max(input mtimes) → max(output mtimes): captures AI processing time, avoids cross-checkpoint inflation.
    $dur  = if ($status -ne 'pend') { Get-AgentDuration $inputs $outputs } else { $null }
    [ordered]@{
        id              = $id
        name            = $name
        file            = $file
        status          = $status
        completedAt     = $ca
        durationSeconds = $dur
        inputs          = $inputs
        outputs         = $outputs
        note            = $note
        confidence      = $conf
    }
}

function New-Checkpoint($label, $passed, $sourceRel) {
    [ordered]@{
        checkpoint = $true
        label      = $label
        passed     = $passed
        approvedAt = if ($passed -and $sourceRel) { Get-CompletedAt $sourceRel } else { $null }
    }
}

function Get-PhaseStatus($statuses) {
    if ($statuses -contains 'err')  { return 'err'  }
    if ($statuses -contains 'warn') { return 'warn' }
    if ($statuses -contains 'pend') { return 'pend' }
    return 'done'
}

# ── Detect each stage ─────────────────────────────────────────────────────────

$s00  = if (Exists 'inputs/prd.md')  { 'done' } else { 'pend' }
$s01  = Get-ArtifactStatus 'outputs/requirements.md'
$s02  = Get-ArtifactStatus 'outputs/clarifications.md'
$s03  = Get-ArtifactStatus 'outputs/specs.md'
$s04  = Get-ArtifactStatus 'outputs/architecture.md'
$s05  = Get-ArtifactStatus 'outputs/risks.md'
$s06  = Get-ArtifactStatus 'outputs/stories.md'
$s07  = Get-ArtifactStatus 'outputs/tasks.md'

$s08  = if ((Exists 'outputs/task-log.md') -and (HasFiles 'src' '*.cs')) { 'done' }
        elseif (Exists 'outputs/task-log.md')                            { 'warn' }
        else                                                              { 'pend' }

$s09  = if (HasFiles 'tests' '*.cs') { 'done' } else { 'pend' }

$s9b  = if   (HasFiles 'tests/functional') { 'done' }
        elseif ($s09 -eq 'done')           { 'err'  }
        else                               { 'pend' }

$hasPlan = (HasFiles 'tests' '*.csv') -or (HasFiles 'tests' '*.xlsx')
$s9c  = if   ($hasPlan)             { 'done' }
        elseif ($s09 -eq 'done')    { 'err'  }
        else                        { 'pend' }

$s10  = Get-ArtifactStatus 'outputs/review-findings.md'
$s11  = Get-ArtifactStatus 'outputs/security-findings.md'
$s12  = if (HasFiles 'outputs/docs') { 'done' } else { 'pend' }
$s13  = Get-ArtifactStatus 'outputs/pr-description.md'

# ── Checkpoints ───────────────────────────────────────────────────────────────

$cp0 = ($s00 -eq 'done')
$cp1 = ($s02 -ne 'pend') -and -not ((ReadContent 'outputs/clarifications.md') -match 'AWAITING HUMAN RESPONSE')
$cp2 = ($s04 -ne 'pend') -and ($s05 -ne 'pend')
$cp3 = ($s06 -ne 'pend') -and ($s07 -ne 'pend')
$cp4 = ($s10 -eq 'done') -and ($s11 -eq 'done')

# ── Phase statuses ────────────────────────────────────────────────────────────

$phReq  = Get-PhaseStatus @($s00,$s01,$s02,$s03,$s04,$s05)
$phStor = Get-PhaseStatus @($s06,$s07)
$phDev  = Get-PhaseStatus @($s08)
$phTest = Get-PhaseStatus @($s09,$s9b,$s9c)
$phQual = Get-PhaseStatus @($s10,$s11,$s12,$s13)

$allStatuses   = @($s00,$s01,$s02,$s03,$s04,$s05,$s06,$s07,$s08,$s09,$s9b,$s9c,$s10,$s11,$s12,$s13)
$agentsRun     = ($allStatuses | Where-Object { $_ -ne 'pend' }).Count
$phasesComplete = @($phReq,$phStor,$phDev,$phTest,$phQual) | Where-Object { $_ -eq 'done' } | Measure-Object | Select-Object -ExpandProperty Count

# Parse task count/hours from tasks.md
$taskContent   = ReadContent 'outputs/tasks.md'
$totalTasks    = if ($taskContent -match 'Total tasks:\s*(\d+)')            { [int]$Matches[1] } else { 0 }
$totalHours    = if ($taskContent -match 'Total estimated hours:\s*(\d+)')  { [int]$Matches[1] } else { 0 }
$taskNote      = if ($s07 -ne 'pend') { "$totalTasks tasks - $totalHours estimated hours - 0 blocked tasks" } else { '' }

# ── Build agent arrays ────────────────────────────────────────────────────────

$reqAgents = @(
    (New-Agent '00' 'BRD to PRD bridge'  'agents/00-brd-to-prd.md'      $s00 'inputs/prd.md'               @('inputs/brd.doc')                                          @('inputs/prd.md')              $null   $null)
    (New-Checkpoint 'Checkpoint 0 - PRD review approved'          $cp0   'inputs/prd.md')
    (New-Agent '01' 'PRD analyst'         'agents/01-prd-analyst.md'     $s01 'outputs/requirements.md'     @('inputs/prd.md')                                           @('outputs/requirements.md')    $null   $null)
    (New-Agent '02' 'Clarification'       'agents/02-clarification.md'   $s02 'outputs/clarifications.md'   @('outputs/requirements.md')                                 @('outputs/clarifications.md')  $null   $null)
    (New-Checkpoint 'Checkpoint 1 - requirements sign-off'        $cp1   'outputs/clarifications.md')
    (New-Agent '03' 'Spec decomposer'     'agents/03-spec-decomposer.md' $s03 'outputs/specs.md'            @('outputs/requirements.md','outputs/clarifications.md')     @('outputs/specs.md')           $null   $null)
    (New-Agent '04' 'Architecture'        'agents/04-architecture.md'    $s04 'outputs/architecture.md'     @('outputs/specs.md','outputs/requirements.md')              @('outputs/architecture.md')    $null   $null)
    (New-Agent '05' 'Risk assessment'     'agents/05-risk-assessment.md' $s05 'outputs/risks.md'            @('outputs/specs.md','outputs/architecture.md')              @('outputs/risks.md')           $(if($s05 -ne 'pend'){'info'}else{$null}) 'Risk assessment complete. Review risks.md for GO / NO-GO recommendation.')
    (New-Checkpoint 'Checkpoint 2 - architecture & risk approved' $cp2   'outputs/risks.md')
)

$storAgents = @(
    (New-Agent '06' 'Story writer'    'agents/06-story-writer.md'    $s06 'outputs/stories.md' @('outputs/specs.md','outputs/architecture.md','outputs/risks.md') @('outputs/stories.md') $null $null)
    (New-Agent '07' 'Task breakdown'  'agents/07-task-breakdown.md'  $s07 'outputs/tasks.md'   @('outputs/stories.md','outputs/architecture.md')                  @('outputs/tasks.md')   $(if($s07 -ne 'pend'){'info'}else{$null}) $taskNote)
    (New-Checkpoint 'Checkpoint 3 - stories & tasks approved' $cp3 'outputs/tasks.md')
)

$devAgents = @(
    (New-Agent '08' 'Code generator' 'agents/08-code-generator.md' $s08 'outputs/task-log.md' @('outputs/tasks.md','outputs/architecture.md','outputs/specs.md','src/ (existing)') @('src/TCPA.Api/','src/TCPA.Scheduler/','outputs/task-log.md') $(if($s08 -ne 'pend'){'info'}else{$null}) 'Implementation files generated. Review task-log.md for per-task details.')
)

$s9bNoteType = if ($s9b -eq 'err') {'err'} elseif ($s9b -eq 'done') {'info'} else {$null}
$s9bNoteText = if ($s9b -eq 'err') {'No tests/functional/ directory. Journey, contract and smoke tests required for TCPA audit evidence.'} elseif ($s9b -eq 'done') {'Functional and E2E tests generated.'} else {''}
$s9cNoteType = if ($s9c -eq 'err') {'err'} elseif ($s9c -eq 'done') {'info'} else {$null}
$s9cNoteText = if ($s9c -eq 'err') {'Test plan CSV and Excel not produced. Traceability matrix missing.'} elseif ($s9c -eq 'done') {'Test plan generated.'} else {''}

$testAgents = @(
    (New-Agent '09' 'Unit & integration tests'  'agents/09-test-generator.md'         $s09 'outputs/task-log.md' @('outputs/tasks.md','outputs/specs.md','src/')                                                                  @('tests/TCPA.Api.Tests/','outputs/task-log.md') $null           $null)
    (New-Agent '9b' 'Functional & E2E tests'    'agents/09b-functional-test-agent.md' $s9b 'outputs/task-log.md' @('outputs/stories.md','outputs/specs.md','outputs/architecture.md','outputs/risks.md','tests/')                 @('tests/functional/')                          $s9bNoteType    $s9bNoteText)
    (New-Agent '9c' 'Test plan generator'       'agents/09c-test-plan-agent.md'       $s9c 'outputs/task-log.md' @('outputs/requirements.md','outputs/stories.md','outputs/risks.md','tests/')                                    @('tests/TCPA-Test-Cases.csv','tests/TCPA-Test-Plan.xlsx') $s9cNoteType   $s9cNoteText)
)

$s10NoteType = if ($s10 -eq 'warn') {'warn'} elseif ($s10 -eq 'done') {'info'} else {$null}
$s10NoteText = if ($s10 -eq 'warn') {'APPROVED WITH CONDITIONS - open findings require owner acknowledgement before merge.'} elseif ($s10 -eq 'done') {'APPROVED - no blocking findings.'} else {''}
$s11NoteType = if ($s11 -eq 'warn') {'warn'} elseif ($s11 -eq 'done') {'info'} else {$null}
$s11NoteText = if ($s11 -eq 'warn') {'PASS WITH CONDITIONS - security conditions must be acknowledged before merge.'} elseif ($s11 -eq 'done') {'PASS - no security-blocking findings.'} else {''}

$qualAgents = @(
    (New-Agent '10' 'Code reviewer'   'agents/10-code-reviewer.md'    $s10 'outputs/review-findings.md'   @('src/','tests/','outputs/architecture.md','outputs/specs.md')                                                                                    @('outputs/review-findings.md')   $s10NoteType $(if($s10NoteText){$s10NoteText}else{$null}))
    (New-Agent '11' 'Security agent'  'agents/11-security-agent.md'   $s11 'outputs/security-findings.md' @('src/','outputs/specs.md','outputs/risks.md','outputs/architecture.md')                                                                          @('outputs/security-findings.md') $s11NoteType $(if($s11NoteText){$s11NoteText}else{$null}))
    (New-Checkpoint 'Checkpoint 4 - review & security sign-off' $cp4 'outputs/security-findings.md')
    (New-Agent '12' 'Documentation'   'agents/12-documentation-agent.md' $s12 'outputs/task-log.md'       @('src/','tests/','outputs/architecture.md','outputs/specs.md')                                                                                    @('outputs/docs/README.md','outputs/docs/api.md','outputs/docs/operations.md','outputs/docs/CHANGELOG.md') $null $null)
    (New-Agent '13' 'PR assembler'    'agents/13-pr-assembler.md'     $s13 'outputs/pr-description.md'    @('outputs/requirements.md','outputs/stories.md','outputs/task-log.md','outputs/review-findings.md','outputs/security-findings.md')                @('outputs/pr-description.md')    $(if($s13 -eq 'done'){'warn'}else{$null}) $(if($s13 -eq 'done'){'PR assembled. Human approval required before merge.'}else{$null}))
)

# ── Build artifacts map ───────────────────────────────────────────────────────

$artifactsMap = [ordered]@{}
$allAgentArrays = @($reqAgents) + @($storAgents) + @($devAgents) + @($testAgents) + @($qualAgents)
foreach ($a in $allAgentArrays) {
    if ($a.Contains('checkpoint')) { continue }
    foreach ($out in $a.outputs) {
        if (-not $artifactsMap.Contains($out)) {
            $artifactsMap[$out] = Get-ArtifactMeta $out
        }
    }
}

# ── Assemble state ────────────────────────────────────────────────────────────

$testDone = @($s09,$s9b,$s9c) | Where-Object { $_ -eq 'done' } | Measure-Object | Select-Object -ExpandProperty Count

$cfReq  = Get-PhaseConfidence $reqAgents
$cfStor = Get-PhaseConfidence $storAgents
$cfDev  = Get-PhaseConfidence $devAgents
$cfTest = Get-PhaseConfidence $testAgents
$cfQual = Get-PhaseConfidence $qualAgents

$state = [ordered]@{
    project = [ordered]@{ name = 'TCPA Regulatory Compliance API'; client = 'Southern Company Gas'; phase = 'Phase 1'; prd = 'inputs/prd.md' }
    stats   = [ordered]@{ totalTasks = $totalTasks; estimatedHours = $totalHours; agentsRun = $agentsRun; agentsTotal = $allStatuses.Count; phasesComplete = $phasesComplete; phasesTotal = 5 }
    inputs    = [ordered]@{ brd = (Test-Path (Join-Path $Root 'inputs/brd.md')); prd = (Test-Path (Join-Path $Root 'inputs/prd.md')) }
    artifacts = $artifactsMap
    phases  = @(
        [ordered]@{ id='requirements';   label='Requirements';        icon='ti-clipboard-list'; status=$phReq;  confidence=$cfReq;  durationSeconds=(Get-PhaseDuration $reqAgents);  summary="7 agents - 6 output artifacts - $(@($reqAgents|Where-Object{$_.Contains('checkpoint') -and $_.passed}).Count) checkpoints passed"; agents=$reqAgents }
        [ordered]@{ id='stories';        label='Epics & user stories'; icon='ti-list-check';    status=$phStor; confidence=$cfStor; durationSeconds=(Get-PhaseDuration $storAgents); summary="2 agents - 2 output artifacts - $(@($storAgents|Where-Object{$_.Contains('checkpoint') -and $_.passed}).Count) checkpoint$(if(@($storAgents|Where-Object{$_.Contains('checkpoint') -and $_.passed}).Count -ne 1){'s'}) passed"; agents=$storAgents }
        [ordered]@{ id='development';    label='Development';          icon='ti-code';           status=$phDev;  confidence=$cfDev;  durationSeconds=(Get-PhaseDuration $devAgents);  summary='1 agent - implementation files generated'; agents=$devAgents }
        [ordered]@{ id='testing';        label='Testing';              icon='ti-test-pipe';      status=$phTest; confidence=$cfTest; durationSeconds=(Get-PhaseDuration $testAgents); summary="3 agents - $testDone of 3 complete"; agents=$testAgents }
        [ordered]@{ id='quality';        label='Security & quality';   icon='ti-shield-check';   status=$phQual; confidence=$cfQual; durationSeconds=(Get-PhaseDuration $qualAgents); summary="4 agents - $(@($qualAgents|Where-Object{$_.Contains('checkpoint') -and $_.passed}).Count) checkpoint$(if(@($qualAgents|Where-Object{$_.Contains('checkpoint') -and $_.passed}).Count -ne 1){'s'}) passed"; agents=$qualAgents }
    )
}

# ── Write pipeline-state.json ─────────────────────────────────────────────────

$jsonPath = Join-Path $Root 'pipeline-state.json'
$state | ConvertTo-Json -Depth 20 | Set-Content $jsonPath -Encoding utf8
Write-Host "  [sync] pipeline-state.json updated"

# ── Patch PIPELINE_STATE in pipeline-dashboard.html ──────────────────────────

$htmlPath = Join-Path $Root 'pipeline-dashboard.html'
if (-not (Test-Path $htmlPath)) {
    Write-Warning "pipeline-dashboard.html not found at $htmlPath"
    exit 0
}

$html        = Get-Content $htmlPath -Raw -Encoding utf8
$compactJson = $state | ConvertTo-Json -Depth 20 -Compress

# Replace everything between the ASCII sentinel comments
$pattern  = '(?s)(// <<PIPELINE_STATE_BEGIN>>.*?const PIPELINE_STATE = ).*?(;\s*// <<PIPELINE_STATE_END>>)'
$replacement = "// <<PIPELINE_STATE_BEGIN>>`r`n// Edit this object to update the dashboard. Mirrors pipeline-state.json.`r`nconst PIPELINE_STATE = $compactJson;`r`n// <<PIPELINE_STATE_END>>"

if ($html -match $pattern) {
    $html = [regex]::Replace($html, $pattern, $replacement)
    [System.IO.File]::WriteAllText($htmlPath, $html, [System.Text.Encoding]::UTF8)
    Write-Host "  [sync] pipeline-dashboard.html patched"
} else {
    Write-Warning "Sentinel comments not found in pipeline-dashboard.html - HTML not patched"
}

# ── Patch PIPELINE_STATE in pipeline-dashboard-v2.html ───────────────────────

$htmlPathV2 = Join-Path $Root 'pipeline-dashboard-v2.html'
if (Test-Path $htmlPathV2) {
    $htmlV2 = Get-Content $htmlPathV2 -Raw -Encoding utf8
    if ($htmlV2 -match $pattern) {
        $htmlV2 = [regex]::Replace($htmlV2, $pattern, $replacement)
        [System.IO.File]::WriteAllText($htmlPathV2, $htmlV2, [System.Text.Encoding]::UTF8)
        Write-Host "  [sync] pipeline-dashboard-v2.html patched"
    } else {
        Write-Warning "Sentinel comments not found in pipeline-dashboard-v2.html - HTML not patched"
    }
}

Write-Host "  [sync] done - $agentsRun/$($allStatuses.Count) agents complete, $phasesComplete/5 phases done"
