// Tests for InboundWebhookController — POST /webhook/inbound
// Source: TASK-007 | SPEC-001 | BR-001, BR-002, BR-003

using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TCPA.Api.Controllers;
using TCPA.Api.Messaging;
using TCPA.Api.Models;
using TCPA.Core.Interfaces;
using TCPA.Core.Models;
using TCPA.Core.Services;
using Xunit;

namespace TCPA.Api.Tests.Controllers;

public class InboundWebhookControllerTests
{
    private readonly ICoolTextAccountRepository _coolTextRepo = Substitute.For<ICoolTextAccountRepository>();
    private readonly IProcessedMessageRepository _processedRepo = Substitute.For<IProcessedMessageRepository>();
    private readonly IMessagePublisher _publisher = Substitute.For<IMessagePublisher>();
    private readonly IPhoneNumberHasher _hasher = Substitute.For<IPhoneNumberHasher>();
    private readonly ILogger<InboundWebhookController> _logger = Substitute.For<ILogger<InboundWebhookController>>();

    private InboundWebhookController BuildSut()
        => new(_coolTextRepo, _processedRepo, _publisher, _hasher, _logger);

    private static InboundWebhookRequest ValidRequest() => new()
    {
        From = "+14045551234",
        To = "+18005559876",
        Body = "STOP",
        Provider = "cooltext",
        MessageId = "msg-001",
        Timestamp = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task ReceiveInbound_ValidRequest_Returns200WithReceivedStatus()
    {
        // Arrange
        _coolTextRepo.GetByAccountNumberAsync("+18005559876", default)
            .Returns(new CoolTextAccount { AccountNumber = "+18005559876", ApplicationId = "biztalk", IsActive = true });
        _processedRepo.FindAsync("msg-001", "webhook", default).Returns((ProcessedMessage?)null);

        // Act
        var result = await BuildSut().ReceiveInbound(ValidRequest(), default);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<InboundWebhookResponse>().Subject;
        response.Status.Should().Be("received");
        response.InternalId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ReceiveInbound_UnknownToNumber_Returns400()
    {
        // Arrange
        _coolTextRepo.GetByAccountNumberAsync(Arg.Any<string>(), default).Returns((CoolTextAccount?)null);

        // Act
        var result = await BuildSut().ReceiveInbound(ValidRequest(), default);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ReceiveInbound_InactiveAccount_Returns400()
    {
        // Arrange
        _coolTextRepo.GetByAccountNumberAsync(Arg.Any<string>(), default)
            .Returns(new CoolTextAccount { AccountNumber = "+18005559876", IsActive = false });

        // Act
        var result = await BuildSut().ReceiveInbound(ValidRequest(), default);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ReceiveInbound_DuplicateMessageId_Returns200WithOriginalInternalId()
    {
        // Arrange
        var existingInternalId = Guid.NewGuid();
        _coolTextRepo.GetByAccountNumberAsync(Arg.Any<string>(), default)
            .Returns(new CoolTextAccount { AccountNumber = "+18005559876", IsActive = true });
        _processedRepo.FindAsync("msg-001", "webhook", default)
            .Returns(new ProcessedMessage
            {
                MessageId = "msg-001",
                InternalId = existingInternalId,
                ResponseStatus = "received",
                Endpoint = "webhook",
                ProcessedAt = DateTime.UtcNow
            });

        // Act
        var result = await BuildSut().ReceiveInbound(ValidRequest(), default);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<InboundWebhookResponse>().Subject;
        response.InternalId.Should().Be(existingInternalId.ToString());
        await _publisher.DidNotReceive().PublishInboundAsync(Arg.Any<InboundMessageEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveInbound_KafkaPublishFails_Returns500()
    {
        // Arrange
        _coolTextRepo.GetByAccountNumberAsync(Arg.Any<string>(), default)
            .Returns(new CoolTextAccount { AccountNumber = "+18005559876", ApplicationId = "biztalk", IsActive = true });
        _processedRepo.FindAsync(Arg.Any<string>(), Arg.Any<string>(), default).Returns((ProcessedMessage?)null);
        _publisher.PublishInboundAsync(Arg.Any<InboundMessageEvent>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Kafka unavailable"));

        // Act
        var result = await BuildSut().ReceiveInbound(ValidRequest(), default);

        // Assert
        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(500);
    }
}
