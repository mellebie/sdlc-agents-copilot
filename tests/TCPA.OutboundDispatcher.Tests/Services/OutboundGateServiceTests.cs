// Tests: OutboundGateService — opt-out suppression, quiet hours suppression, allowed path
// Source: Task 3 | SPEC-006 (Outbound Gate) | AuditEventType.OutboundSuppressed

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using FluentAssertions;
using Xunit;
using TCPA.Core.Data;
using TCPA.Core.Interfaces;
using TCPA.Core.Models;
using TCPA.Core.Services;
using TCPA.OutboundDispatcher.Messaging;
using TCPA.OutboundDispatcher.Services;

namespace TCPA.OutboundDispatcher.Tests.Services;

public class OutboundGateServiceTests
{
    private readonly TcpaDbContext _ctx;
    private readonly IOptOutStatusRepository _statusRepo;
    private readonly IAuditLogRepository _auditRepo;
    private readonly IPhoneNumberHasher _hasher;

    public OutboundGateServiceTests()
    {
        var options = new DbContextOptionsBuilder<TcpaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _ctx = new TcpaDbContext(options);
        _statusRepo = Substitute.For<IOptOutStatusRepository>();
        _auditRepo = Substitute.For<IAuditLogRepository>();
        _hasher = Substitute.For<IPhoneNumberHasher>();
        _hasher.Hash(Arg.Any<string>()).Returns(args => "hashed:" + args[0]);
    }

    private OutboundGateService BuildService() =>
        new OutboundGateService(
            _ctx,
            _statusRepo,
            _auditRepo,
            _hasher,
            Substitute.For<ILogger<OutboundGateService>>());

    private static OutboundMessageEvent MakeEvent(string toNumber = "+12025551234") =>
        new OutboundMessageEvent(
            MessageId: "msg-gate-test-001",
            ToNumber: toNumber,
            Body: "Hello from the dispatcher",
            CoolTextAccountNumber: "CT-ACCT-001",
            ApplicationId: "app-test",
            CorrelationId: null,
            QueuedAt: DateTimeOffset.UtcNow);

    // A time that is within TCPA hours: 2 PM UTC = 14:00
    private static DateTimeOffset WithinHours() =>
        new DateTimeOffset(2026, 7, 23, 14, 0, 0, TimeSpan.Zero);

    // A time that is outside TCPA hours: 6 AM UTC = 06:00
    private static DateTimeOffset OutsideHours() =>
        new DateTimeOffset(2026, 7, 23, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EvaluateAsync_WhenOptedOut_ReturnsSuppressedAndWritesAudit()
    {
        // Arrange
        var @event = MakeEvent();
        _statusRepo.IsOptedOutAsync(@event.ToNumber, Arg.Any<CancellationToken>()).Returns(true);
        var sut = BuildService();

        // Act
        var result = await sut.EvaluateAsync_WithClock(@event, WithinHours(), CancellationToken.None);

        // Assert
        result.IsAllowed.Should().BeFalse();
        result.SuppressReason.Should().Be("opt_out");

        // Audit entry staged and saved
        _auditRepo.Received(1).Write(Arg.Is<AuditLog>(a =>
            a.EventType == AuditEventType.OutboundSuppressed &&
            a.PhoneNumber == _hasher.Hash(@event.ToNumber) &&
            a.MessageId == @event.MessageId));
    }

    [Fact]
    public async Task EvaluateAsync_WhenOptedOut_DoesNotCheckQuietHours()
    {
        // Arrange: opted out AND outside hours — should short-circuit on opt-out
        var @event = MakeEvent();
        _statusRepo.IsOptedOutAsync(@event.ToNumber, Arg.Any<CancellationToken>()).Returns(true);
        var sut = BuildService();

        // Act — note: outside hours, but opt-out takes precedence
        var result = await sut.EvaluateAsync_WithClock(@event, OutsideHours(), CancellationToken.None);

        // Assert — reason is opt_out, not quiet_hours
        result.SuppressReason.Should().Be("opt_out");
    }

    [Fact]
    public async Task EvaluateAsync_WhenNotOptedOutAndOutsideQuietHours_ReturnsSuppressedWithQuietHoursReason()
    {
        // Arrange
        var @event = MakeEvent();
        _statusRepo.IsOptedOutAsync(@event.ToNumber, Arg.Any<CancellationToken>()).Returns(false);
        var sut = BuildService();

        // Act: 6 AM UTC is before 8 AM — outside allowed window
        var result = await sut.EvaluateAsync_WithClock(@event, OutsideHours(), CancellationToken.None);

        // Assert
        result.IsAllowed.Should().BeFalse();
        result.SuppressReason.Should().Be("quiet_hours");
        _auditRepo.Received(1).Write(Arg.Is<AuditLog>(a =>
            a.EventType == AuditEventType.OutboundSuppressed &&
            a.MessageId == @event.MessageId));
    }

    [Fact]
    public async Task EvaluateAsync_WhenNotOptedOutAndWithinHours_ReturnsAllowed()
    {
        // Arrange
        var @event = MakeEvent();
        _statusRepo.IsOptedOutAsync(@event.ToNumber, Arg.Any<CancellationToken>()).Returns(false);
        var sut = BuildService();

        // Act: 2 PM UTC is within [8, 21) — allowed
        var result = await sut.EvaluateAsync_WithClock(@event, WithinHours(), CancellationToken.None);

        // Assert
        result.IsAllowed.Should().BeTrue();
        result.SuppressReason.Should().BeNull();
        _auditRepo.DidNotReceive().Write(Arg.Any<AuditLog>());
    }

    [Theory]
    [InlineData(8, true)]    // 8 AM UTC — exactly on boundary, allowed
    [InlineData(20, true)]   // 8 PM UTC — last allowed hour
    [InlineData(21, false)]  // 9 PM UTC — first disallowed hour
    [InlineData(7, false)]   // 7 AM UTC — before window
    [InlineData(0, false)]   // Midnight UTC — outside
    public async Task EvaluateAsync_QuietHoursBoundaries_AreCorrect(int utcHour, bool expectAllowed)
    {
        // Arrange
        var @event = MakeEvent();
        _statusRepo.IsOptedOutAsync(@event.ToNumber, Arg.Any<CancellationToken>()).Returns(false);
        var sut = BuildService();
        var clockAt = new DateTimeOffset(2026, 7, 23, utcHour, 0, 0, TimeSpan.Zero);

        // Act
        var result = await sut.EvaluateAsync_WithClock(@event, clockAt, CancellationToken.None);

        // Assert
        result.IsAllowed.Should().Be(expectAllowed,
            because: $"UTC hour {utcHour} should be {(expectAllowed ? "allowed" : "suppressed")}");
    }
}
