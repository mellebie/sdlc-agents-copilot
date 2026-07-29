#Requires -Version 5.1
<#
.SYNOPSIS
    Writes outputs/pipeline-manifest.json from the staged manifest template.
.DESCRIPTION
    Populates the run metadata, normalizes artifact names to the repository's
    current outputs, and records checkpoint and eval status based on existing files.
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$TemplateFile = 'outputs/pipeline-manifest-template.json',

    [Parameter()]
    [string]$OutputFile = 'outputs/pipeline-manifest.json',

    [Parameter()]
    [string]$ModelId = '',

    [Parameter()]
    [string]$ModelVendor = '',

    [Parameter()]
    [string]$ModelDisplayName = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-FilePresent {
    param([string]$Path)
    return [bool](Test-Path (Join-Path (Split-Path $PSScriptRoot -Parent) $Path))
}

function Get-GitValue {
    param([string[]]$Arguments)
    try {
        $result = & git @Arguments 2>$null
        if ($LASTEXITCODE -eq 0) { return ($result | Out-String).Trim() }
    }
    catch { }
    return ''
}

function Resolve-ModelAttestation {
    param(
        [string]$Id,
        [string]$Vendor,
        [string]$Name
    )

    $resolvedId = if ($Id) { $Id } elseif ($env:COPILOT_MODEL_ID) { $env:COPILOT_MODEL_ID } else { 'unknown' }
    $resolvedVendor = if ($Vendor) { $Vendor } elseif ($env:COPILOT_MODEL_VENDOR) { $env:COPILOT_MODEL_VENDOR } else { 'unknown' }
    $resolvedName = if ($Name) { $Name } elseif ($env:COPILOT_MODEL_NAME) { $env:COPILOT_MODEL_NAME } else { $resolvedId }

    return [PSCustomObject]@{
        id = $resolvedId
        vendor = $resolvedVendor
        display_name = $resolvedName
    }
}

function Set-ArtifactModelAttestation {
    param(
        [string]$Root,
        [string]$RelativePath,
        [string]$StepId,
        [string]$PromptFile,
        [object]$Model
    )

    if (-not $RelativePath -or $RelativePath.EndsWith('/')) {
        return
    }

    $targetPath = Join-Path $Root $RelativePath
    if (-not (Test-Path $targetPath)) {
        return
    }

    $item = Get-Item $targetPath
    if (-not $item.PSIsContainer -and $item.Extension -ieq '.md') {
        $content = Get-Content $targetPath -Raw

        $startMarker = '<!-- MODEL ATTESTATION START -->'
        $endMarker = '<!-- MODEL ATTESTATION END -->'

        $attestationBlock = @(
            $startMarker
            '> **Model Attestation**'
            "> **Step:** $StepId"
            "> **Prompt:** $PromptFile"
            "> **Model ID:** $($Model.id)"
            "> **Model Vendor:** $($Model.vendor)"
            "> **Model Name:** $($Model.display_name)"
            "> **Captured:** $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
            $endMarker
        ) -join [Environment]::NewLine

        $markerPattern = '(?s)<!-- MODEL ATTESTATION START -->.*?<!-- MODEL ATTESTATION END -->\r?\n?'
        if ($content -match $markerPattern) {
            $updated = [regex]::Replace($content, $markerPattern, $attestationBlock + [Environment]::NewLine + [Environment]::NewLine)
            Set-Content -Path $targetPath -Value $updated -Encoding utf8
            return
        }

        $newContent = $attestationBlock + [Environment]::NewLine + [Environment]::NewLine + $content
        Set-Content -Path $targetPath -Value $newContent -Encoding utf8
    }
}

$root = Split-Path $PSScriptRoot -Parent
$templatePath = Join-Path $root $TemplateFile
$outputPath = Join-Path $root $OutputFile

if (-not (Test-Path $templatePath)) {
    throw "Template file '$TemplateFile' not found."
}

$template = Get-Content $templatePath -Raw | ConvertFrom-Json
$modelAttestation = Resolve-ModelAttestation -Id $ModelId -Vendor $ModelVendor -Name $ModelDisplayName

$template.run.id = Get-Date -Format 'yyyyMMdd-HHmmss'
$template.run.date = Get-Date -Format 'yyyy-MM-dd'
$template.run.initiated_by = $env:USERNAME
$remoteUrl = Get-GitValue @('remote', 'get-url', 'origin')
$template.run.project = if ($remoteUrl -match '([^/\\]+?)(?:\.git)?$') { $Matches[1] } else { Split-Path $root -Leaf }
$template.run.branch = (Get-GitValue @('branch', '--show-current'))
$template.run.pr_number = $null
$template.run.model = [ordered]@{
    id = $modelAttestation.id
    vendor = $modelAttestation.vendor
    display_name = $modelAttestation.display_name
    captured_at = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
}

$stepFiles = [ordered]@{
    '00' = 'inputs/prd.md'
    '01' = 'outputs/requirements.md'
    '02' = 'outputs/clarifications.md'
    '03' = 'outputs/specs.md'
    '04' = 'outputs/architecture.md'
    '05' = 'outputs/risks.md'
    '06' = 'outputs/stories.md'
    '07' = 'outputs/tasks.md'
    '08' = 'src/'
    '09' = 'tests/'
    '10' = 'outputs/review-findings.md'
    '11' = 'outputs/security-findings.md'
    '12' = 'outputs/docs/'
    '13' = 'outputs/pr-description.md'
}

foreach ($step in $template.steps) {
    $key = [string]$step.step
    if ($stepFiles.Contains($key)) {
        $step.output_file = $stepFiles[$key]
    }
    $step.status = if (Test-FilePresent $step.output_file) { 'done' } else { 'not_run' }
    $step.model_version = if ($step.status -eq 'done') { $modelAttestation.id } else { '' }

    if ($step.status -eq 'done') {
        Set-ArtifactModelAttestation -Root $root -RelativePath $step.output_file -StepId $step.step -PromptFile $step.prompt_file -Model $modelAttestation
    }

    if ($step.step -in @('02', '04', '06', '11', '13')) {
        $step.checkpoint_required = $true
    }
}

$template.eval.run = (Test-FilePresent 'outputs/eval-summary.md')
$template.eval.eval_agent_version = '1.0'
$template.eval.scores = @{}

$template.summary.steps_completed = @($template.steps | Where-Object { $_.status -eq 'done' }).Count
$template.summary.steps_total = @($template.steps).Count
$template.summary.checkpoints_required = 5
$template.summary.checkpoints_approved = @($template.steps | Where-Object { $_.checkpoint.approved_by }).Count
$template.summary.overall_status = if ($template.summary.steps_completed -eq $template.summary.steps_total) { 'complete' } else { 'in_progress' }

$template | ConvertTo-Json -Depth 20 | Out-File -FilePath $outputPath -Encoding utf8

Write-Host "Pipeline manifest written to $outputPath"
