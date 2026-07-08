# TCPA Regulatory Compliance API

The TCPA Compliance API is a middleware service that enforces the Telephone Consumer Protection Act (TCPA) opt-out requirements for all outbound SMS messages sent by Southern Company Gas (SCG) upstream applications. It sits between SCG application systems (BizTalk, GCMA, KMI Active, ARM/Construction Portal, CCB/My Account) and the Cool Text/Twilio SMS platform.

Every outbound SMS must pass through the compliance gate before delivery. The gate checks the destination cell number against the opt-out database and suppresses the message if the recipient has opted out. Inbound replies (such as "STOP") are received via webhook and trigger the opt-out pipeline automatically.

Full documentation is in [docs/](./):
- [api.md](api.md) — API endpoint reference
- [architecture.md](architecture.md) — Developer architecture overview
- [operations.md](operations.md) — Ops team configuration and runbook
- [CHANGELOG.md](CHANGELOG.md) — Release history

---

## Quickstart — Run Locally

**Prerequisites:** .NET 8 SDK, SQL Server LocalDB (bundled with Visual Studio), Azure Functions Core Tools v4

### 1. Clone and restore

```bash
git clone <repo-url>
cd sdlc-agents
dotnet restore src/TCPA.Api/TCPA.Api.csproj
```

### 2. Create the local database

The app auto-migrates in non-Production environments. Start it once with LocalDB configured and the migration runs automatically.

The `appsettings.Development.json` already points to LocalDB:

```
Server=(localdb)\mssqllocaldb;Database=TcpaApi_Dev;Trusted_Connection=True;Column Encryption Setting=Enabled;
```

No changes needed for local development.

### 3. Set required secrets

The following secrets are required. For local development, use [.NET user secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets):

```bash
cd src/TCPA.Api
dotnet user-secrets set "Auth:ApiKey" "dev-api-key-replace-me"
dotnet user-secrets set "CoolText:WebhookSecret" "dev-webhook-secret-replace-me"
```

The Admin API endpoints require `Authentication:AdminApi:Authority` to be set. For local development without an identity provider, the admin endpoints will be unavailable (a warning is logged at startup — this is expected).

### 4. Run the API

```bash
dotnet run --project src/TCPA.Api/TCPA.Api.csproj
```

The API listens on `https://localhost:5001` and `http://localhost:5000` by default. On first startup, EF Core migrations run automatically and create the database schema.

### 5. Verify it is running

```bash
curl http://localhost:5000/health
```

Expected response:
```json
{"status":"healthy","checks":{"tcpa-database":{"status":"ok","description":null}},"timestamp":"2026-06-26T..."}
```

---

## Running Tests

```bash
dotnet test tests/TCPA.Api.Tests/TCPA.Api.Tests.csproj
```

Tests use in-memory or mocked dependencies and do not require a running database or Cool Text connection.

---

## Key Configuration Options

All production values are stored in Azure Key Vault and Azure App Configuration. See [operations.md](operations.md) for the full reference. The most critical settings:

| Key | Purpose | Where to set |
|-----|---------|--------------|
| `ConnectionStrings:TcpaDatabase` | Azure SQL connection string | Azure Key Vault |
| `Auth:ApiKey` | API key for upstream application authentication | Azure Key Vault |
| `CoolText:WebhookSecret` | HMAC shared secret for inbound webhook validation | Azure Key Vault |
| `Authentication:AdminApi:Authority` | SCG Identity Provider OIDC endpoint | Azure App Configuration |
| `AzureKeyVault:Endpoint` | Key Vault URI | `appsettings.json` or environment variable |

For local development, use `appsettings.Development.json` and .NET user secrets. Never commit real credentials.
