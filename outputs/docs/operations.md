# TCPA Compliance API — Operations Guide

This document is for the IT operations team responsible for deploying, configuring, and operating the TCPA Compliance API.

---

## Environment Variables and Configuration Reference

Configuration is loaded in this order (later sources override earlier ones):
1. `appsettings.json` (base defaults, committed to source — values are empty strings)
2. `appsettings.{Environment}.json` (Development overrides only; not deployed to Production)
3. Azure Key Vault (secrets — highest priority for sensitive values)
4. Azure App Configuration (dynamic settings, e.g., log level toggle)
5. Environment variables (override any of the above)

### Connection Strings

| Key | Required | Description |
|-----|----------|-------------|
| `ConnectionStrings:TcpaDatabase` | Yes | Azure SQL connection string for the operational database (opt-out status, app registry, audit log, SMS message log). Must include `Column Encryption Setting=Enabled` for Always Encrypted to function. Store in Azure Key Vault. |
| `ConnectionStrings:AuditLogDatabase` | No | Reserved for a separate audit log database if introduced in Phase 2. Not used in Phase 1 — the audit log shares `TcpaDatabase`. |

**Example connection string structure (do not include real credentials in documentation):**

```
Server=tcp:<server>.database.windows.net,1433;Initial Catalog=TcpaApi;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Column Encryption Setting=Enabled;
```

### Authentication

| Key | Required | Description |
|-----|----------|-------------|
| `Auth:ApiKey` | Yes | Shared API key for upstream SCG application authentication (`X-API-Key` header). Store in Azure Key Vault. Rotate by updating this value — no restart needed. |
| `Authentication:AdminApi:Authority` | Yes (for Admin API) | OIDC authority endpoint for the SCG Identity Provider (Azure AD/Entra ID). Example: `https://login.microsoftonline.com/<tenant-id>/`. Without this, Admin endpoints are unavailable (warning logged at startup). |
| `Authentication:AdminApi:Audience` | Yes (for Admin API) | Expected JWT audience claim. Confirms tokens are issued for this application. |
| `Authentication:AdminApi:ValidIssuer` | No | Optional additional issuer validation. |

### Cool Text Integration

| Key | Required | Description |
|-----|----------|-------------|
| `CoolText:BaseUrl` | Yes | Base URL for the Cool Text API (e.g., `https://api.cooltext.com`). |
| `CoolText:TimeoutSeconds` | No | HTTP timeout for Cool Text API calls. Default: `10`. Increase if Cool Text consistently times out under load. |
| `CoolText:WebhookSecret` | Yes | HMAC-SHA256 shared secret used to validate inbound webhooks from Cool Text. Store in Azure Key Vault. Confirm the exact value with the Cool Text vendor. |
| `CoolText:WebhookSignatureHeader` | No | HTTP header name Cool Text uses for the HMAC signature. Default: `X-CoolText-Signature`. Override if the vendor uses a different header name. Confirm with Cool Text vendor (ARCH-RISK-004). |

### Application Registry

| Key | Required | Description |
|-----|----------|-------------|
| `ApplicationRegistry:CacheTtlMinutes` | No | Time-to-live for the application registry in-memory cache. Default: `5`. Reduces database calls for near-static data. |
| `ApplicationRegistry:StartupValidation:RequiredApplicationNames` | No | JSON array of application names expected at startup. A warning is logged for any missing name. Default: `["BizTalk","GCMA","KMI Active","ARM/Construction Portal","CCB/My Account"]`. |

### Azure Platform

| Key | Required | Description |
|-----|----------|-------------|
| `AzureKeyVault:Endpoint` | Yes (in Azure) | Azure Key Vault URI (e.g., `https://<vault-name>.vault.azure.net/`). The application uses `DefaultAzureCredential` to authenticate (Managed Identity in Azure). |
| `AzureAppConfiguration:Endpoint` | No | Azure App Configuration URI for dynamic log level toggling. Without this, log level can only be changed via restart. |
| `AzureAppConfiguration:RefreshIntervalSeconds` | No | How often the app polls App Configuration for log level changes. Default: `30`. |

### Reporting (Azure Functions Scheduler)

These settings apply to the `TCPA.Scheduler` Azure Functions project:

| Key | Required | Description |
|-----|----------|-------------|
| `Reporting:SmtpHost` | Yes | SMTP relay hostname (SCG email relay). |
| `Reporting:SmtpPort` | No | SMTP port. Default: `587` (STARTTLS). |
| `Reporting:SmtpUser` | Yes | SMTP authentication username. Store in Azure Key Vault or App Configuration. |
| `Reporting:SmtpPassword` | Yes | SMTP password. Store in Azure Key Vault. Never commit a real value. |
| `Reporting:RecipientList` | Yes | Semicolon-separated list of Compliance Officer email addresses. [TODO: confirm distribution list address with Compliance team — CQ-004 is open.] |
| `Reporting:SenderAddress` | Yes | From address for compliance report emails. |

### Health Checks

| Key | Required | Description |
|-----|----------|-------------|
| `HealthChecks:DependencyTimeoutSeconds` | No | Timeout for each dependency health check. Default: `2`. |

---

## Health Check Endpoint

`GET /health` — no authentication required.

The health check evaluates all registered dependencies and returns:
- `200 OK` with `{"status":"healthy",...}` when all checks pass.
- `503 Service Unavailable` with `{"status":"degraded",...}` when any check fails.

**Currently registered checks:**

| Check name | What it verifies |
|------------|-----------------|
| `tcpa-database` | EF Core can open a connection to `TcpaDatabase` and execute a minimal query. |

[TODO: Cool Text connectivity check is not yet registered. TASK-059 notes it as a planned addition. Until implemented, health will show `healthy` even if Cool Text is unreachable.]

**Monitoring recommendation:** Configure your load balancer and Azure Monitor to probe `/health` at 1-minute intervals. Alert on sustained 503 responses.

---

## Key Log Events and Meanings

All logs are structured JSON (Serilog). Each log event includes `CorrelationId`, `Timestamp`, `Level`, and `MachineName`.

### Critical — Requires Immediate Response

| Event | Log message pattern | Meaning and action |
|-------|--------------------|--------------------|
| Audit log write failure | `AUDIT LOG WRITE FAILURE — compliance event not persisted` | The audit log INSERT failed. A TCPA compliance event was not recorded. **Immediate action required.** IT must manually reconstruct the missing record from operational logs and the failed request context (CorrelationId in the log event). Check database connectivity and disk space. |
| Outbound gate DB failure | `Outbound SMS BLOCKED (fail-closed): TCPA database unavailable` | The compliance gate cannot read opt-out status. All outbound SMS is blocked until the database is restored. Check Azure SQL availability. |
| Outbound SMS error | `Outbound SMS failed: unexpected error` | An unhandled exception from the compliance gate (including Cool Text unreachable). The calling application received 502. |
| Compliance failure detected | `COMPLIANCE FAILURE detected in report` (in `ReportingService`) | A forwarded message was found for an opted-out number. Regulatory risk. Investigate immediately. |
| Weekly report job failure | `WEEKLY REPORT JOB FAILURE — compliance report not delivered` | The Azure Functions Timer Trigger failed to generate or send the weekly report. The report was not delivered to Compliance Officers. Use the manual re-run endpoint to regenerate. |
| API key misconfiguration | `API key authentication misconfiguration: 'Auth:ApiKey' is not set` | The API key is not configured. All outbound SMS requests will be rejected with 503 until fixed. Update Key Vault and restart or wait for config refresh. |

### Warning — Investigate

| Event | Log message pattern | Meaning |
|-------|--------------------|--------------------|
| Inbound webhook HMAC failure | `Inbound webhook rejected: HMAC signature invalid or missing` | A webhook request arrived with a missing or mismatched signature. May indicate a misconfigured Cool Text webhook secret, or a spoofed request attempt. |
| API key auth failure | `API key authentication failed: invalid key supplied` | An SCG application supplied the wrong key. Check whether the key was recently rotated and the application configuration was not updated. |
| Outbound SMS validation failure | `Outbound SMS request rejected: validation failure` | An upstream application sent a malformed request (invalid E.164, missing field). Contact the sending application team. |
| Application registry warning | `Required application '{name}' not found in the active registry` | Startup check found fewer applications than expected. Verify the seed script ran and the Cool Text account IDs are populated. |
| Confirmation SMS SLA breach | `SLA BREACH: opt-out confirmation SMS not dispatched within 60 seconds` | The 60-second TCPA confirmation SLA was exceeded. Investigate Cool Text API latency or database write delays. |

### Information — Normal Operations

| Event pattern | Meaning |
|--------------|---------|
| `Outbound SMS compliance decision: FORWARDED` | Normal forwarded message. |
| `Outbound SMS compliance decision: SUPPRESSED` | Message blocked — recipient is opted out. |
| `Inbound webhook received and signature validated` | Normal inbound webhook receipt. |
| `SECURITY_EVENT: Admin re-opt-in called` | Help Desk or Compliance Officer initiated a re-opt-in action. Every admin action is logged at this level. |
| `Weekly compliance report job completed successfully` | Normal scheduler completion. Includes forwarded/blocked counts and compliance failure count. |

### Dynamic Log Level Toggle

The minimum log level can be changed without restarting the service:
1. Update `Logging:MinimumLevel` in Azure App Configuration.
2. The change takes effect within 30 seconds (configurable via `AzureAppConfiguration:RefreshIntervalSeconds`).
3. To enable debug logging temporarily: set `Logging:MinimumLevel` to `Debug` in Azure App Configuration.
4. Restore to `Information` after investigation.

---

## Common Failure Modes and Diagnostics

### All outbound SMS returning 503

**Symptom:** Upstream applications receive 503 with `"SERVICE_UNAVAILABLE"`.

**Cause:** The TCPA opt-out database is unavailable (fail-closed behavior is working correctly).

**Diagnosis:**
1. Check Azure SQL Database availability in the Azure Portal.
2. Look for `Outbound SMS BLOCKED (fail-closed): TCPA database unavailable` in Azure Monitor / Log Analytics.
3. Check the connection string in Key Vault is correct and includes `Column Encryption Setting=Enabled`.
4. Test connectivity: `GET /health` — if `tcpa-database` shows `degraded`, the database is confirmed unavailable.

**Resolution:** Restore the Azure SQL Database. The application will recover automatically once connectivity is re-established (EF Core has retry-on-failure configured with 3 retries and 10-second backoff).

### Inbound webhooks returning 401

**Symptom:** Cool Text reports webhook delivery failures; this API returns 401.

**Cause:** HMAC signature mismatch. Possible causes:
- `CoolText:WebhookSecret` in Key Vault does not match the secret configured on the Cool Text side.
- Cool Text changed their signing mechanism (ARCH-RISK-004: mechanism must be confirmed with vendor).
- The `X-CoolText-Signature` header name changed on the Cool Text side (configurable via `CoolText:WebhookSignatureHeader`).

**Diagnosis:** Check logs for `Inbound webhook rejected: HMAC signature invalid or missing`. Confirm the signature header name and secret value with the Cool Text vendor.

### Weekly compliance report not delivered

**Symptom:** Compliance Officers did not receive the Monday report.

**Diagnosis:**
1. Check Azure Functions Monitor for `WeeklyComplianceReportFunction` execution history.
2. Look for `WEEKLY REPORT JOB FAILURE` in logs.
3. Check `Reporting:SmtpHost`, `Reporting:SmtpUser`, `Reporting:SmtpPassword`, `Reporting:RecipientList` in App Configuration.
4. Check that `Reporting:RecipientList` is populated.

**Manual re-run:** Use the HTTP-triggered companion function `ManualReportTriggerFunction` at `POST /api/reports/manual-run` (requires `ComplianceReporting` role). Provide `period_start` and `period_end` in the request body. Maximum 31-day period per manual run.

### Opt-out not being enforced immediately

**Symptom:** An outbound message was forwarded to a number that had previously sent "STOP".

**Possible causes:**
- The inbound webhook for the "STOP" message was not processed (check webhook delivery history in Cool Text portal).
- The HMAC secret was misconfigured and the webhook was rejected (check logs for 401 on inbound).
- The opt-out status write failed (check logs for audit log write failures around that time).

**Diagnosis:** Search logs for `sender_cell_number` suffix (last 4 digits) around the time of the opt-out, then around the time of the forwarded message. Retrieve the full audit trail from the database using the `GET /api/v1/reports/opted-out` endpoint.

---

## Running Database Migrations

### Non-Production (automatic)

Migrations run automatically at application startup in non-Production environments. The application detects pending migrations and applies them.

### Production (manual, via CI/CD pipeline)

```bash
dotnet ef database update \
  --project src/TCPA.Api \
  --connection "<production-connection-string-from-key-vault>"
```

Run this before deploying the new application version.

### Post-Migration Steps (first deployment only)

After the initial migration (`20260626000001_InitialSchema`) is applied in Production, the platform team must complete three steps before the service goes live:

**Step 1: Apply Always Encrypted** (TASK-061)

Encrypt the `CellPhoneNumber` column on these three tables using Azure SQL Always Encrypted with a Column Master Key stored in Azure Key Vault:
- `CellNumberOptOutRecords.CellPhoneNumber`
- `AuditLogEntries.CellPhoneNumber`
- `SmsMessageLogs.CellPhoneNumber`

Use deterministic AES-256 encryption to allow indexed lookups. Coordinate with IT Security for CMK/CEK provisioning.

**Step 2: Apply the audit log immutability trigger** (TASK-064)

Run the SQL script at `src/TCPA.Api/Infrastructure/Data/Seeds/002_AuditLogImmutabilityTrigger.sql` against the Production database. This creates the DDL trigger that prevents UPDATE or DELETE operations on `AuditLogEntries`.

**Step 3: Seed the application registry** (TASK-049)

Run `src/TCPA.Api/Infrastructure/Data/Seeds/001_ApplicationRegistrationSeed.sql` after replacing the placeholder values (`[PLACEHOLDER: ...]`) with the actual Cool Text account IDs and callback URLs for each SCG application. CCB is seeded with `IsActive = 0` — activate it only after end-to-end integration testing with the CCB team.

---

## Key Rotation Procedures

### Upstream Application API Key (`Auth:ApiKey`)

1. Generate a new key (minimum 32 random bytes, base64-encoded or hex).
2. Update the secret in Azure Key Vault.
3. The change takes effect on the next request (the filter reads from `IConfiguration` at request time — no restart needed).
4. Update the calling application's configuration to use the new key.
5. Verify delivery resumes, then confirm the old key is no longer needed.

### Cool Text Webhook Secret (`CoolText:WebhookSecret`)

1. Coordinate with the Cool Text vendor to change the signing secret on their side.
2. Update `CoolText:WebhookSecret` in Azure Key Vault.
3. Restart the application (or wait for Key Vault refresh) to pick up the new value.
4. Verify inbound webhook delivery succeeds.

### SMTP Credentials (`Reporting:SmtpPassword`)

1. Update the password in Azure Key Vault.
2. The `ReportEmailer` reads credentials from `IConfiguration` at dispatch time — the new value is used on the next report send without a restart.

---

## Querying Archived Audit Records (> 90 Days)

Audit log records are kept in Azure SQL for 90 days (hot, fully queryable via the API). Records older than 90 days are tiered to Azure Blob Storage WORM (immutable storage) for cost management.

To query records older than 90 days:

[TODO: The Azure Blob Storage tiering pipeline and external query path (Azure Data Factory or external table bridge) have not yet been implemented. This is a Phase 2 item. For regulatory discovery requests requiring records older than 90 days, contact IT to perform a manual data export from Blob Storage. Reference ADR-004 in `outputs/architecture.md`.]
