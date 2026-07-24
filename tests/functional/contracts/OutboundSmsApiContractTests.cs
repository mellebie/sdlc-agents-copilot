// contracts/OutboundSmsApiContractTests.cs
// Source: Agent 09b (Drew) | API contract verification for POST /api/v1/messages/outbound
// These tests verify SHAPE and STRUCTURE — not business logic.
// If these tests break, upstream integrators (BizTalk, GCMA, ARM) must be notified.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TCPA.Functional.Tests.Infrastructure;
using Xunit;

namespace TCPA.Functional.Tests.Contracts;

/// <summary>
/// Contract tests for POST /api/v1/messages/outbound.
/// Asserts field names (camelCase), types, and required/optional presence for each response variant.
/// </summary>
[Collection(TcpaTestCollection.Name)]
public class OutboundSmsApiContractTests : FunctionalTestBase
{
    public OutboundSmsApiContractTests(TcpaTestFactory factory) : base(factory) { }

    // ─── Queued response contract ─────────────────────────────────────────────────

    /// <summary>
    /// Queued response must contain: status (string), messageId (string GUID), queuedAt (ISO-8601),
    /// suppressionReason (null). All field names are camelCase.
    /// </summary>
    [Fact]
    public async Task OutboundEndpoint_QueuedResponse_ContainsRequiredFields()
    {
        // Arrange
        await SeedCoolTextAccountAsync(accountNumber: "CT-CONTRACT-001");

        var payload = new
        {
            ToNumber = "+15554440001",
            Body = "Contract test message",
            CoolTextAccountNumber = "CT-CONTRACT-001",
            ApplicationId = "BizTalk",
        };

        // Act
        var response = await Client.PostAsJsonAsync("/api/v1/messages/outbound", payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var root = json!.RootElement;

        // Assert — field presence and types
        root.TryGetProperty("status", out var statusEl).Should().BeTrue("'status' field must be present");
        statusEl.GetString().Should().Be("queued");

        root.TryGetProperty("messageId", out var messageIdEl).Should().BeTrue("'messageId' field must be present");
        messageIdEl.GetString().Should().NotBeNullOrWhiteSpace("messageId must be a non-empty string");
        Guid.TryParse(messageIdEl.GetString(), out _).Should().BeTrue("messageId must be a valid GUID");

        root.TryGetProperty("queuedAt", out var queuedAtEl).Should().BeTrue("'queuedAt' field must be present");
        DateTimeOffset.TryParse(queuedAtEl.GetString(), out _).Should().BeTrue("queuedAt must be a valid ISO-8601 timestamp");

        root.TryGetProperty("suppressionReason", out var suppressionEl).Should().BeTrue("'suppressionReason' field must be present");
        suppressionEl.ValueKind.Should().Be(JsonValueKind.Null, "suppressionReason is null for queued messages");
    }

    // ─── Suppressed response contract ─────────────────────────────────────────────

    /// <summary>
    /// Suppressed response must contain: status="suppressed", messageId (null), queuedAt (null),
    /// suppressionReason="opted-out".
    /// </summary>
    [Fact]
    public async Task OutboundEndpoint_SuppressedResponse_ContainsRequiredFields()
    {
        // Arrange
        await SeedCoolTextAccountAsync(accountNumber: "CT-CONTRACT-001");
        await SeedOptOutStatusAsync("+15554440002", "opted-out");

        var payload = new
        {
            ToNumber = "+15554440002",
            Body = "Contract test message",
            CoolTextAccountNumber = "CT-CONTRACT-001",
            ApplicationId = "BizTalk",
        };

        // Act
        var response = await Client.PostAsJsonAsync("/api/v1/messages/outbound", payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var root = json!.RootElement;

        // Assert
        root.TryGetProperty("status", out var statusEl).Should().BeTrue();
        statusEl.GetString().Should().Be("suppressed");

        root.TryGetProperty("messageId", out var messageIdEl).Should().BeTrue("'messageId' field must be present");
        messageIdEl.ValueKind.Should().Be(JsonValueKind.Null, "messageId is null for suppressed messages");

        root.TryGetProperty("queuedAt", out var queuedAtEl).Should().BeTrue("'queuedAt' field must be present");
        queuedAtEl.ValueKind.Should().Be(JsonValueKind.Null, "queuedAt is null for suppressed messages");

        root.TryGetProperty("suppressionReason", out var suppressionEl).Should().BeTrue();
        suppressionEl.GetString().Should().Be("opted-out");
    }

    // ─── Error response contracts ─────────────────────────────────────────────────

    /// <summary>
    /// 400 Bad Request for an unknown account must return a JSON error object.
    /// </summary>
    [Fact]
    public async Task OutboundEndpoint_UnknownAccount_400HasErrorField()
    {
        var payload = new
        {
            ToNumber = "+15554440003",
            Body = "Contract test message",
            CoolTextAccountNumber = "CT-DOES-NOT-EXIST",
            ApplicationId = "BizTalk",
        };

        var response = await Client.PostAsJsonAsync("/api/v1/messages/outbound", payload);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        json!.RootElement.TryGetProperty("error", out _).Should().BeTrue("400 responses must include an 'error' field");
    }

    /// <summary>
    /// 401 Unauthorized must be returned as a plain HTTP 401, not a JSON error.
    /// (No body contract required — just the status code.)
    /// </summary>
    [Fact]
    public async Task OutboundEndpoint_MissingAuth_Returns401()
    {
        using var anon = CreateUnauthenticatedClient();
        var payload = new
        {
            ToNumber = "+15554440004",
            Body = "Contract test",
            CoolTextAccountNumber = "CT-CONTRACT-001",
            ApplicationId = "BizTalk",
        };

        var response = await anon.PostAsJsonAsync("/api/v1/messages/outbound", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
