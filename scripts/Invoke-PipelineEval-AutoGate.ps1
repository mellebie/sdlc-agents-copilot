#Requires -Version 5.1
<#!
.SYNOPSIS
    Runs pipeline eval with automatic strict-mode gating after Checkpoint 3 approval.
.DESCRIPTION
    Detects whether Checkpoint 3 is approved using outputs/pipeline-manifest.json
    (primary) and phrase fallback files (secondary). If approved, calls
    Invoke-PipelineEval.ps1 with -EnforcePostCheckpoint3.
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
    [string]$RubricDir = ".github/eval-rubrics",

    [Parameter()]
    [switch]$RunRubricEval = $true,

    [Parameter()]
    [switch]$EnforceRubricGate,

    [Parameter()]
    [string]$ManifestPath = "",

    [Parameter()]
    [string[]]$CheckpointSignalFiles = @("task-log.md", "clarifications.md", "stories.md", "tasks.md")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$evalScriptPath = Join-Path $scriptRoot "Invoke-PipelineEval.ps1"

if (-not (Test-Path $evalScriptPath)) {
    throw "Required script not found: $evalScriptPath"
}

if (-not (Test-Path $OutputDir)) {
    throw "Output directory '$OutputDir' not found."
}

if (-not $ManifestPath) {
    $ManifestPath = Join-Path $OutputDir "pipeline-manifest.json"
}

$strictMode = $false
$strictModeReason = "Checkpoint 3 not detected"

function Test-Checkpoint3FromManifest {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return $false
    }

    try {
        $manifest = Get-Content $Path -Raw | ConvertFrom-Json
    } catch {
        Write-Warning "Failed to parse manifest JSON at '$Path'. Fallback detection will be used."
        return $false
    }

    # Primary signal: Step 06 checkpoint contains approval metadata.
    $step06 = @($manifest.steps | Where-Object { $_.step -eq '06' }) | Select-Object -First 1
    if ($null -ne $step06 -and $null -ne $step06.checkpoint) {
        if ($step06.checkpoint.approved_at -or $step06.checkpoint.approved_by) {
            return $true
        }
    }

    # Secondary manifest signal: checkpoints approved count is at least three.
    if ($null -ne $manifest.summary -and $null -ne $manifest.summary.checkpoints_approved) {
        try {
            $approved = [int]$manifest.summary.checkpoints_approved
            if ($approved -ge 3) {
                return $true
            }
        } catch {
            return $false
        }
    }

    return $false
}

function Test-Checkpoint3FromPhraseFiles {
    param(
        [string]$BaseOutputDir,
        [string[]]$Files
    )

    foreach ($file in $Files) {
        $path = Join-Path $BaseOutputDir $file
        if (-not (Test-Path $path)) {
            continue
        }

        $content = Get-Content $path -Raw
        if ($content -match 'Checkpoint\s*3\s*approved') {
            return $true
        }
    }

    return $false
}

if (Test-Checkpoint3FromManifest -Path $ManifestPath) {
    $strictMode = $true
    $strictModeReason = "Detected Checkpoint 3 approval from pipeline manifest"
} elseif (Test-Checkpoint3FromPhraseFiles -BaseOutputDir $OutputDir -Files $CheckpointSignalFiles) {
    $strictMode = $true
    $strictModeReason = 'Detected "Checkpoint 3 approved" phrase in output artifacts'
}

Write-Host "AutoGate decision: $(if ($strictMode) { 'STRICT MODE ENABLED' } else { 'STRICT MODE DISABLED' })" -ForegroundColor Cyan
Write-Host "Reason: $strictModeReason"

$invokeParams = @{
    OutputDir = $OutputDir
    RubricDir = $RubricDir
    RunRubricEval = $RunRubricEval
}

if ($ReportFile) {
    $invokeParams.ReportFile = $ReportFile
}

if ($Step) {
    $invokeParams.Step = $Step
}

if ($strictMode) {
    $invokeParams.EnforcePostCheckpoint3 = $true
}

if ($EnforceRubricGate) {
    $invokeParams.EnforceRubricGate = $true
}

& $evalScriptPath @invokeParams
