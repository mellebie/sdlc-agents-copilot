# Generate-TestPlan.ps1
# Agent 09c (Avery) — TCPA Regulatory Compliance API
# Reads tests/TCPA-Test-Cases.csv and produces tests/TCPA-Test-Plan.xlsx
# with three sheets: Test Cases, Coverage Summary, Traceability Matrix.
#
# Numeric cells use $cell.Formula = "=" + [string]$n to avoid PowerShell 5.1
# COM Int32->String casting errors.

param(
    [string]$CsvPath  = "$PSScriptRoot\..\tests\TCPA-Test-Cases.csv",
    [string]$XlsxPath = "$PSScriptRoot\..\tests\TCPA-Test-Plan.xlsx"
)

$ErrorActionPreference = 'Stop'

# Resolve to absolute paths
$CsvPath  = (Resolve-Path $CsvPath).Path
$XlsxPath = [System.IO.Path]::GetFullPath($XlsxPath)

Write-Host "Reading CSV: $CsvPath"
$rows = Import-Csv -Path $CsvPath -Encoding UTF8

if ($rows.Count -eq 0) {
    Write-Error "CSV is empty. Aborting."
    exit 1
}

Write-Host "Loaded $($rows.Count) test cases."

# ─── Check Excel availability ─────────────────────────────────────────────────
$excelAvailable = $false
try {
    $testExcel = New-Object -ComObject Excel.Application -ErrorAction Stop
    $testExcel.Quit()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($testExcel) | Out-Null
    $excelAvailable = $true
} catch {
    Write-Warning "Excel COM not available: $_"
    Write-Host "CSV produced at: $CsvPath"
    Write-Host "Excel generation skipped (non-Windows or Excel not installed)."
    exit 0
}

# ─── Helper: set cell value safely ───────────────────────────────────────────
function Set-CellValue {
    param($Cell, $Value)
    if ($Value -is [int] -or $Value -is [long] -or $Value -is [double]) {
        $Cell.Formula = "=" + [string]$Value
    } else {
        $Cell.Value2 = [string]$Value
    }
}

# ─── Helper: apply header style ───────────────────────────────────────────────
function Apply-HeaderStyle {
    param($Range, [string]$HexColor = "2E6DA4")
    $Range.Font.Bold   = $true
    $Range.Font.Color  = 0xFFFFFF
    $Range.Interior.Color = [Convert]::ToInt32($HexColor, 16)
    $Range.HorizontalAlignment = -4108  # xlCenter
}

# ─── Open Excel ───────────────────────────────────────────────────────────────
Write-Host "Starting Excel..."
$xl  = New-Object -ComObject Excel.Application
$xl.Visible        = $false
$xl.DisplayAlerts  = $false
$wb  = $xl.Workbooks.Add()

# Ensure exactly 3 sheets
while ($wb.Sheets.Count -lt 3) { $wb.Sheets.Add() | Out-Null }
while ($wb.Sheets.Count -gt 3) { $wb.Sheets.Item($wb.Sheets.Count).Delete() }

# ═══════════════════════════════════════════════════════════════════
# SHEET 1 — Test Cases (multi-row per step)
# ═══════════════════════════════════════════════════════════════════
$ws1 = $wb.Sheets.Item(1)
$ws1.Name = "Test Cases"

$headers1 = @(
    "TC_ID","Test Case Name","Module","Source Traceability","Priority",
    "Test Type","Scenario Type","Automated Coverage",
    "Preconditions","Step #","Step Description","Expected Result"
)

$col = 1
foreach ($h in $headers1) {
    Set-CellValue $ws1.Cells.Item(1, $col) $h
    $col++
}
Apply-HeaderStyle $ws1.Range($ws1.Cells.Item(1,1), $ws1.Cells.Item(1, $headers1.Count))

$row = 2
foreach ($tc in $rows) {
    $steps = $tc.Test_Steps -split '\\n'
    if ($steps.Count -eq 0) { $steps = @($tc.Test_Steps) }

    $firstRow = $true
    $stepNum  = 1
    foreach ($step in $steps) {
        if ($firstRow) {
            Set-CellValue $ws1.Cells.Item($row, 1)  $tc.TC_ID
            Set-CellValue $ws1.Cells.Item($row, 2)  $tc.Test_Case_Name
            Set-CellValue $ws1.Cells.Item($row, 3)  $tc.Module
            Set-CellValue $ws1.Cells.Item($row, 4)  $tc.Source_Traceability
            Set-CellValue $ws1.Cells.Item($row, 5)  $tc.Priority
            Set-CellValue $ws1.Cells.Item($row, 6)  $tc.Test_Type
            Set-CellValue $ws1.Cells.Item($row, 7)  $tc.Scenario_Type
            Set-CellValue $ws1.Cells.Item($row, 8)  $tc.Automated_Coverage
            Set-CellValue $ws1.Cells.Item($row, 9)  $tc.Preconditions
            Set-CellValue $ws1.Cells.Item($row, 12) $tc.Expected_Result
            $firstRow = $false
        }
        Set-CellValue $ws1.Cells.Item($row, 10) $stepNum
        Set-CellValue $ws1.Cells.Item($row, 11) $step.Trim()
        $stepNum++
        $row++
    }
}

$ws1.Range($ws1.Cells.Item(1,1), $ws1.Cells.Item($row-1,12)).Columns.AutoFit() | Out-Null
for ($c = 1; $c -le 12; $c++) {
    if ($ws1.Columns.Item($c).ColumnWidth -gt 60) { $ws1.Columns.Item($c).ColumnWidth = 60 }
}
$ws1.Activate()
$xl.ActiveWindow.SplitRow    = 1
$xl.ActiveWindow.FreezePanes = $true
Write-Host "Sheet 1 (Test Cases) written: $($row - 2) expanded step rows."

# ═══════════════════════════════════════════════════════════════════
# SHEET 2 — Coverage Summary
# ═══════════════════════════════════════════════════════════════════
$ws2 = $wb.Sheets.Item(2)
$ws2.Name = "Coverage Summary"

# By Module
Set-CellValue $ws2.Cells.Item(1,1) "By Module"
Apply-HeaderStyle $ws2.Range($ws2.Cells.Item(1,1), $ws2.Cells.Item(1,3))
Set-CellValue $ws2.Cells.Item(2,1) "Module"
Set-CellValue $ws2.Cells.Item(2,2) "Count"
Set-CellValue $ws2.Cells.Item(2,3) "Automated %"
Apply-HeaderStyle $ws2.Range($ws2.Cells.Item(2,1), $ws2.Cells.Item(2,3)) "4472C4"
$modules = $rows | Group-Object Module | Sort-Object Name
$modRow  = 3
foreach ($m in $modules) {
    $auto = ($m.Group | Where-Object { $_.Test_Type -ne 'Manual' }).Count
    Set-CellValue $ws2.Cells.Item($modRow, 1) $m.Name
    $ws2.Cells.Item($modRow, 2).Formula = "=" + [string]$m.Count
    $pct = if ($m.Count -gt 0) { [math]::Round(($auto / $m.Count) * 100, 0) } else { 0 }
    $ws2.Cells.Item($modRow, 3).Formula = "=" + [string]$pct
    $modRow++
}
$ws2.Cells.Item($modRow, 1).Value2    = "TOTAL"
$ws2.Cells.Item($modRow, 1).Font.Bold = $true
$ws2.Cells.Item($modRow, 2).Formula   = "=" + [string]$rows.Count

# By Priority
$priCol = 5
Set-CellValue $ws2.Cells.Item(1,$priCol) "By Priority"
Apply-HeaderStyle $ws2.Range($ws2.Cells.Item(1,$priCol), $ws2.Cells.Item(1,$priCol+1))
Set-CellValue $ws2.Cells.Item(2,$priCol)   "Priority"
Set-CellValue $ws2.Cells.Item(2,$priCol+1) "Count"
Apply-HeaderStyle $ws2.Range($ws2.Cells.Item(2,$priCol), $ws2.Cells.Item(2,$priCol+1)) "4472C4"
$priRow = 3
foreach ($p in @("Critical","High","Medium","Low")) {
    $cnt = ($rows | Where-Object { $_.Priority -eq $p }).Count
    Set-CellValue $ws2.Cells.Item($priRow, $priCol) $p
    $ws2.Cells.Item($priRow, $priCol+1).Formula = "=" + [string]$cnt
    $priRow++
}

# By Scenario Type
$scCol = 8
Set-CellValue $ws2.Cells.Item(1,$scCol) "By Scenario Type"
Apply-HeaderStyle $ws2.Range($ws2.Cells.Item(1,$scCol), $ws2.Cells.Item(1,$scCol+1))
Set-CellValue $ws2.Cells.Item(2,$scCol)   "Scenario Type"
Set-CellValue $ws2.Cells.Item(2,$scCol+1) "Count"
Apply-HeaderStyle $ws2.Range($ws2.Cells.Item(2,$scCol), $ws2.Cells.Item(2,$scCol+1)) "4472C4"
$scRow = 3
foreach ($s in @("Positive","Negative","Edge","Security","NFR","Contract","E2E")) {
    $cnt = ($rows | Where-Object { $_.Scenario_Type -eq $s }).Count
    Set-CellValue $ws2.Cells.Item($scRow, $scCol) $s
    $ws2.Cells.Item($scRow, $scCol+1).Formula = "=" + [string]$cnt
    $scRow++
}

# Automation Split
$autoCol = 11
Set-CellValue $ws2.Cells.Item(1,$autoCol) "Automation Split"
Apply-HeaderStyle $ws2.Range($ws2.Cells.Item(1,$autoCol), $ws2.Cells.Item(1,$autoCol+1))
Set-CellValue $ws2.Cells.Item(2,$autoCol)   "Test Type"
Set-CellValue $ws2.Cells.Item(2,$autoCol+1) "Count"
Apply-HeaderStyle $ws2.Range($ws2.Cells.Item(2,$autoCol), $ws2.Cells.Item(2,$autoCol+1)) "4472C4"
$autoRow = 3
foreach ($t in @("Automated","Manual+Automated","Manual")) {
    $cnt = ($rows | Where-Object { $_.Test_Type -eq $t }).Count
    Set-CellValue $ws2.Cells.Item($autoRow, $autoCol) $t
    $ws2.Cells.Item($autoRow, $autoCol+1).Formula = "=" + [string]$cnt
    $autoRow++
}

$ws2.UsedRange.Columns.AutoFit() | Out-Null
Write-Host "Sheet 2 (Coverage Summary) written."

# ═══════════════════════════════════════════════════════════════════
# SHEET 3 — Traceability Matrix
# ═══════════════════════════════════════════════════════════════════
$ws3 = $wb.Sheets.Item(3)
$ws3.Name = "Traceability Matrix"

$headers3 = @(
    "TC_ID","Test Case Name","Module","Source Traceability","Priority",
    "Test Type","Scenario Type","Automated Coverage","Expected Result"
)

$col = 1
foreach ($h in $headers3) {
    Set-CellValue $ws3.Cells.Item(1, $col) $h
    $col++
}
Apply-HeaderStyle $ws3.Range($ws3.Cells.Item(1,1), $ws3.Cells.Item(1, $headers3.Count))

$row = 2
foreach ($tc in $rows) {
    Set-CellValue $ws3.Cells.Item($row, 1) $tc.TC_ID
    Set-CellValue $ws3.Cells.Item($row, 2) $tc.Test_Case_Name
    Set-CellValue $ws3.Cells.Item($row, 3) $tc.Module
    Set-CellValue $ws3.Cells.Item($row, 4) $tc.Source_Traceability
    Set-CellValue $ws3.Cells.Item($row, 5) $tc.Priority
    Set-CellValue $ws3.Cells.Item($row, 6) $tc.Test_Type
    Set-CellValue $ws3.Cells.Item($row, 7) $tc.Scenario_Type
    Set-CellValue $ws3.Cells.Item($row, 8) $tc.Automated_Coverage
    Set-CellValue $ws3.Cells.Item($row, 9) $tc.Expected_Result
    $row++
}

$ws3.Activate()
$xl.ActiveWindow.SplitRow    = 1
$xl.ActiveWindow.FreezePanes = $true
$ws3.UsedRange.Columns.AutoFit() | Out-Null
for ($c = 1; $c -le $headers3.Count; $c++) {
    if ($ws3.Columns.Item($c).ColumnWidth -gt 60) { $ws3.Columns.Item($c).ColumnWidth = 60 }
}
Write-Host "Sheet 3 (Traceability Matrix) written."

# ═══════════════════════════════════════════════════════════════════
# Save and close
# ═══════════════════════════════════════════════════════════════════
$ws1.Activate()
Write-Host "Saving to: $XlsxPath"
$wb.SaveAs($XlsxPath, 51)   # 51 = xlOpenXMLWorkbook
$wb.Close($false)
$xl.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($wb) | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($xl) | Out-Null
[System.GC]::Collect()
[System.GC]::WaitForPendingFinalizers()

Write-Host "Done. XLSX written to: $XlsxPath"
