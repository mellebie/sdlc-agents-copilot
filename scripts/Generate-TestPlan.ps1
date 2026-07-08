param()

$outPath = "C:\Users\mark.ellebie\Projects\Southern\Claude_projects\sdlc-agents\tests\TCPA-Test-Plan.xlsx"

$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false
$wb = $excel.Workbooks.Add()

function SetCell($ws, $row, $col, $val, $bold=$false, $wrap=$true, $bg=$null) {
    $cell = $ws.Cells.Item($row, $col)
    $cell.Value2 = $val
    if ($bold) { $cell.Font.Bold = $true }
    $cell.WrapText = $wrap
    if ($null -ne $bg) { $cell.Interior.Color = $bg }
}

# ── COVER PAGE ──────────────────────────────────────────────────────────────
$cvr = $wb.Sheets.Item(1)
$cvr.Name = "Cover Page"
$cvr.Tab.Color = 0x0070C0
SetCell $cvr 2 2 "TCPA Compliance Engine - Test Case Coverage Plan" $true $false
$cvr.Cells.Item(2,2).Font.Size = 18
$meta = @(
  @(4,"Project:","Southern Company Gas - TCPA Regulatory Compliance for Text Messages"),
  @(5,"Document Type:","Test Case Coverage Plan"),
  @(6,"Version:","1.0"),
  @(7,"Date:","2026-06-26"),
  @(8,"Regulatory Deadline:","January 31, 2027"),
  @(9,"Compliance Standard:","TCPA 47 CFR Section 64.1200"),
  @(11,"Applications In Scope:","BizTalk, GCMA, KMI, ARM, VNG (CCB/My Account - activation gate pending)"),
  @(12,"SMS Platforms:","Cool Text, Twilio"),
  @(14,"Source Artifacts:","BRD, PRD (inputs/prd.md), Requirements (outputs/requirements.md)"),
  @(15,"","Specifications (outputs/specs.md), Architecture (outputs/architecture.md)"),
  @(16,"","Stories (outputs/stories.md), Risks (outputs/risks.md)")
)
foreach ($m in $meta) {
    if ($m[1] -ne "") { SetCell $cvr $m[0] 2 $m[1] $true $false }
    SetCell $cvr $m[0] 3 $m[2] $false $false
}
$cvr.Columns.Item(2).ColumnWidth = 22
$cvr.Columns.Item(3).ColumnWidth = 70

# ── TEST CASES SHEET ────────────────────────────────────────────────────────
$ws = $wb.Sheets.Add()
$ws.Move([System.Reflection.Missing]::Value, $wb.Sheets.Item($wb.Sheets.Count))
$ws.Name = "TCPA Test Cases"
$ws.Tab.Color = 0x00B050

$hdrBg = 0x203864
$hdrs = @("TC ID","Test Case Name","Module / Area","Req / Story Ref","Priority","Test Type","Scenario Type","Pre-conditions","Test Steps","Expected Result","Pass / Fail","Notes / Environment")
for ($c = 1; $c -le $hdrs.Count; $c++) {
    $cell = $ws.Cells.Item(1,$c)
    $cell.Value2 = $hdrs[$c-1]
    $cell.Font.Bold = $true
    $cell.Font.Color = 0xFFFFFF
    $cell.Interior.Color = $hdrBg
    $cell.WrapText = $true
}
$widths = @(12,40,22,20,10,12,14,32,58,48,12,26)
for ($c = 1; $c -le $widths.Count; $c++) { $ws.Columns.Item($c).ColumnWidth = $widths[$c-1] }

$colPos  = 0xE2EFDA
$colNeg  = 0xFCE4D6
$colEdge = 0xFFF2CC
$colSec  = 0xDDEBF7
$colNFR  = 0xEDEDED

# ID | Name | Module | Ref | Priority | TestType | ScenarioType | Precond | Steps | Expected | BgColor
$tc = [System.Collections.ArrayList]@()

# APPLICATION REGISTRY
[void]$tc.Add(@("TCPA-TC-001","Application Registry - Active app lookup resolves correctly","Application Registry","STORY-001 / SPEC-014 / REQ-001","Critical","Automated","Positive","Active registration seeded for CT-GCMA-001 (ApplicationName, CallbackUrl, IsActive=true).","1. POST /api/v1/sms/outbound with cool_text_account_id = CT-GCMA-001.`n2. Inspect request processing logs.","System resolves ApplicationName, CallbackUrl, IsActive=true. Request proceeds to compliance gate. No UNREGISTERED_ACCOUNT warning.",$colPos))
[void]$tc.Add(@("TCPA-TC-002","Application Registry - Unregistered account treated as pass-through","Application Registry","STORY-001 AC-002 / SPEC-014","High","Automated","Negative","No registry entry for CT-UNKNOWN-999.","1. POST /api/v1/sms/outbound with cool_text_account_id = CT-UNKNOWN-999.`n2. Inspect response and logs.","HTTP 200. Response status = UNREGISTERED_ACCOUNT. Message forwarded without compliance check. No compliance event logged. Warning in operational log.",$colNeg))
[void]$tc.Add(@("TCPA-TC-003","Application Registry - Inactive CCB app treated as unregistered","Application Registry","STORY-001 AC-003 / SPEC-014 BR-063","High","Automated","Edge","CCB/My Account registered with IsActive=false (deployment default per RISK-003).","1. POST outbound using CCB Cool Text account ID.`n2. Check response and AuditLog.","HTTP 200. status = UNREGISTERED_ACCOUNT. No compliance event written. Confirms CCB activation gate enforced at deployment.",$colEdge))
[void]$tc.Add(@("TCPA-TC-004","Application Registry - Cache serves requests within 5-min TTL","Application Registry","STORY-001 AC-004 / NFR-Cache","Medium","Automated","Edge","App registry seeded. Cache warmed at startup.","1. Send 5 sequential requests within TTL window.`n2. Count DB queries via mock or profiler.","All 5 resolved from cache. Zero extra DB reads. Lookup < 5ms. TTL expiry triggers refresh.",$colEdge))

# OUTBOUND SMS GATE
[void]$tc.Add(@("TCPA-TC-005","Outbound SMS - Opted-in number forwarded to Cool Text","Outbound Compliance Gate","STORY-002 AC-001 / SPEC-001","Critical","Automated","Positive","Active app registered. OPT-IN record for +12025551001.","1. POST /api/v1/sms/outbound: {cool_text_account_id, destination_cell_number: +12025551001, message_body} + valid X-API-Key.`n2. Verify Cool Text client called.","HTTP 200. Response: status = FORWARDED, cool_text_message_id populated. Cool Text API called exactly once. No BlockedOutbound audit event.",$colPos))
[void]$tc.Add(@("TCPA-TC-006","Outbound SMS - Opted-out number suppressed","Outbound Compliance Gate","STORY-002 AC-002 / SPEC-001 BR-001","Critical","Automated","Negative","Active app registered. OPT-OUT record for +12025551002.","1. POST /api/v1/sms/outbound for +12025551002 with valid API key.`n2. Verify Cool Text client NOT called.`n3. Query AuditLogEntries.","HTTP 200. status = SUPPRESSED, suppression_reason = OPT_OUT. Cool Text NOT called. BlockedOutbound audit entry written.",$colNeg))
[void]$tc.Add(@("TCPA-TC-007","Outbound SMS - No status record defaults to opt-in (BR-001)","Outbound Compliance Gate","STORY-002 / SPEC-001 BR-001","High","Automated","Edge","Active app. No record in DB for +12025551003.","1. POST /api/v1/sms/outbound for +12025551003.","HTTP 200. status = FORWARDED. Confirms BR-001: unknown number treated as opted-in.",$colEdge))
[void]$tc.Add(@("TCPA-TC-008","Outbound SMS - Fail-closed when database unavailable","Outbound Compliance Gate","STORY-002 AC-004 / SPEC-001 BR-007 / RISK-008","Critical","Manual","Negative","Simulate DB outage (stop DB service or invalid connection string).","1. POST /api/v1/sms/outbound for any number.`n2. Verify Cool Text not called.","HTTP 503. Body: 'Compliance check unavailable; message not forwarded.' Cool Text NOT called. Error logged.",$colNeg))
[void]$tc.Add(@("TCPA-TC-009","Outbound SMS - Missing X-API-Key returns 401","Outbound Compliance Gate","STORY-002 AC-006 / SEC-001","Critical","Automated","Negative","None.","1. POST /api/v1/sms/outbound WITHOUT X-API-Key header.","HTTP 401 Unauthorized. Request not processed. No Cool Text call. No compliance event.",$colSec))
[void]$tc.Add(@("TCPA-TC-010","Outbound SMS - Wrong X-API-Key returns 401","Outbound Compliance Gate","STORY-002 AC-006 / SEC-001","Critical","Automated","Negative","None.","1. POST with X-API-Key: INVALID_KEY_VALUE.","HTTP 401. Constant-time comparison prevents timing oracle. No information leak.",$colSec))
[void]$tc.Add(@("TCPA-TC-011","Outbound SMS - Missing required field returns 400","Outbound Compliance Gate","STORY-002 AC-005","High","Automated","Negative","Valid API key.","1. POST omitting destination_cell_number.`n2. Repeat omitting cool_text_account_id.`n3. Repeat omitting message_body.","HTTP 400 for each. ProblemDetails response with field-level validation errors.",$colNeg))
[void]$tc.Add(@("TCPA-TC-012","Outbound SMS - Invalid E.164 phone number returns 400","Outbound Compliance Gate","STORY-002 AC-005 / SPEC-001","High","Automated","Negative","Valid API key.","1. POST with destination_cell_number = 12025551234 (no +).`n2. POST with 555-1234.`n3. POST with abc.","HTTP 400 for all three. Error references E.164 format requirement.",$colNeg))

# INBOUND WEBHOOK
[void]$tc.Add(@("TCPA-TC-013","Inbound webhook - Non-opt-out message forwarded to app callback","Inbound SMS Proxy","STORY-003 AC-001 / SPEC-002","Critical","Automated","Positive","Active app with callback URL. Valid HMAC secret.","1. POST /api/v1/sms/inbound: valid HMAC, message_body = 'What time will the tech arrive?'.`n2. Measure response time.`n3. Verify app callback.","HTTP 200 in < 200ms. Response: {received: true}. App callback invoked with sender_cell_number, message_body, cool_text_account_id, received_timestamp.",$colPos))
[void]$tc.Add(@("TCPA-TC-014","Inbound webhook - Invalid HMAC signature returns 401","Inbound SMS Proxy","STORY-003 AC-005 / SPEC-002","Critical","Automated","Negative","None.","1. POST /api/v1/sms/inbound with tampered HMAC in X-CoolText-Signature header.","HTTP 401. No processing. Security event logged.",$colSec))
[void]$tc.Add(@("TCPA-TC-015","Inbound webhook - Missing HMAC header returns 401","Inbound SMS Proxy","STORY-003 AC-005","Critical","Automated","Negative","None.","1. POST /api/v1/sms/inbound with no signature header.","HTTP 401. Security event logged. No opt-out processing.",$colSec))
[void]$tc.Add(@("TCPA-TC-016","Inbound webhook - Unregistered account: 200 returned, message discarded","Inbound SMS Proxy","STORY-003 AC-003","Medium","Automated","Edge","No registry entry for payload account ID.","1. POST inbound with valid HMAC and unregistered cool_text_account_id.","HTTP 200 (Cool Text must receive ACK). Message discarded. Warning logged. No opt-out check performed.",$colEdge))
[void]$tc.Add(@("TCPA-TC-017","Inbound webhook - 200 returned immediately before background processing","Inbound SMS Proxy","STORY-003 / SPEC-002 ADR-fire-and-forget","High","Automated","Edge","Active app. Valid HMAC.","1. POST inbound STOP keyword.`n2. Measure HTTP response time.`n3. Poll DB for opt-out record write.","HTTP 200 returned in < 200ms. DB write occurs after response. Prevents Cool Text timeout retry.",$colEdge))
[void]$tc.Add(@("TCPA-TC-018","Inbound callback - 3 retries with backoff on app callback failure","Inbound SMS Proxy","STORY-003 AC-004 / SPEC-002","High","Manual","Edge","Active app. Callback URL configured to return 503.","1. POST inbound non-opt-out message.`n2. Callback URL returns 503 for all attempts.`n3. Monitor retry count and logs.","3 delivery attempts with exponential backoff. Permanent failure logged after 3 fails. No indefinite retry.",$colEdge))

# OPT-OUT KEYWORD DETECTION
[void]$tc.Add(@("TCPA-TC-019","Keyword detection - STOP triggers opt-out","Opt-Out Detection","STORY-004 AC-001 / SPEC-003","Critical","Automated","Positive","None - pure logic.","1. Process message body = 'STOP'.`n2. Check is_opt_out_keyword and matched_keyword.","is_opt_out_keyword = true. matched_keyword = STOP.",$colPos))
[void]$tc.Add(@("TCPA-TC-020","Keyword detection - All 7 TCPA keywords detected","Opt-Out Detection","STORY-004 AC-008 / SPEC-003","Critical","Automated","Positive","None.","1. Process messages each containing one standalone keyword: STOP, QUIT, END, REVOKE, OPT-OUT, CANCEL, UNSUBSCRIBE.","is_opt_out_keyword = true for all 7. matched_keyword correctly names each.",$colPos))
[void]$tc.Add(@("TCPA-TC-021","Keyword detection - Case-insensitive matching","Opt-Out Detection","STORY-004 AC-007 / SPEC-003 BR-011","High","Automated","Positive","None.","1. Process: 'stop', 'Stop', 'STOP', 'sToP', 'Quit', 'END', 'end'.","is_opt_out_keyword = true for all variants. Case-insensitive regex confirmed.",$colPos))
[void]$tc.Add(@("TCPA-TC-022","Keyword detection - Keyword in sentence (word-boundary match)","Opt-Out Detection","STORY-004 AC-002 / SPEC-003","High","Automated","Positive","None.","1. Process: 'Please stop sending me texts'.`n2. Process: 'I want to cancel my subscription'.","is_opt_out_keyword = true for both. Word-boundary regex matches STOP and CANCEL as standalone words.",$colPos))
[void]$tc.Add(@("TCPA-TC-023","Keyword detection - NONSTOP substring does NOT trigger opt-out","Opt-Out Detection","STORY-004 AC-003 / SPEC-003","High","Automated","Negative","None.","1. Process: 'NONSTOP service is great'.`n2. Check is_opt_out_keyword.","is_opt_out_keyword = false. Word-boundary prevents partial STOP match.",$colNeg))
[void]$tc.Add(@("TCPA-TC-024","Keyword detection - CANCELLATION does NOT match CANCEL","Opt-Out Detection","STORY-004 AC-004 / SPEC-003","High","Automated","Negative","None.","1. Process: 'CANCELLATION confirmed'.`n2. Process: 'My unsubscription was processed'.","is_opt_out_keyword = false for both. Partial word matches rejected.",$colNeg))
[void]$tc.Add(@("TCPA-TC-025","Keyword detection - OPT-OUT hyphenated token matched","Opt-Out Detection","STORY-004 AC-005 / SPEC-003","High","Automated","Positive","None.","1. Process: 'OPT-OUT'.`n2. Process: 'opt-out please'.`n3. Process: 'OPT IN' (should NOT match).","OPT-OUT and opt-out: true. 'OPT IN': false.",$colPos))
[void]$tc.Add(@("TCPA-TC-026","Keyword detection - Empty/null message body","Opt-Out Detection","STORY-004 AC-009","Medium","Automated","Edge","None.","1. Process body = '' (empty).`n2. Process body = null.","is_opt_out_keyword = false. Warning logged. No exception.",$colEdge))

# OPT-OUT STATUS WRITE
[void]$tc.Add(@("TCPA-TC-027","Opt-out write - New record created with correct timestamp","Opt-Out Management","STORY-005 AC-001 / SPEC-004","Critical","Automated","Positive","No prior record for +12025551010. DB available.","1. Trigger opt-out for +12025551010 (STOP inbound).`n2. Query CellNumberOptOutRecords.","Record: CellPhoneNumber=+12025551010, Status=OPT_OUT, OptOutTimestamp within 2s. previous_status=OPT_IN.",$colPos))
[void]$tc.Add(@("TCPA-TC-028","Opt-out write - Idempotent: no duplicate when already opted out","Opt-Out Management","STORY-005 AC-002 / SPEC-004","High","Automated","Edge","OPT-OUT record exists for +12025551011.","1. Send STOP again for +12025551011.`n2. COUNT records for +12025551011.","Count = 1 (no duplicate). previous_status=OPT_OUT. Confirmation SMS not re-sent (BR-019).",$colEdge))
[void]$tc.Add(@("TCPA-TC-029","Opt-out write - Global scope: opt-out via GCMA blocks VNG outbound","Opt-Out Management","STORY-005 AC-003 / SPEC-004 BR-013","Critical","Automated","Positive","Both GCMA and VNG registered active. +12025551012 opted in.","1. STOP inbound via GCMA account for +12025551012.`n2. Wait for DB write.`n3. POST outbound from VNG to +12025551012.","VNG outbound returns SUPPRESSED. Global opt-out scope confirmed across all SCG applications.",$colPos))
[void]$tc.Add(@("TCPA-TC-030","Opt-out write - DB failure: status not written, confirmation not sent","Opt-Out Management","STORY-005 AC-004 / SPEC-004","Critical","Manual","Negative","DB unavailable for write.","1. Trigger opt-out with DB write fail.`n2. Check confirmation dispatch.`n3. Check logs.","status_write_success=false. Confirmation SMS NOT sent (BR-017). Critical alert emitted. No silent drop.",$colNeg))

# CONFIRMATION SMS
[void]$tc.Add(@("TCPA-TC-031","Confirmation SMS - Dispatched within 60 seconds of opt-out","Opt-Out Confirmation","STORY-006 / SPEC-005","Critical","Automated","Positive","Active app. Cool Text mock records calls.","1. POST inbound STOP for +12025551013.`n2. Poll for confirmation SMS dispatch within 60s.","Confirmation SMS dispatched to +12025551013 within 60s. Message text matches TCPA:OptOutConfirmationSmsText config value.",$colPos))
[void]$tc.Add(@("TCPA-TC-032","Confirmation SMS - Not sent for already-opted-out number","Opt-Out Confirmation","STORY-006 / SPEC-005 BR-019","High","Automated","Edge","+12025551014 already has OPT-OUT record.","1. Send STOP again for +12025551014.`n2. Monitor Cool Text mock for calls.","Confirmation SMS NOT dispatched. BR-019 enforced.",$colEdge))
[void]$tc.Add(@("TCPA-TC-033","Confirmation SMS - Not sent if opt-out DB write fails","Opt-Out Confirmation","STORY-006 / SPEC-005 BR-017","Critical","Manual","Negative","DB unavailable for write.","1. Trigger opt-out with DB failure.`n2. Monitor confirmation dispatch.","Confirmation NOT sent. Prevents false confirmation when opt-out not actually recorded.",$colNeg))

# ADMIN RE-OPT-IN
[void]$tc.Add(@("TCPA-TC-034","Admin re-opt-in - Happy path: status changed to OPT-IN","Admin / Re-Opt-In","STORY-007 AC-001 / SPEC-007","Critical","Automated","Positive","OPT-OUT record for +12025551020. Helpdesk JWT token with tcpa.helpdesk role.","1. PUT /admin/v1/opt-out/re-opt-in: {cellPhoneNumber: +12025551020, reason: 'Customer called to request re-opt-in'} + Bearer.`n2. Query DB.","HTTP 200. success=true, previousStatus=OPT_OUT, newStatus=OPT_IN. DB updated. ReOptIn audit entry written.",$colPos))
[void]$tc.Add(@("TCPA-TC-035","Admin re-opt-in - 409 when no prior opt-out record","Admin / Re-Opt-In","STORY-007 / SPEC-007 BR-038","High","Automated","Negative","No record for +12025551021.","1. PUT re-opt-in for +12025551021.","HTTP 409 Conflict. Security event logged. DB unchanged.",$colNeg))
[void]$tc.Add(@("TCPA-TC-036","Admin re-opt-in - Idempotent when number already opted in","Admin / Re-Opt-In","STORY-007 / SPEC-007","Medium","Automated","Edge","OPT-IN record for +12025551022.","1. PUT re-opt-in for +12025551022.","HTTP 200. Idempotent no-op. Informational message returned. No error.",$colEdge))
[void]$tc.Add(@("TCPA-TC-037","Admin re-opt-in - Missing Bearer token returns 401","Admin / Re-Opt-In","STORY-007 / SPEC-007 BR-031","Critical","Automated","Negative","None.","1. PUT without Authorization header.","HTTP 401. Request rejected. DB unchanged.",$colSec))
[void]$tc.Add(@("TCPA-TC-038","Admin re-opt-in - Valid JWT without required role returns 403","Admin / Re-Opt-In","STORY-007 / SPEC-007 BR-032","Critical","Automated","Negative","JWT token without tcpa.helpdesk or tcpa.compliance_officer role.","1. PUT with underprivileged JWT.","HTTP 403 Forbidden. Security event logged.",$colSec))
[void]$tc.Add(@("TCPA-TC-039","Admin re-opt-in - Reason < 20 characters returns 400","Admin / Re-Opt-In","STORY-007 / SPEC-007","Medium","Automated","Negative","Valid JWT.","1. PUT with reason = 'Too short'.","HTTP 400. Validation error: reason minimum 20 characters.",$colNeg))
[void]$tc.Add(@("TCPA-TC-040","Admin re-opt-in - Invalid E.164 number returns 400","Admin / Re-Opt-In","STORY-007 / SPEC-007","Medium","Automated","Negative","Valid JWT.","1. PUT with cellPhoneNumber = 5551234 (no country code).","HTTP 400. Validation error referencing E.164 format.",$colNeg))

# ADMIN STATUS
[void]$tc.Add(@("TCPA-TC-041","Admin status lookup - Returns masked phone number (last 4 digits)","Admin / Re-Opt-In","STORY-010 / SPEC-010 BR-037","High","Automated","Positive","OPT-OUT record for +12025559876. Valid JWT.","1. GET /admin/v1/opt-out/status/+12025559876 (URL-encoded %2B).`n2. Inspect maskedCellNumber field.","HTTP 200. maskedCellNumber = ****9876. Full number absent from response. optOutStatus = OPT_OUT.",$colPos))
[void]$tc.Add(@("TCPA-TC-042","Admin status lookup - 404 when no record","Admin / Re-Opt-In","STORY-010 / SPEC-010","Medium","Automated","Negative","No record for +12025559999.","1. GET status for +12025559999.","HTTP 404 Not Found.",$colNeg))
[void]$tc.Add(@("TCPA-TC-043","Admin status - Full phone number never appears in response body","Admin / Re-Opt-In","BR-037 / NFR-Privacy","Critical","Automated","Positive","Any opted-out 10-digit number.","1. GET status.`n2. Search entire JSON response for full phone number.","Full number absent. Only last 4 digits in maskedCellNumber. PII minimisation enforced.",$colSec))

# AUDIT LOGGING
[void]$tc.Add(@("TCPA-TC-044","Audit log - OptOut event written on opt-out","Audit Logging","STORY-008 / SPEC-008","Critical","Automated","Positive","DB available. Opt-out triggered.","1. Trigger opt-out for +12025551030.`n2. Query AuditLogEntries WHERE EventType=OptOut.","Entry created: EventType=OptOut, CellPhoneNumberLast4=last 4 digits only, RecordId=unique GUID, CreatedAt within 2s.",$colPos))
[void]$tc.Add(@("TCPA-TC-045","Audit log - BlockedOutbound event written on suppression","Audit Logging","STORY-009 / SPEC-008","Critical","Automated","Positive","OPT-OUT record. Outbound SMS attempted.","1. POST outbound to opted-out number.`n2. Query AuditLogEntries WHERE EventType=BlockedOutbound.","BlockedOutbound entry created with unique RecordId and accurate timestamp.",$colPos))
[void]$tc.Add(@("TCPA-TC-046","Audit log - ReOptIn event written on successful re-opt-in","Audit Logging","STORY-009 / SPEC-008","High","Automated","Positive","Opted-out number. Admin re-opt-in executed.","1. PUT re-opt-in.`n2. Query AuditLogEntries WHERE EventType=ReOptIn.","ReOptIn entry: RequestedBy contains agent user ID, timestamp accurate.",$colPos))
[void]$tc.Add(@("TCPA-TC-047","Audit log - UPDATE rejected by DDL trigger (WORM immutability)","Audit Logging","SPEC-008 BR-053 / NFS-009","Critical","Manual","Positive","AuditLogEntries has at least one record.","1. Connect to TCPA DB with TCPA_App credentials.`n2. Execute: UPDATE dbo.AuditLogEntries SET EventType=0 WHERE Id=<any>.","UPDATE rejected. Error from DDL trigger. Record unchanged. WORM immutability confirmed.",$colPos))
[void]$tc.Add(@("TCPA-TC-048","Audit log - DELETE rejected by DDL trigger","Audit Logging","SPEC-008 BR-053","Critical","Manual","Positive","AuditLogEntries has at least one record.","1. Execute: DELETE FROM dbo.AuditLogEntries WHERE Id=<any>.","DELETE rejected by DDL trigger. Record intact.",$colPos))
[void]$tc.Add(@("TCPA-TC-049","Audit log - 5-year retention: records not deleted","Audit Logging","SPEC-008 / NFR-011 / RISK-009","Critical","Manual","Positive","AuditLogEntries has records dated 5+ years ago.","1. Insert record with CreatedAt = UtcNow minus 5 years.`n2. Run any cleanup jobs.`n3. Verify record persists.","Record present after 5-year mark. No automated deletion. NFS-009 retention confirmed.",$colPos))
[void]$tc.Add(@("TCPA-TC-050","Audit log - Full phone number never stored in audit entries","Audit Logging","NFS-007c / BR-068","Critical","Automated","Positive","Multiple opt-out and suppression events.","1. SELECT * FROM AuditLogEntries.`n2. Scan all rows for full E.164 numbers.","No full phone numbers. Only last 4 digits in CellPhoneNumberLast4 column. PII protection enforced.",$colSec))

# COMPLIANCE REPORTING
[void]$tc.Add(@("TCPA-TC-051","Compliance report - Weekly report generated by Azure Function","Compliance Reporting","STORY-011 / SPEC-011","High","Automated","Positive","AuditLogEntries populated. Azure Function deployed.","1. Trigger WeeklyComplianceReportFunction (timer or manual).`n2. Retrieve report.","Report covers preceding 7-day window. Includes opt-out count, blocked outbound count, re-opt-in count. No errors.",$colPos))
[void]$tc.Add(@("TCPA-TC-052","Compliance report - Auth required (Bearer JWT)","Compliance Reporting","STORY-011 / SPEC-011 BR-055","High","Automated","Negative","Report endpoint deployed.","1. GET report endpoint without Authorization header.","HTTP 401 Unauthorized.",$colSec))
[void]$tc.Add(@("TCPA-TC-053","Compliance report - Date range > 90 days returns 400","Compliance Reporting","STORY-011 / SPEC-011","Medium","Automated","Negative","Valid Compliance Officer JWT.","1. GET report with from=2026-01-01, to=2026-12-31 (>90 days).","HTTP 400. Error: date range cannot exceed 90 days.",$colNeg))

# HEALTH / OPS
[void]$tc.Add(@("TCPA-TC-054","Health endpoint - Returns 200 Healthy when DB reachable","Health / Ops","NFR / TASK-059","High","Automated","Positive","DB available.","1. GET /health.","HTTP 200. Body: tcpa-database status = Healthy. No authentication required.",$colPos))
[void]$tc.Add(@("TCPA-TC-055","Health endpoint - Returns 503 when DB unreachable","Health / Ops","NFR / TASK-059","High","Manual","Negative","DB connection made unavailable.","1. GET /health with DB down.","HTTP 503 Unhealthy. tcpa-database shown as Unhealthy. Enables alerting to detect outage.",$colNeg))
[void]$tc.Add(@("TCPA-TC-056","Correlation ID - Propagated through request lifecycle","Health / Ops","SPEC-013 / CR-001","Medium","Automated","Positive","CorrelationIdMiddleware active.","1. POST outbound with X-Correlation-ID: test-corr-abc123.`n2. Check application logs.","X-Correlation-ID appears in all log entries for the request. Enables distributed tracing.",$colPos))
[void]$tc.Add(@("TCPA-TC-057","Correlation ID - Generated if header absent","Health / Ops","SPEC-013 / CR-001","Low","Automated","Edge","Application running.","1. POST without X-Correlation-ID.`n2. Check logs.","System-generated UUID appears in all log entries for the request.",$colEdge))
[void]$tc.Add(@("TCPA-TC-058","Correlation ID - Injection attempt sanitized (SEC-002)","Security","SEC-002 / SPEC-013","Critical","Automated","Negative","Application running.","1. POST with X-Correlation-ID: '; DROP TABLE AuditLogEntries--'.`n2. POST with '<script>alert(1)</script>'.`n3. Check logs.","Both values sanitized to alphanumeric/hyphen/underscore only. No log injection. Request proceeds normally.",$colSec))

# SECURITY
[void]$tc.Add(@("TCPA-TC-059","Security - SQL injection in message body not executed","Security","RISK-013 / OWASP A03","Critical","Automated","Negative","DB connected.","1. POST inbound with message_body = `"'; DROP TABLE CellNumberOptOutRecords;--`" (valid HMAC).`n2. Verify DB tables.","Request handled normally. DB tables intact. No SQL executed from user input. Parameterized queries confirmed.",$colSec))
[void]$tc.Add(@("TCPA-TC-060","Security - API key never appears in application logs","Security","NFR-Privacy / SEC","Critical","Automated","Positive","Logging enabled. Valid API key used.","1. Make valid outbound call.`n2. Search all log output for API key value.","API key absent from all log lines. Secrets not logged.",$colSec))
[void]$tc.Add(@("TCPA-TC-061","Security - HMAC comparison is constant-time (anti-timing oracle)","Security","ADR-007 / CWE-208","Critical","Automated","Edge","Application running.","1. Send 500 requests with varying-length invalid HMAC values.`n2. Measure response time distribution.","Response times uniform regardless of how many HMAC bytes match. CryptographicOperations.FixedTimeEquals confirmed.",$colSec))
[void]$tc.Add(@("TCPA-TC-062","Security - No sensitive files modified (.tf .yml .yaml .env)","Security","CLAUDE.md Global Rule / SPEC-SEC","Critical","Automated","Positive","Codebase under version control.","1. git diff --name-only on all generated files.`n2. Filter for .tf, .bicep, .yml, .yaml, .cfn, .env.","Zero results. Infrastructure and config files untouched.",$colSec))

# PERFORMANCE / NFR
[void]$tc.Add(@("TCPA-TC-063","Performance - Outbound compliance gate P99 < 500ms","Performance / NFR","NFS-001 / NFR-003","High","Automated","Positive","DB with 100k+ records. Load test tool.","1. Run 1000 concurrent outbound requests.`n2. Measure P99 latency.","P99 < 500ms. P50 < 100ms. Zero 503 errors at target concurrency.",$colNFR))
[void]$tc.Add(@("TCPA-TC-064","Performance - Inbound webhook acknowledged within 200ms","Performance / NFR","NFS-001 / SPEC-002","High","Automated","Positive","Application under normal load.","1. POST inbound webhook.`n2. Measure time to HTTP 200 response (before background processing).","HTTP 200 returned in < 200ms. Fire-and-forget ensures Cool Text timeout never reached.",$colNFR))

# END-TO-END
[void]$tc.Add(@("TCPA-TC-065","E2E GCMA - Work order SMS, STOP opt-out, subsequent message suppressed","End-to-End / GCMA","STORY-002/003/004/005 / TC-135946","Critical","Manual","Positive","GCMA UA environment. TCPA API deployed. Test phone with SMS capability.","1. Create GCMA work order requesting text notification.`n2. Customer receives text.`n3. Customer replies STOP.`n4. Verify TCPA DB: OPT-OUT record.`n5. Create new work order for same number.`n6. Verify text NOT received.","Step 4: CellNumberOptOutRecords has OPT-OUT entry. Step 6: SMS suppressed. Global opt-out confirmed across GCMA.",$colPos))
[void]$tc.Add(@("TCPA-TC-066","E2E ARM - Gas connection request, text opt-in, CANCEL opt-out confirmed","End-to-End / ARM","STORY-002/003/004/005 / TC-135947","Critical","Manual","Positive","ARM UA environment. TCPA API deployed. Test phone.","1. Submit ARM gas connection request with text updates enabled.`n2. Verify text received.`n3. Customer replies CANCEL.`n4. Verify TCPA DB: OPT-OUT.`n5. Attempt next notification.`n6. Verify suppression.","Opt-out recorded after CANCEL. ARM notification suppressed. ARM-TCPA integration confirmed end-to-end.",$colPos))
[void]$tc.Add(@("TCPA-TC-067","E2E KMI - Outbound suppressed for globally opted-out number","End-to-End / KMI","STORY-002 / SPEC-001","High","Manual","Positive","KMI integrated. Number opted out via any application.","1. Opt-out number via GCMA (or direct API).`n2. KMI sends SMS to same number.`n3. Check TCPA response.","KMI outbound SUPPRESSED. BlockedOutbound audit entry written. Global scope across KMI confirmed.",$colPos))
[void]$tc.Add(@("TCPA-TC-068","E2E VNG - Outbound suppressed for globally opted-out number","End-to-End / VNG","STORY-002 / SPEC-001","High","Manual","Positive","VNG integrated. Number opted out via GCMA.","1. Opt-out via GCMA.`n2. VNG sends outbound to same number.","VNG outbound SUPPRESSED. Confirms global opt-out scope across LDC boundaries.",$colPos))
[void]$tc.Add(@("TCPA-TC-069","E2E BizTalk - REST adapter calls outbound endpoint with valid payload","End-to-End / BizTalk","STORY-002 / RISK-001 / SPEC-001","Critical","Manual","Positive","BizTalk REST adapter configured for SIT. Valid API key in BizTalk config.","1. Trigger BizTalk orchestration that sends SMS.`n2. Monitor TCPA API request logs.`n3. Verify compliance gate result.","TCPA receives well-formed POST from BizTalk with X-API-Key. Compliance gate executes. Response returned to BizTalk orchestration.",$colPos))
[void]$tc.Add(@("TCPA-TC-070","E2E BizTalk - Outbound suppressed for opted-out number","End-to-End / BizTalk","STORY-002 / RISK-001","Critical","Manual","Negative","BizTalk REST adapter configured. Number opted out in TCPA DB.","1. Trigger BizTalk SMS orchestration for opted-out number.`n2. Verify Cool Text did NOT receive message.","Cool Text NOT called. BizTalk receives SUPPRESSED response. No delivery to opted-out customer.",$colNeg))

# DATABASE / SCHEMA
[void]$tc.Add(@("TCPA-TC-071","Database - ApplicationRegistrations table has correct columns","Database / Schema","SPEC-014 / TASK-001","Medium","Manual","Positive","TCPA database accessible via SSMS.","1. SELECT TOP 1 * FROM ApplicationRegistrations.","Table exists. Columns: Id, CoolTextAccountId, ApplicationName, CallbackUrl, IsActive, CreatedAt. CoolTextAccountId has unique index.",$colPos))
[void]$tc.Add(@("TCPA-TC-072","Database - CellNumberOptOutRecords table has correct columns","Database / Schema","SPEC-004 / TASK-001","Medium","Manual","Positive","TCPA database accessible.","1. SELECT TOP 1 * FROM CellNumberOptOutRecords.","Table exists. Columns: Id, CellPhoneNumber, Status, OptOutTimestamp, UpdatedTimestamp. CellPhoneNumber has unique index.",$colPos))
[void]$tc.Add(@("TCPA-TC-073","Database - AuditLogEntries table has correct columns","Database / Schema","SPEC-008 / TASK-001","Medium","Manual","Positive","TCPA database accessible.","1. SELECT TOP 1 * FROM AuditLogEntries.","Table exists. Columns: RecordId (PK), EventType, CellPhoneNumberLast4, ApplicationName, RequestedBy, EventTimestamp, Details.",$colPos))
[void]$tc.Add(@("TCPA-TC-074","Database - CCB registration has IsActive=false at deployment","Database / Schema","SPEC-014 BR-063 / RISK-003","Critical","Manual","Positive","Freshly deployed TCPA database with seed scripts applied.","1. SELECT IsActive FROM ApplicationRegistrations WHERE ApplicationName LIKE '%CCB%'.","IsActive = 0. CCB activation gate in place. Prevents unprotected CCB SMS before integration testing complete.",$colPos))

# Write rows
$row = 2
foreach ($t in $tc) {
    $bg = $t[10]
    for ($c = 1; $c -le 12; $c++) {
        $cell = $ws.Cells.Item($row, $c)
        $cell.Interior.Color = $bg
        $cell.WrapText = $true
        $cell.VerticalAlignment = -4160
    }
    SetCell $ws $row 1  $t[0]  $true  $false $bg
    SetCell $ws $row 2  $t[1]  $false $true  $bg
    SetCell $ws $row 3  $t[2]  $false $false $bg
    SetCell $ws $row 4  $t[3]  $false $false $bg
    SetCell $ws $row 5  $t[4]  $false $false $bg
    SetCell $ws $row 6  $t[5]  $false $false $bg
    SetCell $ws $row 7  $t[6]  $false $false $bg
    SetCell $ws $row 8  $t[7]  $false $true  $bg
    SetCell $ws $row 9  $t[8]  $false $true  $bg
    SetCell $ws $row 10 $t[9]  $false $true  $bg
    SetCell $ws $row 11 ""     $false $false $bg
    SetCell $ws $row 12 ""     $false $false $bg
    $ws.Rows.Item($row).RowHeight = 80
    $row++
}

$ws.Rows.Item(1).RowHeight = 30
$ws.Application.ActiveWindow.SplitRow = 1
$ws.Application.ActiveWindow.FreezePanes = $true
$ws.Range("A1").AutoFilter() | Out-Null

# ── COVERAGE SUMMARY SHEET ───────────────────────────────────────────────────
$sum = $wb.Sheets.Add()
$sum.Move([System.Reflection.Missing]::Value, $wb.Sheets.Item($wb.Sheets.Count))
$sum.Name = "Coverage Summary"
$sum.Tab.Color = 0xFF6600

SetCell $sum 1 1 "Coverage Summary - TCPA Test Plan" $true $false
$sum.Cells.Item(1,1).Font.Size = 14
$sum.Rows.Item(2).RowHeight = 8

$hd2 = @("Module / Area","Positive","Negative","Edge","Security","NFR / Perf","E2E / Manual","Total TCs")
for ($c = 1; $c -le $hd2.Count; $c++) {
    $cell = $sum.Cells.Item(3,$c)
    $cell.Value2 = $hd2[$c-1]
    $cell.Font.Bold = $true
    $cell.Interior.Color = 0x203864
    $cell.Font.Color = 0xFFFFFF
}
$dataRows = @(
  @("Application Registry",    2,1,2,0,0,0,5),
  @("Outbound Compliance Gate",3,5,1,2,0,0,11),
  @("Inbound SMS Proxy",       2,2,3,1,0,0,8),
  @("Opt-Out Detection",       3,2,2,0,0,0,7),
  @("Opt-Out Management",      2,1,1,0,0,0,4),
  @("Opt-Out Confirmation",    1,1,1,0,0,0,3),
  @("Admin / Re-Opt-In",       2,4,1,2,0,0,9),
  @("Audit Logging",           5,0,0,1,0,0,6),
  @("Compliance Reporting",    1,2,0,1,0,0,4),
  @("Health / Ops",            1,1,2,1,0,0,5),
  @("Security",                1,2,1,4,0,0,8),
  @("Performance / NFR",       2,0,0,0,2,0,4),
  @("End-to-End Integration",  4,1,0,0,0,6,11),
  @("Database / Schema",       4,0,0,0,0,0,4),
  @("TOTAL",                   33,22,14,12,2,6,89)
)
$r = 4
foreach ($dr in $dataRows) {
    $isBold = ($dr[0] -eq "TOTAL")
    for ($c = 1; $c -le $dr.Count; $c++) {
        $cell = $sum.Cells.Item($r,$c)
        $cell.Value2 = $dr[$c-1]
        $cell.Font.Bold = $isBold
        if ($isBold) { $cell.Interior.Color = 0xD9E1F2 }
    }
    $r++
}

SetCell $sum ($r+2) 1 "COLOUR LEGEND" $true
$sum.Cells.Item($r+3,1).Value2 = "Positive scenario"
$sum.Cells.Item($r+3,1).Interior.Color = $colPos
$sum.Cells.Item($r+4,1).Value2 = "Negative scenario"
$sum.Cells.Item($r+4,1).Interior.Color = $colNeg
$sum.Cells.Item($r+5,1).Value2 = "Edge / boundary scenario"
$sum.Cells.Item($r+5,1).Interior.Color = $colEdge
$sum.Cells.Item($r+6,1).Value2 = "Security scenario"
$sum.Cells.Item($r+6,1).Interior.Color = $colSec
$sum.Cells.Item($r+7,1).Value2 = "NFR / Performance"
$sum.Cells.Item($r+7,1).Interior.Color = $colNFR

for ($c = 1; $c -le 8; $c++) { $sum.Columns.Item($c).ColumnWidth = 22 }
$sum.Columns.Item(1).ColumnWidth = 30

# ── Save ─────────────────────────────────────────────────────────────────────
$wb.SaveAs($outPath, 51)
$wb.Close($false)
$excel.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($excel) | Out-Null
Write-Output "SUCCESS: $outPath"
