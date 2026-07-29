# Smoke Test Checklist

## Scope
- Production-safe smoke validations under 2 minutes total execution target.

## Smoke Cases
- `SMOKE-001`: Inbound validation endpoint responds and rejects malformed payload with stable error shape.
- `SMOKE-002`: Routeable in-scope inbound payload returns accepted response and correlation ID.
- `SMOKE-003`: Out-of-scope mapping returns deterministic `SCOPE_MAPPING_NOT_FOUND` code.
- `SMOKE-004`: STOP classification maps mixed-case punctuation input to `STOP`.
- `SMOKE-005`: HELP forwarding path returns success or retryable result without consent mutation.

## Execution Notes
- Use non-production identifiers and no real customer phone numbers.
- Do not mutate production data stores.
