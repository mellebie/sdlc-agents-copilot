// journeys/InboundOptOutJourneyTests.cs
// Source: Agent 09b (Drew) | STORY-001 (Inbound webhook receipt) | AC-001 through AC-006
// SPEC-001, SPEC-002 — Inbound webhook receives an SMS from Cool Text/Twilio, validates the
// destination account, enforces idempotency, and publishes the event to Kafka.

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using NSubstitute;
using TCPA.Api.Messaging;
using TCPA.Functional.Tests.Infrastructure;
using Xunit;

namespace TCPA.Functional.Tests.Journeys;

/// <summary>
/// Journey tests for POST /webhook/inbound.
/// Each test uses a unique phone number and message ID to avoid cross-test interference.
/// All test classes share one <see cref="TcpaTestFactory"/> via the TcpaFunctional collection fixture.
/// </summary>
[Collection(TcpaTestCollection.Name)]
public class InboundOptOutJourneyTests : FunctionalTestBase
{
    public InboundOptOutJourneyTests(TcpaTestFactory factory) : base(factory) { }

    // ─── Happy path ───────────────────────────────────────────────────────────────

    /// <summary>
    /// AC-001 (Happy Path): Valid account and new message ID → HTTP 200 "received",
    /// non-empty internalId, and Kafka publish called exactly once.
    /// </summary>
    [Fact]
    public async Task InboundWebhook_ValidAccount_Returns200AndPublishesToKafka()
    {
        // Arrange
        await SeedCoolTextAccountAsync(accountNumber: "CT-INB-001");
        var messageId = $"inb-happy-{Guid.NewGuid():N}";

        var payload = new
        {
            From = "+15550000001",
            To = "CT-INB-001",
            Body = "STOP",
            Provider = "cooltext",
            MessageId = messageId,
            Timestamp = DateTimeOffset.UtcNow,
        };

        // Act
        var response = await Client.PostAsJsonAsync("/webhook/inbound", payload);

        // Assert — HTTP
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ResponseShape>();
        body!.Status.Should().Be("received");
        body.InternalId.Should().NotBeNullOrWhiteSpace("a tracking GUID must be returned");

        // Assert — Kafka publish
        await Factory.MockPublisher.Received(1)
            .PublishInboundAsync(
                Arg.Is<InboundMessageEvent>(e =>
                    e.MessageId == messageId &&
                    e.From == "+15550000001" &&
                    e.To == "CT-INB-001"),
                Arg.Any<CancellationToken>());
    }

    // ─── Idempotency ─────────────────────────────────────────────────────────────

    /// <summary>
    /// AC-002: Same messageId sent twice → both return HTTP 200 with the SAME internalId.
    /// Kafka is published only on the first call.
    /// </summary>
    [Fact]
    public async Task InboundWebhook_DuplicateMessageId_ReturnsIdempotentResponseAndDoesNotRepublish()
    {
        // Arrange
        await SeedCoolTextAccountAsync(accountNumber: "CT-INB-001");
        var messageId = $"inb-idem-{Guid.NewGuid():N}";

        var payload = new
        {
            From = "+15550000002",
            To = "CT-INB-001",
            Body = "STOP",
            Provider = "cooltext",
            MessageId = messageId,
            Timestamp = DateTimeOffset.UtcNow,
        };

        // First call
        var first = await Client.PostAsJsonAsync("/webhook/inbound", payload);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadFromJsonAsync<ResponseShape>();

        Factory.MockPublisher.ClearReceivedCalls();

        // Act — second call with identical messageId
        var second = await Client.PostAsJsonAsync("/webhook/inbound", payload);

        // Assert — same internalId returned
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = await second.Content.ReadFromJsonAsync<ResponseShape>();
        secondBody!.InternalId.Should().Be(firstBody!.InternalId,
            because: "idempotent replay must return the original tracking ID");

        // Assert — Kafka not published again
        await Factory.MockPublisher.DidNotReceive()
            .PublishInboundAsync(Arg.Any<InboundMessageEvent>(), Arg.Any<CancellationToken>());
    }

    // ─── Account validation ───────────────────────────────────────────────────────

    /// <summary>
    /// AC-003: Message to an unknown Cool Text account → HTTP 400.
    /// </summary>
    [Fact]
    public async Task InboundWebhook_UnknownAccount_Returns400()
    {
        var payload = new
        {
            From = "+15550000003",
            To = "CT-NOT-REGISTERED",
            Body = "STOP",
            Provider = "cooltext",
            MessageId = $"inb-unk-{Guid.NewGuid():N}",
            Timestamp = DateTimeOffset.UtcNow,
        };

        var response = await Client.PostAsJsonAsync("/webhook/inbound", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// AC-004: Message to an inactive Cool Text account → HTTP 400.
    /// </summary>
    [Fact]
    public async Task InboundWebhook_InactiveAccount_Returns400()
    {
        await SeedCoolTextAccountAsync(accountNumber: "CT-INB-INACTIVE", isActive: false);

        var payload = new
        {
            From = "+15550000004",
            To = "CT-INB-INACTIVE",
            Body = "STOP",
            Provider = "cooltext",
            MessageId = $"inb-inact-{Guid.NewGuid():N}",
            Timestamp = DateTimeOffset.UtcNow,
        };

        var response = await Client.PostAsJsonAsync("/webhook/inbound", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ─── Authentication ───────────────────────────────────────────────────────────

    /// <summary>
    /// AC-005: Missing X-Api-Key header → HTTP 401.
    /// </summary>
    [Fact]
    public async Task InboundWebhook_MissingApiKey_Returns401()
    {
        using var anon = CreateUnauthenticatedClient();
        var payload = new
        {
            From = "+15550000005",
            To = "CT-INB-001",
            Body = "STOP",
            Provider = "cooltext",
            MessageId = $"inb-noauth-{Guid.NewGuid():N}",
            Timestamp = DateTimeOffset.UtcNow,
        };

        var response = await anon.PostAsJsonAsync("/webhook/inbound", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// AC-006: Wrong X-Api-Key → HTTP 401.
    /// </summary>
    [Fact]
    public async Task InboundWebhook_InvalidApiKey_Returns401()
    {
        using var badKey = CreateUnauthenticatedClient();
        badKey.DefaultRequestHeaders.Add(TestApiKeys.HeaderName, "invalid-key-99999");
        var payload = new
        {
            From = "+15550000006",
            To = "CT-INB-001",
            Body = "STOP",
            Provider = "cooltext",
            MessageId = $"inb-badkey-{Guid.NewGuid():N}",
            Timestamp = DateTimeOffset.UtcNow,
        };

        var response = await badKey.PostAsJsonAsync("/webhook/inbound", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── Local response shape record ──────────────────────────────────────────────
    private record ResponseShape(string Status, string InternalId);
}
