// Tests for OutboundSmsGate
// Source: TASK (SMS Proxy & Routing) | SPEC-001, SPEC-006, SPEC-009, NFS-005
// Covers: opt-in forwarding, opt-out suppression, unregistered account, fail-closed behavior

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

public sealed class OutboundSmsGateTests
{
    private readonly Mock<IApplicationRegistryService> _applicationRegistry;
    private readonly Mock<IOptOutStatusService> _optOutStatusService;
    private readonly Mock<ICoolTextClient> _coolTextClient;
    private readonly Mock<IAuditLogService> _auditLogService;
    private readonly OutboundSmsGate _sut;

    private static readonly ApplicationRegistryEntry TestApplication = new()
    {
        CoolTextAccountNumber = "ACC-001",
        ApplicationName = "GCMA",
        CallbackUrl = "https://gcma.example.com/webhook",
        IsActive = true,
        OnboardedDate = new DateOnly(2025, 1, 1)
    };

    private static readonly OutboundSmsRequest ValidRequest = new()
    {
        CoolTextAccountId = "ACC-001",
        DestinationCellNumber = "+12025551234",
        MessageBody = "Your appointment is confirmed."
    };

    public OutboundSmsGateTests()
    {
        _applicationRegistry = new Mock<IApplicationRegistryService>();
        _optOutStatusService = new Mock<IOptOutStatusService>();
        _coolTextClient = new Mock<ICoolTextClient>();
        _auditLogService = new Mock<IAuditLogService>();

        _sut = new OutboundSmsGate(
            _applicationRegistry.Object,
            _optOutStatusService.Object,
            _coolTextClient.Object,
            _auditLogService.Object,
            new Mock<ILogger<OutboundSmsGate>>().Object);
    }

    [Fact]
    public async Task Should_ForwardSms_And_ReturnAllowed_When_NumberIsOptedIn()
    {
        // Arrange
        _applicationRegistry
            .Setup(r => r.GetByAccountNumberAsync("ACC-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestApplication);

        _optOutStatusService
            .Setup(s => s.IsOptedOutAsync("+12025551234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _coolTextClient
            .Setup(c => c.SendSmsAsync("ACC-001", "+12025551234", "Your appointment is confirmed.", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendSmsResult { MessageId = "CT-MSG-001" });

        // Act
        var result = await _sut.ProcessAsync(ValidRequest);

        // Assert
        result.Decision.Should().Be(OutboundGateDecision.Forwarded);
        result.MessageId.Should().Be("CT-MSG-001");

        _coolTextClient.Verify(
            c => c.SendSmsAsync("ACC-001", "+12025551234", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Should_SuppressSms_And_WriteBlockedAuditEntry_And_ReturnBlocked_When_NumberIsOptedOut()
    {
        // Arrange
        _applicationRegistry
            .Setup(r => r.GetByAccountNumberAsync("ACC-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestApplication);

        _optOutStatusService
            .Setup(s => s.IsOptedOutAsync("+12025551234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _auditLogService
            .Setup(a => a.WriteBlockedOutboundEventAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.ProcessAsync(ValidRequest);

        // Assert
        result.Decision.Should().Be(OutboundGateDecision.Suppressed);
        result.MessageId.Should().BeNull();

        // Message must NOT be forwarded to Cool Text
        _coolTextClient.Verify(
            c => c.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Audit entry for blocked outbound must be written (SPEC-009)
        _auditLogService.Verify(
            a => a.WriteBlockedOutboundEventAsync(
                "+12025551234", "ACC-001", "GCMA",
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Should_ReturnUnregisteredAccount_When_AccountIsNotInRegistry()
    {
        // Arrange — registry returns null for unregistered account
        _applicationRegistry
            .Setup(r => r.GetByAccountNumberAsync("UNREG-ACC", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationRegistryEntry?)null);

        var unregisteredRequest = new OutboundSmsRequest
        {
            CoolTextAccountId = "UNREG-ACC",
            DestinationCellNumber = "+12025551234",
            MessageBody = "Test message"
        };

        // Act
        var result = await _sut.ProcessAsync(unregisteredRequest);

        // Assert
        result.Decision.Should().Be(OutboundGateDecision.UnregisteredAccount);

        // No opt-out check or forwarding should happen for unregistered accounts (BR-004)
        _optOutStatusService.Verify(
            s => s.IsOptedOutAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _coolTextClient.Verify(
            c => c.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_ThrowOutboundGateUnavailableException_When_StatusCheckFails()
    {
        // Arrange — fail-closed: any exception on status check must throw OutboundGateUnavailableException (NFS-005)
        _applicationRegistry
            .Setup(r => r.GetByAccountNumberAsync("ACC-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestApplication);

        _optOutStatusService
            .Setup(s => s.IsOptedOutAsync("+12025551234", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database connection failed"));

        // Act
        var act = async () => await _sut.ProcessAsync(ValidRequest);

        // Assert — must throw OutboundGateUnavailableException, not the raw DB exception
        await act.Should().ThrowAsync<OutboundGateUnavailableException>()
            .WithMessage("*opt-out status unavailable*");

        // No message forwarded when we cannot confirm status (fail-closed)
        _coolTextClient.Verify(
            c => c.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_ReturnUnregisteredAccount_When_ApplicationIsInactive()
    {
        // Arrange — inactive application should be treated as unregistered (BR-063)
        // The application registry service only returns active entries; inactive ones return null.
        _applicationRegistry
            .Setup(r => r.GetByAccountNumberAsync("ACC-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationRegistryEntry?)null); // null = inactive treated as not found

        // Act
        var result = await _sut.ProcessAsync(ValidRequest);

        // Assert
        result.Decision.Should().Be(OutboundGateDecision.UnregisteredAccount);
    }
}
