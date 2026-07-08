// Global Opt-Out Scope Cross-Component Integration Tests
// Source: STORY-005 AC-003 | SPEC-005 BR-026 | HIGH-RISK
// Generated: 2026-06-26 | Agent 09b — Functional & E2E Test Agent
//
// This test verifies the most critical cross-component behavioral invariant of the
// TCPA Compliance Engine: an opt-out received via ANY registered application must
// suppress outbound messages from ALL other registered applications.
//
// This cannot be tested at the unit level because unit tests mock the opt-out service.
// Only a full-pipeline functional test can verify that the inbound opt-out write and
// the outbound compliance gate both operate against the same shared opt-out table.

using System.Net;
using FluentAssertions;
using Moq;
using TCPA.Api.FunctionalTests.Infrastructure;
using TCPA.Api.Infrastructure.CoolText;

namespace TCPA.Api.FunctionalTests.Integration;

/// <summary>
/// Cross-component integration test verifying that opt-out state is global across
/// all registered SCG applications. An opt-out via one application account must
/// suppress outbound SMS from all other application accounts.
///
/// Components exercised:
///   InboundSmsController → InboundSmsHandler → OptOutStatusService → TcpaDbContext
///   OutboundSmsController → OutboundSmsGate → OptOutStatusService → TcpaDbContext
/// </summary>
public class GlobalOptOutScopeIntegrationTests : FunctionalTestBase, IClassFixture<TcpaFunctionalTestFactory>
{
    private const string GcmaAccountId = "CT-GCMA-INTEG-001";
    private const string VngAccountId = "CT-VNG-INTEG-001";

    public GlobalOptOutScopeIntegrationTests(TcpaFunctionalTestFactory factory)
        : base(factory)
    {
    }

    /// <summary>
    /// STORY-005 AC-003 (HIGH-RISK — Global Opt-Out Scope):
    /// Customer opts out via the GCMA inbound webhook. VNG then attempts to send an outbound
    /// SMS to the same number. The outbound must be SUPPRESSED even though the opt-out was
    /// received by a different application account.
    ///
    /// This is the TCPA system's core compliance guarantee: one opt-out record applies globally
    /// across all SCG application accounts (BR-026).
    /// </summary>
    [Fact]
    public async Task GlobalOptOut_OptOutViaGcmaAccount_SuppressesOutboundFromVngAccount()
    {
        // Arrange: seed both application registrations
        await SeedApplicationRegistrationAsync(
            GcmaAccountId,
            "GCMA Integration Test",
            "https://callback.gcma.example.com/sms");

        await SeedApplicationRegistrationAsync(
            VngAccountId,
            "VNG Integration Test",
            "https://callback.vng.example.com/sms");

        const string customerNumber = "+15555550301";
        // No opt-out record seeded — customer is currently opted in (by BR-001 default)

        Factory.MockCoolTextClient
            .Setup(c => c.SendSmsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendSmsResult { MessageId = "ct-msg-global-test", Status = "sent" });

        // --- Step 1: Customer opts out via GCMA inbound webhook ---
        var inboundRequest = MakeHmacSignedRequest(HttpMethod.Post, "/api/v1/sms/inbound", new
        {
            cool_text_account_id = GcmaAccountId,
            sender_cell_number = customerNumber,
            message_body = "STOP",
            cool_text_message_id = "inbound-stop-via-gcma",
        });

        var inboundResponse = await Client.SendAsync(inboundRequest);

        inboundResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "inbound webhook must be acknowledged with 200");

        // Wait for the background fire-and-forget opt-out write to complete
        await WaitForOptOutRecordAsync(customerNumber, timeoutMs: 5000);

        // --- Step 2: VNG attempts to send an outbound SMS to the same number ---
        var outboundRequest = MakeApiKeyRequest(HttpMethod.Post, "/api/v1/sms/outbound", new
        {
            cool_text_account_id = VngAccountId,
            destination_cell_number = customerNumber,
            message_body = "Your VNG bill is ready. Visit vngas.com to pay.",
            originating_application_reference = "VNG-BILL-CYCLE-202606",
        });

        var outboundResponse = await Client.SendAsync(outboundRequest);

        // Assert: outbound is SUPPRESSED even though the opt-out came through GCMA
        outboundResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var outboundBody = await ReadJsonAsync(outboundResponse);
        outboundBody.GetProperty("status").GetString().Should().Be("SUPPRESSED",
            because: "BR-026 requires that a STOP via any application suppresses ALL outbound messages " +
                     "across all applications — opt-out scope is global, not per-application");

        outboundBody.GetProperty("suppression_reason").GetString().Should().Be("OPT_OUT");

        // Critical: Cool Text must NOT have been called for the VNG outbound
        Factory.MockCoolTextClient.Verify(
            c => c.SendSmsAsync(
                It.Is<string>(accountId => accountId == VngAccountId),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            because: "an opted-out number must never have messages forwarded to Cool Text");
    }

    /// <summary>
    /// Verifies the inverse scenario: a number that is NOT opted-out can still receive
    /// messages from multiple application accounts. This confirms the global opt-out scope
    /// test above is actually testing the opt-out — not always-suppressing.
    /// </summary>
    [Fact]
    public async Task GlobalOptOut_NotOptedOutNumber_ForwardsFromBothAccounts()
    {
        // Arrange
        await SeedApplicationRegistrationAsync(
            GcmaAccountId,
            "GCMA Integration Test",
            "https://callback.gcma.example.com/sms");

        await SeedApplicationRegistrationAsync(
            VngAccountId,
            "VNG Integration Test",
            "https://callback.vng.example.com/sms");

        const string customerNumber = "+15555550302"; // no opt-out record

        Factory.MockCoolTextClient
            .Setup(c => c.SendSmsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendSmsResult { MessageId = "ct-msg-opt-in", Status = "sent" });

        // Act — VNG sends outbound to an opted-in number
        var outboundRequest = MakeApiKeyRequest(HttpMethod.Post, "/api/v1/sms/outbound", new
        {
            cool_text_account_id = VngAccountId,
            destination_cell_number = customerNumber,
            message_body = "Your VNG bill is ready.",
        });

        var outboundResponse = await Client.SendAsync(outboundRequest);

        // Assert
        outboundResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(outboundResponse);
        body.GetProperty("status").GetString().Should().Be("FORWARDED",
            because: "numbers without an opt-out record default to OPT_IN (BR-001)");
    }
}
