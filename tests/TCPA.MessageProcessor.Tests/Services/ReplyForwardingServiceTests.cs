using System.Net;
using Microsoft.Extensions.Logging;
using NSubstitute;
using FluentAssertions;
using Xunit;
using TCPA.Core.Interfaces;
using TCPA.Core.Services;
using TCPA.MessageProcessor.Messaging;
using TCPA.MessageProcessor.Services;

namespace TCPA.MessageProcessor.Tests.Services;

public class ReplyForwardingServiceTests
{
    private static InboundMessageEvent MakeEvent(string body = "Hello, I need help") =>
        new("int-1", "msg-1", "+12025551234", "CT-001", body, "CoolText", "CT-001", "app1", DateTimeOffset.UtcNow);

    private static ReplyForwardingService BuildSut(HttpClient httpClient)
    {
        var hasher = Substitute.For<IPhoneNumberHasher>();
        hasher.Hash(Arg.Any<string>()).Returns(args => "hashed:" + args[0]);
        return new ReplyForwardingService(httpClient, hasher, Substitute.For<ILogger<ReplyForwardingService>>());
    }

    [Fact]
    public async Task ForwardReplyAsync_WhenCallbackReturns200_CompletesWithoutThrowing()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "");
        using var httpClient = new HttpClient(handler);
        var sut = BuildSut(httpClient);

        // Act
        var act = () => sut.ForwardReplyAsync(MakeEvent(), "https://app.example.com/callback", CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ForwardReplyAsync_WhenCallbackReturns500_DoesNotThrow()
    {
        // Arrange — non-2xx must not propagate (BR-017: best-effort, no retry)
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "error");
        using var httpClient = new HttpClient(handler);
        var sut = BuildSut(httpClient);

        // Act
        var act = () => sut.ForwardReplyAsync(MakeEvent(), "https://app.example.com/callback", CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ForwardReplyAsync_WhenNetworkThrows_DoesNotThrow()
    {
        // Arrange
        var handler = new ThrowingHttpMessageHandler(new HttpRequestException("Connection refused"));
        using var httpClient = new HttpClient(handler);
        var sut = BuildSut(httpClient);

        // Act
        var act = () => sut.ForwardReplyAsync(MakeEvent(), "https://app.example.com/callback", CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ForwardReplyAsync_ForwardsBodyUnmodified()
    {
        // Arrange
        string? capturedBody = null;
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.OK, body => capturedBody = body);
        using var httpClient = new HttpClient(handler);
        var sut = BuildSut(httpClient);
        var @event = MakeEvent("Hello, I need help with my bill");

        // Act
        await sut.ForwardReplyAsync(@event, "https://app.example.com/callback", CancellationToken.None);

        // Assert — BR-015: body forwarded byte-for-byte identical
        capturedBody.Should().Be("Hello, I need help with my bill");
    }
}

// Test helpers — local definitions to avoid namespace collision
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

internal class CapturingHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly Action<string> _capture;

    public CapturingHttpMessageHandler(HttpStatusCode status, Action<string> capture)
    {
        _status = status;
        _capture = capture;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Content is not null)
            _capture(await request.Content.ReadAsStringAsync(ct));
        return new HttpResponseMessage(_status);
    }
}
