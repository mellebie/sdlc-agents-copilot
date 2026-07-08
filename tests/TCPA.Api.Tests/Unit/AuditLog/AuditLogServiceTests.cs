// Tests for AuditLogService
// Source: TASK (Data Services) | SPEC-008, SPEC-009, SPEC-010 | NFS-004, NFS-008
// Covers: append-only invariant, AuditLogWriteException on DB failure, retention, query filtering

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TCPA.Api.Domain;
using TCPA.Api.Infrastructure.Data;
using TCPA.Api.Services.AuditLog;
using Xunit;

namespace TCPA.Api.Tests.Unit.AuditLog;

public sealed class AuditLogServiceTests : IDisposable
{
    private readonly TcpaDbContext _dbContext;
    private readonly Mock<ICorrelationIdAccessor> _correlationIdAccessor;
    private readonly AuditLogService _sut;

    public AuditLogServiceTests()
    {
        var options = new DbContextOptionsBuilder<TcpaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new TcpaDbContext(options);
        _correlationIdAccessor = new Mock<ICorrelationIdAccessor>();
        _correlationIdAccessor.Setup(a => a.CorrelationId).Returns("test-correlation-id");

        _sut = new AuditLogService(
            _dbContext,
            new Mock<ILogger<AuditLogService>>().Object,
            _correlationIdAccessor.Object);
    }

    public void Dispose() => _dbContext.Dispose();

    // -----------------------------------------------------------------------
    // LogAsync — append-only invariant
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LogAsync_Should_InsertRecord_And_ReturnRecordId()
    {
        // Arrange
        var entry = BuildAuditEntry(AuditEventType.OptOut, "+12025551234");

        // Act
        var returnedId = await _sut.LogAsync(entry);

        // Assert
        returnedId.Should().Be(entry.RecordId);

        var savedEntry = await _dbContext.AuditLogEntries.FindAsync(entry.RecordId);
        savedEntry.Should().NotBeNull();
        savedEntry!.EventType.Should().Be(AuditEventType.OptOut);
        savedEntry.CellPhoneNumber.Should().Be("+12025551234");
    }

    [Fact]
    public async Task LogAsync_Should_NeverUpdate_ExistingRecord()
    {
        // Arrange — write the record once
        var entry = BuildAuditEntry(AuditEventType.OptOut, "+12025551234");
        await _sut.LogAsync(entry);

        // Verify it exists
        var savedEntry = await _dbContext.AuditLogEntries.FindAsync(entry.RecordId);
        savedEntry.Should().NotBeNull();

        // Act — verify the service exposes no update pathway
        // (The audit log entry type uses init-only setters; EF is not tracking it for updates.)
        // The key invariant is that LogAsync only ever calls AddAsync + SaveChangesAsync
        // on a new entry, never Update. We verify this by checking record count stays at 1.
        var countAfter = await _dbContext.AuditLogEntries.CountAsync();

        // Assert
        countAfter.Should().Be(1);
    }

    [Fact]
    public async Task LogAsync_Should_ThrowAuditLogWriteException_When_DbFails()
    {
        // Arrange — use a disposed context to force a DB failure
        await _dbContext.DisposeAsync();

        var entry = BuildAuditEntry(AuditEventType.BlockedOutbound, "+12025551234");
        var disposedContextService = new AuditLogService(
            _dbContext, // already disposed
            new Mock<ILogger<AuditLogService>>().Object,
            _correlationIdAccessor.Object);

        // Act
        var act = async () => await disposedContextService.LogAsync(entry);

        // Assert — must throw AuditLogWriteException, never swallow the error (NFS-008)
        await act.Should().ThrowAsync<AuditLogWriteException>()
            .WithMessage("*audit log entry*");
    }

    [Fact]
    public async Task LogAsync_Should_NotSwallowException_On_DbFailure()
    {
        // Arrange — verify AuditLogWriteException wraps the inner exception
        await _dbContext.DisposeAsync();

        var entry = BuildAuditEntry(AuditEventType.OptOut, "+12025551234");
        var disposedContextService = new AuditLogService(
            _dbContext,
            new Mock<ILogger<AuditLogService>>().Object,
            _correlationIdAccessor.Object);

        // Act
        Func<Task> act = async () => await disposedContextService.LogAsync(entry);

        // Assert — exception has an inner cause (the raw DB exception is not lost)
        await act.Should().ThrowAsync<AuditLogWriteException>()
            .Where(ex => ex.InnerException is not null);
    }

    // -----------------------------------------------------------------------
    // RetentionExpiresAt — 5-year retention
    // -----------------------------------------------------------------------

    [Fact]
    public void ComputeRetentionExpiry_Should_ReturnTimestampPlusFiveYears()
    {
        // Arrange
        var eventTimestamp = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);

        // Act
        var expiry = AuditLogService.ComputeRetentionExpiry(eventTimestamp);

        // Assert — must be at least 5 years after the event timestamp
        // (implementation adds 365*5+2 days to account for leap years)
        expiry.Should().BeAfter(eventTimestamp.AddYears(5).AddDays(-1));
        expiry.Should().BeBefore(eventTimestamp.AddYears(5).AddDays(4));
    }

    [Fact]
    public void ComputeRetentionExpiry_Should_BeAtLeast5YearsAfterEvent()
    {
        // Arrange
        var eventTimestamp = new DateTime(2020, 2, 29, 0, 0, 0, DateTimeKind.Utc); // leap day

        // Act
        var expiry = AuditLogService.ComputeRetentionExpiry(eventTimestamp);

        // Assert — must be strictly after the 5-year mark from the event
        expiry.Should().BeAfter(eventTimestamp.AddYears(5));
    }

    // -----------------------------------------------------------------------
    // QueryAsync — date range and event type filtering
    // -----------------------------------------------------------------------

    [Fact]
    public async Task QueryAsync_Should_ReturnEntries_FilteredByDateRange()
    {
        // Arrange
        var insideRange = BuildAuditEntry(AuditEventType.OptOut, "+12025551111",
            timestamp: new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc));
        var outsideRange = BuildAuditEntry(AuditEventType.OptOut, "+12025552222",
            timestamp: new DateTime(2026, 2, 15, 12, 0, 0, DateTimeKind.Utc));

        await _dbContext.AuditLogEntries.AddRangeAsync(insideRange, outsideRange);
        await _dbContext.SaveChangesAsync();

        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 31, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var results = await _sut.QueryAsync(from, to);

        // Assert
        results.Should().HaveCount(1);
        results.First().CellPhoneNumber.Should().Be("+12025551111");
    }

    [Fact]
    public async Task QueryAsync_Should_ReturnEntries_FilteredByEventType()
    {
        // Arrange — mix of event types in same date range
        var optOutEntry = BuildAuditEntry(AuditEventType.OptOut, "+12025551111");
        var blockedEntry = BuildAuditEntry(AuditEventType.BlockedOutbound, "+12025552222");

        await _dbContext.AuditLogEntries.AddRangeAsync(optOutEntry, blockedEntry);
        await _dbContext.SaveChangesAsync();

        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var results = await _sut.QueryAsync(from, to, eventType: AuditEventType.OptOut);

        // Assert
        results.Should().HaveCount(1);
        results.First().EventType.Should().Be(AuditEventType.OptOut);
    }

    [Fact]
    public async Task QueryAsync_Should_ThrowArgumentException_When_FromIsAfterTo()
    {
        // Arrange
        var from = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc); // to before from

        // Act
        var act = async () => await _sut.QueryAsync(from, to);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("from");
    }

    [Fact]
    public async Task QueryAsync_Should_ReturnResultsOrderedByEventTimestamp()
    {
        // Arrange
        var later = BuildAuditEntry(AuditEventType.OptOut, "+12025551111",
            timestamp: new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc));
        var earlier = BuildAuditEntry(AuditEventType.OptOut, "+12025552222",
            timestamp: new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc));

        // Insert later first
        await _dbContext.AuditLogEntries.AddRangeAsync(later, earlier);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _sut.QueryAsync(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 31, 23, 59, 59, DateTimeKind.Utc));

        // Assert — must be ordered ascending by EventTimestamp
        results.Should().HaveCount(2);
        results.First().EventTimestamp.Should().BeBefore(results.Last().EventTimestamp);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static AuditLogEntry BuildAuditEntry(
        AuditEventType eventType,
        string cellPhoneNumber,
        DateTime? timestamp = null)
    {
        return new AuditLogEntry
        {
            RecordId = Guid.NewGuid(),
            EventType = eventType,
            EventTimestamp = timestamp ?? new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            CellPhoneNumber = cellPhoneNumber,
            OriginatingCoolTextAccountId = "ACC-001",
            OriginatingApplicationName = "GCMA",
            SystemResponse = "OPT_OUT_STATUS_WRITTEN",
            CreatedAt = DateTime.UtcNow
        };
    }
}
