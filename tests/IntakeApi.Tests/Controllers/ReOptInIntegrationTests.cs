using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ConsentService.Models;
using FluentAssertions;
using IntakeApi.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace IntakeApi.Tests.Controllers;

public sealed class ReOptInIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ReOptInIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Service-Auth", "integration-test");
    }

    [Fact]
    public async Task Post_WithFormChannelAndProof_ReturnsUpdated()
    {
        var request = new ReOptInRequest
        {
            ReOptInRequestId = "reopt-1",
            CustomerPhoneNumber = "+14045550155",
            InitiationChannel = ReOptInChannel.Form,
            InitiatedAtUtc = DateTimeOffset.UtcNow
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/consent/reoptin")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        message.Headers.Add("X-ReOptIn-Proof", "form-proof");
        message.Headers.Add("X-Request-Nonce", "nonce-1");

        var response = await _client.SendAsync(message);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ReOptInResponse>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.UpdateResult.Should().Be("UPDATED");
        payload.UpdatedConsentStatus.Should().Be("OPT-IN");
    }

    [Fact]
    public async Task Post_WithoutProof_ReturnsUnauthorized()
    {
        var request = new ReOptInRequest
        {
            ReOptInRequestId = "reopt-2",
            CustomerPhoneNumber = "+14045550156",
            InitiationChannel = ReOptInChannel.SmsResponse,
            InitiatedAtUtc = DateTimeOffset.UtcNow
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/consent/reoptin")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };

        var response = await _client.SendAsync(message);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var payload = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.Code.Should().Be("REOPTIN_NOT_AUTHORIZED");
    }
}
