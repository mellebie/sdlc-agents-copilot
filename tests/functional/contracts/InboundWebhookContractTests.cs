// contracts/InboundWebhookContractTests.cs
// Source: Agent 09b (Drew) | API contract for POST /webhook/inbound
// Verifies the response shape expected by Cool Text / Twilio webhook integrations.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TCPA.Functional.Tests.Infrastructure;
using Xunit;

namespace TCPA.Functional.Tests.Contracts;

/// <summary>
/// Contract tests for POST /webhook/inbound.
/// Field names, types, and required presence — not business logic.
/// </summary>
[Collection(TcpaTestCollection.Name)]
public class InboundWebhookContractTests : FunctionalTestBase
{
    public InboundWebhookContractTests(TcpaTestFactory factory) : base(factory) { }

    // ─── Success response contract ────────────────────────────────────────────────

    /// <summary>
    /// Success response must contain: status ("received"), internalId (non-empty GUID string).
    /// Both field names are camelCase. HTTP 200.
    /// </summary>
    [Fact]
    public async Task InboundWebhook_SuccessResponse_ContainsRequiredFields()
    {
        // Arrange
        await SeedCoolTextAccountAsync(accountNumber: "CT-WH-CONTRACT-001");

        var payload = new
        {
            From = "+15554450001",
            To = "CT-WH-CONTRACT-001",
            Body = "STOP",
            Provider = "cooltext",
            MessageId = $"wh-contract-{Guid.NewGuid():N}",
            Timestamp = DateTimeOffset.UtcNow,
        };

        // Act
        var response = await Client.PostAsJsonAsync("/webhook/inbound", payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var root = json!.RootElement;

        // Assert — field presence
        root.TryGetProperty("status", out var statusEl).Should().BeTrue("'status' field must be present");
        statusEl.GetString().Should().Be("received");

        root.TryGetProperty("internalId", out var idEl).Should().BeTrue("'internalId' field must be present");
        var internalId = idEl.GetString();
        internalId.Should().NotBeNullOrWhiteSpace("internalId must be a non-empty string");
        Guid.TryParse(internalId, out _).Should().BeTrue("internalId must be a valid GUID");
    }

    // ─── Error response contracts ─────────────────────────────────────────────────

    /// <summary>
    /// Unknown account → HTTP 400 with an 'error' field.
    /// </summary>
    [Fact]
    public async Task InboundWebhook_UnknownAccount_Returns400WithErrorField()
    {
        var payload = new
        {
            From = "+15554450002",
            To = "CT-DOES-NOT-EXIST",
            Body = "STOP",
            Provider = "cooltext",
            MessageId = $"wh-unknown-{Guid.NewGuid():N}",
            Timestamp = DateTimeOffset.UtcNow,
        };

        var response = await Client.PostAsJsonAsync("/webhook/inbound", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        json!.RootElement.TryGetProperty("error", out _).Should().BeTrue("400 must include an 'error' field");
    }

    /// <summary>
    /// Missing From field → HTTP 400 (model validation — required field).
    /// </summary>
    [Fact]
    public async Task InboundWebhook_MissingRequiredField_Returns400()
    {
        await SeedCoolTextAccountAsync(accountNumber: "CT-WH-CONTRACT-001");

        var payload = new
        {
            // From is omitted
            To = "CT-WH-CONTRACT-001",
            Body = "STOP",
            Provider = "cooltext",
            MessageId = $"wh-missing-{Guid.NewGuid():N}",
            Timestamp = DateTimeOffset.UtcNow,
        };

        var response = await Client.PostAsJsonAsync("/webhook/inbound", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Missing X-Api-Key → HTTP 401 (status code is the full contract for auth failures).
    /// </summary>
    [Fact]
    public async Task InboundWebhook_MissingAuth_Returns401()
    {
        using var anon = CreateUnauthenticatedClient();
        var payload = new
        {
            From = "+15554450003",
            To = "CT-WH-CONTRACT-001",
            Body = "STOP",
            Provider = "cooltext",
            MessageId = $"wh-noauth-{Guid.NewGuid():N}",
            Timestamp = DateTimeOffset.UtcNow,
        };

        var response = await anon.PostAsJsonAsync("/webhook/inbound", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
