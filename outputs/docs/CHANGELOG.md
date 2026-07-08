# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [1.0.0] — 2026-06-26

Initial delivery of the TCPA Regulatory Compliance API (Phase 1).

### Added

**Outbound SMS Compliance Gate**
- `POST /api/v1/sms/outbound` — receives outbound SMS requests from upstream SCG applications (BizTalk, GCMA, KMI Active, ARM/Construction Portal, CCB/My Account) and enforces TCPA opt-out compliance before forwarding to Cool Text/Twilio.
- Fail-closed behavior: returns 503 and blocks the message if the opt-out database is unavailable. No message is ever forwarded without a confirmed opt-in status check.
- Per-application API key authentication (`X-API-Key` header) with constant-time key comparison.
- Returns `FORWARDED`, `SUPPRESSED`, or `UNREGISTERED_ACCOUNT` status in the response.
- Structured audit log entry written for every suppressed message.

**Inbound SMS Processing and Opt-Out Detection**
- `POST /api/v1/sms/inbound` — receives inbound customer SMS replies via Cool Text webhook.
- HMAC-SHA256 webhook signature validation (ADR-007). Rejects unsigned or tampered webhook payloads with 401 before any processing.
- Keyword detection for all 7 CTIA opt-out keywords: STOP, CANCEL, UNSUBSCRIBE, END, QUIT, REMOVE, OPT-OUT. Word-boundary regex matching — embedded occurrences (e.g., "NONSTOP") do not trigger opt-out.
- Opt-out status written atomically to the database on keyword detection.
- Opt-out confirmation SMS dispatched to the customer within 60-second SLA via Cool Text.
- Non-opt-out messages forwarded to the originating SCG application's registered callback URL (3-attempt exponential backoff).
- Immediate 200 OK webhook acknowledgement before asynchronous processing to prevent Cool Text retries.

**Admin API — Re-Opt-In and Status Lookup**
- `PUT /admin/v1/opt-out/re-opt-in` — allows authenticated Help Desk agents and Compliance Officers to manually reverse a customer opt-out. Requires mandatory reason (min 20 characters) and optional ticket reference. Agent identity extracted from JWT; cannot be supplied in request body.
- `GET /admin/v1/opt-out/status/{cellPhoneNumber}` — look up current opt-out status for a cell number. Returns masked number (last 4 digits only).
- JWT Bearer authentication via SCG Identity Provider (OAuth 2.0/OIDC). Required roles: `tcpa.helpdesk` or `tcpa.compliance_officer`.
- Every admin call logged as a security event regardless of outcome.

**Compliance Reporting**
- `GET /api/v1/reports/opted-in` — on-demand query of forwarded SMS records. Supports date range filtering (up to 90 days), application filter, and cell number filter.
- `GET /api/v1/reports/opted-out` — on-demand query of blocked (suppressed) SMS records with same filter options.
- Weekly automated compliance report delivered every Monday at 06:00 UTC via the Azure Functions timer trigger (`TCPA.Scheduler`). Report includes: forwarded count, blocked count, opt-out event count, re-opt-in count, per-application breakdown, opt-out enforcement success rate, and compliance failure list.
- HTML email body + CSV attachment. Subject prefixed with `[COMPLIANCE FAILURE]` when violations are detected.
- Manual report re-run endpoint for regenerating any prior reporting period.

**Immutable Audit Log**
- Append-only `AuditLogEntries` table. Records for opt-out events, blocked outbound attempts, and re-opt-in actions.
- Application-layer enforcement: `AuditLogService` exposes `AddAsync` only; no Update/Delete methods on the interface.
- Database-layer enforcement: DDL trigger `trg_AuditLogEntries_Immutability` rejects any UPDATE or DELETE on the table.
- 5-year retention policy. Records older than 90 days tiered to Azure Blob Storage WORM (immutable) storage.

**Application Registry**
- In-memory cached lookup of Cool Text account IDs to SCG application names, callback URLs, and active status. 5-minute TTL.
- CCB/My Account registered with `IsActive = false`. Active flag must be set via the IT deployment pipeline after full integration testing with the CCB team.
- Startup validation logs a warning if any of the five expected SCG applications are missing from the registry.

**Observability**
- `GET /health` — unauthenticated health check. Returns 200/503 with per-dependency status. Sanitizes descriptions to prevent internal detail disclosure.
- Correlation ID middleware: every request assigned a UUID correlation ID, propagated through all log events and echoed in the `X-Correlation-ID` response header.
- Structured JSON logging via Serilog with async console sink.
- Dynamic log level toggle via Azure App Configuration (no restart required, 30-second polling interval).
- Cell phone numbers masked to last 4 digits in all log output.

**Database Schema**
- Initial EF Core migration (`20260626000001_InitialSchema`) creates: `ApplicationRegistrations`, `CellNumberOptOutRecords`, `AuditLogEntries`, `SmsMessageLogs`.
- Azure SQL connection with retry-on-failure (3 retries, 10-second backoff).

### Security

- All cell phone numbers stored with Azure SQL Always Encrypted (AES-256, deterministic encryption) — post-migration infrastructure step required before production go-live.
- TLS 1.2+ enforced at the Azure Application Gateway layer.
- API keys never logged; constant-time comparison used to prevent timing oracle attacks.
- HMAC-SHA256 webhook validation with constant-time signature comparison.
- Health check descriptions sanitized before response — no connection strings, hostnames, or stack traces returned to callers.
- Admin endpoints network-restricted to SCG internal network.

### Known Limitations and Open Items

- **Always Encrypted not yet applied:** Column encryption on `CellPhoneNumber` columns is a post-migration infrastructure step (TASK-061). The migration creates plain `nvarchar` columns. Do not promote to production until encryption is confirmed applied.
- **Cool Text webhook secret must be confirmed with vendor** (ARCH-RISK-004): The HMAC signing mechanism and header name must be verified with the Cool Text vendor before go-live.
- **SCG Identity Provider not yet provisioned** (TASK-024): Admin API endpoints will log a startup warning and be unavailable until `Authentication:AdminApi:Authority` is configured. Confirm the Azure AD/Entra ID tenant and RBAC roles with IT Security.
- **CCB/My Account inactive:** CCB is seeded in the Application Registry with `IsActive = false`. Activate only after end-to-end integration testing.
- **`AuditEventType` enum reconciliation required:** `ReportingService.QueryOptedOutAsync` references `AuditEventType.SmsBlocked`; the enum defines `BlockedOutbound`. This will not compile until resolved. Use `AuditEventType.BlockedOutbound`.
- **Archived audit query path not implemented:** Query path for audit records older than 90 days (in Blob Storage) is a Phase 2 item.
- **Application Registry seed placeholders:** `001_ApplicationRegistrationSeed.sql` contains `[PLACEHOLDER: ...]` values for Cool Text account IDs and callback URLs. These must be replaced before the seed script is run in any environment.
- **Report distribution list pending:** `Reporting:RecipientList` must be confirmed with the Compliance team (CQ-004 is open). The scheduler will fail if this is not set.
- **Legal approval for opt-out confirmation SMS text pending** (ARCH-RISK-007): The confirmation SMS message text must be approved by Legal before go-live. Store the approved text at `TCPA:OptOutConfirmationSmsText` in Azure Key Vault.
