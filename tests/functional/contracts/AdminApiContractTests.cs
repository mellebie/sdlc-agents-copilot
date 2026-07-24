// contracts/AdminApiContractTests.cs
// Source: Agent 09b (Drew) | API contract for POST /api/v1/admin/reopt-in
// Verifies the shape of admin API responses consumed by helpdesk tooling.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TCPA.Functional.Tests.Infrastructure;
using Xunit;

namespace TCPA.Functional.Tests.Contracts;

/// <summary>
/// Contract tests for POST /api/v1/admin/reopt-in.
/// Asserts field names (camelCase), types, and required presence.
/// </summary>
[Collection(TcpaTestCollection.Name)]
public class AdminApiContractTests : FunctionalTestBase
{
    public AdminApiContractTests(TcpaTestFactory factory) : base(factory) { }

    // ─── Success response contract ────────────────────────────────────────────────

    /// <summary>
    /// Successful re-opt-in response must contain:
    /// reOptInId (long > 0), phoneNumber (E.164 string), status ("opted-in"), effectiveAt (ISO-8601).
    /// All field names are camelCase.
    /// </summary>
    [Fact]
    public async Task AdminReOptIn_SuccessResponse_ContainsAllRequiredFields()
    {
        // Arrange
        const string phoneNumber = "+15554440101";

        var payload = new
        {
            PhoneNumber = phoneNumber,
            Reason = "Customer called to re-opt-in — verified via account PIN.",
            AgentId = "helpdesk-agent-contract-001",
        };

        // Act
        var response = await Client.PostAsJsonAsync("/api/v1/admin/reopt-in", payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var root = json!.RootElement;

        // Assert — field presence and types
        root.TryGetProperty("reOptInId", out var idEl).Should().BeTrue("'reOptInId' field must be present");
        idEl.ValueKind.Should().Be(JsonValueKind.Number, "reOptInId must be numeric");
        idEl.GetInt64().Should().BeGreaterThan(0, "reOptInId must be a positive audit record ID");

        root.TryGetProperty("phoneNumber", out var phoneEl).Should().BeTrue("'phoneNumber' field must be present");
        phoneEl.GetString().Should().Be(phoneNumber, "phoneNumber must echo back the submitted value");

        root.TryGetProperty("status", out var statusEl).Should().BeTrue("'status' field must be present");
        statusEl.GetString().Should().Be("opted-in");

        root.TryGetProperty("effectiveAt", out var effectiveAtEl).Should().BeTrue("'effectiveAt' field must be present");
        DateTimeOffset.TryParse(effectiveAtEl.GetString(), out var effectiveAt).Should().BeTrue("effectiveAt must be a valid ISO-8601 timestamp");
        effectiveAt.Should().BeCloseTo(DateTimeOffset.UtcNow, precision: TimeSpan.FromMinutes(1));
    }

    // ─── Error response contracts ─────────────────────────────────────────────────

    /// <summary>
    /// Missing X-Api-Key → HTTP 401 (no body contract required; status code is the contract).
    /// </summary>
    [Fact]
    public async Task AdminReOptIn_MissingAuth_Returns401()
    {
        using var anon = CreateUnauthenticatedClient();
        var payload = new
        {
            PhoneNumber = "+15554440102",
            Reason = "Valid reason",
            AgentId = "agent-001",
        };

        var response = await anon.PostAsJsonAsync("/api/v1/admin/reopt-in", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Invalid phone format → HTTP 400 with a ProblemDetails-style body.
    /// </summary>
    [Fact]
    public async Task AdminReOptIn_ValidationError_Returns400WithProblemDetails()
    {
        var payload = new
        {
            PhoneNumber = "not-e164",  // invalid format
            Reason = "Valid reason",
            AgentId = "agent-001",
        };

        var response = await Client.PostAsJsonAsync("/api/v1/admin/reopt-in", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The response should be a ProblemDetails object (ASP.NET Core default for model validation)
        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrEmpty("400 must include a body describing what was invalid");
    }
}
