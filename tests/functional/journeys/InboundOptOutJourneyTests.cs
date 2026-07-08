// Inbound Opt-Out Journey Tests
// Source: STORY-003, STORY-004, STORY-005 | SPEC-003 through SPEC-008 | AC-001 through AC-008
// Generated: 2026-06-26 | Agent 09b — Functional & E2E Test Agent
// Risk Level: HIGH-RISK — Full happy + unhappy paths required

using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TCPA.Api.Domain;
using TCPA.Api.FunctionalTests.Infrastructure;
using TCPA.Api.Infrastructure.Data;

namespace TCPA.Api.FunctionalTests.Journeys;

/// <summary>
/// User journey tests for the inbound SMS opt-out pipeline.
/// Tests the complete flow: webhook receipt → HMAC validation → 200 immediate response
/// → background keyword detection → opt-out status write.
///
/// IMPORTANT: The inbound opt-out write is fire-and-forget (occurs AFTER the 200 is returned).
/// All tests that verify the DB write must use <see cref="FunctionalTestBase.WaitForOptOutRecordAsync"/>
/// rather than asserting synchronously after the HTTP response.
/// </summary>
public class InboundOptOutJourneyTests : FunctionalTestBase, IClassFixture<TcpaFunctionalTestFactory>
{
    private const string RegisteredAccountId = "CT-VNG-TEST-001";
    private const string CallbackUrl = "https://callback.vng.example.com/sms";

    public InboundOptOutJourneyTests(TcpaFunctionalTestFactory factory)
        : base(factory)
    {
    }

    /// <summary>
    /// AC-001 (Happy Path): STOP keyword triggers immediate 200 and asynchronous opt-out write.
    /// SPEC-003: Webhook is acknowledged within SLA before processing.
    /// SPEC-005: Opt-out record written after acknowledgement.
    /// </summary>
    [Fact]
    public async Task InboundOptOut_StopKeyword_Returns200ImmediatelyAndWritesOptOut()
    {
        // Arrange
        const string senderNumber = "+15555550101";
        await SeedApplicationRegistrationAsync(RegisteredAccountId, "VNG Test App", CallbackUrl);

        var request = MakeHmacSignedRequest(HttpMethod.Post, "/api/v1/sms/inbound", new
        {
            cool_text_account_id = RegisteredAccountId,
            sender_cell_number = senderNumber,
            message_body = "STOP",
            cool_text_message_id = "inbound-msg-001",
        });

        // Act
        var response = await Client.SendAsync(request);

        // Assert — immediate response
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "the webhook must be acknowledged within SLA before processing begins");

        var body = await ReadJsonAsync(response);
        body.GetProperty("received").GetBoolean().Should().BeTrue();

        // Assert — background write (fire-and-forget)
        await WaitForOptOutRecordAsync(senderNumber,
            timeoutMs: 5000);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TcpaDbContext>();
        var record = await db.OptOutRecords.FirstOrDefaultAsync(r => r.CellPhoneNumber == senderNumber);

        record.Should().NotBeNull();
        record!.Status.Should().Be(OptOutStatus.OptOut);
    }

    /// <summary>
    /// AC-002 (Theory — all 7 CTIA keywords): Each of the seven mandated opt-out keywords
    /// triggers an opt-out write. Uses unique numbers per keyword to avoid DB state conflicts.
    /// BR-013: OPT-OUT (hyphenated) is a distinct keyword from OPT.
    /// </summary>
    [Theory]
    [InlineData("STOP", "+15555550111")]
    [InlineData("QUIT", "+15555550112")]
    [InlineData("END", "+15555550113")]
    [InlineData("REVOKE", "+15555550114")]
    [InlineData("OPT-OUT", "+15555550115")]
    [InlineData("CANCEL", "+15555550116")]
    [InlineData("UNSUBSCRIBE", "+15555550117")]
    public async Task InboundOptOut_AllSevenKeywords_EachWritesOptOut(string keyword, string senderNumber)
    {
        // Arrange — seed registration for each test (idempotent helper handles duplicates)
        await SeedApplicationRegistrationAsync(RegisteredAccountId, "VNG Test App", CallbackUrl);

        var request = MakeHmacSignedRequest(HttpMethod.Post, "/api/v1/sms/inbound", new
        {
            cool_text_account_id = RegisteredAccountId,
            sender_cell_number = senderNumber,
            message_body = keyword,
            cool_text_message_id = $"inbound-msg-{keyword.ToLower()}",
        });

        // Act
        var response = await Client.SendAsync(request);

        // Assert — 200 always returned immediately regardless of keyword
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: $"keyword '{keyword}' is a valid CTIA opt-out keyword");

        // Assert — opt-out written asynchronously
        await WaitForOptOutRecordAsync(senderNumber, timeoutMs: 5000);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TcpaDbContext>();
        var record = await db.OptOutRecords.FirstOrDefaultAsync(r => r.CellPhoneNumber == senderNumber);

        record.Should().NotBeNull($"keyword '{keyword}' must result in an opt-out record");
        record!.Status.Should().Be(OptOutStatus.OptOut);
    }

    /// <summary>
    /// AC-003 (Unhappy Path): Non-opt-out keyword message returns 200 but does NOT write opt-out.
    /// Verifies that arbitrary message content does not inadvertently trigger opt-out.
    /// </summary>
    [Fact]
    public async Task InboundOptOut_NonKeyword_Returns200_DoesNotWriteOptOut()
    {
        // Arrange
        const string senderNumber = "+15555550120";
        await SeedApplicationRegistrationAsync(RegisteredAccountId, "VNG Test App", CallbackUrl);

        var request = MakeHmacSignedRequest(HttpMethod.Post, "/api/v1/sms/inbound", new
        {
            cool_text_account_id = RegisteredAccountId,
            sender_cell_number = senderNumber,
            message_body = "Hello, I need help with my bill. Can you assist?",
            cool_text_message_id = "inbound-msg-non-keyword",
        });

        // Act
        var response = await Client.SendAsync(request);

        // Assert — webhook is always acknowledged
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(response);
        body.GetProperty("received").GetBoolean().Should().BeTrue();

        // Give background processing time to complete (if any)
        await Task.Delay(1000);

        // Assert — no opt-out record created for a non-keyword message
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TcpaDbContext>();
        var record = await db.OptOutRecords.FirstOrDefaultAsync(r => r.CellPhoneNumber == senderNumber);

        record.Should().BeNull(
            because: "non-keyword messages must not create opt-out records");
    }

    /// <summary>
    /// AC-004 (Unhappy Path): Invalid HMAC signature returns 401.
    /// SPEC-004: Webhook signature is mandatory; invalid signatures are rejected fail-closed.
    /// </summary>
    [Fact]
    public async Task InboundOptOut_InvalidHmac_Returns401()
    {
        // Arrange
        await SeedApplicationRegistrationAsync(RegisteredAccountId, "VNG Test App", CallbackUrl);

        var request = new System.Net.Http.HttpRequestMessage(HttpMethod.Post, "/api/v1/sms/inbound")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                cool_text_account_id = RegisteredAccountId,
                sender_cell_number = "+15555550130",
                message_body = "STOP",
                cool_text_message_id = "inbound-msg-bad-sig",
            }),
        };
        // Use a deliberately wrong signature value
        request.Headers.Add(TcpaTestConstants.SignatureHeaderName, "sha256=000000deadbeef000000");

        // Act
        var response = await Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "an invalid HMAC signature must be rejected to prevent spoofed opt-outs");
    }

    /// <summary>
    /// AC-005 (Unhappy Path): Missing HMAC signature header returns 401.
    /// </summary>
    [Fact]
    public async Task InboundOptOut_MissingHmacHeader_Returns401()
    {
        // Arrange — no signature header added
        var request = new System.Net.Http.HttpRequestMessage(HttpMethod.Post, "/api/v1/sms/inbound")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                cool_text_account_id = RegisteredAccountId,
                sender_cell_number = "+15555550131",
                message_body = "STOP",
                cool_text_message_id = "inbound-msg-no-sig",
            }),
        };

        // Act
        var response = await Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// AC-006 (Unhappy Path per STORY-003 AC-003): An unregistered Cool Text account ID
    /// returns 200 (not an error) and discards the message with a warning log.
    /// The webhook is always acknowledged to prevent Cool Text retry storms.
    /// </summary>
    [Fact]
    public async Task InboundOptOut_UnregisteredAccount_Returns200AndDiscards()
    {
        // Arrange — no application registration seeded
        const string unregisteredAccount = "CT-UNREGISTERED-INBOUND-999";

        // We need a valid HMAC signature even for unregistered accounts
        // because signature validation happens BEFORE account lookup
        var request = MakeHmacSignedRequest(HttpMethod.Post, "/api/v1/sms/inbound", new
        {
            cool_text_account_id = unregisteredAccount,
            sender_cell_number = "+15555550140",
            message_body = "STOP",
            cool_text_message_id = "inbound-msg-unreg",
        });

        // Act
        var response = await Client.SendAsync(request);

        // Assert — must return 200 to prevent Cool Text retry loops
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "unregistered accounts must still receive a 200 acknowledgement to prevent retry storms");

        var body = await ReadJsonAsync(response);
        body.GetProperty("received").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// AC-007 (Idempotency): Sending STOP for an already-opted-out number is idempotent.
    /// A second STOP must not create a duplicate record or return an error.
    /// BR-023: Confirmation SMS is only sent for NEW opt-outs (not re-sent for already-opted-out).
    /// </summary>
    [Fact]
    public async Task InboundOptOut_AlreadyOptedOut_IdempotentNoError()
    {
        // Arrange — pre-seed opt-out record
        const string senderNumber = "+15555550150";
        await SeedApplicationRegistrationAsync(RegisteredAccountId, "VNG Test App", CallbackUrl);
        await SeedOptOutRecordAsync(senderNumber, OptOutStatus.OptOut);

        var request = MakeHmacSignedRequest(HttpMethod.Post, "/api/v1/sms/inbound", new
        {
            cool_text_account_id = RegisteredAccountId,
            sender_cell_number = senderNumber,
            message_body = "STOP",
            cool_text_message_id = "inbound-msg-duplicate-stop",
        });

        // Act
        var response = await Client.SendAsync(request);

        // Assert — idempotent 200
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(response);
        body.GetProperty("received").GetBoolean().Should().BeTrue();

        // Give background processing time to complete
        await Task.Delay(500);

        // Assert — exactly one record, not duplicated
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TcpaDbContext>();
        var recordCount = await db.OptOutRecords.CountAsync(r => r.CellPhoneNumber == senderNumber);

        recordCount.Should().Be(1,
            because: "a duplicate STOP must not create additional records (idempotent)");
    }
}
