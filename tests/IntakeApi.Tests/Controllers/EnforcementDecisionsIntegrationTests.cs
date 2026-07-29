using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using IntakeApi.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace IntakeApi.Tests.Controllers;

public sealed class EnforcementDecisionsIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public EnforcementDecisionsIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Service-Auth", "integration-test");
    }

    [Fact]
    public async Task Post_WithOptOutConsent_ReturnsBlockDecision()
    {
        var request = new EnforcementDecisionRequest
        {
            OutboundRequestId = "out-1",
            CustomerPhoneNumber = "+14045550100",
            SourceApplication = SourceApplication.BizTalk,
            SourceLdc = SourceLdc.Vng,
            ApplicationReportedStatus = ConsentDecisionStatus.Unknown
        };

        var response = await _client.PostAsJsonAsync("/api/v1/enforcement/decisions", request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<EnforcementDecisionResponse>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.EnforcementDecision.Should().Be("BLOCK");
        payload.DecisionReason.Should().Be("API_OPTED_OUT");
    }

    [Fact]
    public async Task Post_WhenConsentLookupFails_ReturnsGuardedFailure()
    {
        var request = new EnforcementDecisionRequest
        {
            OutboundRequestId = "out-2",
            CustomerPhoneNumber = "+14045550999",
            SourceApplication = SourceApplication.BizTalk,
            SourceLdc = SourceLdc.Vng,
            ApplicationReportedStatus = ConsentDecisionStatus.Unknown
        };

        var response = await _client.PostAsJsonAsync("/api/v1/enforcement/decisions", request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var payload = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.Code.Should().Be("ENFORCEMENT_UNAVAILABLE");
    }

    [Fact]
    public async Task Post_WithDivergence_ReturnsAllowWithPrecedenceReason()
    {
        var request = new EnforcementDecisionRequest
        {
            OutboundRequestId = "out-3",
            CustomerPhoneNumber = "+14045550100",
            SourceApplication = SourceApplication.BizTalk,
            SourceLdc = SourceLdc.Vng,
            ApplicationReportedStatus = ConsentDecisionStatus.OptIn
        };

        var response = await _client.PostAsJsonAsync("/api/v1/enforcement/decisions", request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<EnforcementDecisionResponse>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.EnforcementDecision.Should().Be("ALLOW");
        payload.DecisionReason.Should().Be("APP_STATUS_TAKES_PRECEDENCE");
    }
}
