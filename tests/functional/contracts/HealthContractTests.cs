// contracts/HealthContractTests.cs
// Source: Agent 09b (Drew) | API contract for GET /api/v1/health (no auth required)
// Verifies the shape of the health check response for load balancers and monitoring tools.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TCPA.Functional.Tests.Infrastructure;
using Xunit;

namespace TCPA.Functional.Tests.Contracts;

/// <summary>
/// Contract tests for GET /api/v1/health.
/// Health check is unauthenticated by design — probes must not require an API key.
/// </summary>
[Collection(TcpaTestCollection.Name)]
public class HealthContractTests : FunctionalTestBase
{
    public HealthContractTests(TcpaTestFactory factory) : base(factory) { }

    // ─── Healthy response contract ────────────────────────────────────────────────

    /// <summary>
    /// Healthy response must contain: status ("healthy"), checks.database ("ok"),
    /// checks.kafka ("ok"), timestamp (ISO-8601). HTTP 200.
    /// </summary>
    [Fact]
    public async Task HealthEndpoint_WhenHealthy_Returns200WithCorrectShape()
    {
        // Act — unauthenticated by design
        using var anon = CreateUnauthenticatedClient();
        var response = await anon.GetAsync("/api/v1/health");

        // Assert — status code
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var root = json!.RootElement;

        // Assert — top-level fields
        root.TryGetProperty("status", out var statusEl).Should().BeTrue("'status' field must be present");
        statusEl.GetString().Should().Be("healthy");

        root.TryGetProperty("timestamp", out var timestampEl).Should().BeTrue("'timestamp' field must be present");
        DateTimeOffset.TryParse(timestampEl.GetString(), out _).Should().BeTrue("timestamp must be a valid ISO-8601 value");

        // Assert — nested checks object
        root.TryGetProperty("checks", out var checksEl).Should().BeTrue("'checks' field must be present");
        checksEl.ValueKind.Should().Be(JsonValueKind.Object);

        checksEl.TryGetProperty("database", out var dbEl).Should().BeTrue("'checks.database' must be present");
        dbEl.GetString().Should().Be("ok");

        checksEl.TryGetProperty("kafka", out var kafkaEl).Should().BeTrue("'checks.kafka' must be present");
        kafkaEl.GetString().Should().Be("ok");
    }

    /// <summary>
    /// Health endpoint is accessible without an API key (no authentication required).
    /// </summary>
    [Fact]
    public async Task HealthEndpoint_NoApiKey_Returns200NotUnauthorized()
    {
        // Arrange — no auth header
        using var anon = CreateUnauthenticatedClient();

        // Act
        var response = await anon.GetAsync("/api/v1/health");

        // Assert — must NOT return 401
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            because: "health probes must not require authentication");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Content-Type of the health response must be application/json.
    /// Load balancer health check parsers depend on JSON content type.
    /// </summary>
    [Fact]
    public async Task HealthEndpoint_ResponseContentType_IsApplicationJson()
    {
        using var anon = CreateUnauthenticatedClient();
        var response = await anon.GetAsync("/api/v1/health");

        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }
}
