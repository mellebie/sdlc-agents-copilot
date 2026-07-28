#Requires -Version 5.1
<#
.SYNOPSIS
    Runs deterministic quality checks across pipeline output files.
.DESCRIPTION
    Scans the actual repository artifacts under outputs/ and writes a stable
    outputs/eval-summary.md plus a timestamped eval report for traceability.
#>

[CmdletBinding()]
param (
    [Parameter()]
    [string]$OutputDir = "outputs",

    [Parameter()]
    [string]$ReportFile = "",

    [Parameter()]
    [string]$Step = "",

    [Parameter()]
    [switch]$EnforcePostCheckpoint3,

    [Parameter()]
    [string]$RubricDir = ".github/eval-rubrics",

    [Parameter()]
    [switch]$RunRubricEval = $true,

    [Parameter()]
    [switch]$EnforceRubricGate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$artifactPlan = @(
    [ordered]@{ Step = '01'; File = 'requirements.md'; Sections = @('Functional Requirements', 'Non-Functional Requirements', 'Constraints', 'Out of Scope') }
    [ordered]@{ Step = '02'; File = 'clarifications.md'; Sections = @('Clarifications', 'Open Questions', 'Conflicts') }
    [ordered]@{ Step = '03'; File = 'specs.md'; Sections = @('Specifications', 'Non-Functional Specifications', 'Dependencies') }
    [ordered]@{ Step = '04'; File = 'architecture.md'; Sections = @('Components', 'Data Flow', 'Architectural Risks', 'Security Considerations', 'Technology Stack') }
    [ordered]@{ Step = '05'; File = 'risks.md'; Sections = @('Risk Register', 'GO / NO-GO Recommendation') }
    [ordered]@{ Step = '06'; File = 'stories.md'; Sections = @('Epics') }
    [ordered]@{ Step = '07'; File = 'tasks.md'; Sections = @('Tasks', 'Task Dependency Map', 'Effort Summary') }
    [ordered]@{ Step = '08'; File = 'task-log.md'; Sections = @('Context Standards Applied', 'Context Divergences') }
    [ordered]@{ Step = '10'; File = 'review-findings.md'; Sections = @('Review Summary', 'Context Standards Applied', 'Context Divergences') }
    [ordered]@{ Step = '11'; File = 'security-findings.md'; Sections = @('Summary', 'Context Standards Applied', 'Context Divergences') }
    [ordered]@{ Step = '12'; File = 'docs/README.md'; Sections = @('Context Standards Applied', 'Context Divergences', 'AI Pipeline Disclosure') }
    [ordered]@{ Step = '13'; File = 'pr-description.md'; Sections = @('Human Approval Required', 'Context Standards Applied', 'Context Divergences', 'AI Pipeline Disclosure') }
)

$flagPatterns = [ordered]@{
    AMBIGUOUS      = '\[AMBIGUOUS'
    GAP            = '\[GAP:'
    ASSUMPTION     = '\[ASSUMPTION:'
    BLOCKER        = 'BLOCKER:'
    TCPA_RISK      = '\[TCPA-RISK'
    NEW_DEPENDENCY = '\[NEW-DEPENDENCY:'
    CRITICAL       = '🚨 CRITICAL FINDING'
    SECURITY_BLOCK = 'SECURITY-BLOCKING'
    BLOCKING       = '\bBLOCKING\b'
}

function Get-FlagCounts {
    param([string]$Content)

    $counts = [ordered]@{}
    foreach ($key in $flagPatterns.Keys) {
        $counts[$key] = ([regex]::Matches($Content, $flagPatterns[$key])).Count
    }
    return $counts
}

function Test-RequiredSections {
    param(
        [string]$Content,
        [string[]]$Sections
    )

    $results = @()
    foreach ($section in $Sections) {
        $present = $false
        if ($Content -match "(?m)^#+\s+$([regex]::Escape($section))\s*$") { $present = $true }
        if (-not $present -and $Content -match [regex]::Escape($section)) { $present = $true }
        $results += [PSCustomObject]@{ Section = $section; Present = $present }
    }
    return $results
}

function Get-QualityGate {
    param(
        [int]$PassedSections,
        [int]$TotalSections,
        [int]$BlockerCount
    )

    if ($BlockerCount -gt 0) { return 'FAIL' }
    if ($TotalSections -eq 0) { return 'SKIP' }
    $pct = [math]::Round(($PassedSections / $TotalSections) * 100)
    if ($pct -ge 100) { return 'PASS' }
    if ($pct -ge 75) { return 'CONDITIONAL' }
    return 'FAIL'
}

function Get-RubricFileForStep {
    param(
        [string]$StepId,
        [string]$RubricDirectory
    )

    if (-not (Test-Path $RubricDirectory)) {
        return $null
    }

    $pattern = "step-{0}-*.md" -f $StepId.ToLower()
    $matches = @(Get-ChildItem -Path $RubricDirectory -Filter $pattern -File -ErrorAction SilentlyContinue)
    if ($matches.Count -eq 0) {
        return $null
    }

    $selected = $matches | Sort-Object Name | Select-Object -First 1
    return $selected.FullName
}

function Get-RubricThresholds {
    param([string]$RubricContent)

    $threshold = [ordered]@{
        PassMin = 80
        ConditionalMin = 60
    }

    if ($RubricContent -match '(?i)(\d+)%\s*for\s*PASS;\s*(\d+)\s*[–-]\s*(\d+)%\s*for\s*CONDITIONAL;\s*<\s*(\d+)%\s*for\s*FAIL') {
        $threshold.PassMin = [int]$matches[1]
        $threshold.ConditionalMin = [int]$matches[2]
    }

    return $threshold
}

function Get-RubricCriteria {
    param([string]$RubricPath)

    $criteria = [System.Collections.Generic.List[object]]::new()
    $lines = Get-Content $RubricPath

    foreach ($line in $lines) {
        if ($line -match '^\|\s*(\d+)\s*\|\s*(.*?)\s*\|\s*(\d+)%\s*\|\s*(.*?)\s*\|\s*$') {
            $criteria.Add([PSCustomObject]@{
                Index = [int]$matches[1]
                Criterion = $matches[2].Trim()
                Weight = [int]$matches[3]
                PassCondition = $matches[4].Trim()
            })
        }
    }

    return @($criteria)
}

function Get-PassConditionKeywordCoverage {
    param(
        [string]$PassCondition,
        [string]$ArtifactContent
    )

    $stopWords = @(
        'with','from','that','this','have','has','must','into','each','least','more','less',
        'when','then','than','only','over','under','such','their','there','where','which',
        'explicit','present','contains','listed','against','across','every','while','without',
        'within','using','mapped','traceable','output','section','sections','criteria','condition',
        'pass','fail','partial','applicable','condition','criterion'
    )

    $contentLower = $ArtifactContent.ToLower()
    $tokens = [regex]::Matches($PassCondition.ToLower(), '[a-z0-9]{4,}') |
        ForEach-Object { $_.Value } |
        Where-Object { $_ -notin $stopWords } |
        Select-Object -Unique

    if (@($tokens).Count -eq 0) {
        return [PSCustomObject]@{
            Coverage = 0.0
            Matched = 0
            Total = 0
        }
    }

    $matched = 0
    foreach ($token in $tokens) {
        if ($contentLower -match "\\b$([regex]::Escape($token))\\b") {
            $matched++
        }
    }

    $coverage = [double]$matched / [double]@($tokens).Count
    return [PSCustomObject]@{
        Coverage = $coverage
        Matched = $matched
        Total = @($tokens).Count
    }
}

function Invoke-RubricAutoEvaluation {
    param(
        [string]$RubricPath,
        [string]$ArtifactContent,
        [double]$StructuralSignal = 0.0
    )

    $rubricContent = Get-Content $RubricPath -Raw
    $threshold = Get-RubricThresholds -RubricContent $rubricContent
    $criteria = Get-RubricCriteria -RubricPath $RubricPath

    if (@($criteria).Count -eq 0) {
        return [PSCustomObject]@{
            Status = 'SKIP'
            Reason = 'No parsable rubric criteria table found.'
            Confidence = 0
            Verdict = 'SKIP'
            CriteriaResults = @()
        }
    }

    $criteriaResults = [System.Collections.Generic.List[object]]::new()
    $weightTotal = 0
    $weightedScore = 0.0

    foreach ($criterion in $criteria) {
        $signal = Get-PassConditionKeywordCoverage -PassCondition ("{0} {1}" -f $criterion.Criterion, $criterion.PassCondition) -ArtifactContent $ArtifactContent
        $effectiveCoverage = [math]::Max($signal.Coverage, ($StructuralSignal * 0.60))
        $score = 'FAIL'
        $scoreValue = 0.0

        if ($effectiveCoverage -ge 0.40) {
            $score = 'PASS'
            $scoreValue = 1.0
        } elseif ($effectiveCoverage -ge 0.20) {
            $score = 'PARTIAL'
            $scoreValue = 0.5
        }

        $weightTotal += $criterion.Weight
        $weightedScore += ($criterion.Weight * $scoreValue)

        $criteriaResults.Add([PSCustomObject]@{
            Index = $criterion.Index
            Criterion = $criterion.Criterion
            Weight = $criterion.Weight
            Score = $score
            Coverage = [math]::Round($effectiveCoverage * 100)
        })
    }

    $confidence = if ($weightTotal -gt 0) {
        [math]::Round(($weightedScore / $weightTotal) * 100)
    } else {
        0
    }

    $verdict = if ($confidence -ge $threshold.PassMin) {
        'PASS'
    } elseif ($confidence -ge $threshold.ConditionalMin) {
        'CONDITIONAL'
    } else {
        'FAIL'
    }

    return [PSCustomObject]@{
        Status = 'EXECUTED'
        Reason = ''
        Confidence = $confidence
        Verdict = $verdict
        CriteriaResults = @($criteriaResults)
    }
}

if (-not (Test-Path $OutputDir)) {
    throw "Output directory '$OutputDir' not found. Run the pipeline first."
}

$deliverySteps = @('08', '09', '09b', '09c', '10', '11', '12', '13')

$selectedArtifacts = @($artifactPlan)
if ($Step) {
    $selectedArtifacts = @($artifactPlan | Where-Object { $_.Step -eq $Step })
    if (@($selectedArtifacts).Count -eq 0) {
        throw "Unknown step '$Step'."
    }
}

$reportLines = [System.Collections.Generic.List[string]]::new()
$reportLines.Add('# Pipeline Eval Summary')
$reportLines.Add('')
$reportLines.Add("**Generated:** $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$reportLines.Add("**Output directory:** $OutputDir")
$reportLines.Add("**Evaluation scope:** $(if ($Step) { "Step $Step only" } else { 'Full pipeline' })")
$reportLines.Add("**Post-Checkpoint-3 strict mode:** $(if ($EnforcePostCheckpoint3) { 'ENABLED' } else { 'DISABLED' })")
$reportLines.Add("**Rubric auto-eval:** $(if ($RunRubricEval) { 'ENABLED' } else { 'DISABLED' })")
$reportLines.Add('')

$results = [System.Collections.Generic.List[object]]::new()

foreach ($artifact in $selectedArtifacts) {
    $filePath = Join-Path $OutputDir $artifact.File
    $content = if (Test-Path $filePath) { Get-Content $filePath -Raw } else { '' }
    $flags = Get-FlagCounts -Content $content
    $sections = Test-RequiredSections -Content $content -Sections $artifact.Sections

    $presentCount = @($sections | Where-Object { $_.Present }).Count
    $totalCount = @($sections).Count
    $blockerCount = $flags.BLOCKER + $flags.TCPA_RISK + $flags.SECURITY_BLOCK + $flags.CRITICAL
    $isDeliveryStep = $artifact.Step -in $deliverySteps
    $gate = if (-not $content) {
        if ($EnforcePostCheckpoint3 -and $isDeliveryStep) {
            'FAIL'
        } else {
            'MISSING'
        }
    } else { Get-QualityGate -PassedSections $presentCount -TotalSections $totalCount -BlockerCount $blockerCount }

    $rubricFile = $null
    $rubricEval = $null
    $rubricStatus = 'DISABLED'

    if ($RunRubricEval) {
        $rubricFile = Get-RubricFileForStep -StepId $artifact.Step -RubricDirectory $RubricDir
        if (-not $rubricFile) {
            $rubricStatus = 'NO_RUBRIC'
        } elseif (-not $content) {
            $rubricStatus = 'SKIP_MISSING_ARTIFACT'
        } else {
            $structuralSignal = if ($totalCount -gt 0) {
                [double]$presentCount / [double]$totalCount
            } else {
                0.0
            }
            $rubricEval = Invoke-RubricAutoEvaluation -RubricPath $rubricFile -ArtifactContent $content -StructuralSignal $structuralSignal
            $rubricStatus = $rubricEval.Status
        }
    }

    $results.Add([PSCustomObject]@{
        Step = $artifact.Step
        File = $artifact.File
        Gate = $gate
        Flags = $flags
        Sections = $sections
        Blockers = $blockerCount
        RubricFile = $rubricFile
        RubricStatus = $rubricStatus
        RubricEval = $rubricEval
    })

    $reportLines.Add('---')
    $reportLines.Add('')
    $reportLines.Add("## $($artifact.File)")
    $reportLines.Add('')
    $reportLines.Add("**Quality gate:** $gate")
    if (-not $content -and $EnforcePostCheckpoint3 -and $isDeliveryStep) {
        $reportLines.Add('')
        $reportLines.Add('> Strict mode applied: delivery-stage artifact missing after Checkpoint 3. This is treated as FAIL.')
    }
    $reportLines.Add('')

    if (@($sections).Count -gt 0) {
        $reportLines.Add('### Required Sections')
        $reportLines.Add('')
        $reportLines.Add('| Section | Present |')
        $reportLines.Add('| --- | --- |')
        foreach ($section in $sections) {
            $icon = if ($section.Present) { '✅' } else { '❌' }
            $reportLines.Add("| $($section.Section) | $icon |")
        }
        $reportLines.Add('')
    }

    $reportLines.Add('### Flag Counts')
    $reportLines.Add('')
    $reportLines.Add('| Flag | Count |')
    $reportLines.Add('| --- | --- |')
    foreach ($key in $flags.Keys) {
        $count = $flags[$key]
        $marker = if ($count -gt 0 -and $key -in @('BLOCKER','TCPA_RISK','SECURITY_BLOCK','CRITICAL','BLOCKING')) { ' ⚠️' } else { '' }
        $reportLines.Add("| $key$marker | $count |")
    }
    $reportLines.Add('')

    $reportLines.Add('### Rubric Evaluation')
    $reportLines.Add('')
    if (-not $RunRubricEval) {
        $reportLines.Add('Rubric auto-evaluation is disabled for this run.')
        $reportLines.Add('')
    } elseif (-not $rubricFile) {
        $reportLines.Add('No rubric file matched this step in `.github/eval-rubrics`.')
        $reportLines.Add('')
    } elseif (-not $content) {
        $reportLines.Add('Rubric evaluation skipped because the target artifact is missing.')
        $reportLines.Add('')
    } elseif ($rubricEval.Status -eq 'EXECUTED') {
        $reportLines.Add("**Rubric file:** $([System.IO.Path]::GetFileName($rubricFile))")
        $reportLines.Add("**Rubric confidence:** $($rubricEval.Confidence)%")
        $reportLines.Add("**Rubric verdict:** $($rubricEval.Verdict)")
        $reportLines.Add('')
        $reportLines.Add('| Criterion | Weight | Score | Signal Coverage |')
        $reportLines.Add('| --- | --- | --- | --- |')
        foreach ($criterionResult in $rubricEval.CriteriaResults) {
            $reportLines.Add("| $($criterionResult.Criterion) | $($criterionResult.Weight)% | $($criterionResult.Score) | $($criterionResult.Coverage)% |")
        }
        $reportLines.Add('')
    } else {
        $reportLines.Add("Rubric evaluation status: $($rubricEval.Status). $($rubricEval.Reason)")
        $reportLines.Add('')
    }
}

$passCount = @($results | Where-Object { $_.Gate -eq 'PASS' }).Count
$conditionalCount = @($results | Where-Object { $_.Gate -eq 'CONDITIONAL' }).Count
$failCount = @($results | Where-Object { $_.Gate -eq 'FAIL' }).Count
$missingCount = @($results | Where-Object { $_.Gate -eq 'MISSING' }).Count
$totalBlockers = ($results | Measure-Object -Property Blockers -Sum).Sum
if ($null -eq $totalBlockers) { $totalBlockers = 0 }

$rubricExecutedCount = @($results | Where-Object { $_.RubricStatus -eq 'EXECUTED' }).Count
$rubricPassCount = @($results | Where-Object { $_.RubricEval -and $_.RubricEval.Verdict -eq 'PASS' }).Count
$rubricConditionalCount = @($results | Where-Object { $_.RubricEval -and $_.RubricEval.Verdict -eq 'CONDITIONAL' }).Count
$rubricFailCount = @($results | Where-Object { $_.RubricEval -and $_.RubricEval.Verdict -eq 'FAIL' }).Count

$reportLines.Add('---')
$reportLines.Add('')
$reportLines.Add('## Summary')
$reportLines.Add('')
$reportLines.Add('| Result | Count |')
$reportLines.Add('| --- | --- |')
$reportLines.Add("| PASS | $passCount |")
$reportLines.Add("| CONDITIONAL | $conditionalCount |")
$reportLines.Add("| FAIL | $failCount |")
$reportLines.Add("| MISSING | $missingCount |")
$reportLines.Add("| Total blocker flags | $totalBlockers |")
$reportLines.Add('')

if ($RunRubricEval) {
    $reportLines.Add('| Rubric Result | Count |')
    $reportLines.Add('| --- | --- |')
    $reportLines.Add("| EXECUTED | $rubricExecutedCount |")
    $reportLines.Add("| PASS | $rubricPassCount |")
    $reportLines.Add("| CONDITIONAL | $rubricConditionalCount |")
    $reportLines.Add("| FAIL | $rubricFailCount |")
    $reportLines.Add('')
}

$overallGate = if ($failCount -gt 0 -or $totalBlockers -gt 0) { 'FAIL' } elseif ($conditionalCount -gt 0) { 'CONDITIONAL' } else { 'PASS' }

if ($RunRubricEval -and $EnforceRubricGate) {
    if ($rubricFailCount -gt 0) {
        $overallGate = 'FAIL'
    }
}

$reportLines.Add("**Overall pipeline gate: $overallGate**")
$reportLines.Add('')

$stableSummaryPath = Join-Path $OutputDir 'eval-summary.md'
$timestampedPath = if ($ReportFile) { $ReportFile } else { Join-Path $OutputDir ("eval-report-{0}.md" -f (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$reportLines | Out-File -FilePath $stableSummaryPath -Encoding utf8
$reportLines | Out-File -FilePath $timestampedPath -Encoding utf8

Write-Host ''
Write-Host 'Eval complete.' -ForegroundColor Cyan
Write-Host "  Files evaluated : $(@($selectedArtifacts).Count)"
Write-Host "  Overall gate    : $overallGate" -ForegroundColor $(if ($overallGate -eq 'PASS') { 'Green' } elseif ($overallGate -eq 'CONDITIONAL') { 'Yellow' } else { 'Red' })
Write-Host "  Summary file    : $stableSummaryPath"
Write-Host "  Report file     : $timestampedPath"
Write-Host ''

if ($overallGate -eq 'FAIL') {
    Write-Warning 'Pipeline gate is FAIL. Resolve blocker flags before proceeding.'
    exit 1
}
