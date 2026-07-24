<#
.SYNOPSIS
    Generates TCPA-Test-Plan.xlsx from TCPA-Test-Cases.csv.
    Each test case is expanded to one row per test step (reference Excel style).
    All data is sourced from pipeline artifacts via the CSV — not from memory.

.NOTES
    PowerShell 5.1 compatibility:
    - Numeric cell writes use $cell.Formula = "=" + [string]$n  (avoids Int32/String cast error)
    - No pipeline chain operators (&&, ||)
    - String comparison uses -eq not ==
#>

param(
    [string]$CsvPath    = "$PSScriptRoot\..\tests\TCPA-Test-Cases.csv",
    [string]$OutputPath = "$PSScriptRoot\..\tests\TCPA-Test-Plan.xlsx"
)

Add-Type -AssemblyName System.Drawing

$CsvPath    = [System.IO.Path]::GetFullPath($CsvPath)
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)

Write-Host "Reading CSV: $CsvPath"
if (-not (Test-Path $CsvPath)) {
    Write-Error "CSV not found: $CsvPath"
    exit 1
}

$rows = Import-Csv -Path $CsvPath

# ---------------------------------------------------------------------------
# Open Excel
# ---------------------------------------------------------------------------
Write-Host "Opening Excel..."
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false

$workbook  = $excel.Workbooks.Add()

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
function Set-Cell {
    param($ws, [int]$row, [int]$col, $value, [bool]$bold = $false, [string]$bg = "")
    $cell = $ws.Cells.Item($row, $col)
    $cell.Value2 = [string]$value
    if ($bold)  { $cell.Font.Bold = $true }
    if ($bg -ne "") {
        $cell.Interior.Color = [System.Drawing.ColorTranslator]::ToOle([System.Drawing.ColorTranslator]::FromHtml($bg))
    }
}

function Set-NumCell {
    param($ws, [int]$row, [int]$col, $value)
    $cell = $ws.Cells.Item($row, $col)
    $cell.Formula = "=" + [string]$value
}

# ---------------------------------------------------------------------------
# Sheet 1 — Test Cases (multi-row per step)
# ---------------------------------------------------------------------------
Write-Host "Building Test Cases sheet..."

$ws1 = $workbook.Worksheets.Item(1)
$ws1.Name = "Test Cases"

# Column layout (matches reference BizTalk excel style + traceability):
# A  Name
# B  Id
# C  Module
# D  Source Traceability
# E  Priority
# F  Status
# G  Type (Test Type)
# H  Design Test Type (Scenario Type)
# I  Description
# J  Precondition
# K  Test Step #
# L  Test Step Description
# M  Test Step Expected Result
# N  Attachments

$headers = @(
    "Name",
    "Id",
    "Module",
    "Source Traceability",
    "Priority",
    "Status",
    "Type",
    "Design Test Type",
    "Description",
    "Precondition",
    "Test Step #",
    "Test Step Description",
    "Test Step Expected Result",
    "Attachments"
)

# Header row — blue background, white bold text
for ($c = 1; $c -le $headers.Count; $c++) {
    $hCell = $ws1.Cells.Item(1, $c)
    $hCell.Value2 = $headers[$c - 1]
    $hCell.Font.Bold = $true
    $hCell.Font.Color = [System.Drawing.ColorTranslator]::ToOle([System.Drawing.Color]::White)
    $hCell.Interior.Color = [System.Drawing.ColorTranslator]::ToOle([System.Drawing.ColorTranslator]::FromHtml("#1F497D"))
}

# Priority colour map
$priorityColors = @{
    "Critical" = "#FF0000"
    "High"     = "#FFC000"
    "Medium"   = "#FFFF00"
    "Low"      = "#92D050"
}

$excelRow = 2
$tcCount  = 0

foreach ($tc in $rows) {
    $tcCount++

    # Split steps on literal \n
    $rawSteps = $tc.Test_Steps -replace '\\n', "`n"
    $steps    = $rawSteps -split "`n" | Where-Object { $_.Trim() -ne "" }

    if ($steps.Count -eq 0) { $steps = @("1. (no steps defined)") }

    # Determine alternating row shade
    $rowBg = if ($tcCount % 2 -eq 0) { "#D9E1F2" } else { "#FFFFFF" }

    $priorityBg = ""
    if ($priorityColors.ContainsKey($tc.Priority)) {
        $priorityBg = $priorityColors[$tc.Priority]
    }

    $firstRow = $true
    $stepNum  = 1

    foreach ($step in $steps) {
        # Strip leading "N. " numbering from step text if present
        $stepText = $step -replace '^\d+\.\s*', ''

        # For first row of this TC: write all columns
        if ($firstRow) {
            Set-Cell $ws1 $excelRow 1 $tc.Test_Case_Name    # Name
            Set-Cell $ws1 $excelRow 2 $tc.TC_ID             # Id
            Set-Cell $ws1 $excelRow 3 $tc.Module            # Module
            Set-Cell $ws1 $excelRow 4 $tc.Source_Traceability  # Source Traceability
            $prCell = $ws1.Cells.Item($excelRow, 5)
            $prCell.Value2 = $tc.Priority
            if ($priorityBg -ne "") {
                $prCell.Interior.Color = [System.Drawing.ColorTranslator]::ToOle([System.Drawing.ColorTranslator]::FromHtml($priorityBg))
            }
            Set-Cell $ws1 $excelRow 6 "Not Run"             # Status
            Set-Cell $ws1 $excelRow 7 $tc.Test_Type         # Type
            Set-Cell $ws1 $excelRow 8 $tc.Scenario_Type     # Design Test Type
            Set-Cell $ws1 $excelRow 9 $tc.Test_Case_Name    # Description (same as name for overview)
            Set-Cell $ws1 $excelRow 10 $tc.Preconditions    # Precondition
            $firstRow = $false
        } else {
            # Continuation rows: columns 1–10 blank (merged appearance)
            for ($bCol = 1; $bCol -le 10; $bCol++) {
                $ws1.Cells.Item($excelRow, $bCol).Value2 = ""
            }
        }

        # Step columns always written
        Set-NumCell $ws1 $excelRow 11 $stepNum              # Test Step #

        $stepCell = $ws1.Cells.Item($excelRow, 12)
        $stepCell.Value2 = $stepText                         # Test Step Description
        $stepCell.WrapText = $true

        # Expected result only on last step row
        if ($stepNum -eq $steps.Count) {
            $expCell = $ws1.Cells.Item($excelRow, 13)
            $expCell.Value2 = $tc.Expected_Result
            $expCell.WrapText = $true
        }

        Set-Cell $ws1 $excelRow 14 ""                       # Attachments

        # Alternating row background (light columns)
        if ($rowBg -ne "#FFFFFF") {
            for ($bCol = 1; $bCol -le 14; $bCol++) {
                $bgCell = $ws1.Cells.Item($excelRow, $bCol)
                if ($bgCell.Interior.ColorIndex -eq -4142) {   # xlColorIndexNone
                    $bgCell.Interior.Color = [System.Drawing.ColorTranslator]::ToOle([System.Drawing.ColorTranslator]::FromHtml($rowBg))
                }
            }
        }

        $excelRow++
        $stepNum++
    }

    # Draw a thin bottom border after the last step row of each TC
    $borderRow = $excelRow - 1
    $borderRange = $ws1.Range($ws1.Cells.Item($borderRow, 1), $ws1.Cells.Item($borderRow, 14))
    $borderRange.Borders.Item(9).LineStyle = 1    # xlEdgeBottom = 9
    $borderRange.Borders.Item(9).Weight    = 2    # xlThin = 2
}

# Auto-fit columns
Write-Host "Auto-fitting columns..."
$usedRange = $ws1.UsedRange
$usedRange.Columns.AutoFit() | Out-Null

# Freeze the header row
$ws1.Application.ActiveWindow.SplitRow = 1
$ws1.Application.ActiveWindow.FreezePanes = $true

# ---------------------------------------------------------------------------
# Sheet 2 — Coverage Summary
# ---------------------------------------------------------------------------
Write-Host "Building Coverage Summary sheet..."

if ($workbook.Worksheets.Count -lt 2) {
    $workbook.Worksheets.Add([System.Reflection.Missing]::Value, $ws1) | Out-Null
}
$ws2 = $workbook.Worksheets.Item(2)
$ws2.Name = "Coverage Summary"

# Header
$sumHeaders = @("Area", "Total TCs", "Critical", "High", "Medium", "Low")
for ($c = 1; $c -le $sumHeaders.Count; $c++) {
    $hc = $ws2.Cells.Item(1, $c)
    $hc.Value2 = $sumHeaders[$c - 1]
    $hc.Font.Bold = $true
    $hc.Interior.Color = [System.Drawing.ColorTranslator]::ToOle([System.Drawing.ColorTranslator]::FromHtml("#1F497D"))
    $hc.Font.Color = [System.Drawing.ColorTranslator]::ToOle([System.Drawing.Color]::White)
}

# Aggregate by module
$modules = $rows | Group-Object -Property Module | Sort-Object Name

$sumRow = 2
foreach ($grp in $modules) {
    $tcList   = $grp.Group
    $total    = $tcList.Count
    $critical = ($tcList | Where-Object { $_.Priority -eq "Critical" }).Count
    $high     = ($tcList | Where-Object { $_.Priority -eq "High"     }).Count
    $medium   = ($tcList | Where-Object { $_.Priority -eq "Medium"   }).Count
    $low      = ($tcList | Where-Object { $_.Priority -eq "Low"      }).Count

    Set-Cell    $ws2 $sumRow 1 $grp.Name
    Set-NumCell $ws2 $sumRow 2 $total
    Set-NumCell $ws2 $sumRow 3 $critical
    Set-NumCell $ws2 $sumRow 4 $high
    Set-NumCell $ws2 $sumRow 5 $medium
    Set-NumCell $ws2 $sumRow 6 $low

    $sumRow++
}

# Totals row
$totalTCs  = $rows.Count
$totCrit   = ($rows | Where-Object { $_.Priority -eq "Critical" }).Count
$totHigh   = ($rows | Where-Object { $_.Priority -eq "High"     }).Count
$totMed    = ($rows | Where-Object { $_.Priority -eq "Medium"   }).Count
$totLow    = ($rows | Where-Object { $_.Priority -eq "Low"      }).Count

$totCell = $ws2.Cells.Item($sumRow, 1)
$totCell.Value2 = "TOTAL"
$totCell.Font.Bold = $true
Set-NumCell $ws2 $sumRow 2 $totalTCs
Set-NumCell $ws2 $sumRow 3 $totCrit
Set-NumCell $ws2 $sumRow 4 $totHigh
Set-NumCell $ws2 $sumRow 5 $totMed
Set-NumCell $ws2 $sumRow 6 $totLow

for ($c = 1; $c -le 6; $c++) {
    $ws2.Cells.Item($sumRow, $c).Font.Bold = $true
    $ws2.Cells.Item($sumRow, $c).Interior.Color = [System.Drawing.ColorTranslator]::ToOle([System.Drawing.ColorTranslator]::FromHtml("#D9E1F2"))
}

$ws2.UsedRange.Columns.AutoFit() | Out-Null

# Scenario type breakdown
$sumRow += 2
Set-Cell $ws2 $sumRow 1 "Scenario Type Breakdown" $true
$sumRow++

$scenarios = @("Positive","Negative","Edge","Security","NFR","E2E","Contract")
foreach ($sc in $scenarios) {
    $cnt = ($rows | Where-Object { $_.Scenario_Type -eq $sc }).Count
    Set-Cell    $ws2 $sumRow 1 $sc
    Set-NumCell $ws2 $sumRow 2 $cnt
    $sumRow++
}

# ---------------------------------------------------------------------------
# Sheet 3 — Traceability Matrix
# ---------------------------------------------------------------------------
Write-Host "Building Traceability Matrix sheet..."

if ($workbook.Worksheets.Count -lt 3) {
    $workbook.Worksheets.Add([System.Reflection.Missing]::Value, $ws2) | Out-Null
}
$ws3 = $workbook.Worksheets.Item(3)
$ws3.Name = "Traceability Matrix"

$traceHeaders = @("TC ID", "Test Case Name", "Module", "Source Traceability", "Priority", "Scenario Type", "Test Type")
for ($c = 1; $c -le $traceHeaders.Count; $c++) {
    $th = $ws3.Cells.Item(1, $c)
    $th.Value2 = $traceHeaders[$c - 1]
    $th.Font.Bold = $true
    $th.Interior.Color = [System.Drawing.ColorTranslator]::ToOle([System.Drawing.ColorTranslator]::FromHtml("#1F497D"))
    $th.Font.Color = [System.Drawing.ColorTranslator]::ToOle([System.Drawing.Color]::White)
}

$traceRow = 2
foreach ($tc in $rows) {
    Set-Cell $ws3 $traceRow 1 $tc.TC_ID
    Set-Cell $ws3 $traceRow 2 $tc.Test_Case_Name
    Set-Cell $ws3 $traceRow 3 $tc.Module
    Set-Cell $ws3 $traceRow 4 $tc.Source_Traceability
    Set-Cell $ws3 $traceRow 5 $tc.Priority
    Set-Cell $ws3 $traceRow 6 $tc.Scenario_Type
    Set-Cell $ws3 $traceRow 7 $tc.Test_Type

    if ($traceRow % 2 -eq 0) {
        for ($c = 1; $c -le 7; $c++) {
            $ws3.Cells.Item($traceRow, $c).Interior.Color = [System.Drawing.ColorTranslator]::ToOle([System.Drawing.ColorTranslator]::FromHtml("#D9E1F2"))
        }
    }
    $traceRow++
}

$ws3.UsedRange.Columns.AutoFit() | Out-Null

# ---------------------------------------------------------------------------
# Activate Sheet 1 and save
# ---------------------------------------------------------------------------
$ws1.Activate()
$workbook.SaveAs($OutputPath, 51)   # 51 = xlOpenXMLWorkbook (.xlsx)
$workbook.Close($false)
$excel.Quit()

[System.Runtime.InteropServices.Marshal]::ReleaseComObject($ws3)     | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($ws2)     | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($ws1)     | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($workbook) | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($excel)    | Out-Null
[System.GC]::Collect()
[System.GC]::WaitForPendingFinalizers()

Write-Host ""
Write-Host "Done. File written to: $OutputPath"
Write-Host "  Test cases : $tcCount"
Write-Host "  Excel rows : $($excelRow - 2) (steps expanded)"
