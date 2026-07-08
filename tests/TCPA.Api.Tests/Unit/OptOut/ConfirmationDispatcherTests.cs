// tests/TCPA.Api.Tests/Unit/OptOut/ConfirmationDispatcherTests.cs
// Tests for ConfirmationDispatcher — opt-out confirmation SMS dispatch
// Source: TASK-021 | SPEC-005 | STORY-006
// Business Rules: BR-021 through BR-026

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TCPA.Api.Infrastructure.CoolText;
using TCPA.Api.Services.OptOut;
using Xunit;

namespace TCPA.Api.Tests.Unit.OptOut;

/// <summary>
/// Tests for <see cref="ConfirmationDispatcher"/>.
/// Verifies: successful dispatch, SLA breach logging, single retry on failure,
/// permanent failure does not throw and does not reverse opt-out.
/// </summary>
public sealed class ConfirmationDispatcherTests
{
    private const string ConfirmationTextKey = "TCPA:OptOutConfirmationSmsText";
    private const string ApprovedConfirmationText = "You have been unsubscribed. Reply START to re-subscribe.";

    private readonly Mock<ICoolTextClient> _coolTextClientMock;
    private readonly Mock<ILogger<ConfirmationDispatcher>> _loggerMock;

    public ConfirmationDispatcherTests()
    {
        _coolTextClientMock = new Mock<ICoolTextClient>();
        _loggerMock = new Mock<ILogger<ConfirmationDispatcher>>();
    }

    private ConfirmationDispatcher BuildSut(string? confirmationText = ApprovedConfirmationText)
    {
        var configData = new Dictionary<string, string?>();
        if (confirmationText is not null)
        {
            configData[ConfirmationTextKey] = confirmationText;
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        return new ConfirmationDispatcher(_coolTextClientMock.Object, configuration, _loggerMock.Object);
    }

    // -----------------------------------------------------------------------
    // Successful dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_ReturnSuccess_When_CoolTextAcceptsConfirmationSms()
    {
        // Arrange
        const string expectedMessageId = "MSG-12345";
        _coolTextClientMock
            .Setup(c => c.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(expectedMessageId);

        ConfirmationDispatcher sut = BuildSut();

        // Act
        ConfirmationDispatchResult result = await sut.DispatchAsync(
            "+12025551234", "ACCOUNT-001", DateTime.UtcNow);

        // Assert
        result.ConfirmationSent.Should().BeTrue();
        result.CoolTextMessageId.Should().Be(expectedMessageId);
        result.SendTimestamp.Should().NotBeNull();
    }

    [Fact]
    public async Task Should_CallCoolTextWithCorrectParameters_When_Dispatching()
    {
        // Arrange
        const string cellNumber = "+12025551234";
        const string accountId = "ACCOUNT-001";

        _coolTextClientMock
            .Setup(c => c.SendSmsAsync(accountId, cellNumber, ApprovedConfirmationText))
            .ReturnsAsync("MSG-001");

        ConfirmationDispatcher sut = BuildSut();

        // Act
        await sut.DispatchAsync(cellNumber, accountId, DateTime.UtcNow);

        // Assert — must use the correct account ID (BR-024: confirmation from same sender)
        _coolTextClientMock.Verify(
            c => c.SendSmsAsync(accountId, cellNumber, ApprovedConfirmationText),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // SLA breach logging
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_LogSlaBreachWarning_When_ElapsedExceeds60Seconds()
    {
        // Arrange — receipt timestamp 90 seconds ago to guarantee SLA breach
        DateTime receiptTimestamp = DateTime.UtcNow.AddSeconds(-90);

        _coolTextClientMock
            .Setup(c => c.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("MSG-LATE");

        ConfirmationDispatcher sut = BuildSut();

        // Act
        ConfirmationDispatchResult result = await sut.DispatchAsync(
            "+12025551234", "ACCOUNT-001", receiptTimestamp);

        // Assert — SLA elapsed must exceed 60
        result.SlaElapsedSeconds.Should().BeGreaterThan(60,
            because: "dispatch occurred more than 60s after receipt");

        // Verify error-level log was emitted for SLA breach
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SLA BREACH")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Should_NotLogSlaBreachWarning_When_ElapsedIsWithin60Seconds()
    {
        // Arrange — receipt is effectively now (0 elapsed)
        _coolTextClientMock
            .Setup(c => c.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("MSG-FAST");

        ConfirmationDispatcher sut = BuildSut();

        // Act
        await sut.DispatchAsync("+12025551234", "ACCOUNT-001", DateTime.UtcNow);

        // Assert — no SLA BREACH error log
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SLA BREACH")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // Retry on first failure — succeeds on second attempt
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_RetryOnce_When_FirstCoolTextAttemptFails()
    {
        // Arrange — first call throws, second call succeeds
        _coolTextClientMock
            .SetupSequence(c => c.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("Network error"))
            .ReturnsAsync("MSG-RETRY-OK");

        ConfirmationDispatcher sut = BuildSut();

        // Act
        ConfirmationDispatchResult result = await sut.DispatchAsync(
            "+12025551234", "ACCOUNT-001", DateTime.UtcNow);

        // Assert — succeeded on retry
        result.ConfirmationSent.Should().BeTrue();
        result.CoolTextMessageId.Should().Be("MSG-RETRY-OK");

        // Verify two calls were made (original + 1 retry)
        _coolTextClientMock.Verify(
            c => c.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Exactly(2));
    }

    // -----------------------------------------------------------------------
    // Permanent failure after both attempts — returns failure, does NOT throw
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_ReturnFailure_When_BothCoolTextAttemptsFail()
    {
        // Arrange — both attempts throw
        _coolTextClientMock
            .Setup(c => c.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("Network down"));

        ConfirmationDispatcher sut = BuildSut();

        // Act
        ConfirmationDispatchResult result = await sut.DispatchAsync(
            "+12025551234", "ACCOUNT-001", DateTime.UtcNow);

        // Assert — failure returned, not thrown (opt-out status NOT reversed — BR-025)
        result.ConfirmationSent.Should().BeFalse();
        result.CoolTextMessageId.Should().BeNull();
        result.SendTimestamp.Should().BeNull();
    }

    [Fact]
    public async Task Should_NotThrow_When_PermanentCoolTextFailureOccurs()
    {
        // Arrange
        _coolTextClientMock
            .Setup(c => c.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("Service down"));

        ConfirmationDispatcher sut = BuildSut();

        // Act — must not propagate exception; opt-out is already written (BR-025)
        Func<Task> act = async () => await sut.DispatchAsync(
            "+12025551234", "ACCOUNT-001", DateTime.UtcNow);

        // Assert
        await act.Should().NotThrowAsync(
            because: "a Cool Text delivery failure must not reverse or affect the opt-out status");
    }

    [Fact]
    public async Task Should_CallCoolTextExactlyTwice_When_BothAttemptsFail()
    {
        // Arrange
        _coolTextClientMock
            .Setup(c => c.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("Failure"));

        ConfirmationDispatcher sut = BuildSut();

        // Act
        await sut.DispatchAsync("+12025551234", "ACCOUNT-001", DateTime.UtcNow);

        // Assert — exactly 2 attempts (no more, no fewer)
        _coolTextClientMock.Verify(
            c => c.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Exactly(2));
    }

    // -----------------------------------------------------------------------
    // Missing confirmation text configuration
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_ReturnFailure_When_ConfirmationTextConfigKeyIsMissing()
    {
        // Arrange — configuration with no confirmation text key
        ConfirmationDispatcher sut = BuildSut(confirmationText: null);

        // Act
        ConfirmationDispatchResult result = await sut.DispatchAsync(
            "+12025551234", "ACCOUNT-001", DateTime.UtcNow);

        // Assert
        result.ConfirmationSent.Should().BeFalse();

        // Cool Text should not have been called at all
        _coolTextClientMock.Verify(
            c => c.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // Argument validation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_ThrowArgumentException_When_CellPhoneNumberIsWhiteSpace()
    {
        // Arrange
        ConfirmationDispatcher sut = BuildSut();

        // Act
        Func<Task> act = async () => await sut.DispatchAsync(
            "   ", "ACCOUNT-001", DateTime.UtcNow);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*cellPhoneNumber*");
    }

    [Fact]
    public async Task Should_ThrowArgumentException_When_AccountIdIsWhiteSpace()
    {
        // Arrange
        ConfirmationDispatcher sut = BuildSut();

        // Act
        Func<Task> act = async () => await sut.DispatchAsync(
            "+12025551234", "   ", DateTime.UtcNow);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*coolTextAccountId*");
    }

    // -----------------------------------------------------------------------
    // Cancellation propagation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Should_PropagateOperationCanceledException_When_CancellationIsRequested()
    {
        // Arrange — CoolText blocks until cancellation
        using var cts = new CancellationTokenSource();

        _coolTextClientMock
            .Setup(c => c.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new OperationCanceledException());

        ConfirmationDispatcher sut = BuildSut();

        // Act
        Func<Task> act = async () => await sut.DispatchAsync(
            "+12025551234", "ACCOUNT-001", DateTime.UtcNow, cts.Token);

        // Assert — cancellation must propagate, not be swallowed
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
