// smoke/TcpaApiSmokeTests.cs
// Source: Agent 09b (Drew) | Post-deployment smoke verification | All critical paths
// Smoke tests must:
//   - Complete in under 2 minutes total
//   - Be safe to run against any environment (no data mutation — read-only or idempotent)
//   - Produce a clear PASS/FAIL with actionable failure messages

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TCPA.Functional.Tests.Infrastructure;
using Xunit;

namespace TCPA.Functional.Tests.Smoke;

/// <summary>
/// Smoke tests for the TCPA Compliance API.
/// These tests run against the in-process WebApplicationFactory and verify that all
/// critical paths are alive and authentication layers are functioning.
/// They are designed to be safe and fast — no heavy data setup, no external dependencies.
/// </summary>
[Collection(TcpaTestCollection.Name)]
public class TcpaApiSmokeTests : FunctionalTestBase
{
    public TcpaApiSmokeTests(TcpaTestFactory factory) : base(factory) { }

    // ─── Health check ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Smoke-001: Health endpoint is reachable and returns 200 (no auth required).
    /// This is the first check any monitoring system should make.
    /// </summary>
    [Fact]
    public async Task Smoke_HealthEndpoint_Returns200()
    {
        // Arrange — no auth needed
        using var anon = CreateUnauthenticatedClient();

        // Act
        var response = await anon.GetAsync("/api/v1/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "the health endpoint must return 200 for the API to be considered live");

        var body = await response.Content.ReadFromJsonAsync<HealthShape>();
        body!.Status.Should().BeOneOf(new[] { "healthy", "degraded" },
            "status must be a known value");
        body.Checks.Should().NotBeNull("health checks sub-object must be present");
    }

    // ─── API key enforcement ──────────────────────────────────────────────────────

    /// <summary>
    /// Smoke-002: Inbound webhook endpoint rejects unauthenticated requests with 401.
    /// Verifies that the API key filter is active.
    /// </summary>
    [Fact]
    public async Task Smoke_InboundWebhook_RejectsUnauthenticated_Returns401()
    {
        // Arrange — no auth
        using var anon = CreateUnauthenticatedClient();
        var payload = new
        {
            From = "+15550000001",
            To = "CT-SMOKE",
            Body = "STOP",
            Provider = "cooltext",
            MessageId = $"smoke-inb-{Guid.NewGuid():N}",
            Timestamp = DateTimeOffset.UtcNow,
        };

        // Act
        var response = await anon.PostAsJsonAsync("/webhook/inbound", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "the API key filter must reject unauthenticated requests to the inbound endpoint");
    }

    /// <summary>
    /// Smoke-003: Outbound messages endpoint rejects unauthenticated requests with 401.
    /// </summary>
    [Fact]
    public async Task Smoke_OutboundMessages_RejectsUnauthenticated_Returns401()
    {
        using var anon = CreateUnauthenticatedClient();
        var payload = new
        {
            ToNumber = "+15550000002",
            Body = "Smoke test",
            CoolTextAccountNumber = "CT-SMOKE",
            ApplicationId = "BizTalk",
        };

        var response = await anon.PostAsJsonAsync("/api/v1/messages/outbound", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "the API key filter must reject unauthenticated requests to the outbound endpoint");
    }

    /// <summary>
    /// Smoke-004: Admin endpoint rejects unauthenticated requests with 401.
    /// </summary>
    [Fact]
    public async Task Smoke_AdminEndpoint_RejectsUnauthenticated_Returns401()
    {
        using var anon = CreateUnauthenticatedClient();
        var payload = new
        {
            PhoneNumber = "+15550000003",
            Reason = "Smoke test",
            AgentId = "smoke-agent",
        };

        var response = await anon.PostAsJsonAsync("/api/v1/admin/reopt-in", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "the admin endpoint must reject unauthenticated requests");
    }

    // ─── Authenticated happy-path smoke ───────────────────────────────────────────

    /// <summary>
    /// Smoke-005: Inbound webhook with a valid API key accepts the request.
    /// Uses a seeded account to get past account validation.
    /// Asserts a non-5xx response (400 is also acceptable — account not registered is expected in some envs).
    /// </summary>
    [Fact]
    public async Task Smoke_InboundWebhook_ValidApiKey_IsProcessed()
    {
        // Arrange — seed an account so we can get a 200 (not a 400 for unknown account)
        await SeedCoolTextAccountAsync(accountNumber: "CT-SMOKE-001");

        var payload = new
        {
            From = "+15550000010",
            To = "CT-SMOKE-001",
            Body = "STOP",
            Provider = "cooltext",
            MessageId = $"smoke-auth-{Guid.NewGuid():N}",
            Timestamp = DateTimeOffset.UtcNow,
        };

        // Act
        var response = await Client.PostAsJsonAsync("/webhook/inbound", payload);

        // Assert — accepted by the API (200 = processed, 400 = account validation — either is non-5xx)
        ((int)response.StatusCode).Should().BeLessThan(500,
            because: "the inbound endpoint must not return 5xx for a properly authenticated request");
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "a seeded account should result in a 200 received response");
    }

    /// <summary>
    /// Smoke-006: Outbound message with a valid API key is accepted.
    /// Uses a seeded account and a number with no opt-out record.
    /// </summary>
    [Fact]
    public async Task Smoke_OutboundMessage_ValidApiKey_IsQueued()
    {
        // Arrange
        await SeedCoolTextAccountAsync(accountNumber: "CT-SMOKE-001");

        // Decimal digits only — GUID.N hex chars (a-f) fail E.164 \d validation
        var toNumber = $"+1555{Random.Shared.Next(1_000_000, 9_999_999):D7}";
        var payload = new
        {
            ToNumber = toNumber,
            Body = "Smoke test message — safe to ignore.",
            CoolTextAccountNumber = "CT-SMOKE-001",
            ApplicationId = "BizTalk",
        };

        // Act
        var response = await Client.PostAsJsonAsync("/api/v1/messages/outbound", payload);

        // Assert
        ((int)response.StatusCode).Should().BeLessThan(500,
            because: "authenticated outbound requests must not return 5xx");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ─── Local response shape helpers ─────────────────────────────────────────────
    private record HealthShape(string Status, HealthChecksShape? Checks, DateTimeOffset Timestamp);
    private record HealthChecksShape(string Database, string Kafka);
}
