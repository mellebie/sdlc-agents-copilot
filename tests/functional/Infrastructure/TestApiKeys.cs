// Infrastructure/TestApiKeys.cs
// Source: Agent 09b (Drew) — Functional & E2E Tests
// Centralised constants for test API key values injected via TcpaTestFactory config override.

namespace TCPA.Functional.Tests.Infrastructure;

/// <summary>
/// Test-environment API key values. These are configured in <see cref="TcpaTestFactory"/>
/// and differ from the production placeholder so tests cannot accidentally hit real services.
/// </summary>
internal static class TestApiKeys
{
    /// <summary>
    /// Accepted by <c>ApiKeyAuthFilter</c> for all API endpoints.
    /// Value matches <c>ApiKeys:ValidKeys</c> in <c>appsettings.json</c> ("REPLACE_IN_ENV").
    /// In production this is overridden by environment variable or secrets manager.
    /// Also in <c>ApiKeys:AdminKeys</c> so a single header value satisfies both filters on admin endpoints.
    /// </summary>
    internal const string ValidKey = "REPLACE_IN_ENV";

    /// <summary>
    /// Accepted by <c>AdminApiKeyAuthFilter</c>.
    /// Same placeholder value as ValidKey so one header works for all test scenarios.
    /// </summary>
    internal const string AdminKey = "REPLACE_IN_ENV";

    /// <summary>Header name expected by both auth filters.</summary>
    internal const string HeaderName = "X-Api-Key";
}
