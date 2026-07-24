# Push-ToADO.ps1
# Parses outputs/stories.md and creates Epics + User Stories in Azure DevOps (Agile template).
#
# Usage:
#   .\scripts\Push-ToADO.ps1 -OrgUrl "https://dev.azure.com/myorg" -Project "MyProject"
#
# Auth:
#   Set $env:ADO_PAT before running, or pass -Pat directly.
#   Generate a PAT at: https://dev.azure.com/{org}/_usersSettings/tokens
#   Required scopes: Work Items (Read & Write)

param(
    [Parameter(Mandatory)]
    [string]$OrgUrl,

    [Parameter(Mandatory)]
    [string]$Project,

    [string]$Pat = $env:ADO_PAT,

    [string]$StoriesFile = "outputs\stories.md",

    [string]$AreaPath = "",        # Optional: "MyProject\Team A" — defaults to project root
    [string]$IterationPath = "",   # Optional: "MyProject\Sprint 1" — defaults to project root

    [switch]$DryRun                # Print what would be created without calling ADO
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# --- Validate prerequisites ---
if (-not $Pat) {
    Write-Error "No PAT provided. Set `$env:ADO_PAT or pass -Pat."
    exit 1
}

$root = Split-Path $PSScriptRoot -Parent
$storiesPath = Join-Path $root $StoriesFile
if (-not (Test-Path $storiesPath)) {
    Write-Error "Stories file not found: $storiesPath. Run Agent 06 first."
    exit 1
}

# --- Auth header ---
$token  = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(":$Pat"))
$header = @{
    Authorization = "Basic $token"
}

$apiBase = "$($OrgUrl.TrimEnd('/'))/$([Uri]::EscapeDataString($Project))/_apis/wit/workitems"

# --- Helper: MoSCoW → ADO priority (1=Critical, 2=High, 3=Medium, 4=Low) ---
function ConvertTo-AdoPriority([string]$moscow) {
    switch -Wildcard ($moscow.Trim()) {
        "Must*"   { return 2 }
        "Should*" { return 3 }
        "Could*"  { return 4 }
        default   { return 3 }
    }
}

# --- Helper: Create a work item via REST ---
function New-WorkItem([string]$type, [array]$ops, [int]$parentId = 0) {
    if ($parentId -gt 0) {
        $parentUrl = "$($OrgUrl.TrimEnd('/'))/$([Uri]::EscapeDataString($Project))/_apis/wit/workitems/$parentId"
        $ops += @{
            op    = "add"
            path  = "/relations/-"
            value = @{
                rel        = "System.LinkTypes.Hierarchy-Reverse"
                url        = $parentUrl
                attributes = @{ comment = "Created by SDLC pipeline" }
            }
        }
    }

    $encodedType = [Uri]::EscapeDataString('$' + $type)
    $uri  = "$apiBase/$encodedType`?api-version=7.1"
    $body = ConvertTo-Json -InputObject @($ops) -Depth 10 -Compress

    if ($env:ADO_DEBUG) { Write-Host "DEBUG BODY: $body" }

    $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($body)
    $response = Invoke-RestMethod -Uri $uri -Method Post -Headers $header -Body $bodyBytes -ContentType "application/json-patch+json"
    return $response.id
}

# --- Helper: Build a patch op ---
function op([string]$path, $value) {
    return @{ op = "add"; path = $path; value = $value }
}

# --- Parse stories.md ---
$lines   = Get-Content $storiesPath
$epics   = [System.Collections.Generic.List[hashtable]]::new()
$current = $null
$story   = $null
$section = $null

foreach ($line in $lines) {

    # Epic header: ## EPIC-001: Title
    if ($line -match '^## (EPIC-\d+): (.+)$') {
        if ($story -and $current) { $current.Stories.Add($story) }
        $story = $null
        $current = @{
            Id          = $Matches[1]
            Title       = $Matches[2].Trim()
            Description = ""
            Priority    = "Should Have"
            Stories     = [System.Collections.Generic.List[hashtable]]::new()
        }
        $epics.Add($current)
        $section = "epic"
        continue
    }

    # Epic fields
    if ($current -and $section -eq "epic") {
        if ($line -match '^\s*-\s*\*\*Description:\*\*\s*(.+)$') { $current.Description = $Matches[1].Trim() }
        if ($line -match '^\s*-\s*\*\*Priority:\*\*\s*(.+)$')    { $current.Priority    = $Matches[1].Trim() }
    }

    # Story header: ### STORY-001: Title
    if ($line -match '^### (STORY-\d+): (.+)$') {
        if ($story -and $current) { $current.Stories.Add($story) }
        $story = @{
            Id          = $Matches[1]
            Title       = $Matches[2].Trim()
            UserStory   = ""
            AC          = [System.Collections.Generic.List[string]]::new()
            Points      = 0
            Priority    = "Should Have"
            Flags       = ""
        }
        $section = "story"
        continue
    }

    if ($story -and $section -eq "story") {

        # Story Points
        if ($line -match '^\*\*Story Points:\*\*\s*(\d+)') {
            $story.Points = [int]$Matches[1]
        }

        # Priority
        if ($line -match '^\*\*Priority:\*\*\s*(.+)$') {
            $story.Priority = $Matches[1].Trim()
        }

        # Flags line
        if ($line -match '^\*\*Flags:\*\*\s*(.+)$') {
            $story.Flags = $Matches[1].Trim()
        }

        # User story text (lines after "**User Story:**")
        if ($line -match '^\*\*User Story:\*\*') {
            $section = "userstory"
            continue
        }

        # AC section
        if ($line -match '^\*\*Acceptance Criteria:\*\*') {
            $section = "ac"
            continue
        }
    }

    # Capture user story prose (As a / I want / So that)
    if ($section -eq "userstory" -and $story) {
        if ($line -match '^\*\*') { $section = "story" }  # next bold heading ends it
        elseif ($line.Trim() -ne "") {
            $story.UserStory += $line.Trim() + " "
        }
    }

    # Capture AC lines
    if ($section -eq "ac" -and $story) {
        if ($line -match '^\*\*' -and $line -notmatch '_AC-') { $section = "story" }
        elseif ($line.Trim() -ne "") {
            $story.AC.Add($line.Trim())
        }
    }
}

# Flush last story and epic
if ($story -and $current) { $current.Stories.Add($story) }

# --- Summary before creating ---
$totalStories = ($epics | ForEach-Object { $_.Stories.Count } | Measure-Object -Sum).Sum
Write-Host ""
Write-Host "Parsed from ${StoriesFile}:"
Write-Host "  Epics  : $($epics.Count)"
Write-Host "  Stories: $totalStories"
Write-Host ""

if ($DryRun) {
    Write-Host "--- DRY RUN (no items will be created) ---"
    foreach ($epic in $epics) {
        Write-Host "EPIC: $($epic.Id) - $($epic.Title) [$($epic.Priority)]"
        foreach ($s in $epic.Stories) {
            Write-Host "  STORY: $($s.Id) - $($s.Title) [$($s.Priority)] ($($s.Points) pts)"
        }
    }
    exit 0
}

# --- Create work items ---
Write-Host "Creating work items in: $OrgUrl / $Project"
Write-Host ""

# Map to collect STORY-XXX → ADO work item ID
$storyIdMap = [System.Collections.Generic.List[string]]::new()

foreach ($epic in $epics) {
    $epicOps = @(
        op "/fields/System.Title"                $epic.Title
        op "/fields/System.Description"          $epic.Description
        op "/fields/Microsoft.VSTS.Common.Priority" (ConvertTo-AdoPriority $epic.Priority)
        op "/fields/System.Tags"                 "sdlc-pipeline; $($epic.Id)"
    )
    if ($AreaPath)      { $epicOps += op "/fields/System.AreaPath"      $AreaPath }
    if ($IterationPath) { $epicOps += op "/fields/System.IterationPath" $IterationPath }

    Write-Host "Creating Epic: $($epic.Id) - $($epic.Title)..."
    $epicId = New-WorkItem -type "Epic" -ops $epicOps
    Write-Host "  Created Epic #$epicId"

    foreach ($s in $epic.Stories) {
        $acHtml = "<ul>" + (($s.AC | ForEach-Object { "<li>$_</li>" }) -join "") + "</ul>"

        $storyOps = @(
            op "/fields/System.Title"                                  $s.Title
            op "/fields/System.Description"                            $s.UserStory.Trim()
            op "/fields/Microsoft.VSTS.Common.AcceptanceCriteria"      $acHtml
            op "/fields/Microsoft.VSTS.Scheduling.StoryPoints"         $s.Points
            op "/fields/Microsoft.VSTS.Common.Priority"                (ConvertTo-AdoPriority $s.Priority)
            op "/fields/System.Tags"                                   "sdlc-pipeline; $($s.Id); $($epic.Id)$(if ($s.Flags) { '; ' + $s.Flags })"
        )
        if ($AreaPath)      { $storyOps += op "/fields/System.AreaPath"      $AreaPath }
        if ($IterationPath) { $storyOps += op "/fields/System.IterationPath" $IterationPath }

        Write-Host "  Creating Story: $($s.Id) - $($s.Title)..."
        $storyId = New-WorkItem -type "User Story" -ops $storyOps -parentId $epicId
        Write-Host "    Created User Story #$storyId (parent: Epic #$epicId)"
        $storyIdMap.Add("$($s.Id)=AB#$storyId")
    }

    Write-Host ""
}

# --- Write STORY-XXX → AB# mapping file ---
$mapPath = Join-Path $root "outputs\ado-story-ids.md"
$mapLines = @(
    "<!-- SDLC Pipeline Artifact"
    "     Stage: 06-story-writer (Push-ToADO)"
    "     Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
    "     Purpose: Maps STORY-XXX identifiers to Azure DevOps AB# work item IDs"
    "              Include AB# tags in git commit messages to link commits to ADO."
    "-->"
    ""
    "# ADO Story ID Map"
    ""
    "| Story ID  | ADO Tag | ADO URL |"
    "|-----------|---------|---------|"
)
foreach ($entry in $storyIdMap) {
    $parts  = $entry -split '='
    $storyKey = $parts[0]
    $abTag    = $parts[1]
    $adoId    = $abTag -replace 'AB#', ''
    $mapLines += "| $storyKey | $abTag | $($OrgUrl.TrimEnd('/'))/$([Uri]::EscapeDataString($Project))/_workitems/edit/$adoId |"
}
$mapLines | Set-Content -Path $mapPath -Encoding UTF8
Write-Host "Story ID map written to outputs\ado-story-ids.md"

Write-Host ""
Write-Host "Done. $($epics.Count) epics and $totalStories user stories created in $Project."
Write-Host "View board: $OrgUrl/$([Uri]::EscapeDataString($Project))/_boards"
