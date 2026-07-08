// Tests for InboundSmsHandler
// Source: TASK (SMS Proxy & Routing) | SPEC-002, SPEC-003, SPEC-004, SPEC-005
// Covers: opt-out keyword detection, confirmation dispatch, message forwarding, idempotency

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TCPA.Api.Infrastructure.Configuration;
using TCPA.Api.Infrastructure.CoolText;
using TCPA.Api.Models;
using TCPA.Api.Services.AuditLog;
using TCPA.Api.Services.OptOut;
using TCPA.Api.Services.SmsProxy;
using Xunit;

namespace TCPA.Api.Tests.Unit.SmsProxy;

public sealed class InboundSmsHandlerTests
{
    private readonly Mock<IApplicationRegistryService> _applicationRegistry;
    private readonly Mock<IOptOutDetector> _optOutDetector;
    private readonly Mock<IOptOutStatusService> _optOutStatusService;
    private readonly Mock<IConfirmationDispatcher> _confirmationDispatcher;
    private readonly Mock<ICoolTextForwardingClient> _forwardingClient;
    private readonly Mock<IAuditLogService> _auditLogService;
    private readonly InboundSmsHandler _sut;

    private static readonly ApplicationRegistryEntry TestApplication = new()
    {
        CoolTextAccountNumber = "ACC-001",
        ApplicationName = "GCMA",
        CallbackUrl = "https://gcma.example.com/webhook",
        IsActive = true,
        OnboardedDate = new DateOnly(2025, 1, 1)
    };

    private static readonly InboundSmsMessage TestMessage = new()
    {
        CoolTextAccountId = "ACC-001",
        SenderCellNumber = "+12025551234",
        MessageBody = "STOP",
        CoolTextMessageId = "MSG-001"
    };

    private static readonly InboundSmsMessage NonKeywordMessage = new()
    {
        CoolTextAccountId = "ACC-001",
        SenderCellNumber = "+12025551234",
        MessageBody = "Hello, please call me back",
        CoolTextMessageId = "MSG-002"
    };

    public InboundSmsHandlerTests()
    {
        _applicationRegistry = new Mock<IApplicationRegistryService>();
        _optOutDetector = new Mock<IOptOutDetector>();
        _optOutStatusService = new Mock<IOptOutStatusService>();
        _confirmationDispatcher = new Mock<IConfirmationDispatcher>();
        _forwardingClient = new Mock<ICoolTextForwardingClient>();
        _auditLogService = new Mock<IAuditLogService>();

        _sut = new InboundSmsHandler(
            _applicationRegistry.Object,
            _optOutDetector.Object,
            _optOutStatusService.Object,
            _confirmationDispatcher.Object,
            _forwardingClient.Object,
            _auditLogService.Object,
            new Mock<ILogger<InboundSmsHandler>>().Object);
    }

    [Fact]
    public async Task Should_CallSetOptOut_And_SendConfirmation_And_ForwardToApp_When_OptOutKeywordDetected()
    {
        // Arrange
        _applicationRegistry
            .Setup(r => r.GetByAccountNumberAsync("ACC-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestApplication);

        _optOutDetector
            .Setup(d => d.Detect("STOP"))
            .Returns(new KeywordDetectionResult { IsOptOutKeyword = true, MatchedKeyword = "STOP" });

        _optOutStatusService
            .Setup(s => s.WriteOptOutAsync("+12025551234", It.IsAny<DateTime>(), "GCMA", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WriteOptOutResult { StatusWriteSuccess = true, PreviousStatus = "OPT_IN" });

        _confirmationDispatcher
            .Setup(d => d.DispatchAsync("+12025551234", "ACC-001", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfirmationDispatchResult { ConfirmationSent = true, CoolTextMessageId = "CONF-001" });

        _forwardingClient
            .Setup(c => c.ForwardToApplicationAsync(TestApplication.CallbackUrl, TestMessage))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.HandleAsync(TestMessage);

        // Assert
        _optOutStatusService.Verify(
            s => s.WriteOptOutAsync("+12025551234", It.IsAny<DateTime>(), "GCMA", It.IsAny<CancellationToken>()),
            Times.Once);

        _confirmationDispatcher.Verify(
            d => d.DispatchAsync("+12025551234", "ACC-001", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _forwardingClient.Verify(
            c => c.ForwardToApplicationAsync(TestApplication.CallbackUrl, TestMessage),
            Times.Once);
    }

    [Fact]
    public async Task Should_OnlyForwardToApp_When_NoOptOutKeywordDetected()
    {
        // Arrange
        _applicationRegistry
            .Setup(r => r.GetByAccountNumberAsync("ACC-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestApplication);

        _optOutDetector
            .Setup(d => d.Detect("Hello, please call me back"))
            .Returns(new KeywordDetectionResult { IsOptOutKeyword = false });

        _forwardingClient
            .Setup(c => c.ForwardToApplicationAsync(TestApplication.CallbackUrl, NonKeywordMessage))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.HandleAsync(NonKeywordMessage);

        // Assert — no opt-out calls
        _optOutStatusService.Verify(
            s => s.WriteOptOutAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _confirmationDispatcher.Verify(
            d => d.DispatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _forwardingClient.Verify(
            c => c.ForwardToApplicationAsync(TestApplication.CallbackUrl, NonKeywordMessage),
            Times.Once);
    }

    [Fact]
    public async Task Should_NotCrash_And_LogWarning_When_CoolTextAccountIsUnknown()
    {
        // Arrange — registry returns null for unknown account
        _applicationRegistry
            .Setup(r => r.GetByAccountNumberAsync("UNKNOWN-ACC", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationRegistryEntry?)null);

        var messageWithUnknownAccount = new InboundSmsMessage
        {
            CoolTextAccountId = "UNKNOWN-ACC",
            SenderCellNumber = "+12025559999",
            MessageBody = "STOP",
            CoolTextMessageId = "MSG-003"
        };

        // Act — should complete without throwing
        var act = async () => await _sut.HandleAsync(messageWithUnknownAccount);

        // Assert
        await act.Should().NotThrowAsync();

        // No forwarding or opt-out should occur for unregistered accounts
        _forwardingClient.Verify(
            c => c.ForwardToApplicationAsync(It.IsAny<string>(), It.IsAny<InboundSmsMessage>()),
            Times.Never);
        _optOutStatusService.Verify(
            s => s.WriteOptOutAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_StillForwardToApp_When_ConfirmationDispatchFails()
    {
        // Arrange — confirmation throws but message must still be forwarded (BR-025)
        _applicationRegistry
            .Setup(r => r.GetByAccountNumberAsync("ACC-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestApplication);

        _optOutDetector
            .Setup(d => d.Detect("STOP"))
            .Returns(new KeywordDetectionResult { IsOptOutKeyword = true, MatchedKeyword = "STOP" });

        _optOutStatusService
            .Setup(s => s.WriteOptOutAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WriteOptOutResult { StatusWriteSuccess = true, PreviousStatus = "OPT_IN" });

        // Confirmation dispatch throws
        _confirmationDispatcher
            .Setup(d => d.DispatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("SMS gateway unavailable"));

        _forwardingClient
            .Setup(c => c.ForwardToApplicationAsync(TestApplication.CallbackUrl, TestMessage))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.HandleAsync(TestMessage);

        // Assert — forwarding still happens despite confirmation failure
        _forwardingClient.Verify(
            c => c.ForwardToApplicationAsync(TestApplication.CallbackUrl, TestMessage),
            Times.Once);
    }

    [Fact]
    public async Task Should_ForwardToApp_And_NotSendDoubleConfirmation_When_AlreadyOptedOut()
    {
        // Arrange — number was already OPT_OUT (idempotent case, BR-019, BR-023)
        _applicationRegistry
            .Setup(r => r.GetByAccountNumberAsync("ACC-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestApplication);

        _optOutDetector
            .Setup(d => d.Detect("STOP"))
            .Returns(new KeywordDetectionResult { IsOptOutKeyword = true, MatchedKeyword = "STOP" });

        // PreviousStatus = "OPT_OUT" means number was already opted out
        _optOutStatusService
            .Setup(s => s.WriteOptOutAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WriteOptOutResult { StatusWriteSuccess = true, PreviousStatus = "OPT_OUT" });

        _forwardingClient
            .Setup(c => c.ForwardToApplicationAsync(TestApplication.CallbackUrl, TestMessage))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.HandleAsync(TestMessage);

        // Assert — confirmation is NOT sent again (BR-023), but forward still happens (SPEC-002)
        _confirmationDispatcher.Verify(
            d => d.DispatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _forwardingClient.Verify(
            c => c.ForwardToApplicationAsync(TestApplication.CallbackUrl, TestMessage),
            Times.Once);
    }

    [Fact]
    public async Task Should_StillForwardToApp_When_OptOutStatusWriteFails()
    {
        // Arrange — status write throws; forwarding must still occur per SPEC-002
        _applicationRegistry
            .Setup(r => r.GetByAccountNumberAsync("ACC-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestApplication);

        _optOutDetector
            .Setup(d => d.Detect("STOP"))
            .Returns(new KeywordDetectionResult { IsOptOutKeyword = true, MatchedKeyword = "STOP" });

        _optOutStatusService
            .Setup(s => s.WriteOptOutAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB unavailable"));

        _forwardingClient
            .Setup(c => c.ForwardToApplicationAsync(TestApplication.CallbackUrl, TestMessage))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.HandleAsync(TestMessage);

        // Assert — confirmation must NOT be sent per BR-017, but forwarding still occurs
        _confirmationDispatcher.Verify(
            d => d.DispatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _forwardingClient.Verify(
            c => c.ForwardToApplicationAsync(TestApplication.CallbackUrl, TestMessage),
            Times.Once);
    }
}
