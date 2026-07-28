using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using IntakeApi.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FunctionalTests.Contracts;

public sealed class InboundApiContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public InboundApiContractTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Service-Auth", "integration-test");
    }

    [Fact]
    public async Task InvalidInput_ReturnsErrorShapeContract()
    {
        var request = new InboundMessageRequest
        {
            EventId = string.Empty,
            ReceivedAtUtc = null,
            CustomerPhoneNumber = "invalid",
            SourceLdc = SourceLdc.Unknown,
            SourceApplication = SourceApplication.Unknown,
            CoolTextAccountId = string.Empty,
            MessageText = string.Empty
        };

        var response = await _client.PostAsJsonAsync("/api/v1/inbound/messages", request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.Code.Should().Be("INVALID_INPUT");
        payload.CorrelationId.Should().NotBeNullOrWhiteSpace();
        payload.Message.Should().NotBeNullOrWhiteSpace();
    }
}
