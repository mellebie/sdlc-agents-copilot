# TCPA Compliance API — Endpoint Reference

**Base URL:** `https://<host>`
**Auth:** All protected endpoints require the `X-Api-Key` request header.

Swagger UI is available at `/swagger` when the application is running in the Development environment.

---

## Authentication

| Header | Description |
|--------|-------------|
| `X-Api-Key` | Required on all endpoints except `/api/v1/health`. Value must be present in `ApiKeys:ValidKeys`. |

Admin endpoints (`/api/v1/admin/*`) require an additional check: the key must also appear in `ApiKeys:AdminKeys`.

Missing or invalid key: `401 Unauthorized`.

---

## Endpoints

### POST /webhook/inbound

Receives an inbound SMS event from Cool Text or Twilio. Validates the destination account, enforces idempotency, and publishes the event to the `inbound-messages` Kafka topic for async processing. Returns HTTP 200 within the 5-second SLA; all processing happens downstream.

**Auth:** `X-Api-Key` (standard key)

**Request body:**

| Field | Type | Required | Constraints |
|-------|------|----------|-------------|
| `from` | string | Yes | E.164 format, e.g. `+14045551234` |
| `to` | string | Yes | Cool Text account number, e.g. `CT-001` |
| `body` | string | Yes | Message text (min length 1) |
| `provider` | string | Yes | SMS provider name, e.g. `cooltext` |
| `messageId` | string | Yes | Provider-assigned message ID (used for idempotency) |
| `timestamp` | string (DateTimeOffset) | Yes | ISO 8601, e.g. `2026-07-24T10:00:00Z` |

**Request example:**

```http
POST /webhook/inbound HTTP/1.1
Host: api.tcpa.example.com
X-Api-Key: your-api-key
Content-Type: application/json

{
  "from": "+14045551234",
  "to": "CT-001",
  "body": "STOP",
  "provider": "cooltext",
  "messageId": "ct-msg-abc123",
  "timestamp": "2026-07-24T10:00:00Z"
}
```

**Response 200 — message received and queued:**

```json
{
  "status": "received",
  "internalId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

**Response 200 — duplicate messageId (idempotent retry):**

Same shape as above. The `internalId` matches the original response.

**Response 400 — unknown or inactive Cool Text account:**

```json
{
  "error": "Cool Text account 'CT-999' is not registered or inactive."
}
```

**Response 401 — missing or invalid API key:**

```json
{
  "error": "Invalid or missing API key."
}
```

**Response 500 — Kafka broker unreachable:**

```json
{
  "error": "Failed to queue message for processing."
}
```

---

### POST /api/v1/messages/outbound

Submits an outbound SMS for dispatch. Performs a queue-time opt-out check (fail-safe: message is suppressed if the compliance store is unavailable). Approved messages are published to the `outbound-messages` Kafka topic for dispatch by OutboundDispatcher.

**Auth:** `X-Api-Key` (standard key)

**Request body:**

| Field | Type | Required | Constraints |
|-------|------|----------|-------------|
| `toNumber` | string | Yes | Destination phone, E.164 format |
| `body` | string | Yes | SMS text, max 160 characters |
| `coolTextAccountNumber` | string | Yes | Registered account number, e.g. `CT-001` |
| `applicationId` | string | Yes | Submitting application identifier, e.g. `GCMA` |
| `correlationId` | string | No | Caller-provided idempotency key; if omitted, a new key is generated |

**Request example:**

```http
POST /api/v1/messages/outbound HTTP/1.1
Host: api.tcpa.example.com
X-Api-Key: your-api-key
Content-Type: application/json

{
  "toNumber": "+14045551234",
  "body": "Your meter read appointment is confirmed for tomorrow.",
  "coolTextAccountNumber": "CT-001",
  "applicationId": "GCMA",
  "correlationId": "gcma-order-789"
}
```

**Response 200 — message queued:**

```json
{
  "status": "queued",
  "messageId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "queuedAt": "2026-07-24T10:00:00+00:00",
  "suppressionReason": null
}
```

**Response 200 — message suppressed (recipient opted out):**

```json
{
  "status": "suppressed",
  "messageId": null,
  "queuedAt": null,
  "suppressionReason": "opted-out"
}
```

**Response 400 — unregistered or inactive Cool Text account:**

```json
{
  "error": "Cool Text account 'CT-999' is not registered or inactive."
}
```

**Response 401 — missing or invalid API key:**

```json
{
  "error": "Invalid or missing API key."
}
```

**Response 503 — compliance store unavailable (fail-safe, message not sent):**

```json
{
  "error": "TCPA compliance check unavailable. Message not sent."
}
```

**Response 503 — Kafka broker unavailable:**

```json
{
  "error": "Messaging service unavailable. Retry after a moment."
}
```

**Idempotency note:** If `correlationId` is provided and has already been processed, the original response is returned without re-queuing.

---

### POST /api/v1/admin/reopt-in

Re-opts in a customer who has previously opted out. Intended for help desk agents responding to verbal customer requests. Writes an audit log entry and updates opt-out status atomically within a single database transaction.

**Auth:** `X-Api-Key` must be present in both `ApiKeys:ValidKeys` and `ApiKeys:AdminKeys`.

**Rate limit:** 10 requests per minute per API key (fixed window). Exceeding the limit returns `429 Too Many Requests` with `Retry-After: 60`.

**Request body:**

| Field | Type | Required | Constraints |
|-------|------|----------|-------------|
| `phoneNumber` | string | Yes | E.164 format |
| `agentId` | string | Yes | Help desk agent identifier |
| `reason` | string | Yes | Reason for re-opt-in, max 500 characters |

**Request example:**

```http
POST /api/v1/admin/reopt-in HTTP/1.1
Host: api.tcpa.example.com
X-Api-Key: your-admin-api-key
Content-Type: application/json

{
  "phoneNumber": "+14045551234",
  "agentId": "agent-042",
  "reason": "Customer called in to request re-enrolment after accidental STOP reply."
}
```

**Response 200 — re-opt-in successful:**

```json
{
  "reOptInId": 4217,
  "phoneNumber": "+14045551234",
  "status": "opted-in",
  "effectiveAt": "2026-07-24T10:00:00+00:00"
}
```

**Response 400 — validation error (e.g. invalid phone format):**

Standard ASP.NET Core model validation problem details.

**Response 401 — missing or invalid API key:**

```json
{
  "error": "Invalid or missing admin API key."
}
```

**Response 429 — rate limit exceeded:**

```json
{
  "error": "Rate limit exceeded. Retry after 60 seconds."
}
```

`Retry-After: 60` header is also set.

**Response 500 — transaction failed (rolled back):**

```json
{
  "error": "Re-opt-in failed. The operation was rolled back."
}
```

**Anomaly flag:** If the phone number is not currently opted out when this endpoint is called, the audit record is written with `AnomalyFlag = true`. This does not change the HTTP response but is visible in audit log queries.

---

### GET /api/v1/health

Returns the health status of the API and its dependencies (database and Kafka). No authentication required.

**Response 200 — all dependencies healthy:**

```json
{
  "status": "healthy",
  "checks": {
    "database": "ok",
    "kafka": "ok"
  },
  "timestamp": "2026-07-24T10:00:00+00:00"
}
```

**Response 503 — one or more dependencies degraded:**

```json
{
  "status": "degraded",
  "checks": {
    "database": "degraded",
    "kafka": "ok"
  },
  "timestamp": "2026-07-24T10:00:00+00:00"
}
```

The response body shape is identical for 200 and 503. Callers should check the HTTP status code.

---

## Opt-out Keyword Reference

The following exact strings trigger the opt-out pipeline when received as the complete message body (case-insensitive, trimmed). Partial matches and substrings do not match.

`STOP` · `QUIT` · `END` · `REVOKE` · `OPT-OUT` · `CANCEL` · `UNSUBSCRIBE`
