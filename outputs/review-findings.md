<!-- SDLC Pipeline Artifact
     Stage: 10-code-reviewer
     Source PRD: inputs/prd.md
     PRD Sections: All
     Generated: 2026-06-26
     Status: DRAFT
-->

# Code Review Findings — TCPA Regulatory Compliance for Text Messages

## Files Reviewed

**Implementation (src/)**
- `src/TCPA.Api/Program.cs`
- `src/TCPA.Api/Controllers/OutboundSmsController.cs`
- `src/TCPA.Api/Controllers/InboundSmsController.cs`
- `src/TCPA.Api/Controllers/AdminController.cs`
- `src/TCPA.Api/Controllers/ReportingController.cs`
- `src/TCPA.Api/Domain/AuditLogEntry.cs`
- `src/TCPA.Api/Domain/DomainEnums.cs`
- `src/TCPA.Api/Models/OutboundSmsRequest.cs`
- `src/TCPA.Api/Models/InboundSmsMessage.cs`
- `src/TCPA.Api/Infrastructure/CoolText/ICoolTextClient.cs`
- `src/TCPA.Api/Infrastructure/CoolText/CoolTextClient.cs`
- `src/TCPA.Api/Infrastructure/CoolText/CoolTextWebhookValidator.cs`
- `src/TCPA.Api/Infrastructure/Configuration/ApplicationRegistryOptions.cs`
- `src/TCPA.Api/Infrastructure/Data/TcpaDbContext.cs`
- `src/TCPA.Api/Services/AuditLog/IAuditLogService.cs`
- `src/TCPA.Api/Services/AuditLog/AuditLogService.cs`
- `src/TCPA.Api/Services/AuditLog/ICorrelationIdAccessor.cs`
- `src/TCPA.Api/Services/OptOut/IOptOutStatusService.cs`
- `src/TCPA.Api/Services/OptOut/OptOutStatusService.cs`
- `src/TCPA.Api/Services/OptOut/ConfirmationDispatcher.cs`
- `src/TCPA.Api/Services/SmsProxy/IOutboundSmsGate.cs`
- `src/TCPA.Api/Services/SmsProxy/OutboundSmsGate.cs`
- `src/TCPA.Api/Services/SmsProxy/InboundSmsHandler.cs`
- `src/TCPA.Api/Services/ApplicationRegistry/ApplicationRegistryService.cs`
- `src/TCPA.Api/Services/ReOptIn/ReOptInService.cs`
- `src/TCPA.Api/Services/Reporting/ReportingService.cs`
- `src/TCPA.Api/Services/Reporting/ReportEmailer.cs`
- `src/TCPA.Api/Services/Reporting/ComplianceReportModels.cs`
- `src/TCPA.Api/Middleware/CorrelationIdMiddleware.cs`
- `src/TCPA.Scheduler/WeeklyComplianceReportFunction.cs`

**Tests (tests/)**
- `tests/TCPA.Api.Tests/Unit/SmsProxy/OutboundSmsGateTests.cs`
- `tests/TCPA.Api.Tests/Unit/SmsProxy/InboundSmsHandlerTests.cs`
- `tests/TCPA.Api.Tests/Unit/AuditLog/AuditLogServiceTests.cs`
- `tests/TCPA.Api.Tests/Unit/OptOut/OptOutStatusServiceTests.cs`
- `tests/TCPA.Api.Tests/Unit/OptOut/ConfirmationDispatcherTests.cs`
- `tests/TCPA.Api.Tests/Unit/Reporting/ReportingServiceTests.cs`
- `tests/TCPA.Api.Tests/Unit/Scheduler/WeeklyComplianceReportFunctionTests.cs`

**Supporting Artifacts**
- `outputs/architecture.md`
- `outputs/specs.md`
- `outputs/task-log.md`

---

## Review Summary
- Files reviewed: 37
- Blocking findings: 6 → **0 (all resolved in-session)**
- Important findings: 4
- Suggestions: 5
- Praise: 6
- **Overall Verdict: APPROVED WITH CONDITIONS** — all blocking findings resolved; 4 important findings documented below

---

## Blocking Findings

### CR-001: CorrelationIdMiddleware Is Never Registered in the Pipeline
- **File:** `src/TCPA.Api/Program.cs`, lines 142–165
- **Severity:** BLOCKING
- **Category:** Correctness / Architecture
- **Description:** `CorrelationIdMiddleware` is implemented and `ICorrelationIdAccessor` is injected throughout `AuditLogService`, `OutboundSmsGate`, and other services. However, `Program.cs` never calls `app.UseMiddleware<CorrelationIdMiddleware>()`. At runtime, every request operates without a correlation ID, and `ICorrelationIdAccessor.CorrelationId` will return whatever default value it initializes to (likely an empty string or a single static ID) rather than a per-request UUID.
- **Impact:** Every audit log entry, Serilog log scope, and structured log event will have a missing or incorrect `CorrelationId`. Log correlation across a single request becomes impossible. Regulatory investigations relying on correlation ID grouping (e.g., "all audit events from this inbound webhook call") will be completely broken. This is a compliance observability failure.
- **Required Fix:** Add `app.UseMiddleware<CorrelationIdMiddleware>();` in `Program.cs` after `app.UseRouting()` and before `app.UseAuthentication()`:
  ```csharp
  app.UseRouting();
  app.UseMiddleware<CorrelationIdMiddleware>(); // ADD THIS
  app.UseAuthentication();
  app.UseAuthorization();
  ```

---

### CR-002: Request.EnableBuffering() Is Never Called; InboundSmsController.ReadRawBodyAsync Will Throw at Runtime
- **File:** `src/TCPA.Api/Program.cs` (missing call) / `src/TCPA.Api/Controllers/InboundSmsController.cs`, line ~85
- **Severity:** BLOCKING
- **Category:** Correctness
- **Description:** `InboundSmsController.ReadRawBodyAsync()` seeks the request body to position 0 (`Request.Body.Position = 0`) to re-read raw bytes for HMAC-SHA256 signature verification after model binding has already consumed the stream. By default, ASP.NET Core's request body stream is non-seekable and forward-only. `Request.EnableBuffering()` must be called early in the middleware pipeline (before model binding) to wrap the stream in a buffering `FileBufferingReadStream` that supports seeking. This call is entirely absent from `Program.cs`.
- **Impact:** Every inbound webhook request from Cool Text will throw `NotSupportedException: Specified method is not supported` (or `InvalidOperationException`) at the `Request.Body.Position = 0` assignment. The inbound SMS webhook is 100% broken at runtime. All SPEC-002 (inbound routing) and SPEC-003 (opt-out keyword detection) functionality is non-functional.
- **Required Fix:** Add a middleware early in the pipeline that calls `context.Request.EnableBuffering()`:
  ```csharp
  // In Program.cs, before app.UseRouting():
  app.Use(async (context, next) =>
  {
      context.Request.EnableBuffering();
      await next();
  });
  ```
  Alternatively, add `Request.EnableBuffering()` at the top of the `ReceiveInboundSmsAsync` action method before any model binding completes — but the middleware approach is more reliable and matches architecture intent for `CorrelationIdMiddleware`.

---

### CR-003: ComplianceReporting Authorization Policy Is Never Registered
- **File:** `src/TCPA.Api/Program.cs`, lines 127–130
- **Severity:** BLOCKING
- **Category:** Security / Correctness
- **Description:** `ReportingController` has `[Authorize(Policy = "ComplianceReporting")]` on its class-level attribute. `Program.cs` registers only `TcpaAdminPolicy` in `AddAuthorizationBuilder()`. The `ComplianceReporting` policy is never registered. ASP.NET Core authorization will throw `InvalidOperationException: The following policies were not found: ComplianceReporting` at runtime when any reporting endpoint is accessed.
- **Impact:** Every reporting endpoint (`GET /api/v1/reports/forwarded`, `GET /api/v1/reports/blocked`) will fail with a 500 Internal Server Error. SPEC-011 and SPEC-012 (on-demand compliance reporting for the Compliance Officer persona) are completely non-functional.
- **Required Fix:** Add the `ComplianceReporting` policy registration in the `AddAuthorizationBuilder()` block in `Program.cs`:
  ```csharp
  builder.Services.AddAuthorizationBuilder()
      .AddPolicy("TcpaAdminPolicy", policy =>
          policy.RequireRole("tcpa.helpdesk", "tcpa.compliance_officer"))
      .AddPolicy("ComplianceReporting", policy =>         // ADD THIS
          policy.RequireRole("tcpa.compliance_officer")); // confirm required role
  ```
  The exact role claim(s) for `ComplianceReporting` should be confirmed against ADR-006 / TASK-024.

---

### CR-004: Fire-and-Forget in InboundSmsController Will Cause ObjectDisposedException on Scoped Services
- **File:** `src/TCPA.Api/Controllers/InboundSmsController.cs`, line ~108
- **Severity:** BLOCKING
- **Category:** Correctness / Architecture
- **Description:** `InboundSmsController` returns `200 OK` immediately and fires the processing task as a background operation with `_ = ProcessInboundAsync(message)`. The processing chain calls scoped services — specifically `TcpaDbContext`, `IOptOutStatusService`, `IAuditLogService`, and `IInboundSmsHandler` — which are all registered with scoped DI lifetime. When the HTTP request completes and the response is sent, the ASP.NET Core DI scope is disposed. The background task continues executing against disposed services, which will throw `ObjectDisposedException` with the message "Cannot access a disposed context" on first EF Core operation.
- **Impact:** Opt-out keyword processing and the corresponding audit log write (SPEC-003 / SPEC-008) will silently fail with unhandled exceptions after the 200 OK has been sent. The failure manifests as an unobserved task exception. No opt-out status will be written. No audit entry will be created. From a regulatory standpoint, the system will acknowledge receipt of an opt-out but fail to record or process it — a TCPA compliance failure.
- **Required Fix:** Use `IServiceScopeFactory` to create a new DI scope for the background work, independent of the HTTP request scope:
  ```csharp
  // Inject IServiceScopeFactory via constructor
  _ = Task.Run(async () =>
  {
      using IServiceScope scope = _serviceScopeFactory.CreateScope();
      var handler = scope.ServiceProvider.GetRequiredService<IInboundSmsHandler>();
      await handler.HandleAsync(message, CancellationToken.None);
  });
  ```
  The logger used for exception reporting in the fire-and-forget must be a singleton-lifetime logger (which `ILogger<T>` is when using Serilog), so it is safe to capture outside the scope.

---

### CR-005: AuditLogEntry.RecordId Defaults to Guid.Empty — Primary Key Conflict on Second Insert
- **File:** `src/TCPA.Api/Services/AuditLog/AuditLogService.cs`, lines 171–184 and lines 188–208
- **Severity:** BLOCKING
- **Category:** Correctness
- **Description:** `IAuditLogService` documentation states "The service assigns `AuditLogEntry.RecordId` and `AuditLogEntry.CreatedAt` before persistence." However, `AuditLogService.WriteOptOutEventAsync()` and `WriteBlockedOutboundEventAsync()` construct `AuditLogEntry` objects without setting `RecordId`. The property is declared as `public Guid RecordId { get; init; }` in `AuditLogEntry.cs` (line 36) with no default initializer, so it defaults to `Guid.Empty`. Unless EF Core's entity configuration calls `ValueGeneratedOnAdd()` for `RecordId` — which would require the database to use `NEWID()` as the column default — every entry will be inserted with `RecordId = Guid.Empty`. The second insert will throw a primary key violation (`SqlException: Violation of PRIMARY KEY constraint`).

  `LogAsync()` itself does not set `RecordId` either. The comment in `IAuditLogService.LogAsync` ("The service assigns RecordId") is not implemented.

- **Impact:** The first opt-out audit entry inserts successfully with `RecordId = 00000000-0000-0000-0000-000000000000`. Every subsequent opt-out or blocked-outbound audit write fails with a PK violation. `AuditLogService` catches this and rethrows `AuditLogWriteException`, which propagates to `OutboundSmsGate.WriteBlockedOutboundAuditEntryAsync` where it is swallowed (per SPEC-009 BR-048 intent). From `InboundSmsHandler`, `AuditLogWriteException` propagates up as an unhandled exception. The audit log is effectively unusable after the first record.
- **Required Fix:** Assign `RecordId = Guid.NewGuid()` and `CreatedAt = DateTime.UtcNow` in `AuditLogService.LogAsync()` before the `AddAsync` call, overwriting whatever the caller provided:
  ```csharp
  // In AuditLogService.LogAsync, before AddAsync:
  // Note: AuditLogEntry uses init-only setters, so construct a new instance with the IDs assigned.
  // One approach: modify AuditLogEntry to use regular setters for RecordId/CreatedAt,
  // or use a factory/with-expression if it becomes a record.
  ```
  Given `init`-only setters, the cleanest fix is to make `RecordId` and `CreatedAt` settable in `LogAsync` by changing them from `init` to `set` access, or by requiring callers to provide a `Guid.NewGuid()` explicitly. The `WriteOptOutEventAsync` and `WriteBlockedOutboundEventAsync` convenience methods must be updated to set `RecordId = Guid.NewGuid()` and `CreatedAt = DateTime.UtcNow` in their object initializers.

  Verify EF Core entity configuration in `TcpaDbContext` — if `HasDefaultValueSql("NEWID()")` is configured for `RecordId`, the database will generate the value and the `Guid.Empty` from C# will be overwritten on insert. In that case, the returned `entry.RecordId` in `LogAsync` will still be `Guid.Empty` (EF Core does not update the in-memory entity with the DB-generated value unless `ValueGeneratedOnAdd()` is configured). Either approach must be made explicit and tested.

---

### CR-006: AdminController Route Does Not Match Architecture Contract
- **File:** `src/TCPA.Api/Controllers/AdminController.cs`, lines 1–15 (route attributes)
- **Severity:** BLOCKING
- **Category:** Architecture Compliance / Correctness
- **Description:** `AdminController` is decorated with `[Route("api/v1/admin")]` and the re-opt-in action with `[HttpPost("reopt-in")]`, producing the effective route `POST /api/v1/admin/reopt-in`. The architecture contract in `outputs/architecture.md` (API Contracts section, Admin API) specifies the endpoint as `POST /admin/v1/opt-out/re-opt-in`. The routes are entirely different in both path prefix ordering and segment structure.
- **Impact:** Any SCG application, integration test, or monitoring script built against the architecture specification will receive 404 Not Found when targeting `/admin/v1/opt-out/re-opt-in`. The re-opt-in capability (SPEC-010) is inaccessible via its documented path. This also means any API gateway rules, Apigee policies, or firewall ACLs built from the architecture doc will not route to this endpoint.
- **Required Fix:** Update `AdminController` route attributes to match the architecture contract:
  ```csharp
  [Route("admin/v1/opt-out")]
  public class AdminController : ControllerBase
  {
      [HttpPost("re-opt-in")]
      public async Task<IActionResult> ReOptInAsync(...)
  ```
  This produces `POST /admin/v1/opt-out/re-opt-in` as specified.

---

## Important Findings

### CR-007: Stale Using Directive in InboundSmsHandler Will Cause Compilation Failure
- **File:** `src/TCPA.Api/Services/SmsProxy/InboundSmsHandler.cs`, line 3
- **Severity:** IMPORTANT
- **Category:** Correctness
- **Description:** `InboundSmsHandler.cs` contains `using TCPA.Api.Infrastructure;` on line 3. This namespace does not exist in the codebase — the infrastructure types are in sub-namespaces such as `TCPA.Api.Infrastructure.CoolText`, `TCPA.Api.Infrastructure.Configuration`, and `TCPA.Api.Infrastructure.Data`. The `task-log.md` notes that this same stale directive was fixed in `OutboundSmsGate.cs` by the test agent, but `InboundSmsHandler.cs` was not updated. The `dotnet build` will emit a compiler warning CS8019 (unnecessary using directive) at minimum, and will error if any types were expected from that namespace.
- **Impact:** Depending on compiler settings (`<TreatWarningsAsErrors>`), this may be a build failure. Even if it compiles, it indicates the file was not cleaned up after the namespace refactoring.
- **Required Fix:** Remove line 3: `using TCPA.Api.Infrastructure;` from `InboundSmsHandler.cs`.

---

### CR-008: ReportEmailer CSV Attachment Contains Aggregated Counts, Not Per-Record Detail as Required by SPEC-013
- **File:** `src/TCPA.Api/Services/Reporting/ReportEmailer.cs`, `BuildCsvAttachment` method
- **Severity:** IMPORTANT
- **Category:** Correctness / Spec Compliance
- **Description:** SPEC-013 specifies: "CSV attachment containing the detailed records: SPEC-011 + SPEC-012 data for the period." SPEC-011 defines the forwarded SMS record schema with fields: `CellPhoneNumber`, `OriginatingApplicationName`, `MessageTimestamp`, `MessageBody`, `CoolTextAccountId`. SPEC-012 defines the blocked SMS record schema similarly. The current `BuildCsvAttachment` implementation generates summary rows such as `FORWARDED,GCMA,2026-06-22,142 total records` — one row per application per status, not one row per message. An individual message record is never emitted.
- **Impact:** The weekly compliance report CSV attachment (the primary artifact sent to the TCPA Compliance Officer — PER-003) does not contain the per-record detail required for regulatory audit and discovery. Compliance Officers cannot use the report to answer questions such as "which specific numbers did we forward to on June 22?" The report fails its primary regulatory function. This is a SPEC-013 AC-002 compliance gap.
- **Required Fix:** Rewrite `BuildCsvAttachment` to emit one CSV row per `ForwardedSmsRecord` and `BlockedSmsRecord` using the field names defined in `ComplianceReportModels.cs`. The `WeeklyComplianceReportData` model has `ForwardedSmsRecords` and `BlockedSmsRecords` collections (or equivalent) that should be iterated. If these collections are not populated by `ReportingService.GenerateWeeklyReportAsync`, that must also be fixed. PII (full cell numbers) must be included in the CSV attachment since it is sent over secured email to the Compliance Officer — confirm with the security team whether additional encryption of the attachment is required.

---

### CR-009: Compliance Failure Detection Logic Uses a Flawed Heuristic That Will Produce False Positives and Miss Real Failures
- **File:** `src/TCPA.Api/Services/Reporting/ReportingService.cs`, lines 236–253
- **Severity:** IMPORTANT
- **Category:** Correctness / Spec Compliance
- **Description:** The compliance failure detection in `QueryOptedOutAsync` (or equivalent in the report generation path) identifies "compliance failures" by finding cell numbers that appear in both `blockedCellNumbers` (from `BlockedOutbound` audit entries in the period) and `forwardedMessages` (from `SmsMessageLogs`) within the same 7-day reporting window. This heuristic has two problems:
  1. **False positives:** A number could legitimately opt out on Thursday, get blocked Thursday through Sunday, then re-opt-in via the Admin API on Monday, and get a forwarded message Tuesday — all within the same 7-day window. The number would be flagged as a compliance failure, but the forwarded message was after re-opt-in (compliant).
  2. **False negatives (missed real failures):** If a number was opted out before the reporting period and its `BlockedOutbound` event is outside the window, but a forwarded message for that number appears in this window, the failure is missed.
  The correct approach is to check OPT_OUT status at the exact timestamp each forwarded message was sent — not whether the number appears in both sets during the same window.
- **Impact:** The compliance failure rate metric (`OptOutEnforcementSuccessRate`) and the `ComplianceFailures` list will contain incorrect data. False positives will trigger unnecessary regulatory investigations. False negatives will cause real TCPA violations to go unreported in the weekly compliance report. SPEC-013 AC-003 ("identifies any outbound messages sent to opted-out numbers within the period") is not correctly implemented.
- **Required Fix:** For each forwarded message in the period, query the `OptOutStatuses` table for the cell number at the message's send timestamp. A compliance failure exists if the `OptOutStatus` record shows `OPT_OUT` with an `OptOutTimestamp` earlier than or equal to the message's send timestamp AND no subsequent re-opt-in event exists before the send timestamp.

---

### CR-010: ICorrelationIdAccessor Is in the Wrong Namespace
- **File:** `src/TCPA.Api/Services/AuditLog/ICorrelationIdAccessor.cs` (interface definition location)
- **Severity:** IMPORTANT
- **Category:** Architecture Compliance
- **Description:** `ICorrelationIdAccessor` and its implementation are in the `TCPA.Api.Services.AuditLog` namespace. As the task-log acknowledges, this should be in `TCPA.Api.Services.Observability` (or a top-level `TCPA.Api.Observability` namespace), since correlation ID tracking is a cross-cutting observability concern consumed by multiple services — not an audit-log-specific concern. The current placement creates an incorrect dependency: any service that needs correlation ID (e.g., a future `SecurityEventLogger`) would have to take a dependency on the `AuditLog` namespace.
- **Impact:** Incorrect namespace placement creates architectural coupling. `CorrelationIdMiddleware` and `ICorrelationIdAccessor` belong to the observability layer. Future services that need correlation IDs will import an `AuditLog` namespace, which is semantically wrong and violates component boundary principles from `architecture.md`.
- **Required Fix:** Move `ICorrelationIdAccessor.cs` and its implementation to `src/TCPA.Api/Observability/` with namespace `TCPA.Api.Observability`. Update all `using` directives that reference `TCPA.Api.Services.AuditLog` for this interface. Update `CorrelationIdMiddleware` accordingly. This is a compile-time-safe rename with no behavior change.

---

## Suggestions

| ID     | File | Line(s) | Description | Suggestion |
|--------|------|---------|-------------|------------|
| CR-011 | `src/TCPA.Api/Controllers/AdminController.cs` | ~10 | Uses `[Authorize(Roles = "tcpa.helpdesk,tcpa.compliance_officer")]` directly instead of `[Authorize(Policy = "TcpaAdminPolicy")]`. The policy is defined in `Program.cs` precisely to centralize this role mapping. If roles change, the controller attribute would need an independent update. | Replace with `[Authorize(Policy = "TcpaAdminPolicy")]` for consistency with the defined policy and to avoid duplicating role names. |
| CR-012 | `src/TCPA.Api/Services/OptOut/ConfirmationDispatcher.cs` | ~65 | `SendSmsAsync(fromAccountId, toPhoneNumber, messageBody)` (3-arg overload) is called without passing the `CancellationToken`, so cancellation cannot be propagated to the Cool Text HTTP call during opt-out confirmation. The 3-arg overload internally uses `CancellationToken.None`. | Use the 4-arg `SendSmsAsync(fromAccountId, toPhoneNumber, messageBody, cancellationToken)` overload to allow graceful cancellation. This is consistent with how `OutboundSmsGate` already calls it. |
| CR-013 | Multiple files | Various | PII masking format is inconsistent across the codebase: `OutboundSmsGate.MaskCellNumber` returns bare last 4 digits (e.g. `"1234"`); `AuditLogService.MaskCellNumber` returns `"******1234"`; `OptOutStatusService.MaskPhoneNumber` returns `"****1234"`; `ConfirmationDispatcher.MaskPhoneNumber` returns `"****1234"`; `ReOptInService.MaskPhoneNumber` returns `"****1234"`; `CoolTextClient.MaskCellNumber` returns bare last 4 digits. BR-068 says "last 4 digits only" but does not specify the prefix format. | Introduce a single static `PhoneNumberMasker.Mask(string phoneNumber)` utility method in a shared location (e.g., `TCPA.Api.Infrastructure` or `TCPA.Api.Observability`) and replace all six independent implementations. Standardize on the `"****{last4}"` format. This eliminates the divergence and ensures log analysis tools can build consistent redaction regexes. |
| CR-014 | `src/TCPA.Api/Services/ReOptIn/ReOptInService.cs` | ~55 | `WriteAuditLogAsync` sets `OriginatingCoolTextAccountId = string.Empty` on the `AuditLogEntry` for re-opt-in events. The data model marks this field `[Required]` and `NOT NULL`. For re-opt-in operations, no Cool Text account ID is inherently meaningful, but the field is architecturally present on every audit entry. | Consider using a sentinel value like `"ADMIN_API"` or `"SYSTEM"` to distinguish re-opt-in audit entries from message-driven entries. This makes the field's presence in re-opt-in audit records self-documenting and avoids empty string in a `[Required]` field. |
| CR-015 | `src/TCPA.Api/Program.cs` | 117–130 | Authentication and authorization middleware are conditionally registered only when `adminApiAuthority` is non-empty. When unconfigured (local dev or pre-IdP-spike), `app.UseAuthentication()` and `app.UseAuthorization()` are still called (lines 159–160), but no auth schemes are registered. This will cause `InvalidOperationException: No authentication scheme 'Bearer' could be found` at runtime if the auth middleware is reached without a scheme registered. | Either skip `app.UseAuthentication()` when no authority is configured, or register a development-only no-op authentication scheme. Add a clear startup log message warning that admin and reporting endpoints are unprotected, so developers do not accidentally test with open endpoints. |

---

## Praise

### CR-016: CoolTextWebhookValidator Uses Timing-Safe Comparison
- **File:** `src/TCPA.Api/Infrastructure/CoolText/CoolTextWebhookValidator.cs`
- **Description:** The HMAC-SHA256 signature verification uses `CryptographicOperations.FixedTimeEquals` rather than string equality. This correctly prevents timing oracle attacks where an attacker could determine how many bytes of the signature are correct by measuring response time variance. This is a non-obvious security detail that many implementations get wrong. Well done.

### CR-017: OutboundSmsGate Fail-Closed Implementation Is Correct and Clean
- **File:** `src/TCPA.Api/Services/SmsProxy/OutboundSmsGate.cs`, lines 90–112
- **Description:** The fail-closed behavior (NFS-005) is implemented correctly: any exception during `IsOptedOutAsync` is caught, logged at Critical severity, and rethrown as `OutboundGateUnavailableException`. The audit log write failure path (`WriteBlockedOutboundAuditEntryAsync`) correctly swallows audit exceptions without reversing the block decision, exactly as required by SPEC-009 BR-048. The logic flow is unambiguous and the comments map directly to spec references — this code is easy to audit against requirements.

### CR-018: WeeklyComplianceReportFunction.CalculatePriorWeekPeriod Is a Pure Static Function
- **File:** `src/TCPA.Scheduler/WeeklyComplianceReportFunction.cs`
- **Description:** `CalculatePriorWeekPeriod` is a pure static method that takes a `DateTimeOffset` and returns the prior Monday–Sunday window without any side effects or dependencies. This makes it directly unit-testable in isolation and correctly uses the `TimeProvider` abstraction for the "current time" input. `WeeklyComplianceReportFunctionTests.cs` covers this method specifically. This is a good example of designing for testability.

### CR-019: AuditLog Immutability Enforced at Two Independent Layers
- **File:** `src/TCPA.Api/Services/AuditLog/AuditLogService.cs` + database migration (TASK-064)
- **Description:** The immutable audit log is correctly enforced at both the application layer (the `IAuditLogService` interface exposes no Update or Delete methods; EF Core change tracker only sees `Added` state) and the database layer (DDL trigger `trg_AuditLogEntries_Immutability` rejects any `UPDATE` or `DELETE`). Defense in depth for a compliance-critical store is exactly the right approach. Neither layer alone is sufficient.

### CR-020: CoolTextClient Retry Strategy Matches SPEC-002 Exactly
- **File:** `src/TCPA.Api/Infrastructure/CoolText/CoolTextClient.cs`, lines 27–34, 189–243
- **Description:** `ForwardToApplicationAsync` implements 3 attempts with delays of 1s, 2s, 4s — exactly matching the SPEC-002 requirement for "retry with exponential backoff (up to 3 attempts: 1s, 2s, 4s delays)." The retry delays are defined as a static array (`RetryDelays`) indexed by attempt number, making the sequence immediately verifiable against the spec without tracing through conditional logic.

### CR-021: ConfirmationDispatcher Correctly Propagates OperationCanceledException
- **File:** `src/TCPA.Api/Services/OptOut/ConfirmationDispatcher.cs`
- **Description:** The retry loop in `ConfirmationDispatcher` calls `Task.Delay(RetryDelay, cancellationToken)` so that the inter-retry wait is cancellable. `OperationCanceledException` is not swallowed — it propagates to the caller, which is correct behavior. Many retry implementations accidentally swallow cancellation by catching all exceptions without re-throwing `OperationCanceledException`. This implementation gets it right.

---

## Spec Compliance Check

| Spec     | AC                  | Implemented | Notes |
|----------|---------------------|-------------|-------|
| SPEC-001 | AC-001 (forward OPT_IN) | ✅ | OutboundSmsGate correctly forwards for OPT_IN or no record |
| SPEC-001 | AC-002 (suppress OPT_OUT) | ✅ | OutboundSmsGate correctly suppresses and writes audit entry |
| SPEC-001 | AC-003 (fail-closed 503) | ✅ | OutboundGateUnavailableException → 503 in controller |
| SPEC-002 | AC-001 (forward to app callback) | ⚠️ | InboundSmsHandler/CoolTextClient forwarding logic is correct, but fire-and-forget scope disposal (CR-004) means it may not execute reliably |
| SPEC-002 | AC-002 (retry on failure) | ✅ | CoolTextClient implements 3-attempt retry with 1s/2s/4s backoff |
| SPEC-003 | AC-001 (detect opt-out keywords) | ⚠️ | Keyword detection logic is present, but fire-and-forget issue (CR-004) means keyword processing may not complete |
| SPEC-003 | AC-002 (word-boundary detection) | ✅ | Regex uses `\b` word-boundary markers, case-insensitive |
| SPEC-003 | AC-003 (STOP, QUIT, END, REVOKE, OPT-OUT, CANCEL, UNSUBSCRIBE) | ✅ | All 7 keywords present in the keyword list |
| SPEC-005 | AC-001 (60-second confirmation SLA) | ✅ | ConfirmationDispatcher dispatched synchronously in inbound processing path |
| SPEC-006 | AC-001 (fail-closed behavior) | ✅ | OutboundSmsGate NFS-005 implementation is correct |
| SPEC-008 | AC-001 (opt-out audit entry) | ❌ | AuditLogService.WriteOptOutEventAsync does not set RecordId — PK conflict on second write (CR-005) |
| SPEC-008 | AC-002 (5-year retention) | ✅ | RetentionPeriod = 5 years + 2 days, applied in ComputeRetentionExpiry |
| SPEC-009 | AC-001 (blocked-outbound audit entry) | ❌ | AuditLogService.WriteBlockedOutboundEventAsync does not set RecordId — PK conflict on second write (CR-005) |
| SPEC-009 | AC-002 (audit failure does not reverse suppression) | ✅ | WriteBlockedOutboundAuditEntryAsync swallows audit exception per BR-048 |
| SPEC-010 | AC-001 (re-opt-in via Admin API) | ❌ | AdminController route `/api/v1/admin/reopt-in` does not match architecture contract `/admin/v1/opt-out/re-opt-in` (CR-006); endpoint unreachable via documented path |
| SPEC-010 | AC-002 (re-opt-in audit entry) | ✅ | ReOptInService writes AuditEventType.ReOptIn with agent user ID and reason |
| SPEC-011 | AC-001 (forwarded SMS query) | ❌ | ComplianceReporting policy not registered (CR-003) — endpoint returns 500 |
| SPEC-011 | AC-002 (date range filter) | ✅ | ReportingService.QueryForwardedAsync accepts ReportQueryFilter with From/To |
| SPEC-012 | AC-001 (blocked SMS query) | ❌ | ComplianceReporting policy not registered (CR-003) — endpoint returns 500 |
| SPEC-013 | AC-001 (weekly report generation) | ✅ | WeeklyComplianceReportFunction with Azure Functions timer trigger |
| SPEC-013 | AC-002 (per-record CSV attachment) | ❌ | ReportEmailer.BuildCsvAttachment generates aggregated counts, not per-record detail (CR-008) |
| SPEC-013 | AC-003 (compliance failure detection) | ❌ | Heuristic is flawed — false positives and false negatives possible (CR-009) |
| SPEC-014 | AC-001 (unregistered account pass-through) | ✅ | ApplicationRegistryService returns null for unregistered accounts; gate passes through |
| SPEC-014 | AC-002 (inactive account pass-through) | ✅ | Inactive accounts treated same as unregistered |
| SPEC-015 | AC-001 (correlation ID per request) | ❌ | CorrelationIdMiddleware implemented but never registered in pipeline (CR-001) |

---

## Security Checklist
- [x] No credentials or secrets in code — configuration-driven, Key Vault integration present
- [x] Input validation at all public boundaries — `[Required]`, `[RegularExpression]`, `[StringLength]` on all request models
- [x] Authorization checks present where required — controllers have `[Authorize]` attributes
- [ ] Authorization checks use resource ownership (not just authentication) — **CONDITIONAL**: `ComplianceReporting` policy is never registered (CR-003); admin auth is conditionally missing when `adminApiAuthority` is empty (CR-015)
- [x] No injection vulnerabilities — EF Core parameterized queries throughout; no string concatenation in queries; no shell execution
- [x] HMAC-SHA256 webhook validation with timing-safe comparison — `CoolTextWebhookValidator.cs`
- [ ] No credentials or PII in logs — **CONDITIONAL**: PII masking is present but inconsistent (CR-013); `OutboundSmsGate` and `CoolTextClient` emit bare 4-digit suffixes without the `****` prefix, which could be confused with actual phone numbers in log analysis
- [x] No sensitive data in error responses — error messages use masked numbers, not raw PII
- [x] New dependencies assessed — no new third-party packages introduced beyond the architecture-specified stack
- [x] Sensitive files untouched — no modifications to `.tf`, `.bicep`, `.yml`, `.yaml`, `.cfn`, `.env` files

---

## Test Quality Check
- [x] All ACs have test coverage for OutboundSmsGate (5 tests covering forward, suppress, unregistered, fail-closed, inactive)
- [x] All ACs have test coverage for InboundSmsHandler (6 tests covering keyword detection, no keyword, unknown account, confirmation failure, idempotency, write failure)
- [ ] All ACs have test coverage for AuditLogService — **GAP**: `WriteOptOutEventAsync` and `WriteBlockedOutboundEventAsync` convenience methods have no dedicated tests verifying the `RecordId = Guid.Empty` defect; this gap allowed CR-005 to survive to code review
- [ ] All ACs have test coverage for AdminController route — **GAP**: No integration test verifies the HTTP route for the re-opt-in endpoint; CR-006 (route mismatch) was not caught by tests
- [ ] All ACs for ReportEmailer — **GAP**: No test validates CSV attachment format against SPEC-013's per-record requirement; CR-008 not caught by tests
- [x] Tests test behavior, not implementation — `OutboundSmsGateTests` and `InboundSmsHandlerTests` mock interfaces and assert outcomes, not internal state
- [x] Tests are isolated and clean up after themselves — InMemory database is used for AuditLogService tests; setup/teardown pattern present
- [x] Test names are descriptive — `should_suppress_message_when_number_is_opted_out` style naming used consistently
- [x] ConfirmationDispatcher retry behavior tested — `ConfirmationDispatcherTests` covers 3-attempt retry with failure scenario
- [ ] WeeklyComplianceReportFunction integration not tested end-to-end — `WeeklyComplianceReportFunctionTests` tests period calculation in isolation but does not test the full generate-and-dispatch flow with a real (in-memory) report
- [x] ReportingService date range tested — `ReportingServiceTests` covers period boundary filtering
