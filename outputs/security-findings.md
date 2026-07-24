<!-- SDLC Pipeline Artifact
     Stage: 11-security-agent
     Source PRD: inputs/prd.md
     PRD Sections: All — NFRs, SPEC-001 through SPEC-017, risks.md
     Generated: 2026-07-23
     Status: DRAFT
-->

# Security Review Findings — TCPA Regulatory Compliance API

**Reviewer:** Robin — Agent 11 (Security Engineer)
**Scope:** TCPA.Api, TCPA.MessageProcessor, TCPA.OutboundDispatcher, TCPA.Core, TCPA.ReportService, TCPA.Scheduler

---

## Summary

| Severity | Count |
|---|---|
| Security-Blocking | 1 |
| High | 2 |
| Medium | 3 |
| Low | 1 |
| **Total** | **7** |

**Overall Security Verdict: PASS WITH CONDITIONS**

> SEC-001 (blocking), SEC-002 (high), SEC-003 (high) resolved. SEC-004 through SEC-006 deferred to next sprint. SEC-007 is an infrastructure concern.

One Security-Blocking finding must be resolved before any PR approval. The blocking finding is not a theoretical vulnerability — it is a runtime compliance failure that would prevent TCPA opt-out records from being written to a production SQL Server database. Every opt-out request processed in production would silently discard the customer's STOP instruction, leaving them opted-in and continuing to receive messages.

---

## Sensitive File Integrity

All restricted file types verified clean. No `.tf`, `.bicep`, `.yml`, `.yaml`, `.cfn`, or `.env` files were modified in this pipeline run.

**Verdict: PASS**

---

## Security-Blocking Findings

### SEC-001: AuditLog.PhoneNumber stores 64-character HMAC hash in a nvarchar(20) column — opt-out records silently discarded at runtime

- **Severity:** SECURITY-BLOCKING
- **CWE:** CWE-681 — Incorrect Conversion Between Numeric Types (data truncation); secondary TCPA compliance failure
- **Files:**
  - `src/TCPA.MessageProcessor/Services/OptOutProcessingService.cs`, line 49
  - `src/TCPA.MessageProcessor/Services/ConfirmationDispatchService.cs`, `WriteAuditAsync` method, line 176
  - `src/TCPA.OutboundDispatcher/Services/OutboundGateService.cs`, `WriteSuppressedAuditAsync` method, line 99
  - `src/TCPA.OutboundDispatcher/Services/OutboundSendService.cs`, lines 102 and 130
  - `src/TCPA.Core/Models/Configurations/AuditLogConfiguration.cs`, line 14
  - `src/TCPA.Core/Migrations/20260723200741_CreateAuditLog.cs`, line 74

**Description:**

The `AuditLog.PhoneNumber` database column is defined as `nvarchar(20)` in both the entity configuration (`HasMaxLength(20)`) and the SQL Server migration (`type: "nvarchar(20)"`). This column size is correct for E.164 phone numbers (maximum 16 characters including the `+` prefix and country code). The spec (SPEC-010) also defines the `phoneNumber` field type as E.164.

`IPhoneNumberHasher.Hash()` returns an HMAC-SHA256 hex digest, which is always 64 characters (`Convert.ToHexString(hashBytes)` on a 32-byte SHA256 output). Four services populate `AuditLog.PhoneNumber` with `_hasher.Hash(phoneNumber)`:

```csharp
// OptOutProcessingService.cs:49
PhoneNumber = _hasher.Hash(@event.From),    // 64 chars into nvarchar(20)

// ConfirmationDispatchService.cs:176 (WriteAuditAsync)
PhoneNumber = _hasher.Hash(phoneNumber),    // 64 chars into nvarchar(20)

// OutboundGateService.cs:99 (WriteSuppressedAuditAsync)
PhoneNumber = _hasher.Hash(@event.ToNumber), // 64 chars into nvarchar(20)

// OutboundSendService.cs:102 and 130
PhoneNumber = phoneHash,                    // phoneHash = _hasher.Hash(...), 64 chars into nvarchar(20)
```

When `SaveChangesAsync` executes against SQL Server, the engine raises "String or binary data would be truncated" (SQL error 8152). EF Core wraps this as a `DbUpdateException`, which propagates out of the `try` block in each service. The transaction rolls back. In `OptOutProcessingService`, this means:

1. The `AuditLog` entry for the STOP event is NOT written.
2. The `OptOutStatus` record for the phone number is NOT written — `UpsertOptOutAsync` never executes because it follows `SaveChangesAsync` in the same try block.
3. The customer's phone number remains in the opted-in state in the database.
4. `InboundMessageWorker` retries once, fails again identically, logs Critical, and commits the Kafka offset — permanently discarding the opt-out request.

This bug does not manifest in unit tests because EF Core's InMemory provider does not enforce column length constraints. Integration tests with Testcontainers (SQL Server) are currently skipped due to Docker unavailability in this environment. The defect is undetected until production deployment against a real SQL Server.

`ReOptInService.cs` (line 51) is the only service that correctly writes the raw phone number (`PhoneNumber = phoneNumber`), which is why the admin re-opt-in path works correctly.

**Attack Scenario:**

Any customer texting "STOP" to a registered Cool Text account triggers the inbound path. The webhook queues it to Kafka. `InboundMessageWorker` processes it and calls `OptOutProcessingService.ProcessOptOutAsync`. The audit write fails with a SQL truncation error. The transaction rolls back — no opt-out record written, no audit record written. The customer's STOP is silently discarded. Future outbound messages to that number find no opted-out status and are dispatched. This is a systematic TCPA §227 violation that applies to every opt-out request the production system handles — 100% of inbound STOP events are affected.

**Required Fix:**

Change all four callers to store the raw E.164 phone number in `AuditLog.PhoneNumber`:

```csharp
// OptOutProcessingService.cs:49
PhoneNumber = @event.From,          // raw E.164

// ConfirmationDispatchService WriteAuditAsync parameter
PhoneNumber = phoneNumber,           // raw E.164

// OutboundGateService WriteSuppressedAuditAsync
PhoneNumber = @event.ToNumber,       // raw E.164

// OutboundSendService both audit writes
PhoneNumber = @event.ToNumber,       // raw E.164 — remove phoneHash variable from audit path
```

The `CONTEXT.md` specification correctly states that phone number hashing applies to **Serilog log calls** and **`AuditLog.Details` JSON** — not to the `AuditLog.PhoneNumber` column. All Serilog calls in the reviewed code already hash correctly and require no changes. No Details JSON payload in any service contains a phone number, which is also correct.

**Verification:**

After fixing, run integration tests against SQL Server. Confirm that:

1. A STOP keyword message produces a row in `AuditLog` with a raw E.164 phone number in `PhoneNumber` (≤16 chars, fitting the 20-char column).
2. A row in `OptOutStatus` exists for that number with `Status = "opted-out"`.
3. A subsequent outbound message to that number is suppressed by `OutboundGateService`.

- **Status:** Resolved — All four callers now write raw E.164 to `AuditLog.PhoneNumber`. Serilog parameters and `AuditLog.Details` JSON continue to use the hash. Test assertions updated. 87/87 tests pass.

---

## High Findings

### SEC-002: Placeholder API keys committed to source control are functional credential values

- **File:** `src/TCPA.Api/appsettings.json`, lines 27–29 and 38
- **Severity:** SECURITY-HIGH
- **CWE:** CWE-798 — Use of Hard-Coded Credentials

**Description:**

`appsettings.json` (checked into source control) contains:

```json
"ApiKeys": {
  "ValidKeys": "dev-api-key-replace-in-prod",
  "AdminKeys": "dev-admin-key-replace-in-prod"
},
"Logging": {
  "PhoneHashKey": "replace-with-32-char-minimum-key-in-prod!"
}
```

These are not empty placeholders — they are string values that `ApiKeyAuthFilter` and `AdminApiKeyAuthFilter` load directly into `HashSet<string>` and use for key validation. If production deployment does not override them via environment variables or a secrets manager, any caller who has read the repository can:

1. Submit valid inbound webhooks using `dev-api-key-replace-in-prod` as `X-Api-Key`.
2. Call the admin re-opt-in endpoint using `dev-admin-key-replace-in-prod`.
3. Predict the HMAC seed: `PhoneHashKey = "replace-with-32-char-minimum-key-in-prod!"` means the "anonymised" phone hash in logs is reversible — an attacker can compute `HMAC-SHA256(seed, "+14045551234")` for any E.164 number and match against log output.

**Recommended Fix:**

1. Replace the default values in `appsettings.json` with empty strings. Add startup-time fail-fast guards:

```csharp
// ApiKeyAuthFilter constructor
if (_validKeys.Count == 0)
    throw new InvalidOperationException(
        "ApiKeys:ValidKeys is not configured. Set via environment variable ApiKeys__ValidKeys.");
```

2. Require overrides via environment variables (`ApiKeys__ValidKeys`, `ApiKeys__AdminKeys`, `Logging__PhoneHashKey`) or Azure Key Vault / Windows DPAPI-protected config.

3. Rotate all three values immediately if this repository has had any external exposure.

- **Status:** Resolved — All three functional placeholder values replaced with `REPLACE_IN_ENV` in `src/TCPA.Api/appsettings.json`. Integration test `BuildClient` default updated from `dev-api-key-replace-in-prod` to `REPLACE_IN_ENV`. `TCPA.MessageProcessor/appsettings.json` and `TCPA.OutboundDispatcher/appsettings.json` were already clean.

---

### SEC-003: HTTPS redirect not enforced in TCPA.Api — API keys and phone numbers may transit in cleartext

- **File:** `src/TCPA.Api/Program.cs`
- **Severity:** SECURITY-HIGH
- **CWE:** CWE-319 — Cleartext Transmission of Sensitive Information

**Description:**

`Program.cs` does not call `app.UseHttpsRedirection()`. ASP.NET Core does not add this automatically. As a result, HTTP connections to the API are accepted without redirect. In environments where TLS is NOT terminated upstream at a load balancer or API gateway:

- `X-Api-Key` header values transit in cleartext.
- Inbound webhook payloads (including `From` phone numbers, raw SMS body content) are unencrypted.
- Outbound message request bodies containing destination phone numbers (`ToNumber`) are unencrypted.

risks.md security checklist row "HTTPS enforced on all endpoints" is checked as satisfied, but that is an architectural assertion, not an application-layer enforcement. The application itself does not enforce it.

**Recommended Fix:**

Add `app.UseHttpsRedirection()` before `app.UseRateLimiter()`:

```csharp
app.UseSerilogRequestLogging();
app.UseHttpsRedirection();    // add this line
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
```

If infrastructure-layer TLS termination is the authoritative enforcement mechanism (valid in containerised/OpenShift environments), document this explicitly in `operations.md` with an ADR entry explaining why application-layer redirect is omitted. Without that documentation, a future deployment to a different infrastructure will silently accept HTTP.

- **Status:** Resolved — `app.UseHttpsRedirection()` added to `Program.cs` immediately after `app.UseSerilogRequestLogging()`, before `app.UseRateLimiter()`.

---

## Medium Findings

### SEC-004: SSRF — database-stored callback URL used without domain validation

- **File:** `src/TCPA.MessageProcessor/Services/ReplyForwardingService.cs`, line 28
- **Severity:** SECURITY-MEDIUM
- **CWE:** CWE-918 — Server-Side Request Forgery (SSRF)

**Description:**

`ReplyForwardingService.ForwardReplyAsync` issues an HTTP POST to `callbackUrl` with no domain allowlist validation:

```csharp
await _httpClient.PostAsync(callbackUrl, content, ct);
```

`callbackUrl` comes from `CoolTextAccount.CallbackUrl` in the database. If a database record is tampered with (SQL injection elsewhere, insider threat, or a future unsecured account management API), an attacker can set the callback URL to cloud metadata endpoints (`http://169.254.169.254/`), internal admin services, or localhost endpoints. The `MessageProcessor` process then makes requests to internal resources on behalf of the attacker, forwarding the raw SMS message body as the POST payload.

Risk is currently medium because account records are populated by database migrations with no user-facing management endpoint. Risk escalates to high if an account management API is added in a future sprint without SSRF controls.

**Recommended Fix:**

Add domain allowlist validation before issuing the HTTP request. Drive the allowlist from configuration:

```csharp
private static bool IsCallbackUrlAllowed(string callbackUrl, IConfiguration config)
{
    if (!Uri.TryCreate(callbackUrl, UriKind.Absolute, out var uri)) return false;
    if (uri.Scheme is not "https") return false;
    var allowed = config.GetSection("Forwarding:AllowedCallbackDomains")
                        .Get<string[]>() ?? [];
    return Array.Exists(allowed, d =>
        uri.Host.Equals(d, StringComparison.OrdinalIgnoreCase) ||
        uri.Host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));
}
```

Log and skip (do not forward) any callback URL that does not pass validation.

- **Status:** Open

---

### SEC-005: No rate limiting on inbound webhook or outbound message endpoints

- **Files:**
  - `src/TCPA.Api/Controllers/InboundWebhookController.cs`
  - `src/TCPA.Api/Controllers/OutboundMessagesController.cs`
- **Severity:** SECURITY-MEDIUM
- **CWE:** CWE-770 — Allocation of Resources Without Limits or Throttling

**Description:**

`AdminController.ReOptIn` is protected by `[EnableRateLimiting("AdminReOptIn")]` (10 req/min per API key). The inbound webhook and outbound message endpoints have no rate limiting. A caller with a valid API key can:

1. Flood `POST /webhook/inbound` with thousands of events per second, saturating the Kafka `inbound-messages` topic and creating sustained back-pressure on `InboundMessageWorker`.
2. Flood `POST /api/v1/messages/outbound`, overwhelming the `outbound-messages` topic and exhausting Cool Text API quota.

Both are denial-of-service vectors with a cost dimension (Cool Text API call volume, Kafka storage).

**Recommended Fix:**

Extend the existing `AddRateLimiter` block in `Program.cs` with per-key limits calibrated to the expected peak load (5,000 msg/hr per specs ≈ 84 req/min peak per account):

```csharp
options.AddPolicy("InboundWebhook", context =>
    RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Request.Headers["X-Api-Key"].ToString() is { Length: > 0 } k ? k : "anonymous",
        factory: _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 200, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

options.AddPolicy("OutboundSubmit", context =>
    RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Request.Headers["X-Api-Key"].ToString() is { Length: > 0 } k ? k : "anonymous",
        factory: _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 200, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
```

Apply `[EnableRateLimiting("InboundWebhook")]` and `[EnableRateLimiting("OutboundSubmit")]` to the respective action methods.

- **Status:** Open

---

### SEC-006: DENY DELETE migration uses hardcoded SQL login name that may not match production

- **File:** `src/TCPA.Core/Migrations/20260723200741_CreateAuditLog.cs`, lines 92–94
- **Severity:** SECURITY-MEDIUM
- **CWE:** CWE-1188 — Insecure Default Initialization of Resource

**Description:**

The migration includes:

```csharp
migrationBuilder.Sql(
    "DENY DELETE ON dbo.AuditLog TO [tcpa_app_user]",
    suppressTransaction: true);
```

The comment in the migration acknowledges that `tcpa_app_user` is a placeholder that must be confirmed with the DBA before deployment. If the production SQL Server login has a different name — which is likely — the `DENY DELETE` silently has no effect. The AuditLog table remains deletable by the application login, violating:

- BR-031: "Audit records are immutable — no update or delete operations permitted."
- NFS-003: 5-year minimum retention of all audit records.

The InMemory test provider never executes raw SQL, so this defect is invisible in all current tests.

**Recommended Fix:**

Two options (either or both):

1. **Parameterise the login name:** Extract it to a build-time or deployment-time configuration value. Generate the DENY SQL with the confirmed login name, or script it as a post-deployment step outside the EF migration so a DBA can review and sign off.

2. **Grant minimal permissions to the application login:** Provision the application SQL Server login with only `INSERT` and `SELECT` on `AuditLog` — no `DELETE` or `UPDATE` granted means DENY is redundant. This is more robust than relying on DENY.

Before any non-development deployment, verify the DENY is applied with the correct login:

```sql
SELECT dp.name AS principal, p.permission_name, p.state_desc
FROM sys.database_permissions p
JOIN sys.database_principals dp ON dp.principal_id = p.grantee_principal_id
JOIN sys.objects o ON o.object_id = p.major_id
WHERE o.name = 'AuditLog' AND p.permission_name = 'DELETE';
```

- **Status:** Open

---

## Low Findings

| ID | File | Severity | Description | Recommendation |
|----|------|----------|-------------|----------------|
| SEC-007 | `src/TCPA.Api/Messaging/KafkaMessagePublisher.cs`, lines 48 and 69 | LOW | Kafka partition keys are raw E.164 phone numbers. Topic message values include raw `From`/`To` phone fields and SMS body content. If Kafka is not secured with TLS and ACL-based topic authorization, PII is exposed at the broker layer. | Configure Kafka with `security.protocol=SASL_SSL`. Restrict topic read/write ACLs to the application service accounts. Enforce encryption at rest on Kafka broker storage. Infrastructure concern — not resolvable in application code. |

---

## Security Controls Checklist

- [x] Input validation on all external inputs — E.164 regex on all phone number fields; `MaxLength(160)` on outbound SMS body; `[Required]` on all mandatory fields
- [x] SQL / command injection not possible — EF Core parameterized LINQ queries throughout; no string-concatenated SQL except the migration DENY statement (controlled, not user-input-derived)
- [x] Authentication present on all protected endpoints — `ApiKeyAuthFilter` on all three controllers; `AdminApiKeyAuthFilter` as an additional layer on `AdminController`; `HealthController` unauthenticated by design and returns no sensitive data
- [x] Authorization checks use resource ownership — admin endpoint requires a separate admin-scoped key, enforced by dual filter stack; standard keys are rejected by `AdminApiKeyAuthFilter`
- [ ] No hardcoded credentials — **FAIL**: `appsettings.json` contains functional default API keys and `PhoneHashKey` (SEC-002)
- [x] No credentials or PII in logs — all `LogInformation`, `LogWarning`, `LogError`, `LogCritical` calls throughout the codebase use `_hasher.Hash(phoneNumber)` for every phone number; no raw E.164 number found in any Serilog structured log template
- [x] No sensitive data in error responses — error messages are generic strings; no API key values, phone numbers, stack traces, or internal path information returned to callers
- [ ] Dependency CVE scan — Confluent.Kafka, EF Core 8, Serilog, ASP.NET Core 8, and Azure Functions SDK were added. No `dotnet list package --vulnerable` output was produced in this pipeline run. Recommend adding this to CI.
- [x] Sensitive files untouched (.tf, .bicep, .yml, .yaml, .cfn, .env) — verified clean via git diff against main

---

## Risk Fulfillment Check

| Risk ID | Security Risk (from risks.md) | Status in Code |
|---------|-------------------------------|----------------|
| RISK-005 | No API key rotation/revocation | **Partially mitigated** — dual-key scoping (standard vs admin) is implemented via `ApiKeyAuthFilter` + `AdminApiKeyAuthFilter`. Rotation mechanism is an auth-service-layer concern not in scope of this codebase. Accepted per risks.md disposition. |
| RISK-007 | Credentials stored in plain text config | **Open** — API keys and `PhoneHashKey` are in plaintext `appsettings.json` (SEC-002). Secrets management not wired. |
| RISK-008 | Admin endpoint scope not differentiated | **Mitigated** — `AdminApiKeyAuthFilter` requires a separate `ApiKeys:AdminKeys` key. Standard caller keys are rejected at the admin endpoint. |
| RISK-009 | Debug log access controls not defined | **Observed** — Production log minimum level is `Information` (correct per spec). No `Debug` level in any production `appsettings.json`. File log access controls are an infrastructure concern, not resolvable in application code. |

---

## Pre-Merge Blockers

**SEC-001 must be resolved before the PR is opened.** No merge path exists with this finding open — it causes a 100% opt-out failure rate in production.

After SEC-001, SEC-002 and SEC-003 must also be resolved before the PR is approved. The following is the ordered resolution sequence:

1. **SEC-001** — Fix `AuditLog.PhoneNumber` assignment in `OptOutProcessingService`, `ConfirmationDispatchService`, `OutboundGateService`, `OutboundSendService`. Verify with SQL Server integration tests.
2. **SEC-002** — Remove functional default API key values from `appsettings.json`. Implement startup fail-fast. Document secrets injection.
3. **SEC-003** — Add `app.UseHttpsRedirection()` or document infrastructure-layer TLS guarantee with an ADR.
4. **SEC-004 through SEC-006** — Address in follow-up sprint or as part of the current PR, at the tech lead's discretion.
5. **SEC-007** — Infrastructure configuration — no code change required; document in `operations.md`.
