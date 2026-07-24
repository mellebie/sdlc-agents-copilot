// Tests: ConfirmationDispatchService — config guard, retry loop, SLA breach, audit writes
// Source: Task 4 | TCPA.MessageProcessor inbound plan
// Covers: BR-012 (confirmation dispatched with retry), BR-013 (SLA breach detection),
//         BR-014 (config guard — ConfirmationFailed when OptOutMessageBody missing)

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using FluentAssertions;
using Xunit;
using TCPA.Core.Data;
using TCPA.Core.Interfaces;
using TCPA.Core.Models;
using TCPA.Core.Services;
using TCPA.MessageProcessor.Services;

namespace TCPA.MessageProcessor.Tests.Services;

public class ConfirmationDispatchServiceTests : IDisposable
{
    private readonly TcpaDbContext _ctx;
    private readonly ISystemConfigRepository _configRepo;
    private readonly ICoolTextApiClient _coolTextClient;
    private readonly IAuditLogRepository _auditRepo;
    private readonly IPhoneNumberHasher _hasher;
    private readonly ConfirmationDispatchService _sut;

    public ConfirmationDispatchServiceTests()
    {
        var options = new DbContextOptionsBuilder<TcpaDbContext>()
            .UseInMemoryDatabase($"ConfirmTest_{Guid.NewGuid()}")
            .Options;
        _ctx = new TcpaDbContext(options);

        // NSubstitute the audit repo — SqlAuditLogRepository requires two keyed-service TcpaDbContext
        // ctor params and cannot be instantiated directly in tests. Use a mock with a side-effect so
        // Write() stages the entry on _ctx, allowing SaveChangesAsync to assign the generated Id.
        _auditRepo = Substitute.For<IAuditLogRepository>();
        _auditRepo.When(r => r.Write(Arg.Any<AuditLog>()))
                  .Do(call => _ctx.AuditLogs.Add(call.Arg<AuditLog>()));

        _configRepo = Substitute.For<ISystemConfigRepository>();
        _coolTextClient = Substitute.For<ICoolTextApiClient>();
        _hasher = Substitute.For<IPhoneNumberHasher>();
        _hasher.Hash(Arg.Any<string>()).Returns(args => "hashed:" + args[0]);

        _sut = new ConfirmationDispatchService(
            _ctx, _configRepo, _coolTextClient, _auditRepo, _hasher,
            Substitute.For<ILogger<ConfirmationDispatchService>>());
    }

    public void Dispose() => _ctx.Dispose();

    private void SetupMessageBody(string body = "You have been unsubscribed.")
        => _configRepo.GetRequiredValueAsync("OptOutMessageBody", Arg.Any<CancellationToken>())
            .Returns(body);

    private void SetupCoolTextSuccess(string msgId = "cool-msg-123")
        => _coolTextClient.SendSmsAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CoolTextSendResult(true, msgId, null));

    [Fact]
    public async Task DispatchConfirmationAsync_OnFirstAttemptSuccess_WritesConfirmationDispatchedAudit()
    {
        // Arrange
        SetupMessageBody();
        SetupCoolTextSuccess();
        var receivedAt = DateTimeOffset.UtcNow.AddSeconds(-5);

        // Act
        await _sut.DispatchConfirmationAsync("+12025551234", "CT-001", receivedAt, 42L, CancellationToken.None);

        // Assert
        var audit = await _ctx.AuditLogs.SingleAsync();
        audit.EventType.Should().Be(AuditEventType.ConfirmationDispatched);
        audit.PhoneNumber.Should().Be("hashed:+12025551234");
        audit.Details.Should().Contain("cool-msg-123");
    }

    [Fact]
    public async Task DispatchConfirmationAsync_WhenLatencyExceeds60s_WritesSlaBreach()
    {
        // Arrange
        SetupMessageBody();
        SetupCoolTextSuccess();
        var receivedAt = DateTimeOffset.UtcNow.AddSeconds(-90); // 90s ago — SLA breach

        // Act
        await _sut.DispatchConfirmationAsync("+12025551234", "CT-001", receivedAt, 42L, CancellationToken.None);

        // Assert — both ConfirmationDispatched AND SlaBreach entries written
        var audits = await _ctx.AuditLogs.ToListAsync();
        audits.Should().HaveCount(2);
        audits.Should().ContainSingle(a => a.EventType == AuditEventType.ConfirmationDispatched);
        audits.Should().ContainSingle(a => a.EventType == AuditEventType.SlaBreach);
    }

    [Fact]
    public async Task DispatchConfirmationAsync_WhenAllRetriesExhausted_WritesConfirmationFailed()
    {
        // Arrange
        SetupMessageBody();
        _coolTextClient.SendSmsAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act — use zero-delay overload so tests don't actually wait 14s
        await _sut.DispatchConfirmationAsync_WithDelays(
            "+12025551234", "CT-001", DateTimeOffset.UtcNow, 42L,
            [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero],
            CancellationToken.None);

        // Assert
        var audit = await _ctx.AuditLogs.SingleAsync();
        audit.EventType.Should().Be(AuditEventType.ConfirmationFailed);
    }

    [Fact]
    public async Task DispatchConfirmationAsync_WhenCoolTextReturnsFailure_RetriesAndEventuallyFails()
    {
        // Arrange
        SetupMessageBody();
        _coolTextClient.SendSmsAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CoolTextSendResult(false, null, "API error"));

        // Act
        await _sut.DispatchConfirmationAsync_WithDelays(
            "+12025551234", "CT-001", DateTimeOffset.UtcNow, 42L,
            [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero],
            CancellationToken.None);

        // Assert: called 4 times (initial + 3 retries), single ConfirmationFailed audit
        await _coolTextClient.Received(4).SendSmsAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        var audit = await _ctx.AuditLogs.SingleAsync();
        audit.EventType.Should().Be(AuditEventType.ConfirmationFailed);
    }

    [Fact]
    public async Task DispatchConfirmationAsync_WhenOptOutMessageBodyMissing_WritesConfirmationFailed()
    {
        // Arrange
        _configRepo.GetRequiredValueAsync("OptOutMessageBody", Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Configuration key 'OptOutMessageBody' is missing or empty."));

        // Act
        await _sut.DispatchConfirmationAsync("+12025551234", "CT-001", DateTimeOffset.UtcNow, 42L, CancellationToken.None);

        // Assert — ConfirmationFailed written, Cool Text never called
        var audit = await _ctx.AuditLogs.SingleAsync();
        audit.EventType.Should().Be(AuditEventType.ConfirmationFailed);
        await _coolTextClient.DidNotReceive().SendSmsAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchConfirmationAsync_WhenSendSucceedsOnSecondAttempt_WritesConfirmationDispatched()
    {
        // Arrange
        SetupMessageBody();
        _coolTextClient.SendSmsAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => new CoolTextSendResult(false, null, "transient error"),
                _ => new CoolTextSendResult(true, "msg-on-retry", null));

        // Act
        await _sut.DispatchConfirmationAsync_WithDelays(
            "+12025551234", "CT-001", DateTimeOffset.UtcNow, 42L,
            [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero],
            CancellationToken.None);

        // Assert: called exactly twice (fails on attempt 0, succeeds on attempt 1)
        await _coolTextClient.Received(2).SendSmsAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        var audit = await _ctx.AuditLogs.SingleAsync();
        audit.EventType.Should().Be(AuditEventType.ConfirmationDispatched);
    }
}
