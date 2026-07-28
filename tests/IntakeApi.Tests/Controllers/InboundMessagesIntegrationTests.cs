using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using IntakeApi.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace IntakeApi.Tests.Controllers;

public sealed class InboundMessagesIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public InboundMessagesIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Service-Auth", "integration-test");
    }

    [Fact]
    public async Task Post_WithRouteableScopeMapping_ReturnsAcceptedAsync()
    {
        var request = CreateRequest("acct-001", SourceLdc.Vng, SourceApplication.BizTalk);

        var response = await _client.PostAsJsonAsync("/api/v1/inbound/messages", request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<InboundMessageAcceptedResponse>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.Accepted.Should().BeTrue();
        payload.ClassificationState.Should().Be("PENDING");
        payload.CorrelationId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Post_WithMissingMapping_ReturnsOutOfScopeNotFoundAsync()
    {
        var request = CreateRequest("acct-999", SourceLdc.Vng, SourceApplication.BizTalk);

        var response = await _client.PostAsJsonAsync("/api/v1/inbound/messages", request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var payload = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.Code.Should().Be("SCOPE_MAPPING_NOT_FOUND");
        payload.Message.Should().Contain("REJECTED_OUT_OF_SCOPE");
    }

    [Fact]
    public async Task Post_WithMismatchedLdcAndAccount_ReturnsOutOfScopeNotFoundAsync()
    {
        var request = CreateRequest("acct-001", SourceLdc.Cgc, SourceApplication.BizTalk);

        var response = await _client.PostAsJsonAsync("/api/v1/inbound/messages", request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var payload = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.Code.Should().Be("SCOPE_MAPPING_NOT_FOUND");
        payload.Message.Should().Contain("REJECTED_OUT_OF_SCOPE");
    }

    private static InboundMessageRequest CreateRequest(string accountId, SourceLdc sourceLdc, SourceApplication sourceApplication)
    {
        return new InboundMessageRequest
        {
            EventId = Guid.NewGuid().ToString("N"),
            ReceivedAtUtc = DateTimeOffset.UtcNow,
            CustomerPhoneNumber = "+14045550100",
            SourceLdc = sourceLdc,
            SourceApplication = sourceApplication,
            CoolTextAccountId = accountId,
            MessageText = "STOP"
        };
    }
}
