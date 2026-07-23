// Tests for OutboundMessagesController — POST /api/v1/messages/outbound
// Source: TASK-008 | SPEC-006, SPEC-007 | BR-018-BR-023

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

public class OutboundMessagesControllerTests
{
    private readonly ICoolTextAccountRepository _coolTextRepo = Substitute.For<ICoolTextAccountRepository>();
    private readonly IOptOutStatusRepository _statusRepo = Substitute.For<IOptOutStatusRepository>();
    private readonly IProcessedMessageRepository _processedRepo = Substitute.For<IProcessedMessageRepository>();
    private readonly IMessagePublisher _publisher = Substitute.For<IMessagePublisher>();
    private readonly IPhoneNumberHasher _hasher = Substitute.For<IPhoneNumberHasher>();
    private readonly ILogger<OutboundMessagesController> _logger = Substitute.For<ILogger<OutboundMessagesController>>();

    private OutboundMessagesController BuildSut()
        => new(_coolTextRepo, _statusRepo, _processedRepo, _publisher, _hasher, _logger);

    private static OutboundMessageRequest ValidRequest(string? correlationId = null) => new()
    {
        ToNumber = "+14045551234",
        Body = "Your bill is due.",
        CoolTextAccountNumber = "CT-001",
        ApplicationId = "biztalk",
        CorrelationId = correlationId
    };

    private void SetupValidAccount()
        => _coolTextRepo.GetByAccountNumberAsync("CT-001", default)
            .Returns(new CoolTextAccount { AccountNumber = "CT-001", ApplicationId = "biztalk", IsActive = true });

    [Fact]
    public async Task SubmitOutbound_OptedInNumber_ReturnsQueued()
    {
        SetupValidAccount();
        _processedRepo.FindAsync(Arg.Any<string>(), Arg.Any<string>(), default).Returns((ProcessedMessage?)null);
        _statusRepo.IsOptedOutAsync("+14045551234", default).Returns(false);

        var result = await BuildSut().SubmitOutbound(ValidRequest(), default);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<OutboundMessageResponse>().Subject;
        response.Status.Should().Be("queued");
        response.MessageId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SubmitOutbound_OptedOutNumber_ReturnsSuppressed()
    {
        SetupValidAccount();
        _processedRepo.FindAsync(Arg.Any<string>(), Arg.Any<string>(), default).Returns((ProcessedMessage?)null);
        _statusRepo.IsOptedOutAsync("+14045551234", default).Returns(true);

        var result = await BuildSut().SubmitOutbound(ValidRequest(), default);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<OutboundMessageResponse>().Subject;
        response.Status.Should().Be("suppressed");
        response.SuppressionReason.Should().Be("opted-out");
        await _publisher.DidNotReceive().PublishOutboundAsync(Arg.Any<OutboundMessageEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitOutbound_UnknownAccount_Returns400()
    {
        _coolTextRepo.GetByAccountNumberAsync(Arg.Any<string>(), default).Returns((CoolTextAccount?)null);

        var result = await BuildSut().SubmitOutbound(ValidRequest(), default);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SubmitOutbound_DuplicateCorrelationId_ReturnsOriginalResponse()
    {
        SetupValidAccount();
        var existingMsgId = Guid.NewGuid();
        _processedRepo.FindAsync("corr-001", "outbound", default)
            .Returns(new ProcessedMessage
            {
                MessageId = "corr-001",
                InternalId = existingMsgId,
                ResponseStatus = "queued",
                Endpoint = "outbound",
                ProcessedAt = DateTime.UtcNow
            });

        var result = await BuildSut().SubmitOutbound(ValidRequest(correlationId: "corr-001"), default);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<OutboundMessageResponse>().Subject;
        response.Status.Should().Be("queued");
        await _publisher.DidNotReceive().PublishOutboundAsync(Arg.Any<OutboundMessageEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitOutbound_KafkaFails_Returns503()
    {
        SetupValidAccount();
        _processedRepo.FindAsync(Arg.Any<string>(), Arg.Any<string>(), default).Returns((ProcessedMessage?)null);
        _statusRepo.IsOptedOutAsync(Arg.Any<string>(), default).Returns(false);
        _publisher.PublishOutboundAsync(Arg.Any<OutboundMessageEvent>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Kafka unavailable"));

        var result = await BuildSut().SubmitOutbound(ValidRequest(), default);

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task SubmitOutbound_StatusStoreUnavailable_Returns503()
    {
        SetupValidAccount();
        _processedRepo.FindAsync(Arg.Any<string>(), Arg.Any<string>(), default).Returns((ProcessedMessage?)null);
        _statusRepo.IsOptedOutAsync(Arg.Any<string>(), default)
            .ThrowsAsync(new Exception("DB unavailable"));

        var result = await BuildSut().SubmitOutbound(ValidRequest(), default);

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(503);
    }
}
