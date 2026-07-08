// Outbound SMS Compliance Journey Tests
// Source: STORY-002 | SPEC-001, SPEC-002, SPEC-009 | AC-001 through AC-007
// Generated: 2026-06-26 | Agent 09b — Functional & E2E Test Agent
// Risk Level: HIGH-RISK — Full happy + 3 unhappy paths required

using System.Net;
using FluentAssertions;
using Moq;
using TCPA.Api.Domain;
using TCPA.Api.FunctionalTests.Infrastructure;
using TCPA.Api.Infrastructure.CoolText;

namespace TCPA.Api.FunctionalTests.Journeys;

/// <summary>
/// User journey tests for the outbound SMS TCPA compliance gate.
/// Verifies the complete request path from HTTP → ApiKeyAuthFilter → OutboundSmsGate
/// → ICoolTextClient (mocked) → SmsResponse, using a real in-process ASP.NET Core host.
/// Tests operate against the full middleware pipeline; no components are mocked except
/// the external Cool Text HTTP client.
/// </summary>
public class OutboundSmsComplianceJourneyTests : FunctionalTestBase, IClassFixture<TcpaFunctionalTestFactory>
{
    // Each test uses a unique phone number to prevent cross-test state pollution
    // when multiple tests run against the shared InMemory database instance.
    private const string OptedInNumber = "+12025550001";
    private const string OptedOutNumber = "+12025550002";
    private const string NoRecordNumber = "+12025550003";

    private const string RegisteredAccountId = "CT-GCMA-TEST-001";
    private const string UnregisteredAccountId = "CT-UNKNOWN-TEST-999";

    public OutboundSmsComplianceJourneyTests(TcpaFunctionalTestFactory factory)
        : base(factory)
    {
    }

    /// <summary>
    /// AC-001 (Happy Path): An opted-in number receives the message via Cool Text.
    /// BR-001: No opt-out record means OPT_IN by default.
    /// </summary>
    [Fact]
    public async Task OutboundSmsCompliance_OptedInNumber_ForwardsMessage()
    {
        // Arrange
        await SeedApplicationRegistrationAsync(RegisteredAccountId, "GCMA Test App");
        await SeedOptOutRecordAsync(OptedInNumber, OptOutStatus.OptIn);

        Factory.MockCoolTextClient
            .Setup(c => c.SendSmsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendSmsResult { MessageId = "ct-msg-001", Status = "sent" });

        var request = MakeApiKeyRequest(HttpMethod.Post, "/api/v1/sms/outbound", new
        {
            cool_text_account_id = RegisteredAccountId,
            destination_cell_number = OptedInNumber,
            message_body = "Your monthly bill is ready. Visit scg.com to pay.",
        });

        // Act
        var response = await Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(response);
        body.GetProperty("status").GetString().Should().Be("FORWARDED");

        Factory.MockCoolTextClient.Verify(
            c => c.SendSmsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// AC-002 (Unhappy Path): An opted-out number is suppressed; Cool Text is never called.
    /// SPEC-009: Suppression must be logged as a blocked-outbound audit entry.
    /// </summary>
    [Fact]
    public async Task OutboundSmsCompliance_OptedOutNumber_SuppressesMessage()
    {
        // Arrange
        await SeedApplicationRegistrationAsync(RegisteredAccountId, "GCMA Test App");
        await SeedOptOutRecordAsync(OptedOutNumber, OptOutStatus.OptOut);

        var request = MakeApiKeyRequest(HttpMethod.Post, "/api/v1/sms/outbound", new
        {
            cool_text_account_id = RegisteredAccountId,
            destination_cell_number = OptedOutNumber,
            message_body = "Your monthly bill is ready.",
        });

        // Act
        var response = await Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(response);
        body.GetProperty("status").GetString().Should().Be("SUPPRESSED");
        body.GetProperty("suppression_reason").GetString().Should().Be("OPT_OUT");

        // Critical: Cool Text must NEVER be called for an opted-out number
        Factory.MockCoolTextClient.Verify(
            c => c.SendSmsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// AC-003 (BR-001): A number with no opt-out record is treated as OPT_IN and forwarded.
    /// This is the "unknown = opted-in" default behavior required by BR-001.
    /// </summary>
    [Fact]
    public async Task OutboundSmsCompliance_NoStatusRecord_DefaultsToOptIn_Forwards()
    {
        // Arrange — no opt-out record seeded for this number
        await SeedApplicationRegistrationAsync(RegisteredAccountId, "GCMA Test App");

        Factory.MockCoolTextClient
            .Setup(c => c.SendSmsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendSmsResult { MessageId = "ct-msg-002", Status = "sent" });

        var request = MakeApiKeyRequest(HttpMethod.Post, "/api/v1/sms/outbound", new
        {
            cool_text_account_id = RegisteredAccountId,
            destination_cell_number = NoRecordNumber,
            message_body = "Payment reminder: your bill is due.",
        });

        // Act
        var response = await Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(response);
        body.GetProperty("status").GetString().Should().Be("FORWARDED",
            because: "BR-001 requires no-record to default to OPT_IN");
    }

    /// <summary>
    /// AC-004 (Unhappy Path): Missing X-API-Key header returns 401.
    /// </summary>
    [Fact]
    public async Task OutboundSmsCompliance_MissingApiKey_Returns401()
    {
        // Arrange — no API key header
        var request = new System.Net.Http.HttpRequestMessage(HttpMethod.Post, "/api/v1/sms/outbound");
        request.Content = System.Net.Http.Json.JsonContent.Create(new
        {
            cool_text_account_id = RegisteredAccountId,
            destination_cell_number = "+12025550010",
            message_body = "Test message.",
        });

        // Act
        var response = await Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// AC-005 (Unhappy Path): Wrong API key returns 401.
    /// </summary>
    [Fact]
    public async Task OutboundSmsCompliance_WrongApiKey_Returns401()
    {
        // Arrange
        var request = new System.Net.Http.HttpRequestMessage(HttpMethod.Post, "/api/v1/sms/outbound");
        request.Headers.Add(TcpaTestConstants.ApiKeyHeaderName, "completely-wrong-key");
        request.Content = System.Net.Http.Json.JsonContent.Create(new
        {
            cool_text_account_id = RegisteredAccountId,
            destination_cell_number = "+12025550011",
            message_body = "Test message.",
        });

        // Act
        var response = await Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// AC-006 (Unhappy Path): A destination cell number not in E.164 format returns 400.
    /// OutboundSmsRequest validates destination_cell_number with an E.164 regex.
    /// </summary>
    [Fact]
    public async Task OutboundSmsCompliance_InvalidE164Number_Returns400()
    {
        // Arrange — "12025551234" is missing the leading + required by E.164
        var request = MakeApiKeyRequest(HttpMethod.Post, "/api/v1/sms/outbound", new
        {
            cool_text_account_id = RegisteredAccountId,
            destination_cell_number = "12025551234",
            message_body = "Test message.",
        });

        // Act
        var response = await Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// AC-007 (BR-004): An unregistered Cool Text account ID still returns 200 but with
    /// UNREGISTERED_ACCOUNT status. The message is NOT forwarded (cannot route without registration).
    /// </summary>
    [Fact]
    public async Task OutboundSmsCompliance_UnregisteredAccount_Returns200WithUnregisteredStatus()
    {
        // Arrange — no application registration for this account
        var request = MakeApiKeyRequest(HttpMethod.Post, "/api/v1/sms/outbound", new
        {
            cool_text_account_id = UnregisteredAccountId,
            destination_cell_number = "+12025550012",
            message_body = "Test message.",
        });

        // Act
        var response = await Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(response);
        body.GetProperty("status").GetString().Should().Be("UNREGISTERED_ACCOUNT");
    }
}
