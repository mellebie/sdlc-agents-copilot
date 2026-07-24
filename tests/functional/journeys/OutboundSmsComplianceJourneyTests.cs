// journeys/OutboundSmsComplianceJourneyTests.cs
// Source: Agent 09b (Drew) | STORY-002 (Outbound SMS compliance gate) | AC-001 through AC-007
// SPEC-006, SPEC-007, BR-018 through BR-023 — Queue-time opt-out check and Kafka dispatch

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using NSubstitute;
using TCPA.Api.Messaging;
using TCPA.Functional.Tests.Infrastructure;
using Xunit;

namespace TCPA.Functional.Tests.Journeys;

/// <summary>
/// Journey tests for POST /api/v1/messages/outbound.
/// Verifies the queue-time opt-out check, idempotency, account validation, and auth.
/// </summary>
[Collection(TcpaTestCollection.Name)]
public class OutboundSmsComplianceJourneyTests : FunctionalTestBase
{
    public OutboundSmsComplianceJourneyTests(TcpaTestFactory factory) : base(factory) { }

    // ─── Happy path — queued ──────────────────────────────────────────────────────

    /// <summary>
    /// AC-001 (Happy Path): Opted-in number with valid account → HTTP 200 "queued",
    /// non-empty messageId, non-null queuedAt. Kafka publish called once.
    /// </summary>
    [Fact]
    public async Task OutboundMessage_OptedInNumber_Returns200Queued()
    {
        // Arrange
        await SeedCoolTextAccountAsync(accountNumber: "CT-OUTB-001");
        // No OptOutStatus record seeded → defaults to "opted-in"

        var payload = new
        {
            ToNumber = "+15551110001",
            Body = "Your gas bill is ready. Reply STOP to opt out.",
            CoolTextAccountNumber = "CT-OUTB-001",
            ApplicationId = "BizTalk",
            CorrelationId = $"corr-happy-{Guid.NewGuid():N}",
        };

        // Act
        var response = await Client.PostAsJsonAsync("/api/v1/messages/outbound", payload);

        // Assert — HTTP
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OutboundResponseShape>();
        body!.Status.Should().Be("queued");
        body.MessageId.Should().NotBeNullOrWhiteSpace("a message GUID must be returned");
        body.QueuedAt.Should().NotBeNull("a queue timestamp must be returned");
        body.SuppressionReason.Should().BeNull("non-suppressed messages have no suppression reason");

        // Assert — Kafka publish
        await Factory.MockPublisher.Received(1)
            .PublishOutboundAsync(
                Arg.Is<OutboundMessageEvent>(e =>
                    e.ToNumber == "+15551110001" &&
                    e.CoolTextAccountNumber == "CT-OUTB-001"),
                Arg.Any<CancellationToken>());
    }

    // ─── Opt-out suppression ──────────────────────────────────────────────────────

    /// <summary>
    /// AC-002 (Unhappy Path): Opted-out number → HTTP 200 "suppressed",
    /// null messageId, null queuedAt, suppressionReason = "opted-out".
    /// Kafka must NOT be published.
    /// </summary>
    [Fact]
    public async Task OutboundMessage_OptedOutNumber_Returns200Suppressed_NeverPublishesToKafka()
    {
        // Arrange
        await SeedCoolTextAccountAsync(accountNumber: "CT-OUTB-001");
        await SeedOptOutStatusAsync("+15551110002", status: "opted-out");
        Factory.MockPublisher.ClearReceivedCalls();

        var payload = new
        {
            ToNumber = "+15551110002",
            Body = "Your gas bill is ready.",
            CoolTextAccountNumber = "CT-OUTB-001",
            ApplicationId = "BizTalk",
            CorrelationId = $"corr-suppressed-{Guid.NewGuid():N}",
        };

        // Act
        var response = await Client.PostAsJsonAsync("/api/v1/messages/outbound", payload);

        // Assert — HTTP
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OutboundResponseShape>();
        body!.Status.Should().Be("suppressed");
        body.MessageId.Should().BeNull("suppressed messages have no messageId");
        body.QueuedAt.Should().BeNull("suppressed messages have no queuedAt");
        body.SuppressionReason.Should().Be("opted-out");

        // Assert — Kafka NOT published (TCPA compliance: never deliver to opted-out numbers)
        await Factory.MockPublisher.DidNotReceive()
            .PublishOutboundAsync(Arg.Any<OutboundMessageEvent>(), Arg.Any<CancellationToken>());
    }

    // ─── Idempotency ─────────────────────────────────────────────────────────────

    /// <summary>
    /// AC-003: Same correlationId sent twice on a queued message → idempotent 200 "queued"
    /// with the same messageId. Kafka published only once.
    /// </summary>
    [Fact]
    public async Task OutboundMessage_DuplicateCorrelationId_ReturnsIdempotentQueuedResponse()
    {
        // Arrange
        await SeedCoolTextAccountAsync(accountNumber: "CT-OUTB-001");
        var correlationId = $"corr-idem-{Guid.NewGuid():N}";

        var payload = new
        {
            ToNumber = "+15551110003",
            Body = "Your bill is ready.",
            CoolTextAccountNumber = "CT-OUTB-001",
            ApplicationId = "BizTalk",
            CorrelationId = correlationId,
        };

        // First call
        var first = await Client.PostAsJsonAsync("/api/v1/messages/outbound", payload);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadFromJsonAsync<OutboundResponseShape>();

        Factory.MockPublisher.ClearReceivedCalls();

        // Act — second call
        var second = await Client.PostAsJsonAsync("/api/v1/messages/outbound", payload);

        // Assert
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = await second.Content.ReadFromJsonAsync<OutboundResponseShape>();
        secondBody!.Status.Should().Be("queued");
        secondBody.MessageId.Should().Be(firstBody!.MessageId,
            because: "idempotent replay must return the original messageId");

        // Kafka must NOT be republished
        await Factory.MockPublisher.DidNotReceive()
            .PublishOutboundAsync(Arg.Any<OutboundMessageEvent>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// AC-004: Same correlationId for a previously suppressed message → idempotent 200 "suppressed".
    /// </summary>
    [Fact]
    public async Task OutboundMessage_DuplicateCorrelationId_ReturnsIdempotentSuppressedResponse()
    {
        // Arrange
        await SeedCoolTextAccountAsync(accountNumber: "CT-OUTB-001");
        await SeedOptOutStatusAsync("+15551110004", status: "opted-out");
        var correlationId = $"corr-supp-idem-{Guid.NewGuid():N}";

        var payload = new
        {
            ToNumber = "+15551110004",
            Body = "Your bill is ready.",
            CoolTextAccountNumber = "CT-OUTB-001",
            ApplicationId = "BizTalk",
            CorrelationId = correlationId,
        };

        // First call
        var first = await Client.PostAsJsonAsync("/api/v1/messages/outbound", payload);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act — second call
        var second = await Client.PostAsJsonAsync("/api/v1/messages/outbound", payload);

        // Assert
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = await second.Content.ReadFromJsonAsync<OutboundResponseShape>();
        secondBody!.Status.Should().Be("suppressed");
    }

    // ─── Account validation ───────────────────────────────────────────────────────

    /// <summary>
    /// AC-005: Unknown CoolTextAccountNumber → HTTP 400.
    /// </summary>
    [Fact]
    public async Task OutboundMessage_UnknownAccount_Returns400()
    {
        var payload = new
        {
            ToNumber = "+15551110005",
            Body = "Your bill is ready.",
            CoolTextAccountNumber = "CT-DOES-NOT-EXIST",
            ApplicationId = "BizTalk",
        };

        var response = await Client.PostAsJsonAsync("/api/v1/messages/outbound", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ─── Authentication ───────────────────────────────────────────────────────────

    /// <summary>
    /// AC-006: Missing X-Api-Key → HTTP 401.
    /// </summary>
    [Fact]
    public async Task OutboundMessage_MissingApiKey_Returns401()
    {
        using var anon = CreateUnauthenticatedClient();
        var payload = new
        {
            ToNumber = "+15551110006",
            Body = "Your bill is ready.",
            CoolTextAccountNumber = "CT-OUTB-001",
            ApplicationId = "BizTalk",
        };

        var response = await anon.PostAsJsonAsync("/api/v1/messages/outbound", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── Validation ───────────────────────────────────────────────────────────────

    /// <summary>
    /// AC-007: ToNumber in non-E.164 format → HTTP 400 (model validation).
    /// </summary>
    [Fact]
    public async Task OutboundMessage_InvalidPhoneFormat_Returns400()
    {
        await SeedCoolTextAccountAsync(accountNumber: "CT-OUTB-001");
        var payload = new
        {
            ToNumber = "not-a-phone-number",
            Body = "Your bill is ready.",
            CoolTextAccountNumber = "CT-OUTB-001",
            ApplicationId = "BizTalk",
        };

        var response = await Client.PostAsJsonAsync("/api/v1/messages/outbound", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// AC-008: SMS body exceeds 160 characters → HTTP 400 (model validation).
    /// </summary>
    [Fact]
    public async Task OutboundMessage_BodyExceeds160Characters_Returns400()
    {
        await SeedCoolTextAccountAsync(accountNumber: "CT-OUTB-001");
        var payload = new
        {
            ToNumber = "+15551110007",
            Body = new string('A', 161), // 161 chars — 1 over limit
            CoolTextAccountNumber = "CT-OUTB-001",
            ApplicationId = "BizTalk",
        };

        var response = await Client.PostAsJsonAsync("/api/v1/messages/outbound", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ─── Local response shape ─────────────────────────────────────────────────────
    private record OutboundResponseShape(
        string Status,
        string? MessageId,
        DateTimeOffset? QueuedAt,
        string? SuppressionReason);
}
