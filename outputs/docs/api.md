# TCPA Compliance API — Endpoint Reference

All endpoints use HTTPS. All request and response bodies are `application/json` (UTF-8) unless otherwise noted.

Every response includes an `X-Correlation-ID` header containing the request's correlation ID. Include this header in support requests.

---

## Authentication Summary

| Endpoint group | Auth mechanism | Header / token |
|----------------|----------------|----------------|
| `POST /api/v1/sms/outbound` | API key (per application) | `X-API-Key: <key>` |
| `POST /api/v1/sms/inbound` | HMAC-SHA256 webhook signature | `X-CoolText-Signature: sha256=<hex>` |
| `PUT /admin/v1/opt-out/re-opt-in` | Bearer JWT (SCG IdP) | `Authorization: Bearer <token>` |
| `GET /admin/v1/opt-out/status/{cellPhoneNumber}` | Bearer JWT (SCG IdP) | `Authorization: Bearer <token>` |
| `GET /api/v1/reports/opted-in` | Bearer JWT (SCG IdP) | `Authorization: Bearer <token>` |
| `GET /api/v1/reports/opted-out` | Bearer JWT (SCG IdP) | `Authorization: Bearer <token>` |
| `GET /health` | None | — |

**Required JWT roles:**
- Admin endpoints (`/admin/v1/`): `tcpa.helpdesk` or `tcpa.compliance_officer`
- Reporting endpoints (`/api/v1/reports/`): `tcpa.compliance_officer` or `tcpa.helpdesk`

---

## POST /api/v1/sms/outbound

Receive an outbound SMS from an upstream SCG application, check the destination number against the TCPA opt-out database, and either forward the message to Cool Text or suppress it.

**Fail-closed behavior:** if the opt-out database is unavailable for any reason, this endpoint returns `503` and does not forward the message. No message is ever forwarded without a confirmed database read.

### Authentication

API key in the `X-API-Key` header. Keys are per-application and stored in Azure Key Vault. Contact IT Security to obtain or rotate a key.

### Request

```http
POST /api/v1/sms/outbound
Content-Type: application/json
X-API-Key: your-application-api-key
```

```json
{
  "cool_text_account_id": "ct-acct-gcma-001",
  "destination_cell_number": "+14045551234",
  "message_body": "Your appointment is confirmed for Monday at 9am.",
  "originating_application_reference": "appt-confirm-txn-98765"
}
```

**Request fields:**

| Field | Type | Required | Constraints | Description |
|-------|------|----------|-------------|-------------|
| `cool_text_account_id` | string | Yes | Non-empty | Cool Text account ID identifying the originating SCG application. Must be registered in the Application Registry. |
| `destination_cell_number` | string | Yes | E.164 format (e.g., `+14045551234`) | Destination cell phone number. Validated against the opt-out database. **PII — never log this value in full.** |
| `message_body` | string | Yes | 1–1600 characters | SMS message content. Concatenated SMS supported up to 1600 characters. |
| `originating_application_reference` | string | No | Optional | Caller-supplied reference ID. Passed to the audit log for traceability. Not used in compliance logic. |

### Response — 200 OK

A 200 response indicates the compliance gate made a decision. Check the `status` field to determine the outcome.

```json
{
  "status": "FORWARDED",
  "message_id": "ct-msg-a1b2c3d4",
  "suppression_reason": null
}
```

**Possible `status` values:**

| Value | Meaning |
|-------|---------|
| `FORWARDED` | Destination number is opted in. Message was forwarded to Cool Text. `message_id` contains the Cool Text message ID. |
| `SUPPRESSED` | Destination number has OPT_OUT status. Message was blocked. `suppression_reason` is `"OPT_OUT"`. |
| `UNREGISTERED_ACCOUNT` | The `cool_text_account_id` is not in the Application Registry (or is inactive). Message was not forwarded. |

### Error Responses

**400 Bad Request — validation failure**

Returned when a required field is missing or `destination_cell_number` is not valid E.164 format.

```json
{
  "error": "VALIDATION_ERROR",
  "fields": ["destination_cell_number"],
  "message": null
}
```

**401 Unauthorized**

Missing or invalid `X-API-Key` header. No body beyond the HTTP status.

**502 Bad Gateway**

The Cool Text platform was unreachable after the opt-in check passed. The message was not delivered. Retry after a short delay; if the problem persists, contact IT operations.

```json
{
  "error": "BAD_GATEWAY",
  "message": "Downstream SMS platform unreachable."
}
```

**503 Service Unavailable — fail-closed**

The TCPA opt-out database was unavailable. The message was NOT forwarded. This is the correct fail-closed behavior. Do not retry until the database is restored.

```json
{
  "error": "SERVICE_UNAVAILABLE",
  "message": "Compliance check unavailable; message not forwarded."
}
```

---

## POST /api/v1/sms/inbound

Receive an inbound SMS webhook from the Cool Text platform. Cool Text calls this endpoint when a customer replies to an SCG SMS.

**Important:** This endpoint returns `200 OK` immediately upon successful signature validation, before any opt-out processing or application forwarding occurs. This prevents Cool Text from timing out and retrying delivery. Downstream processing (keyword detection, opt-out status write, application callback) runs asynchronously after the response is sent.

### Authentication

HMAC-SHA256 payload signature, delivered by Cool Text in the `X-CoolText-Signature` header with the prefix `sha256=`. The TCPA API validates the signature using the shared secret configured at `CoolText:WebhookSecret` in Azure Key Vault. Any request with a missing or invalid signature is rejected immediately with `401`.

This endpoint is called by the Cool Text platform, not by SCG applications. SCG applications do not call this endpoint directly.

### Request

```http
POST /api/v1/sms/inbound
Content-Type: application/json
X-CoolText-Signature: sha256=3b9e7f8a1c2d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0c1d2e3f4a5b6c7d8e9f
```

```json
{
  "cool_text_account_id": "ct-acct-gcma-001",
  "sender_cell_number": "+14045551234",
  "message_body": "STOP",
  "cool_text_message_id": "ct-inbound-msg-xkcd99"
}
```

**Request fields:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `cool_text_account_id` | string | Yes | Cool Text account that received the inbound message. Used to resolve the originating SCG application. |
| `sender_cell_number` | string | Yes | E.164 cell number of the customer who sent the reply. **PII.** |
| `message_body` | string | Yes | Raw message text. Inspected for TCPA opt-out keywords (STOP, CANCEL, UNSUBSCRIBE, END, QUIT, REMOVE, OPT-OUT). |
| `cool_text_message_id` | string | Yes | Platform-assigned message ID from Cool Text. Used for correlation and deduplication logging. |

### Response — 200 OK

```json
{
  "received": true
}
```

The `200 OK` with `{"received":true}` is the webhook acknowledgement contract with Cool Text. It confirms receipt only — not that opt-out processing has completed.

**Opt-out keyword processing (asynchronous after response):** if the message body contains a recognized opt-out keyword, the system will:
1. Write the OPT_OUT status to the database.
2. Send a confirmation SMS to the customer within 60 seconds.
3. Write an immutable audit log entry.
4. Not forward the message to the originating application.

If the message is not an opt-out keyword, it is forwarded to the originating application's registered callback URL.

### Error Responses

**400 Bad Request** — malformed JSON payload or missing required field.

**401 Unauthorized** — HMAC-SHA256 signature is missing or does not match the computed signature. The signature header name defaults to `X-CoolText-Signature` but is configurable via `CoolText:WebhookSignatureHeader`.

---

## PUT /admin/v1/opt-out/re-opt-in

Manually re-opt-in a cell phone number. Used by Help Desk agents and Compliance Officers to reverse a customer's opt-out when they have provided consent through an alternative channel.

This endpoint requires a mandatory reason (minimum 20 characters) and an optional Help Desk ticket reference. All calls are logged as security events regardless of outcome. The agent's identity is extracted from the JWT token — it cannot be supplied in the request body.

### Authentication

Bearer JWT from the SCG Identity Provider. Required role claim: `tcpa.helpdesk` or `tcpa.compliance_officer`. The Admin API endpoints are network-restricted to the SCG internal network (VPN required from off-site).

### Request

```http
PUT /admin/v1/opt-out/re-opt-in
Content-Type: application/json
Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR...
```

```json
{
  "cellPhoneNumber": "+14045551234",
  "reason": "Customer called Help Desk and provided verbal consent to receive SMS. See ticket HD-20260626-4421.",
  "ticketReference": "HD-20260626-4421"
}
```

**Request fields:**

| Field | Type | Required | Constraints | Description |
|-------|------|----------|-------------|-------------|
| `cellPhoneNumber` | string | Yes | E.164 format | Cell phone number to re-opt-in. **PII.** |
| `reason` | string | Yes | Minimum 20 characters | Mandatory free-text reason for the re-opt-in action. Recorded in the immutable audit log. |
| `ticketReference` | string | No | Optional | Help Desk ticket ID or reference number. Recorded in the audit log. |

### Response — 200 OK

Returned on success, including when the number was already in OPT_IN status (idempotent). When the call is a no-op (already OPT_IN), `message` describes this.

```json
{
  "success": true,
  "previousStatus": "OPT_OUT",
  "newStatus": "OPT_IN",
  "updatedTimestamp": "2026-06-26T14:32:00Z",
  "recordId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "message": ""
}
```

**Response fields:**

| Field | Type | Description |
|-------|------|-------------|
| `success` | boolean | `true` when status was set to OPT_IN. |
| `previousStatus` | string | Status before this call: `"OPT_IN"` or `"OPT_OUT"`. |
| `newStatus` | string | Status after this call: always `"OPT_IN"` on success. |
| `updatedTimestamp` | string | ISO 8601 UTC timestamp of the update. |
| `recordId` | string (UUID) | Audit log record ID for this re-opt-in event. Useful for compliance investigations. |
| `message` | string | Informational note, e.g., `"Number was already OPT_IN; no change made."` |

### Error Responses

**400 Bad Request** — required field missing, `reason` is shorter than 20 characters, or `cellPhoneNumber` is not valid E.164 format.

**401 Unauthorized** — missing or invalid Bearer token.

**403 Forbidden** — valid token, but the caller does not have the `tcpa.helpdesk` or `tcpa.compliance_officer` role.

**409 Conflict** — no prior opt-out record exists for this cell number. The re-opt-in endpoint is for reversing a prior opt-out only. If the number has never opted out, no action is needed.

```json
{
  "title": "Conflict",
  "detail": "No opt-out record exists for this cell number. Re-opt-in is only applicable for numbers with a prior opt-out history.",
  "status": 409
}
```

**503 Service Unavailable** — database unavailable. Retry after a short delay.

---

## GET /admin/v1/opt-out/status/{cellPhoneNumber}

Look up the current TCPA opt-out status for a cell phone number. Read-only. The response returns only the last four digits of the cell number to minimize PII exposure.

### Authentication

Same as `PUT /admin/v1/opt-out/re-opt-in` — Bearer JWT with `tcpa.helpdesk` or `tcpa.compliance_officer` role.

### Request

```http
GET /admin/v1/opt-out/status/%2B14045551234
Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR...
```

The `cellPhoneNumber` path parameter must be URL-encoded. The leading `+` in E.164 numbers must be encoded as `%2B`.

### Response — 200 OK

```json
{
  "maskedCellNumber": "****1234",
  "optOutStatus": "OPT_OUT",
  "lastOptOutTimestamp": "2026-06-20T10:00:00Z",
  "lastOptInTimestamp": null
}
```

**Response fields:**

| Field | Type | Description |
|-------|------|-------------|
| `maskedCellNumber` | string | Last four digits only, prefixed with `****`. Full number never returned. |
| `optOutStatus` | string | `"OPT_IN"` or `"OPT_OUT"`. |
| `lastOptOutTimestamp` | string or null | ISO 8601 UTC timestamp of the most recent opt-out. Null if the number has never opted out. |
| `lastOptInTimestamp` | string or null | ISO 8601 UTC timestamp of the most recent re-opt-in. Null if the number has never been re-opted-in. |

### Error Responses

**400 Bad Request** — `cellPhoneNumber` path parameter is not valid E.164 format.

**401 Unauthorized** — missing or invalid token.

**403 Forbidden** — valid token, insufficient role.

**404 Not Found** — no record exists for this cell number. A 404 means the system has no history for this number. For TCPA compliance purposes, no history implies OPT_IN (no block on record).

```json
{
  "title": "Not Found",
  "detail": "No opt-out record exists for this cell number.",
  "status": 404
}
```

---

## GET /api/v1/reports/opted-in

On-demand query of outbound SMS messages that were forwarded to opted-in numbers within a specified date range. Supports up to 90 days per query.

### Authentication

Bearer JWT with `tcpa.compliance_officer` or `tcpa.helpdesk` role.

### Request

```http
GET /api/v1/reports/opted-in?from=2026-06-01&to=2026-06-07&application=GCMA
Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR...
```

**Query parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `from` | ISO 8601 date | Yes | Inclusive start date (e.g., `2026-06-01`). Interpreted as UTC midnight. |
| `to` | ISO 8601 date | Yes | Inclusive end date (e.g., `2026-06-07`). Must be on or after `from`. Maximum 90-day range from `from`. |
| `application` | string | No | Filter by SCG application name (e.g., `GCMA`, `KMI Active`). When omitted, returns records for all applications. |
| `cell_number` | string | No | Filter by specific cell number in E.164 format. When omitted, returns all numbers. |

### Response — 200 OK

```json
{
  "records": [
    {
      "status": "FORWARDED",
      "cellPhoneNumber": "+14045551234",
      "originatingApplicationName": "GCMA",
      "messageTimestamp": "2026-06-03T14:22:00Z",
      "messageBody": "Your appointment is confirmed.",
      "coolTextAccountId": "ct-acct-gcma-001"
    }
  ],
  "totalCount": 1
}
```

**Record fields:**

| Field | Type | Description |
|-------|------|-------------|
| `status` | string | Always `"FORWARDED"` in this dataset. |
| `cellPhoneNumber` | string | E.164 destination number. **PII** — handle accordingly. |
| `originatingApplicationName` | string | SCG application that submitted the message (e.g., `"GCMA"`). |
| `messageTimestamp` | string | ISO 8601 UTC timestamp when the message was forwarded. |
| `messageBody` | string or null | SMS content. Present for regulatory discovery; treat as PII-adjacent. |
| `coolTextAccountId` | string | Cool Text account ID used to submit the message. |

### Error Responses

**400 Bad Request** — `from` or `to` is missing, unparseable, `from` is later than `to`, or the range exceeds 90 days.

**401 Unauthorized**, **403 Forbidden** — authentication or authorization failure.

---

## GET /api/v1/reports/opted-out

On-demand query of outbound SMS messages that were suppressed because the destination number was in OPT_OUT status at send time. Same query parameters as `GET /api/v1/reports/opted-in`.

### Authentication

Same as `/api/v1/reports/opted-in`.

### Request

```http
GET /api/v1/reports/opted-out?from=2026-06-01&to=2026-06-07
Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR...
```

### Response — 200 OK

```json
{
  "records": [
    {
      "status": "BLOCKED",
      "cellPhoneNumber": "+14045559876",
      "originatingApplicationName": "KMI Active",
      "attemptTimestamp": "2026-06-04T09:11:00Z",
      "messageBody": "Your meter reading is ready.",
      "suppressionReason": "OPT_OUT"
    }
  ],
  "totalCount": 1
}
```

**Record fields:**

| Field | Type | Description |
|-------|------|-------------|
| `status` | string | Always `"BLOCKED"` in this dataset. |
| `cellPhoneNumber` | string | E.164 destination number that was blocked. **PII.** |
| `originatingApplicationName` | string | SCG application that attempted to send the message. |
| `attemptTimestamp` | string | ISO 8601 UTC timestamp of the blocked send attempt. |
| `messageBody` | string or null | Content of the suppressed message. Stored for regulatory discovery. |
| `suppressionReason` | string | Always `"OPT_OUT"`. |

### Error Responses

Same as `GET /api/v1/reports/opted-in`.

---

## GET /health

Unauthenticated health check endpoint for load balancer and external monitoring probes.

### Request

```http
GET /health
```

No authentication required.

### Response — 200 OK (healthy)

```json
{
  "status": "healthy",
  "checks": {
    "tcpa-database": {
      "status": "ok",
      "description": null
    }
  },
  "timestamp": "2026-06-26T14:30:00.0000000Z"
}
```

### Response — 503 Service Unavailable (degraded)

Returned when any registered dependency is unhealthy. The body structure is identical to the 200 response; check `status` (will be `"degraded"`) and individual check entries.

```json
{
  "status": "degraded",
  "checks": {
    "tcpa-database": {
      "status": "degraded",
      "description": "Dependency check failed. Contact IT operations."
    }
  },
  "timestamp": "2026-06-26T14:30:00.0000000Z"
}
```

**Note:** Error descriptions are sanitized. Connection strings, IP addresses, hostnames, and stack traces are never returned in health check responses.

The health check currently registers one check: `tcpa-database` (EF Core DbContext ping). The Cool Text connectivity check is not yet registered (see [TODO note in operations.md](operations.md)).

---

## Error Response Format

For 4xx and 5xx responses from the outbound/inbound SMS endpoints, the body is:

```json
{
  "error": "ERROR_CODE",
  "message": "Human-readable description.",
  "fields": ["field_name"]
}
```

`fields` is present only for `VALIDATION_ERROR` responses.

For Admin and Reporting endpoints, errors use ASP.NET Core `ProblemDetails` format (RFC 7807):

```json
{
  "title": "Error category",
  "detail": "Specific description.",
  "status": 400
}
```
