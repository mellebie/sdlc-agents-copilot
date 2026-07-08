// TCPA Functional Test Constants
// Purpose: Shared test configuration values used across all functional test classes.
// Source: Agent 09b | Tests/functional infrastructure

namespace TCPA.Api.FunctionalTests.Infrastructure;

/// <summary>
/// Shared constants used across all TCPA functional tests.
/// These values must match what is configured in <see cref="TcpaFunctionalTestFactory"/>.
/// </summary>
public static class TcpaTestConstants
{
    /// <summary>API key injected into <c>X-API-Key</c> header for outbound SMS requests.</summary>
    public const string ApiKey = "test-api-key-12345";

    /// <summary>HMAC-SHA256 secret used to sign inbound webhook payloads.</summary>
    public const string WebhookSecret = "test-webhook-secret";

    /// <summary>Header name for Cool Text HMAC signature (from CoolTextWebhookValidator).</summary>
    public const string SignatureHeaderName = "X-CoolText-Signature";

    /// <summary>Header name for API key authentication.</summary>
    public const string ApiKeyHeaderName = "X-API-Key";
}
