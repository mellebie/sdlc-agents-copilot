// Admin Re-Opt-In Journey Tests
// Source: STORY-007, STORY-008 | SPEC-010, SPEC-011 | AC-001 through AC-008
// Generated: 2026-06-26 | Agent 09b — Functional & E2E Test Agent
// Risk Level: Standard — Happy + 1 unhappy path minimum
//
// KNOWN TEST GAP — JWT auth bypass:
// In this test environment, Authentication:AdminApi:Authority is set to empty string,
// which causes Program.cs to skip the AddJwtBearer registration. As a result, the
// [Authorize(Roles = "tcpa.helpdesk,tcpa.compliance_officer")] attribute on AdminController
// does not reject unauthenticated requests in functional tests. Admin endpoint tests here
// verify business logic (re-opt-in workflow, status lookup, validation) but NOT auth enforcement.
// Auth enforcement is deferred to a security integration test environment with a real OIDC provider.

using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TCPA.Api.Domain;
using TCPA.Api.FunctionalTests.Infrastructure;
using TCPA.Api.Infrastructure.Data;

namespace TCPA.Api.FunctionalTests.Journeys;

/// <summary>
/// Journey tests for the admin re-opt-in workflow and status lookup endpoints.
/// Verifies business logic correctness for the helpdesk-facing API used to restore
/// opted-out numbers on behalf of customers who call in to request it.
/// </summary>
public class AdminReOptInJourneyTests : FunctionalTestBase, IClassFixture<TcpaFunctionalTestFactory>
{
    public AdminReOptInJourneyTests(TcpaFunctionalTestFactory factory)
        : base(factory)
    {
    }

    /// <summary>
    /// AC-001 (Happy Path): Re-opting in an opted-out number updates the DB and returns
    /// the correct previous/new status in the response.
    /// </summary>
    [Fact]
    public async Task AdminReOptIn_OptedOutNumber_ReturnsSuccess_AndUpdatesDB()
    {
        // Arrange
        const string cellNumber = "+15555550201";
        await SeedOptOutRecordAsync(cellNumber, OptOutStatus.OptOut);

        var request = new System.Net.Http.HttpRequestMessage(
            HttpMethod.Put, "/admin/v1/opt-out/re-opt-in")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                cellPhoneNumber = cellNumber,
                reason = "Customer called helpdesk to request re-opt-in after accidental STOP.",
            }),
        };

        // Act
        var response = await Client.SendAsync(request);

        // Assert — response shape
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(response);
        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.GetProperty("previousStatus").GetString().Should().Be("OPT_OUT");
        body.GetProperty("newStatus").GetString().Should().Be("OPT_IN");

        // Assert — DB state updated
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TcpaDbContext>();
        var record = await db.OptOutRecords.FirstOrDefaultAsync(r => r.CellPhoneNumber == cellNumber);

        record.Should().NotBeNull();
        record!.Status.Should().Be(OptOutStatus.OptIn);
    }

    /// <summary>
    /// AC-002: Re-opting in a number with no prior record returns 409 Conflict.
    /// BR-038: Cannot re-opt-in a number that was never opted out via this system.
    /// </summary>
    [Fact]
    public async Task AdminReOptIn_NoRecord_Returns409()
    {
        // Arrange — no record seeded for this number
        const string cellNumber = "+15555550202";

        var request = new System.Net.Http.HttpRequestMessage(
            HttpMethod.Put, "/admin/v1/opt-out/re-opt-in")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                cellPhoneNumber = cellNumber,
                reason = "Customer called helpdesk to request re-opt-in after accidental STOP.",
            }),
        };

        // Act
        var response = await Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            because: "BR-038 requires 409 when there is no prior opt-out record to reverse");
    }

    /// <summary>
    /// AC-003 (Validation): Reason field shorter than 20 characters returns 400 Bad Request.
    /// ReOptInService validates reason length before processing.
    /// </summary>
    [Fact]
    public async Task AdminReOptIn_ShortReason_Returns400()
    {
        // Arrange
        const string cellNumber = "+15555550203";
        await SeedOptOutRecordAsync(cellNumber, OptOutStatus.OptOut);

        var request = new System.Net.Http.HttpRequestMessage(
            HttpMethod.Put, "/admin/v1/opt-out/re-opt-in")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                cellPhoneNumber = cellNumber,
                reason = "too short", // 9 characters — below 20-character minimum
            }),
        };

        // Act
        var response = await Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// AC-004 (Validation): Cell number not in E.164 format returns 400 Bad Request.
    /// AdminController validates the cell number format at the boundary.
    /// </summary>
    [Fact]
    public async Task AdminReOptIn_InvalidE164_Returns400()
    {
        // Arrange — missing leading +
        var request = new System.Net.Http.HttpRequestMessage(
            HttpMethod.Put, "/admin/v1/opt-out/re-opt-in")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                cellPhoneNumber = "5555550204", // missing leading +
                reason = "Customer called helpdesk to request re-opt-in after accidental STOP.",
            }),
        };

        // Act
        var response = await Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// AC-005 (Idempotency): Re-opting in a number that is already OPT_IN is a no-op success.
    /// ReOptInService handles this case idempotently without error.
    /// </summary>
    [Fact]
    public async Task AdminReOptIn_AlreadyOptedIn_IsIdempotentSuccess()
    {
        // Arrange — number is already opted in
        const string cellNumber = "+15555550205";
        await SeedOptOutRecordAsync(cellNumber, OptOutStatus.OptIn);

        var request = new System.Net.Http.HttpRequestMessage(
            HttpMethod.Put, "/admin/v1/opt-out/re-opt-in")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                cellPhoneNumber = cellNumber,
                reason = "Helpdesk re-opt-in requested by customer via phone call.",
            }),
        };

        // Act
        var response = await Client.SendAsync(request);

        // Assert — idempotent success, not an error
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "re-opting in an already-opted-in number must be idempotent");
        var body = await ReadJsonAsync(response);
        body.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// AC-006 (Happy Path): Status lookup returns the correct status for an opted-out number.
    /// BR-037: The response must mask the phone number, showing only the last 4 digits.
    /// </summary>
    [Fact]
    public async Task AdminStatus_ExistingOptOutRecord_ReturnsStatus()
    {
        // Arrange
        const string cellNumber = "+15555550210";
        await SeedOptOutRecordAsync(cellNumber, OptOutStatus.OptOut);

        // URL-encode the + in the E.164 number for the path segment
        var response = await Client.GetAsync("/admin/v1/opt-out/status/%2B15555550210");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(response);
        body.GetProperty("optOutStatus").GetString().Should().Be("OPT_OUT");

        // BR-037: Masked number must only reveal last 4 digits
        var maskedNumber = body.GetProperty("maskedCellNumber").GetString();
        maskedNumber.Should().NotBeNull();
        maskedNumber.Should().EndWith("0210",
            because: "BR-037 requires masking to last 4 digits");
        maskedNumber.Should().NotContain("+15555550210",
            because: "the full number must never be returned in the response");
    }

    /// <summary>
    /// AC-007 (Unhappy Path): Status lookup for a number with no record returns 404.
    /// </summary>
    [Fact]
    public async Task AdminStatus_NoRecord_Returns404()
    {
        // Arrange — no record seeded for this number
        var response = await Client.GetAsync("/admin/v1/opt-out/status/%2B15555550299");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
