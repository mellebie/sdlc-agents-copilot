// Tests: OutboundSendService — success, retry-then-success, all-retries-fail, network-exception-exhausted
// Source: Task 4 | SPEC-007 (Outbound Delivery) | AuditEventType.OutboundDelivered / OutboundFailed

using System.Text.Json;
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
using TCPA.OutboundDispatcher.Messaging;
using TCPA.OutboundDispatcher.Services;

namespace TCPA.OutboundDispatcher.Tests.Services;

public class OutboundSendServiceTests
{
    private readonly TcpaDbContext _ctx;
    private readonly ICoolTextApiClient _coolTextClient;
    private readonly IAuditLogRepository _auditRepo;
    private readonly IPhoneNumberHasher _hasher;
    private readonly List<AuditLog> _auditLog = new();

    // Zero-duration retry delays for tests — exercises retry logic without real waits
    private static readonly TimeSpan[] ZeroDelays =
        [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero];

    public OutboundSendServiceTests()
    {
        var options = new DbContextOptionsBuilder<TcpaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _ctx = new TcpaDbContext(options);
        _coolTextClient = Substitute.For<ICoolTextApiClient>();
        _auditRepo = Substitute.For<IAuditLogRepository>();
        _hasher = Substitute.For<IPhoneNumberHasher>();
        _hasher.Hash(Arg.Any<string>()).Returns(args => "hashed:" + args[0]);

        // Capture all Write() calls so assertions can inspect what was staged
        _auditRepo
            .When(r => r.Write(Arg.Any<AuditLog>()))
            .Do(call => _auditLog.Add(call.Arg<AuditLog>()));
    }

    private OutboundSendService BuildService() =>
        new OutboundSendService(
            _ctx,
            _coolTextClient,
            _auditRepo,
            _hasher,
            Substitute.For<ILogger<OutboundSendService>>());

    private static OutboundMessageEvent MakeEvent() =>
        new OutboundMessageEvent(
            MessageId: "msg-send-test-001",
            ToNumber: "+12025551234",
            Body: "Outbound SMS body",
            CoolTextAccountNumber: "CT-ACCT-001",
            ApplicationId: "app-test",
            CorrelationId: null,
            QueuedAt: DateTimeOffset.UtcNow);

    [Fact]
    public async Task SendAsync_WhenCoolTextSucceeds_WritesOutboundDeliveredAudit()
    {
        // Arrange
        var @event = MakeEvent();
        _coolTextClient
            .SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CoolTextSendResult(true, "provider-msg-id-123", null));
        var sut = BuildService();

        // Act
        await sut.SendAsync_WithDelays(@event, ZeroDelays, CancellationToken.None);

        // Assert: exactly one Cool Text call, one OutboundDelivered audit
        await _coolTextClient.Received(1).SendSmsAsync(
            @event.ToNumber, @event.CoolTextAccountNumber, @event.Body, Arg.Any<CancellationToken>());

        _auditLog.Should().HaveCount(1);
        var audit = _auditLog[0];
        audit.EventType.Should().Be(AuditEventType.OutboundDelivered);
        // AuditLog.PhoneNumber stores raw E.164, not the hash.
        audit.PhoneNumber.Should().Be(@event.ToNumber);
        audit.MessageId.Should().Be(@event.MessageId);
        audit.ApplicationId.Should().Be(@event.ApplicationId);

        var details = JsonSerializer.Deserialize<JsonElement>(audit.Details!);
        details.GetProperty("providerMessageId").GetString().Should().Be("provider-msg-id-123");
    }

    [Fact]
    public async Task SendAsync_WhenCoolTextFailsTwiceThenSucceeds_WritesOutboundDeliveredAfterRetry()
    {
        // Arrange: fail twice, succeed on third call (attempt index 2)
        var @event = MakeEvent();
        _coolTextClient
            .SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                new CoolTextSendResult(false, null, "HTTP 503: Service Unavailable"),
                new CoolTextSendResult(false, null, "HTTP 503: Service Unavailable"),
                new CoolTextSendResult(true, "provider-msg-retry-success", null));
        var sut = BuildService();

        // Act
        await sut.SendAsync_WithDelays(@event, ZeroDelays, CancellationToken.None);

        // Assert: 3 calls total, OutboundDelivered written
        await _coolTextClient.Received(3).SendSmsAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        _auditLog.Should().HaveCount(1);
        _auditLog[0].EventType.Should().Be(AuditEventType.OutboundDelivered);
    }

    [Fact]
    public async Task SendAsync_WhenAllRetriesExhaustedWithFailureResult_WritesOutboundFailedAudit()
    {
        // Arrange: 4 consecutive failures (initial + 3 retries)
        var @event = MakeEvent();
        _coolTextClient
            .SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CoolTextSendResult(false, null, "HTTP 500: Internal Server Error"));
        var sut = BuildService();

        // Act
        await sut.SendAsync_WithDelays(@event, ZeroDelays, CancellationToken.None);

        // Assert: 4 total attempts (initial + 3 retry delays), OutboundFailed written
        await _coolTextClient.Received(4).SendSmsAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        _auditLog.Should().HaveCount(1);
        var audit = _auditLog[0];
        audit.EventType.Should().Be(AuditEventType.OutboundFailed);
        // AuditLog.PhoneNumber stores raw E.164, not the hash.
        audit.PhoneNumber.Should().Be(@event.ToNumber);
        audit.MessageId.Should().Be(@event.MessageId);

        var details = JsonSerializer.Deserialize<JsonElement>(audit.Details!);
        details.GetProperty("retriesAttempted").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task SendAsync_WhenCoolTextThrowsOnAllAttempts_WritesOutboundFailedAudit()
    {
        // Arrange: HttpRequestException on every call
        var @event = MakeEvent();
        _coolTextClient
            .SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));
        var sut = BuildService();

        // Act — should NOT throw even though Cool Text throws
        Func<Task> act = () => sut.SendAsync_WithDelays(@event, ZeroDelays, CancellationToken.None);
        await act.Should().NotThrowAsync();

        // Assert: OutboundFailed written
        _auditLog.Should().HaveCount(1);
        _auditLog[0].EventType.Should().Be(AuditEventType.OutboundFailed);
    }

    [Fact]
    public async Task SendAsync_WhenCancelled_ThrowsOperationCancelledException()
    {
        // Arrange
        var @event = MakeEvent();
        using var cts = new CancellationTokenSource();
        _coolTextClient
            .SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        var sut = BuildService();
        cts.Cancel();

        // Act & Assert — OperationCancelledException propagates (graceful shutdown path)
        Func<Task> act = () => sut.SendAsync_WithDelays(@event, ZeroDelays, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
