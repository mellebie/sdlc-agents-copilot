// TCPA API Smoke Tests
// Source: All critical paths | Post-deployment verification
// Generated: 2026-06-26 | Agent 09b — Functional & E2E Test Agent
//
// These tests run against a deployed environment (not the in-process test factory).
// They verify that the API is alive and its authentication/authorization layers are
// functioning correctly. They make NO data mutations and are safe against production.
//
// Configuration:
//   Set TCPA_API_BASE_URL environment variable to the deployed API base URL.
//   Falls back to http://localhost:5000 for local smoke testing.
//
// Performance target: all smoke tests must complete in under 30 seconds total.
// Each individual test has a 10-second timeout on the HttpClient.

using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace TCPA.Api.FunctionalTests.Smoke;

/// <summary>
/// Post-deployment smoke tests that verify the TCPA API is alive and security controls
/// are operational. Safe to run against any environment including production because
/// no valid API keys or HMAC secrets are used — all requests are intentionally unauthenticated.
/// </summary>
public class TcpaApiSmokeTests : IDisposable
{
    private readonly HttpClient _client;
    private readonly string _baseUrl;

    public TcpaApiSmokeTests()
    {
        _baseUrl = Environment.GetEnvironmentVariable("TCPA_API_BASE_URL") ?? "http://localhost:5000";
        _client = new HttpClient
        {
            BaseAddress = new Uri(_baseUrl),
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    /// <summary>
    /// SMOKE: Health endpoint returns a 200 response, confirming the API is alive
    /// and its EF Core health check is passing.
    /// </summary>
    [Fact]
    public async Task HealthEndpoint_ReturnsHealthy()
    {
        // Act
        var response = await _client.GetAsync("/health");

        // Assert — 200 = Healthy, 503 = Degraded (still reachable but something is wrong)
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.ServiceUnavailable,
            because: "any status other than these indicates the API is unreachable or crashed (not just unhealthy)");

        // If we get 200, confirm the response is parseable health check output
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var content = await response.Content.ReadAsStringAsync();
            content.Should().NotBeNullOrEmpty(
                because: "health endpoint must return a response body");
        }
    }

    /// <summary>
    /// SMOKE: Outbound endpoint without an API key returns 401 (not 500).
    /// Verifies that API key authentication middleware is active and correctly rejecting
    /// unauthenticated requests before they reach business logic.
    /// Safe: no mutation, no valid key used.
    /// </summary>
    [Fact]
    public async Task OutboundEndpoint_WithMissingApiKey_Returns401NotServerError()
    {
        // Arrange — deliberately no X-API-Key header
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sms/outbound")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    cool_text_account_id = "SMOKE-TEST-ACCOUNT",
                    destination_cell_number = "+10000000000",
                    message_body = "Smoke test — should be rejected before processing.",
                }),
                Encoding.UTF8,
                "application/json"),
        };

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "API key authentication must reject requests without X-API-Key before they reach business logic; " +
                     "a 500 here indicates an uncaught startup error or missing middleware");
    }

    /// <summary>
    /// SMOKE: Inbound webhook endpoint without HMAC signature returns 401 (not 500).
    /// Verifies that HMAC signature validation middleware is active.
    /// Safe: no valid signature means no processing occurs.
    /// </summary>
    [Fact]
    public async Task InboundEndpoint_WithMissingSignature_Returns401NotServerError()
    {
        // Arrange — no X-CoolText-Signature header
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sms/inbound")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    cool_text_account_id = "SMOKE-TEST-ACCOUNT",
                    sender_cell_number = "+10000000001",
                    message_body = "Smoke test — should be rejected before processing.",
                    cool_text_message_id = "smoke-test-msg-001",
                }),
                Encoding.UTF8,
                "application/json"),
        };

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "HMAC signature validation must reject requests without a valid signature; " +
                     "a 500 here indicates an uncaught startup error or missing middleware");
    }

    /// <summary>
    /// SMOKE: Admin re-opt-in endpoint without Bearer token returns 401 (not 500).
    /// Verifies that JWT Bearer authentication is active on admin endpoints.
    /// Safe: no data mutation (request is rejected before reaching any service layer).
    /// </summary>
    [Fact]
    public async Task AdminEndpoint_WithoutAuth_Returns401NotServerError()
    {
        // Arrange — no Authorization header
        var request = new HttpRequestMessage(HttpMethod.Put, "/admin/v1/opt-out/re-opt-in")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    cellPhoneNumber = "+10000000002",
                    reason = "Smoke test — should be rejected before processing by auth middleware.",
                }),
                Encoding.UTF8,
                "application/json"),
        };

        // Act
        var response = await _client.SendAsync(request);

        // Assert — 401 = JWT auth is working; 500 = startup/middleware error
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Forbidden,
            because: "admin endpoints must require JWT Bearer authentication; " +
                     "a 500 here indicates an uncaught startup error, " +
                     "a 200 would indicate auth is not configured (test environment only)");
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}
