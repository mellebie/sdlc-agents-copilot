// integration/GlobalOptOutScopeIntegrationTests.cs
// Source: Agent 09b (Drew) | Cross-component integration | SPEC-006, SPEC-007, SPEC-008
// Verifies that:
//   1. An opt-out status written to the DB is respected by the outbound gate (suppression)
//   2. A re-opt-in via the admin endpoint is immediately reflected in the outbound gate
//   3. A number with no prior opt-out record is treated as opted-in (outbound queued)
// These scenarios require multiple components working together and cannot be verified at the unit level.

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TCPA.Functional.Tests.Infrastructure;
using Xunit;

namespace TCPA.Functional.Tests.Integration;

/// <summary>
/// Cross-component integration tests for the global opt-out scope.
/// Uses real InMemory DB interactions (not mocked repositories) to verify
/// that opt-out state flows correctly across the API, repositories, and admin service.
/// </summary>
[Collection(TcpaTestCollection.Name)]
public class GlobalOptOutScopeIntegrationTests : FunctionalTestBase
{
    public GlobalOptOutScopeIntegrationTests(TcpaTestFactory factory) : base(factory) { }

    // ─── Opt-out enforcement ──────────────────────────────────────────────────────

    /// <summary>
    /// Scenario 1: Number with an "opted-out" record in DB → outbound suppressed.
    /// Verifies that <see cref="TCPA.Core.Repositories.SqlOptOutStatusRepository"/> correctly
    /// reads opt-out status from the InMemory DB and the controller honours it.
    /// </summary>
    [Fact]
    public async Task OptedOutNumber_OutboundSuppressed_AcrossComponents()
    {
        // Arrange — seed account and opt-out status directly to DB
        await SeedCoolTextAccountAsync(accountNumber: "CT-INTEG-001");
        const string optedOutNumber = "+15553330001";
        await SeedOptOutStatusAsync(optedOutNumber, "opted-out");

        var payload = new
        {
            ToNumber = optedOutNumber,
            Body = "Bill reminder",
            CoolTextAccountNumber = "CT-INTEG-001",
            ApplicationId = "BizTalk",
            CorrelationId = $"integ-supp-{Guid.NewGuid():N}",
        };

        // Act
        var response = await Client.PostAsJsonAsync("/api/v1/messages/outbound", payload);

        // Assert — outbound gate respected the DB opt-out record
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OutboundShape>();
        body!.Status.Should().Be("suppressed",
            because: "the opt-out record in DB must suppress outbound delivery across components");
    }

    /// <summary>
    /// Scenario 2: Number with an "opted-in" record in DB → outbound queued.
    /// Confirms that an explicit "opted-in" status (not just absence of a record) allows delivery.
    /// </summary>
    [Fact]
    public async Task OptedInNumber_OutboundQueued_AcrossComponents()
    {
        // Arrange
        await SeedCoolTextAccountAsync(accountNumber: "CT-INTEG-001");
        const string optedInNumber = "+15553330002";
        await SeedOptOutStatusAsync(optedInNumber, "opted-in");

        var payload = new
        {
            ToNumber = optedInNumber,
            Body = "Bill reminder",
            CoolTextAccountNumber = "CT-INTEG-001",
            ApplicationId = "BizTalk",
            CorrelationId = $"integ-queue-{Guid.NewGuid():N}",
        };

        // Act
        var response = await Client.PostAsJsonAsync("/api/v1/messages/outbound", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OutboundShape>();
        body!.Status.Should().Be("queued");
    }

    /// <summary>
    /// Scenario 3: Number with no record in DB → treated as opted-in (outbound queued).
    /// Verifies the <c>GetStatusAsync</c> default of "opted-in" when no record exists.
    /// </summary>
    [Fact]
    public async Task NumberWithNoRecord_OutboundQueued_DefaultsToOptedIn()
    {
        // Arrange — no OptOutStatus seeded for this number
        await SeedCoolTextAccountAsync(accountNumber: "CT-INTEG-001");
        // Use decimal digits only — GUID.N hex chars (a-f) fail E.164 \d validation
        var phoneNumber = $"+1555333{Random.Shared.Next(1000, 9999):D4}";

        var payload = new
        {
            ToNumber = phoneNumber,
            Body = "Bill reminder",
            CoolTextAccountNumber = "CT-INTEG-001",
            ApplicationId = "BizTalk",
            CorrelationId = $"integ-norecord-{Guid.NewGuid():N}",
        };

        // Act
        var response = await Client.PostAsJsonAsync("/api/v1/messages/outbound", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OutboundShape>();
        body!.Status.Should().Be("queued",
            because: "absence of a record must default to opted-in per TCPA safe harbour");
    }

    // ─── Re-opt-in then outbound ──────────────────────────────────────────────────

    /// <summary>
    /// Scenario 4 (Cross-component): Admin re-opts-in a customer → subsequent outbound is queued.
    /// This is the critical cross-component scenario: admin write via ReOptInService must be
    /// immediately visible to the outbound gate (SqlOptOutStatusRepository).
    /// Both read from the same InMemory database, so the write is immediately visible.
    /// </summary>
    [Fact]
    public async Task AdminReOptIn_ThenOutbound_IsQueued_CrossComponent()
    {
        // Arrange — start with opted-out
        await SeedCoolTextAccountAsync(accountNumber: "CT-INTEG-001");
        const string phoneNumber = "+15553330004";
        await SeedOptOutStatusAsync(phoneNumber, "opted-out");

        // Verify baseline: outbound is suppressed before re-opt-in
        var beforePayload = new
        {
            ToNumber = phoneNumber,
            Body = "Bill reminder",
            CoolTextAccountNumber = "CT-INTEG-001",
            ApplicationId = "BizTalk",
            CorrelationId = $"integ-before-{Guid.NewGuid():N}",
        };
        var before = await Client.PostAsJsonAsync("/api/v1/messages/outbound", beforePayload);
        var beforeBody = await before.Content.ReadFromJsonAsync<OutboundShape>();
        beforeBody!.Status.Should().Be("suppressed", because: "baseline: number is opted-out");

        // Act — admin re-opts-in via admin endpoint
        var reOptInPayload = new
        {
            PhoneNumber = phoneNumber,
            Reason = "Customer called and verified identity. Wishes to receive bills by SMS.",
            AgentId = "helpdesk-agent-007",
        };
        var reOptIn = await Client.PostAsJsonAsync("/api/v1/admin/reopt-in", reOptInPayload);
        reOptIn.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert — subsequent outbound for the same number is now queued
        var afterPayload = new
        {
            ToNumber = phoneNumber,
            Body = "Welcome back! Your bill is ready.",
            CoolTextAccountNumber = "CT-INTEG-001",
            ApplicationId = "BizTalk",
            CorrelationId = $"integ-after-{Guid.NewGuid():N}",
        };
        var after = await Client.PostAsJsonAsync("/api/v1/messages/outbound", afterPayload);

        after.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterBody = await after.Content.ReadFromJsonAsync<OutboundShape>();
        afterBody!.Status.Should().Be("queued",
            because: "after re-opt-in the outbound gate must allow delivery");

        // Assert — DB reflects opted-in
        var dbStatus = await GetOptOutStatusAsync(phoneNumber);
        dbStatus.Should().Be("opted-in");
    }

    // ─── Local response shape ─────────────────────────────────────────────────────
    private record OutboundShape(string Status, string? MessageId, DateTimeOffset? QueuedAt, string? SuppressionReason);
}
