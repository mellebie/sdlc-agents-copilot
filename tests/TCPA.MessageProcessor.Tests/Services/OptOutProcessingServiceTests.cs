// Tests: OptOutProcessingService — atomic opt-out write with duplicate detection
// Source: Task 3 | TCPA.MessageProcessor inbound plan
// Covers: BR-009 (opt-out written before confirmation), BR-010 (audit + status atomic)

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using FluentAssertions;
using Xunit;
using TCPA.Core.Data;
using TCPA.Core.Interfaces;
using TCPA.Core.Models;
using TCPA.Core.Services;
using TCPA.MessageProcessor.Messaging;
using TCPA.MessageProcessor.Services;

namespace TCPA.MessageProcessor.Tests.Services;

public class OptOutProcessingServiceTests : IDisposable
{
    private readonly TcpaDbContext _ctx;
    private readonly IOptOutStatusRepository _statusRepo;
    private readonly IAuditLogRepository _auditRepo;
    private readonly IPhoneNumberHasher _hasher;
    private readonly OptOutProcessingService _sut;

    public OptOutProcessingServiceTests()
    {
        var options = new DbContextOptionsBuilder<TcpaDbContext>()
            .UseInMemoryDatabase($"OptOutTest_{Guid.NewGuid()}")
            .Options;
        _ctx = new TcpaDbContext(options);

        // NSubstitute the audit repo (SqlAuditLogRepository requires two keyed-service ctor params).
        // Add a side-effect so Write() actually stages the entry on the DbContext — this allows
        // SaveChangesAsync to assign the generated Id, making AuditRecordId assertions valid.
        _auditRepo = Substitute.For<IAuditLogRepository>();
        _auditRepo.When(r => r.Write(Arg.Any<AuditLog>()))
                  .Do(call => _ctx.AuditLogs.Add(call.Arg<AuditLog>()));

        _statusRepo = Substitute.For<IOptOutStatusRepository>();

        _hasher = Substitute.For<IPhoneNumberHasher>();
        _hasher.Hash(Arg.Any<string>()).Returns(args => "hashed:" + args[0]);

        _sut = new OptOutProcessingService(
            _ctx,
            _auditRepo,
            _statusRepo,
            _hasher,
            Substitute.For<ILogger<OptOutProcessingService>>());
    }

    public void Dispose() => _ctx.Dispose();

    private static InboundMessageEvent MakeEvent(string phone = "+12025551234") =>
        new("internal-1", "msg-1", phone, "CT-001", "STOP", "CoolText",
            "CT-001", "app1", DateTimeOffset.UtcNow);

    [Fact]
    public async Task ProcessOptOutAsync_FirstTimeOptOut_WritesOptOutWrittenAuditAndReturnsIsNewTrue()
    {
        // Arrange
        _statusRepo.IsOptedOutAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var result = await _sut.ProcessOptOutAsync(MakeEvent(), CancellationToken.None);

        // Assert
        result.IsNew.Should().BeTrue();
        result.AuditRecordId.Should().BeGreaterThan(0);

        var audit = await _ctx.AuditLogs.SingleAsync();
        audit.EventType.Should().Be(AuditEventType.OptOutWritten);
        // AuditLog.PhoneNumber stores raw E.164 (nvarchar(20)) — not the hash.
        // Hash is only used in Serilog log parameters and AuditLog.Details JSON.
        audit.PhoneNumber.Should().Be("+12025551234");
        audit.MessageId.Should().Be("msg-1");
        audit.AnomalyFlag.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessOptOutAsync_DuplicateOptOut_WritesOptOutDuplicateAuditAndReturnsIsNewFalse()
    {
        // Arrange
        _statusRepo.IsOptedOutAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var result = await _sut.ProcessOptOutAsync(MakeEvent(), CancellationToken.None);

        // Assert
        result.IsNew.Should().BeFalse();
        result.AuditRecordId.Should().BeGreaterThan(0);

        var audit = await _ctx.AuditLogs.SingleAsync();
        audit.EventType.Should().Be(AuditEventType.OptOutDuplicate);
        audit.AnomalyFlag.Should().BeFalse();

        // UpsertOptOutAsync must NOT be called for a duplicate
        await _statusRepo.DidNotReceive().UpsertOptOutAsync(
            Arg.Any<string>(), Arg.Any<long>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessOptOutAsync_WhenStatusRepoUpsertThrows_PropagatesException()
    {
        // Arrange
        _statusRepo.IsOptedOutAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _statusRepo.UpsertOptOutAsync(
            Arg.Any<string>(), Arg.Any<long>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => Task.FromException(new Exception("DB failure")));

        // Act
        var act = () => _sut.ProcessOptOutAsync(MakeEvent(), CancellationToken.None);

        // Assert — exception propagates cleanly
        await act.Should().ThrowAsync<Exception>().WithMessage("DB failure");
        // Note: InMemory does not enforce real transaction rollback. In a relational scenario both
        // the audit write and the status upsert would roll back atomically via BeginTransactionAsync.
    }

    [Fact]
    public async Task ProcessOptOutAsync_CallsUpsertWithCorrectAuditRecordId()
    {
        // Arrange
        _statusRepo.IsOptedOutAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        long capturedAuditId = 0;
        await _statusRepo.UpsertOptOutAsync(
            Arg.Any<string>(),
            Arg.Do<long>(id => capturedAuditId = id),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());

        // Act
        var result = await _sut.ProcessOptOutAsync(MakeEvent(), CancellationToken.None);

        // Assert — the auditRecordId passed to UpsertOptOutAsync matches the returned record Id
        capturedAuditId.Should().Be(result.AuditRecordId);
        capturedAuditId.Should().BeGreaterThan(0);
    }
}
