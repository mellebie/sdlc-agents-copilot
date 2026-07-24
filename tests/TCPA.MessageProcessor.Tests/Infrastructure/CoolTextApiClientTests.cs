using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using FluentAssertions;
using Xunit;
using TCPA.MessageProcessor.Infrastructure;

namespace TCPA.MessageProcessor.Tests.Infrastructure;

public class CoolTextApiClientTests
{
    private static CoolTextApiClient BuildClient(HttpClient httpClient)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CoolText:ApiKey"] = "test-api-key"
            })
            .Build();
        var logger = Substitute.For<ILogger<CoolTextApiClient>>();
        return new CoolTextApiClient(httpClient, config, logger);
    }

    [Fact]
    public async Task SendSmsAsync_WhenApiReturns200_ReturnsSuccessWithMessageId()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK,
            """{"messageId":"msg-abc","status":"sent"}""");
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.cooltext.example.com")
        };
        var sut = BuildClient(httpClient);

        // Act
        var result = await sut.SendSmsAsync("+12025551234", "CT-001", "You have been unsubscribed.", CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.MessageId.Should().Be("msg-abc");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task SendSmsAsync_WhenApiReturns400_ReturnsFailureResult()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler(HttpStatusCode.BadRequest,
            """{"error":"Invalid phone number"}""");
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.cooltext.example.com")
        };
        var sut = BuildClient(httpClient);

        // Act
        var result = await sut.SendSmsAsync("+12025551234", "CT-001", "body", CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.MessageId.Should().BeNull();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SendSmsAsync_WhenNetworkThrows_PropagatesException()
    {
        // Arrange
        var handler = new ThrowingHttpMessageHandler(new HttpRequestException("Connection refused"));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.cooltext.example.com")
        };
        var sut = BuildClient(httpClient);

        // Act
        var act = () => sut.SendSmsAsync("+12025551234", "CT-001", "body", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }
}

// Test helpers
internal class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _body;

    public FakeHttpMessageHandler(HttpStatusCode status, string body)
    {
        _status = status;
        _body = body;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(new HttpResponseMessage(_status)
        {
            Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json")
        });
}

internal class ThrowingHttpMessageHandler : HttpMessageHandler
{
    private readonly Exception _exception;
    public ThrowingHttpMessageHandler(Exception exception) => _exception = exception;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => throw _exception;
}
