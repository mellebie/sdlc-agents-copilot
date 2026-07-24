# TCPA Regulatory Compliance API

The TCPA Compliance system enforces the Telephone Consumer Protection Act opt-out requirements for all outbound SMS messages sent by Southern Company Gas (SCG) upstream applications. It sits between SCG application systems and the Cool Text SMS platform, ensuring no message reaches an opted-out recipient.

Every outbound SMS passes through a compliance gate that checks opt-out status and TCPA quiet hours before delivery. Inbound replies containing opt-out keywords (STOP, QUIT, END, etc.) are received via webhook and trigger the opt-out pipeline automatically.

Full documentation:
- [api.md](api.md) — API endpoint reference with request/response examples
- [architecture.md](architecture.md) — Developer architecture overview
- [operations.md](operations.md) — Configuration reference, health checks, runbook
- [CHANGELOG.md](CHANGELOG.md) — Release history

---

## Quickstart — Run Locally

**Prerequisites:** .NET 8 SDK, SQL Server LocalDB (included with Visual Studio 2022), Kafka (local or Docker)

### 1. Clone and restore

```bash
git clone <repo-url>
cd sdlc-agents
dotnet restore src/TCPA.sln
```

### 2. Set required secrets

Use .NET user secrets for local development. Never commit real credentials.

```bash
# TCPA.Api
cd src/TCPA.Api
dotnet user-secrets set "ApiKeys:ValidKeys" "dev-api-key-local"
dotnet user-secrets set "ApiKeys:AdminKeys" "dev-admin-key-local"
dotnet user-secrets set "Logging:PhoneHashKey" "dev-hash-key-at-least-32-chars!!"

# TCPA.MessageProcessor
cd ../TCPA.MessageProcessor
dotnet user-secrets set "CoolText:ApiKey" "dev-cooltext-key"
dotnet user-secrets set "Logging:PhoneHashKey" "dev-hash-key-at-least-32-chars!!"

# TCPA.OutboundDispatcher
cd ../TCPA.OutboundDispatcher
dotnet user-secrets set "CoolText:ApiKey" "dev-cooltext-key"
dotnet user-secrets set "Logging:PhoneHashKey" "dev-hash-key-at-least-32-chars!!"
```

### 3. Run database migrations

```bash
dotnet ef database update --project src/TCPA.Core --startup-project src/TCPA.Api
```

This creates all five tables: `OptOutStatuses`, `AuditLogs`, `CoolTextAccounts`, `SystemConfigs`, `ProcessedMessages`.

### 4. Run the API

```bash
dotnet run --project src/TCPA.Api/TCPA.Api.csproj
```

The API starts on `https://localhost:5001` / `http://localhost:5000` by default.

### 5. Verify the API is running

```bash
curl http://localhost:5000/api/v1/health
```

Expected response (200 OK):
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

---

## Running Tests

```bash
# All tests
dotnet test src/TCPA.sln

# Specific project
dotnet test tests/TCPA.Api.Tests/TCPA.Api.Tests.csproj
dotnet test tests/TCPA.MessageProcessor.Tests/
dotnet test tests/TCPA.OutboundDispatcher.Tests/
```

Unit tests use in-memory dependencies and require no running services. Integration tests require SQL Server and are skipped automatically if Testcontainers/Docker is unavailable.

Current test counts: 41 API · 22 MessageProcessor · 24 OutboundDispatcher.

---

## Key Configuration Options

All production values are injected as environment variables or via a secrets manager. See [operations.md](operations.md) for the complete reference.

| Key | Purpose | Required by |
|-----|---------|-------------|
| `ConnectionStrings:Primary` | SQL Server write endpoint | All components |
| `ApiKeys:ValidKeys` | Comma-separated valid API keys | TCPA.Api |
| `ApiKeys:AdminKeys` | Comma-separated admin API keys | TCPA.Api |
| `Kafka:BootstrapServers` | Kafka broker list | All components |
| `CoolText:ApiUrl` | Cool Text gateway base URL | MessageProcessor, OutboundDispatcher |
| `CoolText:ApiKey` | Cool Text API key | MessageProcessor, OutboundDispatcher |
| `Logging:PhoneHashKey` | HMAC-SHA256 key for phone number hashing in logs | All components |
