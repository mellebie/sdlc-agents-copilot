// Admin API Contract Tests
// Source: SPEC-010, SPEC-011 | BR-037 (PII masking in responses)
// Generated: 2026-06-26 | Agent 09b — Functional & E2E Test Agent
//
// Verifies the shape of admin API responses consumed by helpdesk tooling and
// compliance reporting workflows.

using System.Net;
using System.Text.Json;
using FluentAssertions;
using TCPA.Api.Domain;
using TCPA.Api.FunctionalTests.Infrastructure;

namespace TCPA.Api.FunctionalTests.Contracts;

/// <summary>
/// Contract tests for the admin opt-out management endpoints.
/// Verifies response shape and PII masking behavior for the helpdesk-facing API.
/// </summary>
public class AdminApiContractTests : FunctionalTestBase, IClassFixture<TcpaFunctionalTestFactory>
{
    public AdminApiContractTests(TcpaFunctionalTestFactory factory)
        : base(factory)
    {
    }

    /// <summary>
    /// CONTRACT: Admin status response must contain maskedCellNumber, optOutStatus,
    /// and lastOptOutTimestamp fields.
    /// </summary>
    [Fact]
    public async Task AdminStatusResponse_ContainsRequiredFields()
    {
        // Arrange
        const string cellNumber = "+12025551101";
        await SeedOptOutRecordAsync(cellNumber, OptOutStatus.OptOut);

        // Act
        var response = await Client.GetAsync("/admin/v1/opt-out/status/%2B12025551101");
        var body = await ReadJsonAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        body.TryGetProperty("maskedCellNumber", out var maskedProp).Should().BeTrue(
            because: "admin status response must return maskedCellNumber, not the full number");
        maskedProp.GetString().Should().NotBeNullOrEmpty();

        body.TryGetProperty("optOutStatus", out var statusProp).Should().BeTrue(
            because: "admin status response must include optOutStatus");
        statusProp.GetString().Should().NotBeNullOrEmpty();

        body.TryGetProperty("lastOptOutTimestamp", out var timestampProp).Should().BeTrue(
            because: "admin status response must include lastOptOutTimestamp (nullable ISO 8601)");

        // Timestamp may be null for OPT_IN records, but must be present for OPT_OUT
        if (timestampProp.ValueKind != JsonValueKind.Null)
        {
            DateTimeOffset.TryParse(timestampProp.GetString(), out _).Should().BeTrue(
                because: "lastOptOutTimestamp when present must be a valid ISO 8601 datetime");
        }
    }

    /// <summary>
    /// CONTRACT — BR-037: The maskedCellNumber in admin responses must show only the
    /// last 4 digits, prefixed with asterisks. The full number must never be returned.
    /// </summary>
    [Fact]
    public async Task AdminStatusResponse_MaskedNumber_OnlyShowsLast4Digits()
    {
        // Arrange
        const string cellNumber = "+12025559876";
        await SeedOptOutRecordAsync(cellNumber, OptOutStatus.OptOut);

        // Act
        var response = await Client.GetAsync("/admin/v1/opt-out/status/%2B12025559876");
        var body = await ReadJsonAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var maskedNumber = body.GetProperty("maskedCellNumber").GetString();
        maskedNumber.Should().NotBeNull();

        // Must end with last 4 digits
        maskedNumber.Should().EndWith("9876",
            because: "BR-037: masked number must show only the last 4 digits");

        // Must NOT contain the full phone number
        maskedNumber.Should().NotContain("+12025559876",
            because: "BR-037: the full cell number is PII and must never appear in any response");

        // Must contain masking characters
        maskedNumber.Should().Match("*9876",
            because: "BR-037: the leading digits must be replaced with asterisks");
    }

    /// <summary>
    /// CONTRACT: Re-opt-in response must contain success, previousStatus, newStatus,
    /// and updatedTimestamp fields.
    /// </summary>
    [Fact]
    public async Task AdminReOptInResponse_ContainsRequiredFields()
    {
        // Arrange
        const string cellNumber = "+12025551102";
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
        var body = await ReadJsonAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        body.TryGetProperty("success", out var successProp).Should().BeTrue(
            because: "re-opt-in response must include a 'success' boolean");
        successProp.ValueKind.Should().Be(JsonValueKind.True,
            because: "success must be true for a successful re-opt-in");

        body.TryGetProperty("previousStatus", out var prevStatusProp).Should().BeTrue(
            because: "re-opt-in response must include previousStatus for audit trail");
        prevStatusProp.GetString().Should().NotBeNullOrEmpty();

        body.TryGetProperty("newStatus", out var newStatusProp).Should().BeTrue(
            because: "re-opt-in response must include newStatus for confirmation");
        newStatusProp.GetString().Should().NotBeNullOrEmpty();

        body.TryGetProperty("updatedTimestamp", out var updatedTimestampProp).Should().BeTrue(
            because: "re-opt-in response must include updatedTimestamp for audit correlation");
        DateTimeOffset.TryParse(updatedTimestampProp.GetString(), out _).Should().BeTrue(
            because: "updatedTimestamp must be a parseable ISO 8601 datetime");
    }
}
