// Outbound SMS API Contract Tests
// Source: SPEC-001, SPEC-002, SPEC-009 | API contract verification for upstream integrators
// Generated: 2026-06-26 | Agent 09b — Functional & E2E Test Agent
//
// These tests verify the SHAPE and STRUCTURE of API responses — not business logic.
// They act as the contract that BizTalk, GCMA, KMI, and ARM integration teams depend on.
// If these tests break, upstream integrators must be notified before release.

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Moq;
using TCPA.Api.Domain;
using TCPA.Api.FunctionalTests.Infrastructure;
using TCPA.Api.Infrastructure.CoolText;

namespace TCPA.Api.FunctionalTests.Contracts;

/// <summary>
/// API contract tests for the outbound SMS endpoint (/api/v1/sms/outbound) and
/// inbound webhook endpoint (/api/v1/sms/inbound). Verifies field presence, types,
/// and required/optional field semantics as consumed by upstream SCG application teams.
/// </summary>
public class OutboundSmsApiContractTests : FunctionalTestBase, IClassFixture<TcpaFunctionalTestFactory>
{
    private const string ContractAccountId = "CT-CONTRACT-TEST-001";

    public OutboundSmsApiContractTests(TcpaFunctionalTestFactory factory)
        : base(factory)
    {
    }

    /// <summary>
    /// CONTRACT: FORWARDED response must contain status, cool_text_message_id, and
    /// processed_timestamp fields with correct types.
    /// Upstream integrators use cool_text_message_id to correlate delivery receipts.
    /// </summary>
    [Fact]
    public async Task OutboundSmsResponse_ForwardedResponse_ContainsRequiredFields()
    {
        // Arrange
        await SeedApplicationRegistrationAsync(ContractAccountId, "Contract Test App");

        Factory.MockCoolTextClient
            .Setup(c => c.SendSmsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendSmsResult { MessageId = "ct-contract-msg-001", Status = "sent" });

        var request = MakeApiKeyRequest(HttpMethod.Post, "/api/v1/sms/outbound", new
        {
            cool_text_account_id = ContractAccountId,
            destination_cell_number = "+12025551001",
            message_body = "Contract test message.",
        });

        // Act
        var response = await Client.SendAsync(request);
        var body = await ReadJsonAsync(response);

        // Assert — required fields in FORWARDED response
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        body.TryGetProperty("status", out var statusProp).Should().BeTrue(
            because: "FORWARDED response must contain a 'status' field");
        statusProp.GetString().Should().Be("FORWARDED");

        body.TryGetProperty("cool_text_message_id", out var messageIdProp).Should().BeTrue(
            because: "FORWARDED response must contain 'cool_text_message_id' for delivery correlation");
        messageIdProp.ValueKind.Should().NotBe(JsonValueKind.Null,
            because: "cool_text_message_id must be present when a message is forwarded");

        body.TryGetProperty("processed_timestamp", out var timestampProp).Should().BeTrue(
            because: "all responses must include a 'processed_timestamp' for audit correlation");
        var timestampString = timestampProp.GetString();
        timestampString.Should().NotBeNullOrEmpty();
        DateTimeOffset.TryParse(timestampString, out _).Should().BeTrue(
            because: "processed_timestamp must be a parseable ISO 8601 datetime");
    }

    /// <summary>
    /// CONTRACT: SUPPRESSED response must contain status, suppression_reason, and
    /// processed_timestamp. cool_text_message_id must be null or absent (no message sent).
    /// </summary>
    [Fact]
    public async Task OutboundSmsResponse_SuppressedResponse_ContainsRequiredFields()
    {
        // Arrange
        await SeedApplicationRegistrationAsync(ContractAccountId, "Contract Test App");
        await SeedOptOutRecordAsync("+12025551002", OptOutStatus.OptOut);

        var request = MakeApiKeyRequest(HttpMethod.Post, "/api/v1/sms/outbound", new
        {
            cool_text_account_id = ContractAccountId,
            destination_cell_number = "+12025551002",
            message_body = "Contract test message.",
        });

        // Act
        var response = await Client.SendAsync(request);
        var body = await ReadJsonAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        body.TryGetProperty("status", out var statusProp).Should().BeTrue();
        statusProp.GetString().Should().Be("SUPPRESSED");

        body.TryGetProperty("suppression_reason", out var reasonProp).Should().BeTrue(
            because: "SUPPRESSED response must include suppression_reason for caller audit logging");
        reasonProp.GetString().Should().Be("OPT_OUT");

        body.TryGetProperty("processed_timestamp", out var timestampProp).Should().BeTrue();
        DateTimeOffset.TryParse(timestampProp.GetString(), out _).Should().BeTrue();

        // cool_text_message_id should be null or absent for suppressed messages
        if (body.TryGetProperty("cool_text_message_id", out var messageIdProp))
        {
            messageIdProp.ValueKind.Should().Be(JsonValueKind.Null,
                because: "no Cool Text message ID exists for a suppressed message");
        }
    }

    /// <summary>
    /// CONTRACT: 400 Bad Request must return a ProblemDetails-compatible response
    /// with field-level validation errors that integrators can parse and log.
    /// </summary>
    [Fact]
    public async Task OutboundSmsResponse_400_HasFieldLevelErrors()
    {
        // Arrange — omit destination_cell_number to trigger validation failure
        var request = MakeApiKeyRequest(HttpMethod.Post, "/api/v1/sms/outbound", new
        {
            cool_text_account_id = ContractAccountId,
            // destination_cell_number intentionally omitted
            message_body = "Contract test message.",
        });

        // Act
        var response = await Client.SendAsync(request);
        var body = await ReadJsonAsync(response);

        // Assert — ProblemDetails structure
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        body.TryGetProperty("errors", out var errors).Should().BeTrue(
            because: "ASP.NET Core ModelState validation returns ProblemDetails with 'errors' dictionary");

        // The errors dictionary must contain the invalid field name (case-insensitive)
        var errorKeys = errors.EnumerateObject().Select(p => p.Name.ToLower()).ToList();
        errorKeys.Should().Contain(
            key => key.Contains("destination_cell_number") || key.Contains("destinationcellnumber"),
            because: "field-level errors must identify which field failed validation");
    }

    /// <summary>
    /// CONTRACT: Inbound webhook acknowledgement must always return { "received": true }.
    /// This is the contract that the Cool Text platform expects to prevent retry escalation.
    /// </summary>
    [Fact]
    public async Task InboundAcknowledgement_ContainsReceivedTrue()
    {
        // Arrange
        await SeedApplicationRegistrationAsync(ContractAccountId, "Contract Test App");

        var request = MakeHmacSignedRequest(HttpMethod.Post, "/api/v1/sms/inbound", new
        {
            cool_text_account_id = ContractAccountId,
            sender_cell_number = "+12025551003",
            message_body = "Hello, what are my payment options?",
            cool_text_message_id = "inbound-contract-001",
        });

        // Act
        var response = await Client.SendAsync(request);
        var body = await ReadJsonAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        body.TryGetProperty("received", out var receivedProp).Should().BeTrue(
            because: "the inbound acknowledgement contract requires a 'received' field");
        receivedProp.GetBoolean().Should().BeTrue(
            because: "{ \"received\": true } is the contract Cool Text uses to confirm delivery");
    }

    /// <summary>
    /// CONTRACT: UNREGISTERED_ACCOUNT response must include status field.
    /// Upstream integrators must be able to distinguish unregistered from suppressed responses.
    /// </summary>
    [Fact]
    public async Task OutboundSmsResponse_UnregisteredAccount_ContainsStatusField()
    {
        // Arrange — no application registration seeded for this account
        var request = MakeApiKeyRequest(HttpMethod.Post, "/api/v1/sms/outbound", new
        {
            cool_text_account_id = "CT-CONTRACT-UNREG-999",
            destination_cell_number = "+12025551004",
            message_body = "Test message.",
        });

        // Act
        var response = await Client.SendAsync(request);
        var body = await ReadJsonAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        body.TryGetProperty("status", out var statusProp).Should().BeTrue();
        statusProp.GetString().Should().Be("UNREGISTERED_ACCOUNT");
    }
}
